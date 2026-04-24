using System.Collections.Generic;
using System.IO;
using System.Text;
using Virtuagym.API.Serialization;
using Virtuagym.CheckIn.Core.Helper;
using Virtuagym.CheckIn.Core.Models;

namespace Virtuagym.CheckIn.Core.Services
{
    /// <summary>
    /// Loads club services from Resources\club_service.json.
    /// Returns an empty list if the file is not found.
    /// </summary>
    public static class ClubServiceLoader
    {
        private static readonly string ServicePath = Path.Combine(
            System.AppDomain.CurrentDomain.BaseDirectory, Constants.ClubServiceFilePath);

        private static List<ClubService> _cache;

        /// <summary>Loads all available club services (with cache).</summary>
        public static List<ClubService> Load()
        {
            if (_cache != null) return _cache;

            try
            {
                if (File.Exists(ServicePath))
                {
                    string json = File.ReadAllText(ServicePath, Encoding.UTF8);
                    var serializer = new JsonSerializerAdapter();
                    _cache = serializer.Deserialize<List<ClubService>>(json);
                }
            }
            catch { /* File missing or corrupted → empty list as fallback */ }

            if (_cache == null)
                _cache = new List<ClubService>();

            return _cache;
        }

        /// <summary>Returns the service with the specified service_id, or null.</summary>
        public static ClubService GetByServiceId(string serviceId)
        {
            if (string.IsNullOrWhiteSpace(serviceId)) return null;
            return Load().Find(s => s.service_id == serviceId);
        }

        /// <summary>Clears the cache so the file is re-read on the next Load() call.</summary>
        public static void ClearCache()
        {
            _cache = null;
        }
    }
}
