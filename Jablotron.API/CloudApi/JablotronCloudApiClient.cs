using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using Jablotron.API.CloudApi.Models;
using Jablotron.API.Serialization;

namespace Jablotron.API.CloudApi
{
    internal sealed class JablotronCloudApiClient : IDisposable
    {
        private static readonly JsonSerializerAdapter Json = new();
        private readonly string _defaultPinCode;
        private readonly HttpClient _httpClient;
        private readonly CookieContainer _cookieContainer;
        private readonly bool _ownsHttpClient;
        private bool _disposed;

        public string ApiBaseUrl { get; }
        public string Username { get; }
        public string Password { get; }

        public JablotronCloudApiClient(
            string apiBaseUrl,
            string username,
            string password,
            string defaultPinCode = null,
            HttpClient httpClient = null)
        {
            if (string.IsNullOrWhiteSpace(apiBaseUrl)) throw new ArgumentException("API base url is required.", nameof(apiBaseUrl));
            if (string.IsNullOrWhiteSpace(username)) throw new ArgumentException("Username is required.", nameof(username));
            if (string.IsNullOrWhiteSpace(password)) throw new ArgumentException("Password is required.", nameof(password));

            ApiBaseUrl = apiBaseUrl.TrimEnd('/');
            Username = username;
            Password = password;
            _defaultPinCode = defaultPinCode;

            if (httpClient == null)
            {
                _cookieContainer = new CookieContainer();
                var handler = new HttpClientHandler
                {
                    CookieContainer = _cookieContainer,
                    UseCookies = true,
                    AutomaticDecompression = DecompressionMethods.Deflate | DecompressionMethods.GZip
                };

                _httpClient = new HttpClient(handler);
                _ownsHttpClient = true;
            }
            else
            {
                _httpClient = httpClient;
                _ownsHttpClient = false;
                _cookieContainer = new CookieContainer();
            }

            if (!_httpClient.DefaultRequestHeaders.Contains(ApiConstants.HeaderVendorId))
                _httpClient.DefaultRequestHeaders.Add(ApiConstants.HeaderVendorId, ApiConstants.HeaderVendorIdValue);
            if (!_httpClient.DefaultRequestHeaders.Contains(ApiConstants.HeaderClientVersion))
                _httpClient.DefaultRequestHeaders.Add(ApiConstants.HeaderClientVersion, ApiConstants.HeaderClientVersionValue);
            if (!_httpClient.DefaultRequestHeaders.Contains(ApiConstants.HeaderAcceptLanguage))
                _httpClient.DefaultRequestHeaders.Add(ApiConstants.HeaderAcceptLanguage, ApiConstants.DefaultAcceptLanguage);
            if (!_httpClient.DefaultRequestHeaders.Contains(ApiConstants.HeaderAccept))
                _httpClient.DefaultRequestHeaders.Add(ApiConstants.HeaderAccept, ApiConstants.ContentTypeJson);
        }

        public TResponse SendRequest<TRequest, TResponse>(string endpoint, TRequest payload)
        {
            return SendRequestInternal<TRequest, TResponse>(endpoint, payload, true);
        }

        private TResponse SendRequestInternal<TRequest, TResponse>(string endpoint, TRequest payload, bool allowReloginRetry)
        {
            ThrowIfDisposed();
            if (string.IsNullOrWhiteSpace(endpoint)) throw new ArgumentException("Endpoint is required.", nameof(endpoint));
            ArgumentNullException.ThrowIfNull(payload);

            var requestUrl = string.Format("{0}/{1}", ApiBaseUrl, endpoint.TrimStart('/'));
            var body = Serialize(payload);

            using (var request = new HttpRequestMessage(HttpMethod.Post, requestUrl))
            {
                request.Content = new StringContent(body, Encoding.UTF8, ApiConstants.ContentTypeJson);

                var response = _httpClient.SendAsync(request).GetAwaiter().GetResult();
                var content = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();

                if (response.StatusCode == HttpStatusCode.BadRequest)
                    throw new BadRequestException("Request is not valid, please review provided parameters.", content);

                if (response.StatusCode == HttpStatusCode.Unauthorized)
                {
                    bool canRetry = allowReloginRetry
                                    && !string.Equals(endpoint, ApiConstants.EndpointLogin, StringComparison.OrdinalIgnoreCase)
                                    && HasSessionExpiredError(content);

                    if (canRetry)
                    {
                        PerformLogin();
                        return SendRequestInternal<TRequest, TResponse>(endpoint, payload, false);
                    }

                    throw new UnauthorizedException("Failed to authenticate using entered credentials or session id expired.", content);
                }

                if ((int)response.StatusCode == 408)
                    throw new SessionExpiredException("Session expired, please re-login.", content);
                if (!response.IsSuccessStatusCode)
                    throw new JablotronApiException("Jablotron API request failed.", (int)response.StatusCode, content);

                return Deserialize<TResponse>(content);
            }
        }

        private void PerformLogin()
        {
            SendRequestInternal<LoginRequest, ApiEnvelope<object>>(
                ApiConstants.EndpointLogin,
                new LoginRequest
                {
                    Login = Username,
                    Password = Password,
                    Source = ApiConstants.DefaultLoginSource
                },
                false);
        }

        private static bool HasSessionExpiredError(string content)
        {
            if (string.IsNullOrWhiteSpace(content)) return false;

            try
            {
                var response = Deserialize<ApiEnvelope<object>>(content);
                return (response?.Errors ?? new System.Collections.Generic.List<ApiError>())
                    .Any(e => string.Equals(e.Code, ApiConstants.ErrorUserSessionExpired, StringComparison.OrdinalIgnoreCase));
            }
            catch
            {
                return false;
            }
        }

        public Cookie GetSessionCookie()
        {
            try
            {
                var uri = new Uri(ApiBaseUrl + "/");
                return _cookieContainer.GetCookies(uri).Cast<Cookie>()
                    .FirstOrDefault(c => string.Equals(c.Name, "PHPSESSID", StringComparison.OrdinalIgnoreCase));
            }
            catch
            {
                return null;
            }
        }

        public string GetProvidedPinOrDefaultPin(string pinCode)
        {
            if (!string.IsNullOrWhiteSpace(pinCode)) return pinCode;
            if (!string.IsNullOrWhiteSpace(_defaultPinCode)) return _defaultPinCode;
            throw new NoPinCodeException("Please, provide pin code or set default pin code.");
        }

        private static string Serialize<T>(T value)
        {
            return Json.Serialize(value);
        }

        private static T Deserialize<T>(string content)
        {
            if (string.IsNullOrWhiteSpace(content)) return default;

            return Json.Deserialize<T>(content);
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(JablotronCloudApiClient));
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_ownsHttpClient)
                _httpClient.Dispose();
        }
    }
}

