using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NHunspell;

namespace LettersToPhrases.Cli
{
    public static class LettersCombinator
    {
        public enum Languages
        {
            German,
            English,
            EnglishGb,
            EnglishUs,
            French
        }

        private static readonly Dictionary<Languages, (string Aff, string Dic)[]> HunspellFiles = new()
        {
            { Languages.German, new[] { ("de_DE.aff", "de_DE.dic") } },
            { Languages.English, new[] { ("en_US.aff", "en_US.dic"), ("en_GB.aff", "en_GB.dic") } },
            { Languages.EnglishGb, new[] { ("en_GB.aff", "en_GB.dic") } },
            { Languages.EnglishUs, new[] { ("en_US.aff", "en_US.dic") } },
            { Languages.French, new[] { ("fr.aff", "fr.dic"), ("fr_FR.aff", "fr_FR.dic") } }
        };

        /// <summary>
        /// Generates words/phrases from a pool of letters using Hunspell dictionaries.
        /// If indicateConsecutiveSubstrings==true, results are decorated with [..] marking
        /// maximum "no-cut" reuse via DP segmentation, and output is ranked best-first by chain score.
        /// </summary>
        public static async Task<IEnumerable<string>> GetPhrasesFromLettersAsync(
            string letters,
            bool caseSensitive = false,
            bool reuse = false,
            Languages? language = Languages.German,
            int maxPhrases = 1024,
            bool realTimeOutput = true,
            bool indicateConsecutiveSubstrings = false,
            IProgress<double>? progress = null)
        {
            if (string.IsNullOrWhiteSpace(letters))
                return Enumerable.Empty<string>();

            var normalizedLetters = NormalizeLetters(letters, caseSensitive);
            if (normalizedLetters.Length == 0)
                return Enumerable.Empty<string>();

            var langToUse = language ?? Languages.German;
            if (!HunspellFiles.TryGetValue(langToUse, out var files))
                return Enumerable.Empty<string>();

            var hunspells = LoadHunspellSet(langToUse);
            if (hunspells.Count == 0)
            {
                Console.Error.WriteLine($"Hunspell dictionaries for {langToUse} not found (expected in hunspell folder). No phrases generated.");
                return Enumerable.Empty<string>();
            }

            var basePath = Path.Combine(AppContext.BaseDirectory, "hunspell");
            var dictionaryPaths = files
                .Select(f => Path.Combine(basePath, f.Dic))
                .Where(File.Exists)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (dictionaryPaths.Count == 0)
            {
                Console.Error.WriteLine($"Hunspell dictionary files for {langToUse} not found (expected in hunspell folder). No phrases generated.");
                return Enumerable.Empty<string>();
            }

            int maxCandidates = maxPhrases == int.MaxValue ? int.MaxValue : 5000;
            const int maxWordsPerPhrase = 4;

            var originalSegments = indicateConsecutiveSubstrings ? GetOriginalSegments(letters, caseSensitive) : null;
            var substringSet = (indicateConsecutiveSubstrings && originalSegments != null)
                ? BuildSubstringSet(originalSegments, caseSensitive, maxSubLen: 64)
                : null;

            var letterCounts = CountLetters(normalizedLetters);
            var allowedLetters = new HashSet<char>(letterCounts.Keys);
            var maxLen = normalizedLetters.Length;

            var wordSet = new ConcurrentDictionary<string, byte>(caseSensitive ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase);
            var phraseSet = new ConcurrentDictionary<string, byte>(caseSensitive ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase);

            // We'll buffer for best-first output (even if realTimeOutput==true).
            var candidates = new List<string>(capacity: Math.Min(4096, maxCandidates));

            // We'll also buffer phrases (because best-first needs scoring).
            var phrasesBuffer = new List<string>(capacity: Math.Min(maxPhrases, 8192));

            await Task.Run(() =>
            {
                // 1) Collect word candidates (no immediate printing; we'll sort later)
                foreach (var dicPath in dictionaryPaths)
                {
                    var isFirstLine = true;
                    foreach (var rawLine in File.ReadLines(dicPath))
                    {
                        if (string.IsNullOrWhiteSpace(rawLine))
                            continue;

                        if (isFirstLine && rawLine.Trim().All(char.IsDigit))
                        {
                            isFirstLine = false;
                            continue; // skip word count header
                        }

                        isFirstLine = false;

                        var wordPart = rawLine.Split('/', 2)[0];
                        var word = caseSensitive ? wordPart : wordPart.ToLowerInvariant();

                        if (word.Length < 2 || word.Length > maxLen)
                            continue;

                        if (!WordFits(word, letterCounts, allowedLetters, reuse))
                            continue;

                        if (!hunspells.Any(h => h.Spell(word)))
                            continue;

                        if (wordSet.TryAdd(word, 0))
                        {
                            candidates.Add(word);
                            if (candidates.Count >= maxCandidates)
                                break;
                        }
                    }

                    if (candidates.Count >= maxCandidates)
                        break;
                }

                // 2) Generate multi-word phrases only if without reuse.
                if (!reuse && candidates.Count > 0)
                {
                    // For phrase exploration, we want to try "better" words earlier.
                    // If substring scoring is enabled, order candidates by chain score desc (best-first),
                    // otherwise fall back to length-based order.
                    var orderedCandidates = (substringSet != null)
                        ? candidates
                            .OrderBy(w => ScoreWord(w, substringSet, caseSensitive).Pieces)
                            .ThenByDescending(w => ScoreWord(w, substringSet, caseSensitive).MaxChain)
                            .ThenByDescending(w => ScoreWord(w, substringSet, caseSensitive).TotalChained)
                            .ThenByDescending(w => w.Length)
                            .ThenBy(w => w)
                            .ToList()
                        : candidates
                            .OrderByDescending(w => w.Length)
                            .ThenBy(w => w)
                            .ToList();

                    var remaining = new Dictionary<char, int>(letterCounts);
                    var phraseBuffer = new List<string>(capacity: maxWordsPerPhrase);

                    void Backtrack(int startIndex, int remainingLetters)
                    {
                        if (phraseBuffer.Count >= maxWordsPerPhrase)
                            return;

                        if (phraseBuffer.Count >= 2)
                        {
                            var phrase = string.Join(" ", phraseBuffer);
                            if (phraseSet.TryAdd(phrase, 0))
                            {
                                phrasesBuffer.Add(phrase);
                                if (phraseSet.Count >= maxPhrases)
                                    return;
                            }
                        }

                        for (var i = startIndex; i < orderedCandidates.Count; i++)
                        {
                            if (phraseSet.Count >= maxPhrases)
                                return;

                            var candidate = orderedCandidates[i];
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
            }).ConfigureAwait(false);

            foreach (var h in hunspells)
                h.Dispose();

            progress?.Report(1.0);

            // -------- Best-first ordering + optional decoration --------

            // Build final set: phrases first (preferred), then words.
            var all = phraseSet.Keys
                .Concat(wordSet.Keys)
                .Distinct(caseSensitive ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase)
                .ToList();

            // Rank by chain score if enabled; else keep your old ordering.
            List<string> ranked;
            if (substringSet != null)
            {
                ranked = all
                    .Select(p => (Text: p, Score: ScorePhrase(p, substringSet, caseSensitive)))
                    .OrderBy(x => x.Score.Pieces)                 // fewer cuts first
                    .ThenByDescending(x => x.Score.MaxChain)      // longer best chain
                    .ThenByDescending(x => x.Score.TotalChained)  // more chained coverage
                    .ThenBy(x => x.Text.Count(c => c == ' '))     // slightly prefer fewer words
                    .ThenBy(x => x.Text.Length)
                    .ThenBy(x => x.Text)
                    .Take(maxPhrases)
                    .Select(x => x.Text)
                    .ToList();
            }
            else
            {
                ranked = all
                    .OrderBy(w => w.Count(c => c == ' '))
                    .ThenBy(w => w.Length)
                    .ThenBy(w => w)
                    .Take(maxPhrases)
                    .ToList();
            }

            // Decorate if requested.
            if (substringSet != null)
            {
                var decorated = ranked
                    .Select(p => DecorateWithConsecutiveDP(p, substringSet, caseSensitive))
                    .ToList();

                // "Real time" output, but best-first: print in ranked order.
                if (realTimeOutput)
                {
                    foreach (var line in decorated)
                        Console.WriteLine(line);
                }

                return decorated;
            }

            // No decoration
            if (realTimeOutput)
            {
                foreach (var line in ranked)
                    Console.WriteLine(line);
            }

            return ranked;
        }

        // ---------------- Decoration (DP, multiple [..] segments) ----------------

        private static string DecorateWithConsecutiveDP(string phrase, HashSet<string> subs, bool caseSensitive)
        {
            var words = phrase.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var builder = new StringBuilder();

            for (var i = 0; i < words.Length; i++)
            {
                if (i > 0) builder.Append(' ');

                var decorated = DecorateWordDP(words[i], subs, caseSensitive);
                builder.Append(decorated);
            }

            return builder.ToString();
        }

        private static string DecorateWordDP(string word, HashSet<string> subs, bool caseSensitive)
        {
            if (string.IsNullOrEmpty(word) || word.Length < 2)
                return word;

            var target = caseSensitive ? word : word.ToLowerInvariant();
            int n = target.Length;

            // DP to minimize pieces, then maximize maxChain, then totalChained
            var dp = new ChainScore[n + 1];
            var has = new bool[n + 1];
            var nextLen = new int[n + 1];      // chosen length
            var nextIsChain = new bool[n + 1]; // chosen token type

            dp[n] = new ChainScore(0, 0, 0);
            has[n] = true;

            for (int i = n - 1; i >= 0; i--)
            {
                ChainScore best = default;
                bool bestSet = false;
                int bestLen = 1;
                bool bestChain = false;

                // Single char fallback
                if (has[i + 1])
                {
                    var cand = new ChainScore(
                        Pieces: dp[i + 1].Pieces + 1,
                        MaxChain: dp[i + 1].MaxChain,
                        TotalChained: dp[i + 1].TotalChained
                    );
                    best = cand;
                    bestSet = true;
                    bestLen = 1;
                    bestChain = false;
                }

                // Chains len>=2
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

            // Reconstruct with brackets around chains
            var sb = new StringBuilder();
            int pos = 0;
            while (pos < n)
            {
                int len = nextLen[pos] <= 0 ? 1 : nextLen[pos];
                bool isChain = nextIsChain[pos] && len >= 2;

                if (isChain)
                {
                    sb.Append('[');
                    sb.Append(word.Substring(pos, len)); // preserve original casing in output
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

        // ---------------- Scoring ----------------

        private readonly record struct ChainScore(int Pieces, int MaxChain, int TotalChained);

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

                // single char
                if (has[i + 1])
                {
                    best = new ChainScore(dp[i + 1].Pieces + 1, dp[i + 1].MaxChain, dp[i + 1].TotalChained);
                    bestSet = true;
                }

                // chains
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

        // ---------------- Helpers ----------------

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
                    {
                        set.Add(seg.Substring(i, len));
                    }
                }
            }

            return set;
        }

        private static bool TryConsumeWord(string word, Dictionary<char, int> remaining, out List<(char Letter, int Count)> consumed)
        {
            consumed = new List<(char, int)>();
            var needed = new Dictionary<char, int>();
            foreach (var c in word)
            {
                if (!needed.ContainsKey(c))
                    needed[c] = 0;
                needed[c]++;
            }

            foreach (var kvp in needed)
            {
                if (!remaining.TryGetValue(kvp.Key, out var count) || count < kvp.Value)
                    return false;
            }

            foreach (var kvp in needed)
            {
                remaining[kvp.Key] -= kvp.Value;
                consumed.Add((kvp.Key, kvp.Value));
            }

            return true;
        }

        private static void RestoreConsumed(List<(char Letter, int Count)> consumed, Dictionary<char, int> remaining)
        {
            foreach (var (letter, count) in consumed)
                remaining[letter] = remaining.TryGetValue(letter, out var current) ? current + count : count;
        }

        private static bool WordFits(string word, Dictionary<char, int> available, HashSet<char> allowedLetters, bool reuse)
        {
            if (reuse)
            {
                foreach (var c in word)
                    if (!allowedLetters.Contains(c))
                        return false;
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

        private static List<Hunspell> LoadHunspellSet(Languages language)
        {
            if (!HunspellFiles.TryGetValue(language, out var files))
                return new List<Hunspell>();

            var basePath = Path.Combine(AppContext.BaseDirectory, "hunspell");
            var list = new List<Hunspell>();
            foreach (var (aff, dic) in files)
            {
                var affPath = Path.Combine(basePath, aff);
                var dicPath = Path.Combine(basePath, dic);
                if (File.Exists(affPath) && File.Exists(dicPath))
                    list.Add(new Hunspell(affPath, dicPath));
            }

            return list;
        }

        private static Dictionary<char, int> CountLetters(string letters)
        {
            var counts = new Dictionary<char, int>();
            foreach (var c in letters)
            {
                if (!counts.ContainsKey(c))
                    counts[c] = 0;
                counts[c]++;
            }
            return counts;
        }

        private static string NormalizeLetters(string input, bool caseSensitive)
        {
            var builder = new StringBuilder();
            foreach (var c in caseSensitive ? input : input.ToLowerInvariant())
            {
                if (char.IsLetter(c))
                    builder.Append(c);
            }
            return builder.ToString();
        }
    }
}
