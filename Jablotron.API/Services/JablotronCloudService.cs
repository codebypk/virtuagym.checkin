using System;
using System.Collections.Generic;
using System.Net.Http;
using Jablotron.API.CloudApi;
using Jablotron.API.CloudApi.Models;

namespace Jablotron.API.Services
{
    /// <summary>
    /// Facade for Jablotron Cloud API grouped domain services.
    /// </summary>
    public class JablotronCloudService : IDisposable
    {
        private readonly JablotronCloudApiClient _client;
        private readonly JablotronAuthCloudApi _authApi;
        private readonly JablotronServicesCloudApi _servicesApi;
        private readonly JablotronSectionsCloudApi _sectionsApi;
        private readonly JablotronProgrammableGatesCloudApi _programmableGatesApi;
        private readonly JablotronThermoCloudApi _thermoApi;
        private readonly JablotronHistoryCloudApi _historyApi;
        private readonly JablotronDeviceScheduleCloudApi _deviceScheduleApi;

        public string ApiBaseUrl => _client.ApiBaseUrl;

        public int? CurrentServiceId { get; private set; }
        public string CurrentServiceType { get; private set; }
        public bool HasServiceContext => CurrentServiceId.HasValue && !string.IsNullOrWhiteSpace(CurrentServiceType);

        public JablotronCloudService(
            string apiBaseUrl,
            string username,
            string password,
            string defaultPinCode = null,
            HttpClient httpClient = null)
        {
            _client = new JablotronCloudApiClient(apiBaseUrl, username, password, defaultPinCode, httpClient);
            _authApi = new JablotronAuthCloudApi(_client);
            _servicesApi = new JablotronServicesCloudApi(_client);
            _sectionsApi = new JablotronSectionsCloudApi(_client);
            _programmableGatesApi = new JablotronProgrammableGatesCloudApi(_client);
            _thermoApi = new JablotronThermoCloudApi(_client);
            _historyApi = new JablotronHistoryCloudApi(_client);
            _deviceScheduleApi = new JablotronDeviceScheduleCloudApi(_client);
        }

        public string PerformLogin(string source = ApiConstants.DefaultLoginSource)
        {
            var loginResult = _authApi.PerformLogin(source);
            if (loginResult.ServiceId.HasValue)
                CurrentServiceId = loginResult.ServiceId.Value;

            if (!string.IsNullOrWhiteSpace(loginResult.ServiceType))
                CurrentServiceType = loginResult.ServiceType;

            return loginResult.SessionId;
        }

        public void SetServiceContext(int serviceId, string serviceType)
        {
            if (string.IsNullOrWhiteSpace(serviceType))
                throw new ArgumentException("serviceType is required.", nameof(serviceType));

            CurrentServiceId = serviceId;
            CurrentServiceType = serviceType;
        }

        public List<ServiceSummary> GetServices()
        {
            return _servicesApi.GetServices();
        }

        public ServiceInformationData GetServiceInformation(int? serviceId = null)
        {
            return _servicesApi.GetServiceInformation(ResolveServiceId(serviceId));
        }

        public ServiceSettingsResponse GetServiceSettings(int? serviceId = null, string serviceType = null)
        {
            return _servicesApi.GetServiceSettings(ResolveServiceId(serviceId), ResolveServiceType(serviceType));
        }

        public SectionsData GetSections(int? serviceId = null, string serviceType = null)
        {
            return _sectionsApi.GetSections(ResolveServiceId(serviceId), ResolveServiceType(serviceType));
        }

        public List<ThermoDeviceResult> GetThermoDevices(int? serviceId = null, string serviceType = null)
        {
            return _thermoApi.GetThermoDevices(ResolveServiceId(serviceId), ResolveServiceType(serviceType));
        }

        public List<KeyboardItem> GetKeyboardSegments(int? serviceId = null, string serviceType = null)
        {
            return _programmableGatesApi.GetKeyboardSegments(ResolveServiceId(serviceId), ResolveServiceType(serviceType));
        }

        public ProgrammableGatesData GetProgrammableGates(int? serviceId = null, string serviceType = null)
        {
            return _programmableGatesApi.GetProgrammableGates(ResolveServiceId(serviceId), ResolveServiceType(serviceType));
        }

        public List<ServiceHistoryEvent> GetServiceHistory(
            int? serviceId = null,
            string dateFrom = null,
            string dateTo = null,
            string eventIdFrom = null,
            string eventIdTo = null,
            int limit = 20,
            string serviceType = null)
        {
            return _historyApi.GetServiceHistory(
                ResolveServiceId(serviceId),
                dateFrom,
                dateTo,
                eventIdFrom,
                eventIdTo,
                limit,
                ResolveServiceType(serviceType));
        }

        public bool ControlSection(
            int? serviceId = null,
            string componentId = null,
            string state = null,
            string pinCode = null,
            string serviceType = null,
            bool force = false)
        {
            return _sectionsApi.ControlSection(ResolveServiceId(serviceId), componentId, state, pinCode, ResolveServiceType(serviceType), force);
        }

        public bool ControlProgrammableGate(
            int? serviceId = null,
            string componentId = null,
            string state = null,
            string pinCode = null,
            string serviceType = null,
            bool force = false)
        {
            return _programmableGatesApi.ControlProgrammableGate(ResolveServiceId(serviceId), componentId, state, pinCode, ResolveServiceType(serviceType), force);
        }

        public ThermoDeviceState ControlThermoDeviceWithResponse(
            int? serviceId = null,
            string objectDeviceId = null,
            string heatingMode = null,
            double? temperature = null,
            string serviceType = null)
        {
            return _thermoApi.ControlThermoDeviceWithResponse(ResolveServiceId(serviceId), objectDeviceId, heatingMode, temperature, ResolveServiceType(serviceType));
        }

        public bool ControlThermoDevice(
            int? serviceId = null,
            string objectDeviceId = null,
            string heatingMode = null,
            double? temperature = null,
            string serviceType = null)
        {
            return _thermoApi.ControlThermoDevice(ResolveServiceId(serviceId), objectDeviceId, heatingMode, temperature, ResolveServiceType(serviceType));
        }

        public DeviceScheduleResponse GetDeviceSchedule(
            string deviceType,
            string deviceId,
            string roomId,
            int? serviceId = null,
            string serviceType = null)
        {
            return _deviceScheduleApi.GetDeviceSchedule(ResolveServiceId(serviceId), deviceType, deviceId, roomId, ResolveServiceType(serviceType));
        }

        private int ResolveServiceId(int? serviceId)
        {
            if (serviceId.HasValue)
            {
                CurrentServiceId = serviceId.Value;
                return serviceId.Value;
            }

            if (CurrentServiceId.HasValue)
                return CurrentServiceId.Value;

            throw new InvalidOperationException("No service-id is set. Call PerformLogin() first or pass serviceId explicitly.");
        }

        private string ResolveServiceType(string serviceType)
        {
            if (!string.IsNullOrWhiteSpace(serviceType))
            {
                CurrentServiceType = serviceType;
                return serviceType;
            }

            if (!string.IsNullOrWhiteSpace(CurrentServiceType))
                return CurrentServiceType;

            throw new InvalidOperationException("No service-type is set. Call PerformLogin() first or pass serviceType explicitly.");
        }

        public void Dispose()
        {
            _client.Dispose();
        }
    }
}

