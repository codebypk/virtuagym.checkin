using Hardware.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
#if WINDOWS
using System.Management;
#endif

namespace Hardware.Services
{
    /// <summary>
    /// Discovers connected cameras and caches the result.
    /// On Windows uses WMI. On Linux uses /dev/video* probing (index-based).
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
            if (OperatingSystem.IsWindows())
                return DiscoverCamerasWindows();

            if (OperatingSystem.IsLinux())
                return DiscoverCamerasLinux();

            return [];
        }

#if WINDOWS
        private static List<CameraDeviceInfo> DiscoverCamerasWindows()
        {
            var result = new List<CameraDeviceInfo>();
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

                    if (ExcludePattern.IsMatch(name))
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
                // WMI not available
            }

            return result;
        }
#else
        private static List<CameraDeviceInfo> DiscoverCamerasWindows() => [];
#endif

        private static List<CameraDeviceInfo> DiscoverCamerasLinux()
        {
            var result = new List<CameraDeviceInfo>();
            try
            {
                // Basic V4L2 heuristic: /dev/video0..N represent capture devices.
                var devFiles = Directory.GetFiles("/dev", "video*");
                Array.Sort(devFiles, StringComparer.OrdinalIgnoreCase);

                foreach (var file in devFiles)
                {
                    // Parse numeric suffix as index if possible (video0 -> 0).
                    var name = Path.GetFileName(file) ?? file;
                    int index = -1;
                    if (name.StartsWith("video", StringComparison.OrdinalIgnoreCase))
                        _ = int.TryParse(name.AsSpan(5), out index);

                    if (index < 0)
                        continue;

                    result.Add(new CameraDeviceInfo
                    {
                        Index = index,
                        Name = file,
                        Manufacturer = "",
                        Status = File.Exists(file) ? "OK" : "Missing"
                    });
                }
            }
            catch
            {
                // /dev not accessible or unsupported
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
