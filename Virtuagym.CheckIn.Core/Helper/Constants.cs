namespace Virtuagym.CheckIn.Core.Helper
{
    /// <summary>
    /// Central constants for the application.
    /// </summary>
    public static class Constants
    {
        #region Application

        public const string AppTitle = "Virtuagym Member Check-In";
        public const string MutexName = "VirtuagymCheckIn_SingleInstance";
        public const string IconPath = "resources\\Main.ico";
        #endregion

        #region Logging

        /// <summary>Log type: Information.</summary>
        public const int LogInfo = 1;
        /// <summary>Log type: Warning.</summary>
        public const int LogWarning = 2;
        /// <summary>Log type: Error (written to file).</summary>
        public const int LogError = 3;
        /// <summary>Log type: Success.</summary>
        public const int LogSuccess = 4;

        public const string LogFileSuffix = "_VirtuagymMemberCheckIn.log";
        public const string LogDateFormat = "ddMMyyyy";
        public const string LogTimestampFormat = "dd.MM.yyyy HH:mm:ss";
        public const int MaxLogEntries = 20;

        /// <summary>Filename for rejected check-in attempts (card IDs that were not found).</summary>
        public const string RejectedCheckinLogFile = "rejected_checkins.log";

        #endregion

        #region CSV Export

        public const string CsvExportDateFormat = "yyyyMMdd_HHmmss";

        #endregion

        #region Paths

        /// <summary>Local folder for welcome screen background images.</summary>
        public const string BackgroundsFolder = "resources\\backgrounds";
        public const string LanguageFilesFolder = "resources\\lang";
        public const string ClubServiceFilePath = "resources\\data\\club_services.json";
        public const string JablotronGatesFilePath = "resources\\data\\jablotron_gates.json";
        public const string LogFolder = "logs";
        #endregion

        #region NotifyIcon

        public const string BalloonTipText = "Click the tray icon to show.";
        public const int BalloonTipTimeoutMs = 2000;

        #endregion

        #region Welcome Screen

        /// <summary>Interval in ms for the welcome screen foreground check.</summary>
        public const int WelcomeForegroundCheckIntervalMs = 500;

        #endregion


        #region Status

        /// <summary>Check-in status: Success.</summary>
        public const string StatusOk = "ok";
        /// <summary>Check-in status: Warning (e.g. expired subscription).</summary>
        public const string StatusWarn = "warn";
        /// <summary>Check-in status: Rejected.</summary>
        public const string StatusReject = "reject";
        /// <summary>Check-in status: Double scan (already checked in, re-scanned within threshold).</summary>
        public const string StatusDoubleScan = "doublescan";

        #endregion
    }
}
