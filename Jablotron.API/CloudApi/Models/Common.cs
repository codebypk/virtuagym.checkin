using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Jablotron.API.CloudApi.Models
{
    public class ApiEnvelope<T>
    {
        [JsonPropertyName("data")]
        public T Data { get; set; }

        [JsonPropertyName("http-code")]
        public int? HttpCode { get; set; }

        [JsonPropertyName("errors")]
        public List<ApiError> Errors { get; set; }
    }

    public class ApiError
    {
        [JsonPropertyName("code")]
        public string Code { get; set; }

        [JsonPropertyName("message")]
        public string Message { get; set; }
    }

    public class ServiceState
    {
        [JsonPropertyName("component-id")]
        public string ComponentId { get; set; }

        [JsonPropertyName("cloud-component-id")]
        public string CloudComponentId { get; set; }

        [JsonPropertyName("state")]
        public string State { get; set; }
    }

    public class ControlError
    {
        [JsonPropertyName("component-id")]
        public string ComponentId { get; set; }

        [JsonPropertyName("control-error")]
        public string ErrorCode { get; set; }
    }
}
