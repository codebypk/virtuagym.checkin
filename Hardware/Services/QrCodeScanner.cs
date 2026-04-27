using Hardware.Events;
using Hardware.Interfaces;
using Hardware.Models;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using ZXing;
using ZXing.Common;

namespace Hardware.Services
{
    /// <summary>
    /// QR-Code-Scanner über eine angeschlossene Kamera.
    /// Liest QR-Codes und feuert das <see cref="QrCodeRead"/>-Event.
    /// Komplett unabhängig von UI, Business-Logik und Checkin-Handler.
    /// Nutzt OpenCvSharp4 für Kamera-Zugriff und ZXing.Net für QR-Decoding.
    /// </summary>
    public class QrCodeScanner : IDisposable
    {
        private readonly string _mappingUuid;
        private readonly IHardwareLogger _logger;
        private readonly int _cameraIndex;
        private readonly string _name;
        private readonly double _duplicateTimeoutSeconds;
        private readonly bool _debugMode;
        private readonly int _resolutionWidth;
        private readonly int _resolutionHeight;
        private readonly VideoCaptureAPIs _captureApi;

        private VideoCapture _videoCapture;
        private Thread _captureThread;
        private volatile bool _running;
        private readonly BarcodeReaderGeneric _barcodeReader;

        private string _lastQrCode = string.Empty;
        private long _lastQrCodeTicks;
        private long _lastPreviewTicks;
        private static readonly long PreviewIntervalTicks = TimeSpan.FromMilliseconds(100).Ticks;
        private readonly object _qrProcessLock = new object();

        // Vorallokierte Mats für den Capture-Loop – vermeidet GC-Druck
        private Mat _frame;
        private Mat _gray;
        private Mat _resized;
        private byte[] _grayBuffer;

        /// <summary>Zielbreite für das Decode-Bild. Kleineres Bild = schnelleres Decoding.</summary>
        private const int DecodeWidth = 640;

        private bool _disposed;

        /// <summary>
        /// Der Geräte-Mapping-ID, die beim Erstellen des Readers angegeben wurde.
        /// </summary>
        public string MappingUuid => _mappingUuid;

        /// <summary>Statische Registry aller aktiven Scanner-Instanzen.</summary>
        private static readonly List<QrCodeScanner> _activeInstances = new();
        private static readonly object _instanceLock = new();

        /// <summary>
        /// Wird ausgelöst wenn ein QR-Code gelesen wurde (nach Duplikat-Prüfung).
        /// Achtung: Event wird auf einem Hintergrund-Thread gefeuert.
        /// </summary>
        public event EventHandler<QrCodeReadEventArgs> QrCodeRead;

        /// <summary>
        /// Wird ausgelöst wenn ein neues Kamera-Vorschaubild verfügbar ist (gedrosselt).
        /// Achtung: Event wird auf einem Hintergrund-Thread gefeuert.
        /// </summary>
        public event EventHandler<CameraPreviewEventArgs> CameraPreview;

        /// <summary>
        /// Erstellt einen neuen QR-Code-Scanner.
        /// </summary>
        /// <param name="mappingUuid">ID des Geräte-Mappings.</param>
        /// <param name="logger">Logger für Log-Ausgaben.</param>
        /// <param name="cameraIndex">Index des Kamera-Geräts.</param>
        /// <param name="name">Optionaler Anzeigename.</param>
        /// <param name="duplicateTimeoutSeconds">Zeitspanne in Sekunden, in der derselbe QR-Code ignoriert wird.</param>
        /// <param name="debugMode">Erweiterte Log-Ausgaben aktivieren.</param>
        /// <param name="resolutionWidth">Kamera-Auflösung Breite (Standard: 640).</param>
        /// <param name="resolutionHeight">Kamera-Auflösung Höhe (Standard: 480).</param>
        /// <param name="captureApi">Video-Backend (Standard: ANY).</param>
        public QrCodeScanner(string mappingUuid, IHardwareLogger logger, int cameraIndex, string name = null,
            double duplicateTimeoutSeconds = 5, bool debugMode = false,
            int resolutionWidth = 640, int resolutionHeight = 480,
            VideoCaptureAPIs captureApi = VideoCaptureAPIs.ANY)
        {
            ArgumentNullException.ThrowIfNull(logger);
            _mappingUuid = mappingUuid;
            _logger = logger;
            _cameraIndex = cameraIndex;
            _name = name;
            _duplicateTimeoutSeconds = duplicateTimeoutSeconds;
            _debugMode = debugMode;
            _resolutionWidth = resolutionWidth > 0 ? resolutionWidth : 640;
            _resolutionHeight = resolutionHeight > 0 ? resolutionHeight : 480;
            _captureApi = captureApi;

            _barcodeReader = new BarcodeReaderGeneric
            {
                AutoRotate = false,
                Options = new DecodingOptions
                {
                    TryHarder = false,
                    TryInverted = false,
                    PossibleFormats = new[] { BarcodeFormat.QR_CODE }
                }
            };

            StartCamera();

            lock (_instanceLock) { _activeInstances.Add(this); }
        }

