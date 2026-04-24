using Hardware.Events;
using Hardware.Interfaces;
using Hardware.Models;
using HidLibrary;
using System;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Hardware.Services
{
    /// <summary>
    /// HID USB RFID-Reader.
    /// Liest RFID-Tags und feuert das <see cref="CardRead"/>-Event.
    /// Komplett unabhängig von UI, Business-Logik und Checkin-Handler.
    /// </summary>
    public class HidCardReader : IDisposable
    {
        enum Command
        {
            ReadTag,
            Beep
        }

        private readonly IHardwareLogger _logger;
        private readonly string _deviceID;
        private readonly string _name;
        private readonly HidProfile _profile;
        private readonly int _repeatTime;
        private readonly double _duplicateTimeoutSeconds;
        private readonly bool _debugMode;

        private HidDevice _myDevice;
        private bool _deviceAttached;
        private bool _deviceRemoved = false;

        private string _lastRfidTag = String.Empty;
        private DateTime _lastRfidTagTime = DateTime.MinValue;

        private CancellationTokenSource _cancellationTokenSource;
        private readonly object _cardProcessLock = new object();
        private bool _readInProgress;

        /// <summary>
        /// Wird ausgelöst wenn ein gültiger RFID-Tag gelesen wurde (nach Duplikat-Prüfung).
        /// Achtung: Event wird auf einem Hintergrund-Thread gefeuert.
        /// </summary>
        public event EventHandler<CardReadEventArgs> CardRead;

        /// <summary>
        /// Maximaler Read-Timeout in Millisekunden.
        /// </summary>
        private const int ReadTimeoutMs = 5000;

        /// <summary>
        /// Minimale Pause nach einer erfolgreichen Kartenlesung.
        /// </summary>
        private const int PostReadDelayMs = 300;

        /// <summary>
        /// Erstellt einen neuen HID Card Reader.
        /// </summary>
        /// <param name="logger">Logger für Log-Ausgaben.</param>
        /// <param name="deviceID">HID Device-ID (Teilstring des DevicePath).</param>
        /// <param name="hidProfileId">ID des HID-Profils (Lese-/Beep-Befehle).</param>
        /// <param name="repeatTimeInMs">Wiederholungszeit für Read-Befehle in ms.</param>
        /// <param name="name">Optionaler Anzeigename für Log-Meldungen.</param>
        /// <param name="duplicateTimeoutSeconds">Zeitspanne in Sekunden, in der derselbe Tag ignoriert wird.</param>
        /// <param name="debugMode">Erweiterte Log-Ausgaben aktivieren.</param>
        public HidCardReader(IHardwareLogger logger, string deviceID, string hidProfileId, int repeatTimeInMs,
            string name = null, double duplicateTimeoutSeconds = 5, bool debugMode = false)
        {
            ArgumentNullException.ThrowIfNull(logger);
            ArgumentNullException.ThrowIfNull(deviceID);
            _logger = logger;
            _deviceID = deviceID;
            _name = name;
            _repeatTime = repeatTimeInMs;
            _duplicateTimeoutSeconds = duplicateTimeoutSeconds;
            _debugMode = debugMode;
            _profile = HidProfileLoader.GetById(hidProfileId);

            try
            {
                ConnectToDevice();
            }
            catch (Exception ex)
            {
                _logger.Log($"Error on Init HidCardReader() {ex.Message}", HardwareLogLevel.Error);
            }
        }

        private void ConnectToDevice()
        {
            try
            {
                var devList = HidDevices.Enumerate();
                foreach (var item in devList)
                {
                    string escapedPattern = Regex.Escape(_deviceID).Replace(Regex.Escape("&8"), "[#&]8");
                    bool contains = Regex.IsMatch(item.DevicePath, escapedPattern, RegexOptions.IgnoreCase);
                    if (contains)
                    {
                        if (_debugMode)
                            _logger.Log("USB Reader '" + item.DevicePath + "' found!");

                        _myDevice = item;
                        break;
                    }
                }

                if (_myDevice == null)
                {
                    _logger.Log("USB Reader '" + _deviceID + "' not found!", HardwareLogLevel.Error);
                    return;
                }

                _myDevice.OpenDevice();
                _myDevice.Inserted += _myDevice_Inserted;
                _myDevice.Removed += _myDevice_Removed;
                //_myDevice.MonitorDeviceEvents = true;

                if (_debugMode)
                    _logger.Log("USB Reader " + _deviceID + " connected!", HardwareLogLevel.Success);

                StartReadingAsync();
            }
            catch (Exception ex)
            {
                _logger.Log("Error on ConnectToDevice() " + ex.Message, HardwareLogLevel.Error);
            }
        }

        #region Async Reading Loop

        private void StartReadingAsync()
        {
            StopReading();

            _cancellationTokenSource = new CancellationTokenSource();
            _deviceAttached = true;

            Task.Run(() => ReadLoopAsync(_cancellationTokenSource.Token))
                .ContinueWith(t =>
                {
                    if (t.IsFaulted)
                        _logger.Log($"ReadLoop fatal error: {t.Exception?.InnerException?.Message}", HardwareLogLevel.Error);
                }, TaskContinuationOptions.OnlyOnFaulted);

            if (_debugMode)
                _logger.Log("USB Reader: " + _myDevice.DevicePath + " wait for card ...");
        }

        private async Task ReadLoopAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested && _deviceAttached)
            {
                try
                {
                    if (_myDevice == null || !_myDevice.IsConnected)
                    {
                        await Task.Delay(1000, cancellationToken);
                        continue;
                    }

                    bool writeSuccess = _myDevice.Write(_profile.ReadCommandBytes);

                    if (!writeSuccess)
                    {
                        await Task.Delay(500, cancellationToken);
                        continue;
                    }

                    HidDeviceData report = await Task.Run(() => _myDevice.Read(ReadTimeoutMs), cancellationToken);

                    if (report.Status == HidDeviceData.ReadStatus.Success)
                    {
                        ProcessCardData(report.Data);
                        await Task.Delay(PostReadDelayMs, cancellationToken);
                    }
                    else if (report.Status == HidDeviceData.ReadStatus.WaitTimedOut)
                    {
                        // Keine Karte vorhanden
                    }
                    else
                    {
                        await Task.Delay(500, cancellationToken);
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.Log("Error in ReadLoopAsync: " + ex.Message, HardwareLogLevel.Error);
                    await Task.Delay(1000, cancellationToken);
                }
            }
        }

        private void ProcessCardData(byte[] data)
        {
            try
            {
                Card myCard = new Card(data);
                if (myCard.IsValidTag)
                {
                    lock (_cardProcessLock)
                    {
                        var duplicateTimeout = TimeSpan.FromSeconds(_duplicateTimeoutSeconds);
                        bool isDuplicate = myCard.UidHex == _lastRfidTag
                            && (DateTime.Now - _lastRfidTagTime) < duplicateTimeout;

                        if (!isDuplicate && !_readInProgress)
                        {
                            _lastRfidTag = myCard.UidHex;
                            _lastRfidTagTime = DateTime.Now;
                            _readInProgress = true;

                            _logger.Log($"USB Reader: {_myDevice.DevicePath} read card with tag id '{myCard.UidDecimal}'", HardwareLogLevel.Success);

                            string sourceName = !string.IsNullOrWhiteSpace(_name) ? _name : _deviceID;

                            try
                            {
                                CardRead?.Invoke(this, new CardReadEventArgs(myCard, sourceName));
                            }
                            finally
                            {
                                _readInProgress = false;
                            }
                        }
                        else
                        {
                            _lastRfidTagTime = DateTime.Now;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _readInProgress = false;
                _logger.Log($"Error processing card data: {ex.Message}", HardwareLogLevel.Error);
            }
        }

        private void StopReading()
        {
            _deviceAttached = false;
            _cancellationTokenSource?.Cancel();
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;
        }

        #endregion

        #region HID Reader Events

        public void _myDevice_Removed()
        {
            _deviceRemoved = true;
            _deviceAttached = false;

            try
            {
                _logger.Log("Card reader disconnected!", HardwareLogLevel.Warning);

                StopReading();

                if (_myDevice != null)
                    _myDevice.CloseDevice();

                _lastRfidTag = String.Empty;
            }
            catch (Exception ex)
            {
                _logger.Log("Error on stop rfid reader! " + ex.Message, HardwareLogLevel.Error);
            }
        }

        private void _myDevice_Inserted()
        {
            if (_deviceRemoved)
            {
                _deviceRemoved = false;
                _logger.Log("Hid card reader connected!", HardwareLogLevel.Success);
            }

            StartReadingAsync();
        }

        #endregion

        /// <summary>
        /// Simuliert das Lesen einer Karte (feuert CardRead-Event).
        /// </summary>
        public void SimulateCardRead(string cardId)
        {
            _logger.Log($"SIMULATION (HID): Card '{cardId}'", HardwareLogLevel.Warning);
            var card = new Card(cardId);
            string sourceName = !string.IsNullOrWhiteSpace(_name) ? _name : _deviceID;
            CardRead?.Invoke(this, new CardReadEventArgs(card, $"SIM {sourceName}"));
        }

        #region IDisposable

        private bool _disposed;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _myDevice_Removed();
        }

        #endregion
    }
}
