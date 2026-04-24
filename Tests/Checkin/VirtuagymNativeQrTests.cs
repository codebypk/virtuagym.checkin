using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Virtuagym.CheckIn.Core.Abstractions;
using Virtuagym.CheckIn.Core.Services;
using Virtuagym.CheckIn.WPF.Services;
using Virtuagym.CheckIn.Core.Models;

namespace Tests.Checkin
{
    /// <summary>
    /// Tests für den Virtuagym-nativen QR-Code-Flow (vg_checkin_qr=...).
    /// Nutzt ForceOffline, damit keine echte API-Verbindung benötigt wird.
    /// </summary>
    [TestClass]
    public class VirtuagymNativeQrTests
    {
        private const string ReaderName = "QR Scanner Test";

        private (CheckinHandler handler, TestLogWriter logger) CreateHandler(string checkinKey = "CS-TEST-KEY")
        {
            var mapping = new CheckinClientMapping
            {
                Name = ReaderName,
                InputType = CheckinClientMapping.InputTypeQrCode,
                CheckinKey = checkinKey
            };
            mapping.EnsureUuid();
            var logger = new TestLogWriter();
            var settings = new WpfAppSettings();
            var soundPlayer = new WpfSoundPlayer(logger);
            var apiFactory = new WpfVirtuagymApiServiceFactory();
            var handler = new CheckinHandler(logger, mapping, null, settings, soundPlayer, apiFactory, null)
            {
                ForceOffline = true
            };
            return (handler, logger);
        }


        [TestMethod]
        public async Task VgQrCode_CardIdPassedToCheckin()
        {
            var (handler, logger) = CreateHandler();
            string expectedCardId = "tGZlvTDLW+kjVv618BjrsbrCUnwhIBKl";
            string qrCode = "vg_checkin_qr=" + expectedCardId;

            await handler.PerformCheckinAsync(qrCode, ReaderName);

            // Der card_id-Wert sollte im Log als RFID-Tag auftauchen (gekürzt auf 20 Zeichen im Detection-Log)
            string truncatedId = expectedCardId.Substring(0, Math.Min(expectedCardId.Length, 20));
            bool cardIdLogged = logger.LogEntries.Exists(e => e.Contains(truncatedId));
            Assert.IsTrue(cardIdLogged,
                $"card_id '{truncatedId}' sollte im Checkin-Log erscheinen.\nLog:\n" + string.Join("\n", logger.LogEntries));
        }

        [TestMethod]
        public async Task NonVgQrCode_NotTreatedAsVgQr()
        {
            var (handler, logger) = CreateHandler();
            string qrCode = "some_other_prefix=abc123";

            await handler.PerformCheckinAsync(qrCode, ReaderName);

            // Sollte NICHT als Virtuagym-QR erkannt werden
            bool vgQrDetected = logger.LogEntries.Exists(e => e.Contains("Log_VgQrDetected") || e.Contains("Virtuagym-QR"));
            Assert.IsFalse(vgQrDetected,
                "Nicht-VG-QR sollte nicht als Virtuagym-QR erkannt werden.\nLog:\n" + string.Join("\n", logger.LogEntries));
        }

        /// <summary>
        /// Einfacher ILogWriter für Tests.
        /// </summary>
        private class TestLogWriter : ILogWriter
        {
            private readonly object _lock = new object();
            public List<string> LogEntries { get; } = new List<string>();
            public List<string> RejectedEntries { get; } = new List<string>();

            public void WriteToLog(string text, int type = 0)
            {
                lock (_lock)
                {
                    LogEntries.Add($"[{type}] {text}");
                }
            }

            public void WriteToRejectedLog(string cardId, string status, string reason)
            {
                lock (_lock)
                {
                    RejectedEntries.Add($"{cardId}|{status}|{reason}");
                }
            }
        }
    }
}
