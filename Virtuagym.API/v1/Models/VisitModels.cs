using System;
using System.Collections.Generic;

namespace Virtuagym.API.v1.Models
{
    /// <summary>
    /// Club visit (v1 API: /visits).
    /// GET all, GET single, POST.
    /// </summary>
    public class VisitResult
    {
        private static readonly DateTime Epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        public long id { get; set; }
        public long club_id { get; set; }
        public long member_id { get; set; }
        public string device_id { get; set; }
        public long check_in_timestamp { get; set; }
        public long check_out_timestamp { get; set; }
        public string status { get; set; }
        public string status_message { get; set; }

        /// <summary>
        /// Not populated by the API, but set later via member lookup.
        /// </summary>
        public string MemberName { get; set; }

        /// <summary>
        /// check_in_timestamp als formatiertes Datum.
        /// </summary>
        public string CheckInFormatted
        {
            get { return FormatTimestamp(check_in_timestamp); }
        }

        /// <summary>
        /// check_out_timestamp als formatiertes Datum (leer wenn 0).
        /// </summary>
        public string CheckOutFormatted
        {
            get { return FormatTimestamp(check_out_timestamp); }
        }

        private static string FormatTimestamp(long timestamp)
        {
            if (timestamp <= 0) return "";
            try
            {
                // Heuristic: < 10000000000 = seconds, otherwise milliseconds
                long ms = timestamp < 10000000000L ? timestamp * 1000 : timestamp;
                return Epoch.AddMilliseconds(ms).ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss");
            }
            catch { return timestamp.ToString(); }
        }
    }

    /// <summary>
    /// Club visit create check-in / check-out content (v1 API: POST /visits).
    /// member_id or rfid_tag must be specified. Action: "check_in" or "check_out".
    /// Status is returned by the server but also accepted as input here (e.g. for tests).
    /// </summary>
    public class VisitRequest
    {
        private static readonly HashSet<string> AllowedActions =
            new HashSet<string>(StringComparer.Ordinal) { ApiConstants.VisitActionCheckin, ApiConstants.VisitActionCheckout };

        private static readonly HashSet<string> AllowedStatus =
           new HashSet<string>(StringComparer.Ordinal) { ApiConstants.StatusOk, ApiConstants.StatusWarn, ApiConstants.StatusRejected };

        /// <summary>
        /// Action: only "check_in" or "check_out" allowed.
        /// </summary>
        private string _action;
        public string action
        {
            get { return _action; }
            set
            {
                if (!AllowedActions.Contains(value))
                    throw new ArgumentOutOfRangeException(nameof(action), value,
                        $"Only {string.Join(", ", AllowedActions)} are allowed.");
                _action = value;
            }
        }

        public string member_id { get; set; }

        public string rfid_tag { get; set; }

        /// <summary>
        /// Status: only "ok", "warn" or "reject" allowed (returned by the server but also accepted as input here).
        /// </summary>
        private string _status;
        public string status { 
            get { return _status; } 
            set
            {
                if (!AllowedStatus.Contains(value))
                    throw new ArgumentOutOfRangeException(nameof(status), value,
                        $"Only {string.Join(", ", AllowedStatus)} are allowed.");
                _status = value;
            }
        }
        public string status_message { get; set; }
    }

    /// <summary>
    /// Club-Besuch Anlegen CheckIn / CheckOut Ergebnis (v1 API: POST /visits).
    /// </summary>
    public class VisitRequestResult
    {
        public long id { get; set; }
        public long member_id { get; set; }
        public string message { get; set; }
    }
}

