using System;

namespace Virtuagym.API
{
    /// <summary>
    /// Allows overriding the default API messages.
    /// All properties are optional – if null or empty, the value from <see cref="ApiConstants"/> is used.
    /// Format strings use {0}, {1} etc. as placeholders (as in <see cref="ApiConstants"/>).
    /// </summary>
    public class ApiMessageOverrides
    {
        /// <summary>No member found with RFID tag. {0} = RFID tag.</summary>
        public string MsgMemberNotFoundByRfid { get; set; }

        /// <summary>Double-scan protection triggered. {0} = threshold display.</summary>
        public string MsgDoubleScanBlocked { get; set; }

        /// <summary>Successful check-in. {0} = member name.</summary>
        public string MsgCheckinSuccess { get; set; }

        /// <summary>Successful check-out. {0} = member name.</summary>
        public string MsgCheckoutSuccess { get; set; }

        /// <summary>Check-in fehlgeschlagen.</summary>
        public string MsgCheckinFailed { get; set; }

        /// <summary>Check-out fehlgeschlagen.</summary>
        public string MsgCheckoutFailed { get; set; }

        /// <summary>Insufficient credits. {0} = service name.</summary>
        public string MsgInsufficientCredits { get; set; }

        /// <summary>Member is not active. {0} = member name.</summary>
        public string MsgMemberNotActive { get; set; }

        /// <summary>
        /// Formats threshold text from milliseconds, e.g. "1 minute", "2 Minuten", "30 Sekunden".
        /// </summary>
        public Func<long, string> FormatDoubleScanThreshold { get; set; }

        /// <summary>
        /// Returns the override value, or the fallback if the override is null/empty.
        /// </summary>
        public static string Resolve(string overrideValue, string fallback)
        {
            return !string.IsNullOrWhiteSpace(overrideValue) ? overrideValue : fallback;
        }
    }
}
