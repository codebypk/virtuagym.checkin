using Jablotron.API.CloudApi.Models;

namespace Jablotron.API.CloudApi
{
    internal sealed class JablotronDeviceScheduleCloudApi
    {
        private readonly JablotronCloudApiClient _client;

        public JablotronDeviceScheduleCloudApi(JablotronCloudApiClient client)
        {
            _client = client;
        }

        public DeviceScheduleResponse GetDeviceSchedule(
            int serviceId,
            string deviceType,
            string deviceId,
            string roomId,
            string serviceType)
        {
            return _client.SendRequest<DeviceScheduleRequest, DeviceScheduleResponse>(
                ApiConstants.EndpointDeviceSchedule,
                new DeviceScheduleRequest
                {
                    Status = true,
                    Type = deviceType,
                    Id = deviceId,
                    ParentType = serviceType.ToLowerInvariant(),
                    RoomId = roomId,
                    ParentId = serviceId
                });
        }
    }
}

