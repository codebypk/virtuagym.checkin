using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Virtuagym.API.Services;
using Virtuagym.API.v1.Models;

namespace Virtuagym.API.Cache
{
    /// <summary>
    /// Scheduler for automatic periodic synchronization of the member cache.
    /// Supports configurable intervals and manual triggering.
    /// </summary>
    public class CacheSyncScheduler : IDisposable
    {
        private readonly MemberCacheService _cacheService;
        private readonly Func<VirtuagymApiService> _apiFactory;
        private readonly Func<string, VirtuagymApiService> _checkinApiFactory;
        private readonly List<string> _checkinKeys;
        private List<AutoCheckoutDevice> _autoCheckoutDevices;
        private Timer _timer;
        private Timer _autoCheckoutTimer;
        private CancellationTokenSource _cts;
        private CancellationTokenSource _autoCheckoutCts;
        private bool _isSyncing;
        private bool _isAutoCheckoutRunning;
        private bool _disposed;
        private readonly Dictionary<long, int> _autoCheckoutRetryCount = new();

        /// <summary>
        /// Configuration for auto-checkout per device.
        /// </summary>
        public class AutoCheckoutDevice
        {
            /// <summary>Effective device ID (CheckinClientMapping.EffectiveDeviceId). Numeric device_id for v0, name for v1.</summary>
            public string DeviceId { get; set; }
            /// <summary>CheckinKey (club secret) for API checkout.</summary>
            public string CheckinKey { get; set; }
            /// <summary>Auto-checkout after X minutes. 0 = Disabled.</summary>
            public int AutoCheckoutMinutes { get; set; }
            /// <summary>API version of this device (0 = v0, 1 = v1, etc.).</summary>
            public double ApiVersion { get; set; }
        }

        /// <summary>Fired on log messages.</summary>
        public event Action<string> Log;

        /// <summary>Fired when a sync starts.</summary>
        public event Action SyncStarted;

        /// <summary>Fired when a sync completes (success or error).</summary>
        public event Action<bool, string> SyncFinished;

        /// <summary>Indicates whether a sync is currently running.</summary>
        public bool IsSyncing { get { return _isSyncing; } }

        /// <summary>
        /// Creates a new scheduler.
        /// </summary>
        /// <param name="cacheService">The MemberCacheService. Can be null if caching is disabled (auto-checkout still works via API).</param>
        /// <param name="apiFactory">Factory method that creates a new VirtuagymApiService instance.</param>
        /// <param name="checkinApiFactory">
        /// Optional factory that creates a device-specific VirtuagymApiService instance based on a
        /// CheckinKey (club secret). Required for offline sync so Virtuagym assigns the check-in
        /// to the correct device.
        /// </param>
        /// <param name="checkinKeys">Optional list of CheckinKeys (club secrets) for all configured devices.
        /// Required for mapping visits to devices (device ID extracted from secret).</param>
        public CacheSyncScheduler(MemberCacheService cacheService, Func<VirtuagymApiService> apiFactory,
            Func<string, VirtuagymApiService> checkinApiFactory = null, List<string> checkinKeys = null)
        {
            ArgumentNullException.ThrowIfNull(apiFactory);
            _cacheService = cacheService;
            _apiFactory = apiFactory;
            _checkinApiFactory = checkinApiFactory;
            _checkinKeys = checkinKeys;
        }

        /// <summary>
        /// Sets the auto-checkout configuration for all devices.
        /// Filled with CheckinClientMappings at startup.
        /// </summary>
        public void SetAutoCheckoutDevices(List<AutoCheckoutDevice> devices)
        {
            _autoCheckoutDevices = devices;
        }

