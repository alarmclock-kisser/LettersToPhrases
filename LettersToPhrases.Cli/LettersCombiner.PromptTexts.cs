using System;
using System.Collections.Generic;
using System.Text;
using static LettersToPhrases.Cli.LettersCombiner;

namespace LettersToPhrases.Cli
{
    public static partial class LettersCombiner
    {
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

        // Try use all letters input prompt (multilingual)
        public static readonly Dictionary<Languages, string> TryUseAllInputPrompts = new()
        {
            { Languages.German, "Möglichst alle Buchstaben verwenden? (J/N, Standard N): " },
            { Languages.German_AT, "Möglichst alle Buchstaben verwenden? (J/N, Standard N): " },
            { Languages.German_CH, "Möglichst alle Buchstaben verwenden? (J/N, Standard N): " },
            { Languages.English, "Try to use all letters? (Y/N, default N): " },
            { Languages.English_US, "Try to use all letters? (Y/N, default N): " },
            { Languages.English_GB, "Try to use all letters? (Y/N, default N): " },
            { Languages.French, "Essayer d'utiliser toutes les lettres ? (O/N, défaut N) : " },
            { Languages.Turkish, "Tüm harfleri kullanmaya çalışın mı? (E/H, varsayılan H): " },
            { Languages.Spanish, "¿Intentar usar todas las letras? (S/N, por defecto N): " },
            { Languages.Italian, "Cerca di usare tutte le lettere? (S/N, predefinito N): " }
        };

        // Filter permutations input prompt (multilingual)
        public static readonly Dictionary<Languages, string> FilterPermutationsInputPrompts = new()
        {
            {   Languages.German, "Permutationen von Wortgruppen herausfiltern? (J/N, Standard J): "   },
            { Languages.German_AT, "Permutationen von Wortgruppen herausfiltern? (J/N, Standard J): " },
            { Languages.German_CH, "Permutationen von Wortgruppen herausfiltern? (J/N, Standard J): " },
            { Languages.English, "Filter permutations of word groups? (Y/N, default Y): " },
            { Languages.English_US, "Filter permutations of word groups? (Y/N, default Y): " },
            { Languages.English_GB, "Filter permutations of word groups? (Y/N, default Y): " },
            { Languages.French, "Filtrer les permutations des groupes de mots ? (O/N, défaut O) : " },
            { Languages.Turkish, "Kelime gruplarının permütasyonlarını filtreleyin mi? (E/H, varsayılan E): " },
            { Languages.Spanish, "¿Filtrar permutaciones de grupos de palabras? (S/N, por defecto S): " },
            { Languages.Italian, "Filtrare le permutazioni dei gruppi di parole? (S/N, predefinito S): " }
        };

