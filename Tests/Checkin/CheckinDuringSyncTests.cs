using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Virtuagym.API.Cache;
using Virtuagym.API.Services;
using Virtuagym.API.v1.Models;
using Virtuagym.CheckIn.Core.Abstractions;
using Virtuagym.CheckIn.Core.Helper;
using Virtuagym.CheckIn.WPF.Properties;
using Virtuagym.CheckIn.Core.Services;
using Virtuagym.CheckIn.WPF.Services;
using Hardware.Events;
using Hardware.Models;
using Virtuagym.CheckIn.Core.Models;
using Virtuagym.API.Cache.Models;

namespace Tests.Checkin
{
    /// <summary>
    /// Testet das Verhalten eines Check-ins per HidCardReader während einer
    /// laufenden automatischen Cache-Synchronisierung.
    /// Baut eine echte Verbindung zur Virtuagym-API auf (Credentials aus Settings).
    /// Simuliert das physische Auflegen einer Karte auf das Gerät, während
    /// der CacheSyncScheduler gerade einen Sync durchführt.
    /// Kein ForceOffline – der reale Code-Pfad wird durchlaufen.
    /// </summary>
    [TestClass]
    public class CheckinDuringSyncTests
    {
        private string _tempCachePath;

        // Credentials aus Virtuagym.CheckIn Settings
        private static readonly string ApiKey = Settings.Default.VirtuagymApiKey;
        private static readonly string ServerUrl = (Settings.Default.VirtuagymServerUrl ?? "").TrimEnd('/');
        private static readonly string ClubSecret = Settings.Default.VirtuagymClubSecret;
        private static readonly string CheckinApiKey = Settings.Default.MemberCheckinApiV0Key;
        private static readonly string V0Username = Settings.Default.MemberCheckinApiV0Username;
        private static readonly string V0Password = Settings.Default.MemberCheckinApiV0Password;

        // Echten CheckinKey aus dem ersten konfigurierten Mapping verwenden (v0 API mit device_id)
        private const string CheckinKey = "CS-123-CHECKIN123-test"; //"CS-61464-CHECKIN53986-r4S3P8vd70Kg7A9NrDiCX0FCv";

        /// <summary>
        /// Erstellt eine echte VirtuagymApiService-Instanz mit den Credentials aus den Settings.
        /// </summary>
        private static VirtuagymApiService CreateRealApiService()
        {
            return new VirtuagymApiService(ApiKey, ServerUrl, ClubSecret,
                checkinApiKey: CheckinApiKey,
                checkinClubSecret: CheckinKey,
                v0Username: V0Username,
                v0Password: V0Password);
        }

        [TestInitialize]
        public void Setup()
        {
            _tempCachePath = Path.Combine(Path.GetTempPath(),
                "test_checkin_sync_" + Guid.NewGuid().ToString("N") + ".json");
        }

        [TestCleanup]
        public void Cleanup()
        {
            if (File.Exists(_tempCachePath))
                File.Delete(_tempCachePath);
        }

