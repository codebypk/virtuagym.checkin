using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Virtuagym.CheckIn.Core.Models;
using Virtuagym.CheckIn.Core.Helper;
using Virtuagym.CheckIn.Core.Services;
using Virtuagym.CheckIn.WPF.Properties;

namespace Virtuagym.CheckIn.WPF
{
    /// <summary>
    /// Fullscreen Welcome Window für den Virtuagym Client Modus.
    /// Zeigt nach einem Check-in den Mitgliedernamen, Avatar und Status an.
    /// Wechselt nach einem Timeout zurück zum Idle-Zustand.
    /// Hintergrundbilder werden lokal gecacht und rotiert (wie im Original CheckInClient).
    /// </summary>
    public partial class WelcomeWindow : Window
    {
        private readonly DispatcherTimer _idleTimer;
        private readonly DispatcherTimer _foregroundTimer;
        private readonly DispatcherTimer _backgroundTimer;
        private static int IdleTimeoutMs => Settings.Default.WelcomeIdleTimeoutMs;
        private const int ForegroundCheckIntervalMs = Constants.WelcomeForegroundCheckIntervalMs;

        private string[] _backgroundImages;
        private int _currentImageIndex = -1;
        private int _screenIndex;

        private readonly ObservableCollection<QrCameraPreviewTile> _qrCameraPreviewTiles = new ObservableCollection<QrCameraPreviewTile>();
        private readonly Dictionary<string, QrCameraPreviewTile> _qrCameraPreviewByLabel = new Dictionary<string, QrCameraPreviewTile>(StringComparer.OrdinalIgnoreCase);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        public WelcomeWindow(int screenIndex = 0)
        {
            InitializeComponent();
            ApplyLocalization();

            _idleTimer = new DispatcherTimer();
            _idleTimer.Interval = TimeSpan.FromMilliseconds(IdleTimeoutMs);
            _idleTimer.Tick += IdleTimer_Tick;

            // Vordergrund-Erzwingung: Nur wenn NICHT im Debugger gestartet
            _foregroundTimer = new DispatcherTimer();
            _foregroundTimer.Interval = TimeSpan.FromMilliseconds(ForegroundCheckIntervalMs);
            _foregroundTimer.Tick += ForegroundTimer_Tick;

            if (!Debugger.IsAttached && !Settings.Default.DebugMode)
            {
                _foregroundTimer.Start();
            }

            // Timer für Hintergrundbild-Rotation
            _backgroundTimer = new DispatcherTimer();
            _backgroundTimer.Interval = TimeSpan.FromMilliseconds(Settings.Default.BackgroundRotationIntervalMs);
            _backgroundTimer.Tick += BackgroundTimer_Tick;

            _screenIndex = screenIndex;
            WindowStartupLocation = WindowStartupLocation.Manual;
            SourceInitialized += WelcomeWindow_SourceInitialized;
            ShowIdle();

            LoadBackgroundsFromFolder();

            icQrCameraPreviews.ItemsSource = _qrCameraPreviewTiles;
        }

        /// <summary>
        /// Passt das Fenster im Debug-Modus an: Titelleiste sichtbar, nicht immer im Vordergrund.
        /// Im Produktivmodus (Foreground): ResizeMode bleibt NoResize für Fullscreen-Verhalten.
        /// </summary>
        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            if (Debugger.IsAttached)
            {
                WindowStyle = WindowStyle.SingleBorderWindow;
                Topmost = false;
                ResizeMode = ResizeMode.CanResize;
            }
        }

        /// <summary>
        /// Stellt sicher, dass das WelcomeWindow immer im Vordergrund bleibt.
        /// Wird deaktiviert wenn der Debugger angehängt ist.
        /// </summary>
        private void ApplyLocalization()
        {
            txtPleaseWait.Text = L.T("Welcome_PleaseWait");
        }

        private void ForegroundTimer_Tick(object sender, EventArgs e)
        {
            if (!IsVisible) return;

            try
            {
                var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                if (hwnd == IntPtr.Zero) return;

                var currentForeground = GetForegroundWindow();
                if (currentForeground != hwnd)
                {
                    Topmost = false;
                    Topmost = true;
                    Activate();
                    SetForegroundWindow(hwnd);
                }
            }
            catch
            {
                // P/Invoke-Fehler bei Vordergrund-Erzwingung ignorieren
            }
        }

        private void WelcomeWindow_SourceInitialized(object sender, EventArgs e)
        {
            PositionOnScreen(_screenIndex);
        }

