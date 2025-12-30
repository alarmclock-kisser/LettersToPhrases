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
	public static class LettersCombiner
	{
		// CancellationToken that will be used and triggered (when the user presses Q-Key)
		private static CancellationTokenSource Cts = new();

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

		// Letters input prompt (multilingual)
		public static readonly Dictionary<Languages, string> LettersInputPrompts = new()
		{
			{ Languages.German, "Bitte Buchstabenkette eingeben und mit Enter bestätigen: " },
			{ Languages.German_AT, "Bitte Buchstabenkette eingeben und mit Enter bestätigen: " },
			{ Languages.German_CH, "Bitte Buchstabenkette eingeben und mit Enter bestätigen: " },
			{ Languages.English, "Please enter a letter sequence and confirm with Enter: " },
			{ Languages.English_US, "Please enter a letter sequence and confirm with Enter: " },
			{ Languages.English_GB, "Please enter a letter sequence and confirm with Enter: " },
			{ Languages.French, "Veuillez saisir une séquence de lettres et confirmer avec Entrée : " },
			{ Languages.Turkish, "Lütfen bir harf dizisi girin ve Enter ile onaylayın: " },
			{ Languages.Spanish, "Por favor, introduzca una secuencia de letras y confirme con Enter: " },
			{ Languages.Italian, "Si prega di inserire una sequenza di lettere e confermare con Invio: " }
		};

		// CaseSensitive input prompt (multilingual)
		public static readonly Dictionary<Languages, string> CaseInputPrompts = new()
		{
			{ Languages.German, "Groß-/Kleinschreibung beachten? (J/N, Standard N): " },
			{ Languages.German_AT, "Groß-/Kleinschreibung beachten? (J/N, Standard N): " },
			{ Languages.German_CH, "Groß-/Kleinschreibung beachten? (J/N, Standard N): " },
			{ Languages.English, "Case sensitive? (Y/N, default N): " },
			{ Languages.English_US, "Case sensitive? (Y/N, default N): " },
			{ Languages.English_GB, "Case sensitive? (Y/N, default N): " },
			{ Languages.French, "Sensible à la casse ? (O/N, défaut N) : " },
			{ Languages.Turkish, "Büyük/küçük harf duyarlı mı? (E/H, varsayılan H): " },
			{ Languages.Spanish, "¿Distinguir mayúsculas y minúsculas? (S/N, por defecto N): " },
			{ Languages.Italian, "Maiuscole/minuscole sensibili? (S/N, predefinito N): " }
		};

		// Reuse letters input prompt (multilingual)
		public static readonly Dictionary<Languages, string> ReuseInputPrompts = new()
		{
			{ Languages.German, "Kombinatorik mit Zurücklegen (Wiederverwendung der Buchstaben) ? (J/N, Standard N): " },
			{ Languages.German_AT, "Kombinatorik mit Zurücklegen (Wiederverwendung der Buchstaben) ? (J/N, Standard N): " },
			{ Languages.German_CH, "Kombinatorik mit Zurücklegen (Wiederverwendung der Buchstaben) ? (J/N, Standard N): " },
			{ Languages.English, "Combinatorics with replacement (reuse letters)? (Y/N, default N): " },
			{ Languages.English_US, "Combinatorics with replacement (reuse letters)? (Y/N, default N): " },
			{ Languages.English_GB, "Combinatorics with replacement (reuse letters)? (Y/N, default N): " },
			{ Languages.French, "Combinaisons avec remise (réutilisation des lettres) ? (O/N, défaut N) : " },
			{ Languages.Turkish, "Yerine koyma ile kombinasyon (harfleri yeniden kullanma)? (E/H, varsayılan H): " },
			{ Languages.Spanish, "¿Combinatoria con reemplazo (reutilizar letras)? (S/N, por defecto N): " },
			{ Languages.Italian, "Combinatoria con reinserimento (riutilizzo delle lettere)? (S/N, predefinito N): " }
		};

		// Length minimum input prompt (multilingual)
		public static readonly Dictionary<Languages, string> LengthInputPrompts = new()
		{
			{ Languages.German, "Minimale Wortlänge (Standard 2): " },
			{ Languages.German_AT, "Minimale Wortlänge (Standard 2): " },
			{ Languages.German_CH, "Minimale Wortlänge (Standard 2): " },
			{ Languages.English, "Minimum word length (default 2): " },
			{ Languages.English_US, "Minimum word length (default 2): " },
			{ Languages.English_GB, "Minimum word length (default 2): " },
			{ Languages.French, "Longueur minimale des mots (par défaut 2) : " },
			{ Languages.Turkish, "Minimum kelime uzunluğu (varsayılan 2): " },
			{ Languages.Spanish, "Longitud mínima de la palabra (por defecto 2): " },
			{ Languages.Italian, "Lunghezza minima della parola (predefinito 2): " }
		};

		// Indicate consecutive substrings input prompt (multilingual)
		public static readonly Dictionary<Languages, string> IndicateInputPrompts = new()
		{
			{ Languages.German, "Aufeinanderfolgende Teilstrings der Eingabe-Zeichenfolge kennzeichnen? (J/N, Standard J): " },
			{ Languages.German_AT, "Aufeinanderfolgende Teilstrings der Eingabe-Zeichenfolge kennzeichnen? (J/N, Standard J): " },
			{ Languages.German_CH, "Aufeinanderfolgende Teilstrings der Eingabe-Zeichenfolge kennzeichnen? (J/N, Standard J): " },
			{ Languages.English, "Indicate consecutive substrings of the input letter sequence? (Y/N, default Y): " },
			{ Languages.English_US, "Indicate consecutive substrings of the input letter sequence? (Y/N, default Y): " },
			{ Languages.English_GB, "Indicate consecutive substrings of the input letter sequence? (Y/N, default Y): " },
			{ Languages.French, "Indiquer les sous-chaînes consécutives de la séquence de lettres d'entrée ? (O/N, défaut O) : " },
			{ Languages.Turkish, "Giriş harf dizisinin ardışık alt dizilerini belirtin mi? (E/H, varsayılan E): " },
			{ Languages.Spanish, "¿Indicar subcadenas consecutivas de la secuencia de letras de entrada? (S/N, por defecto S): " },
			{ Languages.Italian, "Indicare le sottostringhe consecutive della sequenza di lettere di input? (S/N, predefinito S): " }
		};

		// Realtime output input prompt (multilingual)
		public static readonly Dictionary<Languages, string> RealtimeInputPrompts = new()
		{
			{ Languages.German, "Echtzeit-Ausgabe der Ergebnisse während der Verarbeitung? (J/N, Standard N): " },
			{ Languages.German_AT, "Echtzeit-Ausgabe der Ergebnisse während der Verarbeitung? (J/N, Standard N): " },
			{ Languages.German_CH, "Echtzeit-Ausgabe der Ergebnisse während der Verarbeitung? (J/N, Standard N): " },
			{ Languages.English, "Realtime output of results during processing? (Y/N, default N): " },
			{ Languages.English_US, "Realtime output of results during processing? (Y/N, default N): " },
			{ Languages.English_GB, "Realtime output of results during processing? (Y/N, default N): " },
			{ Languages.French, "Sortie en temps réel des résultats pendant le traitement ? (O/N, défaut N) : " },
			{ Languages.Turkish, "İşlem sırasında sonuçların gerçek zamanlı çıktısı? (E/H, varsayılan H): " },
			{ Languages.Spanish, "¿Salida en tiempo real de los resultados durante el procesamiento? (S/N, por defecto N): " },
			{ Languages.Italian, "Output in tempo reale dei risultati durante l'elaborazione? (S/N, predefinito N): " }
		};

		// ConsoleProgressLogging input prompt (multilingual)
		public static readonly Dictionary<Languages, string> CmdLogProgressInputPrompts = new()
		{
			{ Languages.German, "Konsolen-Fortschrittsanzeige während der Verarbeitung? (J/N, Standard J): " },
			{ Languages.German_AT, "Konsolen-Fortschrittsanzeige während der Verarbeitung? (J/N, Standard J): " },
			{ Languages.German_CH, "Konsolen-Fortschrittsanzeige während der Verarbeitung? (J/N, Standard J): " },
			{ Languages.English, "Console progress logging during processing? (Y/N, default Y): " },
			{ Languages.English_US, "Console progress logging during processing? (Y/N, default Y): " },
			{ Languages.English_GB, "Console progress logging during processing? (Y/N, default Y): " },
			{ Languages.French, "Journalisation de la progression de la console pendant le traitement ? (O/N, défaut O) : " },
			{ Languages.Turkish, "İşlem sırasında konsol ilerleme günlüğü? (E/H, varsayılan E): " },
			{ Languages.Spanish, "¿Registro del progreso de la consola durante el procesamiento? (S/N, por defecto S): " },
			{ Languages.Italian, "Registrazione dei progressi della console durante l'elaborazione? (S/N, predefinito S): " }
		};

		// Threads input prompt (multilingual)
		public static readonly Dictionary<Languages, string> ThreadsInputPrompts = new()
		{
			{ Languages.German, $"Maximale Anzahl paralleler Threads (von max. {Environment.ProcessorCount} Kerne, Standard alle): " },
			{ Languages.German_AT, $"Maximale Anzahl paralleler Threads (von max. {Environment.ProcessorCount} Kerne, Standard alle): " },
			{ Languages.German_CH, $"Maximale Anzahl paralleler Threads (von max. {Environment.ProcessorCount} Kerne, Standard alle): " },
			{ Languages.English, $"Maximum number of parallel threads (of max. {Environment.ProcessorCount} processors, default all): " },
			{ Languages.English_US, $"Maximum number of parallel threads (of max. {Environment.ProcessorCount} processors, default all): " },
			{ Languages.English_GB, $"Maximum number of parallel threads (of max. {Environment.ProcessorCount} processors, default all): " },
			{ Languages.French, $"Nombre maximum de threads parallèles (sur un maximum de {Environment.ProcessorCount} processeurs, par défaut tous) : " },
			{ Languages.Turkish, $"Maksimum paralel iş parçacığı sayısı (maksimum {Environment.ProcessorCount} işlemciden, varsayılan tümü): " },
			{ Languages.Spanish, $"Número máximo de hilos paralelos (de un máximo de {Environment.ProcessorCount} procesadores, por defecto todos): " },
			{ Languages.Italian, $"Numero massimo di thread paralleli (di un massimo di {Environment.ProcessorCount} processori, predefinito tutti): " }
		};

		// Processing start + stop info messages (multilingual)
		public static readonly Dictionary<Languages, string> ProcessingInfoMessages = new()
		{
			{ Languages.German,    ("BERECHNUNG GESTARTET (Drücke Q zum Stoppen)") },
			{ Languages.German_AT, ("BERECHNUNG GESTARTET (Drücke Q zum Stoppen)") },
			{ Languages.German_CH, ("BERECHNUNG GESTARTET (Drücke Q zum Stoppen)") },
			{ Languages.English,   ("PROCESSING STARTED (Press Q to stop)") },
			{ Languages.English_US,("PROCESSING STARTED (Press Q to stop)") },
			{ Languages.English_GB,("PROCESSING STARTED (Press Q to stop)") },
			{ Languages.French,    ("TRAITEMENT DÉMARRÉ (Appuyez sur Q pour arrêter)") },
			{ Languages.Turkish,   ("İŞLEME BAŞLANDI (Durdurmak için Q'ya basın)") },
			{ Languages.Spanish,   ("PROCESAMIENTO INICIADO (Presione Q para detener)") },
			{ Languages.Italian,   ("ELABORAZIONE AVVIATA (Premere Q per interrompere)") }

		};

		// Clipboard copy input prompt (multilingual)
		public static readonly Dictionary<Languages, string> ClipboardInputPrompts = new()
		{
			{ Languages.German, "Ergebnisse in die Zwischenablage kopieren? (J/N, Standard N): " },
			{ Languages.German_AT, "Ergebnisse in die Zwischenablage kopieren? (J/N, Standard N): " },
			{ Languages.German_CH, "Ergebnisse in die Zwischenablage kopieren? (J/N, Standard N): " },
			{ Languages.English, "Copy results to clipboard? (Y/N, default N): " },
			{ Languages.English_US, "Copy results to clipboard? (Y/N, default N): " },
			{ Languages.English_GB, "Copy results to clipboard? (Y/N, default N): " },
			{ Languages.French, "Copier les résultats dans le presse-papiers ? (O/N, défaut N) : " },
			{ Languages.Turkish, "Sonuçları panoya kopyala mı? (E/H, varsayılan H): " },
			{ Languages.Spanish, "¿Copiar resultados al portapapeles? (S/N, por defecto N): " },
			{ Languages.Italian, "Copia i risultati negli appunti? (S/N, predefinito N): " }
		};

		// Clipboard copy success message (multilingual)
		public static readonly Dictionary<Languages, string> ClipboardSuccessMessages = new()
		{
			{ Languages.German, "Ergebnisse wurden in die Zwischenablage kopiert." },
			{ Languages.German_AT, "Ergebnisse wurden in die Zwischenablage kopiert." },
			{ Languages.German_CH, "Ergebnisse wurden in die Zwischenablage kopiert." },
			{ Languages.English, "Results have been copied to the clipboard." },
			{ Languages.English_US, "Results have been copied to the clipboard." },
			{ Languages.English_GB, "Results have been copied to the clipboard." },
			{ Languages.French, "Les résultats ont été copiés dans le presse-papiers." },
			{ Languages.Turkish, "Sonuçlar panoya kopyalandı." },
			{ Languages.Spanish, "Los resultados se han copiado al portapapeles." },
			{ Languages.Italian, "I risultati sono stati copiati negli appunti." }
		};

		// Clipboard copy failure message (multilingual)

		// Internal mapping to hunspell file pairs located in AppContext.BaseDirectory\hunspell\
		private static readonly Dictionary<Languages, (string Aff, string Dic)[]> HunspellFiles = new()
		{
			// German variants (as shown in your VS folder)
			{ Languages.German,    new[] { ("de_DE.aff","de_DE.dic"), ("de_AT.aff","de_AT.dic"), ("de_CH.aff","de_CH.dic") } },
			{ Languages.German_AT, new[] { ("de_AT.aff","de_AT.dic") } },
			{ Languages.German_CH, new[] { ("de_CH.aff","de_CH.dic") } },

			// English variants
			{ Languages.English,    new[] { ("en_US.aff","en_US.dic"), ("en_GB.aff","en_GB.dic") } },
			{ Languages.English_US, new[] { ("en_US.aff","en_US.dic") } },
			{ Languages.English_GB, new[] { ("en_GB.aff","en_GB.dic") } },

			// Others (as in your folder)
			{ Languages.French,  new[] { ("fr.aff","fr.dic") } },
			{ Languages.Turkish,new[] { ("tr_TR.aff","tr_TR.dic") } },
			{ Languages.Spanish,new[] { ("es_ES.aff","es_ES.dic") } },
			{ Languages.Italian,new[] { ("it_IT.aff","it_IT.dic") } },
		};

		private readonly record struct ChainScore(int Pieces, int MaxChain, int TotalChained);

		/// <summary>
		/// Combines input letters into valid dictionary words and (if reuse=false) phrases.
		/// Cancellation: external ct OR user pressing Q (internal token).
		/// Output ranking: best-first by "fewest cuts" (Pieces asc), then MaxChain desc, then TotalChained desc.
		/// If indicateConsecutiveSubstrings=true, result text is decorated with multiple [..] segments via DP.
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
			IProgress<double>? progress = null,
			CancellationToken ct = default)
		{
			// ---- validate / normalize ----
			if (string.IsNullOrWhiteSpace(letters))
			{
				return Array.Empty<string>();
			}

			var langList = (languages?.ToList() ?? new List<Languages>());
			if (langList.Count == 0)
			{
				var sys = TryGetSystemLanguage();
				langList.Add(sys ?? Languages.English);
			}

			// Clamp workers
			int workers = NormalizeWorkers(maxWorkers);

			// Linked cancellation: external + internal (Q) + our own
			using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, Cts.Token);
			var token = linked.Token;

			// Start Q-key watcher (non-blocking)
			using var keyWatcher = StartCancelOnQ(linked);

			var sw = Stopwatch.StartNew();
			long checkedLines = 0;
			long acceptedWords = 0;
			long acceptedPhrases = 0;

			// Progress / speed logger
			using var progressTicker = (cmdLogProgress && !realTimeOutput)
				? StartCmdProgressTicker(() => Volatile.Read(ref checkedLines),
					() => sw.Elapsed, () => Volatile.Read(ref acceptedWords), () => Volatile.Read(ref acceptedPhrases), linked)
				: null;

			// Build pool letters (remove non letters); for sticker texts this is what you want.
			var normalizedLetters = NormalizeLetters(letters, caseSensitive);
			if (normalizedLetters.Length == 0)
			{
				return Array.Empty<string>();
			}

			var letterCounts = CountLetters(normalizedLetters);
			var allowedLetters = new HashSet<char>(letterCounts.Keys);

			// Build original segments for substring indication (from original input, split by non-letters)
			List<string>? originalSegments = indicateConsecutiveSubstrings ? GetOriginalSegments(letters, caseSensitive) : null;
			HashSet<string>? substringSet = indicateConsecutiveSubstrings && originalSegments != null
				? BuildSubstringSet(originalSegments, caseSensitive, maxSubLen: 64)
				: null;

			// Load hunspell handles for all selected languages (merging variants where desired)
			var hunspells = LoadHunspellSet(langList);
			if (hunspells.Count == 0)
			{
				Console.Error.WriteLine("Hunspell dictionaries not found. Make sure 'hunspell' folder is copied next to the executable.");
				return Array.Empty<string>();
			}

			// Collect dictionary paths for the selected languages
            var dictionaryPaths = ResolveDictionaryPaths(langList).Distinct(System.StringComparer.OrdinalIgnoreCase).ToList();
			if (dictionaryPaths.Count == 0)
			{
				Console.Error.WriteLine("No .dic files found for selected languages in hunspell folder.");
				return Array.Empty<string>();
			}

			// We'll collect words and phrases, then rank best-first.
            var wordSet = new ConcurrentDictionary<string, byte>(caseSensitive ? System.StringComparer.Ordinal : System.StringComparer.OrdinalIgnoreCase);
            var phraseSet = new ConcurrentDictionary<string, byte>(caseSensitive ? System.StringComparer.Ordinal : System.StringComparer.OrdinalIgnoreCase);

			// Phase 1: scan dictionaries in parallel and accept words that fit the letter pool
			await Task.Run(() =>
			{
				var po = new ParallelOptions
				{
					CancellationToken = token,
					MaxDegreeOfParallelism = workers
				};

				try
				{
					Parallel.ForEach(dictionaryPaths, po, dicPath =>
					{
						bool isFirstLine = true;

						foreach (var rawLine in File.ReadLines(dicPath))
						{
							po.CancellationToken.ThrowIfCancellationRequested();

							if (string.IsNullOrWhiteSpace(rawLine))
							{
								continue;
							}

							Interlocked.Increment(ref checkedLines);

							if (isFirstLine && rawLine.Trim().All(char.IsDigit))
							{
								isFirstLine = false;
								continue; // skip word count header
							}

							isFirstLine = false;

							var wordPart = rawLine.Split('/', 2)[0];
							var word = caseSensitive ? wordPart : wordPart.ToLowerInvariant();

							if (word.Length < minWordLength)
							{
								continue;
							}

							// Hard cap: if word longer than available letters without reuse => skip quickly
							if (!reuse && word.Length > normalizedLetters.Length)
							{
								continue;
							}

							// Quick letter-fit test (reuse or no-reuse)
							if (!WordFits(word, letterCounts, allowedLetters, reuse))
							{
								continue;
							}

							// Hunspell verify (any of loaded dictionaries)
							if (!hunspells.Any(h => h.Spell(word)))
							{
								continue;
							}

							if (wordSet.TryAdd(word, 0))
							{
								Interlocked.Increment(ref acceptedWords);
							}
						}
					});
				}
				catch (OperationCanceledException)
				{
					// expected; return whatever is collected
				}
			}, token).ConfigureAwait(false);

			// Phase 1 Output (best-first) if realtime requested
			// We can now rank words and print the best ones first.
			var rankedWords = RankBestFirst(wordSet.Keys, substringSet, caseSensitive);

			if (realTimeOutput)
			{
				foreach (var w in rankedWords)
				{
					token.ThrowIfCancellationRequested();
					Console.WriteLine(substringSet != null ? DecorateWithConsecutiveDP(w, substringSet, caseSensitive) : w);
				}
			}

			// Phase 2: phrases (only makes sense for no-reuse)
			if (!reuse && !token.IsCancellationRequested)
			{
				await Task.Run(() =>
				{
					try
					{
						// Use ranked words for better phrase exploration.
						// (This biases toward longer "no-cut" chains early.)
						var candidates = rankedWords.ToList();
						const int maxWordsPerPhrase = 4;

						// For performance, also keep a length-sorted view to prune
						var candidatesByLen = candidates
							.OrderByDescending(w => w.Length)
							.ThenBy(w => w)
							.ToList();

						var remaining = new Dictionary<char, int>(letterCounts);
						var phraseBuffer = new List<string>(capacity: maxWordsPerPhrase);

						void Backtrack(int startIndex, int remainingLetters)
						{
							if (token.IsCancellationRequested)
							{
								return;
							}

							if (phraseBuffer.Count >= maxWordsPerPhrase)
							{
								return;
							}

							if (phraseBuffer.Count >= 2)
							{
								var phrase = string.Join(" ", phraseBuffer);
								if (phraseSet.TryAdd(phrase, 0))
								{
									Interlocked.Increment(ref acceptedPhrases);
								}
							}

							for (int i = startIndex; i < candidatesByLen.Count; i++)
							{
								if (token.IsCancellationRequested)
								{
									return;
								}

								var candidate = candidatesByLen[i];
								if (candidate.Length > remainingLetters)
								{
									continue;
								}

								if (!TryConsumeWord(candidate, remaining, out var consumed))
								{
									continue;
								}

								phraseBuffer.Add(candidate);
								Backtrack(i, remainingLetters - candidate.Length);
								phraseBuffer.RemoveAt(phraseBuffer.Count - 1);
								RestoreConsumed(consumed, remaining);
							}
						}

						Backtrack(0, normalizedLetters.Length);
					}
					catch (OperationCanceledException)
					{
						// expected
					}
				}, token).ConfigureAwait(false);

				var rankedPhrases = RankBestFirst(phraseSet.Keys, substringSet, caseSensitive);

				if (realTimeOutput)
				{
					foreach (var p in rankedPhrases)
					{
						token.ThrowIfCancellationRequested();
						Console.WriteLine(substringSet != null ? DecorateWithConsecutiveDP(p, substringSet, caseSensitive) : p);
					}
				}
			}

			// Stop timers
			progressTicker?.Dispose();

			// Dispose Hunspell
			foreach (var h in hunspells)
			{
				h.Dispose();
			}

			sw.Stop();

			// Final assembly: phrases first, then words (distinct)
            var finalAll = phraseSet.Keys
                .Concat(wordSet.Keys)
                .Distinct(caseSensitive ? System.StringComparer.Ordinal : System.StringComparer.OrdinalIgnoreCase);

			var finalRanked = RankBestFirst(finalAll, substringSet, caseSensitive).ToList();

			// Decorate in final return if indicate enabled
			string[] finalResult;
			if (substringSet != null)
			{
				finalResult = finalRanked
					.Select(x => DecorateWithConsecutiveDP(x, substringSet, caseSensitive))
					.ToArray();
			}
			else
			{
				finalResult = finalRanked.ToArray();
			}

			// Final console summary
			Console.WriteLine();
			Console.WriteLine($"--- Completed {(token.IsCancellationRequested ? "(cancelled)" : "")} ---");
			Console.WriteLine($"Time: {sw.Elapsed}");
			Console.WriteLine($"Checked dictionary lines: {checkedLines:n0}");
			Console.WriteLine($"Accepted words: {acceptedWords:n0}");
			Console.WriteLine($"Accepted phrases: {acceptedPhrases:n0}");
			if (sw.Elapsed.TotalSeconds > 0)
			{
				var rate = checkedLines / sw.Elapsed.TotalSeconds;
				Console.WriteLine($"Scan rate: {rate:n0} lines/sec");
			}

			progress?.Report(1.0);
			return finalResult;
		}

		// ---------------- Ranking (best-first by chain score) ----------------

		private static IEnumerable<string> RankBestFirst(IEnumerable<string> items, HashSet<string>? substringSet, bool caseSensitive)
		{
			if (substringSet == null)
			{
				return items
					.OrderBy(x => x.Count(c => c == ' '))
					.ThenBy(x => x.Length)
					.ThenBy(x => x);
			}

			return items
				.Select(t => (Text: t, Score: ScorePhrase(t, substringSet, caseSensitive)))
				.OrderBy(x => x.Score.Pieces)                 // fewer cuts
				.ThenByDescending(x => x.Score.MaxChain)      // longer best chain
				.ThenByDescending(x => x.Score.TotalChained)  // more chained coverage
				.ThenBy(x => x.Text.Count(c => c == ' '))
				.ThenBy(x => x.Text.Length)
				.ThenBy(x => x.Text)
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
			{
				return new ChainScore(int.MaxValue, 0, 0);
			}

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

				// chains len>=2
				for (int len = 2; i + len <= n; len++)
				{
					var sub = w.Substring(i, len);
					if (!subs.Contains(sub))
					{
						continue;
					}

					if (!has[i + len])
					{
						continue;
					}

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
				if (a.Pieces != b.Pieces)
				{
					return a.Pieces < b.Pieces;
				}

				if (a.MaxChain != b.MaxChain)
				{
					return a.MaxChain > b.MaxChain;
				}

				if (a.TotalChained != b.TotalChained)
				{
					return a.TotalChained > b.TotalChained;
				}

				return false;
			}
		}

		// ---------------- Decoration: DP segmentation -> multiple [..] segments ----------------

		private static string DecorateWithConsecutiveDP(string phrase, HashSet<string> subs, bool caseSensitive)
		{
			var words = phrase.Split(' ', StringSplitOptions.RemoveEmptyEntries);
			var sb = new StringBuilder();

			for (int i = 0; i < words.Length; i++)
			{
				if (i > 0)
				{
					sb.Append(' ');
				}

				sb.Append(DecorateWordDP(words[i], subs, caseSensitive));
			}

			return sb.ToString();
		}

		private static string DecorateWordDP(string word, HashSet<string> subs, bool caseSensitive)
		{
			if (string.IsNullOrEmpty(word) || word.Length < 2)
			{
				return word;
			}

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

				// single char
				if (has[i + 1])
				{
					best = new ChainScore(dp[i + 1].Pieces + 1, dp[i + 1].MaxChain, dp[i + 1].TotalChained);
					bestSet = true;
					bestLen = 1;
					bestChain = false;
				}

				// chains
				for (int len = 2; i + len <= n; len++)
				{
					var sub = target.Substring(i, len);
					if (!subs.Contains(sub))
					{
						continue;
					}

					if (!has[i + len])
					{
						continue;
					}

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
			{
				return word;
			}

			var sb = new StringBuilder();
			int pos = 0;
			while (pos < n)
			{
				int len = nextLen[pos] <= 0 ? 1 : nextLen[pos];
				bool isChain = nextIsChain[pos] && len >= 2;

				if (isChain)
				{
					sb.Append('[');
					sb.Append(word.Substring(pos, len)); // preserve original casing
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
				if (a.Pieces != b.Pieces)
				{
					return a.Pieces < b.Pieces;
				}

				if (a.MaxChain != b.MaxChain)
				{
					return a.MaxChain > b.MaxChain;
				}

				if (a.TotalChained != b.TotalChained)
				{
					return a.TotalChained > b.TotalChained;
				}

				return false;
			}
		}

		// ---------------- Hunspell loading ----------------

		private static List<Hunspell> LoadHunspellSet(IEnumerable<Languages> languages)
		{
			var basePath = Path.Combine(AppContext.BaseDirectory, "hunspell");

			var uniquePairs = new HashSet<(string aff, string dic)>(StringComparer.OrdinalIgnoreCaseTuple());
			foreach (var lang in languages)
			{
				if (!HunspellFiles.TryGetValue(lang, out var pairs))
				{
					continue;
				}

				foreach (var p in pairs)
				{
					uniquePairs.Add((p.Aff, p.Dic));
				}
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

            var set = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
			foreach (var lang in languages)
			{
				if (!HunspellFiles.TryGetValue(lang, out var pairs))
				{
					continue;
				}

				foreach (var (_, dic) in pairs)
				{
					var dicPath = Path.Combine(basePath, dic);
					if (File.Exists(dicPath))
					{
						set.Add(dicPath);
					}
				}
			}

			return set;
		}

		// ---------------- Cancellation + logging ----------------

		private static IDisposable StartCancelOnQ(CancellationTokenSource linked)
		{
			var cts = new CancellationTokenSource();
			var t = Task.Run(async () =>
			{
				try
				{
					while (!cts.IsCancellationRequested && !linked.IsCancellationRequested)
					{
						// Only check if console input exists
						if (Console.KeyAvailable)
						{
							var key = Console.ReadKey(intercept: true);
							if (key.Key == ConsoleKey.Q)
							{
								linked.Cancel();
								return;
							}
						}

						await Task.Delay(50, cts.Token).ConfigureAwait(false);
					}
				}
				catch
				{
					// ignore
				}
			}, cts.Token);

			return new AnonymousDisposable(() =>
			{
				try { cts.Cancel(); } catch { }
				try { t.Wait(200); } catch { }
				cts.Dispose();
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

			var t = Task.Run(async () =>
			{
				long lastChecked = 0;
				var lastTime = TimeSpan.Zero;

				while (!token.IsCancellationRequested)
				{
					var elapsed = getElapsed();
					var checkedNow = getChecked();
					var delta = checkedNow - lastChecked;
					var dt = (elapsed - lastTime).TotalSeconds;
					var rate = (dt > 0) ? (delta / dt) : 0;

					var line = $"Checked: {checkedNow:n0} | Words: {getWords():n0} | Phrases: {getPhrases():n0} | Rate: {rate:n0}/s | Time: {FormatElapsed(elapsed)} | Press 'Q' to cancel";
					WriteProgressLine(line);

					lastChecked = checkedNow;
					lastTime = elapsed;

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
			// overwrite line in console
			try
			{
				if (text.Length >= Console.BufferWidth)
				{
					text = text.Substring(0, Math.Max(0, Console.BufferWidth - 1));
				}

				Console.Write('\r');
				Console.Write(text);
				// Clear remaining chars from previous longer line
				int rest = Math.Max(0, Console.BufferWidth - text.Length - 1);
				if (rest > 0)
				{
					Console.Write(new string(' ', Math.Min(rest, 80)));
				}
			}
			catch
			{
				// ignore if console not interactive
			}
		}

		private static string FormatElapsed(TimeSpan t)
		{
			if (t.TotalHours >= 1)
			{
				return $"{(int) t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}";
			}

			if (t.TotalMinutes >= 1)
			{
				return $"{t.Minutes}:{t.Seconds:00}";
			}

			return $"{t.Seconds}s";
		}

		private sealed class AnonymousDisposable : IDisposable
		{
			private readonly Action _dispose;
			public AnonymousDisposable(Action dispose) => _dispose = dispose;
			public void Dispose() => _dispose();
		}

		// ---------------- Letter helpers ----------------

		private static Dictionary<char, int> CountLetters(string letters)
		{
			var counts = new Dictionary<char, int>();
			foreach (var c in letters)
			{
				if (!counts.ContainsKey(c))
				{
					counts[c] = 0;
				}

				counts[c]++;
			}
			return counts;
		}

		private static string NormalizeLetters(string input, bool caseSensitive)
		{
			var builder = new StringBuilder(input.Length);
			foreach (var c in caseSensitive ? input : input.ToLowerInvariant())
			{
				if (char.IsLetter(c))
				{
					builder.Append(c);
				}
			}
			return builder.ToString();
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
            var set = new HashSet<string>(caseSensitive ? System.StringComparer.Ordinal : System.StringComparer.OrdinalIgnoreCase);

			foreach (var segRaw in segments)
			{
				var seg = caseSensitive ? segRaw : segRaw.ToLowerInvariant();
				if (seg.Length < 2)
				{
					continue;
				}

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

		private static bool WordFits(string word, Dictionary<char, int> available, HashSet<char> allowedLetters, bool reuse)
		{
			if (reuse)
			{
				foreach (var c in word)
				{
					if (!allowedLetters.Contains(c))
					{
						return false;
					}
				}

				return true;
			}

			var remaining = new Dictionary<char, int>(available);
			foreach (var c in word)
			{
				if (!remaining.TryGetValue(c, out var count) || count == 0)
				{
					return false;
				}

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
				if (!needed.ContainsKey(c))
				{
					needed[c] = 0;
				}

				needed[c]++;
			}

			foreach (var kvp in needed)
			{
				if (!remaining.TryGetValue(kvp.Key, out var count) || count < kvp.Value)
				{
					return false;
				}
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
			{
				remaining[letter] = remaining.TryGetValue(letter, out var current) ? current + count : count;
			}
		}

		// ---------------- System language helper ----------------

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
			if (maxWorkers <= 0)
			{
				return cpu;
			}

			if (maxWorkers == 1)
			{
				return 1;
			}

			return Math.Clamp(maxWorkers, 1, cpu);
		}

		// Small helper: case-insensitive tuple comparer for (string,string)
		private static class StringComparer
		{
			public static IEqualityComparer<(string, string)> OrdinalIgnoreCaseTuple() => new TupleComparer();
			private sealed class TupleComparer : IEqualityComparer<(string, string)>
			{
				public bool Equals((string, string) x, (string, string) y)
					=> System.StringComparer.OrdinalIgnoreCase.Equals(x.Item1, y.Item1)
					   && System.StringComparer.OrdinalIgnoreCase.Equals(x.Item2, y.Item2);

				public int GetHashCode((string, string) obj)
				{
					int h1 = System.StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Item1 ?? string.Empty);
					int h2 = System.StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Item2 ?? string.Empty);
					return (h1 * 397) ^ h2;
				}
			}
		}
	}
}