        private void StartCamera()
        {
            try
            {
                _videoCapture = new VideoCapture(_cameraIndex, _captureApi);
                if (!_videoCapture.IsOpened())
                {
                    _logger.Log($"QR-Scanner: Could not open camera {_cameraIndex} with backend {_captureApi}!", HardwareLogLevel.Error);
                    return;
                }

                // Kamera-Auflösung setzen
                _videoCapture.Set(VideoCaptureProperties.FrameWidth, _resolutionWidth);
                _videoCapture.Set(VideoCaptureProperties.FrameHeight, _resolutionHeight);

                // Vorallokierte Mats
                _frame = new Mat();
                _gray = new Mat();
                _resized = new Mat();

                _running = true;
                _captureThread = new Thread(CaptureLoop)
                {
                    Name = "QrCodeScanner-Capture",
                    IsBackground = true
                };
                _captureThread.Start();

                string displayName = !string.IsNullOrWhiteSpace(_name) ? _name : $"Camera {_cameraIndex}";
                _logger.Log($"QR-Scanner [{displayName}] started", HardwareLogLevel.Success);
            }
            catch (Exception ex)
            {
                _logger.Log($"QR-Scanner error: {ex.Message}", HardwareLogLevel.Error);
            }
        }

        private void CaptureLoop()
        {
            while (_running)
            {
                try
                {
                    if (!_videoCapture.Read(_frame) || _frame.Empty())
                    {
                        Thread.Sleep(30);
                        continue;
                    }

                    // 1) Preview senden (gedrosselt) – nur wenn Handler registriert
                    long nowTicks = Stopwatch.GetTimestamp();
                    if (CameraPreview != null &&
                        (nowTicks - _lastPreviewTicks) >= PreviewIntervalTicks)
                    {
                        _lastPreviewTicks = nowTicks;
                        Cv2.ImEncode(".jpg", _frame, out var jpegBytes);
                        string cameraLabel = !string.IsNullOrWhiteSpace(_name) ? _name : $"Camera {_cameraIndex}";
                        CameraPreview?.Invoke(this, new CameraPreviewEventArgs(jpegBytes, cameraLabel));
                    }

                    // 2) Graustufen-Konvertierung (schneller für ZXing)
                    Cv2.CvtColor(_frame, _gray, ColorConversionCodes.BGR2GRAY);

                    // 3) Auf Decode-Breite skalieren falls nötig
                    Mat decodeSource = _gray;
                    if (_gray.Width > DecodeWidth)
                    {
                        double scale = (double)DecodeWidth / _gray.Width;
                        Cv2.Resize(_gray, _resized, new OpenCvSharp.Size(DecodeWidth, (int)(_gray.Height * scale)),
                            interpolation: InterpolationFlags.Nearest);
                        decodeSource = _resized;
                    }

                    // 4) Direkt aus Mat-Daten dekodieren – KEIN Bitmap erzeugen
                    int dataLength = decodeSource.Rows * decodeSource.Cols;
                    if (_grayBuffer == null || _grayBuffer.Length != dataLength)
                        _grayBuffer = new byte[dataLength];
                    System.Runtime.InteropServices.Marshal.Copy(decodeSource.Data, _grayBuffer, 0, dataLength);

                    var luminance = new RawGrayLuminanceSource(
                        _grayBuffer,
                        decodeSource.Width,
                        decodeSource.Height);

                    var result = _barcodeReader.Decode(luminance);
                    if (result != null && !string.IsNullOrWhiteSpace(result.Text))
                    {
                        ProcessQrCode(result.Text);
                    }
                }
                catch (Exception ex)
                {
                    if (_debugMode)
                        _logger.Log($"QR-Scanner frame error: {ex.Message}", HardwareLogLevel.Warning);
                }

                Thread.Sleep(15); // ~60 FPS Decode-Rate
            }
        }

