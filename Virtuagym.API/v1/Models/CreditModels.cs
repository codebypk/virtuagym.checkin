namespace Virtuagym.API.v1.Models
{
    /// <summary>
    /// Credits eines Mitglieds (v1 API: /credit).
    /// GET abrufen, PUT zuweisen.
    /// </summary>
    public class CreditResult
    {
        public long member_id { get; set; }
        public string member_email { get; set; }
        public long credit_amount { get; set; }
        public bool credit_unlimited { get; set; }
        public string service_type { get; set; }
        public long club_id { get; set; }
        public string valid_until { get; set; }
        public string notes { get; set; }
    }
}

