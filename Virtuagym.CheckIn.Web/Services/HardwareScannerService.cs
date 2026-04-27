using AccessPass.Services;
using Hardware.Events;
using Hardware.Services;
using Microsoft.Extensions.Options;
using OpenCvSharp;
using Virtuagym.API.Serialization;
using Virtuagym.API.Services;
using Virtuagym.CheckIn.Core.Helper;
using Virtuagym.CheckIn.Core.Models;
using Virtuagym.CheckIn.Core.Services;
using Virtuagym.CheckIn.Web.Models;

namespace Virtuagym.CheckIn.Web.Services;

/// <summary>
/// Hosted service that initializes hardware scanners/readers at server startup,
/// mirroring the WPF MainWindow scanner initialization.
/// Runs independently of any browser connection.
/// </summary>
public sealed class HardwareScannerService : IHostedService, IDisposable
{
    private readonly IOptionsMonitor<AppSettings> _optionsMonitor;
    private readonly WebCheckinService _checkinService;
    private readonly WebLogService _log;
    private readonly WebVirtuagymApiServiceFactory _apiFactory;
    private readonly IAccessPassService _accessPassService;

    private readonly List<QrCodeScanner> _qrScanners = [];
    private readonly List<HidCardReader> _hidReaders = [];
    private readonly List<CcidSmartCardReader> _ccidReaders = [];
    // Track active mappings for diff-based hotplug
    private List<CheckinClientMapping> _activeMappings = new();
    // Track physical device availability state (deviceName -> isAvailable)
    private readonly Dictionary<string, bool> _deviceAvailabilityState = new();
    private MemberCacheService? _memberCache;
    private System.Threading.Timer? _hotplugTimer;
    private int _lastActiveDeviceCount;
    private volatile bool _disposed;

    /// <summary>
    /// Fired when any card reader reads a card. Can be used by UI components to capture card IDs.
    /// </summary>
    public event Action<string>? OnCardScanned;

    public HardwareScannerService(
        IOptionsMonitor<AppSettings> optionsMonitor,
        WebCheckinService checkinService,
        WebLogService log,
        WebVirtuagymApiServiceFactory apiFactory,
        IAccessPassService accessPassService)
    {
        _optionsMonitor = optionsMonitor;
        _checkinService = checkinService;
        _log = log;
        _apiFactory = apiFactory;
        _accessPassService = accessPassService;
    }

    private AppSettings Settings => _optionsMonitor.CurrentValue;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        InitializeMemberCache();
        StartScanners();
        StartHotplugMonitor();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Stops all running scanners/readers and re-initializes them from the current settings.
    /// Call this after mappings have been saved.
    /// </summary>
    public void Reload()
    {
        _log.WriteToLog("Hardware-Scanner werden neu geladen...", Constants.LogInfo);
        DisposeHardware();
        StartScanners();
    }

