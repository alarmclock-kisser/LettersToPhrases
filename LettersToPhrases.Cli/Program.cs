using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using TextCopy;

namespace LettersToPhrases.Cli
{
	internal class Program
	{
		static async Task Main(string[] args)
		{
			// ---- Args:
			// string letters (directly after executable call, no -- (option flag), just raw string)
			// ---- Options:
			// --en, --de, --fr (language(s) to use, default: null -> auto get from system culture), can be multiple
			// --casesensitive XOR --ignorecase (default if none: ignore case)
			// --reuse XOR --noreuse (default if none: NO-reuse)
			// --minlength <int> (default if not specified: 2)
			// --indicateconsecutive XOR --noindicate (default if none: indicate)
			// --realtime XOR --silent (default if none: no real-time output (silent (only reports combinations tried count and rate every second in cmd (replace line instead of new line))))
			// --threads <int> XOR --allthreads XOR --singlethread (default if not specified or <=0: all available processors)

			string? letters = null;
			bool dialog = false; // new: interactive prompt for missing options

			var langs = new List<LettersCombiner.Languages>();
			bool caseSensitive = false; // default ignore case
			bool reuse = false;          // default noreuse
			int minLen = 2;
			bool indicate = true;       // default indicate
			bool realtime = false;      // default silent-ish
			bool cmdLogProgress = true; // only used if !realtime
			int threads = 0;            // default all

			// Parse args: first non-flag becomes letters, others are flags/options
			// Track which options were explicitly set via args
			bool caseSpecified = false;
			bool reuseSpecified = false;
			bool minLenSpecified = false;
			bool indicateSpecified = false;
			bool realtimeSpecified = false;
			bool threadsSpecified = false;
			bool cmdLogSpecified = false;

			for (int i = 0; i < args.Length; i++)
			{
				var a = args[i];

				if (!a.StartsWith("--", StringComparison.Ordinal))
				{
					if (letters == null)
					{
						letters = a;
					}

					continue;
				}

				var lower = a.ToLowerInvariant();

				switch (lower)
				{
					case "--dialog":
						dialog = true;
						continue;
					case "--de":
						langs.Add(LettersCombiner.Languages.German);
						continue;
					case "--de-at":
					case "--de_at":
						langs.Add(LettersCombiner.Languages.German_AT);
						continue;
					case "--de-ch":
					case "--de_ch":
						langs.Add(LettersCombiner.Languages.German_CH);
						continue;

					case "--en":
						langs.Add(LettersCombiner.Languages.English);
						continue;
					case "--en-us":
					case "--en_us":
					case "--us":
						langs.Add(LettersCombiner.Languages.English_US);
						continue;
					case "--en-gb":
					case "--en_gb":
					case "--gb":
						langs.Add(LettersCombiner.Languages.English_GB);
						continue;

					case "--fr":
						langs.Add(LettersCombiner.Languages.French);
						continue;
					case "--tr":
						langs.Add(LettersCombiner.Languages.Turkish);
						continue;
					case "--es":
						langs.Add(LettersCombiner.Languages.Spanish);
						continue;
					case "--it":
						langs.Add(LettersCombiner.Languages.Italian);
						continue;

					case "--casesensitive":
						caseSensitive = true;
						caseSpecified = true;
						continue;
					case "--ignorecase":
						caseSensitive = false;
						caseSpecified = true;
						continue;

					case "--reuse":
						reuse = true;
						reuseSpecified = true;
						continue;
					case "--noreuse":
						reuse = false;
						reuseSpecified = true;
						continue;

					case "--indicateconsecutive":
					case "--indicate":
					case "--consecutive":
						indicate = true;
						indicateSpecified = true;
						continue;
					case "--noindicate":
					case "--noindicateconsecutive":
					case "--noconsecutive":
						indicate = false;
						indicateSpecified = true;
						continue;

					case "--realtime":
						realtime = true;
						cmdLogProgress = false;
						realtimeSpecified = true;
						continue;
					case "--silent":
						realtime = false;
						cmdLogProgress = true;
						realtimeSpecified = true;
						cmdLogSpecified = true;
						continue;

					case "--allthreads":
						threads = 0;
						threadsSpecified = true;
						continue;
					case "--singlethread":
						threads = 1;
						threadsSpecified = true;
						continue;

					case "--minlength":
						if (i + 1 < args.Length && int.TryParse(args[i + 1], out var ml))
						{
							minLen = Math.Max(1, ml);
							minLenSpecified = true;
							i++;
						}
						continue;

					case "--threads":
						if (i + 1 < args.Length && int.TryParse(args[i + 1], out var th))
						{
							threads = th;
							threadsSpecified = true;
							i++;
						}
						continue;

					case "--help":
					case "-h":
					case "/?":
					case "--usage":
						PrintUsage();
						return;
				}
			}

			// Pre log headline
			Console.WriteLine(" --=-----=----=---=--=-=   Letters to Phrases CLI   =-=--=---=----=-----=-- ");
			Console.WriteLine();
			// Version / Build + Environment info like: V<x>.<y>.<z> (Debug/Release) + Date of build (if available, try get from assembly info or actually assembly .exe fileinfo last modified e.g)
			Console.WriteLine($" Version: {GetBuildVersion()?.ToString() ?? "unknown"} ({GetBuildEnvironment() ?? "unknown build"}) - Built on: {GetBuildDate()?.ToString("yyyy-MM-dd-HH:mm:ss") ?? "unknown date"}");
			// dotnet-version and OS environment + architecture + arm or x86
			Console.WriteLine($" .NET{Environment.Version} | OS: {Environment.OSVersion} ({(Environment.Is64BitOperatingSystem ? "64" : "32")}-bit {RuntimeInformation.ProcessArchitecture.ToString()})");
			// Headline end
			Console.WriteLine(" ~~~>>~~~>>~~>~>   -- -- --   -- --   <~ ~ ~>   -- --   -- -- --   <~<~~<<~~~<<~~~ ");
			Console.WriteLine();

			// Prompt for letters if not provided
			if (string.IsNullOrWhiteSpace(letters))
			{
				// choose prompt language
				var promptLang = langs.Count > 0 ? langs[0] : LettersCombiner.Languages.English;
				if (LettersCombiner.LettersInputPrompts.TryGetValue(promptLang, out var prompt))
				{
					Console.Write(prompt);
				}
				else
				{
					Console.Write("Please enter letters: ");
				}

				letters = Console.ReadLine();
			}

			if (string.IsNullOrWhiteSpace(letters))
			{
				PrintUsage();
				return;
			}

			// Interactive dialog for unspecified options
			if (dialog)
			{
				var promptLang = langs.Count > 0 ? langs[0] : LettersCombiner.Languages.English;

				// Case sensitive
				if (!caseSpecified)
				{
					if (LettersCombiner.CaseInputPrompts.TryGetValue(promptLang, out var casePrompt))
					{
						Console.Write(casePrompt);
						var inp = Console.ReadLine();
						if (!string.IsNullOrWhiteSpace(inp))
						{
							caseSensitive = IsYes(inp);
						}
					}
					else
					{
						Console.Write("Case sensitive? (Y/N, default N): ");
						var inp = Console.ReadLine();
						if (!string.IsNullOrWhiteSpace(inp))
						{
							caseSensitive = IsYes(inp);
						}
					}
				}

				// Reuse
				if (!reuseSpecified)
				{
					if (LettersCombiner.ReuseInputPrompts.TryGetValue(promptLang, out var reusePrompt))
					{
						Console.Write(reusePrompt);
						var inp = Console.ReadLine();
						if (!string.IsNullOrWhiteSpace(inp))
						{
							reuse = IsYes(inp);
						}
					}
					else
					{
						Console.Write("Reuse letters? (Y/N, default Y): ");
						var inp = Console.ReadLine();
						if (!string.IsNullOrWhiteSpace(inp))
						{
							reuse = IsYes(inp);
						}
					}
				}

				// Min length
				if (!minLenSpecified)
				{
					if (LettersCombiner.LengthInputPrompts.TryGetValue(promptLang, out var lenPrompt))
					{
						Console.Write(lenPrompt);
						var inp = Console.ReadLine();
						if (int.TryParse(inp, out var ml))
						{
							minLen = Math.Max(1, ml);
						}
					}
					else
					{
						Console.Write("Minimum word length (default 2): ");
						var inp = Console.ReadLine();
						if (int.TryParse(inp, out var ml))
						{
							minLen = Math.Max(1, ml);
						}
					}
				}

				// Indicate consecutive
				if (!indicateSpecified)
				{
					if (LettersCombiner.IndicateInputPrompts.TryGetValue(promptLang, out var indicatePrompt))
					{
						Console.Write(indicatePrompt);
					}
					else
					{
						Console.Write("Indicate consecutive substrings? (Y/N, default Y): ");
					}
				}

				// Realtime
				if (!realtimeSpecified)
				{
					if (LettersCombiner.RealtimeInputPrompts.TryGetValue(promptLang, out var realtimePrompt))
					{
						Console.Write(realtimePrompt);
						var inp = Console.ReadLine();
						if (!string.IsNullOrWhiteSpace(inp))
						{
							realtime = IsYes(inp);
							cmdLogProgress = !realtime; // align default if changed here
						}
					}
					else
					{
						Console.Write("Realtime output? (Y/N, default N): ");
					}

				}

				// Cmd progress (only if not realtime)
				if (!realtime && !cmdLogSpecified)
				{
					if (LettersCombiner.CmdLogProgressInputPrompts.TryGetValue(promptLang, out var cmdLogPrompt))
					{
						Console.Write(cmdLogPrompt);
						var inp = Console.ReadLine();
						if (!string.IsNullOrWhiteSpace(inp))
						{
							cmdLogProgress = IsYes(inp);
						}
					}
					else
					{
						Console.Write("Log progress to command line? (Y/N, default Y): ");
						var inp = Console.ReadLine();
						if (!string.IsNullOrWhiteSpace(inp))
						{
							cmdLogProgress = IsYes(inp);
						}
					}
				}

				// Threads
				if (!threadsSpecified)
				{
					int maxCpu = Environment.ProcessorCount;
					if (LettersCombiner.ThreadsInputPrompts.TryGetValue(promptLang, out var threadsPrompt))
					{
						Console.Write(string.Format(threadsPrompt, maxCpu));
						var inp = Console.ReadLine();
						if (int.TryParse(inp, out var th))
						{
							threads = th;
						}
					}
					else
					{
						Console.Write($"Number of threads (1 - singlethread, 0 - all available ({maxCpu}), default 0): ");
						var inp = Console.ReadLine();
						if (int.TryParse(inp, out var th))
						{
							threads = th;
						}
					}

				}
			}

			// Show all settings if no --dialog, else only show settings set via args (since others were just prompted and are visible in console still)
			Console.WriteLine();
			Console.WriteLine(" --=-----=----=---=--=-=   Args | Options | Settings   =-=--=---=----=-----=-- ");
			Console.WriteLine();
			Console.WriteLine($" - Letters: {letters}");
			if (langs.Count > 0)
			{
				Console.WriteLine($" - Languages: {string.Join(", ", langs)}");
			}
			else
			{
				Console.WriteLine(" - Languages: (auto-detect from system culture)");
			}
			if (caseSpecified || dialog)
			{
				Console.WriteLine($" - Case Sensitive: {caseSensitive}");
			}
			if (reuseSpecified || dialog)
			{
				Console.WriteLine($" - Reuse Letters: {reuse}");
			}
			if (minLenSpecified || dialog)
			{
				Console.WriteLine($" - Minimum Word Length: {minLen}");
			}
			if (indicateSpecified || dialog)
			{
				Console.WriteLine($" - Indicate Consecutive Substrings: {indicate}");
			}
			if (realtimeSpecified || dialog)
			{
				Console.WriteLine($" - Realtime Output: {realtime}");
			}
			if (threadsSpecified || dialog)
			{
				Console.WriteLine($" - Threads: {(threads <= 0 ? "All Available" : threads.ToString())}");
			}
			Console.WriteLine();
			string processingMsg = LettersCombiner.ProcessingInfoMessages.TryGetValue(langs.Count > 0 ? langs[0] : LettersCombiner.Languages.English, out var procMsg) ? procMsg : "PROCESSING START (Q to stop)...";
			Console.WriteLine($" ~~~>>~~~>>~~>~>   -- -- --   -- --   <~ {processingMsg} ~>   -- --   -- -- --   <~<~~<<~~~<<~~~ ");
			Console.WriteLine();

			// Run
			var results = await LettersCombiner.CombineLettersAsync(
				letters,
				languages: (langs.Count == 0 ? null : langs),
				caseSensitive: caseSensitive,
				reuse: reuse,
				minWordLength: minLen,
				indicateConsecutiveSubstrings: indicate,
				realTimeOutput: realtime,
				cmdLogProgress: cmdLogProgress,
				maxWorkers: threads,
				progress: null,
				ct: CancellationToken.None);

			Console.WriteLine();
			Console.WriteLine($">---)   Σ = {results.Length:n0}   (---<");

			// If realtime is on, it already printed ranked output as it went,
			// but we still show a compact final list if user wants.
			if (!realtime)
			{
				foreach (var r in results)
				{
					Console.WriteLine(r);
				}
			}
			// Clipboard prompt (TextCopy preferred)
			Console.WriteLine();
			if (LettersCombiner.ClipboardInputPrompts.TryGetValue(
				langs.Count > 0 ? langs[0] : LettersCombiner.Languages.English,
				out var clipboardPrompt))
			{
				Console.Write(clipboardPrompt);
			}
			else
			{
				Console.Write("Copy results to clipboard? (Y/N default N): ");
			}
			ConsoleKeyInfo key = Console.ReadKey(intercept: true);
			Console.WriteLine();

			if (key.Key == ConsoleKey.Y)
			{
				var text = string.Join(Environment.NewLine, results);
				TextCopy.ClipboardService.SetText(text);
				if (LettersCombiner.ClipboardSuccessMessages.TryGetValue(
					langs.Count > 0 ? langs[0] : LettersCombiner.Languages.English,
					out var clipboardSuccessPrompt))
				{
					Console.WriteLine(clipboardSuccessPrompt);
				}
				else
				{
					Console.WriteLine("Results copied to clipboard.");
				}
			}
		}

