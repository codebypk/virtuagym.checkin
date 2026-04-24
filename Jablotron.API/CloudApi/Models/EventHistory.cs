using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Jablotron.API.CloudApi.Models
{
    public class EventHistoryRequest
    {
        [JsonPropertyName("limit")]
        public int Limit { get; set; }

        [JsonPropertyName("service-id")]
        public int ServiceId { get; set; }

        [JsonPropertyName("date-from")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public string DateFrom { get; set; }

        [JsonPropertyName("date-to")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public string DateTo { get; set; }

        [JsonPropertyName("event-id-from")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public string EventIdFrom { get; set; }

        [JsonPropertyName("event-id-to")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public string EventIdTo { get; set; }
    }

    public class EventHistoryData
    {
        [JsonPropertyName("events")]
        public List<ServiceHistoryEvent> Events { get; set; }
    }

    public class ServiceHistoryEvent
    {
        [JsonPropertyName("id")]
        public string Id { get; set; }

        [JsonPropertyName("date")]
        public string Date { get; set; }

        [JsonPropertyName("event-text")]
        public string EventText { get; set; }
    }
}
