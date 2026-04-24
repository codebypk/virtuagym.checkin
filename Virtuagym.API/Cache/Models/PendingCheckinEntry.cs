namespace Virtuagym.API.Cache.Models
{
    /// <summary>
    /// Entry for an offline check-in or check-out that has not yet been synchronized with the API.
    /// Stored in the local cache file and processed during the next successful sync.
    /// </summary>
    public class PendingCheckinEntry
    {
        /// <summary>Virtuagym Member-ID.</summary>
        public long MemberId { get; set; }

        /// <summary>Virtuagym User-ID.</summary>
        public long UserId { get; set; }

        /// <summary>RFID tag or QR code content used for the check-in.</summary>
        public string RfidTag { get; set; }

        /// <summary>Device ID (e.g. CheckinClientMapping.Name or DeviceID).</summary>
        public string DeviceId { get; set; }

        /// <summary>Zeitpunkt des Offline-Checkins (Unix-Timestamp in Millisekunden).</summary>
        public long Timestamp { get; set; }

        /// <summary>
        /// Action performed offline: "checkin" or "checkout".
        /// Used during sync to trigger the correct API action.
        /// Null/leer = Legacy-Eintrag, wird als Toggle (CheckinMemberAsync) behandelt.
        /// </summary>
        public string Action { get; set; }

        /// <summary>
        /// Club secret (CheckinKey) of the device that initiated the check-in.
        /// Used during sync to create the API instance with the correct device key,
        /// so Virtuagym assigns the check-in to the correct device (checkin client).
        /// Null = Legacy-Eintrag, verwendet den Standard-ClubSecret.
        /// </summary>
        public string CheckinKey { get; set; }

        /// <summary>
        /// Visit-ID des aktiven Besuchs, der ausgecheckt werden soll.
        /// Wird beim Offline-Checkout gesetzt, damit der Sync den korrekten Visit beendet,
        /// auch wenn der Member zwischenzeitlich erneut eingecheckt wurde.
        /// Null/empty = unknown (fallback: search for active visit via API).
        /// </summary>
        public long? VisitId { get; set; }

        /// <summary>
        /// Anzahl fehlgeschlagener Sync-Versuche. Nach 3 Fehlversuchen wird der Eintrag
        /// aus der Pending-Queue entfernt, um Endlos-Retry-Loops zu vermeiden.
        /// </summary>
        public int SyncRetryCount { get; set; }
    }
}
