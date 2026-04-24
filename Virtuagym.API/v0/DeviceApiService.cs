using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Virtuagym.API.Serialization;
using Virtuagym.API.Models;
using Virtuagym.API.v0.Models;

namespace Virtuagym.API.v0
{
    /// <summary>
    /// Virtuagym v0 Device API: Device settings and RFID check-in.
    /// </summary>
    [Obsolete("v0 API is deprecated. Use the v1 API.")]
    public class DeviceApiService : VirtuagymApiBase
    {

        public DeviceApiService(HttpClient httpClient, JsonSerializerAdapter json,
            string apiKey, string clubSecret, string apiBaseUrl, string clubId)
            : base(httpClient, json, apiKey, clubSecret, apiBaseUrl, clubId)
        {}

        /// <summary>
        /// F�hrt einen Check-in f�r eine RFID-Karte durch (v0 PUT /devices).
        /// Die v0 API verwendet Toggle-Logik: ist das Mitglied bereits eingecheckt, wird ein Check-out durchgef�hrt.
        /// Mindestens eines von <paramref name="cardId"/> oder <paramref name="memberId"/> muss angegeben werden.
        /// </summary>
        /// <param name="cardId">RFID-Tag des Mitglieds (card_id). Wird nur gesendet wenn nicht leer/null/"0000000000".</param>
        /// <param name="memberId">Virtuagym Member-ID (optional). Wird gesetzt wenn kein g�ltiger RFID-Tag vorhanden ist oder zus�tzlich zum RFID-Tag.</param>
        public async Task<DeviceCheckinResult> CheckinMemberAsync(string cardId, string memberId = null)
        {
            var request = new DeviceCheckinRequest();

            // card_id nur setzen wenn ein g�ltiger RFID-Tag vorliegt
            if (!string.IsNullOrWhiteSpace(cardId) && cardId != "0000000000")
                request.card_id = cardId;

            if (!string.IsNullOrWhiteSpace(memberId))
                request.member_id = memberId;

            var result = await PutSingleV0Async<DeviceCheckinResult>("devices", request);
            return result;
        }

    }
}

