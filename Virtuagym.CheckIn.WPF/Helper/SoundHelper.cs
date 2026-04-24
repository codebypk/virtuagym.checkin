using System;
using System.IO;
using System.Media;
using Virtuagym.CheckIn.Core.Helper;
using Virtuagym.CheckIn.Core.Abstractions;

namespace Virtuagym.CheckIn.WPF.Helper
{
    /// <summary>
    /// Zentrale Hilfsklasse für das Abspielen von Sound-Dateien.
    /// Ersetzt die duplizierten PlaySound-Methoden in MainWindow, CheckinHandler und RfidReaderVirtuagym.
    /// </summary>
    public static class SoundHelper
    {
        /// <summary>
        /// Spielt eine WAV-Datei relativ zum Anwendungsverzeichnis ab.
        /// </summary>
        /// <param name="relativePath">Relativer Pfad zur WAV-Datei (z.B. "resources\error.wav").</param>
        /// <param name="logger">Optionaler Logger für Fehlermeldungen.</param>
        public static void Play(string relativePath, ILogWriter logger = null)
        {
            try
            {
                string fullPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, relativePath);
                if (File.Exists(fullPath))
                {
                    using (var player = new SoundPlayer(fullPath))
                    {
                        player.Play();
                    }
                }
            }
            catch (Exception ex)
            {
                logger?.WriteToLog($"Sound-Fehler: {ex.Message}", Constants.LogWarning);
            }
        }
    }
}
