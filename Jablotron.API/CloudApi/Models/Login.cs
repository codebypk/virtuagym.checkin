using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Jablotron.API.CloudApi.Models
{
    public class LoginRequest
    {
        [JsonPropertyName("login")]
        public string Login { get; set; }

        [JsonPropertyName("password")]
        public string Password { get; set; }

        [JsonPropertyName("source")]
        public string Source { get; set; }
    }

    public class LoginResponse
    {
        [JsonPropertyName("data")]
        public LoginResponseData Data { get; set; }

        [JsonPropertyName("http-code")]
        public int? HttpCode { get; set; }

        [JsonPropertyName("errors")]
        public List<ApiError> Errors { get; set; }
    }

    public class LoginResponseData
    {
        [JsonPropertyName("service-data")]
        public LoginServiceData ServiceData { get; set; }

        [JsonPropertyName("accepts-newsletters")]
        public bool AcceptsNewsletters { get; set; }
    }

    public class LoginServiceData
    {
        [JsonPropertyName("service-detail")]
        public LoginServiceDetail ServiceDetail { get; set; }

        [JsonPropertyName("ja100-service-data")]
        public LoginJa100ServiceData Ja100ServiceData { get; set; }
    }

    public class LoginServiceDetail
    {
        [JsonPropertyName("service-id")]
        public int ServiceId { get; set; }

        [JsonPropertyName("service-type")]
        public string ServiceType { get; set; }

        [JsonPropertyName("service-name")]
        public string ServiceName { get; set; }

        [JsonPropertyName("cloud-entity-id")]
        public string CloudEntityId { get; set; }
    }

    public class LoginJa100ServiceData
    {
        [JsonPropertyName("service-states")]
        public LoginServiceStates ServiceStates { get; set; }

        [JsonPropertyName("states")]
        public List<ServiceState> States { get; set; }

        [JsonPropertyName("sections")]
        public List<SectionItem> Sections { get; set; }
    }

    public class LoginServiceStates
    {
        [JsonPropertyName("events")]
        public List<LoginServiceEvent> Events { get; set; }

        [JsonPropertyName("service-name")]
        public string ServiceName { get; set; }
    }

    public class LoginServiceEvent
    {
        [JsonPropertyName("type")]
        public string Type { get; set; }

        [JsonPropertyName("message")]
        public string Message { get; set; }

        [JsonPropertyName("date")]
        public string Date { get; set; }
    }
}