		private static bool IsYes(string input)
		{
			if (string.IsNullOrWhiteSpace(input))
			{
				return false;
			}

			var s = input.Trim();
			if (s.Length == 0)
			{
				return false;
			}

			s = s.ToLowerInvariant();
			// accept common yes letters across supported languages
			return s is "y" or "yes" or "j" or "ja" or "o" or "oui" or "s" or "si" or "e";
		}

		private static void PrintUsage()
		{
			Console.WriteLine("Usage: LettersToPhrases.Cli [letters] [options]");
			Console.WriteLine();
			Console.WriteLine("Options:");
			Console.WriteLine("  --de | --de-at | --de-ch | --en | --en-us | --en-gb | --fr | --tr | --es | --it   Languages (multiple allowed)");
			Console.WriteLine("  --casesensitive | --ignorecase                     Case handling (default: ignorecase)");
			Console.WriteLine("  --reuse | --noreuse                                Reuse letters (default: reuse)");
			Console.WriteLine("  --minlength <int>                                  Minimum word length (default: 2)");
			Console.WriteLine("  --indicateconsecutive | --noindicate                Mark [consecutive substrings] (default: indicate)");
			Console.WriteLine("  --realtime | --silent                               Output mode (default: silent)");
			Console.WriteLine("  --threads <int> | --allthreads | --singlethread     Parallelism (default: allthreads)");
			Console.WriteLine();
			Console.WriteLine("Cancel anytime with 'Q' (partial results are returned).");
		}



