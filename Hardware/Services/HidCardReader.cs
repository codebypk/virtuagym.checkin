using Hardware.Events;
using Hardware.Interfaces;
using Hardware.Models;
using HidSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Hardware.Services
{
    /// <summary>
    /// HID USB RFID reader.
    /// Reads RFID tags and raises the <see cref="CardRead"/> event.
    /// Uses HidSharp instead of HidLibrary.
    /// </summary>
    public sealed class HidCardReader : IDisposable
    {
        private readonly string _mappingUuid;
        private readonly IHardwareLogger _logger;
        private readonly string _deviceID;
        private readonly string _name;
        private readonly HidProfile _profile;
        private readonly int _repeatTime;
        private readonly double _duplicateTimeoutSeconds;
        private readonly bool _debugMode;

        private HidDevice? _device;
        private HidStream? _stream;

        private string _lastRfidTag = string.Empty;
        private DateTime _lastRfidTagTime = DateTime.MinValue;

        private TimeSpan DuplicateTimeout =>
            TimeSpan.FromSeconds(Math.Max(_duplicateTimeoutSeconds, 0.05));

        private CancellationTokenSource? _cancellationTokenSource;
        private readonly object _cardProcessLock = new();
        private readonly object _streamLock = new();

        private volatile bool _readInProgress;
        private bool _disposed;

        // Pre-allocated report buffers – created once when the device opens, reused every poll.
        // Avoids per-cycle heap allocation in the hot read path.
        private byte[]? _outputReportBuffer;
        private byte[]? _inputReportBuffer;

        /// <summary>
        /// Mapping id used when the reader was created.
        /// </summary>
        public string MappingUuid => _mappingUuid;

        /// <summary>
        /// Indicates whether a HID stream is currently open.
        /// </summary>
        public bool IsConnected
        {
            get
            {
                lock (_streamLock)
                {
                    return _stream != null && _device != null;
                }
            }
        }

        /// <summary>
        /// Raised when a valid RFID tag was read after duplicate filtering.
        /// The event is raised on a background thread.
        /// </summary>
        public event EventHandler<CardReadEventArgs>? CardRead;

        /// <summary>
        /// Raised when the physical device connection state changes.
        /// Parameter: true = connected, false = disconnected.
        /// </summary>
        public event Action<bool>? DeviceConnectionChanged;

        /// <summary>
        /// Fixed hardware read timeout per HID report.
        /// Kept intentionally short so the loop detects a missing card quickly
        /// and moves on to the idle delay without blocking.
        /// </summary>
        private const int ReadTimeoutMs = 50;

        /// <summary>
        /// Idle delay between poll cycles when no card is present.
        /// Corresponds to the RepeatTimeMs setting (default 100 ms).
        /// When a card IS read this delay is skipped so the event fires immediately.
        /// </summary>
        private int IdleDelayMs => _repeatTime > 0 ? _repeatTime : 100;

        /// <summary>
        /// Creates a new HID card reader.
        /// </summary>
        public HidCardReader(string mappingUuid,IHardwareLogger logger,string deviceID,string hidProfileId,int repeatTimeInMs,string name = null,double duplicateTimeoutSeconds = 5,bool debugMode = false)
        {
            ArgumentNullException.ThrowIfNull(logger);
            ArgumentNullException.ThrowIfNull(deviceID);

            _mappingUuid = mappingUuid;
            _logger = logger;
            _deviceID = deviceID;
            _name = name;
            _repeatTime = repeatTimeInMs > 0 ? repeatTimeInMs : 100;
            _duplicateTimeoutSeconds = duplicateTimeoutSeconds;
            _debugMode = debugMode;
            _profile = HidProfileLoader.GetById(hidProfileId);

            StartReadingAsync();
        }

        private HidDevice? FindDevice()
        {
            string escapedPattern = Regex.Escape(_deviceID).Replace(Regex.Escape("&8"), "[#&]8");

            foreach (var device in DeviceList.Local.GetHidDevices())
            {
                string devicePath = device.DevicePath ?? string.Empty;

                if (Regex.IsMatch(devicePath, escapedPattern, RegexOptions.IgnoreCase))
                {
                    if (_debugMode)
                        _logger.Log($"USB Reader '{devicePath}' found.");

                    return device;
                }
            }

            return null;
        }

        private bool TryOpenDevice()
        {
            try
            {
                var foundDevice = FindDevice();
                if (foundDevice == null)
                    return false;

                if (!foundDevice.TryOpen(out HidStream stream))
                    return false;

                stream.ReadTimeout = ReadTimeoutMs;   // 50 ms – fast "no card" detection
                stream.WriteTimeout = 500;

                // Build the report buffers once here, sized for this specific device.
                int outLen = Math.Max(foundDevice.GetMaxOutputReportLength(), _profile.ReadCommandBytes.Length);
                if (outLen <= 0) outLen = _profile.ReadCommandBytes.Length;
                byte[] outputBuf = new byte[outLen];
                Buffer.BlockCopy(_profile.ReadCommandBytes, 0, outputBuf, 0, Math.Min(_profile.ReadCommandBytes.Length, outLen));

                byte[] inputBuf = new byte[Math.Max(foundDevice.GetMaxInputReportLength(), 64)];

                lock (_streamLock)
                {
                    CloseStreamNoLock();
                    _device = foundDevice;
                    _stream = stream;
                    _outputReportBuffer = outputBuf;
                    _inputReportBuffer = inputBuf;
                }

                _lastRfidTag = string.Empty;

                return true;
            }
            catch (Exception ex)
            {
                if (_debugMode)
                    _logger.Log($"USB Reader open failed: {ex.Message}", HardwareLogLevel.Warning);

                return false;
            }
        }

        private void StartReadingAsync()
        {
            StopReading();

            _cancellationTokenSource = new CancellationTokenSource();

            Task.Run(() => ReadLoopAsync(_cancellationTokenSource.Token))
                .ContinueWith(t =>
                {
                    if (t.IsFaulted)
                        _logger.Log($"HID ReadLoop fatal error: {t.Exception?.InnerException?.Message}", HardwareLogLevel.Error);
                }, TaskContinuationOptions.OnlyOnFaulted);

            if (_debugMode)
                _logger.Log($"USB Reader '{_name ?? _deviceID}' waiting for card ...");
        }

        private async Task ReadLoopAsync(CancellationToken cancellationToken)
        {
            bool wasConnected = false;

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    if (!IsConnected)
                    {
                        if (wasConnected)
                        {
                            wasConnected = false;
                            _logger.Log($"USB Reader '{_name ?? _deviceID}' disconnected.", HardwareLogLevel.Warning);
                            DeviceConnectionChanged?.Invoke(false);
                        }

                        if (!TryOpenDevice())
                        {
                            await Task.Delay(500, cancellationToken).ConfigureAwait(false);
                            continue;
                        }

                        wasConnected = true;
                        _logger.Log($"USB Reader '{_name ?? _deviceID}' connected.", HardwareLogLevel.Success);
                        DeviceConnectionChanged?.Invoke(true);
                    }

                    bool success = TryReadOnce(out byte[]? data);
                    bool validCardPresent = false;

                    if (success && data is { Length: > 0 })
                    {
                        // Card present – event fires synchronously inside ProcessCardData.
                        validCardPresent = ProcessCardData(data);
                    }

                    if(!validCardPresent)
                    {
                        // No valid card data in this cycle, release card
                        MarkCardAsRemovedIfExpired();
                    }

                    // Always throttle – regardless of whether a card was read or not.
                    // This prevents busy-spinning while a card is held on the reader
                    // and reduces USB write frequency to a safe level for all HID devices.
                    await Task.Delay(IdleDelayMs, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.Log($"Error in HID ReadLoopAsync: {ex.Message}", HardwareLogLevel.Error);

                    MarkDisconnected();

                    if (wasConnected)
                    {
                        wasConnected = false;
                        DeviceConnectionChanged?.Invoke(false);
                    }

                    await Task.Delay(500, cancellationToken).ConfigureAwait(false);
                }
            }
        }

        private bool TryReadOnce(out byte[]? data)
        {
            data = null;

            HidStream? stream;
            byte[]? outputReport;
            byte[]? inputReport;

            lock (_streamLock)
            {
                stream       = _stream;
                outputReport = _outputReportBuffer;
                inputReport  = _inputReportBuffer;
            }

            if (stream == null || outputReport == null || inputReport == null)
                return false;

            try
            {
                stream.Write(outputReport);

                int bytesRead = stream.Read(inputReport);

                if (bytesRead <= 0)
                    return false;

                // Return a slice of the pre-allocated buffer – no new allocation.
                // Callers must not hold onto this array across loop iterations.
                data = bytesRead == inputReport.Length
                    ? inputReport
                    : inputReport[..bytesRead];   // stack-friendly range slice

                return true;
            }
            catch (TimeoutException)
            {
                // No card present. This is normal.
                return false;
            }
            catch (IOException)
            {
                MarkDisconnected();
                return false;
            }
            catch (InvalidOperationException)
            {
                MarkDisconnected();
                return false;
            }
        }

        private static byte[] BuildOutputReport(HidDevice device, byte[] command)
        {
            int reportLength = Math.Max(device.GetMaxOutputReportLength(), command.Length);

            if (reportLength <= 0)
                reportLength = command.Length;

            byte[] report = new byte[reportLength];

            int copyLength = Math.Min(command.Length, report.Length);
            Buffer.BlockCopy(command, 0, report, 0, copyLength);

            return report;
        }

        private bool ProcessCardData(byte[] data)
        {
            try
            {
                Card myCard = new(data);

                if (!myCard.IsValidTag)
                    return false;

                lock (_cardProcessLock)
                {
                    var now = DateTime.UtcNow;

                    // Same tag still present: keep alive timestamp and suppress duplicate event.
                    if (myCard.UidHex == _lastRfidTag)
                    {
                        _lastRfidTagTime = now;
                        return true;
                    }

                    if (_readInProgress)
                        return true;

                    _lastRfidTag = myCard.UidHex;
                    _lastRfidTagTime = now;
                    _readInProgress = true;
                }

                try
                {
                    string devicePath = _device?.DevicePath ?? _deviceID;

                    _logger.Log(
                        $"USB Reader: {devicePath} read card with tag id '{myCard.UidDecimal}'",
                        HardwareLogLevel.Success);

                    string sourceName = !string.IsNullOrWhiteSpace(_name) ? _name : _deviceID;
                    CardRead?.Invoke(this, new CardReadEventArgs(myCard, sourceName));

                    return true;
                }
                finally
                {
                    _readInProgress = false;
                }
            }
            catch (Exception ex)
            {
                _readInProgress = false;

                if (_debugMode)
                    _logger.Log($"Error processing HID card data: {ex.Message}", HardwareLogLevel.Error);

                return false;
            }
        }

        private void MarkCardAsRemovedIfExpired()
        {
            lock (_cardProcessLock)
            {
                if (string.IsNullOrEmpty(_lastRfidTag))
                    return;

                if ((DateTime.UtcNow - _lastRfidTagTime) >= DuplicateTimeout)
                {
                    _lastRfidTag = string.Empty;
                    _lastRfidTagTime = DateTime.MinValue;
                }
            }
        }

        private void MarkDisconnected()
        {
            lock (_streamLock)
            {
                CloseStreamNoLock();
                _device = null;
                _outputReportBuffer = null;
                _inputReportBuffer = null;
            }

            lock (_cardProcessLock)
            {
                _lastRfidTag = string.Empty;
                _lastRfidTagTime = DateTime.MinValue;
            }
        }

        private void CloseStreamNoLock()
        {
            try { _stream?.Dispose(); }
            catch { }

            _stream = null;
        }

        private void StopReading()
        {
            _cancellationTokenSource?.Cancel();
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;
        }

        /// <summary>
        /// Simulates reading a card.
        /// </summary>
        public void SimulateCardRead(string cardId)
        {
            _logger.Log($"SIMULATION (HID): Card '{cardId}'", HardwareLogLevel.Warning);

            var card = new Card(cardId);
            string sourceName = !string.IsNullOrWhiteSpace(_name) ? _name : _deviceID;

            CardRead?.Invoke(this, new CardReadEventArgs(card, $"SIM {sourceName}"));
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;

            StopReading();

            lock (_streamLock)
            {
                CloseStreamNoLock();
                _device = null;
            }

            GC.SuppressFinalize(this);
        }
    }

    public sealed class HidSharpDeviceService : IHidDeviceService
    {
        public IReadOnlyList<HidDeviceInfo> GetDevices()
        {
            return DeviceList.Local
                .GetHidDevices()
                .Select(ToInfo)
                .OrderBy(x => x.ProductName)
                .ThenBy(x => x.DevicePath)
                .ToList();
        }

        public HidDeviceInfo? FindByDevicePath(string devicePath)
        {
            if (string.IsNullOrWhiteSpace(devicePath))
                return null;

            return DeviceList.Local
                .GetHidDevices()
                .Where(d => string.Equals(
                    d.DevicePath,
                    devicePath,
                    StringComparison.OrdinalIgnoreCase))
                .Select(ToInfo)
                .FirstOrDefault();
        }

        public bool DeviceExists(string devicePath)
        {
            return FindByDevicePath(devicePath) != null;
        }

        public bool SendCommand(string devicePath, byte[] command)
        {
            if (string.IsNullOrWhiteSpace(devicePath))
                return false;

            if (command == null || command.Length == 0)
                return false;

            var device = DeviceList.Local
                .GetHidDevices()
                .FirstOrDefault(d => string.Equals(
                    d.DevicePath,
                    devicePath,
                    StringComparison.OrdinalIgnoreCase));

            if (device == null)
                return false;

            if (!device.TryOpen(out HidStream stream))
                return false;

            using (stream)
            {
                stream.WriteTimeout = 500;

                byte[] report = BuildOutputReport(device, command);
                stream.Write(report);

                return true;
            }
        }

        private static HidDeviceInfo ToInfo(HidDevice device)
        {
            string manufacturer = string.Empty;
            string productName = string.Empty;
            string serialNumber = string.Empty;

            try
            {
                manufacturer = device.GetManufacturer() ?? string.Empty;
            }
            catch
            {
                // Some devices do not expose this descriptor.
            }

            try
            {
                productName = device.GetProductName() ?? string.Empty;
            }
            catch
            {
                // Some devices do not expose this descriptor.
            }

            try
            {
                serialNumber = device.GetSerialNumber() ?? string.Empty;
            }
            catch
            {
                // Some devices do not expose this descriptor.
            }

            return new HidDeviceInfo
            {
                DevicePath = device.DevicePath ?? string.Empty,
                VendorId = device.VendorID,
                ProductId = device.ProductID,
                Manufacturer = manufacturer,
                ProductName = productName,
                SerialNumber = serialNumber
            };
        }

        private static byte[] BuildOutputReport(HidDevice device, byte[] command)
        {
            int reportLength = Math.Max(device.GetMaxOutputReportLength(), command.Length);

            if (reportLength <= 0)
                reportLength = command.Length;

            byte[] report = new byte[reportLength];

            Buffer.BlockCopy(
                command,
                0,
                report,
                0,
                Math.Min(command.Length, report.Length));

            return report;
        }
    }

}