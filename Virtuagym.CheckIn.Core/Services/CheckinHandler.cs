using System;
using System.Threading;
using System.Threading.Tasks;
using AccessPass.Models;
using AccessPass.Services;
using Virtuagym.API;
using Virtuagym.API.Models;
using Virtuagym.API.Qr;
using Virtuagym.API.Services;
using Virtuagym.CheckIn.Core.Abstractions;
using Virtuagym.CheckIn.Core.Helper;
using Virtuagym.CheckIn.Core.Models;
using Hardware.Models;
using Virtuagym.API.Cache.Models;

namespace Virtuagym.CheckIn.Core.Services;

/// <summary>
/// Handles check-in/check-out operations via RFID tag or QR code.
/// Platform-independent – uses <see cref="IWelcomeDisplay"/>, <see cref="ISoundPlayer"/>,
/// <see cref="IAppSettings"/> and <see cref="IVirtuagymApiServiceFactory"/> abstractions.
/// </summary>
public class CheckinHandler
{
    private readonly ILogWriter _logger;
    private readonly IWelcomeDisplay? _welcomeDisplay;
    private readonly IAppSettings _settings;
    private readonly ISoundPlayer _soundPlayer;
    private readonly IVirtuagymApiServiceFactory _apiFactory;
    private readonly CheckinClientMapping _mapping;
    private readonly MemberCacheService? _memberCache;
    private readonly CheckinHardwareTriggerService _hardwareTrigger;
    private readonly CheckinCreditService _creditService;
    private readonly IAccessPassService? _accessPassService;

    /// <summary>
    /// When true, all API calls are skipped and the offline fallback is used.
    /// </summary>
    public bool ForceOffline { get; set; }