    private void StartHotplugMonitor()
    {
        var seconds = Settings.DeviceHotplugPollIntervalSeconds;
        if (seconds <= 0)
            return;

        seconds = Math.Clamp(seconds, 5, 300);

        _hotplugTimer?.Dispose();
        _hotplugTimer = new System.Threading.Timer(_ =>
        {
            if (_disposed) return;
            try
            {
                CameraDiscoveryService.InvalidateCache();
                var settings = Settings;
                var newMappings = LoadMappings(settings) ?? new List<CheckinClientMapping>();

                // Only consider mappings with a CheckinKey
                var filteredNew = newMappings.Where(m => !string.IsNullOrWhiteSpace(m.CheckinKey)).ToList();
                var filteredOld = _activeMappings.Where(m => !string.IsNullOrWhiteSpace(m.CheckinKey)).ToList();

                // Find removed and added mappings by DeviceID/CameraIndex/InputType
                var removed = filteredOld.Where(old => !filteredNew.Any(n => MappingEquals(n, old))).ToList();
                var added = filteredNew.Where(n => !filteredOld.Any(old => MappingEquals(n, old))).ToList();

                // Stop removed devices
                foreach (var mapping in removed)
                {
                    StopDevice(mapping);
                    var deviceName = mapping.Name ?? mapping.DeviceID ?? $"Kamera {mapping.CameraIndex}";
                    _log.WriteToLog($"Gerät nicht mehr verfügbar: {deviceName} ({mapping.InputType})", Constants.LogWarning);
                    _checkinService.RaiseDeviceAvailabilityChanged(new DeviceAvailabilityInfo
                    {
                        DeviceName = deviceName,
                        InputType = mapping.InputType,
                        IsAvailable = false
                    });
                }

                // Start added devices
                foreach (var mapping in added)
                {
                    StartDevice(mapping, settings);
                    var deviceName = mapping.Name ?? mapping.DeviceID ?? $"Kamera {mapping.CameraIndex}";
                    _log.WriteToLog($"Gerät verfügbar: {deviceName} ({mapping.InputType})", Constants.LogInfo);
                    _checkinService.RaiseDeviceAvailabilityChanged(new DeviceAvailabilityInfo
                    {
                        DeviceName = deviceName,
                        InputType = mapping.InputType,
                        IsAvailable = true
                    });
                }

                _activeMappings = filteredNew;

                // Check physical availability of active HID/CCID devices
                CheckPhysicalDeviceAvailability();

                if (settings.DebugMode)
                {
                    int total = _qrScanners.Count + _hidReaders.Count + _ccidReaders.Count;
                    if (total != _lastActiveDeviceCount)
                    {
                        _lastActiveDeviceCount = total;
                        _log.WriteToLog($"Hotplug-Update: aktive Geräte {total}", Constants.LogInfo);
                    }
                }
   
            }
            catch (Exception ex)
            {
                _log.WriteToLog($"Hotplug-Monitor Fehler: {ex.Message}", Constants.LogWarning);
            }
        }, null, TimeSpan.FromSeconds(seconds), TimeSpan.FromSeconds(seconds));

        _log.WriteToLog($"Hotplug-Monitor aktiv (Intervall: {seconds}s).", Constants.LogInfo);
    }

    // Helper to compare mappings by unique device identity
    private static bool MappingEquals(CheckinClientMapping a, CheckinClientMapping b)
    {
        if (a.InputType != b.InputType) return false;
        if (a.InputType == CheckinClientMapping.InputTypeQrCode)
            return a.CameraIndex == b.CameraIndex && a.CameraBackend == b.CameraBackend;
        return a.DeviceID == b.DeviceID && a.HidProfile == b.HidProfile;
    }

    /// <summary>
    /// Checks physical availability of active HID/CCID devices and raises availability events on change.
    /// </summary>
    private void CheckPhysicalDeviceAvailability()
    {
        // Check HID readers
        foreach (var reader in _hidReaders.ToList())
        {
            var mapping = _activeMappings.FirstOrDefault(m => m.Uuid == reader.MappingUuid);
            if (mapping == null) continue;

            var deviceName = mapping.Name ?? mapping.DeviceID ?? "HID";
            bool isConnected = reader.IsConnected;
            bool wasAvailable = !_deviceAvailabilityState.TryGetValue(deviceName, out var prev) || prev;

            if (wasAvailable && !isConnected)
            {
                _deviceAvailabilityState[deviceName] = false;
                _log.WriteToLog($"Gerät physisch getrennt: {deviceName} ({mapping.InputType})", Constants.LogWarning);
                _checkinService.RaiseDeviceAvailabilityChanged(new DeviceAvailabilityInfo
                {
                    DeviceName = deviceName,
                    InputType = mapping.InputType,
                    IsAvailable = false
                });
            }
            else if (!wasAvailable && isConnected)
            {
                _deviceAvailabilityState[deviceName] = true;
                _log.WriteToLog($"Gerät wieder verbunden: {deviceName} ({mapping.InputType})", Constants.LogInfo);
                _checkinService.RaiseDeviceAvailabilityChanged(new DeviceAvailabilityInfo
                {
                    DeviceName = deviceName,
                    InputType = mapping.InputType,
                    IsAvailable = true
                });
            }
        }

        // Check CCID readers
        foreach (var reader in _ccidReaders.ToList())
        {
            var mapping = _activeMappings.FirstOrDefault(m => m.Uuid == reader.MappingUuid);
            if (mapping == null) continue;

            var deviceName = mapping.Name ?? mapping.DeviceID ?? "CCID";
            bool isConnected = reader.IsConnected;
            bool wasAvailable = !_deviceAvailabilityState.TryGetValue(deviceName, out var prev) || prev;

            if (wasAvailable && !isConnected)
            {
                _deviceAvailabilityState[deviceName] = false;
                _log.WriteToLog($"Gerät physisch getrennt: {deviceName} ({mapping.InputType})", Constants.LogWarning);
                _checkinService.RaiseDeviceAvailabilityChanged(new DeviceAvailabilityInfo
                {
                    DeviceName = deviceName,
                    InputType = mapping.InputType,
                    IsAvailable = false
                });
            }
            else if (!wasAvailable && isConnected)
            {
                _deviceAvailabilityState[deviceName] = true;
                _log.WriteToLog($"Gerät wieder verbunden: {deviceName} ({mapping.InputType})", Constants.LogInfo);
                _checkinService.RaiseDeviceAvailabilityChanged(new DeviceAvailabilityInfo
                {
                    DeviceName = deviceName,
                    InputType = mapping.InputType,
                    IsAvailable = true
                });
            }
        }
    }

