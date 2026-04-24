namespace Virtuagym.API.v1.Models
{
    /// <summary>
    /// Club-Mitarbeiter (v1 API: /employee).
    /// GET alle, GET einzeln, PUT erstellen, PUT aktualisieren.
    /// Struktur identisch zu MemberResult.
    /// </summary>
    public class EmployeeResult
    {
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
    }
}

