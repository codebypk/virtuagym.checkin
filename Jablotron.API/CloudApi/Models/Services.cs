using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Jablotron.API.CloudApi.Models
{
    public class ServicesRequest
    {
        [JsonPropertyName("list-type")]
        public string ListType { get; set; }

        [JsonPropertyName("visibility")]
        public string Visibility { get; set; }
    }

    public class ServicesData
    {
        [JsonPropertyName("services")]
        public List<ServiceSummary> Services { get; set; }
    }

    public class ServiceSummary
    {
        [JsonPropertyName("service-id")]
        public int ServiceId { get; set; }

        [JsonPropertyName("cloud-entity-id")]
        public string CloudEntityId { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("service-type")]
        public string ServiceType { get; set; }

        [JsonPropertyName("status")]
        public string Status { get; set; }

        [JsonPropertyName("message")]
        public string Message { get; set; }
    }
}
