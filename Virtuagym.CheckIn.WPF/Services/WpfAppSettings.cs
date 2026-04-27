using Virtuagym.CheckIn.Core.Abstractions;
using Virtuagym.CheckIn.WPF.Properties;

namespace Virtuagym.CheckIn.WPF.Services;

/// <summary>
/// WPF implementation of <see cref="IAppSettings"/> that delegates to <see cref="Settings.Default"/>.
/// </summary>
public class WpfAppSettings : IAppSettings
{
    public bool DebugMode => Settings.Default.DebugMode;
    public string AppLanguage => Settings.Default.AppLanguage;

    public string VirtuagymApiKey => Settings.Default.VirtuagymApiKey;
    public string VirtuagymServerUrl => Settings.Default.VirtuagymServerUrl;
    public string VirtuagymClubSecret => Settings.Default.VirtuagymClubSecret;
    public string VirtuagymProfileImageUrl => Settings.Default.VirtuagymProfileImageUrl;

    public string MemberCheckinApiV0Key => Settings.Default.MemberCheckinApiV0Key;
    public string MemberCheckinApiV0Username => Settings.Default.MemberCheckinApiV0Username;
    public string MemberCheckinApiV0Password => Settings.Default.MemberCheckinApiV0Password;

    public string JablotronApiUrl => Settings.Default.JablotronApiUrl;
    public string JablotronApiUsername => Settings.Default.JablotronApiUsername;
    public string JablotronApiPassword => Settings.Default.JablotronApiPassword;

    public int RepeatTimeInMs => Settings.Default.RepaitTimeInMs;
    public int DuplicateTimeoutSeconds => Settings.Default.DuplicateTimeoutSeconds;
    public long DefaultDoubleScanThresholdMs => Settings.Default.DefaultDoubleScanThresholdMs;

    public int CreditsCacheTtlMinutes => Settings.Default.CreditsCacheTtlMinutes;

    public string SoundCheckinSuccess => Settings.Default.SoundCheckinSuccess;
    public string SoundCheckinWarn => Settings.Default.SoundCheckinWarn;
    public string SoundCheckinError => Settings.Default.SoundCheckinError;
    public string SoundCheckinDoubleScan => Settings.Default.SoundCheckinDoubleScan;

    public string ApiMsgCheckinSuccess => Settings.Default.ApiMsgCheckinSuccess;
    public string ApiMsgCheckoutSuccess => Settings.Default.ApiMsgCheckoutSuccess;
    public string ApiMsgCheckinFailed => Settings.Default.ApiMsgCheckinFailed;
    public string ApiMsgCheckoutFailed => Settings.Default.ApiMsgCheckoutFailed;
    public string ApiMsgMemberNotFoundByRfid => Settings.Default.ApiMsgMemberNotFoundByRfid;
    public string ApiMsgDoubleScanBlocked => Settings.Default.ApiMsgDoubleScanBlocked;
    public string ApiMsgInsufficientCredits => Settings.Default.ApiMsgInsufficientCredits;
    public string ApiMsgMemberNotActive => Settings.Default.ApiMsgMemberNotActive;

    // --- Access Pass Deletion ---
    public AccessPass.Models.AccessPassDeletionMode AccessPassDeletionMode => (AccessPass.Models.AccessPassDeletionMode)Settings.Default.AccessPassDeletionMode;
    public int AccessPassDeletionDays => Settings.Default.AccessPassDeletionDays;
}
