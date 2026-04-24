using System;
using System.Collections.Generic;
using System.Linq;
using Jablotron.API.CloudApi.Models;

namespace Jablotron.API.CloudApi
{
    internal sealed class JablotronSectionsCloudApi
    {
        private readonly JablotronCloudApiClient _client;

        public JablotronSectionsCloudApi(JablotronCloudApiClient client)
        {
            _client = client;
        }

        public SectionsData GetSections(int serviceId, string serviceType)
        {
            var response = _client.SendRequest<SectionsRequest, ApiEnvelope<SectionsData>>(
                string.Format(ApiConstants.EndpointSectionsFormat, serviceType),
                new SectionsRequest
                {
                    ConnectDevice = true,
                    ListType = "FULL",
                    ServiceId = serviceId,
                    ServiceStates = true
                });

            return response?.Data;
        }

        public bool ControlSection(
            int serviceId,
            string componentId,
            string state,
            string pinCode = null,
            string serviceType = null,
            bool force = false)
        {
            var desiredState = (state ?? string.Empty).ToUpperInvariant();
            if (desiredState != ApiConstants.StateArm && desiredState != ApiConstants.StatePartialArm && desiredState != ApiConstants.StateDisarm)
                throw new ArgumentException("State must be ARM, PARTIAL_ARM or DISARM.", nameof(state));

            var response = _client.SendRequest<ControlComponentRequest, ApiEnvelope<ControlComponentData>>(
                string.Format(ApiConstants.EndpointControlComponentFormat, serviceType),
                CreateControlComponentRequest(serviceId, componentId, ApiConstants.ControlActionSection, desiredState, _client.GetProvidedPinOrDefaultPin(pinCode), force));

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

