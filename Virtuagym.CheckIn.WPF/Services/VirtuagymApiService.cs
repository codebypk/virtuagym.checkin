using Virtuagym.CheckIn.WPF.Properties;
using ApiService = Virtuagym.API.Services.VirtuagymApiService;

namespace Virtuagym.CheckIn.WPF.Services
{
    /// <summary>
    /// Factory für den Virtuagym API-Service.
    /// Erstellt Instanzen basierend auf den gespeicherten Anwendungseinstellungen (Settings.Default).
    /// </summary>
    public static class VirtuagymApiServiceFactory
    {
        /// <summary>
        /// Erstellt den Service basierend auf den gespeicherten Einstellungen.
        /// </summary>
        public static ApiService Create()
        {
            string apiKey = Settings.Default.VirtuagymApiKey;
            string apiBaseUrl = (Settings.Default.VirtuagymServerUrl ?? "").TrimEnd('/');
            string clubSecret = Settings.Default.VirtuagymClubSecret;
            string checkinApiKey = Settings.Default.MemberCheckinApiV0Key;
            string username = Settings.Default.MemberCheckinApiV0Username;
            string password = Settings.Default.MemberCheckinApiV0Password;

            return new ApiService(apiKey, apiBaseUrl, clubSecret,checkinApiKey: checkinApiKey, v0Username: username, v0Password: password);
        }

        /// <summary>
        /// Erstellt den Service für Check-in-Anfragen.
        /// Verwendet die Standard-Credentials für alle Services, aber beim MemberCheckIn (v0 API)
        /// wird der MemberCheckinApiV0Key als ApiKey und das übergebene Club-Secret (vom Device) verwendet.
        /// </summary>
        public static ApiService CreateWithClubSecret(string deviceClubSecret)
        {
            string apiKey = Settings.Default.VirtuagymApiKey;
            string apiBaseUrl = (Settings.Default.VirtuagymServerUrl ?? "").TrimEnd('/');
            string clubSecret = Settings.Default.VirtuagymClubSecret;
            string checkinApiKey = Settings.Default.MemberCheckinApiV0Key;
            string username = Settings.Default.MemberCheckinApiV0Username;
            string password = Settings.Default.MemberCheckinApiV0Password;

            return new ApiService(apiKey, apiBaseUrl, clubSecret,checkinApiKey: checkinApiKey, checkinClubSecret: deviceClubSecret,
                v0Username: username, v0Password: password);
        }

        /// <summary>
        /// Erstellt den Service mit expliziten Parametern (für Verbindungstest im Settings-Dialog).
        /// </summary>
        public static ApiService Create(string apiKey, string serverUrl, string clubSecret)
        {
            string apiBaseUrl = serverUrl.TrimEnd('/');
            string checkinApiKey = Settings.Default.MemberCheckinApiV0Key;
            string username = Settings.Default.MemberCheckinApiV0Username;
            string password = Settings.Default.MemberCheckinApiV0Password;

            return new ApiService(apiKey, apiBaseUrl, clubSecret, checkinApiKey: checkinApiKey, v0Username: username, v0Password: password);
        }
    }
}
