using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Virtuagym.API.Serialization;
using Virtuagym.API.v1.Models;
using Virtuagym.API.Cache.Models;

namespace Virtuagym.API.Services
{
    /// <summary>
    /// JSON-Datei-basierter Member-Cache.
    /// Minimiert API-Requests beim Checkin durch lokale Zwischenspeicherung
    /// von Member-Daten und Besuchsinformationen.
    /// Supports offline check-ins: When the API is unreachable,
    /// check-ins are stored locally as "pending" and processed during the next successful
    /// Sync automatisch nachgeholt.
    /// </summary>
    public class MemberCacheService : IDisposable
    {
        private readonly string _filePath;
        private readonly object _lock = new object();
        private readonly JsonSerializerAdapter _json;
        private CacheData _data;
        private bool _disposed;

        /// <summary>Raised when a sync operation is started.</summary>
        public event Action<string> SyncStarted;

        /// <summary>Raised when a sync operation is completed.</summary>
        public event Action<string> SyncCompleted;

        /// <summary>Raised when an error occurs during synchronization.</summary>
        public event Action<string, Exception> SyncError;

        /// <summary>Raised to report synchronization progress (current, total).</summary>
        public event Action<int, int> SyncProgress;

        /// <summary>
        /// Erstellt einen neuen MemberCacheService.
        /// </summary>
        /// <param name="dbPath">Pfad zur Cache-Datei. Standard: membercache.json im Anwendungsverzeichnis.</param>
        public MemberCacheService(string dbPath = null)
        {
            _filePath = dbPath ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ApiConstants.MemberCacheFilePath);
            _json = new JsonSerializerAdapter();
            LoadFromDisk();
        }

        #region Persistence

        private void LoadFromDisk()
        {
            lock (_lock)
            {
                if (File.Exists(_filePath))
                {
                    try
                    {
                        string content = File.ReadAllText(_filePath);
                        _data = _json.Deserialize<CacheData>(content);
                    }
                    catch
                    {
                        _data = null;
                    }
                }

                if (_data == null)
                    _data = new CacheData();

                if (_data.Members == null)
                    _data.Members = new List<MemberCacheEntry>();

                if (_data.Meta == null)
                    _data.Meta = new Dictionary<string, string>();

                if (_data.PendingCheckins == null)
                    _data.PendingCheckins = new List<PendingCheckinEntry>();
            }
        }

