using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Virtuagym.API.Serialization;
using Virtuagym.CheckIn.Core.Helper;
using Virtuagym.CheckIn.Core.Models;

namespace Virtuagym.CheckIn.Core.Services
{
    /// <summary>
    /// Loads Jablotron Programmable Gate entries from Resources\data\jablotron_gates.json.
    /// Returns an empty list if the file is not found.
    /// </summary>
    public static class PgGateLoader
    {
        private static readonly string GatesPath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, Constants.JablotronGatesFilePath);

        private static List<PgGateEntry> _cache;

        /// <summary>Loads all available Programmable Gate entries (with cache).</summary>
        public static List<PgGateEntry> Load()
        {
            if (_cache != null) return _cache;

            try
            {
                if (File.Exists(GatesPath))
                {
                    string json = File.ReadAllText(GatesPath, Encoding.UTF8);
                    var serializer = new JsonSerializerAdapter();
                    var rows = serializer.Deserialize<List<Dictionary<string, object>>>(json)
                               ?? new List<Dictionary<string, object>>();

                    _cache = new List<PgGateEntry>();
                    foreach (var row in rows)
                    {
                        _cache.Add(new PgGateEntry
                        {
                            ServiceId = GetDictString(row, "service-id"),
                            CloudComponentId = GetDictString(row, "cloud-component-id"),
                            Name = GetDictString(row, "name"),
                            CanControl = GetDictBool(row, "can-control"),
                            NeedAuthorization = GetDictBool(row, "need-authorization")
                        });
                    }
                }
            }
            catch { /* File missing or corrupted → empty list as fallback */ }

            if (_cache == null)
                _cache = new List<PgGateEntry>();

            return _cache;
        }

        /// <summary>Returns the gate entry with the specified CloudComponentId, or null.</summary>
        public static PgGateEntry GetByComponentId(string componentId)
        {
            if (string.IsNullOrWhiteSpace(componentId)) return null;
            return Load().Find(g =>
                string.Equals(g.CloudComponentId, componentId, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>Clears the cache so the file is re-read on the next Load() call.</summary>
        public static void ClearCache()
        {
            _cache = null;
        }

        private static string GetDictString(Dictionary<string, object> row, string key)
        {
            if (row == null || !row.ContainsKey(key) || row[key] == null)
                return string.Empty;
            return Convert.ToString(row[key]);
        }

        private static bool GetDictBool(Dictionary<string, object> row, string key)
        {
            if (row == null || !row.ContainsKey(key) || row[key] == null)
                return false;
            var value = row[key];
            if (value is bool b) return b;
            bool parsed;
            return bool.TryParse(Convert.ToString(value), out parsed) && parsed;
        }
    }
}
