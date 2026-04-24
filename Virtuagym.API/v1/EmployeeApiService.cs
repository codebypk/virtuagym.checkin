using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Virtuagym.API.Serialization;
using Virtuagym.API.v1.Models;

namespace Virtuagym.API.v1
{
    /// <summary>
    /// Virtuagym v1 Club Employees API.
    /// GET alle, GET einzeln, PUT erstellen, PUT aktualisieren.
    /// </summary>
    public class EmployeeApiService : VirtuagymApiBase
    {
        public EmployeeApiService(HttpClient httpClient, JsonSerializerAdapter json,
            string apiKey, string clubSecret, string apiBaseUrl, string clubId)
            : base(httpClient, json, apiKey, clubSecret, apiBaseUrl, clubId)
        {
        }

        /// <summary>
        /// Ruft alle Mitarbeiter ab (GET /employee). Paginierung wird automatisch verfolgt.
        /// </summary>
        public async Task<List<EmployeeResult>> GetAllAsync()
        {
            return await GetAllPaginatedAsync<EmployeeResult>("employee");
        }

        /// <summary>
        /// Ruft einen bestimmten Mitarbeiter ab (GET /employee/{id}).
        /// </summary>
        public async Task<EmployeeResult> GetByIdAsync(long employeeId)
        {
            return await GetSingleAsync<EmployeeResult>($"employee/{employeeId}");
        }

        /// <summary>
        /// Erstellt einen neuen Mitarbeiter (PUT /employee).
        /// </summary>
        public async Task<EmployeeResult> CreateAsync(EmployeeResult employee)
        {
            return await PutSingleAsync<EmployeeResult>("employee", employee);
        }

        /// <summary>
        /// Aktualisiert einen bestehenden Mitarbeiter (PUT /employee/{id}).
        /// </summary>
        public async Task<EmployeeResult> UpdateAsync(long employeeId, EmployeeResult employee)
        {
            return await PutSingleAsync<EmployeeResult>($"employee/{employeeId}", employee);
        }
    }
}