    public CheckinHandler(
        ILogWriter logger,
        CheckinClientMapping mapping,
        IWelcomeDisplay? welcomeDisplay,
        IAppSettings settings,
        ISoundPlayer soundPlayer,
        IVirtuagymApiServiceFactory apiFactory,
        MemberCacheService? memberCache = null,
        IAccessPassService? accessPassService = null)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(mapping);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(soundPlayer);
        ArgumentNullException.ThrowIfNull(apiFactory);
        _logger = logger;
        _mapping = mapping;
        _welcomeDisplay = welcomeDisplay;
        _settings = settings;
        _soundPlayer = soundPlayer;
        _apiFactory = apiFactory;
        _memberCache = memberCache;
        _accessPassService = accessPassService;
        _hardwareTrigger = new CheckinHardwareTriggerService(logger, mapping, settings);
        _creditService = new CheckinCreditService(logger, settings, apiFactory, memberCache);
    }

    private static CachedMemberInfo? ToCachedMemberInfo(MemberCacheEntry? entry)
    {
        if (entry == null) return null;
        return new CachedMemberInfo
        {
            MemberId = entry.MemberId,
            UserId = entry.UserId,
            RfidTag = entry.RfidTag,
            Firstname = entry.Firstname,
            Lastname = entry.Lastname,
            Avatar = entry.AvatarUrl,
            Active = entry.Active,
            TimestampEdit = entry.TimestampEdit,
            DeviceCheckins = entry.DeviceCheckins,
            ServiceCredits = entry.ServiceCredits,
            CreditsLastSyncTimestamp = entry.CreditsLastSyncTimestamp
        };
    }

    /// <summary>
    /// Performs a check-in via QR code (encrypted or native Virtuagym QR).
    /// </summary>
    public async Task PerformCheckinAsync(string encryptedQrCode, string? sourceName = null, CancellationToken cancellationToken = default)
    {
        string displayName = ResolveDisplayName(sourceName);

        // 1) Try access pass (offline, local)
        if (_accessPassService != null)
        {
            var accessResult = _accessPassService.TryResolve(encryptedQrCode, _settings.VirtuagymClubSecret.Trim());
            if (accessResult != null)
            {
                HandleAccessPassResult(accessResult, displayName, cancellationToken);
                return;
            }
        }

        // 2) Virtuagym native QR
        if (VirtuagymNativeQr.IsMatch(encryptedQrCode))
        {
            if (!VirtuagymNativeQr.TryExtractCardId(encryptedQrCode, out string vgCardId))
            {
                _logger.WriteToLog($"[{displayName}] {L.T("Log_VgQrEmpty")}", Constants.LogWarning);
                _soundPlayer.Play(_settings.SoundCheckinError);
                _welcomeDisplay?.ShowCheckinResult(Constants.StatusReject, "", null,
                    new[] { L.T("QR_InvalidOrManipulated") }, displayName);
                return;
            }

            _logger.WriteToLog($"[{displayName}] {L.T("Log_VgQrDetected")}: {vgCardId.Substring(0, Math.Min(vgCardId.Length, 20))}...");
            await PerformCheckinAsync(Card.FromRawValue(encryptedQrCode), sourceName, null, cancellationToken);
            return;
        }

        // 3) Unknown QR format
        _logger.WriteToLog($"[{displayName}] {L.T("Log_QrNotEncrypted")}", Constants.LogError);
        _soundPlayer.Play(_settings.SoundCheckinError);
        _welcomeDisplay?.ShowCheckinResult(Constants.StatusReject, "", null,
            new[] { L.T("QR_InvalidOrManipulated") }, displayName);
    }

    private void HandleAccessPassResult(AccessPassResult result, string displayName, CancellationToken cancellationToken)
    {
        if (result.Success)
        {
            string actionText = result.Action == "checkout" ? L.T("Log_ActionCheckout") : L.T("Log_ActionCheckin");
            _logger.WriteToLog($"[{displayName}] AccessPass {actionText}: {result.DisplayName} ({result.RemainingUses}/{result.TotalUses})", Constants.LogSuccess);
            _soundPlayer.Play(_settings.SoundCheckinSuccess);
            _welcomeDisplay?.ShowCheckinResult(Constants.StatusOk, result.DisplayName ?? "", result.AvatarPath,
                result.Messages, displayName);
            _hardwareTrigger.TriggerRelayIfEnabled(displayName);
            _hardwareTrigger.TriggerPgGateIfEnabled(displayName, cancellationToken);
        }
        else
        {
            _logger.WriteToLog($"[{displayName}] AccessPass rejected: {result.Error} – {string.Join(" | ", result.Messages)}", Constants.LogWarning);
            _soundPlayer.Play(_settings.SoundCheckinError);
            _welcomeDisplay?.ShowCheckinResult(Constants.StatusReject, result.DisplayName ?? "", result.AvatarPath,
                result.Messages, displayName);
        }
    }



    /// <summary>
    /// Performs a check-in/check-out via RFID tag.
    /// </summary>
    public async Task PerformCheckinAsync(Card rfidTag, string? sourceName = null, CachedMemberInfo? precachedMember = null, CancellationToken cancellationToken = default)
    {
        string displayName = ResolveDisplayName(sourceName);
        CachedMemberInfo? cachedMember = ResolveCachedMember(rfidTag, precachedMember, displayName);

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            _logger.WriteToLog($"[{displayName}] {L.T("Log_ToggleCheckinStarted")} RFID '{rfidTag}' ...");
            _welcomeDisplay?.ShowLoader();

            if (RejectIfMemberInactive(cachedMember, displayName))
                return;

            if (await RejectIfInsufficientCreditsAsync(cachedMember, displayName))
                return;

            if (ForceOffline)
                throw new System.Net.Http.HttpRequestException(L.T("Log_ForceOfflineSimulated"));

            var result = await ExecuteCheckinToggleAsync(rfidTag, cachedMember);
            cachedMember = await UpdateCacheAfterCheckinAsync(result, rfidTag, cachedMember, displayName);

            if (result.Success)
                HandleCheckinSuccess(result, displayName, cancellationToken, cachedMember);
            else
                HandleCheckinFailure(result, rfidTag, displayName);
        }
        catch (OperationCanceledException)
        {
            _logger.WriteToLog($"[{displayName}] {L.T("Log_CheckinCancelled")}", Constants.LogWarning);
        }
        catch (VirtuagymApiException apiEx) when (apiEx.ApiStatusCode == 400)
        {
            long memberId2 = cachedMember?.MemberId ?? 0;
            string deviceId2 = _mapping.EffectiveDeviceId;
            _memberCache?.UpdateVisitTimestamps(memberId2, 0, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), deviceId2, apiVersion: _mapping.ApiVersion);
        }
        catch (VirtuagymApiException apiEx)
        {
            _logger.WriteToLog($"{L.T("Log_ApiError")} '{rfidTag}': {apiEx.ApiStatusMessage} (Code: {apiEx.ApiStatusCode})", Constants.LogError);
            _logger.WriteToRejectedLog(rfidTag.GetCardId(_mapping.CardIdModeEnum), $"api_error_{apiEx.ApiStatusCode}", apiEx.ApiStatusMessage);
            _soundPlayer.Play(_settings.SoundCheckinError);
            _welcomeDisplay?.ShowCheckinResult(Constants.StatusReject, "", null, new[] { $"{L.T("Log_Error")}: {apiEx.ApiStatusMessage}" }, displayName);
        }
        catch (Exception ex)
        {
            _logger.WriteToLog($"{L.T("Log_ApiError")} '{rfidTag}': {ex.Message}", Constants.LogError);
            HandleOfflineFallback(ex, rfidTag, cachedMember, displayName, cancellationToken);
        }
    }

    #region Checkin Helper Methods

    private string ResolveDisplayName(string? sourceName)
    {
        return sourceName
            ?? (!string.IsNullOrWhiteSpace(_mapping.Name) ? _mapping.Name : "CheckinHandler");
    }

    private static string GetMemberFullName(CachedMemberInfo? member)
    {
        if (member == null) return "";
        return ((member.Firstname ?? "") + " " + (member.Lastname ?? "")).Trim();
    }

    private CachedMemberInfo? ResolveCachedMember(Card rfidTag, CachedMemberInfo? precachedMember, string displayName)
    {
        if (precachedMember != null)
            return precachedMember;

        if (_memberCache == null)
            return null;

        var cached = _memberCache.GetByRfidTag(rfidTag.GetCardId(_mapping.CardIdModeEnum));
        if (cached != null)
        {
            _logger.WriteToLog($"[{displayName}] {L.T("Log_CacheHit")}: {cached.DisplayName} (ID: {cached.MemberId})");
            return ToCachedMemberInfo(cached);
        }

        _logger.WriteToLog($"[{displayName}] {L.T("Log_CacheMiss")} RFID '{rfidTag}'.");
        return null;
    }

    private bool RejectIfMemberInactive(CachedMemberInfo? cachedMember, string displayName)
    {
        if (cachedMember == null || cachedMember.Active)
            return false;

        string memberName = GetMemberFullName(cachedMember);
        string notActiveMsg = string.Format(
            ApiMessageOverrides.Resolve(_settings.ApiMsgMemberNotActive, ApiConstants.MsgMemberNotActive),
            memberName);
        _logger.WriteToLog($"[{displayName}] {L.T("Log_MemberNotActive")}: {cachedMember.MemberId} ({memberName})", Constants.LogWarning);
        _soundPlayer.Play(_settings.SoundCheckinError);
        _welcomeDisplay?.ShowCheckinResult(Constants.StatusReject, memberName,
            cachedMember.Avatar, new[] { notActiveMsg }, displayName);
        return true;
    }

    private async Task<bool> RejectIfInsufficientCreditsAsync(CachedMemberInfo? cachedMember, string displayName)
    {
        bool requireCredits = !string.IsNullOrWhiteSpace(_mapping.CreditServiceId);
        if (!requireCredits || cachedMember == null || cachedMember.MemberId == 0)
            return false;

        string serviceId = _mapping.CreditServiceId ?? "";
        int clubId = _mapping.CreditClubId;

        await _creditService.RefreshIfStaleAsync(cachedMember, serviceId, clubId, _mapping.CheckinKey, displayName);

        if (!_creditService.HasInsufficientCredits(cachedMember, serviceId, out var serviceName, out var creditAmount, out var minCreditsRequired))
            return false;

        string creditMsg = string.Format(
            ApiMessageOverrides.Resolve(_settings.ApiMsgInsufficientCredits, ApiConstants.MsgInsufficientCredits),
            serviceName);
        string? balanceMsg = _creditService.BuildBalanceMessage(cachedMember, serviceId);
        var messages = balanceMsg != null
            ? new[] { creditMsg, balanceMsg }
            : new[] { creditMsg };
        _logger.WriteToLog($"[{displayName}] {L.T("Log_InsufficientCredits")}: {cachedMember.MemberId} ({serviceId}, {creditAmount}/{minCreditsRequired})", Constants.LogWarning);
        _soundPlayer.Play(_settings.SoundCheckinError);
        _welcomeDisplay?.ShowCheckinResult(Constants.StatusReject,
            GetMemberFullName(cachedMember),
            cachedMember.Avatar, messages, displayName);
        return true;
    }

    private ApiMessageOverrides CreateMessageOverrides()
    {
        return new ApiMessageOverrides
        {
            MsgMemberNotFoundByRfid = _settings.ApiMsgMemberNotFoundByRfid,
            MsgDoubleScanBlocked = _settings.ApiMsgDoubleScanBlocked,
            MsgCheckinSuccess = _settings.ApiMsgCheckinSuccess,
            MsgCheckoutSuccess = _settings.ApiMsgCheckoutSuccess,
            MsgCheckinFailed = _settings.ApiMsgCheckinFailed,
            MsgCheckoutFailed = _settings.ApiMsgCheckoutFailed,
            MsgInsufficientCredits = _settings.ApiMsgInsufficientCredits,
            MsgMemberNotActive = _settings.ApiMsgMemberNotActive
        };
    }

    private async Task<CheckinToggleResult> ExecuteCheckinToggleAsync(Card rfidTag, CachedMemberInfo? cachedMember)
    {
        var msgOverrides = CreateMessageOverrides();
        string deviceId = _mapping.EffectiveDeviceId;

        bool useV0 = _mapping.ApiVersion == 0 || rfidTag.IsRawValue;

        if (useV0)
        {
            using var api = _apiFactory.CreateWithClubSecret(_mapping.CheckinKey);
            return await api.ToggleCheckinByRfidAsync(rfidTag.GetCardId(_mapping.CardIdModeEnum), _mapping.DoubleScanThresholdMs, msgOverrides, cachedMember, deviceId);
        }
        else
        {
            using var api = _apiFactory.Create();
            return await api.ToggleCheckinByRfidV1Async(rfidTag.GetCardId(_mapping.CardIdModeEnum), _mapping.DoubleScanThresholdMs, msgOverrides, cachedMember, deviceId);
        }
    }

    private async Task<CachedMemberInfo?> UpdateCacheAfterCheckinAsync(CheckinToggleResult result, Card rfidTag, CachedMemberInfo? cachedMember, string displayName)
    {
        if (_memberCache == null || !result.Success || result.MemberId == 0)
            return cachedMember;

        string deviceId = _mapping.EffectiveDeviceId;

        cachedMember = BackfillCacheOnMiss(result, rfidTag, cachedMember, displayName);
        await RefreshAvatarIfNeededAsync(cachedMember, displayName);

        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        if (result.Action == ApiConstants.ActionCheckin)
            _memberCache.UpdateVisitTimestamps(result.MemberId, now, 0, deviceId, result.VisitId, _mapping.ApiVersion);
        else if (result.Action == ApiConstants.ActionCheckout)
            _memberCache.UpdateVisitTimestamps(result.MemberId, 0, now, deviceId, result.VisitId, _mapping.ApiVersion);

        return cachedMember;
    }

    private CachedMemberInfo? BackfillCacheOnMiss(CheckinToggleResult result, Card rfidTag, CachedMemberInfo? cachedMember, string displayName)
    {
        if (cachedMember != null || _memberCache!.GetByMemberId(result.MemberId) != null)
            return cachedMember;

        string firstName = result.MemberName ?? "";
        string lastName = "";
        if (!string.IsNullOrEmpty(result.MemberName))
        {
            int spaceIdx = result.MemberName.IndexOf(' ');
            if (spaceIdx > 0)
            {
                firstName = result.MemberName.Substring(0, spaceIdx);
                lastName = result.MemberName.Substring(spaceIdx + 1).Trim();
            }
        }

        cachedMember = new CachedMemberInfo
        {
            MemberId = result.MemberId,
            UserId = result.UserId,
            RfidTag = rfidTag.GetCardId(_mapping.CardIdModeEnum),
            Firstname = firstName,
            Lastname = lastName,
            Avatar = result.MemberAvatar,
            Active = true,
            TimestampEdit = result.UserTimestampEdit
        };

        _memberCache.Upsert(new MemberCacheEntry
        {
            UserId = result.UserId,
            MemberId = result.MemberId,
            RfidTag = rfidTag.GetCardId(_mapping.CardIdModeEnum),
            Active = true,
            Firstname = firstName,
            Lastname = lastName,
            AvatarUrl = result.MemberAvatar,
            TimestampEdit = result.UserTimestampEdit
        });

        _logger.WriteToLog($"[{displayName}] {string.Format(L.T("Log_CacheMissBackfilled"), result.MemberName, result.MemberId)}");
        return cachedMember;
    }

    private async Task RefreshAvatarIfNeededAsync(CachedMemberInfo? cachedMember, string displayName)
    {
        if (_memberCache == null || cachedMember == null || cachedMember.UserId == 0)
            return;

        bool needsAvatarRefresh = false;
        var lastSync = _memberCache.GetLastSyncTime();
        if (lastSync.HasValue)
        {
            long lastSyncMs = new DateTimeOffset(lastSync.Value.ToUniversalTime()).ToUnixTimeMilliseconds();
            needsAvatarRefresh = cachedMember.TimestampEdit > lastSyncMs;
        }
        else
        {
            needsAvatarRefresh = string.IsNullOrEmpty(cachedMember.Avatar);
        }

        if (!needsAvatarRefresh)
            return;

        try
        {
            using var api = _apiFactory.Create();
#pragma warning disable CS0618
            var memberDetails = await api.Users.GetByIdAsync(cachedMember.UserId.ToString());
#pragma warning restore CS0618
            if (memberDetails != null && !string.IsNullOrEmpty(memberDetails.user_avatar))
            {
                string fullAvatarUrl = $"{_settings.VirtuagymProfileImageUrl}/{memberDetails.user_avatar}";
                _memberCache.UpdateAvatar(cachedMember.MemberId, fullAvatarUrl);
                cachedMember.Avatar = fullAvatarUrl;
                _logger.WriteToLog($"[{displayName}] {string.Format(L.T("Log_AvatarUpdated"), cachedMember.MemberId)}");

                var localPath = await _memberCache.DownloadAndCacheAvatarAsync(cachedMember.MemberId, fullAvatarUrl);
                if (!string.IsNullOrEmpty(localPath))
                    _logger.WriteToLog($"[{displayName}] Avatar lokal gespeichert: {localPath}");
            }
        }
        catch (Exception avatarEx)
        {
            _logger.WriteToLog($"[{displayName}] {L.T("Log_AvatarUpdateError")}: {avatarEx.Message}", Constants.LogWarning);
        }
    }

    private void HandleCheckinSuccess(CheckinToggleResult result, string displayName, CancellationToken cancellationToken, CachedMemberInfo? cachedMember = null)
    {
        string actionText = result.Action == ApiConstants.ActionCheckout ? L.T("Log_ActionCheckout") : L.T("Log_ActionCheckin");
        _logger.WriteToLog($"[{displayName}] {actionText}: {result.MemberName} (Status: {result.Status})", Constants.LogSuccess);

        if (result.ClientMessages != null && result.ClientMessages.Length > 0)
            _logger.WriteToLog($"[{displayName}] {L.T("Log_ServerMessage")}: {string.Join(" | ", result.ClientMessages)}");

        if (_memberCache != null && result.MemberId != 0 && !string.IsNullOrEmpty(result.MemberAvatar))
        {
            _memberCache.UpdateAvatar(result.MemberId, result.MemberAvatar);
            _ = _memberCache.DownloadAndCacheAvatarAsync(result.MemberId, result.MemberAvatar);
        }

        _soundPlayer.Play(result.Status == Constants.StatusWarn ? _settings.SoundCheckinWarn : _settings.SoundCheckinSuccess);
        string[] baseMessages = result.ClientMessages != null && result.ClientMessages.Length > 0
            ? result.ClientMessages : new[] { result.Message };
        string? creditBalanceMsg = _creditService.BuildBalanceMessage(cachedMember, _mapping.CreditServiceId);
        if (creditBalanceMsg != null)
            baseMessages = [.. baseMessages, creditBalanceMsg];
        _welcomeDisplay?.ShowCheckinResult(result.Status, result.MemberName, result.MemberAvatar,
            baseMessages, displayName);

        _hardwareTrigger.TriggerRelayIfEnabled(displayName);
        _hardwareTrigger.TriggerPgGateIfEnabled(displayName, cancellationToken);
    }

    private void HandleCheckinFailure(CheckinToggleResult result, Card rfidTag, string displayName)
    {
        string reason = result.ClientMessages != null && result.ClientMessages.Length > 0
            ? string.Join(" | ", result.ClientMessages) : result.Message;
        string[] messages = result.ClientMessages != null && result.ClientMessages.Length > 0
            ? result.ClientMessages : new[] { result.Message };

        if (!_settings.DebugMode && result.Action == ApiConstants.ActionUnknown)
        {
            var msgOverride = _settings.ApiMsgMemberNotFoundByRfid;
            string template = ApiMessageOverrides.Resolve(msgOverride, ApiConstants.MsgMemberNotFoundByRfid);
            string sanitized = string.Format(template, "***");
            messages = new[] { sanitized };
        }

        bool isDoubleScan = result.Action == ApiConstants.ActionCheckin
            && result.Status == ApiConstants.StatusUnknown;

        if (isDoubleScan)
        {
            _logger.WriteToLog($"[{displayName}] {L.T("Log_DoubleScanBlocked")}: {result.MemberName} ({reason})", Constants.LogInfo);
            _soundPlayer.Play(_settings.SoundCheckinDoubleScan);
            _welcomeDisplay?.ShowCheckinResult(Constants.StatusDoubleScan, result.MemberName ?? "", result.MemberAvatar,
                messages, displayName);
        }
        else
        {
            _logger.WriteToLog($"[{displayName}] {L.T("Log_CheckinToggleFailed")}: {reason} (Status: {result.Status})", Constants.LogWarning);
            _logger.WriteToRejectedLog(rfidTag.GetCardId(_mapping.CardIdModeEnum), result.Status ?? "rejected", reason);
            _soundPlayer.Play(_settings.SoundCheckinError);
            _welcomeDisplay?.ShowCheckinResult(result.Status ?? Constants.StatusReject, result.MemberName ?? "", result.MemberAvatar,
                messages, displayName);
        }
    }

    private void HandleOfflineFallback(Exception ex, Card rfidTag, CachedMemberInfo? cachedMember, string displayName, CancellationToken cancellationToken)
    {
        if (_memberCache == null || cachedMember == null || cachedMember.MemberId == 0)
        {
            _logger.WriteToLog($"{L.T("Log_ApiError")} '{rfidTag.GetCardId(_mapping.CardIdModeEnum)}': {ex.Message}", Constants.LogError);
            _logger.WriteToRejectedLog(rfidTag.GetCardId(_mapping.CardIdModeEnum), "exception", ex.Message);
            _soundPlayer.Play(_settings.SoundCheckinError);
            _welcomeDisplay?.ShowCheckinResult(Constants.StatusReject, "", null, new[] { L.T("Checkin_ConnectionError") }, displayName);
            return;
        }

        string memberName = GetMemberFullName(cachedMember);
        if (string.IsNullOrEmpty(memberName))
            memberName = L.T("Error_MemberFallbackName") + cachedMember.MemberId;

        string deviceId = _mapping.EffectiveDeviceId;
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        bool isCheckout = false;
        var offlineDevData = cachedMember.GetDeviceData(deviceId);
        if (offlineDevData != null)
            isCheckout = offlineDevData.CheckInTimestamp > 0 && offlineDevData.CheckOutTimestamp == 0;
        string action = isCheckout ? ApiConstants.ActionCheckout : ApiConstants.ActionCheckin;

        long? visitId = isCheckout && offlineDevData != null ? offlineDevData.VisitId : null;
        _memberCache.AddPendingCheckin(cachedMember.MemberId, cachedMember.UserId, rfidTag.GetCardId(_mapping.CardIdModeEnum), deviceId, now, action, _mapping.CheckinKey, visitId);

        if (isCheckout)
        {
            _memberCache.UpdateVisitTimestamps(cachedMember.MemberId, 0, now, deviceId, visitId, _mapping.ApiVersion);
        }
        else
        {
            _memberCache.UpdateVisitTimestamps(cachedMember.MemberId, now, 0, deviceId, visitId, _mapping.ApiVersion);
            _creditService.DecrementOffline(cachedMember, _mapping.CreditServiceId, displayName);
        }

        int pendingCount = _memberCache.GetPendingCheckinCount();
        string actionText = isCheckout ? L.T("Log_ActionCheckout") : L.T("Log_ActionCheckin");
        _logger.WriteToLog($"[{displayName}] {L.T("Log_OfflineCheckin")} ({actionText}): {memberName} ({pendingCount} {L.T("Log_Pending")})", Constants.LogWarning);

        _soundPlayer.Play(_settings.SoundCheckinSuccess);

        string? offlineAvatar = _memberCache.GetAvatarLocalPath(cachedMember.MemberId);
        string[] offlineMessages = [L.T("Checkin_OfflineSyncPending")];
        string? offlineCreditMsg = _creditService.BuildBalanceMessage(cachedMember, _mapping.CreditServiceId);
        if (offlineCreditMsg != null)
            offlineMessages = [.. offlineMessages, offlineCreditMsg];
        _welcomeDisplay?.ShowCheckinResult(Constants.StatusOk, memberName, offlineAvatar,
            offlineMessages, displayName);

        _hardwareTrigger.TriggerRelayIfEnabled(displayName);
        _hardwareTrigger.TriggerPgGateIfEnabled(displayName, cancellationToken);
    }

    #endregion
}
