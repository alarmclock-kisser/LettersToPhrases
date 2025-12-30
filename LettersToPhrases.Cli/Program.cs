using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace LettersToPhrases.Cli
{
    internal class Program
    {
        private const string LettersPrompt = "Bitte Buchstabenkette eingeben und mit Enter bestätigen: ";

        static async Task<int> Main(string[] args)
        {
            string? letters = null;
            var reuse = false;
            var caseSensitive = false;
            var realTimeOutput = true;
            var maxPhrases = 1024;
            var indicateConsecutiveSubstrings = false;
            var noLimit = false;
            LettersCombinator.Languages? language = null;

            foreach (var arg in args)
            {
                var lower = arg.ToLowerInvariant();
                switch (lower)
                {
                    case "--reuse":
                        reuse = true;
                        continue;
                    case "--noreuse":
                        reuse = false;
                        continue;
                    case "--casesensitive":
                        caseSensitive = true;
                        continue;
                    case "--ignorecase":
                        caseSensitive = false;
                        continue;
                    case "--realtime":
                        realTimeOutput = true;
                        continue;
                    case "--norealtime":
                        realTimeOutput = false;
                        continue;
                    case "--indicate":
                    case "--consecutive":
                        indicateConsecutiveSubstrings = true;
                        continue;
                    case "--noindicate":
                    case "--noconsecutive":
                        indicateConsecutiveSubstrings = false;
                        continue;
                    case "--nolimit":
                        noLimit = true;
                        realTimeOutput = true;
                        continue;
                    case "--de":
                        language = LettersCombinator.Languages.German;
                        continue;
                    case "--en":
                        language = LettersCombinator.Languages.English;
                        continue;
                    case "--gb":
                    case "--en-gb":
                    case "--en_gb":
                        language = LettersCombinator.Languages.EnglishGb;
                        continue;
                    case "--us":
                    case "--en-us":
                    case "--en_us":
                        language = LettersCombinator.Languages.EnglishUs;
                        continue;
                    case "--fr":
                        language = LettersCombinator.Languages.French;
                        continue;
                }

                if (arg.StartsWith("--max", StringComparison.OrdinalIgnoreCase))
                {
                    var parts = arg.Split('=', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length == 2 && int.TryParse(parts[1], out var parsedMax))
                    {
                        maxPhrases = parsedMax;
                        continue;
                    }
                }

                // Erste nicht-Option als Letters
                if (letters == null && !lower.StartsWith("--", StringComparison.Ordinal))
                {
                    letters = arg;
                }
            }

            var cultureLanguage = DetectLanguageFromCulture();
            var promptLanguage = language ?? cultureLanguage ?? LettersCombinator.Languages.German;

            if (string.IsNullOrWhiteSpace(letters))
            {
                Console.Write(GetLettersPrompt(promptLanguage));
                letters = Console.ReadLine() ?? string.Empty;
            }

            if (string.IsNullOrWhiteSpace(letters))
            {
                PrintUsage();
                return 1;
            }

            language ??= cultureLanguage ?? promptLanguage;

            if (noLimit)
            {
                maxPhrases = int.MaxValue;
            }

            Console.WriteLine($"Letters: {letters}");
            Console.WriteLine($"Reuse letters: {reuse}");
            Console.WriteLine($"Case sensitive: {caseSensitive}");
            Console.WriteLine($"Real-time output: {realTimeOutput}");
            Console.WriteLine($"No limit: {noLimit}");
            Console.WriteLine($"Indicate consecutive substrings: {indicateConsecutiveSubstrings}");
            Console.WriteLine($"Language: {language?.ToString() ?? "auto"}");
            Console.WriteLine($"Max phrases: {(noLimit ? "unlimited" : maxPhrases.ToString())}");
            Console.WriteLine();

            var phrases = await LettersCombinator.GetPhrasesFromLettersAsync(
                letters,
                caseSensitive,
                reuse,
                language,
                maxPhrases,
                realTimeOutput,
                indicateConsecutiveSubstrings,
                progress: null);

            Console.WriteLine($"--- Completed ({phrases.Count()} phrase(s)) ---");
            if (!realTimeOutput)
            {
                foreach (var phrase in phrases)
                {
                    Console.WriteLine(phrase);
                }
            }

            return 0;
        }

        private static string GetLettersPrompt(LettersCombinator.Languages language)
        {
            return language switch
            {
                LettersCombinator.Languages.English => "Please enter the string of letters and press Enter: ",
                LettersCombinator.Languages.EnglishGb => "Please enter the string of letters and press Enter: ",
                LettersCombinator.Languages.EnglishUs => "Please enter the string of letters and press Enter: ",
                LettersCombinator.Languages.French => "Veuillez saisir la chaîne de lettres puis appuyer sur Entrée : ",
                _ => "Bitte Buchstabenkette eingeben und mit Enter bestätigen: "
            };
        }

        private static LettersCombinator.Languages? DetectLanguageFromCulture()
        {
            var twoLetter = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.ToLowerInvariant();
            return twoLetter switch
            {
                "de" => LettersCombinator.Languages.German,
                "en" => LettersCombinator.Languages.English,
                "fr" => LettersCombinator.Languages.French,
                _ => null
            };
        }

        private static void PrintUsage()
        {
            Console.WriteLine("Usage: LettersToPhrases.Cli [letters] [--reuse|--noreuse] [--casesensitive|--ignorecase] [--realtime|--norealtime] [--indicate|--noindicate] [--nolimit] [--de|--en|--en-gb|--gb|--en-us|--us|--fr] [--max=<number>]");
            Console.WriteLine("Ohne [letters] wirst du zur Eingabe aufgefordert.");
            Console.WriteLine("Example: LettersToPhrases.Cli abcdef --reuse --en --max=50 --realtime --indicate");
        }
    }
}