        /// <summary>
        /// Starts the standalone auto-checkout timer.
        /// Runs independently of cache sync at a shorter interval so expired visits are
        /// checked out promptly. The check interval is automatically derived from the smallest
        /// configured AutoCheckoutMinutes value across all devices (minimum: 1 minute).
        /// </summary>
        public void StartAutoCheckout()
        {
            StopAutoCheckout();

            if (_autoCheckoutDevices == null || _autoCheckoutDevices.Count == 0)
            {
                Log?.Invoke("[AutoCheckout] No devices with auto-checkout configured.");
                return;
            }

            // Check interval = smallest AutoCheckoutMinutes of all devices, at least 1 minute
            int minMinutes = int.MaxValue;
            foreach (var dev in _autoCheckoutDevices)
            {
                if (dev.AutoCheckoutMinutes > 0 && dev.AutoCheckoutMinutes < minMinutes)
                    minMinutes = dev.AutoCheckoutMinutes;
            }

            if (minMinutes == int.MaxValue || minMinutes <= 0)
            {
                Log?.Invoke("[AutoCheckout] Timer disabled (no device with AutoCheckoutMinutes > 0).");
                return;
            }

            int intervalMinutes = Math.Max(1, minMinutes);
            var interval = TimeSpan.FromMinutes(intervalMinutes);
            Log?.Invoke("[AutoCheckout] Timer started (interval: " + intervalMinutes + " min, derived from shortest auto-checkout value).");

            // First run after the calculated interval (initial sync already checks),
            // then periodically.
            _autoCheckoutTimer = new Timer(async _ => await RunAutoCheckoutCycleAsync(), null, interval, interval);
        }

        /// <summary>
        /// Stops the auto-checkout timer.
        /// </summary>
        public void StopAutoCheckout()
        {
            _autoCheckoutCts?.Cancel();
            _autoCheckoutTimer?.Dispose();
            _autoCheckoutTimer = null;
        }

        /// <summary>
        /// Timer callback wrapper: ensures only one run is active at a time and no sync is
        /// running concurrently (to avoid race conditions on visit timestamps).
        /// </summary>
        private async Task RunAutoCheckoutCycleAsync()
        {
            if (_isAutoCheckoutRunning) return;

            // Do not run in parallel with cache sync – sync could overwrite visit timestamps
            // that are currently being updated by auto-checkout.
            if (_isSyncing)
            {
                Log?.Invoke("[AutoCheckout] Skipped (cache sync is currently running).");
                return;
            }

            _isAutoCheckoutRunning = true;
            _autoCheckoutCts = new CancellationTokenSource();

            try
            {
                int checkouts = await RunAutoCheckoutsAsync(_autoCheckoutCts.Token);
                if (checkouts > 0)
                    Log?.Invoke("[AutoCheckout] " + checkouts + " expired visits checked out.");
            }
            catch (OperationCanceledException)
            {
                Log?.Invoke("[AutoCheckout] Check cancelled.");
            }
            catch (Exception ex)
            {
                Log?.Invoke("[AutoCheckout] Error: " + ex.Message);
            }
            finally
            {
                _isAutoCheckoutRunning = false;
            }
        }

        /// <summary>
        /// Starts the periodic scheduler with the specified interval.
        /// Checks the last sync timestamp in the cache to determine whether an immediate sync
        /// is needed. If the last sync is more recent than the interval, the first run is
        /// delayed by the remaining time.
        /// </summary>
        /// <param name="intervalMinutes">Interval in minutes. 0 = scheduler disabled.</param>
        public void Start(int intervalMinutes)
        {
            // Only stop the sync timer – auto-checkout timer remains untouched,
            // as it runs independently from the cache sync.
            StopSync();

            if (_cacheService == null)
            {
                Log?.Invoke("[CacheSync] Scheduler disabled (cache service not available).");
                return;
            }

            if (intervalMinutes <= 0)
            {
                Log?.Invoke("[CacheSync] Scheduler disabled (interval = 0).");
                return;
            }

            var interval = TimeSpan.FromMinutes(intervalMinutes);

            // Check if the last sync is still within the interval
            TimeSpan dueTime = TimeSpan.Zero;
            DateTime? lastSync = _cacheService?.GetLastSyncTime();
            if (lastSync.HasValue)
            {
                TimeSpan elapsed = DateTime.Now - lastSync.Value;
                if (elapsed < interval)
                {
                    dueTime = interval - elapsed;
                    Log?.Invoke("[CacheSync] Scheduler started (interval: " + intervalMinutes
                        + " min). Last sync " + (int)elapsed.TotalMinutes
                        + " min ago – next sync in " + (int)dueTime.TotalMinutes + " min.");
                }
                else
                {
                    Log?.Invoke("[CacheSync] Scheduler started (interval: " + intervalMinutes
                        + " min). Last sync outdated – running initial sync...");
                }
            }
            else
            {
                Log?.Invoke("[CacheSync] Scheduler started (interval: " + intervalMinutes
                    + " min). No previous sync – running initial sync...");
            }

            _timer = new Timer(async _ => await RunSyncAsync(), null, dueTime, interval);
        }

