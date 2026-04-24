using System.Collections.Generic;

namespace Virtuagym.API.v0.Models
{
    public class UserResult
    {
        public long id { get; set; }
        public string username { get; set; }
        public string name { get; set; }
        public string gender { get; set; }
        public int length { get; set; }
        public string length_unit { get; set; }
        public string weight_unit { get; set; }
        public string user_avatar { get; set; }
        public string birthday { get; set; }
        public int pro { get; set; }
        public string language { get; set; }
        public int activated { get; set; }
        public long timestamp_edit { get; set; }
        public long total_kcal { get; set; }
        public long total_min { get; set; }
        public long total_km { get; set; }
        public long fitness_points { get; set; }
        public string country { get; set; }
        public string city { get; set; }
        public List<string> selected_bodymetrics { get; set; }
    }

}
