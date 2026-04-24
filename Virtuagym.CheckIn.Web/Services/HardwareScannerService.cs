using AccessPass.Services;
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
    private MemberCacheService? _memberCache;

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

        var hwLogger = new HardwareLoggerAdapter(_log);
        var welcomeDisplay = new WebWelcomeDisplay(_checkinService);
        var soundPlayer = new ServerSoundPlayer(welcomeDisplay);

        foreach (var mapping in mappings)
        {
            if (string.IsNullOrWhiteSpace(mapping.CheckinKey))
                continue;

            try
            {
                if (mapping.InputType == CheckinClientMapping.InputTypeQrCode)
                {
                    if (!Enum.TryParse<VideoCaptureAPIs>(mapping.CameraBackend, out var captureApi))
                        captureApi = VideoCaptureAPIs.ANY;

                    var scanner = new QrCodeScanner(hwLogger, mapping.CameraIndex, mapping.Name,
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
                        var ccid = new CcidSmartCardReader(hwLogger, mapping.DeviceID, mapping.Name,
                            settings.DuplicateTimeoutSeconds, settings.DebugMode);
                        ccid.CardRead += (_, args) => OnCardRead(args, mapping, welcomeDisplay, soundPlayer);
                        _ccidReaders.Add(ccid);
                        _log.WriteToLog($"CCID-Reader gestartet: {mapping.Name ?? mapping.DeviceID}", Constants.LogInfo);
                    }
                }
                else if (!string.IsNullOrWhiteSpace(mapping.DeviceID))
                {
                    var reader = new HidCardReader(hwLogger, mapping.DeviceID, mapping.HidProfile,
                        mapping.RepeatTimeMs ?? settings.RepeatTimeInMs, mapping.Name,
                        settings.DuplicateTimeoutSeconds, settings.DebugMode);
                    reader.CardRead += (_, args) => OnCardRead(args, mapping, welcomeDisplay, soundPlayer);
                    _hidReaders.Add(reader);
                    _log.WriteToLog($"USB-Reader gestartet: {mapping.Name ?? mapping.DeviceID}", Constants.LogInfo);
                }
            }
            catch (Exception ex)
            {
                _log.WriteToLog($"Fehler beim Starten von {mapping.Name ?? mapping.DeviceID ?? "Gerät"}: {ex.Message}", Constants.LogError);
            }
        }

        int total = _qrScanners.Count + _hidReaders.Count + _ccidReaders.Count;
        _log.WriteToLog($"Hardware-Scanner Service: {total} Gerät(e) aktiv.", Constants.LogInfo);
    }

    private async void OnCardRead(Hardware.Events.CardReadEventArgs e, CheckinClientMapping mapping,
        WebWelcomeDisplay display, ServerSoundPlayer soundPlayer)
    {
        try
        {
            display.ShowLoader();
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
            display.ShowLoader();
            var handler = new CheckinHandler(_log, mapping, display, Settings, soundPlayer, _apiFactory, _memberCache, _accessPassService);
            await handler.PerformCheckinAsync(e.QrCode, e.ReaderName);
        }
        catch (Exception ex)
        {
            _log.WriteToLog($"QR-Checkin error: {ex.Message}", Constants.LogError);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        DisposeHardware();
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        DisposeHardware();
        _memberCache?.Dispose();
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