        /// <summary>
        /// Starts the scheduler in daily mode: sync runs once per day at the specified time.
        /// If the target time has already passed today and no sync has been run today,
        /// an immediate sync is triggered. Otherwise the first run is scheduled for the
        /// next occurrence of the target time. After each run the timer reschedules itself
        /// for the following day at the same time (handles day-length changes / DST).
        /// </summary>
        /// <param name="timeOfDay">Target time in "HH:mm" format (24-hour), e.g. "02:00".</param>
        public void StartDaily(string timeOfDay)
        {
            StopSync();

            if (_cacheService == null)
            {
                Log?.Invoke("[CacheSync] Scheduler disabled (cache service not available).");
                return;
            }

            if (!TimeSpan.TryParse(timeOfDay, out TimeSpan targetTime))
            {
                Log?.Invoke("[CacheSync] Scheduler disabled (invalid time format: \"" + timeOfDay + "\").");
                return;
            }

            TimeSpan dueTime = CalculateDailyDueTime(targetTime);

            // Check if sync was already performed today after the target time.
            // If yes, skip to tomorrow; if no, use the calculated dueTime.
            DateTime? lastSync = _cacheService?.GetLastSyncTime();
            if (lastSync.HasValue)
            {
                DateTime todayTarget = DateTime.Today + targetTime;
                if (lastSync.Value >= todayTarget)
                {
                    // Already synced today after the target time – schedule for tomorrow.
                    dueTime = CalculateDailyDueTime(targetTime, skipToday: true);
                    Log?.Invoke("[CacheSync] Daily scheduler started (time: " + targetTime.ToString(@"hh\:mm")
                        + "). Already synced today – next sync in "
                        + (int)dueTime.TotalHours + "h " + dueTime.Minutes + "min.");
                }
                else
                {
                    Log?.Invoke("[CacheSync] Daily scheduler started (time: " + targetTime.ToString(@"hh\:mm")
                        + "). Next sync in " + (int)dueTime.TotalHours + "h " + dueTime.Minutes + "min.");
                }
            }
            else
            {
                Log?.Invoke("[CacheSync] Daily scheduler started (time: " + targetTime.ToString(@"hh\:mm")
                    + "). No previous sync – next sync in "
                    + (int)dueTime.TotalHours + "h " + dueTime.Minutes + "min.");
            }

            // Use a one-shot timer; after each sync, reschedule for the next day
            // so that DST changes and varying day lengths are handled correctly.
            ScheduleDailySyncTimer(targetTime, dueTime);
        }

        /// <summary>
        /// Schedules a one-shot timer that fires after <paramref name="dueTime"/>,
        /// runs the sync, then reschedules itself for the next occurrence of <paramref name="targetTime"/>.
        /// </summary>
        private void ScheduleDailySyncTimer(TimeSpan targetTime, TimeSpan dueTime)
        {
            _timer = new Timer(async _ =>
            {
                await RunSyncAsync();

                // Reschedule for the next day (always skip today since we just ran).
                TimeSpan nextDue = CalculateDailyDueTime(targetTime, skipToday: true);
                Log?.Invoke("[CacheSync] Daily sync complete. Next sync in "
                    + (int)nextDue.TotalHours + "h " + nextDue.Minutes + "min.");
                ScheduleDailySyncTimer(targetTime, nextDue);
            }, null, dueTime, Timeout.InfiniteTimeSpan);
        }