        private void SaveToDisk()
        {
            lock (_lock)
            {
                string dir = Path.GetDirectoryName(_filePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                string content = _json.Serialize(_data);
                File.WriteAllText(_filePath, content);
            }
        }

        #endregion

        #region Lookup

        /// <summary>
        /// Sucht ein Mitglied anhand des RFID-Tags im Cache.
        /// Sucht zuerst im lokalen rfid_tag, dann im rfid_tag_virtuagym.
        /// </summary>
        /// <returns>Cache-Eintrag oder null wenn nicht gefunden.</returns>
        public MemberCacheEntry GetByRfidTag(string rfidTag)
        {
            if (string.IsNullOrWhiteSpace(rfidTag)) return null;

            lock (_lock)
            {
                var entry = _data.Members.FirstOrDefault(m =>
                    string.Equals(m.RfidTag, rfidTag, StringComparison.OrdinalIgnoreCase));
                return entry?.Clone();
            }
        }

        /// <summary>
        /// Sucht ein Mitglied anhand der Member-ID im Cache.
        /// Returns a copy so the caller cannot mutate internal data.
        /// </summary>
        public MemberCacheEntry GetByMemberId(long memberId)
        {
            lock (_lock)
            {
                var entry = _data.Members.FirstOrDefault(m => m.MemberId == memberId);
                return entry?.Clone();
            }
        }

        /// <summary>
        /// Returns all cached members.
        /// </summary>
        public List<MemberCacheEntry> GetAll()
        {
            lock (_lock)
            {
                return _data.Members
                    .OrderBy(m => m.Lastname)
                    .ThenBy(m => m.Firstname)
                    .ToList();
            }
        }

        /// <summary>
        /// Returns the number of cached members.
        /// </summary>
        public int GetCount()
        {
            lock (_lock)
            {
                return _data.Members.Count;
            }
        }

        #endregion

        #region Query

        /// <summary>
        /// Returns all members that are checked in at a specific device
        /// and whose check-in is older than maxAgeMs milliseconds.
        /// </summary>
        /// <param name="deviceId">Device UUID (CheckinClientMapping.Uuid).</param>
        /// <param name="maxAgeMs">Maximales Alter des Check-ins in Millisekunden.</param>
        /// <returns>Liste von (MemberId, CheckInTimestamp) Paaren.</returns>
        public List<KeyValuePair<long, long>> GetExpiredCheckins(string deviceId, long maxAgeMs)
        {
            var result = new List<KeyValuePair<long, long>>();
            if (string.IsNullOrEmpty(deviceId) || maxAgeMs <= 0) return result;

            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            lock (_lock)
            {
                foreach (var member in _data.Members)
                {
                    if (member.DeviceCheckins == null) continue;
                    var dev = member.DeviceCheckins.FirstOrDefault(d =>
                        string.Equals(d.DeviceId, deviceId, StringComparison.Ordinal));
                    if (dev != null && dev.CheckInTimestamp > 0 && dev.CheckOutTimestamp == 0
                        && (now - dev.CheckInTimestamp) > maxAgeMs)
                    {
                        result.Add(new KeyValuePair<long, long>(member.MemberId, dev.CheckInTimestamp));
                    }
                }
            }
            return result;
        }

        #endregion

        #region Update

        /// <summary>
        /// Aktualisiert den Check-in/Check-out-Zeitstempel eines Mitglieds im Cache.
        /// Wird nach jedem erfolgreichen Checkin/Checkout aufgerufen.
        /// </summary>
        /// <param name="memberId">Virtuagym Member-ID.</param>
        /// <param name="checkInTimestamp">Check-in-Zeitstempel (0 = kein aktiver Besuch).</param>
        /// <param name="checkOutTimestamp">Check-out-Zeitstempel (0 = noch eingecheckt).</param>
        /// <param name="deviceId">
        /// Optional device ID (e.g. CheckinClientMapping.Name or DeviceID).
        /// If specified, the check-in timestamp is additionally stored per device,
        /// so the double-scan protection works per device and a member can check in at multiple devices
        /// innerhalb kurzer Zeit anmelden kann.
        /// </param>
        /// <param name="visitId">
        /// Optional visit ID (returned by the v1 API after POST /visits).
        /// Stored per device so the correct visit can be checked out during offline checkout.
        /// Retained after checkout (never reset).
        /// </param>
        public void UpdateVisitTimestamps(long memberId, long checkInTimestamp, long checkOutTimestamp, string deviceId = null, long? visitId = null, double apiVersion = 0)
        {
            lock (_lock)
            {
                var entry = _data.Members.FirstOrDefault(m => m.MemberId == memberId);
                if (entry != null)
                {
                    // Set device-specific data
                    if (!string.IsNullOrEmpty(deviceId))
                    {
                        if (entry.DeviceCheckins == null)
                            entry.DeviceCheckins = new List<DeviceCheckinData>();

                        var existing = entry.DeviceCheckins.FirstOrDefault(d =>
                            string.Equals(d.DeviceId, deviceId, StringComparison.Ordinal));
                        if (existing != null)
                        {
                            existing.CheckInTimestamp = checkInTimestamp;
                            existing.CheckOutTimestamp = checkOutTimestamp;
                            existing.ApiVersion = apiVersion;
                            // VisitId: Only update when a new value is provided, never reset.
                            // Bleibt als historischer Wert erhalten, damit nachvollziehbar ist,
                            // which visit occurred on this device.
                            if (visitId.HasValue && visitId.Value != 0)
                                existing.VisitId = visitId;
                        }
                        else
                        {
                            entry.DeviceCheckins.Add(new DeviceCheckinData
                            {
                                DeviceId = deviceId,
                                CheckInTimestamp = checkInTimestamp,
                                CheckOutTimestamp = checkOutTimestamp,
                                ApiVersion = apiVersion,
                                VisitId = visitId
                            });
                        }
                    }
                }
            }
            SaveToDisk();
        }

        /// <summary>
        /// Updates the credit data of a member for a specific service in the cache.
        /// </summary>
        public void UpdateCredits(long memberId, int creditAmount, bool creditUnlimited, string serviceId)
        {
            UpdateCredits(memberId, creditAmount, creditUnlimited, serviceId, null,
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        }

        /// <summary>
        /// Updates the credit data of a member for a specific service in the cache
        /// und setzt optional den letzten Credits-Sync-Zeitpunkt.
        /// </summary>
        public void UpdateCredits(long memberId, int creditAmount, bool creditUnlimited, string serviceId, long creditsSyncTimestamp)
        {
            UpdateCredits(memberId, creditAmount, creditUnlimited, serviceId, null, creditsSyncTimestamp);
        }

        /// <summary>
        /// Updates the credit data of a member for a specific service in the cache
        /// und setzt optional den Servicenamen und den letzten Credits-Sync-Zeitpunkt.
        /// </summary>
        public void UpdateCredits(long memberId, int creditAmount, bool creditUnlimited, string serviceId, string serviceName, long creditsSyncTimestamp)
        {
            lock (_lock)
            {
                var entry = _data.Members.FirstOrDefault(m => m.MemberId == memberId);
                if (entry != null)
                {
                    if (entry.ServiceCredits == null)
                        entry.ServiceCredits = new List<ClubServiceCredits>();

                    var existing = entry.ServiceCredits.FirstOrDefault(c =>
                        string.Equals(c.service_id, serviceId, StringComparison.OrdinalIgnoreCase));
                    if (existing != null)
                    {
                        existing.CreditAmount = creditAmount;
                        existing.CreditUnlimited = creditUnlimited;
                        if (!string.IsNullOrEmpty(serviceName))
                            existing.servicename = serviceName;
                    }
                    else
                    {
                        entry.ServiceCredits.Add(new ClubServiceCredits
                        {
                            service_id = serviceId,
                            servicename = serviceName,
                            CreditAmount = creditAmount,
                            CreditUnlimited = creditUnlimited
                        });
                    }

                    if (creditsSyncTimestamp > 0)
                        entry.CreditsLastSyncTimestamp = creditsSyncTimestamp;
                }
            }
            SaveToDisk();
        }

        /// <summary>
        /// Aktualisiert den RFID-Tag eines Members im Cache.
        /// </summary>
        public void UpdateRfidTag(long memberId, string rfidTag)
        {
            lock (_lock)
            {
                var entry = _data.Members.FirstOrDefault(m => m.MemberId == memberId);
                if (entry != null)
                {
                    entry.RfidTag = rfidTag;
                }
            }
            SaveToDisk();
        }

        /// <summary>
        /// Aktualisiert die Avatar-URL eines Members im Cache.
        /// Wird beim Checkin aufgerufen, wenn die API eine Avatar-URL liefert.
        /// </summary>
        public void UpdateAvatar(long memberId, string avatarUrl)
        {
            if (string.IsNullOrEmpty(avatarUrl)) return;

            lock (_lock)
            {
                var entry = _data.Members.FirstOrDefault(m => m.MemberId == memberId);
                if (entry != null && entry.AvatarUrl != avatarUrl)
                {
                    entry.AvatarUrl = avatarUrl;
                }
            }
            SaveToDisk();
        }

        /// <summary>
        /// Downloads the avatar image from the URL and saves it locally.
        /// Aktualisiert den AvatarLocalPath im Cache-Eintrag.
        /// Der Dateiname ist pro Member eindeutig ({memberId}.jpg).
        /// The image is only re-downloaded if the AvatarUrl has changed
        /// oder die lokale Datei nicht existiert.
        /// </summary>
        /// <param name="memberId">Virtuagym Member-ID.</param>
        /// <param name="avatarUrl">Avatar-URL (absolut).</param>
        /// <param name="avatarDirectory">Directory for local avatar files. Default: Resources\Avatars.</param>
        /// <returns>Lokaler Dateipfad zum Avatar oder null bei Fehler.</returns>
        public async Task<string> DownloadAndCacheAvatarAsync(long memberId, string avatarUrl, string avatarDirectory = null)
        {
            if (memberId == 0 || string.IsNullOrEmpty(avatarUrl)) return null;

            string dir = avatarDirectory ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ApiConstants.AvatarCacheFolderPath);
            string localPath = Path.Combine(dir, memberId + ".jpg");

            // Check if the same URL is already saved locally
            string cachedUrl;
            string cachedLocalPath;
            lock (_lock)
            {
                var entry = _data.Members.FirstOrDefault(m => m.MemberId == memberId);
                cachedUrl = entry?.AvatarUrl;
                cachedLocalPath = entry?.AvatarLocalPath;
            }

            bool urlUnchanged = string.Equals(cachedUrl, avatarUrl, StringComparison.Ordinal);
            if (urlUnchanged && !string.IsNullOrEmpty(cachedLocalPath) && File.Exists(localPath))
                return localPath;

            try
            {
                if (!Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                using (var http = new System.Net.Http.HttpClient())
                {
                    var bytes = await http.GetByteArrayAsync(avatarUrl);
                    if (bytes != null && bytes.Length > 0)
                    {
                        File.WriteAllBytes(localPath, bytes);

                        lock (_lock)
                        {
                            var entry = _data.Members.FirstOrDefault(m => m.MemberId == memberId);
                            if (entry != null)
                            {
                                entry.AvatarUrl = avatarUrl;
                                entry.AvatarLocalPath = localPath;
                            }
                        }
                        SaveToDisk();
                        return localPath;
                    }
                }
            }
            catch
            {
                // Download fehlgeschlagen – bestehenden lokalen Pfad beibehalten falls vorhanden
            }

            return File.Exists(localPath) ? localPath : null;
        }

        /// <summary>
        /// Returns the local avatar path for a member, if available.
        /// </summary>
        public string GetAvatarLocalPath(long memberId)
        {
            lock (_lock)
            {
                var entry = _data.Members.FirstOrDefault(m => m.MemberId == memberId);
                return entry?.AvatarLocalPath;
            }
        }

        /// <summary>
        /// Speichert oder aktualisiert einen einzelnen Cache-Eintrag.
        /// The rfid_tag field is NOT overwritten on update (only on insert).
        /// </summary>
        public void Upsert(MemberCacheEntry entry)
        {
            UpsertInternal(entry);
            SaveToDisk();
        }

        private void UpsertInternal(MemberCacheEntry entry)
        {
            lock (_lock)
            {
                var existing = _data.Members.FirstOrDefault(m => m.MemberId == entry.MemberId);
                if (existing != null)
                {
                    existing.UserId = entry.UserId;
                    existing.RfidTag = entry.RfidTag;
                    existing.ClubId = entry.ClubId;
                    existing.Active = entry.Active;
                    existing.Firstname = entry.Firstname;
                    existing.Lastname = entry.Lastname;
                    existing.Email = entry.Email;
                    existing.Phone = entry.Phone;
                    existing.LastActive = entry.LastActive;
                    existing.TimestampEdit = entry.TimestampEdit;

                    // Avatar: Only overwrite when entry provides new data
                    if (!string.IsNullOrEmpty(entry.AvatarUrl))
                        existing.AvatarUrl = entry.AvatarUrl;
                    if (!string.IsNullOrEmpty(entry.AvatarLocalPath))
                        existing.AvatarLocalPath = entry.AvatarLocalPath;

                    // ServiceCredits: Only overwrite when entry provides new data
                    if (entry.ServiceCredits != null && entry.ServiceCredits.Count > 0)
                        existing.ServiceCredits = entry.ServiceCredits;

                    // DeviceCheckins are NOT overwritten during sync,
                    // because the API does not provide device-specific timestamps.
                    // Bestehende Werte bleiben erhalten (werden nur von UpdateVisitTimestamps gesetzt).
                }
                else
                {
                    // INSERT: rfid_tag is set initially
                    _data.Members.Add(entry);
                }
            }
        }

        #endregion

        #region Sync

        /// <summary>
        /// Full synchronization of all members from the API.
        /// Avatar und Credits werden NICHT im Sync geladen (Rate-Limit-Schutz),
        /// sondern beim Checkin per Lazy Loading nachgeladen.
        /// The rfid_tag field is NOT overwritten for existing entries.
        /// </summary>
        /// <param name="api">VirtuagymApiService-Instanz.</param>
        /// <param name="cancellationToken">Token to cancel the synchronization.</param>
        /// <param name="checkinKeys">Optional list of CheckinKeys (club secrets) of all configured devices.
        /// Required to extract the device ID from the secret and assign visits to devices.</param>
        /// <returns>Number of synchronized members.</returns>
        public async Task<int> SyncFromApiAsync(VirtuagymApiService api, CancellationToken cancellationToken = default,
            List<string> checkinKeys = null)
        {
            ArgumentNullException.ThrowIfNull(api);

            SyncStarted?.Invoke("Member synchronization started...");

            try
            {
                // 1) Alle Mitglieder laden
                cancellationToken.ThrowIfCancellationRequested();
                var members = await api.GetMembersAsync(paginate: true);
                if (members == null)
                {
                    SyncCompleted?.Invoke("No members received from API.");
                    return 0;
                }

                SyncStarted?.Invoke(members.Count + " members loaded. Updating cache...");

                // 2) Create/update cache entries (without additional API requests)
                cancellationToken.ThrowIfCancellationRequested();
                int count = 0;

                foreach (var member in members)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var entry = new MemberCacheEntry
                    {
                        MemberId = member.member_id,
                        UserId = member.user_id,
                        RfidTag = member.rfid_tag,
                        ClubId = member.club_id,
                        Active = member.active,
                        Firstname = member.firstname,
                        Lastname = member.lastname,
                        Email = member.email,
                        Phone = member.phone,
                        LastActive = member.timestamp_edit,
                        TimestampEdit = member.timestamp_edit

                    };

                    // Avatar und Credits werden NICHT im Sync geladen (Rate-Limit 500/h).
                    // Stattdessen Lazy Loading beim Checkin:
                    //   - Avatar comes via the checkin API response
                    //   - Credits are loaded via API on demand in the CheckinHandler
                    // Preserve existing cache values:
                    if (member.active)
                    {
                        MemberCacheEntry cached;
                        lock (_lock)
                        {
                            cached = _data.Members.FirstOrDefault(m => m.MemberId == member.member_id);
                        }
                        if (cached != null)
                        {
                            entry.AvatarUrl = cached.AvatarUrl;
                            entry.AvatarLocalPath = cached.AvatarLocalPath;
                            if (cached.ServiceCredits != null && cached.ServiceCredits.Count > 0)
                                entry.ServiceCredits = cached.ServiceCredits;
                        }
                    }

                    UpsertInternal(entry);
                    count++;
                    SyncProgress?.Invoke(count, members.Count);
                }

                // 3) Load today's visits (only check_out_timestamp == 0)
                // Assignment to member is done via member_id + device_id (extracted from CheckinKey)
                cancellationToken.ThrowIfCancellationRequested();
                SyncStarted?.Invoke("Synchronizing visits...");

                long todayStart = new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero).ToUnixTimeMilliseconds();
                var visits = await api.GetVisitsAsync("sync_from=" + todayStart, paginate: true);

                // Extract device IDs from CheckinKeys
                var deviceIds = new List<string>();
                if (checkinKeys != null)
                {
                    foreach (var key in checkinKeys)
                    {
                        var devId = VirtuagymApiBase.ExtractDeviceId(key);
                        if (!string.IsNullOrEmpty(devId) && !deviceIds.Contains(devId))
                            deviceIds.Add(devId);
                    }
                }

                int visitCount = 0;
                if (visits != null && visits.Count > 0)
                {
                    // Only active visits (check_out_timestamp == 0, not rejected)
                    var activeVisits = visits.Where(v => v.check_out_timestamp == 0
                        && !string.Equals(v.status, ApiConstants.StatusRejected, StringComparison.OrdinalIgnoreCase)).ToList();

                    // Group by member_id: Newest check_in_timestamp wins.
                    // Visits WITH device_id are only assigned to the matching device (v0 check-ins),
                    // Visits WITHOUT device_id (v1 check-ins) are assigned to all configured devices.
                    var visitsByMember = new Dictionary<long, VisitResult>();
                    foreach (var v in activeVisits)
                    {
                        if (!visitsByMember.ContainsKey(v.member_id)
                            || v.check_in_timestamp > visitsByMember[v.member_id].check_in_timestamp)
                        {
                            visitsByMember[v.member_id] = v;
                        }
                    }

                    // Assign visits to members in cache
                    lock (_lock)
                    {
                        foreach (var kvp in visitsByMember)
                        {
                            var cached = _data.Members.FirstOrDefault(m => m.MemberId == kvp.Key);
                            if (cached != null)
                            {
                                var visit = kvp.Value;
                                long incomingVisitTs = Math.Max(visit.check_in_timestamp, visit.check_out_timestamp);

                                // Set device-specific timestamps.
                                //
                                // v0-Visits: Die Visits-API liefert visit.device_id mit der numerischen
                                //   ID aus dem CheckinKey (z.B. "54042"). Diese entspricht der
                                //   EffectiveDeviceId of the mapping → visit is only assigned to the matching device
                                //   assigned.
                                //
                                // v1-Visits: Die Visits-API liefert kein device_id, da die v1 API
                                //   does not have this concept when creating. The device info is
                                //   in the status_message field instead (e.g. "@eingang_haupttuer - Check-In"),
                                //   which is not evaluated here. Therefore the visit is assigned to all
                                //   configured devices (fallback).
                                List<string> targetDevIds;
                                if (!string.IsNullOrEmpty(visit.device_id) && deviceIds.Contains(visit.device_id))
                                {
                                    // v0 visit: Only assign to the matching device
                                    targetDevIds = new List<string> { visit.device_id };
                                }
                                else
                                {
                                    // v1 visit (no device_id): Assign to all devices
                                    targetDevIds = deviceIds;
                                }

                                // API version: v0 visits have a device_id, v1 visits do not
                                double visitApiVersion = !string.IsNullOrEmpty(visit.device_id) ? 0 : 1;

                                foreach (var devId in targetDevIds)
                                {
                                    if (cached.DeviceCheckins == null)
                                        cached.DeviceCheckins = new List<DeviceCheckinData>();

                                    var devData = cached.DeviceCheckins.FirstOrDefault(d =>
                                        string.Equals(d.DeviceId, devId, StringComparison.Ordinal));

                                    if (devData != null)
                                    {
                                        long existingDevTs = Math.Max(devData.CheckInTimestamp, devData.CheckOutTimestamp);
                                        if (incomingVisitTs >= existingDevTs)
                                        {
                                            devData.CheckInTimestamp = visit.check_in_timestamp;
                                            devData.CheckOutTimestamp = visit.check_out_timestamp;
                                            devData.ApiVersion = visitApiVersion;
                                        }
                                    }
                                    else
                                    {
                                        cached.DeviceCheckins.Add(new DeviceCheckinData
                                        {
                                            DeviceId = devId,
                                            CheckInTimestamp = visit.check_in_timestamp,
                                            CheckOutTimestamp = visit.check_out_timestamp,
                                            ApiVersion = visitApiVersion
                                        });
                                    }
                                }

                                visitCount++;
                            }
                        }
                    }
                }

                // 3b) Clean up orphaned check-ins
                //     If a visit was deleted online (e.g. manually in the backend), it is no longer
                //     in the API response. Local DeviceCheckins that are still considered active
                //     (CheckInTimestamp > 0, CheckOutTimestamp == 0) but have no matching
                //     API visit are reset.
                cancellationToken.ThrowIfCancellationRequested();
                int orphanedCount = 0;
                if (visits != null)
                {
                    // Set of all active (member_id, device_id) combinations from the API
                    var activeVisitKeys = new HashSet<(long memberId, string deviceId)>();
                    if (visits.Count > 0)
                    {
                        foreach (var v in visits.Where(v => v.check_out_timestamp == 0
                            && !string.Equals(v.status, ApiConstants.StatusRejected, StringComparison.OrdinalIgnoreCase)))
                        {
                            if (!string.IsNullOrEmpty(v.device_id) && deviceIds.Contains(v.device_id))
                            {
                                activeVisitKeys.Add((v.member_id, v.device_id));
                            }
                            else
                            {
                                // v1 visit without device_id → applies to all devices
                                foreach (var devId in deviceIds)
                                    activeVisitKeys.Add((v.member_id, devId));
                            }
                        }
                    }

                    lock (_lock)
                    {
                        foreach (var member in _data.Members)
                        {
                            if (member.DeviceCheckins == null) continue;
                            foreach (var dc in member.DeviceCheckins)
                            {
                                if (dc.CheckInTimestamp > 0 && dc.CheckOutTimestamp == 0
                                    && !activeVisitKeys.Contains((member.MemberId, dc.DeviceId)))
                                {
                                    dc.CheckInTimestamp = 0;
                                    dc.CheckOutTimestamp = 0;
                                    orphanedCount++;
                                }
                            }
                        }
                    }
                }

                // 4) Remove deleted members from cache
                //    If a user was deleted in Virtuagym, they are no longer in the API response
                //    and must be removed from the local cache.
                cancellationToken.ThrowIfCancellationRequested();
                var apiMemberIds = new HashSet<long>(members.Select(m => m.member_id));
                int removedCount;
                lock (_lock)
                {
                    removedCount = _data.Members.RemoveAll(m => !apiMemberIds.Contains(m.MemberId));
                }

                // Save to disk once (instead of per entry)
                lock (_lock)
                {
                    _data.Meta["last_sync"] = DateTime.UtcNow.ToString("o");
                    _data.Meta["last_sync_count"] = count.ToString();
                    if (removedCount > 0)
                        _data.Meta["last_sync_removed"] = removedCount.ToString();
                }
                SaveToDisk();

                string msg = count + " members synchronized (" + visitCount + " active visits"
                    + (orphanedCount > 0 ? ", " + orphanedCount + " orphaned check-ins cleaned up" : "")
                    + (removedCount > 0 ? ", " + removedCount + " removed" : "") + ").";
                SyncCompleted?.Invoke(msg);
                return count;
            }
            catch (OperationCanceledException)
            {
                SyncCompleted?.Invoke("Synchronization cancelled.");
                throw;
            }
            catch (Exception ex)
            {
                SyncError?.Invoke("Synchronization error: " + ex.Message, ex);
                throw;
            }
        }

