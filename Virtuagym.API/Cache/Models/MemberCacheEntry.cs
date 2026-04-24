using System.Collections.Generic;
using System.Linq;

namespace Virtuagym.API.Cache.Models
{
    /// <summary>
    /// Local cache entry for a club member.
    /// Wird als JSON-Datei gespeichert, um API-Requests beim Checkin zu minimieren.
    /// </summary>
    public class MemberCacheEntry
    {
        /// <summary>Virtuagym Member-ID.</summary>
        public long MemberId { get; set; }

        /// <summary>Virtuagym User ID, required for v0 API.</summary>
        public long UserId { get; set; }

        /// <summary>RFID-Tag wie in Virtuagym hinterlegt.</summary>
        public string RfidTag { get; set; }

        public long ClubId { get; set; }

        /// <summary>Aktives Mitglied?</summary>
        public bool Active { get; set; }

        /// <summary>Vorname.</summary>
        public string Firstname { get; set; }

        /// <summary>Nachname.</summary>
        public string Lastname { get; set; }

        /// <summary>E-Mail-Adresse.</summary>
        public string Email { get; set; }

        /// <summary>Telefonnummer.</summary>
        public string Phone { get; set; }

        /// <summary>Last activity time (Unix timestamp in milliseconds).</summary>
        public long LastActive { get; set; }

        /// <summary>timestamp_edit of the member (Unix timestamp in milliseconds). Used for avatar update checks.</summary>
        public long TimestampEdit { get; set; }

        /// <summary>Avatar-URL des Members (von der API).</summary>
        public string AvatarUrl { get; set; }

        /// <summary>Lokaler Pfad zum heruntergeladenen Avatar-Bild.</summary>
        public string AvatarLocalPath { get; set; }

        /// <summary>
        /// Credit-Daten pro Dienstleistung (service_type).
        /// A member can have credits for multiple services.
        /// </summary>
        public List<ClubServiceCredits> ServiceCredits { get; set; }

        /// <summary>
        /// Device-specific check-in/check-out data.
        /// Allows a member to check in at multiple devices within a short time,
        /// while the double-scan protection only applies per device.
        /// </summary>
        public List<DeviceCheckinData> DeviceCheckins { get; set; }

        /// <summary>
        /// Letzter Synchronisationszeitpunkt der ServiceCredits (Unix-Timestamp in Millisekunden).
        /// 0 = unbekannt / noch nie synchronisiert.
        /// </summary>
        public long CreditsLastSyncTimestamp { get; set; }

        /// <summary>Full display name (first name + last name).</summary>
        public string DisplayName
        {
            get
            {
                string name = ((Firstname ?? "") + " " + (Lastname ?? "")).Trim();
                return string.IsNullOrEmpty(name) ? "Member #" + MemberId : name;
            }
        }

        /// <summary>Indicates whether the member is checked in at at least one device.</summary>
        public bool IsCheckedIn
        {
            get
            {
                if (DeviceCheckins == null || DeviceCheckins.Count == 0)
                    return false;
                for (int i = 0; i < DeviceCheckins.Count; i++)
                {
                    if (DeviceCheckins[i].CheckInTimestamp > 0 && DeviceCheckins[i].CheckOutTimestamp == 0)
                        return true;
                }
                return false;
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
        /// Returns the last check-in timestamp for a specific device.
        /// Returns 0 if no data is available for the device.
        /// </summary>
        public long GetCheckInTimestampForDevice(string deviceId)
        {
            var data = GetDeviceData(deviceId);
            return data != null ? data.CheckInTimestamp : 0;
        }

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
        /// Erstellt eine tiefe Kopie dieses Eintrags.
        /// Lists are returned as new instances with copied elements,
        /// so the caller does not hold shared references to internal data.
        /// </summary>
        public MemberCacheEntry Clone()
        {
            return new MemberCacheEntry
            {
                MemberId          = MemberId,
                RfidTag           = RfidTag,
                ClubId            = ClubId,
                Active            = Active,
                Firstname         = Firstname,
                Lastname          = Lastname,
                Email             = Email,
                Phone             = Phone,
                LastActive        = LastActive,
                TimestampEdit     = TimestampEdit,
                AvatarUrl         = AvatarUrl,
                AvatarLocalPath   = AvatarLocalPath,
                ServiceCredits    = ServiceCredits == null ? null :
                    ServiceCredits.Select(c => new ClubServiceCredits
                    {
                        service_id      = c.service_id,
                        servicename     = c.servicename,
                        CreditAmount    = c.CreditAmount,
                        CreditUnlimited = c.CreditUnlimited
                    }).ToList(),
                CreditsLastSyncTimestamp = CreditsLastSyncTimestamp,
                DeviceCheckins    = DeviceCheckins == null ? null :
                    DeviceCheckins.Select(d => new DeviceCheckinData
                    {
                        DeviceId           = d.DeviceId,
                        CheckInTimestamp   = d.CheckInTimestamp,
                        CheckOutTimestamp  = d.CheckOutTimestamp,
                        ApiVersion         = d.ApiVersion,
                        VisitId            = d.VisitId
                    }).ToList()
            };
        }
    }
}
