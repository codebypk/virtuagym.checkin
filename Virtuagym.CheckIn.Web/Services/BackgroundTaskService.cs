using Microsoft.Extensions.Options;
using Virtuagym.API.Cache;
using Virtuagym.API.Serialization;
using Virtuagym.API.Services;
using Virtuagym.CheckIn.Core.Helper;
using Virtuagym.CheckIn.Core.Models;
using Virtuagym.CheckIn.Core.Services;
using Virtuagym.CheckIn.Web.Models;

namespace Virtuagym.CheckIn.Web.Services;

/// <summary>
/// Hosted service that runs background tasks equivalent to the WPF MainWindow startup:
/// <list type="bullet">
///   <item>Log cleanup (delete old log files)</item>
///   <item>Member cache initialization and transient data clearing</item>
///   <item>Cache sync scheduler (interval or daily mode)</item>
///   <item>Auto-checkout scheduler</item>
/// </list>
/// </summary>
using AccessPass.Services;

public sealed class BackgroundTaskService : IHostedService, IDisposable
{
    private Timer? _accessPassCleanupTimer;
    private readonly IAccessPassService _accessPassService;
    private readonly TimeSpan _cleanupInterval = TimeSpan.FromHours(24); // Standard: 24h

    private readonly IOptionsMonitor<AppSettings> _optionsMonitor;
    private readonly WebLogService _log;
    private readonly WebVirtuagymApiServiceFactory _apiFactory;

    private MemberCacheService? _memberCache;
    private CacheSyncScheduler? _cacheSyncScheduler;

    public BackgroundTaskService(
        IOptionsMonitor<AppSettings> optionsMonitor,
        WebLogService log,
        WebVirtuagymApiServiceFactory apiFactory,
        IAccessPassService accessPassService)
    {
        _optionsMonitor = optionsMonitor;
        _log = log;
        _apiFactory = apiFactory;
        _accessPassService = accessPassService;
        _optionsMonitor.OnChange(StartOrStopAccessPassCleanup);
    }

    private AppSettings Settings => _optionsMonitor.CurrentValue;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        var settings = Settings;
        StartOrStopAccessPassCleanup(settings);

        // --- Global defaults (same as WPF MainWindow constructor) ---
        CheckinClientMapping.GlobalDefaultDoubleScanThresholdMs = settings.DefaultDoubleScanThresholdMs;
        CheckinClientMapping.GlobalDefaultDuplicateTimeoutSeconds = settings.DuplicateTimeoutSeconds;

        // --- Log cleanup ---
        try
        {
            int deleted = LogCleanupService.CleanupOldLogs(settings.LogRetentionDays);
            if (deleted > 0)
                _log.WriteToLog($"Log cleanup: {deleted} alte Dateien gelöscht.", Constants.LogInfo);
        }
        catch (Exception ex)
        {
            _log.WriteToLog($"Log cleanup Fehler: {ex.Message}", Constants.LogWarning);
        }

        // --- Member cache ---
        try
        {
            _memberCache = new MemberCacheService();

            if (!settings.MemberCacheEnabled)
            {
                _log.WriteToLog("Member-Cache ist deaktiviert.", Constants.LogInfo);
            }
        }
        catch (Exception ex)
        {
            _log.WriteToLog($"Member-Cache Init-Fehler: {ex.Message}", Constants.LogWarning);
            _memberCache = null;
        }

        // --- Cache sync scheduler & auto-checkout ---
        var checkinCache = settings.MemberCacheEnabled ? _memberCache : null;
        var mappings = LoadMappings(settings);

