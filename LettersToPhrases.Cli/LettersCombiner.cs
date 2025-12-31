#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NHunspell;

namespace LettersToPhrases.Cli
{
    public static partial class LettersCombiner
    {
        // Internal cancellation for "press Q"
        private static readonly CancellationTokenSource InternalCts = new();

        private static readonly object ConsoleLock = new object();

        public enum Languages
        {
            German,      // Combines all German variants
            German_AT,   // Austrian German
            German_CH,   // Swiss German
            English,     // Combines US + GB
            English_US,  // US English
            English_GB,  // British English
            French,
            Turkish,
            Spanish,
            Italian
        }

        // Hunspell dictionary pairs expected in: AppContext.BaseDirectory\hunspell\
        private static readonly Dictionary<Languages, (string Aff, string Dic)[]> HunspellFiles = new()
        {
			// German variants
			{ Languages.German,    new[] { ("de_DE.aff","de_DE.dic"), ("de_AT.aff","de_AT.dic"), ("de_CH.aff","de_CH.dic") } },
            { Languages.German_AT, new[] { ("de_AT.aff","de_AT.dic") } },
            { Languages.German_CH, new[] { ("de_CH.aff","de_CH.dic") } },

			// English variants
			{ Languages.English,    new[] { ("en_US.aff","en_US.dic"), ("en_GB.aff","en_GB.dic") } },
            { Languages.English_US, new[] { ("en_US.aff","en_US.dic") } },
            { Languages.English_GB, new[] { ("en_GB.aff","en_GB.dic") } },

			// Others (depending on your repo)
			{ Languages.French,   new[] { ("fr.aff","fr.dic"), ("fr_FR.aff","fr_FR.dic") } },
            { Languages.Turkish,  new[] { ("tr_TR.aff","tr_TR.dic") } },
            { Languages.Spanish,  new[] { ("es_ES.aff","es_ES.dic") } },
            { Languages.Italian,  new[] { ("it_IT.aff","it_IT.dic") } },
        };

        private readonly record struct ChainScore(int Pieces, int MaxChain, int TotalChained);

        /// <summary>
        /// Main worker:
        /// - scans Hunspell dictionaries, collects valid words fitting input letter pool
        /// - builds phrases (when reuse=false)
        /// - ranks best-first: fewer cuts, longer consecutive substrings, etc.
        /// - supports Q-cancel & external cancellation token
        /// </summary>
        public static async Task<string[]> CombineLettersAsync(
            string letters,
            IEnumerable<Languages>? languages = null,
            bool caseSensitive = false,
            bool reuse = false,
            int minWordLength = 2,
            bool indicateConsecutiveSubstrings = true,
            bool realTimeOutput = false,
            bool cmdLogProgress = true,
            int maxWorkers = 0,
            bool createResultFile = false,
            bool tryUseAll = false,
            bool filterPermutations = true,
            bool addEnumeration = false,
            IProgress<double>? progress = null,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(letters))
                return Array.Empty<string>();

            var langList = (languages?.ToList() ?? new List<Languages>());
            if (langList.Count == 0)
            {
                var sys = TryGetSystemLanguage();
                langList.Add(sys ?? Languages.English);
            }

            int workers = NormalizeWorkers(maxWorkers);

            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, InternalCts.Token);
            var token = linked.Token;

            using var keyWatcher = StartCancelOnQ(linked);

            var sw = Stopwatch.StartNew();
            long checkedLines = 0;
            long acceptedWords = 0;
            long acceptedPhrases = 0;

            using var progressTicker = (cmdLogProgress && !realTimeOutput)
                ? StartCmdProgressTicker(
                    getChecked: () => Volatile.Read(ref checkedLines),
                    getElapsed: () => sw.Elapsed,
                    getWords: () => Volatile.Read(ref acceptedWords),
                    getPhrases: () => Volatile.Read(ref acceptedPhrases),
                    linked: linked)
                : null;

            // normalize input letters (keep letters only)
            var normalizedLetters = NormalizeLetters(letters, caseSensitive);
            if (normalizedLetters.Length == 0)
                return Array.Empty<string>();

            if (minWordLength < 1) minWordLength = 1;