        #endregion

        #region Meta

        /// <summary>
        /// Returns the time of the last successful synchronization.
        /// </summary>
        public DateTime? GetLastSyncTime()
        {
            lock (_lock)
            {
                if (!_data.Meta.TryGetValue("last_sync", out string value))
                    return null;

                if (!string.IsNullOrEmpty(value) && DateTime.TryParse(value, null,
                    System.Globalization.DateTimeStyles.RoundtripKind, out DateTime dt))
                {
                    return dt.ToLocalTime();
                }
                return null;
            }
        }
        #endregion

        #region Maintenance

        /// <summary>
        /// Deletes a single member from the cache by member ID.
        /// </summary>
        /// <returns>True if the member was found and deleted.</returns>
        public bool DeleteByMemberId(long memberId)
        {
            bool removed;
            lock (_lock)
            {
                removed = _data.Members.RemoveAll(m => m.MemberId == memberId) > 0;
            }
            if (removed)
                SaveToDisk();
            return removed;
        }

        /// <summary>
        /// Clears the entire cache.
        /// </summary>
        public void Clear()
        {
            lock (_lock)
            {
                _data.Members.Clear();
                _data.Meta.Clear();
            }
            SaveToDisk();
        }

        /// <summary>
        /// Removes cached ServiceCredits and device-specific check-in data from all members.
        /// </summary>
        /// <returns>Number of modified member entries.</returns>
        public int ClearServiceCreditsAndDeviceCheckins()
        {
            int changed = 0;
            lock (_lock)
            {
                foreach (var member in _data.Members)
                {
                    bool hasCredits = member.ServiceCredits != null && member.ServiceCredits.Count > 0;
                    bool hasDeviceCheckins = member.DeviceCheckins != null && member.DeviceCheckins.Count > 0;

                    if (hasCredits || hasDeviceCheckins)
                    {
                        member.ServiceCredits = null;
                        member.DeviceCheckins = null;
                        changed++;
                    }
                }
            }

            if (changed > 0)
                SaveToDisk();

            return changed;
        }

