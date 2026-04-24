namespace Virtuagym.API.Models
{
    /// <summary>
    /// Helper class for the result of the toggle check-in (check-in/check-out).
    /// Regardless of the API version (v0 or v1), the relevant information for the frontend display is consolidated here.
    /// </summary>
    public class CheckinToggleResult
    {
        /// <summary>
        /// Visit-ID des Check-ins bzw. Check-outs nur bei v1 vorhanden.
        /// For v0, the visit ID is only returned on checkout.
        /// </summary>
        public long? VisitId { get; set; }

        /// <summary>Ob die Aktion erfolgreich war.</summary>
        public bool Success { get; set; }

        /// <summary>Action performed: "checkin" or "checkout".</summary>
        public string Action { get; set; }

        /// <summary>API status (e.g. "ok", "warn", "reject"). Taken from the v0 result on check-in.</summary>
        public string Status { get; set; }

        /// <summary>Anzeigename des Mitglieds.</summary>
        public string MemberName { get; set; }

        /// <summary>Avatar-URL des Mitglieds.</summary>
        public string MemberAvatar { get; set; }

        /// <summary>Member-ID des Mitglieds.</summary>
        public long MemberId { get; set; }

        /// <summary>User ID of the member, required for v0 API</summary>
        public long UserId { get; set; }

        public long UserTimestampEdit { get; set; }

        /// <summary>Message for the user.</summary>
        public string Message { get; set; }

        /// <summary>Check-out-Zeitpunkt als formatierte uhrzeit.</summary>
        public string Checkout { get; set; }

        /// <summary>Server-Nachrichten.</summary>
        public string[] ClientMessages { get; set; }

        /// <summary>Server messages for employees.</summary>
        public string[] EmployeeMessage { get; set; }

    }
}
