using System;
using System.Collections.Generic;
using System.Linq;
using Jablotron.API.CloudApi.Models;

namespace Jablotron.API.CloudApi
{
    internal sealed class JablotronProgrammableGatesCloudApi
    {
        private readonly JablotronCloudApiClient _client;

        public JablotronProgrammableGatesCloudApi(JablotronCloudApiClient client)
        {
            _client = client;
        }

        public List<KeyboardItem> GetKeyboardSegments(int serviceId, string serviceType)
        {
            var response = _client.SendRequest<KeyboardSegmentsRequest, ApiEnvelope<KeyboardSegmentsData>>(
                string.Format(ApiConstants.EndpointKeyboardSegmentsFormat, serviceType),
                new KeyboardSegmentsRequest
                {
                    ConnectDevice = false,
                    ListType = "FULL",
                    ServiceId = serviceId,
                    ServiceStates = false
                });

            return response?.Data?.Keyboards ?? new List<KeyboardItem>();
        }

        public ProgrammableGatesData GetProgrammableGates(int serviceId, string serviceType)
        {
            var response = _client.SendRequest<ProgrammableGatesRequest, ApiEnvelope<ProgrammableGatesData>>(
                string.Format(ApiConstants.EndpointProgrammableGatesFormat, serviceType),
                new ProgrammableGatesRequest
                {
                    ConnectDevice = true,
                    ListType = "FULL",
                    ServiceId = serviceId,
                    ServiceStates = true
                });

            return response?.Data;
        }

        public bool ControlProgrammableGate(
            int serviceId,
            string componentId,
            string state,
            string pinCode = null,
            string serviceType = null,
            bool force = false)
        {
            var desiredState = (state ?? string.Empty).ToUpperInvariant();
            if (desiredState != ApiConstants.StateOn && desiredState != ApiConstants.StateOff)
                throw new ArgumentException("State must be ON or OFF.", nameof(state));

            var response = _client.SendRequest<ControlComponentRequest, ApiEnvelope<ControlComponentData>>(
                string.Format(ApiConstants.EndpointControlComponentFormat, serviceType),
                CreateControlComponentRequest(serviceId, componentId, ApiConstants.ControlActionProgrammableGate, desiredState, _client.GetProvidedPinOrDefaultPin(pinCode), force));

            return WasControlActionSuccessful(response?.Data, componentId, desiredState);
        }

        private static ControlComponentRequest CreateControlComponentRequest(
            int serviceId,
            string componentId,
            string action,
            string value,
            string pinCode,
            bool force)
        {
            if (string.IsNullOrWhiteSpace(componentId))
                throw new ArgumentException("Component id is required.", nameof(componentId));

            return new ControlComponentRequest
            {
                ServiceId = serviceId,
                Authorization = new ControlAuthorization { AuthorizationCode = pinCode },
                ControlComponents = new List<ControlComponentItem>
                {
                    new ControlComponentItem
                    {
                        Actions = new ControlAction { Action = action, Value = value },
                        ComponentId = componentId,
                        Force = force
                    }
                }
            };
        }

        private static bool WasControlActionSuccessful(ControlComponentData responseData, string componentId, string state)
        {
            foreach (var error in responseData?.ControlErrors ?? new List<ControlError>())
            {
                if (string.Equals(error.ErrorCode, ApiConstants.ControlErrorWrongCode, StringComparison.OrdinalIgnoreCase))
                    throw new IncorrectPinCodeException("Provided pin code is not valid.");

                throw new ControlActionException("Control action failed with unexpected error.", error);
            }

            return (responseData?.States ?? new List<ServiceState>()).Any(data =>
                string.Equals(data.ComponentId, componentId, StringComparison.Ordinal) &&
                string.Equals(data.State, state, StringComparison.OrdinalIgnoreCase));
        }
    }
}

