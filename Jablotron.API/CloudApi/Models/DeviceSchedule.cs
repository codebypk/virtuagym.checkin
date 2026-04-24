using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Jablotron.API.CloudApi.Models
{
    public class DeviceScheduleRequest
    {
        [JsonPropertyName("status")]
        public bool Status { get; set; }

        [JsonPropertyName("type")]
        public string Type { get; set; }

        [JsonPropertyName("id")]
        public string Id { get; set; }

        [JsonPropertyName("parent_type")]
        public string ParentType { get; set; }

        [JsonPropertyName("room_id")]
        public string RoomId { get; set; }

        [JsonPropertyName("parent_id")]
        public int ParentId { get; set; }
    }

    public class DeviceScheduleResponse
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("type")]
        public string Type { get; set; }

        [JsonPropertyName("parent_id")]
        public int ParentId { get; set; }

        [JsonPropertyName("parent_type")]
        public string ParentType { get; set; }

        [JsonPropertyName("schedule")]
        public List<object> Schedule { get; set; }

        [JsonPropertyName("status")]
        public bool Status { get; set; }
    }
}