        /// <summary>
        /// Returns the path to the cache file.
        /// </summary>
        public string DatabasePath { get { return _filePath; } }

        #endregion

        #region Offline Checkin

        /// <summary>
        /// Saves an offline check-in or check-out as "pending" in the local cache.
        /// Called when the API is unreachable but the member was found in the cache.
        /// The pending entries will be processed during the next successful sync.
        /// </summary>
        /// <param name="memberId">Virtuagym Member-ID.</param>
        /// <param name="rfidTag">RFID-Tag oder QR-Code des Members.</param>
        /// <param name="deviceId">Device ID (e.g. mapping name).</param>
        /// <param name="timestamp">Time of the offline check-in (Unix milliseconds).</param>
        /// <param name="action">Action: "checkin" or "checkout". Null = Toggle (legacy).</param>
        /// <param name="checkinKey">Club secret of the device (CheckinKey). Used during sync for device assignment.</param>
        /// <param name="visitId">Visit ID of the active visit (only relevant for checkout). Enables precise checkout during sync.</param>
        public void AddPendingCheckin(long memberId, long userId, string rfidTag, string deviceId, long timestamp, string action = null, string checkinKey = null, long? visitId = null)
        {
            lock (_lock)
            {
                if (_data.PendingCheckins == null)
                    _data.PendingCheckins = new List<PendingCheckinEntry>();

                _data.PendingCheckins.Add(new PendingCheckinEntry
                {
                    MemberId = memberId,
                    UserId = userId,
                    RfidTag = rfidTag,
                    DeviceId = deviceId,
                    Timestamp = timestamp,
                    Action = action,
                    CheckinKey = checkinKey,
                    VisitId = visitId
                });
            }
            SaveToDisk();
        }

