using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Virtuagym.API.v0.Models
{
    /// <summary>
    /// Check-in request (v0 API: PUT /devices).
    /// card_id: RFID tag of the member (optional if member_id is provided).
    /// member_id: Virtuagym member ID (optional if card_id is provided).
    /// </summary>
    public class DeviceCheckinRequest
    {
        public string card_id { get; set; }
        public string member_id { get; set; }
        public string action { get; set; } = "identification";
        public string application { get; set; } = "desktop1";
        public string version { get; set; } = "1.4.8.0";
    }

    /// <summary>
    /// Check-in result (v0 API: PUT /devices).
    /// The server uses toggle logic: if the member is checked in, they are checked out and vice versa.
    /// The client_message contains the check-in/check-out message for the user.
    /// </summary>
    public class DeviceCheckinResult
    {
        public string status { get; set; }
        public bool open_relay { get; set; }
        public List<string> client_message { get; set; }
        public List<string> employee_message { get; set; }
        public MemberInfo member { get; set; }
        public string use_credit_from_guid { get; set; }
        /// <summary>
        /// Checkout-Zeitpunkt. Beim Checkin liefert die API <c>0</c> (Number),
        /// beim Checkout einen Zeitstring wie <c>"07:12:02"</c>.
        /// </summary>
        [JsonConverter(typeof(FlexibleStringOrNumberConverter))]
        public string checkout { get; set; }
        public long id { get; set; }
    }

    /// <summary>
    /// Kurzinfo eines Mitglieds (Teil des CheckinResult).
    /// </summary>
    public class MemberInfo
    {
        public string member_id { get; set; }
        public string firstname { get; set; }
        public string lastname { get; set; }
        public string name { get; set; }
        public string avatar { get; set; }
        public string member_url { get; set; }
    }

    /// <summary>
    /// Konvertiert JSON-Werte die entweder als Number (<c>0</c>) oder als String (<c>"07:12:02"</c>)
    /// geliefert werden einheitlich in einen <see cref="string"/>.
    /// </summary>
    internal sealed class FlexibleStringOrNumberConverter : JsonConverter<string>
    {
        public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            return reader.TokenType switch
            {
                JsonTokenType.String => reader.GetString(),
                JsonTokenType.Number => reader.GetDecimal().ToString(System.Globalization.CultureInfo.InvariantCulture),
                JsonTokenType.Null => null,
                JsonTokenType.True => "true",
                JsonTokenType.False => "false",
                _ => throw new JsonException($"Unexpected token type {reader.TokenType} for string property.")
            };
        }

        public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(value);
        }
    }
}

