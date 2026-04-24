namespace Virtuagym.CheckIn.Core.Models
{
    /// <summary>
    /// Club service loaded from Resources\club_service.json.
    /// Used in the mapping configuration for credit service selection.
    /// </summary>
    public class ClubService
    {
        /// <summary>Club ID (e.g. 61464).</summary>
        public int club_id { get; set; }

        /// <summary>Technical service ID (e.g. "feichtinger-fitness-ems"). Used as key for credit validation.</summary>
        public string service_id { get; set; }

        /// <summary>Display name of the service (e.g. "Feichtinger Fitness EMS").</summary>
        public string servicename { get; set; }

        /// <summary>Minimum number of credits required for a check-in. -1 or not present = no minimum requirement.</summary>
        public int min_credits { get; set; } = -1;

        /// <summary>Whether the service is active.</summary>
        public bool isActive { get; set; }

        /// <summary>Display name for the ComboBox: "Servicename (service_id)".</summary>
        public string DisplayName
        {
            get
            {
                if (!string.IsNullOrEmpty(servicename))
                    return servicename + " (" + service_id + ")";
                return service_id ?? "";
            }
        }
    }
}
