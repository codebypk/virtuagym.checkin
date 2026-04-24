using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Jablotron.API.CloudApi.Models
{
    public class ThermoDevicesRequest
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

    public class ThermoDevicesData
    {
        [JsonPropertyName("states")]
        public List<ThermoDeviceState> States { get; set; }

        [JsonPropertyName("thermo-devices")]
        public List<ThermoDeviceDetails> Devices { get; set; }
    }

    public class ThermoDeviceDetails
    {
        [JsonPropertyName("object-device-id")]
        public string ObjectDeviceId { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; }
    }

    public class ThermoDeviceState
    {
        [JsonPropertyName("object-device-id")]
        public string ObjectDeviceId { get; set; }

        [JsonPropertyName("temperature")]
        public double? Temperature { get; set; }

        [JsonPropertyName("last-temperature-time")]
        public string LastTemperatureTime { get; set; }
    }

    public class ThermoDeviceResult
    {
        [JsonPropertyName("object-device-id")]
        public string ObjectDeviceId { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("temperature")]
        public double? Temperature { get; set; }

        [JsonPropertyName("last-temperature-time")]
        public string LastTemperatureTime { get; set; }

        [JsonPropertyName("thermo-device")]
        public ThermoDeviceDetails Device { get; set; }

        [JsonPropertyName("state")]
        public ThermoDeviceState State { get; set; }
    }
}
