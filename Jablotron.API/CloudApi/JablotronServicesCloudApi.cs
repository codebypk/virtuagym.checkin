using System.Collections.Generic;
using Jablotron.API.CloudApi.Models;

namespace Jablotron.API.CloudApi
{
    internal sealed class JablotronServicesCloudApi
    {
        private readonly JablotronCloudApiClient _client;

        public JablotronServicesCloudApi(JablotronCloudApiClient client)
        {
            _client = client;
        }

        public List<ServiceSummary> GetServices()
        {
            var response = _client.SendRequest<ServicesRequest, ApiEnvelope<ServicesData>>(
                ApiConstants.EndpointServiceList,
                new ServicesRequest { ListType = "EXTENDED", Visibility = "DEFAULT" });

            return response?.Data?.Services ?? new List<ServiceSummary>();
        }

        public ServiceInformationData GetServiceInformation(int serviceId)
        {
            var response = _client.SendRequest<ServiceInformationRequest, ApiEnvelope<ServiceInformationData>>(
                ApiConstants.EndpointServiceInformation,
                new ServiceInformationRequest { ServiceId = serviceId });

            return response?.Data;
        }

        public ServiceSettingsResponse GetServiceSettings(int serviceId, string serviceType)
        {
            return _client.SendRequest<ServiceSettingsRequest, ServiceSettingsResponse>(
                ApiConstants.EndpointServiceSettings,
                new ServiceSettingsRequest
                {
                    ServiceId = serviceId,
                    Service = serviceType.ToLowerInvariant()
                });
        }
    }
}

