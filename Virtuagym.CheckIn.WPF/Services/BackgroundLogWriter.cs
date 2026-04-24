using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using Virtuagym.CheckIn.Core.Helper;

namespace Virtuagym.CheckIn.WPF.Services
{
    /// <summary>
    /// Schreibt Log-Einträge asynchron über eine Queue auf einen Hintergrund-Thread,
    /// damit Datei-I/O den UI-Thread nicht blockiert.
    /// Implementiert <see cref="IDisposable"/> – beim Dispose wird die Queue geflusht
    /// und der Hintergrund-Thread sauber beendet.
    /// </summary>
    internal sealed class BackgroundLogWriter : IDisposable
    {
        private readonly BlockingCollection<LogEntry> _queue = new BlockingCollection<LogEntry>(1024);
        private readonly Thread _writerThread;
        private bool _disposed;

        public BackgroundLogWriter()
        {
            _writerThread = new Thread(ProcessQueue)
            {
                Name = "BackgroundLogWriter",
                IsBackground = true
            };
            _writerThread.Start();
        }

        /// <summary>
        /// Stellt einen Log-Eintrag in die Queue. Kehrt sofort zurück.
        /// </summary>
        public void Enqueue(string formattedLine, string filePath)
        {
            if (_disposed) return;
            try
            {
                _queue.TryAdd(new LogEntry(formattedLine, filePath));
            }
            catch (InvalidOperationException)
            {
                // Queue wurde bereits abgeschlossen – ignorieren
            }
        }

        private void ProcessQueue()
        {
            foreach (var entry in _queue.GetConsumingEnumerable())
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(entry.FilePath) ?? Constants.LogFolder);
                    File.AppendAllText(entry.FilePath, entry.FormattedLine + Environment.NewLine);
                }
                catch
                {
                    // Fehler beim Schreiben ignorieren – Logging darf die Anwendung niemals crashen
                }
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _queue.CompleteAdding();

            // Warte bis alle verbleibenden Einträge geschrieben sind (max. 3 Sek.)
            _writerThread.Join(TimeSpan.FromSeconds(3));
        }

        private struct LogEntry
        {
            public readonly string FormattedLine;
            public readonly string FilePath;

            public LogEntry(string formattedLine, string filePath)
            {
                FormattedLine = formattedLine;
                FilePath = filePath;
            }
        }
    }
}
