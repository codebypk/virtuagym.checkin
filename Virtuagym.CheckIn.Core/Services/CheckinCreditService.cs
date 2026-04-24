using System;
using System.Linq;
using System.Threading.Tasks;
using Virtuagym.API.Services;
using Virtuagym.CheckIn.Core.Abstractions;
using Virtuagym.CheckIn.Core.Helper;
using Virtuagym.CheckIn.Core.Models;
using Virtuagym.API.Cache.Models;

namespace Virtuagym.CheckIn.Core.Services;

/// <summary>
/// Checks and updates service credits for members.
/// Encapsulates API refresh, validation, balance display and offline decrement.
/// </summary>
public class CheckinCreditService
{
    private readonly ILogWriter _logger;
    private readonly IAppSettings _settings;
    private readonly IVirtuagymApiServiceFactory _apiFactory;
    private readonly MemberCacheService? _memberCache;

    public CheckinCreditService(ILogWriter logger, IAppSettings settings, IVirtuagymApiServiceFactory apiFactory, MemberCacheService? memberCache)
    {
        _logger = logger;
        _settings = settings;
        _apiFactory = apiFactory;
        _memberCache = memberCache;
    }

    /// <summary>
    /// Refreshes credits from the API if they are stale or not cached.
    /// Falls back to cached credits on error.
    /// </summary>
    public async Task RefreshIfStaleAsync(CachedMemberInfo cachedMember, string serviceId, int clubId, string checkinKey, string displayName)
    {
        int ttlMinutes = Math.Max(1, _settings.CreditsCacheTtlMinutes);
        long nowTs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        long ttlMs = ttlMinutes * 60L * 1000L;

        var cachedCreditBeforeFetch = cachedMember.GetCreditsForService(serviceId);
        bool hasCachedCredit = cachedCreditBeforeFetch != null;
        bool creditsFresh = cachedMember.CreditsLastSyncTimestamp > 0
            && (nowTs - cachedMember.CreditsLastSyncTimestamp) <= ttlMs;

        if (hasCachedCredit && creditsFresh)
            return;

        try
        {
            using var creditApi = _apiFactory.CreateWithClubSecret(checkinKey);
            var credits = await creditApi.Credits.GetByMemberAsync(cachedMember.MemberId.ToString());
            var match = credits?.FirstOrDefault(c =>
                string.Equals(c.service_type, serviceId, StringComparison.OrdinalIgnoreCase)
                && (clubId <= 0 || c.club_id == clubId));

            bool unlimited = match != null && match.credit_unlimited;
            int amount = (int)(match?.credit_amount ?? 0);

            if (cachedMember.ServiceCredits == null)
                cachedMember.ServiceCredits = new System.Collections.Generic.List<ClubServiceCredits>();
            var existingCredit = cachedMember.GetCreditsForService(serviceId);

            string? loadedServiceName = ClubServiceLoader.GetByServiceId(serviceId)?.servicename;

            if (existingCredit != null)
            {
                existingCredit.CreditAmount = amount;
                existingCredit.CreditUnlimited = unlimited;
                if (!string.IsNullOrEmpty(loadedServiceName))
                    existingCredit.servicename = loadedServiceName;
            }
            else
            {
                cachedMember.ServiceCredits.Add(new ClubServiceCredits
                {
                    service_id = serviceId,
                    servicename = loadedServiceName,
                    CreditAmount = amount,
                    CreditUnlimited = unlimited
                });
            }

            cachedMember.CreditsLastSyncTimestamp = nowTs;

            _memberCache?.UpdateCredits(cachedMember.MemberId, amount, unlimited, serviceId, loadedServiceName, nowTs);

            _logger.WriteToLog($"[{displayName}] {L.T("Log_CreditsLoaded")}: {cachedMember.MemberId} → " +
                (unlimited ? "unlimited" : amount.ToString()) + $" ({serviceId}, club={clubId})");
        }
        catch (Exception creditEx)
        {
            _logger.WriteToLog($"[{displayName}] {L.T("Log_CreditsLoadError")}: {creditEx.Message}", Constants.LogWarning);
        }
    }

    /// <summary>
    /// Checks if the member has insufficient credits.
    /// Returns true if credits are insufficient.
    /// </summary>
    public bool HasInsufficientCredits(CachedMemberInfo cachedMember, string serviceId, out string serviceName, out int creditAmount, out int minCreditsRequired)
    {
        var cachedCredit = cachedMember.GetCreditsForService(serviceId);
        bool isUnlimited = cachedCredit != null && cachedCredit.CreditUnlimited;
        creditAmount = cachedCredit?.CreditAmount ?? 0;
        serviceName = cachedCredit?.servicename ?? serviceId;

        var clubService = ClubServiceLoader.GetByServiceId(serviceId);
        minCreditsRequired = clubService?.min_credits ?? -1;

        bool hasMinCredits = isUnlimited || minCreditsRequired < 0 || creditAmount >= minCreditsRequired;
        return !hasMinCredits;
    }

    /// <summary>
    /// Builds a localized credit balance message for UI display.
    /// Returns null if no credits are configured.
    /// </summary>
    public string? BuildBalanceMessage(CachedMemberInfo? cachedMember, string? serviceId)
    {
        if (string.IsNullOrWhiteSpace(serviceId) || cachedMember == null)
            return null;

        var credit = cachedMember.GetCreditsForService(serviceId);
        if (credit == null)
            return null;

        if (credit.CreditUnlimited)
            return L.T("Checkin_CreditBalanceUnlimited");

        return string.Format(L.T("Checkin_CreditBalance"), credit.CreditAmount);
    }

    /// <summary>
    /// Decrements credits locally in offline mode.
    /// </summary>
    public void DecrementOffline(CachedMemberInfo cachedMember, string? serviceId, string displayName)
    {
        if (string.IsNullOrWhiteSpace(serviceId))
            return;

        var offlineCredit = cachedMember.GetCreditsForService(serviceId);
        if (offlineCredit == null || offlineCredit.CreditUnlimited || offlineCredit.CreditAmount <= 0)
            return;

        int newAmount = Math.Max(0, offlineCredit.CreditAmount - 1);
        _memberCache?.UpdateCredits(cachedMember.MemberId, newAmount, false, serviceId);
        _logger.WriteToLog($"[{displayName}] {L.T("Log_CreditsOfflineDecremented")}: {offlineCredit.CreditAmount} → {newAmount} ({serviceId})", Constants.LogWarning);
    }
}