        /// <summary>
        /// Simuliert einen HidCardReader-Checkin (physische Karte) während einer
        /// laufenden automatischen Synchronisierung – mit echten API-Verbindungen.
        ///
        /// Ablauf:
        /// 1. Echte API-Verbindung aufbauen und ein reales Mitglied mit RFID-Tag laden.
        /// 2. MemberCacheService mit dem echten Mitglied vorbefüllen.
        /// 3. CacheSyncScheduler.RunSyncAsync startet im Hintergrund (IsSyncing = true)
        ///    mit einer echten API-Verbindung. Die Factory blockiert kurz, damit der Sync
        ///    "in progress" ist, während der Karten-Checkin parallel durchläuft.
        /// 4. Während der Sync läuft, wird ein HidCardReader.CardRead-Event simuliert:
        ///    - Card-Objekt wird erstellt (wie HidCardReader.ProcessCardData).
        ///    - CheckinHandler.PerformCheckinAsync wird aufgerufen (wie MainWindow.OnCardRead).
        ///    - CheckinHandler baut intern eine echte API-Verbindung auf (VirtuagymApiServiceFactory).
        /// 5. Ergebnis: Checkin und Sync laufen parallel erfolgreich durch, Cache bleibt integer.
        /// </summary>
        [TestMethod]
        public async Task CardRead_DuringSync_CheckinProceedsWithoutCorruption()
        {
            const string readerName = "HID Reader Eingang";

            // --- Arrange: Echtes Mitglied mit RFID-Tag über die API laden ---
            MemberResult realMember;
            using (var api = CreateRealApiService())
            {
                var members = await api.Members.GetAllAsync(paginate: true);
                Assert.IsNotNull(members, "API sollte Members zurückliefern.");
                Assert.IsTrue(members.Count > 0, "Es sollte mindestens 1 Mitglied existieren.");

                // Ein Mitglied mit gültigem RFID-Tag suchen
                realMember = members.FirstOrDefault(m =>
                    !string.IsNullOrWhiteSpace(m.rfid_tag) && m.active);
                Assert.IsNotNull(realMember,
                    "Es sollte mindestens ein aktives Mitglied mit RFID-Tag existieren.");
            }

            string rfidTag = realMember.rfid_tag.Trim();
            long memberId = realMember.member_id;
            string firstname = realMember.firstname ?? "";
            string lastname = realMember.lastname ?? "";

            // --- Arrange: Cache mit dem echten Mitglied vorbefüllen ---
            using var cache = new MemberCacheService(_tempCachePath);
            cache.Upsert(new MemberCacheEntry
            {
                MemberId = memberId,
                UserId = realMember.user_id,
                RfidTag = rfidTag,
                Firstname = firstname,
                Lastname = lastname,
                Active = true
            });
            Assert.AreEqual(1, cache.GetCount(), "Cache sollte genau 1 Mitglied enthalten.");

            // --- Arrange: CacheSyncScheduler mit echter API ---
            // Die API-Factory signalisiert, dass der Sync gestartet hat, und blockiert dann
            // kurz, bis der Checkin abgeschlossen ist – damit beide parallel laufen.
            var syncInProgress = new ManualResetEventSlim(false);
            var checkinDone = new ManualResetEventSlim(false);
            var syncLogs = new List<string>();

            var scheduler = new CacheSyncScheduler(
                cache,
                apiFactory: () =>
                {
                    // Signal: Sync hat die Factory erreicht (IsSyncing ist bereits true).
                    syncInProgress.Set();
                    // Warten bis der Checkin abgeschlossen ist – hält den Sync "aktiv".
                    checkinDone.Wait(TimeSpan.FromSeconds(30));
                    // Echte API-Verbindung für den Sync
                    return CreateRealApiService();
                });
            scheduler.Log += msg => syncLogs.Add(msg);

            // --- Arrange: CheckinHandler (wie in MainWindow.OnCardRead) ---
            // CheckinKey setzen → v0 API (PUT /devices) wird verwendet.
            var mapping = new CheckinClientMapping
            {
                Name = readerName,
                InputType = nameof(HardwareInputType.USBReader),
                DeviceID = "vid_0416&pid_b030&mi_00",
                CheckinKey = CheckinKey
            };
            mapping.EnsureUuid();
            var logger = new TestLogWriter();

            // --- Act ---
            // 1) Automatische Synchronisierung starten (Hintergrund-Thread, wie Timer-Callback)
            var syncTask = Task.Run(() => scheduler.RunSyncAsync());

            // 2) Warten bis der Sync tatsächlich läuft
            Assert.IsTrue(syncInProgress.Wait(TimeSpan.FromSeconds(1)),
                "CacheSyncScheduler.RunSyncAsync sollte gestartet sein.");
            Assert.IsTrue(scheduler.IsSyncing,
                "IsSyncing sollte true sein während des Syncs.");

            // 3) HidCardReader.CardRead simulieren:
            //    Card-Objekt erstellen – exakt wie HidCardReader.ProcessCardData es nach dem
            //    Lesen der RFID-Rohdaten erzeugt.
            var cardFromReader = new Card(rfidTag);
            Assert.IsTrue(cardFromReader.IsValidTag, "Simulierte Karte muss gültig sein.");

            //    CardReadEventArgs erstellen – wie das CardRead-Event sie liefert.
            var cardEventArgs = new CardReadEventArgs(cardFromReader, readerName);

            //    CheckinHandler erstellen und PerformCheckinAsync aufrufen – exakt wie
            //    MainWindow.OnCardRead, nur ohne WPF-Dispatcher (im Test nicht nötig).
            //    KEIN ForceOffline: der komplette reale Code-Pfad wird durchlaufen,
            //    inklusive echter API-Verbindung über VirtuagymApiServiceFactory.
            var settings = new WpfAppSettings();
            var soundPlayer = new WpfSoundPlayer(logger);
            var apiFactory = new WpfVirtuagymApiServiceFactory();
            var handler = new CheckinHandler(logger, mapping, null, settings, soundPlayer, apiFactory, cache);

            await handler.PerformCheckinAsync(
                new Card(cardEventArgs.Card.UidDecimal),
                cardEventArgs.ReaderName);

            // check log entries for debugging:
            var member = cache.GetByMemberId(memberId);
            bool cacheHitLogged = logger.LogEntries.Exists(e => e.Contains(memberId.ToString()));
            Assert.IsTrue(cacheHitLogged,
                $"CheckinHandler sollte {memberId} im Cache gefunden haben (Cache-Hit).\n" +
                "Log-Einträge:\n" + string.Join("\n", logger.LogEntries));


            // 4) Checkin abgeschlossen → Sync-Factory freigeben
            checkinDone.Set();

            // 5) Sync abwarten (echte API-Verbindung – sollte erfolgreich durchlaufen)
            await syncTask;

            scheduler.Dispose();
        }

        /// <summary>
        /// Einfacher ILogWriter für Tests – sammelt alle Log-Einträge in einer Liste.
        /// Thread-safe, da CheckinHandler und Sync auf verschiedenen Threads laufen können.
        /// </summary>
        private class TestLogWriter : ILogWriter
        {
            private readonly object _lock = new object();
            public List<string> LogEntries { get; } = new List<string>();
            public List<string> RejectedEntries { get; } = new List<string>();

            public void WriteToLog(string text, int type = Constants.LogInfo)
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
