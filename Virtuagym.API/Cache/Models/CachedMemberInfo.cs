using System.Collections.Generic;
using System.Linq;

namespace Virtuagym.API.Cache.Models
{
    /// <summary>
    /// Lightweight DTO class for pre-cached member data.
    /// Passed to <see cref="VirtuagymApiService.ToggleCheckinByRfidAsync"/>,
    /// to skip API requests for member lookup and visit queries.
    /// </summary>
    public class CachedMemberInfo
    {
        /// <summary>Member-ID.</summary>
        public long MemberId { get; set; }

        /// <summary>User-Id needed for v0 API</summary>
        public long UserId { get; set; }

        /// <summary>RFID-Tag.</summary>
        public string RfidTag { get; set; }

        /// <summary>Vorname.</summary>
        public string Firstname { get; set; }

        /// <summary>Nachname.</summary>
        public string Lastname { get; set; }

        /// <summary>Avatar-URL (kann null sein).</summary>
        public string Avatar { get; set; }

        /// <summary>Ist das Mitglied aktiv?</summary>
        public bool Active { get; set; }

        /// <summary>timestamp_edit of the member (Unix timestamp in milliseconds). Used for avatar update checks.</summary>
        public long TimestampEdit { get; set; }

        /// <summary>Credits pro Service-Typ. Kann null sein wenn noch nicht geladen.</summary>
        public List<ClubServiceCredits> ServiceCredits { get; set; }

        /// <summary>
        /// Letzter Synchronisationszeitpunkt der ServiceCredits (Unix-Timestamp in Millisekunden).
        /// 0 = unbekannt / noch nie synchronisiert.
        /// </summary>
        public long CreditsLastSyncTimestamp { get; set; }

        /// <summary>
        /// Returns the credit data for a specific service ID.
        /// Returns null if no credits are available for this service.
        /// </summary>
        public ClubServiceCredits GetCreditsForService(string serviceId)
        {
            if (string.IsNullOrEmpty(serviceId) || ServiceCredits == null)
                return null;
            return ServiceCredits.FirstOrDefault(c =>
                string.Equals(c.service_id, serviceId, System.StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Device-specific check-in/check-out data.
        /// Taken from <see cref="MemberCacheEntry.DeviceCheckins"/>.
        /// </summary>
        public List<DeviceCheckinData> DeviceCheckins { get; set; }

        /// <summary>
        /// Indicates whether an active visit exists at any device
        /// (at least one device with CheckIn &gt; 0 and CheckOut == 0).
        /// </summary>
        public bool HasActiveVisit
        {
            get
            {
                if (DeviceCheckins == null || DeviceCheckins.Count == 0)
                    return false;
                return DeviceCheckins.Any(d => d.CheckInTimestamp > 0 && d.CheckOutTimestamp == 0);
            }
        }

        /// <summary>
        /// Returns the device-specific check-in data for a specific device.
        /// Returns null if no data is available for the device.
        /// </summary>
        public DeviceCheckinData GetDeviceData(string deviceId)
        {
            if (!string.IsNullOrEmpty(deviceId) && DeviceCheckins != null)
            {
                for (int i = 0; i < DeviceCheckins.Count; i++)
                {
                    if (string.Equals(DeviceCheckins[i].DeviceId, deviceId, System.StringComparison.Ordinal))
                        return DeviceCheckins[i];
                }
            }
            return null;
        }

        /// <summary>
        /// Returns the check-in timestamp for a specific device.
        /// 0 = no check-in known for this device or no device-specific data available.
        /// </summary>
        public long GetCheckInTimestampForDevice(string deviceId)
        {
            if (!string.IsNullOrEmpty(deviceId) && DeviceCheckins != null)
            {
                var data = GetDeviceData(deviceId);
                if (data != null)
                    return data.CheckInTimestamp;
            }
            return 0;
        }
    }
}
