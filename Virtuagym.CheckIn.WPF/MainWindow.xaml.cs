using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Virtuagym.API.Serialization;
using System.Windows;
using System.Windows.Threading;
using Virtuagym.CheckIn.WPF.Services;
using Virtuagym.API.Services;
using Virtuagym.API.Cache;
using Hardware.Events;
using Hardware.Models;
using Hardware.Services;
using Virtuagym.CheckIn.WPF.Properties;
using Virtuagym.CheckIn.Core.Abstractions;
using Virtuagym.CheckIn.Core.Helper;
using Virtuagym.CheckIn.Core.Services;
using Virtuagym.CheckIn.Core.Models;
using AccessPass.Services;

namespace Virtuagym.CheckIn.WPF
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window, ILogWriter
    {
        private List<HidCardReader> m_rfidReaderVirtuagym;
        private List<QrCodeScanner> m_qrCodeScanners;
        private List<CcidSmartCardReader> m_ccidReaders;
        // Track active mappings for diff-based hotplug
        private List<CheckinClientMapping> m_activeMappings = new();
        private DispatcherTimer? m_hotplugTimer;
        private System.Threading.Timer? m_accessPassCleanupTimer;
        private WelcomeWindow m_welcomeWindow;
        private MemberCacheService m_memberCache;
        private CacheSyncScheduler m_cacheSyncScheduler;
        private readonly BackgroundLogWriter m_logWriter = new();
        private readonly ISoundPlayer m_soundPlayer;
        private readonly IAppSettings m_appSettings;
        private readonly IVirtuagymApiServiceFactory m_apiFactory;
        private readonly AccessPassStore m_accessPassStore;
        private readonly IAccessPassService m_accessPassService;
        private WindowState m_storedWindowState = WindowState.Normal;
        private System.Windows.Forms.NotifyIcon m_notifyIcon;

        /// <summary>
        /// Member-Cache-Referenz die an CheckinHandler ├╝bergeben wird.
        /// Null wenn MemberCacheEnabled = false (Cache wird dann nicht f├╝r Lookup/Offline-Fallback verwendet).
        /// </summary>
        private MemberCacheService m_checkinCache;

        /// <summary>
        /// Flag das anzeigt, dass die Anwendung gerade neu gestartet wird (keine Schlie├ƒen-Best├ñtigung n├╢tig).
        /// </summary>
        public bool IsRestarting { get; set; }


        public MainWindow()
        {
            InitializeComponent();

            // Initialize Core globals from settings
            CheckinClientMapping.GlobalDefaultDoubleScanThresholdMs = Settings.Default.DefaultDoubleScanThresholdMs;
            CheckinClientMapping.GlobalDefaultDuplicateTimeoutSeconds = Settings.Default.DuplicateTimeoutSeconds;
            m_soundPlayer = new WpfSoundPlayer(this);
            m_appSettings = new WpfAppSettings();
            m_apiFactory = new WpfVirtuagymApiServiceFactory();
            m_accessPassStore = new AccessPassStore();
            m_accessPassService = new AccessPassService(m_accessPassStore);

            m_rfidReaderVirtuagym = new List<HidCardReader>();
            m_qrCodeScanners = new List<QrCodeScanner>();
            m_ccidReaders = new List<CcidSmartCardReader>();
            Environment.CurrentDirectory = System.IO.Path.GetDirectoryName(System.Windows.Forms.Application.ExecutablePath);

            m_notifyIcon = new System.Windows.Forms.NotifyIcon();
            m_notifyIcon.BalloonTipText = Constants.BalloonTipText;
            m_notifyIcon.BalloonTipTitle = Constants.AppTitle;
            m_notifyIcon.Text = Constants.AppTitle;
            if (System.IO.File.Exists(Constants.IconPath)) m_notifyIcon.Icon = new System.Drawing.Icon(Constants.IconPath);
            m_notifyIcon.DoubleClick += new EventHandler(notifyIcon_DoubleClick);

            // Kontextmenü erstellen
            var contextMenu = new System.Windows.Forms.ContextMenuStrip();
            var menuItemMainWindow = new System.Windows.Forms.ToolStripMenuItem(L.T("Main_Tray_ShowMainWindow"));
            menuItemMainWindow.Click += (s, args) => ShowMainWindow();
            var menuItemSettings = new System.Windows.Forms.ToolStripMenuItem(L.T("Main_Tray_Settings"));
            menuItemSettings.Click += (s, args) => ShowSettingsWindow();
            var menuItemExit = new System.Windows.Forms.ToolStripMenuItem(L.T("Main_Tray_Exit"));
            menuItemExit.Click += (s, args) => Close();

            contextMenu.Items.Add(menuItemMainWindow);
            contextMenu.Items.Add(menuItemSettings);
            contextMenu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
            contextMenu.Items.Add(menuItemExit);
            m_notifyIcon.ContextMenuStrip = contextMenu;
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // Welcome Screen erstellen (falls aktiviert)
            if (Settings.Default.WelcomeScreenEnabled)
            {
                m_welcomeWindow = new WelcomeWindow(Settings.Default.WelcomeScreenMonitor);
                m_welcomeWindow.Show();
                WriteToLog($"Welcome Screen {L.T("Log_TypeSuccess")} (Monitor {Settings.Default.WelcomeScreenMonitor + 1})", Constants.LogSuccess);

                // Club-Logo aus den Einstellungen setzen
                if (Settings.Default.ShowClubLogo && !string.IsNullOrWhiteSpace(Settings.Default.ClubLogoPath))
                {
                    m_welcomeWindow.SetClubLogo(Settings.Default.ClubLogoPath);
                    WriteToLog(L.T("Log_ClubLogoLoaded") + ": " + Settings.Default.ClubLogoPath, Constants.LogInfo);
                }
            }

            // Access Pass Autodelete (periodisch, wie im Web-Projekt)
            StartAccessPassCleanupTimer();

            // Checkin-Client-Mappings laden
            var mappings = LoadCheckinClientMappings();
            if (m_welcomeWindow != null)
            {
                // Initialize QR camera preview tiles based on mappings
                m_welcomeWindow.SetQrCameraMappings(mappings);
            }


            // Member-Cache initialisieren (wird intern immer erstellt f├╝r AutoCheckout-Tracking)
            try
            {
                m_memberCache = new MemberCacheService();

                if (Settings.Default.ClearTransientMemberDataOnStartup)
                {
                    int clearedMembers = m_memberCache.ClearServiceCreditsAndDeviceCheckins();
                    WriteToLog(L.T("Log_TransientDataCleared") + " (" + clearedMembers + " " + L.T("Log_Entries") + ").", Constants.LogInfo);
                }

                // m_checkinCache wird an CheckinHandler ├╝bergeben.
                // Bei MemberCacheEnabled=false ist m_checkinCache null ΓåÆ kein Cache-Lookup,
                // kein Offline-Fallback, kein Duplikat-Schutz via Cache.
                m_checkinCache = Settings.Default.MemberCacheEnabled ? m_memberCache : null;

                if (!Settings.Default.MemberCacheEnabled)
                    WriteToLog(L.T("Log_MemberCacheDisabled"), Constants.LogInfo);
            }
            catch (Exception ex)
            {
                WriteToLog(L.T("Log_MemberCacheInitError") + ": " + ex.Message, Constants.LogWarning);
                m_memberCache = null;
            }

            // CacheSyncScheduler immer erstellen er wird für Auto-Checkout benötigt,
            // auch wenn die periodische Cache-Synchronisation deaktiviert ist.
            // _cacheService kann null sein wenn MemberCacheEnabled=false ΓÇô
            // AutoCheckout funktioniert dann rein API-basiert.
            try
            {
                m_cacheSyncScheduler = new CacheSyncScheduler(m_checkinCache,
                    () => VirtuagymApiServiceFactory.Create(),
                    checkinKey => VirtuagymApiServiceFactory.CreateWithClubSecret(checkinKey),
                    mappings?.Where(m => !string.IsNullOrWhiteSpace(m.CheckinKey)).Select(m => m.CheckinKey).ToList());

                m_cacheSyncScheduler.Log += msg => Dispatcher.BeginInvoke(new Action(() => WriteToLog(msg)));
                m_cacheSyncScheduler.SyncStarted += () => Dispatcher.BeginInvoke(new Action(() =>
                    pgbSyncIndicator.Visibility = Visibility.Visible));
                m_cacheSyncScheduler.SyncFinished += (success, msg) => Dispatcher.BeginInvoke(new Action(() =>
                    pgbSyncIndicator.Visibility = Visibility.Collapsed));

                // Auto-Checkout-Konfiguration aus Mappings ├╝bernehmen (unabh├ñngig von CacheSync)
                if (mappings != null)
                {
                    var autoCheckoutDevices = mappings
                        .Where(m => m.AutoCheckoutMinutes > 0 && !string.IsNullOrWhiteSpace(m.CheckinKey))
                        .Select(m => new CacheSyncScheduler.AutoCheckoutDevice
                        {
                            DeviceId = m.EffectiveDeviceId,
                            CheckinKey = m.CheckinKey,
                            AutoCheckoutMinutes = m.AutoCheckoutMinutes,
                            ApiVersion = m.ApiVersion
                        })
                        .ToList();
                    if (autoCheckoutDevices.Count > 0)
                    {
                        m_cacheSyncScheduler.SetAutoCheckoutDevices(autoCheckoutDevices);
                        m_cacheSyncScheduler.StartAutoCheckout();
                    }
                }

                // Periodische Cache-Synchronisation nur starten, wenn aktiviert
                if (Settings.Default.CacheSyncEnabled)
                {
                    string syncMode = (Settings.Default.CacheSyncMode ?? "").Trim();
                    int cacheCount = m_memberCache.GetCount();

                    if (string.Equals(syncMode, "DailyTime", StringComparison.OrdinalIgnoreCase))
                    {
                        string dailyTime = Settings.Default.CacheSyncDailyTime ?? "02:00";
                        m_cacheSyncScheduler.StartDaily(dailyTime);
                        WriteToLog(L.T("Log_MemberCacheInitialized") + " (" + cacheCount + " " + L.T("Log_Entries") + "). " + L.T("Log_SyncMode") + ": " + L.T("Log_SyncModeDailyTime") + " " + dailyTime, Constants.LogInfo);
                    }
                    else
                    {
                        m_cacheSyncScheduler.Start(Settings.Default.CacheSyncIntervalMinutes);
                        WriteToLog(L.T("Log_MemberCacheInitialized") + " (" + cacheCount + " " + L.T("Log_Entries") + "). " + L.T("Log_Interval") + ": " + Settings.Default.CacheSyncIntervalMinutes + " " + L.T("Log_CacheSyncIntervalMin"), Constants.LogInfo);
                    }
                }
            }
            catch (Exception ex)
            {
                WriteToLog(L.T("Log_MemberCacheInitError") + ": " + ex.Message, Constants.LogWarning);
            }

            if (mappings != null && mappings.Count > 0)
            {
                // Sicherstellen, dass jedes Mapping eine UUID hat (Rückwärtskompatibilität)
                bool uuidsGenerated = false;
                foreach (var mapping in mappings)
                {
                    if (string.IsNullOrWhiteSpace(mapping.Uuid))
                    {
                        mapping.EnsureUuid();
                        uuidsGenerated = true;
                    }
                }
                if (uuidsGenerated)
                {
                    SaveCheckinClientMappings(mappings);
                }

                var hwLogger = new HardwareLoggerAdapter(this);

                foreach (var mapping in mappings)
                {
                    if (string.IsNullOrWhiteSpace(mapping.CheckinKey))
                        continue;

                    if (mapping.InputType == CheckinClientMapping.InputTypeQrCode)
                    {
                        // Backend aus Mapping parsen (Standard: ANY)
                        if (!Enum.TryParse<OpenCvSharp.VideoCaptureAPIs>(mapping.CameraBackend, out var captureApi))
                            captureApi = OpenCvSharp.VideoCaptureAPIs.ANY;

                        var scanner = new QrCodeScanner(mapping.Uuid, hwLogger, mapping.CameraIndex, mapping.Name,
                            Settings.Default.DuplicateTimeoutSeconds, Settings.Default.DebugMode,
                            mapping.CameraResolutionWidth, mapping.CameraResolutionHeight, captureApi);
                        scanner.QrCodeRead += (sender2, args) => OnQrCodeRead(args, mapping);
                        if (m_welcomeWindow != null)
                            scanner.CameraPreview += (sender2, args) => m_welcomeWindow.UpdateQrCameraPreview(args.FrameData, args.CameraLabel);
                        m_qrCodeScanners.Add(scanner);
                        WriteToLog(L.T("Log_QrScannerLoadedInfo") + ": " + (mapping.Name ?? L.T("Log_Camera") + " " + mapping.CameraIndex), Constants.LogInfo);
                    }
                    else if (mapping.InputType == CheckinClientMapping.InputTypeCcid)
                    {
                        if (!string.IsNullOrWhiteSpace(mapping.DeviceID))
                        {
                            var ccid = new CcidSmartCardReader(mapping.Uuid, hwLogger, mapping.DeviceID, mapping.Name,
                                Settings.Default.DuplicateTimeoutSeconds, Settings.Default.DebugMode);
                            ccid.CardRead += (sender2, args) => OnCardRead(args, mapping);
                            m_ccidReaders.Add(ccid);
                            WriteToLog(L.T("Log_CcidReaderLoadedInfo") + ": " + (mapping.Name ?? mapping.DeviceID), Constants.LogInfo);
                        }
                    }
                    else if (!string.IsNullOrWhiteSpace(mapping.DeviceID))
                    {
                        var reader = new HidCardReader(mapping.Uuid, hwLogger, mapping.DeviceID, mapping.HidProfile,
                            mapping.RepeatTimeMs ?? Settings.Default.RepaitTimeInMs, mapping.Name,
                            Settings.Default.DuplicateTimeoutSeconds, Settings.Default.DebugMode);
                        reader.CardRead += (sender2, args) => OnCardRead(args, mapping);
                        reader.DeviceConnectionChanged += (isConnected) =>
                        {
                            Dispatcher.Invoke(() =>
                            {
                                    var deviceName = mapping.Name ?? mapping.DeviceID ?? "HID";
                                        WriteToLog(isConnected
                                            ? string.Format(L.T("Log_DeviceReconnected"), deviceName, mapping.InputType)
                                            : string.Format(L.T("Log_DeviceDisconnected"), deviceName, mapping.InputType),
                                            isConnected ? Constants.LogInfo : Constants.LogWarning);
                                        m_welcomeWindow?.UpdateDeviceAvailability(deviceName, mapping.InputType, isConnected);
                                    });
                                };

                                // Initialize with actual current state.
                        m_welcomeWindow?.UpdateDeviceAvailability(mapping.Name ?? mapping.DeviceID ?? "HID", mapping.InputType, reader.IsConnected);
                        m_rfidReaderVirtuagym.Add(reader);
                        WriteToLog(L.T("Log_UsbReaderLoadedInfo") + ": " + (mapping.Name ?? mapping.DeviceID), Constants.LogInfo);
                    }
                }

                m_activeMappings = mappings.Where(m => !string.IsNullOrWhiteSpace(m.CheckinKey)).ToList();

                // Idle-Hint-Text basierend auf den konfigurierten Eingabetypen setzen
                if (m_welcomeWindow != null)
                {
                    bool hasRfid = m_rfidReaderVirtuagym.Count > 0 || m_ccidReaders.Count > 0;
                    bool hasQrCode = m_qrCodeScanners.Count > 0;
                    m_welcomeWindow.SetIdleHintText(hasRfid, hasQrCode);

                    // QR-Kamera-Vorschau nur aktivieren, wenn QR-Scanner vorhanden,
                    // die Einstellung aktiv ist und mindestens ein Kamerager├ñt verf├╝gbar ist
                    if (hasQrCode)
                    {
                        bool hasCameraDevice = (await CameraDiscoveryService.GetCamerasAsync()).Count > 0;
                        m_welcomeWindow.SetQrCameraPreviewEnabled(
                            Settings.Default.ShowQrCameraPreviewOnWelcome && hasCameraDevice);
                    }
                }
            }
            else
            {
                WriteToLog(L.T("Log_NoCheckinMappingsConfigured") + ".",  Constants.LogError);
            }

            StartHotplugMonitor();
        }

        private void StartHotplugMonitor()
        {
            int seconds = Settings.Default.DeviceHotplugPollIntervalSeconds;
            if (seconds <= 0)
                return;
            
            seconds = Math.Clamp(seconds, 5, 300);

            m_hotplugTimer?.Stop();
            m_hotplugTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(seconds)
            };

            m_hotplugTimer.Tick += async (_, _) =>
            {
                try
                {
                    CameraDiscoveryService.InvalidateCache();
                    var mappings = LoadCheckinClientMappings() ?? new List<CheckinClientMapping>();
                    var filteredNew = mappings.Where(m => !string.IsNullOrWhiteSpace(m.CheckinKey)).ToList();
                    var filteredOld = m_activeMappings.Where(m => !string.IsNullOrWhiteSpace(m.CheckinKey)).ToList();

                    // Find removed and added mappings by DeviceID/CameraIndex/InputType
                    var removed = filteredOld.Where(old => !filteredNew.Any(n => MappingEquals(n, old))).ToList();
                    var added = filteredNew.Where(n => !filteredOld.Any(old => MappingEquals(n, old))).ToList();

                    // Stop removed devices
                    foreach (var mapping in removed)
                    {
                        StopDevice(mapping);
                        var deviceName = mapping.Name ?? mapping.DeviceID ?? $"Kamera {mapping.CameraIndex}";
                        WriteToLog($"Ger\u00e4t nicht mehr verf\u00fcgbar: {deviceName} ({mapping.InputType})", Constants.LogWarning);
                        m_welcomeWindow?.UpdateDeviceAvailability(deviceName, mapping.InputType, false);
                    }

                    // Start added devices
                    foreach (var mapping in added)
                    {
                        await StartDeviceAsync(mapping);
                        var deviceName = mapping.Name ?? mapping.DeviceID ?? string.Format(L.T("Welcome_Camera_FallbackLabel"), mapping.CameraIndex);
                        WriteToLog(string.Format(L.T("Log_HotplugDeviceAvailable"), deviceName, mapping.InputType), Constants.LogInfo);
                        m_welcomeWindow?.UpdateDeviceAvailability(deviceName, mapping.InputType, true);
                    }

                    m_activeMappings = filteredNew;

                    int total = m_activeMappings.Count;
                    if(Settings.Default.DebugMode)
                        WriteToLog(string.Format(L.T("Log_HotplugDevicesActive"), total), Constants.LogInfo);
                }
                catch (Exception ex)
                {
                    WriteToLog(string.Format(L.T("Log_HotplugMonitorError"), ex.Message), Constants.LogWarning);
                }
            };

            m_hotplugTimer.Start();
            WriteToLog(string.Format(L.T("Log_HotplugMonitorActive"), seconds), Constants.LogInfo);
        }

        // Helper to compare mappings by unique device identity
        private static bool MappingEquals(CheckinClientMapping a, CheckinClientMapping b)
        {
            if (a.InputType != b.InputType) return false;
            if (a.InputType == CheckinClientMapping.InputTypeQrCode)
                return a.CameraIndex == b.CameraIndex && a.CameraBackend == b.CameraBackend;
            return a.DeviceID == b.DeviceID && a.HidProfile == b.HidProfile;
        }

        // Helper to stop a device by mapping
        private void StopDevice(CheckinClientMapping mapping)
        {
            if (mapping.InputType == CheckinClientMapping.InputTypeQrCode)
            {
                var scanner = m_qrCodeScanners.FirstOrDefault(s => s.MappingUuid == mapping.Uuid);
                if (scanner != null)
                {
                    scanner.Dispose();
                    m_qrCodeScanners.Remove(scanner);
                    WriteToLog(string.Format(L.T("Log_QrScannerStopped"), mapping.Name ?? string.Format(L.T("Welcome_Camera_FallbackLabel"), mapping.CameraIndex)), Constants.LogInfo);
                }
            }
            else if (mapping.InputType == CheckinClientMapping.InputTypeCcid)
            {
                var ccid = m_ccidReaders.FirstOrDefault(r => r.MappingUuid == mapping.Uuid);
                if (ccid != null)
                {
                    ccid.Dispose();
                    m_ccidReaders.Remove(ccid);
                    WriteToLog(string.Format(L.T("Log_CcidReaderStopped"), mapping.Name ?? mapping.DeviceID), Constants.LogInfo);
                }
            }
            else
            {
                var reader = m_rfidReaderVirtuagym.FirstOrDefault(r => r.MappingUuid == mapping.Uuid);
                if (reader != null)
                {
                    reader.Dispose();
                    m_rfidReaderVirtuagym.Remove(reader);
                    WriteToLog(string.Format(L.T("Log_UsbReaderStopped"), mapping.Name ?? mapping.DeviceID), Constants.LogInfo);
                }
            }
        }

        // Helper to start a device by mapping
        private async Task StartDeviceAsync(CheckinClientMapping mapping)
        {
            var hwLogger = new HardwareLoggerAdapter(this);
            try
            {
                if (mapping.InputType == CheckinClientMapping.InputTypeQrCode)
                {
                    if (!Enum.TryParse<OpenCvSharp.VideoCaptureAPIs>(mapping.CameraBackend, out var captureApi))
                        captureApi = OpenCvSharp.VideoCaptureAPIs.ANY;

                    var scanner = new QrCodeScanner(mapping.Uuid, hwLogger, mapping.CameraIndex, mapping.Name,
                        Settings.Default.DuplicateTimeoutSeconds, Settings.Default.DebugMode,
                        mapping.CameraResolutionWidth, mapping.CameraResolutionHeight, captureApi);
                    scanner.QrCodeRead += (sender2, args) => OnQrCodeRead(args, mapping);
                    if (m_welcomeWindow != null)
                        scanner.CameraPreview += (sender2, args) => m_welcomeWindow.UpdateQrCameraPreview(args.FrameData, args.CameraLabel);
                    m_qrCodeScanners.Add(scanner);
                    WriteToLog(string.Format(L.T("Log_QrScannerStarted"), mapping.Name ?? string.Format(L.T("Welcome_Camera_FallbackLabel"), mapping.CameraIndex)), Constants.LogInfo);
                }
                else if (mapping.InputType == CheckinClientMapping.InputTypeCcid)
                {
                    if (!string.IsNullOrWhiteSpace(mapping.DeviceID))
                    {
                        var ccid = new CcidSmartCardReader(mapping.Uuid, hwLogger, mapping.DeviceID, mapping.Name,
                            Settings.Default.DuplicateTimeoutSeconds, Settings.Default.DebugMode);
                        ccid.CardRead += (sender2, args) => OnCardRead(args, mapping);
                        m_ccidReaders.Add(ccid);
                        WriteToLog(string.Format(L.T("Log_CcidReaderStarted"), mapping.Name ?? mapping.DeviceID), Constants.LogInfo);
                    }
                }
                else if (!string.IsNullOrWhiteSpace(mapping.DeviceID))
                {
                    var reader = new HidCardReader(mapping.Uuid, hwLogger, mapping.DeviceID, mapping.HidProfile,
                        mapping.RepeatTimeMs ?? Settings.Default.RepaitTimeInMs, mapping.Name,
                        Settings.Default.DuplicateTimeoutSeconds, Settings.Default.DebugMode);
                    reader.CardRead += (sender2, args) => OnCardRead(args, mapping);
                    m_rfidReaderVirtuagym.Add(reader);
                    WriteToLog(string.Format(L.T("Log_UsbReaderStarted"), mapping.Name ?? mapping.DeviceID), Constants.LogInfo);
                }
            }
            catch (Exception ex)
            {
                WriteToLog(string.Format(L.T("Log_DeviceStartError"), mapping.Name ?? mapping.DeviceID ?? "?", ex.Message), Constants.LogError);
            }
        }

        private void DisposeHardwareDevices()
        {
            for (int i = 0; i < m_rfidReaderVirtuagym.Count; i++)
                m_rfidReaderVirtuagym[i].Dispose();
            m_rfidReaderVirtuagym.Clear();

            foreach (var scanner in m_qrCodeScanners)
                scanner.Dispose();
            m_qrCodeScanners.Clear();

            foreach (var ccid in m_ccidReaders)
                ccid.Dispose();
            m_ccidReaders.Clear();
        }

        /// <summary>
        /// L├ñdt die Checkin-Client-Mappings aus den gespeicherten Einstellungen (JSON).
        /// </summary>
        private List<CheckinClientMapping> LoadCheckinClientMappings()
        {
            try
            {
                string json = Settings.Default.CheckinClientMappings;
                if (!string.IsNullOrWhiteSpace(json))
                {
                    var serializer = new JsonSerializerAdapter();
                    return serializer.Deserialize<List<CheckinClientMapping>>(json);
                }
            }
            catch (Exception ex)
            {
                WriteToLog(L.T("Log_CheckinMappingsLoadError") + ": " + ex.Message, Constants.LogError);
            }
            return null;
        }

        /// <summary>
        /// Speichert die Checkin-Client-Mappings in der Config-Datei (XML).
        /// Wird aufgerufen, wenn z.B. UUIDs nachtr├ñglich generiert wurden.
        /// </summary>
        private void SaveCheckinClientMappings(List<CheckinClientMapping> mappings)
        {
            try
            {
                var serializer = new JsonSerializerAdapter();
                string json = serializer.Serialize(mappings);

                string configPath = System.Windows.Forms.Application.ExecutablePath + ".config";
                var doc = new System.Xml.XmlDocument();
                doc.Load(configPath);

                var settingsNode = doc.SelectSingleNode("//applicationSettings/VirtuagymMemberCheckIn.Properties.Settings");
                if (settingsNode != null)
                {
                    var valueNode = settingsNode.SelectSingleNode("setting[@name='CheckinClientMappings']/value");
                    if (valueNode != null)
                    {
                        valueNode.InnerText = json;
                        doc.Save(configPath);
                    }
                }
            }
            catch (Exception ex)
            {
                WriteToLog(L.T("Log_CheckinMappingsLoadError") + ": " + ex.Message, Constants.LogError);
            }
        }

        #region Hardware Event Handlers

        /// <summary>
        /// Wird aufgerufen wenn ein RFID-/CCID-Reader eine Karte gelesen hat.
        /// Erstellt einen CheckinHandler und f├╝hrt den Check-in durch.
        /// </summary>
        private void OnCardRead(CardReadEventArgs e, CheckinClientMapping mapping)
        {
            Dispatcher.BeginInvoke(new Action(async () =>
            {
                try
                {
                    m_welcomeWindow?.ShowLoader();
                    IWelcomeDisplay? display = m_welcomeWindow != null ? new WpfWelcomeDisplay(m_welcomeWindow) : null;
                    var handler = new CheckinHandler(this, mapping, display, m_appSettings, m_soundPlayer, m_apiFactory, m_checkinCache, m_accessPassService);
                    await handler.PerformCheckinAsync(e.Card, e.ReaderName);
                }
                catch (Exception ex)
                {
                    WriteToLog($"Checkin error: {ex.Message}", Constants.LogError);
                }
            }));
        }

        /// <summary>
        /// Wird aufgerufen wenn ein QR-Scanner einen Code gelesen hat.
        /// Erstellt einen CheckinHandler und f├╝hrt den Check-in durch.
        /// </summary>
        private void OnQrCodeRead(QrCodeReadEventArgs e, CheckinClientMapping mapping)
        {
            Dispatcher.BeginInvoke(new Action(async () =>
            {
                try
                {
                    m_welcomeWindow?.ShowLoader();
                    IWelcomeDisplay? display = m_welcomeWindow != null ? new WpfWelcomeDisplay(m_welcomeWindow) : null;
                    var handler = new CheckinHandler(this, mapping, display, m_appSettings, m_soundPlayer, m_apiFactory, m_checkinCache, m_accessPassService);
                    await handler.PerformCheckinAsync(e.QrCode, e.ReaderName);
                }
                catch (Exception ex)
                {
                    WriteToLog($"QR-Checkin error: {ex.Message}", Constants.LogError);
                }
            }));
        }

        #endregion

        void OnClose(object sender, CancelEventArgs args)
        {
            if (!IsRestarting)
            {
                MessageBoxResult res = MessageBox.Show(L.T("Main_Msg_CloseConfirm"), L.T("Main_Msg_CloseTitle"), MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (res == MessageBoxResult.No)
                {
                    args.Cancel = true;
                    return;
                }
            }

            m_hotplugTimer?.Stop();
            m_hotplugTimer = null;
            m_accessPassCleanupTimer?.Dispose();
            m_accessPassCleanupTimer = null;

            DisposeHardwareDevices();

            m_cacheSyncScheduler?.Dispose();
            m_memberCache?.Dispose();
            m_logWriter.Dispose();

            if (m_welcomeWindow != null)
            {
                m_welcomeWindow.Close();
                m_welcomeWindow = null;
            }

            m_notifyIcon.Dispose();
            m_notifyIcon = null;
        }

        void OnStateChanged(object sender, EventArgs args)
        {
            if (WindowState == WindowState.Minimized)
            {
                Hide();
                if (m_notifyIcon != null)
                    m_notifyIcon.ShowBalloonTip(Constants.BalloonTipTimeoutMs);
            }
            else
                m_storedWindowState = WindowState;
        }
        
        void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs args)
        {
            CheckTrayIcon();
        }

        void notifyIcon_DoubleClick(object sender, EventArgs e)
        {
            ShowMainWindow();
        }

        private void MenuItemSettings_Click(object sender, RoutedEventArgs e)
        {
            ShowSettingsWindow();
        }

        private void MenuItemExit_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void ShowMainWindow()
        {
            Show();
            WindowState = m_storedWindowState;
            Activate();
        }

        private void ShowSettingsWindow()
        {
            var settingsWindow = new SettingsWindow(m_accessPassStore, m_accessPassService);
            settingsWindow.Owner = this;
            settingsWindow.SetCardReaders(m_ccidReaders, m_rfidReaderVirtuagym);
            settingsWindow.ShowDialog();
        }

        void CheckTrayIcon()
        {
            ShowTrayIcon(!IsVisible);
        }

        void ShowTrayIcon(bool show)
        {
            if (m_notifyIcon != null)
                m_notifyIcon.Visible = show;
        }

        /// <summary>
        /// Write to ListView 
        /// Write File if type is 3 (Error)
        /// </summary>
        /// <param name="text"></param>
        /// <param name="type">1=Info|2=Warning|3=Error|4=Success</param>
        /// <param name="clear"></param>
        public void WriteToLog(string text, int type = Constants.LogInfo)
        {
            string logFilePath = System.IO.Path.Combine(Constants.LogFolder, DateTime.Now.ToString(Constants.LogDateFormat) + Constants.LogFileSuffix);
            string logText = DateTime.Now.ToString(Constants.LogTimestampFormat) + " # ";
            try
            {
                // Typ-Prefix vorab ermitteln (thread-safe, kein UI-Zugriff)
                string sType = L.T("Log_TypeInfo") + ": ";
                System.Windows.Media.Brush textColor = System.Windows.Media.Brushes.Black;
                if (type == Constants.LogWarning)
                {
                    sType = L.T("Log_TypeWarning") + ": ";
                    textColor = System.Windows.Media.Brushes.Orange;
                }
                else if (type == Constants.LogError)
                {
                    sType = L.T("Log_TypeError") + ": ";
                    textColor = System.Windows.Media.Brushes.Red;
                }
                else if (type == Constants.LogSuccess)
                {
                    sType = L.T("Log_TypeSuccess") + ": ";
                    textColor = System.Windows.Media.Brushes.Green;
                }

                logText += sType + text;

                // Datei-I/O ├╝ber Hintergrund-Queue (blockiert weder Aufrufer noch UI-Thread)
                if (type == Constants.LogError)
                {
                    m_logWriter.Enqueue(logText, logFilePath);
                }

                // UI-Aktualisierung via Dispatcher (nur ListView, kein I/O)
                var app = Application.Current;
                if (app == null) return;
                string capturedLogText = logText;
                app.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => {

                    if (listViewLogEntries.Items.Count > Constants.MaxLogEntries)
                        listViewLogEntries.Items.Clear();

                    listViewLogEntries.Items.Add(new System.Windows.Controls.ListViewItem()
                    {
                        Foreground = textColor,
                        Content = capturedLogText,
                    });
                }));
            }
            catch(Exception ex)
            {
                logText += L.T("Log_ErrorOnWrite") + " " + ex.Message;
                m_logWriter.Enqueue(logText, logFilePath);
            }
        }

        private void StartAccessPassCleanupTimer()
        {
            m_accessPassCleanupTimer?.Dispose();
            m_accessPassCleanupTimer = null;

            var mode = (AccessPass.Models.AccessPassDeletionMode)Settings.Default.AccessPassDeletionMode;
            int days = Settings.Default.AccessPassDeletionDays;

            if (mode == AccessPass.Models.AccessPassDeletionMode.AfterXDays && days > 0)
            {
                m_accessPassCleanupTimer = new System.Threading.Timer(_ => RunAccessPassCleanup(),
                    null, TimeSpan.FromHours(24), TimeSpan.FromHours(24));
                WriteToLog(string.Format(L.T("Log_AccessPassAutodeleteEnabled"), days), Constants.LogInfo);
            }
        }

        private void RunAccessPassCleanup()
        {
            try
            {
                var mode = (AccessPass.Models.AccessPassDeletionMode)Settings.Default.AccessPassDeletionMode;
                int days = Settings.Default.AccessPassDeletionDays;
                int deleted = m_accessPassService.DeleteExpiredOrDepletedPasses(mode, days);
                if (deleted > 0)
                    WriteToLog(string.Format(L.T("Log_AccessPassAutodeleteDeleted"), deleted), Constants.LogInfo);
            }
            catch (Exception ex)
            {
                WriteToLog(string.Format(L.T("Log_AccessPassAutodeleteError"), ex.Message), Constants.LogWarning);
            }
        }

        /// <summary>
        /// Schreibt eine abgelehnte Card-ID in eine separate Log-Datei.
        /// Format: Datum/Uhrzeit ; CardID ; Status ; Grund
        /// </summary>
        public void WriteToRejectedLog(string cardId, string status, string reason)
        {
            string filePath = System.IO.Path.Combine(Constants.LogFolder, Constants.RejectedCheckinLogFile);
            string line = DateTime.Now.ToString(Constants.LogTimestampFormat)
                + " ; " + (cardId ?? "")
                + " ; " + (status ?? "")
                + " ; " + (reason ?? "");
            m_logWriter.Enqueue(line, filePath);
        }
    }
}
