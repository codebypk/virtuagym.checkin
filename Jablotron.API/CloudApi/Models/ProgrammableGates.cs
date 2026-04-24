using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Jablotron.API.CloudApi.Models
{
    public class ProgrammableGatesRequest
    {
        [JsonPropertyName("connect-device")]
        public bool ConnectDevice { get; set; }

        [JsonPropertyName("list-type")]
        public string ListType { get; set; }

        [JsonPropertyName("service-id")]
        public int ServiceId { get; set; }

        [JsonPropertyName("service-states")]
        public bool ServiceStates { get; set; }
    }

    public class ProgrammableGatesData
    {
        [JsonPropertyName("service-states")]
        public SectionsServiceStates ServiceStatesInfo { get; set; }

        [JsonPropertyName("states")]
        public List<ServiceState> States { get; set; }

        [JsonPropertyName("programmableGates")]
        public List<ProgrammableGateItem> ProgrammableGates { get; set; }
    }

    public class ProgrammableGateItem
    {
        [JsonPropertyName("cloud-component-id")]
        public string CloudComponentId { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("can-control")]
        public bool CanControl { get; set; }

        [JsonPropertyName("need-authorization")]
        public bool NeedAuthorization { get; set; }
    }
}