    // Helper to stop a device by mapping
    private void StopDevice(CheckinClientMapping mapping)
    {
        if (mapping.InputType == CheckinClientMapping.InputTypeQrCode)
        {
            var scanner = _qrScanners.FirstOrDefault(s => s.MappingUuid == mapping.Uuid);
            if (scanner != null)
            {
                scanner.Dispose();
                _qrScanners.Remove(scanner);
                _log.WriteToLog($"QR-Scanner gestoppt: {mapping.Name ?? $"Kamera {mapping.CameraIndex}"}", Constants.LogInfo);
            }
        }
        else if (mapping.InputType == CheckinClientMapping.InputTypeCcid)
        {
            var ccid = _ccidReaders.FirstOrDefault(r => r.MappingUuid == mapping.Uuid);
            if (ccid != null)
            {
                ccid.Dispose();
                _ccidReaders.Remove(ccid);
                _log.WriteToLog($"CCID-Reader gestoppt: {mapping.Name ?? mapping.DeviceID}", Constants.LogInfo);
            }
        }
        else
        {
            var reader = _hidReaders.FirstOrDefault(r => r.MappingUuid == mapping.Uuid);
            if (reader != null)
            {
                reader.Dispose();
                _hidReaders.Remove(reader);
                _log.WriteToLog($"USB-Reader gestoppt: {mapping.Name ?? mapping.DeviceID}", Constants.LogInfo);
            }
        }
    }

