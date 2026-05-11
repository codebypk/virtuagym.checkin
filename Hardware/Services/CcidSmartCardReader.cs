using Hardware.Events;
using Hardware.Interfaces;
using Hardware.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace Hardware.Services
{
    /// <summary>
    /// CCID/PC/SC Smartcard-Reader (z.B. HID Omnikey 5022, Identiv uTrust 3700F).
    /// Verwendet die Windows Smart Card API (winscard.dll) über P/Invoke.
    /// Liest die UID kontaktloser Karten und feuert das <see cref="CardRead"/>-Event.
    /// Komplett unabhängig von UI, Business-Logik und Checkin-Handler.
    /// </summary>
    public class CcidSmartCardReader : IDisposable
    {
        #region WinSCard P/Invoke

        private const int SCARD_SCOPE_SYSTEM = 2;
        private const int SCARD_SHARE_SHARED = 2;
        private const int SCARD_PROTOCOL_T0 = 1;
        private const int SCARD_PROTOCOL_T1 = 2;
        private const int SCARD_LEAVE_CARD = 0;
        private const int SCARD_S_SUCCESS = 0;
        private const int SCARD_STATE_PRESENT = 0x00000020;
        private const int SCARD_STATE_EMPTY = 0x00000010;
        private const int SCARD_STATE_CHANGED = 0x00000002;
        private const int SCARD_STATE_UNAWARE = 0;
        private const int INFINITE = unchecked((int)0xFFFFFFFF);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct SCARD_READERSTATE
        {
            public string szReader;
            public IntPtr pvUserData;
            public int dwCurrentState;
            public int dwEventState;
            public int cbAtr;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 36)]
            public byte[] rgbAtr;
        }

        [DllImport("winscard.dll", CharSet = CharSet.Unicode)]
        private static extern int SCardEstablishContext(int dwScope, IntPtr pvReserved1, IntPtr pvReserved2, out IntPtr phContext);

        [DllImport("winscard.dll", CharSet = CharSet.Unicode)]
        private static extern int SCardReleaseContext(IntPtr hContext);

        [DllImport("winscard.dll", CharSet = CharSet.Unicode)]
        private static extern int SCardListReaders(IntPtr hContext, string mszGroups, char[] mszReaders, ref int pcchReaders);

        [DllImport("winscard.dll", CharSet = CharSet.Unicode)]
        private static extern int SCardConnect(IntPtr hContext, string szReader, int dwShareMode, int dwPreferredProtocols, out IntPtr phCard, out int pdwActiveProtocol);

        [DllImport("winscard.dll")]
        private static extern int SCardDisconnect(IntPtr hCard, int dwDisposition);

        [DllImport("winscard.dll")]
        private static extern int SCardTransmit(IntPtr hCard, IntPtr pioSendPci, byte[] pbSendBuffer, int cbSendLength, IntPtr pioRecvPci, byte[] pbRecvBuffer, ref int pcbRecvLength);

        [DllImport("winscard.dll")]
        private static extern int SCardGetStatusChange(IntPtr hContext, int dwTimeout, [In, Out] SCARD_READERSTATE[] rgReaderStates, int cReaders);

        [DllImport("winscard.dll")]
        private static extern int SCardCancel(IntPtr hContext);

        [DllImport("winscard.dll")]
        private static extern IntPtr SCardGetPci(int dwProtocol);

        [DllImport("winscard.dll")]
        private static extern IntPtr g_rgSCardT0Pci();

        [DllImport("winscard.dll")]
        private static extern IntPtr g_rgSCardT1Pci();

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr LoadLibrary(string lpFileName);

        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, ExactSpelling = true)]
        private static extern IntPtr GetProcAddress(IntPtr hModule, string lpProcName);

        #endregion

        private static readonly Lazy<IntPtr> _winsCardHandle = new(() => LoadLibrary("winscard.dll"));
        private static readonly Lazy<IntPtr> _pciT0 = new(() => GetProcAddress(_winsCardHandle.Value, "g_rgSCardT0Pci"));
        private static readonly Lazy<IntPtr> _pciT1 = new(() => GetProcAddress(_winsCardHandle.Value, "g_rgSCardT1Pci"));

        private const int SCARD_E_CANCELLED = unchecked((int)0x80100002);
        private const int SCARD_E_INVALID_HANDLE = unchecked((int)0x80100003);
        private const int SCARD_E_READER_UNAVAILABLE = unchecked((int)0x80100017);
        private const int SCARD_E_SERVICE_STOPPED = unchecked((int)0x8010001E);
        private const int SCARD_E_NO_READERS_AVAILABLE = unchecked((int)0x8010002E);

        private readonly string _mappingUuid;
        private readonly IHardwareLogger _logger;
        private readonly string _readerName;
        private readonly string _name;
        private readonly double _duplicateTimeoutSeconds;
        private readonly bool _debugMode;

        private IntPtr _context;
        private bool _disposed;
        private CancellationTokenSource _cts;
        private string _lastUid = string.Empty;
        private DateTime _lastUidTime = DateTime.MinValue;

        /// <summary>
        /// Der Geräte-Mapping-ID, die beim Erstellen des Readers angegeben wurde.
        /// </summary>
        public string MappingUuid => _mappingUuid;

        /// <summary>
        /// Indicates whether the CCID reader context is still valid (reader physically connected).
        /// </summary>
        public bool IsConnected
        {
            get
            {
                if (_disposed || _context == IntPtr.Zero)
                    return false;
                var readers = ListReaders();
                return readers.Any(r => r.IndexOf(_readerName, StringComparison.OrdinalIgnoreCase) >= 0);
            }
        }

        /// <summary>
        /// Wird ausgelöst wenn eine Karte gelesen wurde (nach Duplikat-Prüfung).
        /// Achtung: Event wird auf einem Hintergrund-Thread gefeuert.
        /// </summary>
        public event EventHandler<CardReadEventArgs> CardRead;

        /// <summary>
        /// Erstellt einen neuen CCID SmartCard Reader.
        /// </summary>
        /// <param name="mappingUuid">ID des Geräte-Mappings.</param>
        /// <param name="logger">Logger-Instanz.</param>
        /// <param name="readerName">Teilstring des PC/SC Reader-Namens (für automatischen Match).</param>
        /// <param name="name">Optionaler Anzeigename für Log-Meldungen.</param>
        /// <param name="duplicateTimeoutSeconds">Zeitspanne in Sekunden, in der dieselbe Karte ignoriert wird.</param>
        /// <param name="debugMode">Erweiterte Log-Ausgaben aktivieren.</param>
        public CcidSmartCardReader(string mappingUuid, IHardwareLogger logger, string readerName,
            string name = null, double duplicateTimeoutSeconds = 5, bool debugMode = false)
        {
            ArgumentNullException.ThrowIfNull(logger);
            ArgumentNullException.ThrowIfNull(readerName);
            _mappingUuid = mappingUuid;
            _logger = logger;
            _readerName = readerName;
            _name = name;
            _duplicateTimeoutSeconds = duplicateTimeoutSeconds;
            _debugMode = debugMode;

            ConnectToReader();
        }

        private void ConnectToReader()
        {
            try
            {
                int result = SCardEstablishContext(SCARD_SCOPE_SYSTEM, IntPtr.Zero, IntPtr.Zero, out _context);
                if (result != SCARD_S_SUCCESS)
                {
                    _logger.Log($"CCID: Context error (0x{result:X8})", HardwareLogLevel.Error);
                    return;
                }

                var readers = ListReaders();
                if (readers.Count == 0)
                {
                    _logger.Log("CCID: No PC/SC readers found!", HardwareLogLevel.Error);
                    return;
                }

                foreach (var r in readers)
                {
                    if (_debugMode)
                        _logger.Log($"CCID Reader: '{r}'");
                }

                string matchedReader = readers.FirstOrDefault(r =>
                    r.IndexOf(_readerName, StringComparison.OrdinalIgnoreCase) >= 0);

                if (matchedReader == null)
                {
                    _logger.Log($"CCID: Reader '{_readerName}' not found! Available: {string.Join(", ", readers)}", HardwareLogLevel.Error);
                    return;
                }

                _logger.Log($"CCID: Reader '{matchedReader}' connected.", HardwareLogLevel.Success);
                StartPolling(matchedReader);
            }
            catch (Exception ex)
            {
                _logger.Log($"CCID: Error connecting: {ex.Message}", HardwareLogLevel.Error);
            }
        }

        /// <summary>
        /// Gibt alle verfügbaren PC/SC Reader-Namen zurück.
        /// </summary>
        public static List<string> GetAvailableReaders()
        {
            IntPtr context;
            int result = SCardEstablishContext(SCARD_SCOPE_SYSTEM, IntPtr.Zero, IntPtr.Zero, out context);
            if (result != SCARD_S_SUCCESS)
                return [];

            try
            {
                return ListReadersFromContext(context);
            }
            finally
            {
                SCardReleaseContext(context);
            }
        }

        private List<string> ListReaders() => ListReadersFromContext(_context);

        private static List<string> ListReadersFromContext(IntPtr context)
        {
            int pcchReaders = 0;
            int result = SCardListReaders(context, null, null, ref pcchReaders);
            if (result != SCARD_S_SUCCESS || pcchReaders <= 0)
                return [];

            char[] buffer = new char[pcchReaders];
            result = SCardListReaders(context, null, buffer, ref pcchReaders);
            if (result != SCARD_S_SUCCESS)
                return [];

            string allReaders = new string(buffer);
            return [.. allReaders.Split('\0', StringSplitOptions.RemoveEmptyEntries)];
        }

        #region Polling Loop

        private void StartPolling(string readerName)
        {
            _cts = new CancellationTokenSource();
            Task.Run(() => PollLoopAsync(readerName, _cts.Token));
            _logger.Log($"CCID Reader '{readerName}' waiting for card...");
        }

        private async Task PollLoopAsync(string readerName, CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var states = new SCARD_READERSTATE[1];
                    states[0].szReader = readerName;
                    states[0].dwCurrentState = SCARD_STATE_UNAWARE;

                    int result = SCardGetStatusChange(_context, INFINITE, states, 1);
                    if (ct.IsCancellationRequested) break;

                    if (result == SCARD_E_CANCELLED) break;

                    if (result == SCARD_E_READER_UNAVAILABLE || result == SCARD_E_NO_READERS_AVAILABLE
                        || result == SCARD_E_SERVICE_STOPPED || result == SCARD_E_INVALID_HANDLE)
                    {
                        _logger.Log($"CCID: Reader disconnected (0x{result:X8}). Attempting reconnect...", HardwareLogLevel.Warning);
                        readerName = await TryReconnectAsync(ct);
                        if (readerName == null)
                        {
                            _logger.Log("CCID: Reconnect failed, reader not found.", HardwareLogLevel.Error);
                            break;
                        }
                        continue;
                    }

                    if (result != SCARD_S_SUCCESS)
                    {
                        _logger.Log($"CCID: SCardGetStatusChange error (0x{result:X8})", HardwareLogLevel.Warning);
                        await Task.Delay(1000, ct);
                        continue;
                    }

                    if ((states[0].dwEventState & SCARD_STATE_PRESENT) != 0)
                    {
                        string uid = ReadCardUid(readerName);
                        if (!string.IsNullOrEmpty(uid))
                        {
                            var duplicateTimeout = TimeSpan.FromSeconds(Math.Max(_duplicateTimeoutSeconds, 0.05));
                            bool isDuplicate = uid == _lastUid && (DateTime.UtcNow - _lastUidTime) < duplicateTimeout;
                            if (!isDuplicate)
                            {
                                _lastUid = uid;
                                _lastUidTime = DateTime.UtcNow;
                                _logger.Log($"CCID Reader: Card read, UID '{uid}'", HardwareLogLevel.Success);

                                var card = new Card(uid);
                                string sourceName = !string.IsNullOrWhiteSpace(_name) ? _name : readerName;
                                CardRead?.Invoke(this, new CardReadEventArgs(card, sourceName));
                            }
                            else if (_debugMode)
                            {
                                _logger.Log($"CCID: Card already scanned (re-arm timeout: {duplicateTimeout.TotalSeconds:0.###}s)", HardwareLogLevel.Warning);
                            }
                        }

                        states[0].dwCurrentState = SCARD_STATE_PRESENT;
                        result = SCardGetStatusChange(_context, INFINITE, states, 1);
                        if (ct.IsCancellationRequested || result == SCARD_E_CANCELLED) break;

                        if (result == SCARD_E_READER_UNAVAILABLE || result == SCARD_E_NO_READERS_AVAILABLE
                            || result == SCARD_E_SERVICE_STOPPED || result == SCARD_E_INVALID_HANDLE)
                        {
                            _logger.Log($"CCID: Reader disconnected (0x{result:X8}). Attempting reconnect...", HardwareLogLevel.Warning);
                            readerName = await TryReconnectAsync(ct);
                            if (readerName == null) break;
                            continue;
                        }

                        if (result == SCARD_S_SUCCESS && (states[0].dwEventState & SCARD_STATE_EMPTY) != 0)
                        {
                            // Start re-arm window after physical card removal.
                            _lastUidTime = DateTime.UtcNow;
                        }
                    }

                    await Task.Yield();
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.Log($"CCID PollLoop error: {ex.Message}", HardwareLogLevel.Error);
                    await Task.Delay(500, ct);
                }
            }
        }

        private async Task<string> TryReconnectAsync(CancellationToken ct)
        {
            if (_context != IntPtr.Zero)
            {
                try { SCardReleaseContext(_context); }
                catch { }
                _context = IntPtr.Zero;
            }

            const int minDelayMs = 2000;
            const int maxDelayMs = 60000;
            int delayMs = minDelayMs;
            int totalSeconds = 0;

            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(delayMs, ct);
                totalSeconds += delayMs / 1000;

                int result = SCardEstablishContext(SCARD_SCOPE_SYSTEM, IntPtr.Zero, IntPtr.Zero, out _context);
                if (result != SCARD_S_SUCCESS)
                {
                    delayMs = Math.Min(delayMs * 2, maxDelayMs);
                    continue;
                }

                var readers = ListReaders();
                string matchedReader = readers.FirstOrDefault(r =>
                    r.IndexOf(_readerName, StringComparison.OrdinalIgnoreCase) >= 0);

                if (matchedReader != null)
                {
                    _logger.Log($"CCID: Reader '{matchedReader}' reconnected after {totalSeconds}s.", HardwareLogLevel.Success);
                    _lastUid = string.Empty;
                    return matchedReader;
                }

                SCardReleaseContext(_context);
                _context = IntPtr.Zero;
                delayMs = Math.Min(delayMs * 2, maxDelayMs);
            }

            return null;
        }

        private string ReadCardUid(string readerName)
        {
            IntPtr hCard;
            int protocol;
            int result = SCardConnect(_context, readerName, SCARD_SHARE_SHARED,
                SCARD_PROTOCOL_T0 | SCARD_PROTOCOL_T1, out hCard, out protocol);

            if (result != SCARD_S_SUCCESS)
                return null;

            try
            {
                byte[] getUidCmd = { 0xFF, 0xCA, 0x00, 0x00, 0x00 };
                byte[] recvBuffer = new byte[256];
                int recvLength = recvBuffer.Length;

                IntPtr pci = protocol == SCARD_PROTOCOL_T0 ? _pciT0.Value : _pciT1.Value;

                if (pci == IntPtr.Zero)
                    return null;

                result = SCardTransmit(hCard, pci, getUidCmd, getUidCmd.Length, IntPtr.Zero, recvBuffer, ref recvLength);
                if (result != SCARD_S_SUCCESS || recvLength < 2)
                    return null;

                byte sw1 = recvBuffer[recvLength - 2];
                byte sw2 = recvBuffer[recvLength - 1];
                if (sw1 != 0x90 || sw2 != 0x00)
                    return null;

                int uidLength = recvLength - 2;
                if (uidLength <= 0)
                    return null;

                byte[] uidBytes = new byte[uidLength];
                Array.Copy(recvBuffer, 0, uidBytes, 0, uidLength);

                return BitConverter.ToString(uidBytes).Replace("-", "").ToUpperInvariant();
            }
            catch (Exception ex)
            {
                if (_debugMode)
                    _logger.Log($"CCID ReadCardUid error: {ex.Message}", HardwareLogLevel.Error);
                return null;
            }
            finally
            {
                SCardDisconnect(hCard, SCARD_LEAVE_CARD);
            }
        }

        #endregion

        /// <summary>
        /// Simuliert einen Kartenscan (feuert CardRead-Event).
        /// </summary>
        public void SimulateCardRead(string cardId)
        {
            _logger.Log($"SIMULATION (CCID): Card '{cardId}'", HardwareLogLevel.Warning);
            var card = new Card(cardId);
            string sourceName = !string.IsNullOrWhiteSpace(_name) ? _name : _readerName;
            CardRead?.Invoke(this, new CardReadEventArgs(card, $"SIM {sourceName}"));
        }

        /// <summary>
        /// Stoppt den Reader und gibt Ressourcen frei.
        /// </summary>
        public void Stop()
        {
            _cts?.Cancel();
            if (_context != IntPtr.Zero)
            {
                SCardCancel(_context);
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _cts?.Cancel();
            _cts?.Dispose();

            if (_context != IntPtr.Zero)
            {
                SCardCancel(_context);
                SCardReleaseContext(_context);
                _context = IntPtr.Zero;
            }
        }
    }
}
