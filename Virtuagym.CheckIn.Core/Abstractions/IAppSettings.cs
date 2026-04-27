namespace Virtuagym.CheckIn.Core.Abstractions;

/// <summary>
/// Abstraction for application settings.
/// Decouples services from the concrete settings store (WPF Settings.Default / Web appsettings.json).
/// </summary>
public interface IAppSettings
{
    // --- General ---
    bool DebugMode { get; }
    string AppLanguage { get; }

    // --- Virtuagym API ---
    string VirtuagymApiKey { get; }
    string VirtuagymServerUrl { get; }
    string VirtuagymClubSecret { get; }
    string VirtuagymProfileImageUrl { get; }

    // --- Member Checkin API v0 ---
    string MemberCheckinApiV0Key { get; }
    string MemberCheckinApiV0Username { get; }
    string MemberCheckinApiV0Password { get; }

    // --- Jablotron ---
    string JablotronApiUrl { get; }
    string JablotronApiUsername { get; }
    string JablotronApiPassword { get; }

    // --- Hardware ---
    int RepeatTimeInMs { get; }
    int DuplicateTimeoutSeconds { get; }
    long DefaultDoubleScanThresholdMs { get; }

    // --- Member Cache ---
    int CreditsCacheTtlMinutes { get; }

    // --- Sounds ---
    string SoundCheckinSuccess { get; }
    string SoundCheckinWarn { get; }
    string SoundCheckinError { get; }
    string SoundCheckinDoubleScan { get; }

    // --- API Message Overrides ---
    string ApiMsgCheckinSuccess { get; }
    string ApiMsgCheckoutSuccess { get; }
    string ApiMsgCheckinFailed { get; }
    string ApiMsgCheckoutFailed { get; }
    string ApiMsgMemberNotFoundByRfid { get; }
    string ApiMsgDoubleScanBlocked { get; }
    string ApiMsgInsufficientCredits { get; }
    string ApiMsgMemberNotActive { get; }

    // --- Access Pass Deletion ---
    AccessPass.Models.AccessPassDeletionMode AccessPassDeletionMode { get; }
    int AccessPassDeletionDays { get; }
}
