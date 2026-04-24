using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Virtuagym.API.Cache;
using Virtuagym.API.Services;
using Virtuagym.API.v1.Models;
using Virtuagym.CheckIn.WPF.Properties;

namespace Tests.Utility
{
    [TestClass]
    public class CustomTests
    {
        private static readonly string ApiKey = Settings.Default.VirtuagymApiKey;
        private static readonly string ServerUrl = (Settings.Default.VirtuagymServerUrl ?? "").TrimEnd('/');
        private static readonly string ClubSecret = Settings.Default.VirtuagymClubSecret;
        private static readonly string CheckinApiKey = Settings.Default.MemberCheckinApiV0Key;
        private static readonly string V0Username = Settings.Default.MemberCheckinApiV0Username;
        private static readonly string V0Password = Settings.Default.MemberCheckinApiV0Password;
        private const string CheckinKey = "CS-123-CHECKIN456-";

        private static VirtuagymApiService CreateRealApiService()
        {
            return new VirtuagymApiService(ApiKey, ServerUrl, ClubSecret,
                checkinApiKey: CheckinApiKey,
                checkinClubSecret: CheckinKey,
                v0Username: V0Username,
                v0Password: V0Password);
        }

        private static VirtuagymApiService CreateCheckinApiService(string checkinKey)
        {
            return new VirtuagymApiService(ApiKey, ServerUrl, ClubSecret,
                checkinApiKey: CheckinApiKey,
                checkinClubSecret: checkinKey,
                v0Username: V0Username,
                v0Password: V0Password);
        }

        /// <summary>
        /// Ruft die private Methode InvokeRunAutoCheckoutsAsync per Reflection auf.
        /// </summary>
        private static async Task<int> InvokeRunAutoCheckoutsAsync(
            CacheSyncScheduler scheduler, CancellationToken ct)
        {
            var method = typeof(CacheSyncScheduler).GetMethod(
                "RunAutoCheckoutsAsync",
                BindingFlags.NonPublic | BindingFlags.Instance);

            Assert.IsNotNull(method, "RunAutoCheckoutsAsync sollte als private Methode existieren.");

            var task = (Task<int>)method.Invoke(scheduler, new object[] { ct });
            return await task;
        }

        [TestMethod]
        public async Task RunAutoCheckouts_NoActiveVisitsOnline()
        {
            // Arrange
            var logs = new List<string>();

            using var scheduler = new CacheSyncScheduler(
                cacheService: null,
                apiFactory: CreateRealApiService,
                checkinApiFactory: CreateCheckinApiService);

            scheduler.Log += msg => logs.Add(msg);

            // Device mit fiktiver DeviceId, die keinem echten Gerät entspricht.
            // Dadurch gibt es keine Visits, die diesem Device zugeordnet sind.
            scheduler.SetAutoCheckoutDevices(
            [
                new CacheSyncScheduler.AutoCheckoutDevice
                {
                    DeviceId = "NonExistentTestDevice_" + Guid.NewGuid().ToString("N"),
                    CheckinKey = CheckinKey,
                    AutoCheckoutMinutes = 1, // Sehr kurz, damit ggf. vorhandene Visits als expired gelten
                    ApiVersion = 0
                }
            ]);

            // Act
            int result = await InvokeRunAutoCheckoutsAsync(scheduler, CancellationToken.None);
        }

        #region Visit Cleanup (Temporär)

        // TODO: Cookie-String manuell aus dem Browser kopieren und hier einfügen.


        /// <summary>
        /// Liest alle Visits per V1 API, filtert nach Status und löscht sie per POST an die Web-Oberfläche.
        /// </summary>
        private async Task<(int Deleted, int Failed)> DeleteVisitsByStatusAsync(string status = "rejected")
        {
            long todayStart = new DateTimeOffset(DateTime.UtcNow.AddDays(-2).Date, TimeSpan.Zero).ToUnixTimeMilliseconds();

            using var api = CreateRealApiService();
            var allVisits = await api.Visits.GetAllAsync("sync_from=" + todayStart+"&member_id=XXXXXX", paginate: true);

            var filtered = string.IsNullOrEmpty(status)
                ? allVisits
                : allVisits.Where(v => string.Equals(v.status, status, StringComparison.OrdinalIgnoreCase)).ToList();

            Console.WriteLine($"Gefunden: {allVisits.Count} Visits gesamt, {filtered.Count} mit Status '{status}'.");

            if (filtered.Count == 0)
                return (0, 0);

            string CleanupBaseUrl = "https://abcd-xyz.virtuagym.com/visitorsregistration";
            string CleanupCookie = @"
                    pkce_code_verifier=; vg-user-access-token-v3=;
            ";
            CleanupCookie = CleanupCookie.Replace("\r\n", "");

            int deleted = 0, failed = 0;

            using var http = new HttpClient();
            http.DefaultRequestHeaders.Add("Cookie", CleanupCookie);

            foreach (var visit in filtered)
            {
                try
                {
                    var content = new FormUrlEncodedContent(new Dictionary<string, string>
                    {
                        { "visit_action", "delete_checkin" },
                        { "id", visit.id.ToString() }
                    });
                    
                    var response = await http.PostAsync(CleanupBaseUrl, content);
                    var contentResp = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                    if (response.IsSuccessStatusCode && String.IsNullOrEmpty(contentResp))
                    {
                        deleted++;
                        Console.WriteLine($"  Gelöscht: Visit {visit.id} (Member {visit.member_id}, Status {visit.status})");
                    }
                    else
                    {
                        failed++;
                        Console.WriteLine($"  FEHLER: Visit {visit.id} -> HTTP {(int)response.StatusCode}");
                    }
                }
                catch (Exception ex)
                {
                    failed++;
                    Console.WriteLine($"  EXCEPTION: Visit {visit.id} -> {ex.Message}");
                }
            }

            Console.WriteLine($"Ergebnis: {deleted} gelöscht, {failed} fehlgeschlagen.");
            return (deleted, failed);
        }

        [TestMethod]
        public async Task DeleteRejectedVisits()
        {
            var (deleted, failed) = await DeleteVisitsByStatusAsync("rejected");
            Console.WriteLine($"Deleted: {deleted}, Failed: {failed}");
        }

        #endregion

    }
}