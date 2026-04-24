using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Virtuagym.API.Serialization;
using Virtuagym.API.Models;

namespace Virtuagym.API
{
    /// <summary>
    /// Abstract base class for all Virtuagym API services.
    /// Provides HttpClient, JSON serialization, URL construction and generic HTTP methods.
    /// </summary>
    public abstract class VirtuagymApiBase
    {
        protected readonly HttpClient HttpClient;
        protected readonly JsonSerializerAdapter Json;
        protected readonly string ApiKey;
        protected readonly string ClubSecret;
        protected readonly string ApiBaseUrl;
        protected readonly string ClubId;
        protected readonly string BasicAuthHeaderValue;

        protected VirtuagymApiBase(HttpClient httpClient, JsonSerializerAdapter json,
            string apiKey, string clubSecret, string apiBaseUrl, string clubId = null,
            string username = null, string password = null)
        {
            HttpClient = httpClient;
            Json = json;
            ApiKey = apiKey;
            ClubSecret = clubSecret;
            ApiBaseUrl = apiBaseUrl;
            ClubId = clubId;

            if (!string.IsNullOrEmpty(username) && !string.IsNullOrEmpty(password))
            {
                BasicAuthHeaderValue = "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes(username + ":" + password));
            }
        }

        /// <summary>
        /// Builds the full API URL (club-based, e.g. v1/club/{id}/resource).
        /// Detects whether the resource already contains query parameters and uses &amp; instead of ?.
        /// </summary>
        protected string BuildUrl(string apiVersion, string resource)
        {
            // apiVersion v0 requires /club/{ClubId} only for /devices
            string baseUrl = $"{ApiBaseUrl}/{apiVersion}";
            if (apiVersion != "v0" || resource.ToLower() == "devices")
                baseUrl += $"/club/{ClubId}";

            if (String.IsNullOrEmpty(resource))
                return $"{baseUrl}?api_key={ApiKey}&club_secret={ClubSecret}";

            string separator = resource.Contains('?') ? "&" : "?";
            return $"{baseUrl}/{resource}{separator}api_key={ApiKey}&club_secret={ClubSecret}";
        }

        /// <summary>
        /// Extracts the club ID from the club secret (e.g. "CS-61464-..." → "61464").
        /// </summary>
        public static string ExtractClubId(string clubSecret)
        {
            if (string.IsNullOrWhiteSpace(clubSecret))
                return null;

            var match = Regex.Match(clubSecret, @"(?<=CS-)(.*?)(?=-)");
            return match.Success ? match.Groups[0].Value : null;
        }

        /// <summary>
        /// Extracts the device ID from the club secret / CheckinKey.
        /// Example: "CS-61464-CHECKIN7821-dDLVJAUZpfF9o0tHV01ipDElM" → "7821".
        /// </summary>
        public static string ExtractDeviceId(string clubSecret)
        {
            if (string.IsNullOrWhiteSpace(clubSecret))
                return null;

            var match = Regex.Match(clubSecret, @"CHECKIN(\d+)");
            return match.Success ? match.Groups[1].Value : null;
        }

        /// <summary>
        /// Checks the HTTP response and throws a VirtuagymApiException on error status codes.
        /// Attempts to read the Virtuagym-specific status code and message from the JSON body.
        /// </summary>
        protected void ThrowOnApiError(HttpResponseMessage response, string responseBody)
        {
            if (response.IsSuccessStatusCode)
                return;

            int httpCode = (int)response.StatusCode;

            try
            {
                var errorResult = Json.Deserialize<ApiErrorResult>(responseBody);
                if (errorResult?.status != null)
                {
                    throw new VirtuagymApiException(
                        httpCode,
                        errorResult.status.statuscode,
                        errorResult.status.statusmessage,
                        responseBody);
                }
            }
            catch (VirtuagymApiException)
            {
                throw;
            }
            catch { /* Not in v1 format → try v0 format */ }

            try
            {
                var v0Result = Json.Deserialize<ApiResultV0List<object>>(responseBody);
                if (v0Result != null && v0Result.statuscode > 0)
                {
                    throw new VirtuagymApiException(
                        httpCode,
                        v0Result.statuscode,
                        v0Result.statusmessage,
                        responseBody);
                }
            }
            catch (VirtuagymApiException)
            {
                throw;
            }
            catch { /* Not in v0 format → throw generic error */ }

            throw new VirtuagymApiException(httpCode, responseBody);
        }

        #region Generic HTTP Methods (v1)

