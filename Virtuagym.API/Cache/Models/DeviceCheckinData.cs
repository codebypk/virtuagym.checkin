namespace Virtuagym.API.Cache.Models
{
    /// <summary>
    /// Device-specific check-in/check-out data for a member.
    /// Wird in <see cref="MemberCacheEntry"/> und <see cref="CachedMemberInfo"/> verwendet,
    /// to store the last check-in and check-out timestamps per device (identified by UUID).
    /// </summary>
    public class DeviceCheckinData
    {
        /// <summary>
        /// Unique device UUID from <see cref="VirtuagymMemberCheckIn.CheckinClientMapping.Uuid"/>.
        /// </summary>
        public string DeviceId { get; set; }

        /// <summary>Last check-in time at this device (Unix timestamp in milliseconds). 0 = no active visit.</summary>
        public long CheckInTimestamp { get; set; }

        /// <summary>Last check-out time at this device (Unix timestamp in milliseconds). 0 = still checked in.</summary>
        public long CheckOutTimestamp { get; set; }


        /// <summary>
        /// API version used for the check-in (0 or 1).
        /// </summary>
        public double ApiVersion { get; set; }

        /// <summary>
        /// Visit ID of the last visit at this device (returned by the v1 API after POST /visits).
        /// Wird beim Offline-Checkout-Sync verwendet, um den korrekten Visit auszuchecken.
        /// Retained after checkout (never reset) so it is traceable
        /// which visit occurred at this device.
        /// Null/leer = unbekannt (z.B. v0-Checkin liefert die ID nur beim Checkout).
        /// </summary>
        public long? VisitId { get; set; }
    }
}