        /// <summary>
        /// Returns all pending offline check-ins.
        /// </summary>
        public List<PendingCheckinEntry> GetPendingCheckins()
        {
            lock (_lock)
            {
                if (_data.PendingCheckins == null)
                    return new List<PendingCheckinEntry>();

                return _data.PendingCheckins.ToList();
            }
        }

        /// <summary>
        /// Returns the number of pending offline check-ins.
        /// </summary>
        public int GetPendingCheckinCount()
        {
            lock (_lock)
            {
                return _data.PendingCheckins?.Count ?? 0;
            }
        }

        /// <summary>
        /// Removes a single pending check-in after successful synchronization.
        /// </summary>
        /// <returns>True if the entry was found and removed.</returns>
        public bool RemovePendingCheckin(long memberId, long timestamp)
        {
            bool removed;
            lock (_lock)
            {
                if (_data.PendingCheckins == null)
                    return false;

                removed = _data.PendingCheckins.RemoveAll(p =>
                    p.MemberId == memberId && p.Timestamp == timestamp) > 0;
            }
            if (removed)
                SaveToDisk();
            return removed;
        }

        /// <summary>
        /// Entfernt alle ausstehenden Offline-Checkins (z.B. nach erfolgreichem Sync).
        /// </summary>
        public void ClearPendingCheckins()
        {
            lock (_lock)
            {
                _data.PendingCheckins?.Clear();
            }
            SaveToDisk();
        }