    // Helper to start a device by mapping
    private void StartDevice(CheckinClientMapping mapping, AppSettings settings)
    {
        var hwLogger = new HardwareLoggerAdapter(_log);
        var welcomeDisplay = new WebWelcomeDisplay(_checkinService);
        var soundPlayer = new ServerSoundPlayer(welcomeDisplay);
        try
        {
            if (mapping.InputType == CheckinClientMapping.InputTypeQrCode)
            {
                if (!Enum.TryParse<VideoCaptureAPIs>(mapping.CameraBackend, out var captureApi))
                    captureApi = VideoCaptureAPIs.ANY;

                var scanner = new QrCodeScanner(mapping.Uuid, hwLogger, mapping.CameraIndex, mapping.Name,
                    settings.DuplicateTimeoutSeconds, settings.DebugMode,
                    mapping.CameraResolutionWidth, mapping.CameraResolutionHeight, captureApi);

                scanner.QrCodeRead += (_, args) => OnQrCodeRead(args, mapping, welcomeDisplay, soundPlayer);
                scanner.CameraPreview += (_, args) => _checkinService.RaiseCameraPreview(
                    new CameraPreviewInfo { FrameData = args.FrameData, CameraLabel = args.CameraLabel });

                _qrScanners.Add(scanner);
                _log.WriteToLog($"QR-Scanner gestartet: {mapping.Name ?? $"Kamera {mapping.CameraIndex}"}", Constants.LogInfo);
            }
            else if (mapping.InputType == CheckinClientMapping.InputTypeCcid)
            {
                if (!string.IsNullOrWhiteSpace(mapping.DeviceID))
                {
                    var ccid = new CcidSmartCardReader(mapping.Uuid, hwLogger, mapping.DeviceID, mapping.Name,
                        settings.DuplicateTimeoutSeconds, settings.DebugMode);
                    ccid.CardRead += (_, args) => OnCardRead(args, mapping, welcomeDisplay, soundPlayer);
                    _ccidReaders.Add(ccid);

                    var ccidDeviceName = mapping.Name ?? mapping.DeviceID ?? "CCID";
                    _checkinService.RaiseDeviceAvailabilityChanged(new DeviceAvailabilityInfo
                    {
                        DeviceName = ccidDeviceName,
                        InputType = mapping.InputType,
                        IsAvailable = ccid.IsConnected
                    });

                    _log.WriteToLog($"CCID-Reader gestartet: {mapping.Name ?? mapping.DeviceID}", Constants.LogInfo);
                }
            }
            else if (!string.IsNullOrWhiteSpace(mapping.DeviceID))
            {
                var reader = new HidCardReader(mapping.Uuid, hwLogger, mapping.DeviceID, mapping.HidProfile,
                    mapping.RepeatTimeMs ?? settings.RepeatTimeInMs, mapping.Name,
                    settings.DuplicateTimeoutSeconds, settings.DebugMode);
                reader.CardRead += (_, args) => OnCardRead(args, mapping, welcomeDisplay, soundPlayer);
                reader.DeviceConnectionChanged += (isConnected) =>
                {
                    var deviceName = mapping.Name ?? mapping.DeviceID ?? "HID";
                    _log.WriteToLog(isConnected
                        ? $"Gerät wieder verbunden: {deviceName} ({mapping.InputType})"
                        : $"Gerät getrennt: {deviceName} ({mapping.InputType})",
                        isConnected ? Constants.LogInfo : Constants.LogWarning);
                    _checkinService.RaiseDeviceAvailabilityChanged(new DeviceAvailabilityInfo
                    {
                        DeviceName = deviceName,
                        InputType = mapping.InputType,
                        IsAvailable = isConnected
                    });
                };
                _hidReaders.Add(reader);

                var hidDeviceName = mapping.Name ?? mapping.DeviceID ?? "HID";
                _checkinService.RaiseDeviceAvailabilityChanged(new DeviceAvailabilityInfo
                {
                    DeviceName = hidDeviceName,
                    InputType = mapping.InputType,
                    IsAvailable = reader.IsConnected
                });

                _log.WriteToLog($"USB-Reader gestartet: {mapping.Name ?? mapping.DeviceID}", Constants.LogInfo);
            }
        }
        catch (Exception ex)
        {
            _log.WriteToLog($"Fehler beim Starten von {mapping.Name ?? mapping.DeviceID ?? "Gerät"}: {ex.Message}", Constants.LogError);
        }
    }

    private void InitializeMemberCache()
    {
        var settings = Settings;
        try
        {
            if (settings.MemberCacheEnabled)
                _memberCache = new MemberCacheService();
        }
        catch (Exception ex)
        {
            _log.WriteToLog($"HardwareScanner Member-Cache Init-Fehler: {ex.Message}", Constants.LogWarning);
        }
    }

    private void StartScanners()
    {
        var settings = Settings;
        var mappings = LoadMappings(settings);
        if (mappings is not { Count: > 0 })
        {
            _log.WriteToLog("Keine Checkin-Client-Mappings konfiguriert – Hardware-Scanner nicht gestartet.", Constants.LogInfo);
            return;
        }

        // Dispose all current devices
        DisposeHardware();

        // Start all devices from mappings
        foreach (var mapping in mappings)
        {
            if (string.IsNullOrWhiteSpace(mapping.CheckinKey))
                continue;
            StartDevice(mapping, settings);
        }

        _activeMappings = mappings.Where(m => !string.IsNullOrWhiteSpace(m.CheckinKey)).ToList();

        int total = _qrScanners.Count + _hidReaders.Count + _ccidReaders.Count;
        _log.WriteToLog($"Hardware-Scanner Service: {total} Gerät(e) aktiv.", Constants.LogInfo);
    }

