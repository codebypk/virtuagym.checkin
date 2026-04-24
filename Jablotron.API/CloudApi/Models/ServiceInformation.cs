using System.Text.Json.Serialization;

namespace Jablotron.API.CloudApi.Models
{
    public class ServiceInformationRequest
    {
        [JsonPropertyName("service-id")]
        public int ServiceId { get; set; }
    }

    public class ServiceInformationData
    {
        [JsonPropertyName("device")]
        public ServiceInformationDevice Device { get; set; }
    }

    public class ServiceInformationDevice
    {
        [JsonPropertyName("family")]
        public string Family { get; set; }

        [JsonPropertyName("model-name")]
        public string ModelName { get; set; }

        [JsonPropertyName("service-name")]
        public string ServiceName { get; set; }

        [JsonPropertyName("registration-key")]
        public string RegistrationKey { get; set; }

        [JsonPropertyName("phone-number")]
        public string PhoneNumber { get; set; }

        [JsonPropertyName("firmware")]
        public string Firmware { get; set; }
    }
}
