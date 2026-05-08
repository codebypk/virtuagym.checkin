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
        if (TryHandleAccessPass(encryptedQrCode, displayName, cancellationToken))
            return;

        // 2) Virtuagym native QR
        if (VirtuagymNativeQr.IsMatch(encryptedQrCode))
        {
            if (!VirtuagymNativeQr.TryExtractCardId(encryptedQrCode, out string vgCardId))
            {
                ShowReject(displayName, "", null, [L.T("QR_InvalidOrManipulated")],
                    $"[{displayName}] {L.T("Log_VgQrEmpty")}");
                return;
            }

            _logger.WriteToLog($"[{displayName}] {L.T("Log_VgQrDetected")}: {vgCardId[..Math.Min(vgCardId.Length, 20)]}...");
            await PerformCheckinAsync(Card.FromRawValue(encryptedQrCode), sourceName, null, cancellationToken);
            return;
        }

        // 3) Unknown QR format
        ShowReject(displayName, "", null, [L.T("QR_InvalidOrManipulated")],
            $"[{displayName}] {L.T("Log_QrNotEncrypted")}", Constants.LogError);
    }

    /// <summary>
    /// Performs a check-in/check-out via RFID tag.
    /// </summary>
    public async Task PerformCheckinAsync(Card rfidTag, string? sourceName = null, CachedMemberInfo? precachedMember = null, CancellationToken cancellationToken = default)
    {
        string displayName = ResolveDisplayName(sourceName);

        // Try access pass lookup by RFID card ID before API call
        if (TryHandleAccessPass(rfidTag.GetCardId(_mapping.CardIdModeEnum), displayName, cancellationToken))
            return;

        CachedMemberInfo? cachedMember = ResolveCachedMember(rfidTag, precachedMember, displayName);

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            _logger.WriteToLog($"[{displayName}] {L.T("Log_ToggleCheckinStarted")} RFID '{rfidTag}' ...");
            _welcomeDisplay?.ShowLoader(displayName);

            if (RejectIfMemberInactive(cachedMember, displayName))
                return;

            // Credits check only applies to check-in (i.e. member has no active visit)
            bool isCheckout = false;
            var offlineDevData = cachedMember?.GetDeviceData(_mapping.DeviceID);
            if (offlineDevData != null)
                isCheckout = offlineDevData.CheckInTimestamp > 0 && offlineDevData.CheckOutTimestamp == 0;

            if (await RejectIfInsufficientCreditsAsync(cachedMember, displayName, isCheckout))
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
            _logger.WriteToRejectedLog(rfidTag.GetCardId(_mapping.CardIdModeEnum), $"api_error_{apiEx.ApiStatusCode}", apiEx.ApiStatusMessage);
            ShowReject(displayName, "", null, [$"{L.T("Log_Error")}: {apiEx.ApiStatusMessage}"],
                $"{L.T("Log_ApiError")} '{rfidTag}': {apiEx.ApiStatusMessage} (Code: {apiEx.ApiStatusCode})", Constants.LogError);
        }
        catch (Exception ex)
        {
            _logger.WriteToLog($"{L.T("Log_ApiError")} '{rfidTag}': {ex.Message}", Constants.LogError);
            HandleOfflineFallback(ex, rfidTag, cachedMember, displayName, cancellationToken);
        }
    }

    private void HandleAccessPassResult(AccessPassResult result, string displayName, CancellationToken cancellationToken)
    {
        var localizedMessages = BuildLocalizedAccessPassMessages(result);

        if (result.Success)
        {
            string actionText = result.Action == "checkout" ? L.T("Log_ActionCheckout") : L.T("Log_ActionCheckin");
            ShowSuccessWithHardwareTrigger(displayName, result.DisplayName ?? "", result.Action, result.AvatarPath, localizedMessages,
                $"[{displayName}] AccessPass {actionText}: {result.DisplayName} ({result.RemainingUses}/{result.TotalUses})", cancellationToken);
        }
        else
        {
            ShowReject(displayName, result.DisplayName ?? "", result.AvatarPath, localizedMessages,
                $"[{displayName}] AccessPass rejected: {result.Error} – {string.Join(" | ", localizedMessages)}");
        }
    }

    private bool TryHandleAccessPass(string identifier, string displayName, CancellationToken cancellationToken)
    {
        if (_accessPassService == null) return false;
        var result = _accessPassService.TryResolve(identifier, _settings.VirtuagymClubSecret?.Trim() ?? "");
        if (result == null) return false;
        HandleAccessPassResult(result, displayName, cancellationToken);
        return true;
    }

    private void ShowReject(string displayName, string memberName, string? avatar, string[] messages, string? logMessage = null, int logLevel = Constants.LogWarning)
    {
        if (logMessage != null)
            _logger.WriteToLog(logMessage, logLevel);
        _soundPlayer.Play(_settings.SoundCheckinError);
        _welcomeDisplay?.ShowCheckinResult(Constants.StatusReject, memberName, avatar, messages, displayName);
    }

    private void ShowSuccessWithHardwareTrigger(string displayName, string memberName, string action, string? avatar, string[] messages, string logMessage, CancellationToken cancellationToken)
    {
        _logger.WriteToLog(logMessage, Constants.LogSuccess);
        _soundPlayer.Play(_settings.SoundCheckinSuccess);
        _welcomeDisplay?.ShowCheckinResult(Constants.StatusOk, memberName, avatar, messages, displayName);

        void OnHardwareError(string errorMessage)
        {
            _logger.WriteToLog($"[{displayName}] Hardware trigger failed after successful check-in: {errorMessage}", Constants.LogWarning);
            _welcomeDisplay?.ShowHardwareError(errorMessage, displayName);
        }

        _hardwareTrigger.TriggerRelayIfEnabled(displayName, action ?? ApiConstants.ActionCheckin, OnHardwareError);
        _hardwareTrigger.TriggerPgGateIfEnabled(displayName, action ?? ApiConstants.ActionCheckin, cancellationToken, OnHardwareError);
    }

    private static string[] BuildLocalizedAccessPassMessages(AccessPassResult result)
    {
        string displayName = result.DisplayName ?? "";

        if (result.Success)
        {
            return result.Action == "checkout"
                ? [string.Format(L.T("AccessPass_CheckoutMessage"), displayName, result.RemainingUses, result.TotalUses)]
                : [string.Format(L.T("AccessPass_CheckinMessage"), displayName, result.RemainingUses, result.TotalUses)];
        }

        return result.Error switch
        {
            AccessPassError.NotFound => [L.T("AccessPass_Error_NotFound")],
            AccessPassError.Inactive => [L.T("AccessPass_Error_Inactive")],
            AccessPassError.Expired => [L.T("AccessPass_Error_Expired")],
            AccessPassError.NoRemainingUses => [L.T("AccessPass_Error_NoRemainingUses")],
            AccessPassError.InvalidToken => [L.T("AccessPass_Error_InvalidToken")],
            AccessPassError.AlreadyCheckedIn => [L.T("AccessPass_Error_AlreadyCheckedIn")],
            AccessPassError.NotCheckedIn => [L.T("AccessPass_Error_NotCheckedIn")],
            _ => result.Messages != null && result.Messages.Length > 0 ? result.Messages : [L.T("Msg_Error")]
        };
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
        ShowReject(displayName, memberName, cachedMember.Avatar, [notActiveMsg],
            $"[{displayName}] {L.T("Log_MemberNotActive")}: {cachedMember.MemberId} ({memberName})");
        return true;
    }

    private async Task<bool> RejectIfInsufficientCreditsAsync(CachedMemberInfo? cachedMember, string displayName, bool isCheckout)
    {
        bool requireCredits = !string.IsNullOrWhiteSpace(_mapping.CreditServiceId);
        if (!requireCredits || cachedMember == null || cachedMember.MemberId == 0 || !isCheckout)
            return false;

        string serviceId = _mapping.CreditServiceId ?? "";

        // TTL <= 0 → don't block, let the API decide (no cache check)
        if (_settings.CreditsCacheTtlMinutes <= 0)
        {
            _logger.WriteToLog($"[{displayName}] Credits TTL deaktiviert – API entscheidet ({serviceId})", Constants.LogInfo);
            return false;
        }

        // Refresh cache if stale (async) – but don't block the check-in flow
        if (IsCreditCacheStale(cachedMember))
            await _creditService.RefreshIfStaleAsync(cachedMember, serviceId,_mapping.CreditClubId,_mapping.CheckinKey, displayName);

        // Only check locally cached credits – NO separate API call
        if (!_creditService.HasInsufficientCredits(cachedMember, serviceId, out var serviceName, out var creditAmount, out var minCreditsRequired))
            return false;

        // Insufficient according to cache – only reject if cache is fresh
        int ttlMinutes = Math.Max(1, _settings.CreditsCacheTtlMinutes);
        long ttlMs = ttlMinutes * 60L * 1000L;
        long nowTs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        bool creditsFresh = cachedMember.CreditsLastSyncTimestamp > 0
            && (nowTs - cachedMember.CreditsLastSyncTimestamp) <= ttlMs;

        if (!creditsFresh)
        {
            // Stale → let the API decide, don't block
            _logger.WriteToLog($"[{displayName}] Credits stale – API entscheidet ({serviceId}, cached={creditAmount})", Constants.LogInfo);
            return false;
        }

        string creditMsg = string.Format(
            ApiMessageOverrides.Resolve(_settings.ApiMsgInsufficientCredits, ApiConstants.MsgInsufficientCredits),
            serviceName);
        string? balanceMsg = _creditService.BuildBalanceMessage(cachedMember, serviceId);
        var messages = balanceMsg != null
            ? new[] { creditMsg, balanceMsg }
            : new[] { creditMsg };
        ShowReject(displayName, GetMemberFullName(cachedMember), cachedMember.Avatar, messages,
            $"[{displayName}] {L.T("Log_InsufficientCredits")}: {cachedMember.MemberId} ({serviceId}, {creditAmount}/{minCreditsRequired})");
        return true;
    }

    private async Task<CheckinToggleResult> ExecuteCheckinToggleAsync(Card rfidTag, CachedMemberInfo? cachedMember)
    {
        string FormatThreshold(long thresholdMs)
        {
            long seconds = Math.Max(1, thresholdMs / 1_000);
            if (seconds >= 60)
            {
                long minutes = seconds / 60;
                return $"{minutes} {(minutes == 1 ? L.T("Time_Minute_Singular") : L.T("Time_Minute_Plural"))}";
            }
            return $"{seconds} {(seconds == 1 ? L.T("Time_Second_Singular") : L.T("Time_Second_Plural"))}";
        };

        var msgOverrides = new ApiMessageOverrides
        {
            MsgMemberNotFoundByRfid = _settings.ApiMsgMemberNotFoundByRfid,
            MsgDoubleScanBlocked = _settings.ApiMsgDoubleScanBlocked,
            MsgCheckinSuccess = _settings.ApiMsgCheckinSuccess,
            MsgCheckoutSuccess = _settings.ApiMsgCheckoutSuccess,
            MsgCheckinFailed = _settings.ApiMsgCheckinFailed,
            MsgCheckoutFailed = _settings.ApiMsgCheckoutFailed,
            MsgInsufficientCredits = _settings.ApiMsgInsufficientCredits,
            MsgMemberNotActive = _settings.ApiMsgMemberNotActive,
            FormatDoubleScanThreshold = FormatThreshold
        };
        
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

        // Backfill cache on miss
        if (cachedMember == null && _memberCache.GetByMemberId(result.MemberId) == null)
        {
            string firstName = result.MemberName ?? "";
            string lastName = "";
            if (!string.IsNullOrEmpty(result.MemberName))
            {
                int spaceIdx = result.MemberName.IndexOf(' ');
                if (spaceIdx > 0)
                {
                    firstName = result.MemberName[..spaceIdx];
                    lastName = result.MemberName[(spaceIdx + 1)..].Trim();
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
        }

        // fire-and-forget avatar refresh if needed should not block the main flow
        _ = RefreshAvatarIfNeededAsync(cachedMember, displayName);

        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        if (result.Action == ApiConstants.ActionCheckin)
            _memberCache.UpdateVisitTimestamps(result.MemberId, now, 0, deviceId, result.VisitId, _mapping.ApiVersion);
        else if (result.Action == ApiConstants.ActionCheckout)
            _memberCache.UpdateVisitTimestamps(result.MemberId, 0, now, deviceId, result.VisitId, _mapping.ApiVersion);

        // Credits aktualisieren:
        // Priorität 1 – Wert direkt aus v0 employee_message parsen (kein extra API-Call)
        // Priorität 2 – Cache stale und kein Parsing möglich (v1 / kein Pattern / unlimited)
        //               → await RefreshIfStaleAsync, damit Cache vor dem nächsten Scan aktuell ist
        if (!string.IsNullOrWhiteSpace(_mapping.CreditServiceId))
        {
            bool creditsUpdatedFromResponse = false;

            if (result.EmployeeMessage != null
                && CheckinCreditService.TryParseCreditFromEmployeeMessage(
                    result.EmployeeMessage, _settings.CreditParsePattern, out int parsedCredits))
            {
                string serviceId = _mapping.CreditServiceId;
                long nowTs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

                if (cachedMember != null)
                {
                    cachedMember.ServiceCredits ??= [];
                    var existing = cachedMember.GetCreditsForService(serviceId);
                    if (existing != null)
                    {
                        existing.CreditAmount = parsedCredits;
                        existing.CreditUnlimited = false;
                    }
                    else
                        cachedMember.ServiceCredits.Add(new ClubServiceCredits
                        {
                            service_id = serviceId,
                            CreditAmount = parsedCredits,
                            CreditUnlimited = false
                        });
                    cachedMember.CreditsLastSyncTimestamp = nowTs;
                }

                _memberCache.UpdateCredits(result.MemberId, parsedCredits, false, serviceId, null, nowTs);
                _logger.WriteToLog($"[{displayName}] Credits aus Response aktualisiert: {parsedCredits} ({serviceId})");
                creditsUpdatedFromResponse = true;
            }

            // Kein Parsing möglich (z.B. v1, kein Pattern konfiguriert, oder unlimited credits
            // → keine Zahl in der employee_message). Falls Cache stale: synchron refreshen,
            // damit der Cache vor dem nächsten Scan bereits den korrekten Wert (inkl. unlimited) enthält.
            if (!creditsUpdatedFromResponse && cachedMember != null && IsCreditCacheStale(cachedMember))
            {
                await _creditService.RefreshIfStaleAsync(
                    cachedMember,
                    _mapping.CreditServiceId,
                    _mapping.CreditClubId,
                    _mapping.CheckinKey,
                    displayName);
            }
        }

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

        void OnHardwareError(string errorMessage)
        {
            _logger.WriteToLog($"[{displayName}] {errorMessage}", Constants.LogWarning);
            _soundPlayer.Play(_settings.SoundCheckinError);
            _welcomeDisplay?.ShowHardwareError(errorMessage, displayName);
        }

        _hardwareTrigger.TriggerRelayIfEnabled(displayName, result.Action, OnHardwareError);
        _hardwareTrigger.TriggerPgGateIfEnabled(displayName, result.Action, cancellationToken, OnHardwareError);
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
            _logger.WriteToRejectedLog(rfidTag.GetCardId(_mapping.CardIdModeEnum), result.Status ?? "rejected", reason);
            ShowReject(displayName, result.MemberName ?? "", result.MemberAvatar, messages,
                $"[{displayName}] {L.T("Log_CheckinToggleFailed")}: {reason} (Status: {result.Status})");
        }
    }

    private void HandleOfflineFallback(Exception ex, Card rfidTag, CachedMemberInfo? cachedMember, string displayName, CancellationToken cancellationToken)
    {
        if (_memberCache == null || cachedMember == null || cachedMember.MemberId == 0)
        {
            _logger.WriteToRejectedLog(rfidTag.GetCardId(_mapping.CardIdModeEnum), "exception", ex.Message);
            ShowReject(displayName, "", null, [L.T("Checkin_ConnectionError")],
                $"{L.T("Log_ApiError")} '{rfidTag.GetCardId(_mapping.CardIdModeEnum)}': {ex.Message}", Constants.LogError);
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
            _creditService.DecrementCredits(cachedMember, _mapping.CreditServiceId, displayName);
        }

        int pendingCount = _memberCache.GetPendingCheckinCount();
        string actionText = isCheckout ? L.T("Log_ActionCheckout") : L.T("Log_ActionCheckin");

        string? offlineAvatar = _memberCache.GetAvatarLocalPath(cachedMember.MemberId);
        string[] offlineMessages = [L.T("Checkin_OfflineSyncPending")];
        string? offlineCreditMsg = _creditService.BuildBalanceMessage(cachedMember, _mapping.CreditServiceId);
        if (offlineCreditMsg != null)
            offlineMessages = [.. offlineMessages, offlineCreditMsg];

        ShowSuccessWithHardwareTrigger(displayName, memberName, isCheckout ? ApiConstants.ActionCheckout : ApiConstants.ActionCheckin, offlineAvatar, offlineMessages,
            $"[{displayName}] {L.T("Log_OfflineCheckin")} ({actionText}): {memberName} ({pendingCount} {L.T("Log_Pending")})", cancellationToken);
    }

    /// <summary>
    /// Returns true when the credit cache has never been synced or the TTL has expired.
    /// TTL <= 0 means local credit cache freshness is disabled.
    /// </summary>
    private bool IsCreditCacheStale(CachedMemberInfo cachedMember)
    {
        if (_settings.CreditsCacheTtlMinutes <= 0)
            return true;

        if (cachedMember.CreditsLastSyncTimestamp <= 0)
            return true;

        long ttlMs = Math.Max(1, _settings.CreditsCacheTtlMinutes) * 60L * 1000L;
        long nowTs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        return (nowTs - cachedMember.CreditsLastSyncTimestamp) > ttlMs;
    }

    #endregion
}