    private async void OnCardRead(Hardware.Events.CardReadEventArgs e, CheckinClientMapping mapping,
        WebWelcomeDisplay display, ServerSoundPlayer soundPlayer)
    {
        OnCardScanned?.Invoke(e.Card.UidHex);
        try
        {
            display.ShowLoader(e.ReaderName);
            var handler = new CheckinHandler(_log, mapping, display, Settings, soundPlayer, _apiFactory, _memberCache, _accessPassService);
            await handler.PerformCheckinAsync(e.Card, e.ReaderName);
        }
        catch (Exception ex)
        {
            _log.WriteToLog($"Checkin error: {ex.Message}", Constants.LogError);
        }
    }

    private async void OnQrCodeRead(Hardware.Events.QrCodeReadEventArgs e, CheckinClientMapping mapping,
        WebWelcomeDisplay display, ServerSoundPlayer soundPlayer)
    {
        try
        {
            display.ShowLoader(e.ReaderName);
            var handler = new CheckinHandler(_log, mapping, display, Settings, soundPlayer, _apiFactory, _memberCache, _accessPassService);
            await handler.PerformCheckinAsync(e.QrCode, e.ReaderName);
        }
        catch (Exception ex)
        {
            _log.WriteToLog($"QR-Checkin error: {ex.Message}", Constants.LogError);
        }
    }

    /// <summary>
    /// Pauses all QR camera scanners, releasing the physical cameras
    /// so the browser can access them (e.g. for webcam photo capture).
    /// </summary>
    public void PauseAllCameras()
    {
        foreach (var scanner in _qrScanners)
            scanner.Pause();
        _log.WriteToLog("Alle QR-Scanner pausiert (Kamera freigegeben für Browser).", Constants.LogInfo);
    }

    /// <summary>
    /// Resumes all previously paused QR camera scanners.
    /// </summary>
    public void ResumeAllCameras()
    {
        foreach (var scanner in _qrScanners)
            scanner.Resume();
        _log.WriteToLog("Alle QR-Scanner fortgesetzt.", Constants.LogInfo);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _disposed = true;
        _hotplugTimer?.Dispose();
        _hotplugTimer = null;

        // Dispose hardware on a background thread to avoid blocking the host shutdown
        // pipeline (QrCodeScanner.Dispose does Thread.Join which can block for seconds).
        try
        {
            await Task.Run(DisposeHardware).WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
        }
        catch (TimeoutException)
        {
            _log.WriteToLog("Hardware-Dispose Timeout – einige Geräte wurden möglicherweise nicht korrekt gestoppt.", Virtuagym.CheckIn.Core.Helper.Constants.LogWarning);
            return;
        }
        catch (OperationCanceledException)
        {
            return;
        }
    }

    public void Dispose()
    {
        _disposed = true;
        _hotplugTimer?.Dispose();
        _hotplugTimer = null;

        // Never block debugger shutdown on hardware dispose.
        _ = Task.Run(() =>
        {
            try { DisposeHardware(); }
            catch { /* best effort during shutdown */ }
            finally
            {
                try { _memberCache?.Dispose(); }
                catch { /* best effort during shutdown */ }
            }
        });
    }

    private void DisposeHardware()
    {
        foreach (var scanner in _qrScanners)
            scanner.Dispose();
        _qrScanners.Clear();

        foreach (var reader in _hidReaders)
            reader.Dispose();
        _hidReaders.Clear();

        foreach (var ccid in _ccidReaders)
            ccid.Dispose();
        _ccidReaders.Clear();
    }

    private static List<CheckinClientMapping>? LoadMappings(AppSettings settings)
    {
        try
        {
            string json = settings.CheckinClientMappings;
            if (!string.IsNullOrWhiteSpace(json))
            {
                var serializer = new JsonSerializerAdapter();
                return serializer.Deserialize<List<CheckinClientMapping>>(json);
            }
        }
        catch
        {
            // Mappings konnten nicht geladen werden
        }
        return null;
    }
}