        /// <summary>
        /// Calculates the <see cref="TimeSpan"/> until the next occurrence of
        /// <paramref name="targetTime"/> (today or tomorrow).
        /// </summary>
        /// <param name="targetTime">Target time of day.</param>
        /// <param name="skipToday">If true, always schedules for tomorrow even if the time hasn't passed today.</param>
        internal static TimeSpan CalculateDailyDueTime(TimeSpan targetTime, bool skipToday = false)
        {
            DateTime now = DateTime.Now;
            DateTime nextRun = DateTime.Today + targetTime;

            if (skipToday || nextRun <= now)
                nextRun = nextRun.AddDays(1);

            // Guard: ensure at least 1 second to avoid immediate re-fire edge cases.
            TimeSpan due = nextRun - now;
            return due > TimeSpan.Zero ? due : TimeSpan.FromSeconds(1);
        }

        /// <summary>
        /// Stops the entire scheduler (cache sync and auto-checkout).
        /// Called when the application shuts down or on dispose.
        /// </summary>
        public void Stop()
        {
            StopSync();
            StopAutoCheckout();
        }

        /// <summary>
        /// Stops only the cache-sync timer, without affecting the auto-checkout timer.
        /// </summary>
        private void StopSync()
        {
            _cts?.Cancel();
            _timer?.Dispose();
            _timer = null;
        }

        /// <summary>
        /// Runs a manual synchronization.
        /// </summary>
        /// <returns>Number of synchronized members, or -1 on error.</returns>
        public async Task<int> RunSyncAsync()
        {
            if (_cacheService == null)
            {
                Log?.Invoke("[CacheSync] Sync skipped (cache service not available).");
                return -1;
            }

            if (_isSyncing)
            {
                Log?.Invoke("[CacheSync] Sync already running, skipping.");
                return -1;
            }

            // Wait for running auto-checkout so that no visit timestamps
            // are read/written in parallel.
            if (_isAutoCheckoutRunning)
            {
                Log?.Invoke("[CacheSync] Waiting for running auto-checkout...");
                int waitMs = 0;
                while (_isAutoCheckoutRunning && waitMs < 30_000)
                {
                    await Task.Delay(500);
                    waitMs += 500;
                }
                if (_isAutoCheckoutRunning)
                    Log?.Invoke("[CacheSync] Auto-checkout still running – starting sync anyway.");
            }

            _isSyncing = true;
            _cts = new CancellationTokenSource();
            SyncStarted?.Invoke();

            try
            {
                Log?.Invoke("[CacheSync] Sync started...");

                using (var api = _apiFactory())
                {
                    // First sync pending offline check-ins
                    int pendingCount = _cacheService.GetPendingCheckinCount();
                    if (pendingCount > 0)
                    {
                        Log?.Invoke("[CacheSync] Syncing " + pendingCount + " pending offline check-ins...");
                        int synced = await _cacheService.SyncPendingCheckinsAsync(api, _cts.Token, _checkinApiFactory);
                        Log?.Invoke("[CacheSync] " + synced + " of " + pendingCount + " offline check-ins synced.");
                    }

                    // Then sync member cache
                    int count = await _cacheService.SyncFromApiAsync(api, _cts.Token, _checkinKeys);

                    string msg = "[CacheSync] Sync complete: " + count + " members.";
                    Log?.Invoke(msg);
                    SyncFinished?.Invoke(true, msg);
                    return count;
                }
            }
            catch (OperationCanceledException)
            {
                Log?.Invoke("[CacheSync] Sync cancelled.");
                SyncFinished?.Invoke(false, "Sync cancelled.");
                return -1;
            }
            catch (Exception ex)
            {
                string msg = "[CacheSync] Error: " + ex.Message;
                Log?.Invoke(msg);
                SyncFinished?.Invoke(false, msg);
                return -1;
            }
            finally
            {
                _isSyncing = false;
            }
        }

