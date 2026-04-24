using Hardware.Interfaces;
using Hardware.Models;
using System;
using System.Collections.Concurrent;
using System.IO.Ports;
using System.Timers;

namespace Hardware.Services
{
    /// <summary>
    /// Steuert ein Relay-Modul über einen seriellen COM-Port.
    /// Schaltet das Relay für eine definierte Dauer ein und dann wieder aus.
    /// </summary>
    public class RelayController : IDisposable
    {
        private SerialPort _serialPort;
        private Timer _timer;
        private static readonly ConcurrentDictionary<string, bool> _busyPorts = new ConcurrentDictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        private readonly IHardwareLogger _logger;
        private string _currentPort;

        public RelayController(IHardwareLogger logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Schaltet das Relay ein, wartet die angegebene Zeit (in Sekunden) und schaltet es wieder aus.
        /// </summary>
        /// <param name="comPortName">COM-Port des Relay-Controllers (z.B. COM3).</param>
        /// <param name="relayNumber">Relay-Nummer (1-basiert).</param>
        /// <param name="relayBaudRate">Baudrate für die serielle Kommunikation (z.B. 9600).</param>
        /// <param name="relayTriggerTimeSeconds">Dauer in Sekunden, wie lange das Relay eingeschaltet bleibt.</param>
        public void CycleRelay(string comPortName, int relayNumber, int relayBaudRate, double relayTriggerTimeSeconds)
        {
            if (!_busyPorts.TryAdd(comPortName, true))
            {
                _logger?.Log($"Relay: Port {comPortName} is busy", HardwareLogLevel.Warning);
                return;
            }

            _currentPort = comPortName;
            _serialPort = new SerialPort(comPortName, relayBaudRate, Parity.None, 8, StopBits.One);

            if (_serialPort.IsOpen)
            {
                _logger?.Log($"Relay: Port {comPortName} already open", HardwareLogLevel.Warning);
                ReleaseBusyPort();
                return;
            }

            try
            {
                _serialPort.Open();
            }
            catch (Exception ex)
            {
                _logger?.Log($"Relay: Error opening port [{comPortName}]: {ex.Message}", HardwareLogLevel.Error);
                ReleaseBusyPort();
                return;
            }

            try
            {
                WriteCommand(relayNumber, true);
                _logger?.Log($"Relay {relayNumber} ON ({comPortName})", HardwareLogLevel.Success);

                _timer?.Dispose();
                _timer = new Timer();
                _timer.Interval = relayTriggerTimeSeconds * 1000.0;
                _timer.Elapsed += (sender, e) =>
                {
                    _timer.Enabled = false;
                    WriteCommand(relayNumber, false);
                    _logger?.Log($"Relay {relayNumber} OFF ({comPortName})");
                    Dispose();
                };
                _timer.Enabled = true;
            }
            catch (Exception ex)
            {
                _logger?.Log($"Relay: Error writing to port [{comPortName}]: {ex.Message}", HardwareLogLevel.Error);
                Dispose();
            }
        }

        private void WriteCommand(int relayNumber, bool on)
        {
            byte state = on ? (byte)1 : (byte)0;
            _serialPort.Write(new byte[]
            {
                0xFF,
                Convert.ToByte(relayNumber),
                state
            }, 0, 3);
        }

        private void ReleaseBusyPort()
        {
            if (_currentPort != null)
            {
                bool ignored;
                _busyPorts.TryRemove(_currentPort, out ignored);
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_serialPort != null)
                {
                    try { _serialPort.Dispose(); }
                    catch { /* Port already closed or invalid */ }
                    _serialPort = null;
                }
                ReleaseBusyPort();
            }
        }
    }
}
