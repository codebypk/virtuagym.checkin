using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Jablotron.API.CloudApi.Models
{
    public class ServiceSettingsRequest
    {
        [JsonPropertyName("serviceId")]
        public int ServiceId { get; set; }

        [JsonPropertyName("service")]
        public string Service { get; set; }
    }

    public class ServiceSettingsResponse
    {
        [JsonPropertyName("status")]
        public bool Status { get; set; }

        [JsonPropertyName("settings_names")]
        public List<ServiceSettingsNameEntry> SettingsNames { get; set; }
    }

    public class ServiceSettingsNameEntry
    {
        [JsonPropertyName("name_type")]
        public string NameType { get; set; }

        [JsonPropertyName("name_key")]
        public string NameKey { get; set; }

        [JsonPropertyName("name_value")]
        public string NameValue { get; set; }
    }
}
