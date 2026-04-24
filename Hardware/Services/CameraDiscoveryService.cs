using Hardware.Models;
using System;
using System.Collections.Generic;
using System.Management;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Hardware.Services
{
    /// <summary>
    /// Erkennt angeschlossene Kameras per WMI und cached das Ergebnis.
    /// Kein OpenCV-Probing – funktioniert auch wenn Kameras bereits belegt sind.
    /// </summary>
    public static class CameraDiscoveryService
    {
        private static readonly SemaphoreSlim _lock = new(1, 1);
        private static List<CameraDeviceInfo> _cache;

        /// <summary>
        /// Regex-Pattern für Kameranamen die ignoriert werden sollen.
        /// Erfasst IR-Kameras (Windows Hello), Depth-Kameras (3D-Sensor) und ToF-Kameras.
        /// </summary>
        private static readonly Regex ExcludePattern = new(
            @"\b(IR|Infrared|Depth|ToF|Windows Hello)\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Gibt die erkannten Kameras zurück (cached).
        /// </summary>
        public static async Task<IReadOnlyList<CameraDeviceInfo>> GetCamerasAsync()
        {
            if (_cache != null)
                return _cache;

            await _lock.WaitAsync().ConfigureAwait(false);
            try
            {
                if (_cache != null)
                    return _cache;

                _cache = await Task.Run(DiscoverCameras).ConfigureAwait(false);
                return _cache;
            }
            finally
            {
                _lock.Release();
            }
        }

        /// <summary>
        /// Cache leeren – beim nächsten Aufruf wird neu erkannt.
        /// </summary>
        public static void InvalidateCache()
        {
            _lock.Wait();
            try { _cache = null; }
            finally { _lock.Release(); }
        }

        private static List<CameraDeviceInfo> DiscoverCameras()
        {
            var result = new List<CameraDeviceInfo>();

            if (!OperatingSystem.IsWindows())
                return result;

            try
            {
                using var searcher = new ManagementObjectSearcher(
                    "SELECT Name, Manufacturer,PNPDeviceID, Status FROM Win32_PnPEntity WHERE PNPClass = 'Camera'");

                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                int index = 0;
                foreach (ManagementObject obj in searcher.Get())
                {
                    string name = obj["Name"]?.ToString() ?? "";
                    string manufacturer = obj["Manufacturer"]?.ToString() ?? "";
                    string pnpDeviceId = obj["PNPDeviceID"]?.ToString() ?? "";
                    string status = obj["Status"]?.ToString() ?? "";

                    if (string.IsNullOrWhiteSpace(name))
                        continue;

                    string dedupeKey = GetDeviceBaseId(pnpDeviceId) ?? name.Trim();
                    if (!seen.Add(dedupeKey))
                        continue;

                    // True wenn die Kamera gefiltert wurde (IR, Windows Hello, Depth, ToF) – nicht für Video geeignet.
                    bool excluded = ExcludePattern.IsMatch(name);
                    if (excluded)
                        continue;

                    result.Add(new CameraDeviceInfo
                    {
                        Index = index,
                        Name = name.Trim(),
                        Manufacturer = manufacturer.Trim(),
                        Status = status.Trim()
                    });
                    index++;
                }
            }
            catch
            {
                // WMI nicht verfügbar
            }

            return result;
        }

        private static string GetDeviceBaseId(string pnpDeviceId)
        {
            if (string.IsNullOrWhiteSpace(pnpDeviceId))
                return null;
            int lastSep = pnpDeviceId.LastIndexOf('\\');
            return lastSep > 0 ? pnpDeviceId[..lastSep] : pnpDeviceId;
        }
    }
}
