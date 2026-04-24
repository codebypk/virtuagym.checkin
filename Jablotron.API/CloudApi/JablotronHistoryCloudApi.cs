using System.Collections.Generic;
using Jablotron.API.CloudApi.Models;

namespace Jablotron.API.CloudApi
{
    internal sealed class JablotronHistoryCloudApi
    {
        private readonly JablotronCloudApiClient _client;

        public JablotronHistoryCloudApi(JablotronCloudApiClient client)
        {
            _client = client;
        }

        public List<ServiceHistoryEvent> GetServiceHistory(
            int serviceId,
            string dateFrom = null,
            string dateTo = null,
            string eventIdFrom = null,
            string eventIdTo = null,
            int limit = 20,
            string serviceType = null)
        {
            var response = _client.SendRequest<EventHistoryRequest, ApiEnvelope<EventHistoryData>>(
                string.Format(ApiConstants.EndpointEventHistoryFormat, serviceType),
                new EventHistoryRequest
                {
                    Limit = limit,
                    ServiceId = serviceId,
                    DateFrom = dateFrom,
                    DateTo = dateTo,
                    EventIdFrom = eventIdFrom,
                    EventIdTo = eventIdTo
                });

            return response?.Data?.Events ?? new List<ServiceHistoryEvent>();
        }
    }
}

