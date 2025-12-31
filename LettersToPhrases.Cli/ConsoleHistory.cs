using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

public sealed class ConsoleHistory : IDisposable
{
    private readonly TextWriter _original;
    private readonly HistoryWriter _tee;

    /// <param name="maxLinesInMemory">How many (non-skipped) lines to keep in RAM (ring-like trimming).</param>
    /// <param name="logFilePath">Optional file path to tee output to a file.</param>
    /// <param name="skipResultPrefix">Lines starting with this prefix are NOT stored in history (but still printed).</param>
    public ConsoleHistory(int maxLinesInMemory = 65536, string? logFilePath = null, string skipResultPrefix = " >>> ", string skipReportPrefix = " ### ")
    {
        this._original = Console.Out;
        this._tee = new HistoryWriter(this._original, maxLinesInMemory, logFilePath, skipResultPrefix, skipResultPrefix);
        Console.SetOut(this._tee);
    }

    public IReadOnlyList<string> GetAllLines()
    {
        var lines = this._tee.GetLinesSnapshot().ToList();

        // If _tee has any skipped lines, add a summary line at the end (count)
        if (this._tee.CombinationFoundLines > 0)
        {
            lines.Add("");
            lines.Add($"[... {this._tee.CombinationFoundLines} combination found lines skipped ...]");
            return lines;
        }

        return lines;
    }

    public void Dispose()
    {
        Console.SetOut(this._original);
        this._tee.Dispose();
    }

    private sealed class HistoryWriter : TextWriter
    {
        private readonly TextWriter _a;
        private readonly object _lock = new();
        private readonly int _maxLines;
        private readonly List<string> _lines = [];
        private readonly StringBuilder _current = new();
        private readonly StreamWriter? _file;
        private readonly string _skipResultPrefix;
        private readonly string _skipReportPrefix;
        private int _resultFoundLines = 0;
        public int CombinationFoundLines => this._resultFoundLines;

        public HistoryWriter(TextWriter a, int maxLines, string? logFilePath, string skipResultPrefix, string skipReportPrefix)
        {
            this._a = a;
            this._maxLines = Math.Max(1, maxLines);
            this._skipResultPrefix = skipResultPrefix ?? string.Empty;
            this._skipReportPrefix = skipReportPrefix ?? string.Empty;

            if (!string.IsNullOrWhiteSpace(logFilePath))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(logFilePath)!);
                this._file = new StreamWriter(File.Open(logFilePath, FileMode.Append, FileAccess.Write, FileShare.Read))
                {
                    AutoFlush = true
                };
            }
        }

        public override Encoding Encoding => this._a.Encoding;

        public override void Write(char value)
        {
            lock (this._lock)
            {
                this._a.Write(value);
                this._file?.Write(value);

                if (value == '\n')
                {
                    this.CommitLine_NoLock();
                }
                else if (value != '\r')
                {
                    this._current.Append(value);
                }
            }
        }

        public override void Write(string? value)
        {
            if (value == null)
            {
                return;
            }

            lock (this._lock)
            {
                this._a.Write(value);
                this._file?.Write(value);

                for (int i = 0; i < value.Length; i++)
                {
                    char c = value[i];
                    if (c == '\n')
                    {
                        this.CommitLine_NoLock();
                    }
                    else if (c != '\r')
                    {
                        this._current.Append(c);
                    }
                }
            }
        }

        public override void WriteLine(string? value)
        {
            lock (this._lock)
            {
                this._a.WriteLine(value);
                this._file?.WriteLine(value);

                if (!string.IsNullOrEmpty(value))
                {
                    this._current.Append(value);
                }

                this.CommitLine_NoLock();
            }
        }

        private void CommitLine_NoLock()
        {
            var line = this._current.ToString();
            this._current.Clear();

            // Skip storing certain realtime-result lines
            if (!string.IsNullOrEmpty(this._skipResultPrefix) && line.StartsWith(this._skipResultPrefix, StringComparison.Ordinal))
            {
                // Increment result found counter
                this._resultFoundLines++;
                return;
            }
            // Also skip certain report lines
            if (!string.IsNullOrEmpty(this._skipReportPrefix) && line.StartsWith(this._skipReportPrefix, StringComparison.Ordinal))
            {
                // Don't count these in the result found counter
                return;
            }

            this._lines.Add(line);

            // Trim old lines (keep last _maxLines)
            if (this._lines.Count > this._maxLines)
            {
                this._lines.RemoveRange(0, this._lines.Count - this._maxLines);
            }
        }

        public IReadOnlyList<string> GetLinesSnapshot()
        {
            lock (this._lock)
            {
                // Include current (unfinished) line only if it wouldn't be skipped.
                var snap = new List<string>(this._lines);
                if (this._current.Length > 0)
                {
                    var cur = this._current.ToString();
                    if (string.IsNullOrEmpty(this._skipResultPrefix) || !cur.StartsWith(this._skipResultPrefix, StringComparison.Ordinal))
                    {
                        snap.Add(cur);
                    }
                }
                return snap;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                this._file?.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
