using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Virtuagym.API.Serialization;
using Virtuagym.API.v1.Models;

namespace Virtuagym.API.v1
{
    /// <summary>
    /// Virtuagym v1 Club Member Credits API.
    /// GET abrufen, PUT zuweisen.
    /// </summary>
    public class CreditApiService : VirtuagymApiBase
    {
        public CreditApiService(HttpClient httpClient, JsonSerializerAdapter json,
            string apiKey, string clubSecret, string apiBaseUrl, string clubId)
            : base(httpClient, json, apiKey, clubSecret, apiBaseUrl, clubId)
        {
        }

        /// <summary>
        /// Ruft Credits eines Mitglieds ab (GET /credit?member_id=...).
        /// </summary>
        public async Task<List<CreditResult>> GetByMemberAsync(string memberId)
        {
            return await GetAllPaginatedAsync<CreditResult>("credit", true, $"member_id={memberId}");
        }

        /// <summary>
        /// Weist einem Mitglied Credits zu (PUT /credit).
        /// </summary>
        public async Task<CreditResult> AssignAsync(CreditResult credit)
        {
            return await PutSingleAsync<CreditResult>("credit", credit);
        }
    }
}