        private void ProcessQrCode(string qrCode)
        {
            lock (_qrProcessLock)
            {
                long nowTicks = Stopwatch.GetTimestamp();
                long duplicateTimeoutTicks = (long)(_duplicateTimeoutSeconds * Stopwatch.Frequency);
                if (qrCode == _lastQrCode && (nowTicks - _lastQrCodeTicks) < duplicateTimeoutTicks)
                    return;

                _lastQrCode = qrCode;
                _lastQrCodeTicks = nowTicks;
            }

            string readerName = !string.IsNullOrWhiteSpace(_name) ? _name : "QR-Scanner";

            if (_debugMode)
                _logger.Log($"[{readerName}] QR-Code read: '{qrCode}'");

            QrCodeRead?.Invoke(this, new QrCodeReadEventArgs(qrCode, readerName));
        }

        /// <summary>Kamera-Index dieses Scanners.</summary>
        public int CameraIndex => _cameraIndex;

        /// <summary>
        /// Pausiert den Scanner und gibt die Kamera frei,
        /// damit sie z.B. für einen Geräte-Test verwendet werden kann.
        /// </summary>
        public void Pause()
        {
            if (!_running) return;
            _running = false;
            _captureThread?.Join(TimeSpan.FromSeconds(3));
            _videoCapture?.Release();
            _videoCapture?.Dispose();
            _videoCapture = null;
            _logger.Log($"QR-Scanner [{_name ?? $"Camera {_cameraIndex}"}] paused");
        }

        /// <summary>
        /// Setzt den Scanner nach einer Pause fort und öffnet die Kamera erneut.
        /// </summary>
        public void Resume()
        {
            if (_running || _disposed) return;
            StartCamera();
            _logger.Log($"QR-Scanner [{_name ?? $"Camera {_cameraIndex}"}] resumed");
        }

        /// <summary>
        /// Pausiert den Scanner der den angegebenen Kamera-Index verwendet.
        /// Gibt die Kamera frei, damit sie für einen Test verwendet werden kann.
        /// </summary>
        public static void PauseByCamera(int cameraIndex)
        {
            lock (_instanceLock)
            {
                foreach (var s in _activeInstances)
                    if (s._cameraIndex == cameraIndex) s.Pause();
            }
        }

        /// <summary>
        /// Setzt den Scanner für den angegebenen Kamera-Index fort.
        /// </summary>
        public static void ResumeByCamera(int cameraIndex)
        {
            lock (_instanceLock)
            {
                foreach (var s in _activeInstances)
                    if (s._cameraIndex == cameraIndex) s.Resume();
            }
        }


        /// <summary>
        /// Stoppt die Kamera und gibt Ressourcen frei.
        /// </summary>
        public void Stop()
        {
            _running = false;
            _captureThread?.Join(TimeSpan.FromSeconds(3));
            _videoCapture?.Release();
            _videoCapture?.Dispose();
            _videoCapture = null;
            _frame?.Dispose();
            _gray?.Dispose();
            _resized?.Dispose();
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                lock (_instanceLock) { _activeInstances.Remove(this); }
                Stop();
                _disposed = true;
            }
            GC.SuppressFinalize(this);
        }
    }

    /// <summary>
    /// ZXing LuminanceSource die direkt auf rohen Graustufen-Bytes arbeitet.
    /// Vermeidet die teure Mat→Bitmap→LuminanceSource-Konvertierung.
    /// </summary>
    public sealed class RawGrayLuminanceSource : LuminanceSource
    {
        private readonly byte[] _luminances;

        public RawGrayLuminanceSource(byte[] grayData, int width, int height)
            : base(width, height)
        {
            _luminances = grayData;
        }

        public override byte[] getRow(int y, byte[] row)
        {
            int offset = y * Width;
            if (row == null || row.Length < Width)
                row = new byte[Width];
            Buffer.BlockCopy(_luminances, offset, row, 0, Width);
            return row;
        }

        public override byte[] Matrix => _luminances;
    }
}