            var letterCounts = CountLetters(normalizedLetters);
            var allowedLetters = new HashSet<char>(letterCounts.Keys);
            int maxLen = normalizedLetters.Length;

            // substring hinting
            List<string>? originalSegments = indicateConsecutiveSubstrings ? GetOriginalSegments(letters, caseSensitive) : null;
            HashSet<string>? substringSet = indicateConsecutiveSubstrings && originalSegments != null
                ? BuildSubstringSet(originalSegments, caseSensitive, maxSubLen: 64)
                : null;

            // Load hunspell engines
            var hunspells = LoadHunspellSet(langList);
            if (hunspells.Count == 0)
            {
                Console.Error.WriteLine("Hunspell dictionaries not found. Make sure 'hunspell' folder is copied next to the executable.");
                return Array.Empty<string>();
            }

            // Collect dictionary paths
            var dictionaryPaths = ResolveDictionaryPaths(langList).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (dictionaryPaths.Count == 0)
            {
                Console.Error.WriteLine("No .dic files found for selected languages in hunspell folder.");
                return Array.Empty<string>();
            }

            var wordSet = new ConcurrentDictionary<string, byte>(caseSensitive ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase);
            var phraseSet = new ConcurrentDictionary<string, byte>(caseSensitive ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase);
            var phraseCanonical = new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase);

            // -------------------------
            // Phase 1: scan dictionaries -> collect words
            // -------------------------
            await Task.Run(() =>
            {
                var po = new ParallelOptions { CancellationToken = token, MaxDegreeOfParallelism = workers };

                try
                {
                    Parallel.ForEach(dictionaryPaths, po, dicPath =>
                    {
                        bool isFirstLine = true;

                        foreach (var rawLine0 in File.ReadLines(dicPath))
                        {
                            po.CancellationToken.ThrowIfCancellationRequested();

                            if (string.IsNullOrWhiteSpace(rawLine0))
                                continue;

                            Interlocked.Increment(ref checkedLines);

                            var rawLine = rawLine0.Trim();

                            if (isFirstLine && rawLine.All(char.IsDigit))
                            {
                                isFirstLine = false;
                                continue;
                            }
                            isFirstLine = false;

                            // robust token extraction (BOM, whitespace, flags etc.)
                            var tokenWord = ExtractHunspellWordToken(rawLine);
                            if (tokenWord.Length == 0)
                                continue;

                            var word = caseSensitive ? tokenWord : tokenWord.ToLowerInvariant();

                            if (word.Length < minWordLength || word.Length > maxLen)
                                continue;

                            // quick fit
                            if (!WordFits(word, letterCounts, allowedLetters, reuse))
                                continue;

                            // hunspell verify
                            if (!hunspells.Any(h => h.Spell(word)))
                                continue;

                            if (wordSet.TryAdd(word, 0))
                            {
                                Interlocked.Increment(ref acceptedWords);
                                EmitRealtimeWord(word, substringSet, caseSensitive, realTimeOutput);
                            }
                        }
                    });
                }
                catch (OperationCanceledException)
                {
                    // expected on cancel
                }
            }, token).ConfigureAwait(false);

            // Build word signatures for tryUseAll filtering
            List<WordSig>? wordSigs = null;
            if (tryUseAll && !reuse)
            {
                wordSigs = BuildWordSigs(wordSet.Keys, minWordLength);
            }

            // Rank words
            var rankedWords = RankBestFirst(wordSet.Keys, substringSet, caseSensitive).ToList();

            // -------------------------
            // Phase 2: build phrases (no reuse)
            // -------------------------
            if (!reuse && !token.IsCancellationRequested && rankedWords.Count > 0)
            {
                await Task.Run(() =>
                {
                    try
                    {
                        int maxWordsPerPhrase = tryUseAll ? maxLen : 4;

                        // bias exploration: longer first helps find “good chains”
                        var candidatesByLen = rankedWords
                            .OrderByDescending(w => w.Length)
                            .ThenBy(w => w, caseSensitive ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase)
                            .ToList();

                        var remaining = new Dictionary<char, int>(letterCounts);
                        var phraseBuffer = new List<string>(capacity: maxWordsPerPhrase);

                        void Backtrack(int startIndex, int remainingLetters)
                        {
                            if (token.IsCancellationRequested)
                                return;

                            if (phraseBuffer.Count >= maxWordsPerPhrase)
                                return;

                            if (phraseBuffer.Count >= 2)
                            {
                                string phrase = string.Join(" ", phraseBuffer);

                                // filter permutations (canonical key based on sorted words)
                                if (filterPermutations)
                                {
                                    string key = CanonicalPhraseKey(phraseBuffer, caseSensitive);
                                    if (!phraseCanonical.TryAdd(key, 0))
                                        goto SkipPhrase;
                                }

                                // tryUseAll: only accept if remaining letters cannot build any further word
                                if (tryUseAll && wordSigs != null && RemainingCanFormAnyWord(remaining, wordSigs, minWordLength))
                                    goto SkipPhrase;

                                if (phraseSet.TryAdd(phrase, 0))
                                {
                                    Interlocked.Increment(ref acceptedPhrases);

                                    if (realTimeOutput)
                                    {
                                        SafeWriteLine(" >>> " + (substringSet != null ? DecorateWithConsecutiveDP(phrase, substringSet, caseSensitive) : phrase));
                                    }
                                }

                                SkipPhrase:;
                            }

                            for (int i = startIndex; i < candidatesByLen.Count; i++)
                            {
                                if (token.IsCancellationRequested)
                                    return;

                                var candidate = candidatesByLen[i];
                                if (candidate.Length > remainingLetters)
                                    continue;

                                if (!TryConsumeWord(candidate, remaining, out var consumed))
                                    continue;

                                phraseBuffer.Add(candidate);
                                Backtrack(i, remainingLetters - candidate.Length);
                                phraseBuffer.RemoveAt(phraseBuffer.Count - 1);
                                RestoreConsumed(consumed, remaining);
                            }
                        }

                        Backtrack(0, maxLen);
                    }
                    catch (OperationCanceledException)
                    {
                        // expected
                    }
                }, token).ConfigureAwait(false);

                var rankedPhrases = RankBestFirst(phraseSet.Keys, substringSet, caseSensitive).ToList();

                if (realTimeOutput)
                {
                    // already printed during backtrack; no duplicate print
                }
                else
                {
                    // nothing to do; phrases are already in phraseSet for final result
                }
            }
            // Dispose hunspell
            foreach (var h in hunspells) h.Dispose();

            progressTicker?.Dispose();
            sw.Stop();
            progress?.Report(1.0);

            // Final mixing (phrases first, then words)
            IEnumerable<string> finalAll = phraseSet.Keys
                .Concat(wordSet.Keys)
                .Distinct(caseSensitive ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase);

            // Apply tryUseAll filter for non-realtime results too
            if (tryUseAll && !reuse)
            {
                finalAll = finalAll.Where(x => PassesTryUseAllFilter(x, letterCounts, wordSigs, minWordLength));
            }

            // Decoration at the very end
            string[] finalResult = substringSet != null
                ? finalAll.Select(x => DecorateWithConsecutiveDP(x, substringSet, caseSensitive)).ToArray()
                : finalAll.ToArray();

            if (addEnumeration && finalResult.Length > 0)
            {
                int width = finalResult.Length <= 1 ? 1 : (int)Math.Floor(Math.Log10(finalResult.Length - 1)) + 1;
                string fmt = new string('0', width);
                for (int i = 0; i < finalResult.Length; i++)
                {
                    finalResult[i] = $"#{i.ToString(fmt)} {finalResult[i]}";
                }
            }

            // Write result file if requested
            if (createResultFile)
            {
                try
                {
                    var targetDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "TextResources");
                    Directory.CreateDirectory(targetDir);
                    var targetFile = Path.Combine(targetDir, "_TempRunResults.txt");
                    File.WriteAllLines(targetFile, finalResult);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Failed to write result file: {ex.Message}");
                }
            }

            // Summary output (your UI expects these numbers)
            Console.WriteLine();
            Console.WriteLine($"--- Completed {(token.IsCancellationRequested ? "(cancelled)" : "")} ---");
            Console.WriteLine($"Time: {sw.Elapsed}");
            Console.WriteLine($"Checked dictionary lines: {checkedLines:n0}");
            Console.WriteLine($"Accepted words: {acceptedWords:n0}");
            Console.WriteLine($"Accepted phrases: {acceptedPhrases:n0}");
            if (sw.Elapsed.TotalSeconds > 0)
            {
                Console.WriteLine("Scan rate: " + (checkedLines / sw.Elapsed.TotalSeconds).ToString("N1", CultureInfo.InvariantCulture) + " lines/s");
            }

            return finalResult;
        }

        // ------------------------------ TRY-USE-ALL LOGIC ------------------------------

        private static bool PassesTryUseAllFilter(
            string candidate,
            Dictionary<char, int> totalLetters,
            List<WordSig>? wordSigsOrNull,
            int minWordLength)
        {
            // tryUseAll only meaningful for no-reuse mode and when word sigs exist
            if (wordSigsOrNull == null) return true;

            var leftover = ComputeLeftover(candidate, totalLetters);
            return !LeftoverCanFormAnyWord(leftover, wordSigsOrNull, minWordLength);
        }

        private static bool LeftoverCanFormAnyWord(Dictionary<char, int> leftover, List<WordSig> wordSigs, int minWordLen)
        {
            int leftoverLen = 0;
            foreach (var v in leftover.Values) leftoverLen += v;
            if (leftoverLen < minWordLen) return false;

            foreach (var ws in wordSigs)
            {
                if (ws.Length > leftoverLen) continue;

                bool fits = true;
                foreach (var kv in ws.Counts)
                {
                    if (!leftover.TryGetValue(kv.Key, out int have) || have < kv.Value)
                    {
                        fits = false;
                        break;
                    }
                }

                if (fits) return true;
            }

            return false;
        }

        private static bool RemainingCanFormAnyWord(Dictionary<char, int> remaining, List<WordSig> wordSigs, int minWordLen)
        {
            int leftoverLen = 0;
            foreach (var v in remaining.Values) leftoverLen += v;
            if (leftoverLen < minWordLen) return false;

            foreach (var ws in wordSigs)
            {
                if (ws.Length > leftoverLen) continue;

                bool fits = true;
                foreach (var kv in ws.Counts)
                {
                    if (!remaining.TryGetValue(kv.Key, out int have) || have < kv.Value)
                    {
                        fits = false;
                        break;
                    }
                }

                if (fits) return true;
            }

            return false;
        }

        private static Dictionary<char, int> ComputeLeftover(string result, Dictionary<char, int> total)
        {
            var used = CountLetters(result.Replace(" ", string.Empty));
            var leftover = new Dictionary<char, int>(total);

            foreach (var kv in used)
            {
                if (leftover.TryGetValue(kv.Key, out var have))
                {
                    int remain = have - kv.Value;
                    if (remain <= 0) leftover.Remove(kv.Key);
                    else leftover[kv.Key] = remain;
                }
            }

            return leftover;
        }

        private sealed class WordSig
        {
            public string Word { get; }
            public int Length { get; }
            public Dictionary<char, int> Counts { get; }

            public WordSig(string word)
            {
                Word = word;
                Length = word.Length;
                Counts = CountLetters(word);
            }
        }

        private static List<WordSig> BuildWordSigs(IEnumerable<string> words, int minLen)
        {
            return words
                .Where(w => w.Length >= minLen)
                .Select(w => new WordSig(w))
                .OrderByDescending(s => s.Length) // early exit faster
                .ToList();
        }

        // ------------------------------ RANKING ------------------------------

        private static IEnumerable<string> RankBestFirst(IEnumerable<string> items, HashSet<string>? substringSet, bool caseSensitive)
        {
            if (substringSet == null)
            {
                return items
                    .OrderBy(x => x.Count(c => c == ' '))
                    .ThenBy(x => x.Length)
                    .ThenBy(x => x, caseSensitive ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase);
            }

            return items
                .Select(t => (Text: t, Score: ScorePhrase(t, substringSet, caseSensitive)))
                .OrderBy(x => x.Score.Pieces)                 // fewer cuts
                .ThenByDescending(x => x.Score.MaxChain)      // longer best chain
                .ThenByDescending(x => x.Score.TotalChained)  // more chained coverage
                .ThenBy(x => x.Text.Count(c => c == ' '))
                .ThenBy(x => x.Text.Length)
                .ThenBy(x => x.Text, caseSensitive ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase)
                .Select(x => x.Text);
        }

        private static ChainScore ScorePhrase(string phrase, HashSet<string> subs, bool caseSensitive)
        {
            var words = phrase.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            int pieces = 0, maxChain = 0, total = 0;

            foreach (var w in words)
            {
                var s = ScoreWord(w, subs, caseSensitive);
                pieces += s.Pieces;
                maxChain = Math.Max(maxChain, s.MaxChain);
                total += s.TotalChained;
            }

            return new ChainScore(pieces, maxChain, total);
        }

        private static ChainScore ScoreWord(string word, HashSet<string> subs, bool caseSensitive)
        {
            if (string.IsNullOrEmpty(word))
                return new ChainScore(int.MaxValue, 0, 0);

            var w = caseSensitive ? word : word.ToLowerInvariant();
            int n = w.Length;

            var dp = new ChainScore[n + 1];
            var has = new bool[n + 1];

            dp[n] = new ChainScore(0, 0, 0);
            has[n] = true;

            for (int i = n - 1; i >= 0; i--)
            {
                ChainScore best = default;
                bool bestSet = false;

                // single char piece
                if (has[i + 1])
                {
                    best = new ChainScore(dp[i + 1].Pieces + 1, dp[i + 1].MaxChain, dp[i + 1].TotalChained);
                    bestSet = true;
                }

                // chain len>=2
                for (int len = 2; i + len <= n; len++)
                {
                    var sub = w.Substring(i, len);
                    if (!subs.Contains(sub)) continue;
                    if (!has[i + len]) continue;

                    var cand = new ChainScore(
                        Pieces: dp[i + len].Pieces + 1,
                        MaxChain: Math.Max(dp[i + len].MaxChain, len),
                        TotalChained: dp[i + len].TotalChained + len
                    );

                    if (!bestSet || Better(cand, best))
                    {
                        best = cand;
                        bestSet = true;
                    }
                }

                if (bestSet)
                {
                    dp[i] = best;
                    has[i] = true;
                }
            }

            return has[0] ? dp[0] : new ChainScore(int.MaxValue, 0, 0);

            static bool Better(ChainScore a, ChainScore b)
            {
                if (a.Pieces != b.Pieces) return a.Pieces < b.Pieces;
                if (a.MaxChain != b.MaxChain) return a.MaxChain > b.MaxChain;
                if (a.TotalChained != b.TotalChained) return a.TotalChained > b.TotalChained;
                return false;
            }
        }

        // ------------------------------ DECORATION: DP that can output multiple [..] segments ------------------------------

        private static string DecorateWithConsecutiveDP(string phrase, HashSet<string> subs, bool caseSensitive)
        {
            var words = phrase.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var sb = new StringBuilder();

            for (int i = 0; i < words.Length; i++)
            {
                if (i > 0) sb.Append(' ');
                sb.Append(DecorateWordDP(words[i], subs, caseSensitive));
            }

            return sb.ToString();
        }

        private static string DecorateWordDP(string word, HashSet<string> subs, bool caseSensitive)
        {
            if (string.IsNullOrEmpty(word) || word.Length < 2)
                return word;

            var target = caseSensitive ? word : word.ToLowerInvariant();
            int n = target.Length;

            var dp = new ChainScore[n + 1];
            var has = new bool[n + 1];
            var nextLen = new int[n + 1];
            var nextIsChain = new bool[n + 1];

            dp[n] = new ChainScore(0, 0, 0);
            has[n] = true;

            for (int i = n - 1; i >= 0; i--)
            {
                ChainScore best = default;
                bool bestSet = false;
                int bestLen = 1;
                bool bestChain = false;

                if (has[i + 1])
                {
                    best = new ChainScore(dp[i + 1].Pieces + 1, dp[i + 1].MaxChain, dp[i + 1].TotalChained);
                    bestSet = true;
                    bestLen = 1;
                    bestChain = false;
                }

                for (int len = 2; i + len <= n; len++)
                {
                    var sub = target.Substring(i, len);
                    if (!subs.Contains(sub)) continue;
                    if (!has[i + len]) continue;

                    var cand = new ChainScore(
                        Pieces: dp[i + len].Pieces + 1,
                        MaxChain: Math.Max(dp[i + len].MaxChain, len),
                        TotalChained: dp[i + len].TotalChained + len
                    );

                    if (!bestSet || Better(cand, best))
                    {
                        best = cand;
                        bestSet = true;
                        bestLen = len;
                        bestChain = true;
                    }
                }

                if (bestSet)
                {
                    dp[i] = best;
                    has[i] = true;
                    nextLen[i] = bestLen;
                    nextIsChain[i] = bestChain;
                }
            }

            if (!has[0])
                return word;

            var sb = new StringBuilder();
            int pos = 0;
            while (pos < n)
            {
                int len = nextLen[pos] <= 0 ? 1 : nextLen[pos];
                bool isChain = nextIsChain[pos] && len >= 2;

                if (isChain)
                {
                    sb.Append('[');
                    sb.Append(word.Substring(pos, len)); // keep original casing
                    sb.Append(']');
                }
                else
                {
                    sb.Append(word[pos]);
                }

                pos += len;
            }

            return sb.ToString();

            static bool Better(ChainScore a, ChainScore b)
            {
                if (a.Pieces != b.Pieces) return a.Pieces < b.Pieces;
                if (a.MaxChain != b.MaxChain) return a.MaxChain > b.MaxChain;
                if (a.TotalChained != b.TotalChained) return a.TotalChained > b.TotalChained;
                return false;
            }
        }

        // ------------------------------ HUNSPELL LOADING ------------------------------

        private static List<Hunspell> LoadHunspellSet(IEnumerable<Languages> languages)
        {
            var basePath = Path.Combine(AppContext.BaseDirectory, "hunspell");

            var uniquePairs = new HashSet<(string aff, string dic)>(new PairComparer());
            foreach (var lang in languages)
            {
                if (!HunspellFiles.TryGetValue(lang, out var pairs)) continue;
                foreach (var p in pairs) uniquePairs.Add((p.Aff, p.Dic));
            }

            var list = new List<Hunspell>();
            foreach (var (aff, dic) in uniquePairs)
            {
                var affPath = Path.Combine(basePath, aff);
                var dicPath = Path.Combine(basePath, dic);
                if (File.Exists(affPath) && File.Exists(dicPath))
                {
                    list.Add(new Hunspell(affPath, dicPath));
                }
            }

            return list;
        }

        private static IEnumerable<string> ResolveDictionaryPaths(IEnumerable<Languages> languages)
        {
            var basePath = Path.Combine(AppContext.BaseDirectory, "hunspell");
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var lang in languages)
            {
                if (!HunspellFiles.TryGetValue(lang, out var pairs)) continue;
                foreach (var (_, dic) in pairs)
                {
                    var dicPath = Path.Combine(basePath, dic);
                    if (File.Exists(dicPath)) set.Add(dicPath);
                }
            }

            return set;
        }

        private sealed class PairComparer : IEqualityComparer<(string, string)>
        {
            public bool Equals((string, string) x, (string, string) y)
                => StringComparer.OrdinalIgnoreCase.Equals(x.Item1, y.Item1)
                   && StringComparer.OrdinalIgnoreCase.Equals(x.Item2, y.Item2);

            public int GetHashCode((string, string) obj)
            {
                int h1 = StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Item1 ?? string.Empty);
                int h2 = StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Item2 ?? string.Empty);
                return (h1 * 397) ^ h2;
            }
        }

        // Robust .dic token extraction:
        // - strips BOM
        // - cuts at first '/' or whitespace
        // - rejects tokens with non-letters (since input letter pool strips non-letters)
        private static string ExtractHunspellWordToken(string rawLine)
        {
            if (string.IsNullOrWhiteSpace(rawLine)) return string.Empty;

            var s = rawLine.Trim().TrimStart('\uFEFF');

            int cut = s.Length;
            int slash = s.IndexOf('/');
            if (slash >= 0 && slash < cut) cut = slash;

            for (int i = 0; i < s.Length; i++)
            {
                if (char.IsWhiteSpace(s[i]))
                {
                    if (i < cut) cut = i;
                    break;
                }
            }

            if (cut <= 0) return string.Empty;

            s = s.Substring(0, cut);

            // reject tokens with punctuation/digits (they'd never fit your normalized letter pool)
            for (int i = 0; i < s.Length; i++)
            {
                if (!char.IsLetter(s[i])) return string.Empty;
            }

            return s;
        }

        private static void EmitRealtimeWord(string word, HashSet<string>? substringSet, bool caseSensitive, bool realTimeOutput)
        {
            if (!realTimeOutput)
                return;

            if (substringSet != null)
            {
                SafeWriteLine(" > " + DecorateWithConsecutiveDP(word, substringSet, caseSensitive));
            }
            else
            {
                SafeWriteLine(" > " + word);
            }
        }


        // ------------------------------ CANCELLATION / PROGRESS ------------------------------

        private static IDisposable StartCancelOnQ(CancellationTokenSource linked)
        {
            var localCts = new CancellationTokenSource();
            var t = Task.Run(async () =>
            {
                try
                {
                    while (!localCts.IsCancellationRequested && !linked.IsCancellationRequested)
                    {
                        if (Console.KeyAvailable)
                        {
                            var key = Console.ReadKey(intercept: true);
                            if (key.Key == ConsoleKey.Q)
                            {
                                linked.Cancel();
                                return;
                            }
                        }
                        await Task.Delay(50, localCts.Token).ConfigureAwait(false);
                    }
                }
                catch
                {
                    // ignore
                }
            }, localCts.Token);

            return new AnonymousDisposable(() =>
            {
                try { localCts.Cancel(); } catch { }
                try { t.Wait(200); } catch { }
                localCts.Dispose();
            });
        }

        private static IDisposable StartCmdProgressTicker(
            Func<long> getChecked,
            Func<TimeSpan> getElapsed,
            Func<long> getWords,
            Func<long> getPhrases,
            CancellationTokenSource linked)
        {
            var local = new CancellationTokenSource();
            var token = CancellationTokenSource.CreateLinkedTokenSource(local.Token, linked.Token).Token;

            long lastChecked = 0;
            long lastTotal = 0;
            long lastTicks = Stopwatch.GetTimestamp();
            double tickFreq = Stopwatch.Frequency;

            var t = Task.Run(async () =>
            {
                 while (!token.IsCancellationRequested)
                 {
                    var checkedNow = getChecked();
                    var phrasesNow = getPhrases();
                    var totalNow = checkedNow + phrasesNow;
                    var nowTicks = Stopwatch.GetTimestamp();
                    var delta = totalNow - lastTotal;
                    var dt = Math.Max((nowTicks - lastTicks) / tickFreq, 0.001);
                    var rate = delta / dt;

                    var elapsed = getElapsed();
                    string line = $" ### Checked: {checkedNow:n0} | Words: {getWords():n0} | Phrases: {phrasesNow:n0} | Rate: {rate:F1}/s | Time: {FormatElapsed(elapsed)} | Press 'Q' to cancel";
                    WriteProgressLine(line);

                    lastChecked = checkedNow;
                    lastTotal = totalNow;
                    lastTicks = nowTicks;

                    await Task.Delay(1000, token).ConfigureAwait(false);
                 }
            }, token);

            return new AnonymousDisposable(() =>
            {
                try { local.Cancel(); } catch { }
                try { t.Wait(300); } catch { }
                local.Dispose();
            });
        }

        private static void WriteProgressLine(string text)
        {
            try
            {
                int width = Math.Max(20, Console.BufferWidth);
                if (text.Length >= width) text = text.Substring(0, width - 1);

                Console.Write('\r');
                Console.Write(text);

                int rest = Math.Max(0, width - text.Length - 1);
                if (rest > 0) Console.Write(new string(' ', Math.Min(rest, 200)));
            }
            catch
            {
                // ignore when console isn't interactive
            }
        }

        private static void SafeWriteLine(string text)
        {
            lock (ConsoleLock)
            {
                Console.WriteLine(text);
            }
        }

        private static string FormatElapsed(TimeSpan t)
        {
            if (t.TotalHours >= 1) return $"{(int) t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}";
            if (t.TotalMinutes >= 1) return $"{t.Minutes}:{t.Seconds:00}";
            return $"{t.Seconds}s";
        }

        private sealed class AnonymousDisposable : IDisposable
        {
            private readonly Action _dispose;
            public AnonymousDisposable(Action dispose) => _dispose = dispose;
            public void Dispose() => _dispose();
        }

        // ------------------------------ LETTER / WORD HELPERS ------------------------------

        private static string NormalizeLetters(string input, bool caseSensitive)
        {
            var builder = new StringBuilder(input.Length);
            foreach (var c0 in caseSensitive ? input : input.ToLowerInvariant())
            {
                if (char.IsLetter(c0))
                    builder.Append(c0);
            }
            return builder.ToString();
        }

        private static Dictionary<char, int> CountLetters(string lettersOnly)
        {
            var counts = new Dictionary<char, int>();
            foreach (var c in lettersOnly)
            {
                if (!counts.TryGetValue(c, out var v)) v = 0;
                counts[c] = v + 1;
            }
            return counts;
        }

        private static List<string> GetOriginalSegments(string input, bool caseSensitive)
        {
            var normalized = caseSensitive ? input : input.ToLowerInvariant();
            var sanitized = normalized.Select(c => char.IsLetter(c) ? c : ' ').ToArray();
            return new string(sanitized)
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .ToList();
        }

        private static HashSet<string> BuildSubstringSet(List<string> segments, bool caseSensitive, int maxSubLen = 64)
        {
            var set = new HashSet<string>(caseSensitive ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase);

            foreach (var segRaw in segments)
            {
                var seg = caseSensitive ? segRaw : segRaw.ToLowerInvariant();
                if (seg.Length < 2) continue;

                int cap = Math.Min(seg.Length, Math.Max(2, maxSubLen));
                for (int i = 0; i < seg.Length; i++)
                {
                    for (int len = 2; len <= cap && i + len <= seg.Length; len++)
                        set.Add(seg.Substring(i, len));
                }
            }

            return set;
        }

        private static bool WordFits(string word, Dictionary<char, int> available, HashSet<char> allowedLetters, bool reuse)
        {
            if (reuse)
            {
                foreach (var c in word)
                    if (!allowedLetters.Contains(c)) return false;
                return true;
            }

            var remaining = new Dictionary<char, int>(available);
            foreach (var c in word)
            {
                if (!remaining.TryGetValue(c, out var count) || count == 0)
                    return false;
                remaining[c] = count - 1;
            }

            return true;
        }

        private static bool TryConsumeWord(string word, Dictionary<char, int> remaining, out List<(char Letter, int Count)> consumed)
        {
            consumed = new List<(char, int)>();

            var needed = new Dictionary<char, int>();
            foreach (var c in word)
            {
                if (!needed.TryGetValue(c, out var v)) v = 0;
                needed[c] = v + 1;
            }

            foreach (var kv in needed)
            {
                if (!remaining.TryGetValue(kv.Key, out var have) || have < kv.Value)
                    return false;
            }

            foreach (var kv in needed)
            {
                remaining[kv.Key] = remaining[kv.Key] - kv.Value;
                consumed.Add((kv.Key, kv.Value));
            }

            return true;
        }

        private static void RestoreConsumed(List<(char Letter, int Count)> consumed, Dictionary<char, int> remaining)
        {
            foreach (var (letter, count) in consumed)
            {
                if (!remaining.TryGetValue(letter, out var have)) have = 0;
                remaining[letter] = have + count;
            }
        }

        private static string CanonicalPhraseKey(IEnumerable<string> words, bool caseSensitive)
        {
            var comparer = caseSensitive ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;
            var ordered = words.OrderBy(w => w, comparer);
            return string.Join(" ", ordered);
        }

        // ------------------------------ SYSTEM / WORKERS ------------------------------

        private static Languages? TryGetSystemLanguage()
        {
            var culture = CultureInfo.CurrentCulture;
            return culture.TwoLetterISOLanguageName switch
            {
                "de" => Languages.German,
                "en" => Languages.English,
                "fr" => Languages.French,
                "tr" => Languages.Turkish,
                "es" => Languages.Spanish,
                "it" => Languages.Italian,
                _ => null
            };
        }

        private static int NormalizeWorkers(int maxWorkers)
        {
            int cpu = Environment.ProcessorCount;
            if (maxWorkers <= 0) return cpu;
            return Math.Clamp(maxWorkers, 1, cpu);
        }
    }
}