        /// <summary>
        /// GET with automatic pagination via next_page (v1 API).
        /// </summary>
        protected async Task<List<T>> GetAllPaginatedAsync<T>(string resource, bool paginate = true, string queryParams = null)
        {
            var allResults = new List<T>();
            string baseResource = resource;
            string url = BuildUrl("v1", resource);
            if (!string.IsNullOrEmpty(queryParams))
                url += "&" + queryParams;

            while (!string.IsNullOrEmpty(url))
            {
                var response = await HttpClient.GetAsync(url);
                string content = await response.Content.ReadAsStringAsync();

                ThrowOnApiError(response, content);

                var apiResult = Json.Deserialize<ApiResultV1<T>>(content);

                if (apiResult?.result != null && apiResult.result.Count > 0)
                {
                    allResults.AddRange(apiResult.result);
                }
                else
                {
                    break;
                }

                // Nur erste Seite laden wenn Paginierung deaktiviert
                if (!paginate)
                    break;

                if (apiResult?.status != null && !string.IsNullOrEmpty(apiResult.status.next_page))
                {
                    string nextPage = apiResult.status.next_page;

                    if (nextPage.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                        || nextPage.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                    {
                        url = nextPage;
                    }
                    else
                    {
                        string baseUrl = BuildUrl("v1", baseResource);
                        if (!string.IsNullOrEmpty(queryParams))
                            baseUrl += "&" + queryParams;
                        if (nextPage.StartsWith('&') || nextPage.StartsWith('?'))
                        {
                            url = baseUrl + nextPage;
                        }
                        else
                        {
                            url = baseUrl + "&" + nextPage;
                        }
                    }
                }
                else if (apiResult?.status != null && apiResult.status.results_remaining > 0)
                {
                    var lastItem = apiResult.result[apiResult.result.Count - 1];
                    var lastId = ExtractIdFromResult(lastItem);

                    if (lastId.HasValue)
                    {
                        string baseUrl = BuildUrl("v1", baseResource);
                        if (!string.IsNullOrEmpty(queryParams))
                            baseUrl += "&" + queryParams;
                        string separator = baseUrl.Contains('?') ? "&" : "?";
                        url = baseUrl + separator + "from_id=" + lastId.Value;
                    }
                    else
                    {
                        break;
                    }
                }
                else
                {
                    url = null;
                }
            }

            return allResults.Count > 0 ? allResults : null;
        }

        private static long? ExtractIdFromResult<T>(T item)
        {
            if (item == null) return null;

            var type = item.GetType();
            string[] idFields = { "member_id", "id", "event_participant_id", "note_id", "instance_id", "membership_id" };
            foreach (var fieldName in idFields)
            {
                var prop = type.GetProperty(fieldName);
                if (prop != null)
                {
                    var value = prop.GetValue(item, null);
                    if (value is long l) return l;
                    if (value is int i) return i;
                }
            }

            return null;
        }

        protected async Task<T> GetSingleAsync<T>(string resource) where T : class
        {
            string url = BuildUrl("v1", resource);
            var response = await HttpClient.GetAsync(url);
            string content = await response.Content.ReadAsStringAsync();

            ThrowOnApiError(response, content);

            var apiResult = Json.Deserialize<ApiResultV1<T>>(content);
            return apiResult?.result?.Count > 0 ? apiResult.result[0] : null;
        }

        protected async Task<T> PutSingleAsync<T>(string resource, object data) where T : class
        {
            string url = BuildUrl("v1", resource);
            var jsonBody = Json.Serialize(data);
            var httpContent = new StringContent(jsonBody, Encoding.UTF8, "application/json");

            var request = new HttpRequestMessage(HttpMethod.Put, url) { Content = httpContent };
            var response = await HttpClient.SendAsync(request);
            string content = await response.Content.ReadAsStringAsync();

            ThrowOnApiError(response, content);

            var apiResult = Json.Deserialize<ApiResult<T>>(content);
            return apiResult?.result;
        }

        protected async Task<T> PostSingleAsync<T>(string resource, object data) where T : class
        {
            string url = BuildUrl("v1", resource);
            var jsonBody = Json.Serialize(data);
            var httpContent = new StringContent(jsonBody, Encoding.UTF8, "application/json");

            var response = await HttpClient.PostAsync(url, httpContent);
            string content = await response.Content.ReadAsStringAsync();

            ThrowOnApiError(response, content);

            var apiResult = Json.Deserialize<ApiResult<T>>(content);
            return apiResult?.result;
        }

        protected async Task<bool> DeleteAsync(string resource)
        {
            string url = BuildUrl("v1", resource);
            var response = await HttpClient.DeleteAsync(url);
            string content = await response.Content.ReadAsStringAsync();

            ThrowOnApiError(response, content);

            return true;
        }

        #endregion

        #region Generic HTTP Methods (v0)
        protected async Task<T> PutSingleV0Async<T>(string resource, object data) where T : class
        {
            string url = BuildUrl("v0", resource);
            var request = new HttpRequestMessage(HttpMethod.Put, url);
            if (!string.IsNullOrEmpty(BasicAuthHeaderValue))
            {
                request.Headers.Add("Authorization", BasicAuthHeaderValue);
            }

            var jsonBody = Json.Serialize(data);
            request.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

            var response = await HttpClient.SendAsync(request);
            string content = await response.Content.ReadAsStringAsync();

            ThrowOnApiError(response, content);

            var apiResult = Json.Deserialize<ApiResult<T>>(content);
            return apiResult?.result;
        }

        protected async Task<T> GetSingleV0Async<T>(string resource) where T : class
        {
            string url = BuildUrl("v0",resource);
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            if (!string.IsNullOrEmpty(BasicAuthHeaderValue)) {
                request.Headers.Add("Authorization", BasicAuthHeaderValue);
            }

            var response = await HttpClient.SendAsync(request);
            string content = await response.Content.ReadAsStringAsync();
            
            ThrowOnApiError(response, content);
            
            var apiResult = Json.Deserialize<ApiResult<T>>(content);
            return apiResult?.result;
        }

        #endregion
    }
}