        /// <summary>
        /// Synchronizes all pending offline check-ins/check-outs with the API.
        /// Entries with an explicit action (checkin/checkout) use the v1 API (Visits),
        /// legacy entries without action use the v0 toggle (CheckinMemberAsync).
        /// For each entry the API instance is created based on the stored CheckinKey,
        /// so Virtuagym assigns the check-in to the correct device (checkin client).
        /// Successfully synchronized entries are removed from the pending queue.
        /// </summary>
        /// <param name="api">Default VirtuagymApiService instance (for entries without CheckinKey).</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <param name="apiFactory">
        /// Optional factory that creates a device-specific VirtuagymApiService instance
        /// based on a CheckinKey (club secret). If null, the default API instance is used for all entries.
        /// </param>
        /// <returns>Number of successfully synchronized entries.</returns>
        public async Task<int> SyncPendingCheckinsAsync(VirtuagymApiService api, CancellationToken cancellationToken = default,
            Func<string, VirtuagymApiService> apiFactory = null)
        {
            ArgumentNullException.ThrowIfNull(api);

            var pending = GetPendingCheckins();
            if (pending.Count == 0)
                return 0;

            SyncStarted?.Invoke(pending.Count + " pending offline check-ins are being synchronized...");

            int synced = 0;
            // Remember visit IDs from check-in syncs so subsequent checkouts
            // of the same member in the same batch can check out the correct visit.
            var visitIdsByMember = new Dictionary<long, long>();

            foreach (var entry in pending)
            {
                cancellationToken.ThrowIfCancellationRequested();
                VirtuagymApiService deviceApi = null;
                try
                {
                    // Create device-specific API instance if CheckinKey is available
                    bool ownsDeviceApi = false;
                    VirtuagymApiService effectiveApi;
                    if (!string.IsNullOrEmpty(entry.CheckinKey) && apiFactory != null)
                    {
                        deviceApi = apiFactory(entry.CheckinKey);
                        effectiveApi = deviceApi;
                        ownsDeviceApi = true;
                    }
                    else
                    {
                        effectiveApi = api;
                    }

                    bool success = false;

                    if (entry.Action == ApiConstants.ActionCheckin)
                    {
                        // Explicit check-in via v1 API (POST /visits).
                        //
                        // Device mapping (device_id):
                        //   v0 (CheckinKey present): The numeric device_id is extracted from the
                        //       CheckinKey (e.g. "54042" from "CS-681264-CHECKIN54042-...") and sent
                        //       in the device_id field of the visit object. Virtuagym automatically
                        //       assigns the visit to the correct checkin client using this ID.
                        //
                        //   v1 (no CheckinKey): The v1 API has no device_id concept when creating
                        //       a visit. To keep the check-in source identifiable (e.g. in reports
                        //       or the Virtuagym backend), a slug derived from the device name
                        //       (e.g. "@eingang_haupttuer") is sent in the status_message field.
                        //       Format: "@slug - Check-In".
                        //       The Visits API does not return a device_id for such visits, so
                        //       SyncFromApiAsync assigns them to all configured devices.
                        string numericDevId = VirtuagymApiBase.ExtractDeviceId(entry.CheckinKey);
                        var visit = new v1.Models.VisitResult
                        {
                            member_id = entry.MemberId,
                            check_in_timestamp = entry.Timestamp,
                            device_id = numericDevId,
                            status_message = string.IsNullOrEmpty(numericDevId) && !string.IsNullOrEmpty(entry.DeviceId)
                                ? entry.DeviceId + " - Check-In"
                                : null
                        };
                        var result = await effectiveApi.Visits.CreateAsync(visit);
                        success = result != null && result.id != 0;

                        // Remember visit ID for subsequent checkouts in the same batch
                        if (success && result.id != 0)
                            visitIdsByMember[entry.MemberId] = result.id;
                    }
                    else if (entry.Action == ApiConstants.ActionCheckout)
                    {
                        // Explicit check-out via v1 API.
                        // Visit ID sources (priority):
                        //   1. Stored in PendingCheckinEntry (from online check-in or cache)
                        //   2. From a previous check-in sync in the same batch
                        //   3. Fallback: Search for active visit via API
                        long knownVisitId = entry.VisitId.GetValueOrDefault();
                        if (knownVisitId == 0)
                            visitIdsByMember.TryGetValue(entry.MemberId, out knownVisitId);

                        // status_message for v1 checkouts (no CheckinKey → no device_id):
                        // Analogous to check-in, the device slug is sent in the status_message field
                        // so the check-out source remains identifiable in the Virtuagym backend.
                        // Format: "@slug - Check-Out" (e.g. "@eingang_haupttuer - Check-Out").
                        string checkoutStatusMsg = string.IsNullOrEmpty(VirtuagymApiBase.ExtractDeviceId(entry.CheckinKey))
                            && !string.IsNullOrEmpty(entry.DeviceId)
                                ? entry.DeviceId + " - Check-Out"
                                : null;

                        if (knownVisitId != 0)
                        {
                            // Precise checkout: Visit ID is known
                            var visit = new { member_id = entry.MemberId, check_out_timestamp = entry.Timestamp, status_message = checkoutStatusMsg };
                            var result = await effectiveApi.Visits.UpdateAsync(visit);
                            success = result != null;
                        }
                        else
                        {
                            // Fallback: Search for active visit via API
                            var activeVisits = await effectiveApi.Visits.GetActiveMemberVisitAsync(entry.MemberId);
                            if (activeVisits != null && activeVisits.Count > 0)
                            {
                                var visit = new { member_id = entry.MemberId, check_out_timestamp = entry.Timestamp, status_message = checkoutStatusMsg };
                                var result = await effectiveApi.Visits.UpdateAsync(visit);
                                success = result != null;
                            }
                            else
                            {
                                // No active visit → discard entry (already checked out)
                                success = true;
                            }
                        }

                        // After successful checkout, remove the stored visit ID
                        if (success)
                            visitIdsByMember.Remove(entry.MemberId);
                    }
                    else
                    {
                        // Legacy entry without action → v0 toggle as fallback
                        var result = await effectiveApi.CheckinMemberAsync(entry.RfidTag, entry.MemberId.ToString());
                        success = result != null && (result.status == "ok" || result.status == "warn");
                    }

                    if (success)
                    {
                        RemovePendingCheckin(entry.MemberId, entry.Timestamp);
                        synced++;
                    }

                    if (ownsDeviceApi)
                        deviceApi?.Dispose();
                    deviceApi = null;
                }
                catch (Exception ex)
                {
                    deviceApi?.Dispose();

                    entry.SyncRetryCount++;
                    if (entry.SyncRetryCount >= 3)
                    {
                        RemovePendingCheckin(entry.MemberId, entry.Timestamp);
                        SyncError?.Invoke("Offline check-in for member " + entry.MemberId
                            + " discarded after 3 attempts: " + ex.Message, ex);
                    }
                    else
                    {
                        SaveToDisk();
                        SyncError?.Invoke("Offline check-in for member " + entry.MemberId
                            + " failed (attempt " + entry.SyncRetryCount + "/3): " + ex.Message, ex);
                    }
                }
            }

            if (synced > 0)
                SyncCompleted?.Invoke(synced + " of " + pending.Count + " offline check-ins synchronized.");

            return synced;
        }

        #endregion

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
        }

        #region Internal Model

        /// <summary>
        /// Internal data model for JSON serialization.
        /// </summary>
        private class CacheData
        {
            public List<MemberCacheEntry> Members { get; set; }
            public Dictionary<string, string> Meta { get; set; }
            public List<PendingCheckinEntry> PendingCheckins { get; set; }

            public CacheData()
            {
                Members = new List<MemberCacheEntry>();
                Meta = new Dictionary<string, string>();
                PendingCheckins = new List<PendingCheckinEntry>();
            }
        }

        #endregion
    }
}
