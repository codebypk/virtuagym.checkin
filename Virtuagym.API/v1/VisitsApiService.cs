using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Virtuagym.API.Serialization;
using Virtuagym.API.v1.Models;

namespace Virtuagym.API.v1
{
    /// <summary>
    /// Virtuagym v1 Visits API.
    /// GET all, GET single, POST check-in/check-out (POST /visit).
    /// </summary>
    public class VisitsApiService : VirtuagymApiBase
    {
        public VisitsApiService(HttpClient httpClient, JsonSerializerAdapter json,
            string apiKey, string clubSecret, string apiBaseUrl, string clubId)
            : base(httpClient, json, apiKey, clubSecret, apiBaseUrl, clubId)
        {
        }

        /// <summary>
        /// Gets all visits (GET /visit). Optional query parameters (e.g. "member_id=123").
        /// Pagination is followed automatically when paginate=true.
        /// </summary>
        public async Task<List<VisitResult>> GetAllAsync(string queryParams = null, bool paginate = true)
        {
            return await GetAllPaginatedAsync<VisitResult>("visits", paginate, queryParams);
        }

        /// <summary>
        /// Gets a specific visit (GET /visit/{id}).
        /// </summary>
        public async Task<VisitResult> GetByIdAsync(long visitId)
        {
            return await GetSingleAsync<VisitResult>($"visits/{visitId}");
        }

        /// <summary>
        /// Creates a check-in (POST /visit).
        /// </summary>
        public async Task<VisitResult> CreateAsync(object data)
        {
            return await PostSingleAsync<VisitResult>("visits", data);
        }

        /// <summary>
        /// Performs a check-out (POST /visit with action=check_out or check_out_timestamp).
        /// </summary>
        public async Task<VisitResult> UpdateAsync(object data)
        {
            return await PostSingleAsync<VisitResult>("visits", data);
        }

        /// <summary>
        /// Gets active visits (without check-out) for a specific member.
        /// </summary>
        public async Task<List<VisitResult>> GetActiveMemberVisitAsync(long memberId)
        {
            var visits = await GetAllPaginatedAsync<VisitResult>("visits", false, $"member_id={memberId}");
            return visits?.Where(v => v.check_out_timestamp <= 0).ToList();
        }
    }
}
