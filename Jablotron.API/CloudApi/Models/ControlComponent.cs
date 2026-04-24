using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Jablotron.API.CloudApi.Models
{
    public class ControlComponentRequest
    {
        [JsonPropertyName("service-id")]
        public int ServiceId { get; set; }

        [JsonPropertyName("authorization")]
        public ControlAuthorization Authorization { get; set; }

        [JsonPropertyName("control-components")]
        public List<ControlComponentItem> ControlComponents { get; set; }
    }

    public class ControlAuthorization
    {
        [JsonPropertyName("authorization-code")]
        public string AuthorizationCode { get; set; }
    }

    public class ControlComponentItem
    {
        [JsonPropertyName("actions")]
        public ControlAction Actions { get; set; }

        [JsonPropertyName("component-id")]
        public string ComponentId { get; set; }

        [JsonPropertyName("force")]
        public bool Force { get; set; }
    }

    public class ControlAction
    {
        [JsonPropertyName("action")]
        public string Action { get; set; }

        [JsonPropertyName("value")]
        public string Value { get; set; }
    }

    public class ControlComponentData
    {
        [JsonPropertyName("control-errors")]
        public List<ControlError> ControlErrors { get; set; }

        [JsonPropertyName("states")]
        public List<ServiceState> States { get; set; }
    }
}