        /// <summary>
        /// Positioniert das Fenster im Fullscreen auf dem angegebenen Monitor.
        /// Berücksichtigt die DPI-Skalierung: Screen.Bounds liefert physische Pixel,
        /// WPF arbeitet mit geräteunabhängigen Pixeln (DIPs).
        /// </summary>
        private void PositionOnScreen(int screenIndex)
        {
            var screens = System.Windows.Forms.Screen.AllScreens;
            if (screenIndex < 0 || screenIndex >= screens.Length)
                screenIndex = 0;

            var screen = screens[screenIndex];
            var bounds = screen.Bounds;

            // DPI-Skalierung vom aktuellen Fenster-Handle ermitteln
            double scaleX = 1.0, scaleY = 1.0;
            var source = PresentationSource.FromVisual(this);
            if (source?.CompositionTarget != null)
            {
                scaleX = source.CompositionTarget.TransformToDevice.M11;
                scaleY = source.CompositionTarget.TransformToDevice.M22;
            }

            Left = bounds.Left / scaleX;
            Top = bounds.Top / scaleY;
            Width = bounds.Width / scaleX;
            Height = bounds.Height / scaleY;
        }

        /// <summary>
        /// Setzt das Club-Logo (URL oder lokaler Pfad).
        /// Bei lokalem Pfad wird BitmapCacheOption.OnLoad verwendet um Dateisperren zu vermeiden.
        /// </summary>
        public void SetClubLogo(string logoPath)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action<string>(SetClubLogo), logoPath);
                return;
            }

            try
            {
                if (!string.IsNullOrWhiteSpace(logoPath) && File.Exists(logoPath))
                {
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.UriSource = new Uri(Path.GetFullPath(logoPath), UriKind.Absolute);
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.EndInit();
                    imgClubLogo.Source = bmp;
                    imgClubLogo.Visibility = Visibility.Visible;
                }
                else
                {
                    imgClubLogo.Visibility = Visibility.Collapsed;
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"SetClubLogo Fehler: {ex.Message}");
                imgClubLogo.Visibility = Visibility.Collapsed;
            }
        }

        /// <summary>
        /// Setzt den Idle-Hint-Text basierend auf den konfigurierten Eingabetypen.
        /// </summary>
        public void SetIdleHintText(bool hasRfid, bool hasQrCode)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action<bool, bool>(SetIdleHintText), hasRfid, hasQrCode);
                return;
            }

            if (hasRfid && hasQrCode)
                txtIdleHint.Text = Settings.Default.IdleHintRfidAndQrCode;
            else if (hasQrCode)
                txtIdleHint.Text = Settings.Default.IdleHintQrCode;
            else
                txtIdleHint.Text = Settings.Default.IdleHintRfid;
        }

        /// <summary>
        /// Lädt alle Hintergrundbilder aus dem Ordner resources\backgrounds.
        /// Bei einem Bild wird es direkt angezeigt, bei mehreren wird der Rotations-Timer gestartet.
        /// </summary>
        private void LoadBackgroundsFromFolder()
        {
            try
            {
                if (!Directory.Exists(Constants.BackgroundsFolder))
                    return;

                var supportedExtensions = new[] { ".png", ".jpg", ".jpeg", ".bmp", ".gif" };
                _backgroundImages = Directory.GetFiles(Constants.BackgroundsFolder)
                    .Where(f => supportedExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                    .OrderBy(f => f)
                    .ToArray();

                if (_backgroundImages.Length == 0)
                    return;

                _currentImageIndex = -1;
                ShowNextBackground();

                if (_backgroundImages.Length > 1)
                {
                    _backgroundTimer.Start();
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"LoadBackgroundsFromFolder Fehler: {ex.Message}");
            }
        }

        /// <summary>
        /// Wechselt zum nächsten Hintergrundbild aus dem lokalen Ordner.
        /// </summary>
        private void ShowNextBackground()
        {
            if (_backgroundImages == null || _backgroundImages.Length == 0)
                return;

            _currentImageIndex = (_currentImageIndex + 1) % _backgroundImages.Length;
            string localPath = _backgroundImages[_currentImageIndex];

            try
            {
                if (File.Exists(localPath))
                {
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.UriSource = new Uri(Path.GetFullPath(localPath), UriKind.Absolute);
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.EndInit();
                    imgBackgroundBrush.ImageSource = bmp;
                    borderBackground.Visibility = Visibility.Visible;
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"ShowNextBackground Fehler: {ex.Message}");
            }
        }

        private void BackgroundTimer_Tick(object sender, EventArgs e)
        {
            ShowNextBackground();
        }


        /// <summary>
        /// Zeigt einen "Laden..."-Zustand an, während der Check-in läuft.
        /// </summary>
        public void ShowLoader()
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(ShowLoader));
                return;
            }

            _idleTimer.Stop();
            txtIdleHint.Visibility = Visibility.Collapsed;
            borderCheckinInfo.Visibility = Visibility.Collapsed;
            panelLoader.Visibility = Visibility.Visible;

            _idleTimer.Interval = TimeSpan.FromMilliseconds(IdleTimeoutMs);
            _idleTimer.Start();
        }

        /// <summary>
        /// Zeigt das Check-in-Ergebnis im rechten Info-Panel an.
        /// </summary>
        public void ShowCheckinResult(string status, string name, string avatarUrl, string[] clientMessages, string readerName = null)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action<string, string, string, string[], string>(ShowCheckinResult),
                    status, name, avatarUrl, clientMessages, readerName);
                return;
            }

            _idleTimer.Stop();
            txtIdleHint.Visibility = Visibility.Collapsed;
            panelLoader.Visibility = Visibility.Collapsed;

            bool isOk = status == Constants.StatusOk;
            bool isWarn = status == Constants.StatusWarn;
            bool isDoubleScan = status == Constants.StatusDoubleScan;

            if (isOk || isWarn)
            {
                txtCheckinName.Text = name ?? "";
                txtCheckinName.Foreground = new SolidColorBrush(Colors.White);

                string statusText = (clientMessages != null && clientMessages.Length > 0)
                    ? string.Join(Environment.NewLine, clientMessages)
                    : Settings.Default.CheckinSuccessText;
                txtCheckinStatus.Text = statusText;

                ShowAvatar(avatarUrl);
                ShowOverlayIcon(isWarn ? OverlayIconType.Warning : OverlayIconType.Success);
            }
            else if (isDoubleScan)
            {
                // Doppelscan: Mitglied ist bereits angemeldet – Hinweis (nicht rot, sondern gelb/orange)
                txtCheckinName.Text = name ?? "";
                txtCheckinName.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xD5, 0x4F));

                string doubleScanText = (clientMessages != null && clientMessages.Length > 0)
                    ? string.Join(Environment.NewLine, clientMessages)
                    : L.T("Welcome_DoubleScan_AlreadyCheckedIn");
                txtCheckinStatus.Text = doubleScanText;

                ShowAvatar(avatarUrl);
                ShowOverlayIcon(OverlayIconType.DoubleScan);
            }
            else
            {
                // Reject (inkl. InsufficientCredits): Zeige Name und Avatar falls vorhanden
                if (!string.IsNullOrWhiteSpace(name))
                {
                    txtCheckinName.Text = name;
                }
                else
                {
                    txtCheckinName.Text = Settings.Default.CheckinNotFoundText;
                }
                txtCheckinName.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x66, 0x66));

                string errorText = (clientMessages != null && clientMessages.Length > 0)
                    ? string.Join(Environment.NewLine, clientMessages)
                    : Settings.Default.CheckinUnknownErrorText;
                txtCheckinStatus.Text = errorText;

                ShowAvatar(avatarUrl);
                ShowOverlayIcon(OverlayIconType.Error);
            }

            txtCheckinReaderName.Text = !string.IsNullOrWhiteSpace(readerName) ? readerName : "";

            borderCheckinInfo.Visibility = Visibility.Visible;

            _idleTimer.Interval = TimeSpan.FromMilliseconds(IdleTimeoutMs);
            _idleTimer.Start();
        }

        private enum OverlayIconType { None, Success, Error, Warning, DoubleScan }

        /// <summary>
        /// Zeigt das passende Overlay-Icon am Avatar an (Success, Error, Warning, DoubleScan).
        /// Verbessert die Usability durch visuelles Feedback am Avatar.
        /// </summary>
        private void ShowOverlayIcon(OverlayIconType overlayType)
        {
            // Alle Overlays ausblenden
            overlaySuccess.Visibility = Visibility.Collapsed;
            overlayError.Visibility = Visibility.Collapsed;
            overlayWarning.Visibility = Visibility.Collapsed;
            overlayDoubleScan.Visibility = Visibility.Collapsed;

            // Gewünschtes Overlay einblenden
            switch (overlayType)
            {
                case OverlayIconType.Success:
                    overlaySuccess.Visibility = Visibility.Visible;
                    break;
                case OverlayIconType.Error:
                    overlayError.Visibility = Visibility.Visible;
                    break;
                case OverlayIconType.Warning:
                    overlayWarning.Visibility = Visibility.Visible;
                    break;
                case OverlayIconType.DoubleScan:
                    overlayDoubleScan.Visibility = Visibility.Visible;
                    break;
            }
        }

        /// <summary>
        /// Zeigt den Avatar des Mitglieds an. Bei fehlendem Avatar wird ein User-Icon angezeigt.
        /// Unterstützt HTTP-URLs und lokale Dateipfade (für Offline-Fallback).
        /// </summary>
        private void ShowAvatar(string avatarUrl)
        {
            iconUserFallback.Visibility = Visibility.Collapsed;
            iconErrorFallback.Visibility = Visibility.Collapsed;
            iconWarnFallback.Visibility = Visibility.Collapsed;

            if (!string.IsNullOrWhiteSpace(avatarUrl))
            {
                try
                {
                    Uri uri;
                    if (avatarUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                        || avatarUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                    {
                        uri = new Uri(avatarUrl, UriKind.Absolute);
                    }
                    else if (File.Exists(avatarUrl))
                    {
                        uri = new Uri(Path.GetFullPath(avatarUrl), UriKind.Absolute);
                    }
                    else
                    {
                        uri = null;
                    }

                    if (uri != null)
                    {
                        var bmp = new BitmapImage();
                        bmp.BeginInit();
                        bmp.UriSource = uri;
                        bmp.CacheOption = BitmapCacheOption.OnLoad;
                        bmp.EndInit();
                        imgCheckinAvatar.Source = bmp;
                        borderCheckinAvatar.Visibility = Visibility.Visible;
                        return;
                    }
                }
                catch (Exception ex)
                {
                    Trace.WriteLine($"Avatar laden Fehler: {ex.Message}");
                }
            }

            imgCheckinAvatar.Source = null;
            iconUserFallback.Visibility = Visibility.Visible;
            borderCheckinAvatar.Visibility = Visibility.Visible;
        }

        /// <summary>
        /// Wechselt zurück zum Idle-Zustand.
        /// </summary>
        public void ShowIdle()
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(ShowIdle));
                return;
            }

            _idleTimer.Stop();
            panelLoader.Visibility = Visibility.Collapsed;
            borderCheckinInfo.Visibility = Visibility.Collapsed;
            borderCheckinAvatar.Visibility = Visibility.Collapsed;
            imgCheckinAvatar.Source = null;
            iconUserFallback.Visibility = Visibility.Visible;
            iconErrorFallback.Visibility = Visibility.Collapsed;
            iconWarnFallback.Visibility = Visibility.Collapsed;

            // Overlay-Icons ausblenden
            overlaySuccess.Visibility = Visibility.Collapsed;
            overlayError.Visibility = Visibility.Collapsed;
            overlayWarning.Visibility = Visibility.Collapsed;
            overlayDoubleScan.Visibility = Visibility.Collapsed;

            txtCheckinName.Text = "";
            txtCheckinStatus.Text = "";
            txtCheckinReaderName.Text = "";
            txtIdleHint.Visibility = Visibility.Visible;
        }

        private void IdleTimer_Tick(object sender, EventArgs e)
        {
            ShowIdle();
        }

        private readonly Dictionary<string, bool> _deviceAvailability = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Updates device availability status and shows/hides the warning panel.
        /// Called from MainWindow hotplug monitor.
        /// </summary>
        public void UpdateDeviceAvailability(string deviceName, string inputType, bool isAvailable)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action<string, string, bool>(UpdateDeviceAvailability), deviceName, inputType, isAvailable);
                return;
            }

            _deviceAvailability[deviceName] = isAvailable;
            RebuildDeviceWarningPanel();
        }

        private void RebuildDeviceWarningPanel()
        {
            panelDeviceWarnings.Children.Clear();
            var unavailable = _deviceAvailability.Where(kv => !kv.Value).Select(kv => kv.Key).ToList();

            if (unavailable.Count == 0)
            {
                borderDeviceWarning.Visibility = Visibility.Collapsed;
                return;
            }

            foreach (var name in unavailable)
            {
                var sp = new System.Windows.Controls.StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
                sp.Children.Add(new System.Windows.Controls.TextBlock { Text = "⚠", Foreground = new SolidColorBrush(Color.FromRgb(0xF4, 0x43, 0x36)), FontSize = 16, Margin = new Thickness(0, 0, 8, 0) });
                sp.Children.Add(new System.Windows.Controls.TextBlock { Text = $"{string.Format(L.T("Welcome_DeviceUnavailable"), name)}", Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xCC, 0xCC)), FontSize = 13 });
                panelDeviceWarnings.Children.Add(sp);
            }

            borderDeviceWarning.Visibility = Visibility.Visible;
        }

        /// <summary>
        /// Aktiviert oder deaktiviert die QR-Code-Kamera-Vorschau auf dem Welcome Screen.
        /// Im Debug-Modus ist die Vorschau immer sichtbar.
        /// </summary>
        public void SetQrCameraPreviewEnabled(bool enabled)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action<bool>(SetQrCameraPreviewEnabled), enabled);
                return;
            }

            borderQrCameraPreview.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>
        /// Initializes the QR camera preview tiles from the configured mappings.
        /// </summary>
        public void SetQrCameraMappings(IEnumerable<CheckinClientMapping> mappings)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action<IEnumerable<CheckinClientMapping>>(SetQrCameraMappings), mappings);
                return;
            }

            _qrCameraPreviewTiles.Clear();
            _qrCameraPreviewByLabel.Clear();

            if (mappings == null)
                return;

            foreach (var mapping in mappings
                .Where(m => string.Equals(m.InputType, CheckinClientMapping.InputTypeQrCode, StringComparison.OrdinalIgnoreCase))
                .OrderBy(m => m.CameraIndex))
            {
                var label = !string.IsNullOrWhiteSpace(mapping.Name) ? mapping.Name : string.Format(L.T("Welcome_Camera_FallbackLabel"), mapping.CameraIndex);
                var tile = new QrCameraPreviewTile
                {
                    Label = label,
                    Status = L.T("Welcome_Camera_WaitingForCamera")
                };
                _qrCameraPreviewTiles.Add(tile);
                _qrCameraPreviewByLabel[label] = tile;
            }
        }

        /// <summary>
        /// Aktualisiert das Kamera-Vorschaubild auf dem Welcome Screen.
        /// Wird vom QrCodeScanner bei jedem neuen Frame aufgerufen.
        /// </summary>
        /// <param name="frameData">Das aktuelle Kamerabild als JPEG-Bytes.</param>
        /// <param name="mappingName">Name des Mappings / der Kamera (wird als Label angezeigt).</param>
        public void UpdateQrCameraPreview(byte[] frameData, string mappingName = null)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action<byte[], string>(UpdateQrCameraPreview), frameData, mappingName);
                return;
            }

            try
            {
                if (borderQrCameraPreview.Visibility != Visibility.Visible)
                    return;

                if (string.IsNullOrWhiteSpace(mappingName))
                    return;

                if (!_qrCameraPreviewByLabel.TryGetValue(mappingName, out var tile))
                    return;

                var bitmapImage = new BitmapImage();
                using (var ms = new System.IO.MemoryStream(frameData))
                {
                    bitmapImage.BeginInit();
                    bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
                    bitmapImage.StreamSource = ms;
                    bitmapImage.EndInit();
                }
                bitmapImage.Freeze();
                tile.PreviewImage = bitmapImage;
                tile.Status = "Vorschau aktiv";
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"UpdateQrCameraPreview Fehler: {ex.Message}");
            }
        }

        private sealed class QrCameraPreviewTile : System.ComponentModel.INotifyPropertyChanged
        {
            private string _label = "";
            private ImageSource _previewImage;
            private string _status = "";

            public string Label
            {
                get => _label;
                set { _label = value; OnPropertyChanged(nameof(Label)); }
            }

            public ImageSource PreviewImage
            {
                get => _previewImage;
                set { _previewImage = value; OnPropertyChanged(nameof(PreviewImage)); }
            }

            public string Status
            {
                get => _status;
                set { _status = value; OnPropertyChanged(nameof(Status)); }
            }

            public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;
            private void OnPropertyChanged(string propertyName)
            {
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(propertyName));
            }
        }

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);

        protected override void OnClosed(EventArgs e)
        {
            _idleTimer.Stop();
            _foregroundTimer.Stop();
            _backgroundTimer.Stop();
            base.OnClosed(e);
        }
    }
}
