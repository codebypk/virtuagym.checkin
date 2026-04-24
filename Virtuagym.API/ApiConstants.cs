namespace Virtuagym.API
{
    /// <summary>
    /// Central constants for the Virtuagym API.
    /// </summary>
    public static class ApiConstants
    {
        #region Misc
        public const string MemberCacheFilePath = "resources\\data\\membercache.json";
        public const string AvatarCacheFolderPath = "resources\\avatars";
        #endregion

        #region Actions

        /// <summary>Action: Unknown.</summary>
        public const string ActionUnknown = "unknown";
        /// <summary>Action: Check-in.</summary>
        public const string ActionCheckin = "checkin";
        /// <summary>Action: Check-out.</summary>
        public const string ActionCheckout = "checkout";

        #endregion

        #region Visit Action
        /// <summary>Visit action: Check-in.</summary>
        public const string VisitActionCheckin = "check_in";
        /// <summary>Visit action: Check-out.</summary>
        public const string VisitActionCheckout = "check_out";
        #endregion

        #region Status

        /// <summary>Status: Success.</summary>
        public const string StatusOk = "ok";
        /// <summary>Status: Warning (e.g. expired subscription).</summary>
        public const string StatusWarn = "warn";
        /// <summary>Status: Unknown.</summary>
        public const string StatusUnknown = "unknown";
        /// <summary>Status: Rejected.</summary>
        public const string StatusRejected = "rejected";

        #endregion

        #region Messages

        /// <summary>No member found with RFID tag. {0} = RFID tag.</summary>
        public const string MsgMemberNotFoundByRfid = "No member found with card number \"{0}\".";

        /// <summary>Double-scan protection triggered. {0} = threshold display.</summary>
        public const string MsgDoubleScanBlocked = "The last check-in was less than {0} ago, check-out is skipped.";

        /// <summary>Successful check-in. {0} = member name.</summary>
        public const string MsgCheckinSuccess = "{0} has been checked in.";

        /// <summary>Successful check-out. {0} = member name.</summary>
        public const string MsgCheckoutSuccess = "{0} has been checked out.";

        /// <summary>Check-in failed.</summary>
        public const string MsgCheckinFailed = "Check-in failed.";

        /// <summary>Check-out failed.</summary>
        public const string MsgCheckoutFailed = "Check-out failed.";

        /// <summary>Insufficient credits. {0} = service name.</summary>
        public const string MsgInsufficientCredits = "Insufficient credits for \"{0}\".";

        /// <summary>Member is not active. {0} = member name.</summary>
        public const string MsgMemberNotActive = "The member \"{0}\" is not active.";

        #endregion

    }
}
