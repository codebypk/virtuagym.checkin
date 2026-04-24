using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Virtuagym.API.Models
{
    /// <summary>
    /// Internal result of the member lookup and double-scan check.
    /// Wird von <see cref="ToggleCheckinByRfidAsync"/> und <see cref="ToggleCheckinByRfidV1Async"/>
    /// gemeinsam verwendet, um Code-Duplizierung zu vermeiden.
    /// </summary>
    internal class MemberLookupResult
    {
        public long MemberId;
        public string MemberName;
        public string MemberAvatar;
        public bool ActiveVisitExists;
        public long UserId;
        public long UserTimestampEdit;
        public long LastCheckInTimestamp;
        /// <summary>Visit ID of the active visit (for v1 checkout). Can be null.</summary>
        public long? ActiveVisitId;
        /// <summary>If set, the lookup/double-scan was aborted and this result should be returned directly.</summary>
        public CheckinToggleResult EarlyReturn;
    }
}
