using Virtuagym.CheckIn.Core.Abstractions;

namespace Virtuagym.CheckIn.Web.Models;

/// <summary>
/// Application settings bound to <c>appsettings.json</c>.
/// Replaces WPF <c>Settings.Default</c> for the web project.
/// </summary>
public sealed class AppSettings : IAppSettings
{
    // --- General ---
    public bool DebugMode { get; set; }
    public string AppLanguage { get; set; } = "de";

    // --- Virtuagym API ---
    public string VirtuagymApiKey { get; set; } = "";
    public string VirtuagymServerUrl { get; set; } = "https://virtuagym.com/api/";
    public string VirtuagymClubSecret { get; set; } = "";
    public string VirtuagymProfileImageUrl { get; set; } = "https://static.virtuagym.com/v29524004/thumb/userpic/m/";

    // --- Member Checkin API v0 ---
    public string MemberCheckinApiV0Key { get; set; } = "";
    public string MemberCheckinApiV0Username { get; set; } = "";
    public string MemberCheckinApiV0Password { get; set; } = "";

    // --- Jablotron ---
    public string JablotronApiUrl { get; set; } = "https://api.jablonet.net/api/2.4";
    public string JablotronApiUsername { get; set; } = "";
    public string JablotronApiPassword { get; set; } = "";
    public string JablotronApiPinCode { get; set; } = "";

    // --- Welcome Screen ---
    public bool ShowClubLogo { get; set; }
    public string ClubLogoPath { get; set; } = "";
    public int WelcomeIdleTimeoutMs { get; set; } = 5000;
    public int BackgroundRotationIntervalMs { get; set; } = 10000;
    public bool ShowQrCameraPreviewOnWelcome { get; set; } = true;

    // --- Welcome Screen Texts ---
    public string IdleHintQrCode { get; set; } = "Bitte scannen Sie Ihren QR-Code";
    public string IdleHintRfid { get; set; } = "Bitte halten Sie Ihre Karte an das Lesegerät";
    public string IdleHintRfidAndQrCode { get; set; } = "Bitte halten Sie Ihre Karte an das Lesegerät oder scannen Sie Ihren QR-Code";
    public string CheckinSuccessText { get; set; } = "Check-in erfolgreich";
    public string CheckinNotFoundText { get; set; } = "Nicht gefunden";
    public string CheckinUnknownErrorText { get; set; } = "Unbekannter Fehler";

    // --- API Messages ---
    public string ApiMsgCheckinSuccess { get; set; } = "{0} wurde eingecheckt.";
    public string ApiMsgCheckoutSuccess { get; set; } = "{0} wurde ausgecheckt.";
    public string ApiMsgCheckinFailed { get; set; } = "Check-in fehlgeschlagen.";
    public string ApiMsgCheckoutFailed { get; set; } = "Check-out fehlgeschlagen.";
    public string ApiMsgMemberNotFoundByRfid { get; set; } = "Es wurde kein Mitglied mit der Kartennummer {0} gefunden.";
    public string ApiMsgDoubleScanBlocked { get; set; } = "Der letzte Check-in liegt weniger als {0} zurück, der Check-out wird übersprungen.";
    public string ApiMsgInsufficientCredits { get; set; } = "Nicht genügend Guthaben für {0}.";
    public string ApiMsgMemberNotActive { get; set; } = "Das Mitglied {0} ist nicht aktiv.";

    // --- Hardware ---
    public int RepeatTimeInMs { get; set; } = 400;
    public int DuplicateTimeoutSeconds { get; set; } = 5;
    public long DefaultDoubleScanThresholdMs { get; set; } = 60000;
    /// <summary>
    /// Polling interval for hotplug device discovery (seconds).
    /// </summary>
    public int DeviceHotplugPollIntervalSeconds { get; set; } = 15;

    // --- Member Cache ---
    public bool MemberCacheEnabled { get; set; } = true;
    public bool CacheSyncEnabled { get; set; } = true;
    public int CacheSyncIntervalMinutes { get; set; } = 480;
    public string CacheSyncMode { get; set; } = "Interval";
    public string CacheSyncDailyTime { get; set; } = "02:00";
    public int CreditsCacheTtlMinutes { get; set; } = 60;

    /// <summary>
    /// Regex pattern to extract credit amount from v0 API employee_message.
    /// Must contain one capture group for the numeric value. Empty = disabled.
    /// </summary>
    public string CreditParsePattern { get; set; } = @"(\d+)\s*Guthabenpunkte";

    // --- Sounds ---
    public string SoundCheckinSuccess { get; set; } = "sounds/login_succes.wav";
    public string SoundCheckinWarn { get; set; } = "sounds/notification.wav";
    public string SoundCheckinError { get; set; } = "sounds/error.wav";
    public string SoundCheckinDoubleScan { get; set; } = "sounds/login_try.wav";

    // --- Logging ---
    public int LogRetentionDays { get; set; } = 30;

    // --- Access Pass Deletion ---
    public AccessPass.Models.AccessPassDeletionMode AccessPassDeletionMode { get; set; } = AccessPass.Models.AccessPassDeletionMode.Immediately;
    public int AccessPassDeletionDays { get; set; } = 0;

    // --- Checkin Mappings (JSON string) ---
    public string CheckinClientMappings { get; set; } = "[]";
}