        // Indicate consecutive substrings input prompt (multilingual)
        public static readonly Dictionary<Languages, string> IndicateInputPrompts = new()
        {
            { Languages.German, "Aufeinanderfolgende Teilstrings aus Eingabe kennzeichnen? (J/N, Standard J): " },
            { Languages.German_AT, "Aufeinanderfolgende Teilstrings aus Eingabe kennzeichnen? (J/N, Standard J): " },
            { Languages.German_CH, "Aufeinanderfolgende Teilstrings aus Eingabe kennzeichnen? (J/N, Standard J): " },
            { Languages.English, "Indicate consecutive substrings from input? (Y/N, default Y): " },
            { Languages.English_US, "Indicate consecutive substrings from input? (Y/N, default Y): " },
            { Languages.English_GB, "Indicate consecutive substrings from input? (Y/N, default Y): " },
            { Languages.French, "Indiquer les sous-chaînes consécutives de l'entrée ? (O/N, défaut O) : " },
            { Languages.Turkish, "Girişten ardışık alt dizeleri belirtin mi? (E/H, varsayılan E): " },
            { Languages.Spanish, "¿Indicar subcadenas consecutivas de la entrada? (S/N, por defecto S): " },
            { Languages.Italian, "Indicare le sottostringhe consecutive dall'input? (S/N, predefinito S): " }
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

        // Enumerate input prompt (multilingual)
        public static readonly Dictionary<Languages, string> EnumerateInputPrompts = new()
        {
            { Languages.German, "Indices der Ergebnisse anzeigen? (J/N, Standard N): " },
            { Languages.German_AT, "Indices der Ergebnisse anzeigen? (J/N, Standard N): " },
            { Languages.German_CH, "Indices der Ergebnisse anzeigen? (J/N, Standard N): " },
            { Languages.English, "Show indices of results? (Y/N, default N): " },
            { Languages.English_US, "Show indices of results? (Y/N, default N): " },
            { Languages.English_GB, "Show indices of results? (Y/N, default N): " },
            { Languages.French, "Afficher les indices des résultats ? (O/N, défaut N) : " },
            { Languages.Turkish, "Sonuçların dizinlerini göster? (E/H, varsayılan H): " },
            { Languages.Spanish, "¿Mostrar índices de resultados? (S/N, por defecto N): " },
            { Languages.Italian, "Mostra gli indici dei risultati? (S/N, predefinito N): " }
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
            { Languages.German, "Erfolgreich in die Zwischenablage kopiert." },
            { Languages.German_AT, "Erfolgreich in die Zwischenablage kopiert." },
            { Languages.German_CH, "Erfolgreich in die Zwischenablage kopiert." },
            { Languages.English, "Successfully copied to clipboard." },
            { Languages.English_US, "Successfully copied to clipboard." },
            { Languages.English_GB, "Successfully copied to clipboard." },
            { Languages.French, "Copié avec succès dans le presse-papiers." },
            { Languages.Turkish, "Panoya başarıyla kopyalandı." },
            { Languages.Spanish, "Copiado con éxito al portapapeles." },
            { Languages.Italian, "Copiato con successo negli appunti." }
        };

        // Result file written message (multilingual)
        public static readonly Dictionary<Languages, string> ResultFileWrittenMessages = new()
        {
            { Languages.German, "Ergebnisse wurden in TXT-Datei geschrieben: " },
            { Languages.German_AT, "Ergebnisse wurden in TXT-Datei geschrieben: " },
            { Languages.German_CH, "Ergebnisse wurden in TXT-Datei geschrieben: " },
            { Languages.English, "Results have been written to TXT file: " },
            { Languages.English_US, "Results have been written to TXT file: " },
            { Languages.English_GB, "Results have been written to TXT file: " },
            { Languages.French, "Les résultats ont été écrits dans un fichier TXT: " },
            { Languages.Turkish, "Sonuçlar TXT dosyasına yazıldı: " },
            { Languages.Spanish, "Los resultados se han escrito en un archivo TXT: " },
            { Languages.Italian, "I risultati sono stati scritti in un file TXT: " }
        };

        // Result file failure message (multilingual)
        public static readonly Dictionary<Languages, string> ResultFileFailureMessages = new()
        {
            { Languages.German, "Fehler beim Schreiben der TXT-Datei: " },
            { Languages.German_AT, "Fehler beim Schreiben der TXT-Datei: " },
            { Languages.German_CH, "Fehler beim Schreiben der TXT-Datei: " },
            { Languages.English, "Error writing TXT file: " },
            { Languages.English_US, "Error writing TXT file: " },
            { Languages.English_GB, "Error writing TXT file: " },
            { Languages.French, "Erreur lors de l'écriture du fichier TXT : " },
            { Languages.Turkish, "TXT dosyası yazılırken hata: " },
            { Languages.Spanish, "Error al escribir el archivo TXT: " },
            { Languages.Italian, "Errore durante la scrittura del file TXT: " }
        };

        // Editor open results prompt (multilingual)
        public static readonly Dictionary<Languages, string> OpenEditorInputPrompts = new()
        {
            {  Languages.German, "Ergebnisse in Texteditor öffnen? (J/N, Standard N): "  },
            { Languages.German_AT, "Ergebnisse in Texteditor öffnen? (J/N, Standard N): " },
            { Languages.German_CH, "Ergebnisse in Texteditor öffnen? (J/N, Standard N): " },
            { Languages.English, "Open results in text editor? (Y/N, default N): " },
            { Languages.English_US, "Open results in text editor? (Y/N, default N): " },
            { Languages.English_GB, "Open results in text editor? (Y/N, default N): " },
            { Languages.French, "Ouvrir les résultats dans un éditeur de texte ? (O/N, défaut N) : " },
            { Languages.Turkish, "Sonuçları metin düzenleyicisinde açın mı? (E/H, varsayılan H): " },
            { Languages.Spanish, "¿Abrir resultados en el editor de texto? (S/N, por defecto N): " },
            { Languages.Italian, "Apri i risultati nell'editor di testo? (S/N, predefinito N): " }
        };

        // Clipboard copy console output prompt (multilingual)
        public static readonly Dictionary<Languages, string> ClipboardConsolePrompts = new()
        {
            {  Languages.German, "Konsolen-Ausgabe (exkl. Ergebnisse) in die Zwischenablage kopieren? (J/N, Standard N): "  },
            { Languages.German_AT, "Konsolen-Ausgabe (exkl. Ergebnisse) in die Zwischenablage kopieren? (J/N, Standard N): " },
            { Languages.German_CH, "Konsolen-Ausgabe (exkl. Ergebnisse) in die Zwischenablage kopieren? (J/N, Standard N): " },
            { Languages.English, "Copy console output (excl. results) to clipboard? (Y/N, default N): " },
            { Languages.English_US, "Copy console output (excl. results) to clipboard? (Y/N, default N): " },
            { Languages.English_GB, "Copy console output (excl. results) to clipboard? (Y/N, default N): " },
            { Languages.French, "Copier la sortie de la console (excl. les résultats) dans le presse-papiers ? (O/N, défaut N) : " },
            { Languages.Turkish, "Konsol çıktısını (sonuçlar hariç) panoya kopyala mı? (E/H, varsayılan H): " },
            { Languages.Spanish, "¿Copiar la salida de la consola (excluyendo resultados) al portapapeles? (S/N, por defecto N): " },
            { Languages.Italian, "Copia l'output della console (esclusi i risultati) negli appunti? (S/N, predefinito N): " }
        };


    }
}
