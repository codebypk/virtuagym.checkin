using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Virtuagym.API.Serialization;
using Virtuagym.API.v1.Models;

namespace Virtuagym.API.v1
{
    /// <summary>
    /// Virtuagym v1 Club Members API.
    /// GET alle, GET einzeln, PUT erstellen, PUT aktualisieren, PUT create_or_update, POST activate_user.
    /// </summary>
    public class MemberApiService : VirtuagymApiBase
    {
        public MemberApiService(HttpClient httpClient, JsonSerializerAdapter json,
            string apiKey, string clubSecret, string apiBaseUrl, string clubId)
            : base(httpClient, json, apiKey, clubSecret, apiBaseUrl, clubId)
        {
        }

        /// <summary>
        /// Ruft alle Mitglieder ab (GET /member). Paginierung wird automatisch verfolgt wenn paginate=true.
        /// </summary>
        public async Task<List<MemberResult>> GetAllAsync(bool paginate = true)
        {
            return await GetAllPaginatedAsync<MemberResult>("member", paginate);
        }

        /// <summary>
        /// Ruft ein bestimmtes Mitglied ab (GET /member/{id}).
        /// </summary>
        public async Task<MemberResult> GetByIdAsync(string memberId)
        {
            return await GetSingleAsync<MemberResult>($"member/{memberId}");
        }

        /// <summary>
        /// Erstellt ein neues Mitglied (PUT /member).
        /// </summary>
        public async Task<MemberResult> CreateAsync(MemberResult member)
        {
            return await PutSingleAsync<MemberResult>("member", member);
        }

        /// <summary>
        /// Aktualisiert ein bestehendes Mitglied (PUT /member/{id}).
        /// </summary>
        public async Task<MemberResult> UpdateAsync(string memberId, MemberResult member)
        {
            return await PutSingleAsync<MemberResult>($"member/{memberId}", member);
        }

        /// <summary>
        /// Aktualisiert nur den RFID-Tag eines Mitglieds (PUT /member/{id}).
        /// Sendet ausschließlich das Feld rfid_tag an die API.
        /// </summary>
        public async Task<MemberResult> UpdateRfidTagAsync(string memberId, string rfidTag)
        {
            return await PutSingleAsync<MemberResult>($"member/{memberId}", new { rfid_tag = rfidTag });
        }

        /// <summary>
        /// Erstellt oder aktualisiert ein Mitglied basierend auf external_id (PUT /member/create_or_update).
        /// </summary>
        public async Task<MemberResult> CreateOrUpdateAsync(MemberResult member)
        {
            return await PutSingleAsync<MemberResult>("member/create_or_update", member);
        }

        /// <summary>
        /// Sucht ein Mitglied anhand seines RFID-Tags (GET /member?rfid_tag=...).
        /// Gibt null zurück, wenn kein Mitglied mit diesem Tag gefunden wurde.
        /// </summary>
        public async Task<MemberResult> GetByRfidTagAsync(string rfidTag)
        {
            return await GetSingleAsync<MemberResult>("member?rfid_tag=" + Uri.EscapeDataString(rfidTag));
        }

        /// <summary>
        /// Aktiviert das Benutzerprofil eines Mitglieds (POST /member/activate_user).
        /// </summary>
        public async Task<MemberActivationResult> ActivateUserAsync(object activationData)
        {
            return await PostSingleAsync<MemberActivationResult>("member/activate_user", activationData);
        }
    }
}

