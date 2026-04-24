namespace Virtuagym.API.v1.Models
{
    /// <summary>
    /// Club-Informationen (v1 API: GET /club/{club_id}).
    /// </summary>
    public class ClubV1Result
    {
        public long id { get; set; }
        public string name { get; set; }
        public string domain { get; set; }
        public string street_name { get; set; }
        public string zipcode { get; set; }
        public string city { get; set; }
        public string country_code { get; set; }
        public string formatted_address { get; set; }
        public string lang { get; set; }
        public string website { get; set; }
        public string email { get; set; }
        public string phone { get; set; }
        public string logo { get; set; }
        public string background { get; set; }
        public string description { get; set; }
        public long timestamp_edit { get; set; }
        public long portal_group_id { get; set; }
    }
}
