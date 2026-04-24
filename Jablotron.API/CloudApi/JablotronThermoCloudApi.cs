using System;
using System.Collections.Generic;
using System.Linq;
using Jablotron.API.CloudApi.Models;

namespace Jablotron.API.CloudApi
{
    internal sealed class JablotronThermoCloudApi
    {
        private readonly JablotronCloudApiClient _client;

        public JablotronThermoCloudApi(JablotronCloudApiClient client)
        {
            _client = client;
        }

        public List<ThermoDeviceResult> GetThermoDevices(int serviceId, string serviceType)
        {
            var response = _client.SendRequest<ThermoDevicesRequest, ApiEnvelope<ThermoDevicesData>>(
                string.Format(ApiConstants.EndpointThermoDevicesFormat, serviceType),
                new ThermoDevicesRequest
                {
                    ConnectDevice = true,
                    ListType = "FULL",
                    ServiceId = serviceId,
                    ServiceStates = false
                });

            var data = response?.Data;
            var states = data?.States ?? new List<ThermoDeviceState>();
            var devices = data?.Devices ?? new List<ThermoDeviceDetails>();
            var deviceById = devices.ToDictionary(d => d.ObjectDeviceId ?? string.Empty, d => d);

            var result = new List<ThermoDeviceResult>();
            foreach (var state in states)
            {
                ThermoDeviceDetails details;
                if (!deviceById.TryGetValue(state.ObjectDeviceId ?? string.Empty, out details))
                    details = new ThermoDeviceDetails();

                result.Add(new ThermoDeviceResult
                {
                    ObjectDeviceId = state.ObjectDeviceId,
                    Name = details.Name,
                    Temperature = state.Temperature,
                    LastTemperatureTime = state.LastTemperatureTime,
                    Device = details,
                    State = state
                });
            }

            return result;
        }

        public ThermoDeviceState ControlThermoDeviceWithResponse(
            int serviceId,
            string objectDeviceId,
            string heatingMode = null,
            double? temperature = null,
            string serviceType = null)
        {
            if (string.Equals(heatingMode, ApiConstants.StateScheduled, StringComparison.OrdinalIgnoreCase) && temperature.HasValue)
                throw new ControlActionException("Temperature cannot be set when setting heating mode to SCHEDULED.");

            var response = _client.SendRequest<ControlThermoDeviceRequest, ApiEnvelope<ControlThermoDeviceData>>(
                string.Format(ApiConstants.EndpointControlThermoDeviceFormat, serviceType),
                new ControlThermoDeviceRequest
                {
                    ServiceId = serviceId,
                    ControlComponents = new List<ControlThermoComponent>
                    {
                        new ControlThermoComponent
                        {
                            ComponentId = objectDeviceId,
                            Actions = new ControlThermoAction
                            {
                                SetHeatingMode = string.IsNullOrWhiteSpace(heatingMode) ? null : heatingMode.ToUpperInvariant(),
                                SetTemperature = temperature
                            }
                        }
                    }
                });

            var controlErrors = response?.Data?.ControlErrors ?? new List<ControlError>();
            if (controlErrors.Count > 0)
                throw new ControlActionException("Thermo device control failed.", controlErrors);

            return (response?.Data?.States ?? new List<ThermoDeviceState>())
                .FirstOrDefault(s => string.Equals(s.ObjectDeviceId, objectDeviceId, StringComparison.Ordinal));
        }

        public bool ControlThermoDevice(
            int serviceId,
            string objectDeviceId,
            string heatingMode = null,
            double? temperature = null,
            string serviceType = null)
        {
            return ControlThermoDeviceWithResponse(serviceId, objectDeviceId, heatingMode, temperature, serviceType) != null;
        }
    }
}