        /// <summary>
        /// Performs auto-checkouts for all configured devices.
        /// Loads active visits via a single API call and checks out visits that
        /// exceed the configured AutoCheckoutMinutes per device.
        /// Each device only checks out visits assigned to it (matched by device_id).
        /// Uses v0 API (PUT /devices toggle) or v1 API (POST /visits) for checkout
        /// depending on the device configuration.
        /// Works independently of the MemberCacheService – the cache is only
        /// updated optionally for consistency.
        /// </summary>
        private async Task<int> RunAutoCheckoutsAsync(CancellationToken cancellationToken)
        {
            if (_autoCheckoutDevices == null || _autoCheckoutDevices.Count == 0 || _checkinApiFactory == null)
                return 0;

            // Filter to devices with auto-checkout enabled
            var activeDevices = _autoCheckoutDevices
                .Where(d => d.AutoCheckoutMinutes > 0 && !string.IsNullOrWhiteSpace(d.CheckinKey))
                .ToList();
            if (activeDevices.Count == 0)
                return 0;

            int totalCheckouts = 0;
            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            long todayStart = new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero).ToUnixTimeMilliseconds();

            // 1) Load all today's visits with a single API call
            List<VisitResult> allVisits;
            try
            {
                using (var api = _apiFactory())
                {
                    allVisits = await api.Visits.GetAllAsync("sync_from=" + todayStart, paginate: true);
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                Log?.Invoke("[AutoCheckout] Error loading visits: " + ex.Message);
                return 0;
            }

            if (allVisits == null || allVisits.Count == 0)
                return totalCheckouts;

            // Pre-filter: only active visits (not checked out, not rejected)
            var activeVisits = allVisits
                .Where(v => v.check_out_timestamp == 0 && v.check_in_timestamp > 0
                         && !string.Equals(v.status, ApiConstants.StatusRejected, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (activeVisits.Count == 0)
                return totalCheckouts;

            // 2) Process each device individually with its own AutoCheckoutMinutes threshold.
            //    Reuse API instances per CheckinKey to avoid redundant connections.
            var apiCache = new Dictionary<string, VirtuagymApiService>(StringComparer.Ordinal);

            try
            {
                foreach (var device in activeDevices)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    long maxAgeMs = (long)device.AutoCheckoutMinutes * 60_000;

                    // Filter visits for THIS device that exceed the threshold.
                    // v0 devices: Visit has device_id set directly by the API.
                    // v1 devices: device_id is empty; the device slug is stored in
                    //   status_message as "{deviceId} - Check-In" / "{deviceId} - Check-Out".
                    var expiredVisits = activeVisits
                        .Where(v => (now - v.check_in_timestamp) > maxAgeMs
                                 && MatchesDevice(v, device))
                        .ToList();

                    if (expiredVisits.Count == 0)
                        continue;

                    Log?.Invoke("[AutoCheckout] Device '" + device.DeviceId + "': "
                        + expiredVisits.Count + " expired visits (>" + device.AutoCheckoutMinutes + " min).");

                    // Get or create API instance for this CheckinKey
                    if (!apiCache.TryGetValue(device.CheckinKey, out var api))
                    {
                        api = _checkinApiFactory(device.CheckinKey);
                        apiCache[device.CheckinKey] = api;
                    }

                    foreach (var visit in expiredVisits)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        try
                        {
                            bool isSuccess;

                            if (device.ApiVersion == 0)
                            {
                                // v0 API: Toggle via PUT /devices (will check out active visit)
                                var result = await api.CheckinMemberAsync(null, visit.member_id.ToString());
                                isSuccess = result != null
                                    && (result.status == ApiConstants.StatusOk || result.status == ApiConstants.StatusWarn);
                            }
                            else
                            {
                                // v1 API: Explicit checkout via POST /visits
                                var checkoutRequest = new VisitRequest
                                {
                                    action = ApiConstants.VisitActionCheckout,
                                    member_id = visit.member_id.ToString(),
                                    status = ApiConstants.StatusOk
                                };
                                var result = await api.Visits.UpdateAsync(checkoutRequest);
                                isSuccess = result != null && result.id != 0;
                            }

                            if (isSuccess)
                            {
                                totalCheckouts++;
                                _cacheService?.UpdateVisitTimestamps(visit.member_id, 0, now, device.DeviceId, visit.id, device.ApiVersion);
                                _autoCheckoutRetryCount.Remove(visit.member_id);
                                Log?.Invoke("[AutoCheckout] Member " + visit.member_id
                                    + " checked out on device '" + device.DeviceId + "'"
                                    + " (v" + device.ApiVersion + ").");
                            }
                            else
                            {
                                // Mark as checked-out locally to prevent retry loops when the API
                                // consistently returns no valid visit (e.g. already checked out server-side).
                                _cacheService?.UpdateVisitTimestamps(visit.member_id, 0, now, device.DeviceId, visit.id, device.ApiVersion);
                                _autoCheckoutRetryCount.Remove(visit.member_id);
                                Log?.Invoke("[AutoCheckout] Member " + visit.member_id + ": Checkout API returned no valid visit. Marked as checked-out locally.");
                            }
                        }
                        catch (VirtuagymApiException apiEx) when (apiEx.ApiStatusCode == 400)
                        {
                            _cacheService?.UpdateVisitTimestamps(visit.member_id, 0, now, device.DeviceId, visit.id, device.ApiVersion);
                            _autoCheckoutRetryCount.Remove(visit.member_id);
                            Log?.Invoke("[AutoCheckout] Member " + visit.member_id + ": No active check-in in API. Reason: " + apiEx.Message);
                        }
                        catch (Exception ex)
                        {
                            _autoCheckoutRetryCount.TryGetValue(visit.member_id, out int retries);
                            retries++;
                            _autoCheckoutRetryCount[visit.member_id] = retries;

                            if (retries >= 3)
                            {
                                _cacheService?.UpdateVisitTimestamps(visit.member_id, 0, now, device.DeviceId, visit.id, device.ApiVersion);
                                _autoCheckoutRetryCount.Remove(visit.member_id);
                                Log?.Invoke("[AutoCheckout] Member " + visit.member_id + ": Max retries (3) reached. Marked as checked-out locally. Last error: " + ex.Message);
                            }
                            else
                            {
                                Log?.Invoke("[AutoCheckout] Member " + visit.member_id + ": Error (attempt " + retries + "/3): " + ex.Message);
                            }
                        }
                    }
                }
            }
            finally
            {
                // Dispose all cached API instances
                foreach (var api in apiCache.Values)
                    api?.Dispose();
            }

            return totalCheckouts;
        }

        /// <summary>
        /// Checks whether a visit belongs to the given device.
        /// v0 visits carry <see cref="VisitResult.device_id"/> directly.
        /// v1 visits have an empty device_id; the device slug is embedded in
        /// <see cref="VisitResult.status_message"/> as "{deviceId} - Check-In".
        /// </summary>
        private static bool MatchesDevice(VisitResult visit, AutoCheckoutDevice device)
        {
            // v0: direct device_id match
            if (!string.IsNullOrEmpty(visit.device_id))
                return string.Equals(visit.device_id, device.DeviceId, StringComparison.OrdinalIgnoreCase);

            // v1: check status_message for device slug prefix
            if (!string.IsNullOrEmpty(visit.status_message) && !string.IsNullOrEmpty(device.DeviceId))
                return visit.status_message.StartsWith(device.DeviceId, StringComparison.OrdinalIgnoreCase);

            return false;
        }

        /// <summary>
        /// Cancels the running synchronization.
        /// </summary>
        public void CancelSync()
        {
            _cts?.Cancel();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Stop();
            _cts?.Dispose();
            _autoCheckoutCts?.Dispose();
        }
    }
}
