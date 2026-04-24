using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using Hardware.Models;

namespace Hardware.Services
{
    /// <summary>
    /// Lädt RFID-Reader-Profile aus einer JSON-Datei.
    /// Fällt auf ein integriertes Fallback-Profil zurück, wenn die Datei nicht gefunden wird.
    /// </summary>
    public static class HidProfileLoader
    {
        private static string _profilesPath;
        private static List<HidProfile> _cache;

        /// <summary>
        /// Setzt den Pfad zur hid_profiles.json. Muss vor dem ersten Zugriff aufgerufen werden,
        /// falls der Standardpfad nicht passt.
        /// </summary>
        public static void Initialize(string profilesFilePath)
        {
            _profilesPath = profilesFilePath;
            _cache = null;
        }

        private static string GetProfilesPath()
        {
            if (!string.IsNullOrEmpty(_profilesPath))
                return _profilesPath;
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "resources\\data\\hid_profiles.json");
        }

        /// <summary>Lädt alle verfügbaren Profile (mit Cache). Gibt mindestens das Fallback-Profil zurück.</summary>
        public static List<HidProfile> Load()
        {
            if (_cache != null) return _cache;

            try
            {
                string path = GetProfilesPath();
                if (File.Exists(path))
                {
                    string json = File.ReadAllText(path, Encoding.UTF8);
                    _cache = JsonSerializer.Deserialize<List<HidProfile>>(json, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });
                }
            }
            catch { /* File missing or corrupted → fallback profile will be used */ }

            if (_cache == null || _cache.Count == 0)
                _cache = new List<HidProfile> { CreateFallbackProfile() };

            return _cache;
        }

        /// <summary>Gibt das Profil mit der angegebenen ID zurück, oder das Standardprofil.</summary>
        public static HidProfile GetById(string profileId)
        {
            if (string.IsNullOrWhiteSpace(profileId)) return GetDefault();
            return Load().Find(p => p.ProfileId == profileId) ?? GetDefault();
        }

        /// <summary>Gibt das erste verfügbare Profil zurück.</summary>
        public static HidProfile GetDefault()
        {
            var profiles = Load();
            return profiles.Count > 0 ? profiles[0] : CreateFallbackProfile();
        }

        /// <summary>Integriertes Fallback-Profil (wCopy NS122), falls rfid_profiles.json fehlt.</summary>
        private static HidProfile CreateFallbackProfile() => new HidProfile
        {
            ProfileId = "wCopy_NS122",
            DisplayName = "wCopy NS122 (USB HID)",
            ReadCommand = "01 01 13 34 00 FF 00 65 05 1E 48 E8 01 00 81 01 18 01 64 FE",
            BeepCommand = "01 01 0F 36 00 FF 00 40 50 04 05 01 01 01 1E FE",
        };
    }
}
