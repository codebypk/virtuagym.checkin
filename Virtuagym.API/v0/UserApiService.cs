using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Virtuagym.API.Serialization;
using Virtuagym.API.Models;
using Virtuagym.API.v0.Models;

namespace Virtuagym.API.v0
{
    /// <summary>
    /// Virtuagym v0 User API: User per user_id laden.
    /// </summary>
    [Obsolete("v0 API ist veraltet. Verwende die v1 API.")]
    public class UserApiService : VirtuagymApiBase
    {
        public UserApiService(HttpClient httpClient, JsonSerializerAdapter json,
            string apiKey, string clubSecret, string apiBaseUrl, string clubId, string username, string password)
            : base(httpClient, json, apiKey, clubSecret, apiBaseUrl, null, username, password)
        {}

        /// <summary>
        /// Ruft die Informationen eines Benutzers ab (v0 GET /users/{userId}).
        /// </summary>
        public async Task<UserResult> GetByIdAsync(string userId = "current")
        {
            var result = await GetSingleV0Async<UserResult>($"user/{userId}");
            return result;
        }

    }
}

