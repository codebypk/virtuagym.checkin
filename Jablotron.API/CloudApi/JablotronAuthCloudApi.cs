using Jablotron.API.CloudApi.Models;

namespace Jablotron.API.CloudApi
{
    internal sealed class JablotronAuthCloudApi
    {
        private readonly JablotronCloudApiClient _client;

        internal sealed class LoginResult
        {
            public string SessionId { get; set; }
            public int? ServiceId { get; set; }
            public string ServiceType { get; set; }
        }

        public JablotronAuthCloudApi(JablotronCloudApiClient client)
        {
            _client = client;
        }

        public LoginResult PerformLogin(string source = ApiConstants.DefaultLoginSource)
        {
            var response = _client.SendRequest<LoginRequest, LoginResponse>(ApiConstants.EndpointLogin, new LoginRequest
            {
                Login = _client.Username,
                Password = _client.Password,
                Source = source
            });

            var sessionCookie = _client.GetSessionCookie();
            if (sessionCookie == null || string.IsNullOrWhiteSpace(sessionCookie.Value))
                throw new InvalidSessionIdException("Login response does not contain a valid session id.");

            var serviceDetail = response?.Data?.ServiceData?.ServiceDetail;

            return new LoginResult
            {
                SessionId = sessionCookie.Value,
                ServiceId = serviceDetail != null ? (int?)serviceDetail.ServiceId : null,
                ServiceType = serviceDetail?.ServiceType
            };
        }
    }
}

