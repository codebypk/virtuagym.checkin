using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Jablotron.API.CloudApi.Models
{
    public class KeyboardSegmentsRequest
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

    public class KeyboardSegmentsData
    {
        [JsonPropertyName("keyboards")]
        public List<KeyboardItem> Keyboards { get; set; }
    }

    public class KeyboardItem
    {
        [JsonPropertyName("object-device-id")]
        public string ObjectDeviceId { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("segments")]
        public List<KeyboardSegment> Segments { get; set; }
    }

    public class KeyboardSegment
    {
        [JsonPropertyName("segment-id")]
        public string SegmentId { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("can-control")]
        public bool CanControl { get; set; }

        [JsonPropertyName("need-authorization")]
        public bool NeedAuthorization { get; set; }

        [JsonPropertyName("display-component-id")]
        public string DisplayComponentId { get; set; }

        [JsonPropertyName("control-component-id")]
        public string ControlComponentId { get; set; }

        [JsonPropertyName("segment-function")]
        public string SegmentFunction { get; set; }
    }
}
