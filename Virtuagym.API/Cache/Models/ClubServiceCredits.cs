namespace Virtuagym.API.Cache.Models
{
    /// <summary>
    /// Credit data of a member for a specific service (service_type).
    /// A member can have credits for multiple services.
    /// </summary>
    public class ClubServiceCredits
    {
        /// <summary>
        /// Hat in der credits v1-API den Namen service_type und in der club_service.json den Namen service_id.
        /// </summary>
        public string service_id { get; set; }

        /// <summary>Anzeigename der Dienstleistung (z.B. "Feichtinger Fitness EMS"). Wird aus club_services.json geladen.</summary>
        public string servicename { get; set; }

        /// <summary>Credit amount for the configured service. -1 = not loaded.</summary>
        public int CreditAmount { get; set; }

        /// <summary>Unlimited credits (check-in always allowed, regardless of CreditAmount).</summary>
        public bool CreditUnlimited { get; set; }
    }
}
