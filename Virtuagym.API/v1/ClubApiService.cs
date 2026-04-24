using System.Net.Http;
using System.Threading.Tasks;
using Virtuagym.API.Serialization;
using Virtuagym.API.Models;
using Virtuagym.API.v1.Models;

namespace Virtuagym.API.v1
{
    /// <summary>
    /// Virtuagym v1 Club API.
    /// GET Club-Informationen (GET /api/v1/club/{club_id}).
    /// </summary>
    public class ClubApiService : VirtuagymApiBase
    {
        public ClubApiService(HttpClient httpClient, JsonSerializerAdapter json,
            string apiKey, string clubSecret, string apiBaseUrl, string clubId)
            : base(httpClient, json, apiKey, clubSecret, apiBaseUrl, clubId)
        {
        }

        /// <summary>
        /// Ruft die Club-Informationen ab (GET /api/v1/club/{club_id}).
        /// </summary>
        public async Task<ClubV1Result> GetClubAsync()
        {
            string url = BuildUrl("v1", string.Empty);
            var response = await HttpClient.GetAsync(url);
            string content = await response.Content.ReadAsStringAsync();

            ThrowOnApiError(response, content);

            var apiResult = Json.Deserialize<ApiResult<ClubV1Result>>(content);
            return apiResult?.result;
        }
    }
}
