using System;

namespace Virtuagym.API.v1.Models
{
    /// <summary>
    /// Club-Mitglied (v1 API: /member).
    /// GET alle, GET einzeln, PUT erstellen, PUT aktualisieren, PUT create_or_update.
    /// </summary>
    public class MemberResult
    {
        private static readonly DateTime Epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        public long member_id { get; set; }
        public long club_id { get; set; }
        public string club_member_id { get; set; }
        public string external_id { get; set; }
        public string firstname { get; set; }
        public string lastname { get; set; }
        public string email { get; set; }
        public bool active { get; set; }
        public bool is_pro { get; set; }
        public string gender { get; set; }
        public long member_since { get; set; }
        public long timestamp_edit { get; set; }
        public string birthday { get; set; }
        public string lang { get; set; }
        public string zip { get; set; }
        public string street { get; set; }
        public string street_extra { get; set; }
        public string place { get; set; }
        public string country { get; set; }
        public string formatted_address { get; set; }
        public string phone { get; set; }
        public string mobile { get; set; }
        public string rfid_tag { get; set; }
        public long registration_date { get; set; }

        /// <summary>
        /// The user_id of the member (not identical to member_id).
        /// Returned by the v1 Member API and required for v0 API calls (e.g. /user/{user_id}).
        /// </summary>
        public long user_id { get; set; }

        /// <summary>
        /// Avatar URL of the user (loaded via v0 /user/{user_id}, not directly from the v1 Member API).
        /// </summary>
        public string user_avatar { get; set; }

        /// <summary>
        /// Timestamp of the last visit (check-in). Set after loading visits, not populated by the API.
        /// </summary>
        public long last_visit_timestamp { get; set; }

        /// <summary>
        /// last_visit_timestamp als formatiertes Datum (dd.MM.yyyy HH:mm:ss).
        /// </summary>
        public string LastVisitFormatted
        {
            get { return FormatTimestampMs(last_visit_timestamp); }
        }

        /// <summary>
        /// member_since als formatiertes Datum (dd.MM.yyyy HH:mm:ss). Timestamp in Millisekunden.
        /// </summary>
        public string MemberSinceFormatted
        {
            get { return FormatTimestampMs(member_since); }
        }

        /// <summary>
        /// timestamp_edit als formatiertes Datum (dd.MM.yyyy HH:mm:ss). Timestamp in Millisekunden.
        /// </summary>
        public string TimestampEditFormatted
        {
            get { return FormatTimestampMs(timestamp_edit); }
        }

        /// <summary>
        /// registration_date als formatiertes Datum (dd.MM.yyyy HH:mm:ss).
        /// Erkennt Unix-Timestamps (Sekunden/Millisekunden) und Datums-Strings.
        /// </summary>
        public string RegistrationDateFormatted
        {
            get { return FormatTimestampMs(timestamp_edit); }
        }

        private static string FormatTimestampMs(long timestampMs)
        {
            if (timestampMs <= 0)
                return "";

            try
            {
                // Heuristic: Values < 10000000000 are seconds, above that milliseconds
                if (timestampMs < 10000000000L)
                    timestampMs *= 1000;

                var dt = Epoch.AddMilliseconds(timestampMs).ToLocalTime();
                return dt.ToString("dd.MM.yyyy HH:mm:ss");
            }
            catch
            {
                return timestampMs.ToString();
            }
        }
    }

    /// <summary>
    /// Result of a member activation (POST /member/activate_user).
    /// </summary>
    public class MemberActivationResult
    {
        public long member_id { get; set; }
        public long user_id { get; set; }
        public long club_id { get; set; }
    }
}