        try
        {
            _cacheSyncScheduler = new CacheSyncScheduler(
                checkinCache,
                () => _apiFactory.Create(),
                checkinKey => _apiFactory.CreateWithClubSecret(checkinKey),
                mappings?.Where(m => !string.IsNullOrWhiteSpace(m.CheckinKey)).Select(m => m.CheckinKey).ToList());

            _cacheSyncScheduler.Log += msg => _log.WriteToLog(msg, Constants.LogInfo);
            _cacheSyncScheduler.SyncStarted += () => _log.WriteToLog("Cache-Sync gestartet...", Constants.LogInfo);
            _cacheSyncScheduler.SyncFinished += (success, msg) =>
                _log.WriteToLog($"Cache-Sync beendet: {msg}", success ? Constants.LogSuccess : Constants.LogWarning);

            // Auto-checkout
            if (mappings is { Count: > 0 })
            {
                var autoCheckoutDevices = mappings
                    .Where(m => m.AutoCheckoutMinutes > 0 && !string.IsNullOrWhiteSpace(m.CheckinKey))
                    .Select(m => new CacheSyncScheduler.AutoCheckoutDevice
                    {
                        DeviceId = m.EffectiveDeviceId,
                        CheckinKey = m.CheckinKey,
                        AutoCheckoutMinutes = m.AutoCheckoutMinutes,
                        ApiVersion = m.ApiVersion
                    })
                    .ToList();

                if (autoCheckoutDevices.Count > 0)
                {
                    _cacheSyncScheduler.SetAutoCheckoutDevices(autoCheckoutDevices);
                    _cacheSyncScheduler.StartAutoCheckout();
                    _log.WriteToLog($"Auto-Checkout gestartet ({autoCheckoutDevices.Count} Geräte).", Constants.LogInfo);
                }
            }

            // Periodic cache sync
            if (settings.CacheSyncEnabled && _memberCache != null)
            {
                string syncMode = (settings.CacheSyncMode ?? "").Trim();
                int cacheCount = _memberCache.GetCount();

                if (string.Equals(syncMode, "DailyTime", StringComparison.OrdinalIgnoreCase))
                {
                    string dailyTime = settings.CacheSyncDailyTime ?? "02:00";
                    _cacheSyncScheduler.StartDaily(dailyTime);
                    _log.WriteToLog($"Member-Cache initialisiert ({cacheCount} Einträge). Sync-Modus: Täglich um {dailyTime}", Constants.LogInfo);
                }
                else
                {
                    _cacheSyncScheduler.Start(settings.CacheSyncIntervalMinutes);
                    _log.WriteToLog($"Member-Cache initialisiert ({cacheCount} Einträge). Intervall: {settings.CacheSyncIntervalMinutes} Min.", Constants.LogInfo);
                }
            }
        }
        catch (Exception ex)
        {
            _log.WriteToLog($"Cache-Sync Init-Fehler: {ex.Message}", Constants.LogWarning);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Stops all running schedulers and re-initializes cache sync and auto-checkout
    /// from the current settings. Also re-creates the member cache if needed.
    /// Call this after settings have been saved.
    /// </summary>
    public void Reload()
    {
        _log.WriteToLog("BackgroundTaskService wird neu geladen...", Constants.LogInfo);
        StartOrStopAccessPassCleanup(Settings);

        // Stop existing schedulers
        _cacheSyncScheduler?.Stop();
        _cacheSyncScheduler?.StopAutoCheckout();
        _cacheSyncScheduler?.Dispose();
        _cacheSyncScheduler = null;

        // Re-create member cache
        _memberCache?.Dispose();
        _memberCache = null;

        var settings = Settings;

        try
        {
            _memberCache = new MemberCacheService();
            if (!settings.MemberCacheEnabled)
                _log.WriteToLog("Member-Cache ist deaktiviert.", Constants.LogInfo);
        }
        catch (Exception ex)
        {
            _log.WriteToLog($"Member-Cache Init-Fehler: {ex.Message}", Constants.LogWarning);
            _memberCache = null;
        }

        // Re-create cache sync scheduler & auto-checkout
        var checkinCache = settings.MemberCacheEnabled ? _memberCache : null;
        var mappings = LoadMappings(settings);

        try
        {
            _cacheSyncScheduler = new CacheSyncScheduler(
                checkinCache,
                () => _apiFactory.Create(),
                checkinKey => _apiFactory.CreateWithClubSecret(checkinKey),
                mappings?.Where(m => !string.IsNullOrWhiteSpace(m.CheckinKey)).Select(m => m.CheckinKey).ToList());

            _cacheSyncScheduler.Log += msg => _log.WriteToLog(msg, Constants.LogInfo);
            _cacheSyncScheduler.SyncStarted += () => _log.WriteToLog("Cache-Sync gestartet...", Constants.LogInfo);
            _cacheSyncScheduler.SyncFinished += (success, msg) =>
                _log.WriteToLog($"Cache-Sync beendet: {msg}", success ? Constants.LogSuccess : Constants.LogWarning);

            // Auto-checkout
            if (mappings is { Count: > 0 })
            {
                var autoCheckoutDevices = mappings
                    .Where(m => m.AutoCheckoutMinutes > 0 && !string.IsNullOrWhiteSpace(m.CheckinKey))
                    .Select(m => new CacheSyncScheduler.AutoCheckoutDevice
                    {
                        DeviceId = m.EffectiveDeviceId,
                        CheckinKey = m.CheckinKey,
                        AutoCheckoutMinutes = m.AutoCheckoutMinutes,
                        ApiVersion = m.ApiVersion
                    })
                    .ToList();

                if (autoCheckoutDevices.Count > 0)
                {
                    _cacheSyncScheduler.SetAutoCheckoutDevices(autoCheckoutDevices);
                    _cacheSyncScheduler.StartAutoCheckout();
                    _log.WriteToLog($"Auto-Checkout neu gestartet ({autoCheckoutDevices.Count} Geräte).", Constants.LogInfo);
                }
            }

            // Periodic cache sync
            if (settings.CacheSyncEnabled && _memberCache != null)
            {
                string syncMode = (settings.CacheSyncMode ?? "").Trim();
                int cacheCount = _memberCache.GetCount();

                if (string.Equals(syncMode, "DailyTime", StringComparison.OrdinalIgnoreCase))
                {
                    string dailyTime = settings.CacheSyncDailyTime ?? "02:00";
                    _cacheSyncScheduler.StartDaily(dailyTime);
                    _log.WriteToLog($"Member-Cache neu initialisiert ({cacheCount} Einträge). Sync-Modus: Täglich um {dailyTime}", Constants.LogInfo);
                }
                else
                {
                    _cacheSyncScheduler.Start(settings.CacheSyncIntervalMinutes);
                    _log.WriteToLog($"Member-Cache neu initialisiert ({cacheCount} Einträge). Intervall: {settings.CacheSyncIntervalMinutes} Min.", Constants.LogInfo);
                }
            }
        }
        catch (Exception ex)
        {
            _log.WriteToLog($"Cache-Sync Reload-Fehler: {ex.Message}", Constants.LogWarning);
        }

        _log.WriteToLog("BackgroundTaskService erfolgreich neu geladen.", Constants.LogInfo);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _cacheSyncScheduler?.Dispose();
        _memberCache?.Dispose();
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _cacheSyncScheduler?.Dispose();
        _memberCache?.Dispose();
        _accessPassCleanupTimer?.Dispose();
    }

    private void StartOrStopAccessPassCleanup(AppSettings settings)
    {
        _accessPassCleanupTimer?.Dispose();
        _accessPassCleanupTimer = null;
        if (settings.AccessPassDeletionMode == AccessPass.Models.AccessPassDeletionMode.AfterXDays && settings.AccessPassDeletionDays > 0)
        {
            _accessPassCleanupTimer = new Timer(_ => RunAccessPassCleanup(), null, TimeSpan.Zero, _cleanupInterval);
            _log.WriteToLog($"Access Pass Autodelete aktiviert (alle {_cleanupInterval.TotalHours}h, nach {settings.AccessPassDeletionDays} Tagen).", Constants.LogInfo);
        }
        else
        {
            _log.WriteToLog("Access Pass Autodelete-Timer gestoppt (Modus nicht 'Nach X Tagen').", Constants.LogInfo);
        }
    }

    private void RunAccessPassCleanup()
    {
        try
        {
            var settings = Settings;
            int deleted = _accessPassService.DeleteExpiredOrDepletedPasses(settings.AccessPassDeletionMode, settings.AccessPassDeletionDays);
            if (deleted > 0)
                _log.WriteToLog($"Access Pass Autodelete: {deleted} Pass(e) gelöscht.", Constants.LogInfo);
        }
        catch (Exception ex)
        {
            _log.WriteToLog($"Access Pass Autodelete Fehler: {ex.Message}", Constants.LogWarning);
        }
    }

    private static List<CheckinClientMapping>? LoadMappings(AppSettings settings)
    {
        try
        {
            string json = settings.CheckinClientMappings;
            if (!string.IsNullOrWhiteSpace(json))
            {
                var serializer = new JsonSerializerAdapter();
                return serializer.Deserialize<List<CheckinClientMapping>>(json);
            }
        }
        catch
        {
            // Mappings konnten nicht geladen werden
        }
        return null;
    }
}