		public static Version? GetBuildVersion()
		{
			// Try-catch returns a Version object representing the build version or null
			Version? ver = null;
			try
			{
				var assembly = System.Reflection.Assembly.GetExecutingAssembly();
				ver = assembly.GetName().Version;
			}
			catch
			{
				// Ignore exceptions and return null
			}

			return ver;
		}

		public static string? GetBuildEnvironment()
		{
			// Try-catch returns a string like "Debug" or "Release" based on build configuration or null
			string? env = null;
			try
			{
				// NOT VIA HASHTAG-IF DEBUG, do reflection or asembly attribute check instead
				var assembly = System.Reflection.Assembly.GetExecutingAssembly();
				var attributes = assembly.GetCustomAttributes(typeof(System.Diagnostics.DebuggableAttribute), false);
				if (attributes.Length > 0)
				{
					var dbgAttr = (System.Diagnostics.DebuggableAttribute)attributes[0];
					env = dbgAttr.IsJITTrackingEnabled ? "Debug" : "Release";
				}
			}
			catch
			{
				// Ignore exceptions and return null
			}

			return env;
		}

		public static DateTime? GetBuildDate()
		{
			// Try-catch returns a DateTime object representing the build date or null, also try get from assembly file info if not embedded
			DateTime? date = null;
			try
			{
				var assembly = System.Reflection.Assembly.GetExecutingAssembly();
				var filePath = assembly.Location;
				if (System.IO.File.Exists(filePath))
				{
					date = System.IO.File.GetLastWriteTime(filePath);
				}
			}
			catch
			{
				// Ignore exceptions and return null
			}

			return date;
		}


	}
}
