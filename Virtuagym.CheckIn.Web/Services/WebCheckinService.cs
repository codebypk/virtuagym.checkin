namespace Virtuagym.CheckIn.Web.Services;

/// <summary>
/// Contains the result information for a check-in event, used to update the Welcome page.
/// </summary>
public sealed class CheckinResultInfo
{
    public string MemberName { get; init; } = "";
    public string[] StatusLines { get; init; } = [];
    public string Status { get; init; } = "";
    public string? AvatarUrl { get; init; }
    public string? ReaderName { get; init; }
    /// <summary>Relative path to the sound file to play in the browser (e.g. "sounds/login_succes.wav").</summary>
    public string? SoundPath { get; init; }
}

/// <summary>
/// Contains camera preview frame data for the Welcome page QR camera preview.
/// </summary>
public sealed class CameraPreviewInfo
{
    public byte[] FrameData { get; init; } = [];
    public string CameraLabel { get; init; } = "";
}

/// <summary>
/// Contains device availability information for the Welcome page.
/// </summary>
public sealed class DeviceAvailabilityInfo
{
    public string DeviceName { get; init; } = "";
    public string InputType { get; init; } = "";
    public bool IsAvailable { get; init; }
}

/// <summary>
/// Server-side check-in service that coordinates hardware events with the web UI.
/// Raises events consumed by the Welcome page and Log page.
/// </summary>
public sealed class WebCheckinService
{
    private readonly WebLogService _logService;
    private readonly WebVirtuagymApiServiceFactory _apiFactory;
    private readonly object _deviceAvailabilityLock = new();
    private readonly Dictionary<string, DeviceAvailabilityInfo> _deviceAvailability = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Raised to show the loading indicator on the Welcome page.
    /// Parameter: ReaderName of the device that triggered the loader.
    /// </summary>
    public event Action<string?>? OnShowLoader;

    /// <summary>
    /// Raised when a check-in result is available to display on the Welcome page.
    /// </summary>
    public event Action<CheckinResultInfo>? OnCheckinResult;

    /// <summary>
    /// Raised when a QR camera preview frame is available.
    /// </summary>
    public event Action<CameraPreviewInfo>? OnCameraPreview;

    /// <summary>
    /// Raised when device availability changes (hotplug).
    /// </summary>
    public event Action<DeviceAvailabilityInfo>? OnDeviceAvailabilityChanged;

    /// <summary>
    /// Raised when a hardware trigger (relay / PG gate) failed to open the door.
    /// Parameter: localized error message.
    /// </summary>
    public event Action<string>? OnHardwareError;

    public WebCheckinService(WebLogService logService, WebVirtuagymApiServiceFactory apiFactory)
    {
        _logService = logService;
        _apiFactory = apiFactory;
    }

    /// <summary>Raises <see cref="OnShowLoader"/>.</summary>
    public void RaiseShowLoader(string? readerName = null) => OnShowLoader?.Invoke(readerName);

    /// <summary>Raises <see cref="OnCheckinResult"/>.</summary>
    public void RaiseCheckinResult(CheckinResultInfo info) => OnCheckinResult?.Invoke(info);

    /// <summary>Raises <see cref="OnCameraPreview"/>.</summary>
    public void RaiseCameraPreview(CameraPreviewInfo info) => OnCameraPreview?.Invoke(info);

    /// <summary>Raises <see cref="OnHardwareError"/>.</summary>
    public void RaiseHardwareError(string message) => OnHardwareError?.Invoke(message);

    /// <summary>Raises <see cref="OnDeviceAvailabilityChanged"/>.</summary>
    public void RaiseDeviceAvailabilityChanged(DeviceAvailabilityInfo info)
    {
        if (!string.IsNullOrWhiteSpace(info.DeviceName))
        {
            lock (_deviceAvailabilityLock)
            {
                _deviceAvailability[info.DeviceName] = info;
            }
        }

        OnDeviceAvailabilityChanged?.Invoke(info);
    }

    /// <summary>Returns the current device availability snapshot.</summary>
    public IReadOnlyList<DeviceAvailabilityInfo> GetDeviceAvailabilitySnapshot()
    {
        lock (_deviceAvailabilityLock)
        {
            return _deviceAvailability.Values.ToList();
        }
    }

    /// <summary>
    /// Simulates a check-in for testing/developer purposes.
    /// </summary>
    public async Task SimulateCheckinAsync(string input)
    {
        OnShowLoader?.Invoke(null);

        _logService.WriteToLog($"Simulation check-in: {input}", Virtuagym.CheckIn.Core.Helper.Constants.LogInfo);

        // Simulate processing delay
        await Task.Delay(1000);

        OnCheckinResult?.Invoke(new CheckinResultInfo
        {
            MemberName = "Simulated Member",
            StatusLines = new[] { "Simulation completed" },
            Status = "ok"
        });

        _logService.WriteToLog("Simulation check-in completed.", Virtuagym.CheckIn.Core.Helper.Constants.LogSuccess);
    }
}
