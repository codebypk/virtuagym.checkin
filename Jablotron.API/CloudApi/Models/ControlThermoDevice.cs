using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Jablotron.API.CloudApi.Models
{
    public class ControlThermoDeviceRequest
    {
        [JsonPropertyName("service-id")]
        public int ServiceId { get; set; }

        [JsonPropertyName("control-components")]
        public List<ControlThermoComponent> ControlComponents { get; set; }
    }

    public class ControlThermoComponent
    {
        [JsonPropertyName("actions")]
        public ControlThermoAction Actions { get; set; }

        [JsonPropertyName("component-id")]
        public string ComponentId { get; set; }
    }

    public class ControlThermoAction
    {
        [JsonPropertyName("set-heating-mode")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public string SetHeatingMode { get; set; }

        [JsonPropertyName("set-temperature")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public double? SetTemperature { get; set; }
    }

    public class ControlThermoDeviceData
    {
        [JsonPropertyName("control-errors")]
        public List<ControlError> ControlErrors { get; set; }

        [JsonPropertyName("states")]
        public List<ThermoDeviceState> States { get; set; }
    }
}
