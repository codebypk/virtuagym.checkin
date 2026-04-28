using Hardware;
using Hardware.Events;
using Hardware.Interfaces;
using Hardware.Models;
using Hardware.Services;
using HidLibrary;
using Jablotron.API.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Virtuagym.API;
using Virtuagym.CheckIn.Core.Models;
using Virtuagym.CheckIn.Core.Services;
using Virtuagym.CheckIn.WPF.Properties;
using Virtuagym.CheckIn.WPF.Services;
using Brushes = System.Windows.Media.Brushes;

namespace Virtuagym.CheckIn.WPF
{
    /// <summary>
    /// Dialog zum Erstellen/Bearbeiten eines CheckinClientMapping.
    /// Angelehnt an createEditCardReaderAction im Original CheckInClient.
    /// </summary>
    public partial class EditCheckinClientMappingWindow : System.Windows.Window
    {
        /// <summary>Das bearbeitete Mapping (wird bei OK zurückgegeben).</summary>
        public CheckinClientMapping ResultMapping { get; private set; }

        /// <summary>
        /// Optional callback to re-enumerate HID devices (set by the parent before ShowDialog).
        /// Returns the refreshed device list.
        /// </summary>
        public Func<HidDeviceInfo[]> RefreshDevicesCallback { get; set; }

        private readonly string[] _availableDeviceIds;
        private HidDeviceInfo[] _availableHidDevices;
        private CancellationTokenSource _qrScanCts;
        private QrCodeScanner _testScanner;
        private List<PgGateEntry> _pgGates = new List<PgGateEntry>();
        private int _pendingCameraIndex = -1;

        public EditCheckinClientMappingWindow(CheckinClientMapping existing, string[] availableDeviceIds, HidDeviceInfo[] availableHidDevices = null)
        {
            InitializeComponent();
            ApplyLocalization();

            _availableDeviceIds = availableDeviceIds ?? [];
            _availableHidDevices = availableHidDevices ?? [];

            // Device-ID ComboBox befüllen (mit Herstellerinfo)
            cmbMappingDeviceID.ItemsSource = _availableHidDevices;
            cmbMappingDeviceID.DisplayMemberPath = "DisplayName";
            cmbMappingDeviceID.SelectedValuePath = "DeviceId";
            // IsEditable bleibt true für manuelle Eingabe

            // Kamera-ComboBox wird asynchron in Loaded befüllt
            Loaded += async (_, __) => await LoadCamerasAsync();

            // Relay COM-Port ComboBox befüllen
            cmbMappingRelayComPort.Items.Clear();
            foreach (string port in SerialPort.GetPortNames().OrderBy(p => p))
            {
                cmbMappingRelayComPort.Items.Add(port);
            }

            // Load General Data
            PopulateCreditServices();
            UpdateRelayControlsEnabled();
            LoadPgGatesFromJson();
            UpdatePgControlsEnabled();
            PopulateHidProfiles();
            PopulateCameraBackends();
            UpdateApiModeInfo();

            if (existing != null)
            {
                LoadMapping(existing);
            }
            else
            {
                // Standard-Check-In Key aus globalen Einstellungen vorbelegen
                txtMappingCheckinKey.Text = Settings.Default.VirtuagymClubSecret;

                // Standard Doppelscan-Schutz: 1 Minute
                foreach (ComboBoxItem item in cmbDoubleScanThreshold.Items)
                {
                    if ((string)item.Tag == Settings.Default.DefaultDoubleScanThresholdMs.ToString())
                    {
                        cmbDoubleScanThreshold.SelectedItem = item;
                        break;
                    }
                }

                // Standard Auto-Checkout: Deaktiviert
                cmbAutoCheckoutMinutes.SelectedIndex = 0;

                txtDuplicateTimeoutSeconds.Text = Settings.Default.DuplicateTimeoutSeconds.ToString();
            }


            tabMain.SelectionChanged += TabMain_SelectionChanged;
            Closing += (_, __) => StopQrScanPreview();
        }

        private async Task LoadCamerasAsync()
        {
            var cameras = await CameraDiscoveryService.GetCamerasAsync();
            foreach (var cam in cameras)
                cmbMappingCamera.Items.Add(new ComboBoxItem { Content = cam.DisplayName, Tag = cam.Index });
            
            // PendingCameraIndex ist ein OpenCV-Index → per Tag suchen
            if (_pendingCameraIndex >= 0)
            {
                for (int i = 0; i < cmbMappingCamera.Items.Count; i++)
                {
                    if ((cmbMappingCamera.Items[i] as ComboBoxItem)?.Tag is int idx && idx == _pendingCameraIndex)
                    {
                        cmbMappingCamera.SelectedIndex = i;
                        break;
                    }
                }
            }
            if (cmbMappingCamera.SelectedIndex < 0 && cmbMappingCamera.Items.Count > 0)
                cmbMappingCamera.SelectedIndex = 0;
        }

        private void ApplyLocalization()
        {
            Title                              = L.T("EditMapping_Title");
            tabGeneral.Header                  = L.T("EditMapping_Grp_General");
            txtGrpMappingHeader.Text           = L.T("EditMapping_Lbl_SectionDevice");
            lblGrpVirtuagym.Text               = L.T("EditMapping_Grp_Virtuagym");
            tabDeviceTest.Header               = L.T("EditMapping_Grp_DeviceTest");
            tabRelayControl.Header             = L.T("EditMapping_Grp_Relay");
            lblMappingName.Text                = L.T("EditMapping_Lbl_Name");
            lblMappingInputType.Text           = L.T("EditMapping_Lbl_InputType");
            lblDeviceId.Text                   = L.T("EditMapping_Lbl_UsbDevice");
            lblCamera.Text                     = L.T("EditMapping_Lbl_Camera");
            lblCameraResolution.Text           = L.T("EditMapping_Lbl_CameraResolution");
            lblCameraBackend.Text              = L.T("EditMapping_Lbl_CameraBackend");
            lblCheckinKey.Text                 = L.T("EditMapping_Lbl_CheckinKey");
            txtMappingCheckinKey.ToolTip       = L.T("EditMapping_Tip_CheckinKey");
            lblApiMode.Text                    = L.T("EditMapping_Lbl_ApiMode");
            lblDoubleScan.Text                 = L.T("EditMapping_Lbl_DoubleScan");
            cmbDoubleScanThreshold.ToolTip     = L.T("EditMapping_Tip_DoubleScan");
            lblDuplicateTimeout.Text           = L.T("EditMapping_Lbl_DuplicateTimeout");
            txtDuplicateTimeoutSeconds.ToolTip = L.T("EditMapping_Tip_DuplicateTimeout");
            lblCreditServiceName.Text          = L.T("EditMapping_Lbl_CreditServiceName");
            cmbCreditService.ToolTip           = L.T("EditMapping_CreditServiceName_Tip");
            lblRepeatTimeMs.Text               = L.T("EditMapping_Lbl_RepeatTime");
            txtRepeatTimeMs.ToolTip            = string.Format(L.T("EditMapping_Tip_RepeatTime"), Settings.Default.RepaitTimeInMs);
            lblHidProfile.Text                 = "Reader-Profil:";
            lblRelayEnabled.Text               = L.T("EditMapping_Lbl_RelayEnabled");
            lblRelayComPort.Text               = L.T("EditMapping_Lbl_RelayComPort");
            lblRelayBaudRate.Text              = L.T("EditMapping_Lbl_RelayBaudRate");
            lblRelayNumber.Text                = L.T("EditMapping_Lbl_RelayNumber");
            btnMappingTestBeep.Content         = L.T("EditMapping_Btn_Beep");

            // Jablotron PG tab
            tabJablotronPGControl.Header       = L.T("EditMapping_Grp_JablotronPG");
            lblPgSectionGate.Text              = L.T("EditMapping_Lbl_PgSectionGate");
            lblPgEnabled.Text                  = L.T("EditMapping_Lbl_PgEnabled");
            lblPgGate.Text                     = L.T("EditMapping_Lbl_PgGate");
            lblPgTriggerTime.Text              = L.T("EditMapping_Lbl_PgTriggerTime");
            lblPgSectionCondition.Text         = L.T("EditMapping_Lbl_PgSectionCondition");
            lblPgConditionDesc.Text            = L.T("EditMapping_Lbl_PgConditionDesc");
            lblPgConditionMode.Text            = L.T("EditMapping_Lbl_PgConditionMode");
            cmbPgCondAlways.Content            = L.T("EditMapping_PgCond_Always");
            cmbPgCondOnlyIfOn.Content          = L.T("EditMapping_PgCond_OnlyIfOn");
            cmbPgCondOnlyIfOff.Content         = L.T("EditMapping_PgCond_OnlyIfOff");
            cmbPgCondTimeRange.Content         = L.T("EditMapping_PgCond_TimeRange");
            lblPgConditionTimeFrom.Text        = L.T("EditMapping_Lbl_PgConditionTimeFrom");
            lblPgConditionTimeTo.Text          = L.T("EditMapping_Lbl_PgConditionTimeTo");
            lblPgConditionGate.Text            = L.T("EditMapping_Lbl_PgConditionGate");
            btnPgReloadGates.Content           = L.T("EditMapping_Btn_PgReloadGates");
            btnTestPg.Content                  = L.T("EditMapping_Btn_PgTest");
            btnMappingTestRead.Content         = L.T("EditMapping_Btn_ReadCard");
            btnMappingTestQrScan.Content       = L.T("EditMapping_Btn_QrScan");
            btnMappingTestQrStop.Content       = L.T("EditMapping_Btn_Stop");
            btnMappingSave.Content             = L.T("Btn_Save");
            btnMappingCancel.Content           = L.T("Btn_Cancel");

            // InputType ComboBox
            ((ComboBoxItem)cmbMappingInputType.Items[0]).Content = L.T("EditMapping_Type_RFID");
            ((ComboBoxItem)cmbMappingInputType.Items[1]).Content = L.T("EditMapping_Type_QR");
            ((ComboBoxItem)cmbMappingInputType.Items[2]).Content = L.T("EditMapping_Type_CCID");

            // DoubleScan ComboBox
            ((ComboBoxItem)cmbDoubleScanThreshold.Items[0]).Content = L.T("EditMapping_DS_Disabled");
            ((ComboBoxItem)cmbDoubleScanThreshold.Items[1]).Content = L.T("EditMapping_DS_5s");
            ((ComboBoxItem)cmbDoubleScanThreshold.Items[2]).Content = L.T("EditMapping_DS_15s");
            ((ComboBoxItem)cmbDoubleScanThreshold.Items[3]).Content = L.T("EditMapping_DS_30s");
            ((ComboBoxItem)cmbDoubleScanThreshold.Items[4]).Content = L.T("EditMapping_DS_1m");
            ((ComboBoxItem)cmbDoubleScanThreshold.Items[5]).Content = L.T("EditMapping_DS_2m");
            ((ComboBoxItem)cmbDoubleScanThreshold.Items[6]).Content = L.T("EditMapping_DS_5m");
            ((ComboBoxItem)cmbDoubleScanThreshold.Items[7]).Content = L.T("EditMapping_DS_10m");
            ((ComboBoxItem)cmbDoubleScanThreshold.Items[8]).Content = L.T("EditMapping_DS_15m");
            ((ComboBoxItem)cmbDoubleScanThreshold.Items[9]).Content = L.T("EditMapping_DS_30m");

            // Auto-Checkout
            lblAutoCheckout.Text = L.T("EditMapping_Lbl_AutoCheckout");
            ((ComboBoxItem)cmbAutoCheckoutMinutes.Items[0]).Content = L.T("EditMapping_AC_Disabled");
            ((ComboBoxItem)cmbAutoCheckoutMinutes.Items[1]).Content = L.T("EditMapping_AC_1m");
            ((ComboBoxItem)cmbAutoCheckoutMinutes.Items[2]).Content = L.T("EditMapping_AC_5m");
            ((ComboBoxItem)cmbAutoCheckoutMinutes.Items[3]).Content = L.T("EditMapping_AC_15m");
            ((ComboBoxItem)cmbAutoCheckoutMinutes.Items[4]).Content = L.T("EditMapping_AC_30m");
            ((ComboBoxItem)cmbAutoCheckoutMinutes.Items[5]).Content = L.T("EditMapping_AC_1h");
            ((ComboBoxItem)cmbAutoCheckoutMinutes.Items[6]).Content = L.T("EditMapping_AC_2h");
            ((ComboBoxItem)cmbAutoCheckoutMinutes.Items[7]).Content = L.T("EditMapping_AC_4h");
            ((ComboBoxItem)cmbAutoCheckoutMinutes.Items[8]).Content = L.T("EditMapping_AC_8h");
            ((ComboBoxItem)cmbAutoCheckoutMinutes.Items[9]).Content = L.T("EditMapping_AC_12h");

            // Kartennummer-Format
            lblCardIdMode.Text = L.T("EditMapping_CardIdMode");
            ((ComboBoxItem)cmbCardIdMode.Items[0]).Content = L.T("EditMapping_CardIdMode_Lower3Bytes");
            ((ComboBoxItem)cmbCardIdMode.Items[1]).Content = L.T("EditMapping_CardIdMode_FullDecimal");
            ((ComboBoxItem)cmbCardIdMode.Items[2]).Content = L.T("EditMapping_CardIdMode_FullHex");
            ((ComboBoxItem)cmbCardIdMode.Items[3]).Content = L.T("EditMapping_CardIdMode_Lower4Bytes");
            txtCardIdModeTooltip.Text = L.T("EditMapping_CardIdMode_Tip");
        }

        private void LoadMapping(CheckinClientMapping mapping)
        {
            txtMappingName.Text = mapping.Name ?? "";

            // Device-ID setzen: erst per SelectedValue versuchen, sonst als Text
            string deviceId = mapping.DeviceID ?? "";
            if (_availableHidDevices.Any(d => d.DeviceId == deviceId))
                cmbMappingDeviceID.SelectedValue = deviceId;
            else
                cmbMappingDeviceID.Text = deviceId;

            txtMappingCheckinKey.Text = mapping.CheckinKey ?? "";

            // Eingabetyp setzen
            string inputType = mapping.InputType ?? nameof(HardwareInputType.USBReader);
            foreach (ComboBoxItem item in cmbMappingInputType.Items)
            {
                if ((string)item.Tag == inputType)
                {
                    cmbMappingInputType.SelectedItem = item;
                    break;
                }
            }

            // Kamera-Index setzen (wird nach async Laden der Kameras in LoadCamerasAsync angewendet)
            _pendingCameraIndex = mapping.CameraIndex;

            // Kamera-Auflösung setzen
            string resTag = $"{mapping.CameraResolutionWidth}x{mapping.CameraResolutionHeight}";
            foreach (ComboBoxItem item in cmbCameraResolution.Items)
            {
                if ((string)item.Tag == resTag)
                {
                    cmbCameraResolution.SelectedItem = item;
                    break;
                }
            }

            // Video-Backend setzen
            string backendTag = mapping.CameraBackend ?? "ANY";
            foreach (ComboBoxItem item in cmbCameraBackend.Items)
            {
                if ((string)item.Tag == backendTag)
                {
                    cmbCameraBackend.SelectedItem = item;
                    break;
                }
            }

            chkMappingRelayEnabled.IsChecked = mapping.RelayEnabled;

            // Relay COM-Port setzen
            if (!string.IsNullOrEmpty(mapping.RelayComPort))
            {
                if (!cmbMappingRelayComPort.Items.Contains(mapping.RelayComPort))
                    cmbMappingRelayComPort.Items.Add(mapping.RelayComPort);
                cmbMappingRelayComPort.SelectedItem = mapping.RelayComPort;
            }

            // Relay Baud Rate setzen
            string baudRateStr = mapping.RelayBaudRate.ToString();
            foreach (ComboBoxItem item in cmbMappingRelayBaudRate.Items)
            {
                if ((string)item.Tag == baudRateStr)
                {
                    cmbMappingRelayBaudRate.SelectedItem = item;
                    break;
                }
            }

            // Relay-Nummer setzen
            foreach (ComboBoxItem item in cmbMappingRelayNumber.Items)
            {
                if ((string)item.Tag == mapping.RelayNumber.ToString())
                {
                    cmbMappingRelayNumber.SelectedItem = item;
                    break;
                }
            }

            // Relay Trigger Time setzen
            string triggerTimeStr = mapping.RelayTriggerTime.ToString(System.Globalization.CultureInfo.InvariantCulture);
            foreach (ComboBoxItem item in cmbMappingRelayTriggerTime.Items)
            {
                if ((string)item.Tag == triggerTimeStr)
                {
                    cmbMappingRelayTriggerTime.SelectedItem = item;
                    break;
                }
            }

            // Relay TriggerAction setzen
            string relayTriggerAction = mapping.RelayTriggerAction ?? "Always";
            foreach (ComboBoxItem item in cmbRelayTriggerAction.Items)
            {
                if ((string)item.Tag == relayTriggerAction)
                {
                    cmbRelayTriggerAction.SelectedItem = item;
                    break;
                }
            }
            if (cmbRelayTriggerAction.SelectedItem == null)
                cmbRelayTriggerAction.SelectedIndex = 0;

            // Relay ShowErrorOnDisplay setzen
            chkRelayShowErrorOnDisplay.IsChecked = mapping.RelayShowErrorOnDisplay;

            // Doppelscan-Schutz setzen
            string thresholdStr = mapping.DoubleScanThresholdMs.ToString();
            bool thresholdFound = false;
            foreach (ComboBoxItem item in cmbDoubleScanThreshold.Items)
            {
                if ((string)item.Tag == thresholdStr)
                {
                    cmbDoubleScanThreshold.SelectedItem = item;
                    thresholdFound = true;
                    break;
                }
            }
            if (!thresholdFound)
            {
                // Standard: 1 Minute
                foreach (ComboBoxItem item in cmbDoubleScanThreshold.Items)
                {
                    if ((string)item.Tag == Settings.Default.DefaultDoubleScanThresholdMs.ToString())
                    {
                        cmbDoubleScanThreshold.SelectedItem = item;
                        break;
                    }
                }
            }

            // Repeat time setzen
            txtRepeatTimeMs.Text = mapping.RepeatTimeMs.HasValue ? mapping.RepeatTimeMs.Value.ToString() : "";

            // Leser-Entprellung setzen
            txtDuplicateTimeoutSeconds.Text = mapping.DuplicateTimeoutSeconds.HasValue ? mapping.DuplicateTimeoutSeconds.Value.ToString() : "";

            // HID-Profil setzen
            if (!string.IsNullOrWhiteSpace(mapping.HidProfile))
            {
                foreach (ComboBoxItem item in cmbHidProfile.Items)
                {
                    if ((string)item.Tag == mapping.HidProfile)
                    {
                        cmbHidProfile.SelectedItem = item;
                        break;
                    }
                }
            }

            // Credit-Einstellungen setzen
            SelectCreditService(mapping.CreditServiceId);

            // Auto-Checkout setzen
            string autoCheckoutStr = mapping.AutoCheckoutMinutes.ToString();
            bool autoCheckoutFound = false;
            foreach (ComboBoxItem item in cmbAutoCheckoutMinutes.Items)
            {
                if ((string)item.Tag == autoCheckoutStr)
                {
                    cmbAutoCheckoutMinutes.SelectedItem = item;
                    autoCheckoutFound = true;
                    break;
                }
            }
            if (!autoCheckoutFound)
                cmbAutoCheckoutMinutes.SelectedIndex = 0;

            // Kartennummer-Format setzen
            string cardIdModeStr = mapping.CardIdMode.ToString();
            bool cardIdModeFound = false;
            foreach (ComboBoxItem item in cmbCardIdMode.Items)
            {
                if ((string)item.Tag == cardIdModeStr)
                {
                    cmbCardIdMode.SelectedItem = item;
                    cardIdModeFound = true;
                    break;
                }
            }
            if (!cardIdModeFound)
                cmbCardIdMode.SelectedIndex = 0;

            // Programmable Gate Einstellungen setzen
            chkPgEnabled.IsChecked = mapping.PgEnabled;
            SelectPgGate(mapping.PgGateComponentId);

            string pgTimeStr = mapping.PgTriggerTimeSec.ToString(System.Globalization.CultureInfo.InvariantCulture);
            foreach (ComboBoxItem item in cmbPgTriggerTime.Items)
            {
                if ((string)item.Tag == pgTimeStr)
                {
                    cmbPgTriggerTime.SelectedItem = item;
                    break;
                }
            }

            // Bedingungsmodus setzen
            string condMode = mapping.PgConditionMode ?? "Always";
            foreach (ComboBoxItem item in cmbPgConditionMode.Items)
            {
                if ((string)item.Tag == condMode)
                {
                    cmbPgConditionMode.SelectedItem = item;
                    break;
                }
            }
            SelectPgConditionGate(mapping.PgConditionGateComponentId);

            // Zeitfenster setzen
            txtPgConditionTimeFrom.Text = mapping.PgConditionTimeFrom ?? "";
            txtPgConditionTimeTo.Text = mapping.PgConditionTimeTo ?? "";

            // PG TriggerAction setzen
            string pgTriggerAction = mapping.PgTriggerAction ?? "Always";
            foreach (ComboBoxItem item in cmbPgTriggerAction.Items)
            {
                if ((string)item.Tag == pgTriggerAction)
                {
                    cmbPgTriggerAction.SelectedItem = item;
                    break;
                }
            }
            if (cmbPgTriggerAction.SelectedItem == null)
                cmbPgTriggerAction.SelectedIndex = 0;

            // PG ShowErrorOnDisplay setzen
            chkPgShowErrorOnDisplay.IsChecked = mapping.PgShowErrorOnDisplay;

            UpdatePgControlsEnabled();
        }

        /// <summary>
        /// Ermittelt die ausgewählte Device-ID: per SelectedValue (aus Dropdown) oder Text (manuelle Eingabe).
        /// </summary>
        private string GetSelectedDeviceId()
        {
            // Wenn ein Eintrag aus der Liste gewählt wurde, SelectedValue nutzen
            if (cmbMappingDeviceID.SelectedValue is string selectedId && !string.IsNullOrWhiteSpace(selectedId))
                return selectedId.Trim();

            // Fallback: manuell eingegebener Text
            return (cmbMappingDeviceID.Text ?? "").Trim();
        }

        private CheckinClientMapping BuildMapping()
        {
            string inputType = (cmbMappingInputType.SelectedItem as ComboBoxItem)?.Tag?.ToString()
                ?? nameof(HardwareInputType.USBReader);
            int cameraIndex = (cmbMappingCamera.SelectedItem as ComboBoxItem)?.Tag is int ci ? ci : 0;
            long doubleScanThreshold = long.TryParse(
                (cmbDoubleScanThreshold.SelectedItem as ComboBoxItem)?.Tag?.ToString(), out long ds)
                ? ds : Settings.Default.DefaultDoubleScanThresholdMs;

            int? repeatTimeMs = null;
            if (int.TryParse(txtRepeatTimeMs.Text, out int rtMs) && rtMs > 0)
                repeatTimeMs = rtMs;

            return new CheckinClientMapping
            {
                Name = txtMappingName.Text.Trim(),
                InputType = inputType,
                DeviceID = GetSelectedDeviceId(),
                CameraIndex = cameraIndex,
                CameraResolutionWidth = GetSelectedResolution().width,
                CameraResolutionHeight = GetSelectedResolution().height,
                CameraBackend = (cmbCameraBackend.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "ANY",
                CheckinKey = txtMappingCheckinKey.Text.Trim(),
                RelayEnabled = chkMappingRelayEnabled.IsChecked == true,
                RelayComPort = cmbMappingRelayComPort.SelectedItem?.ToString() ?? "",
                RelayBaudRate = int.TryParse((cmbMappingRelayBaudRate.SelectedItem as ComboBoxItem)?.Tag?.ToString(), out int rbd) ? rbd : 9600,
                RelayNumber = int.TryParse((cmbMappingRelayNumber.SelectedItem as ComboBoxItem)?.Tag?.ToString(), out int rn) ? rn : 1,
                RelayTriggerTime = double.TryParse(
                    (cmbMappingRelayTriggerTime.SelectedItem as ComboBoxItem)?.Tag?.ToString(),
                    System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out double rt) ? rt : 0.05,
                RelayTriggerAction = (cmbRelayTriggerAction.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "Always",
                RelayShowErrorOnDisplay = chkRelayShowErrorOnDisplay.IsChecked == true,
                DoubleScanThresholdMs = doubleScanThreshold,
                DuplicateTimeoutSeconds = int.TryParse(txtDuplicateTimeoutSeconds.Text, out int dts) && dts >= 0 ? dts : null,
                RepeatTimeMs = repeatTimeMs,
                HidProfile = GetSelectedProfile()?.ProfileId ?? "",
                CreditServiceId = GetSelectedCreditServiceId(),
                CreditClubId = GetSelectedCreditClubId(),
                AutoCheckoutMinutes = int.TryParse(
                    (cmbAutoCheckoutMinutes.SelectedItem as ComboBoxItem)?.Tag?.ToString(), out int acm) ? acm : 0,
                CardIdMode = int.TryParse(
                    (cmbCardIdMode.SelectedItem as ComboBoxItem)?.Tag?.ToString(), out int cim) ? cim : 0,
                PgEnabled = chkPgEnabled.IsChecked == true,
                PgGateComponentId = (cmbPgGate.SelectedItem as PgGateEntry)?.CloudComponentId ?? "",
                PgGateServiceId = (cmbPgGate.SelectedItem as PgGateEntry)?.ServiceId ?? "",
                PgGateName = (cmbPgGate.SelectedItem as PgGateEntry)?.Name ?? "",
                PgTriggerTimeSec = double.TryParse(
                    (cmbPgTriggerTime.SelectedItem as ComboBoxItem)?.Tag?.ToString(),
                    System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out double pgT) ? pgT : 3,
                PgConditionMode = (cmbPgConditionMode.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "Always",
                PgConditionGateComponentId = (cmbPgConditionGate.SelectedItem as PgGateEntry)?.CloudComponentId ?? "",
                PgConditionGateServiceId = (cmbPgConditionGate.SelectedItem as PgGateEntry)?.ServiceId ?? "",
                PgConditionGateName = (cmbPgConditionGate.SelectedItem as PgGateEntry)?.Name ?? "",
                PgConditionTimeFrom = txtPgConditionTimeFrom.Text?.Trim() ?? "",
                PgConditionTimeTo = txtPgConditionTimeTo.Text?.Trim() ?? "",
                PgTriggerAction = (cmbPgTriggerAction.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "Always",
                PgShowErrorOnDisplay = chkPgShowErrorOnDisplay.IsChecked == true,
            };
        }

        private void chkMappingRelayEnabled_Changed(object sender, RoutedEventArgs e)
        {
            UpdateRelayControlsEnabled();
        }

        private void chkPgEnabled_Changed(object sender, RoutedEventArgs e)
        {
            UpdatePgControlsEnabled();
        }

        private void cmbMappingInputType_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateInputTypeVisibility();
        }

        private void UpdateInputTypeVisibility()
        {
            if (cmbMappingInputType == null || lblDeviceId == null || lblCamera == null)
                return;

            string inputType = (cmbMappingInputType.SelectedItem as ComboBoxItem)?.Tag?.ToString()
                ?? nameof(HardwareInputType.USBReader);

            bool isRfid = inputType == nameof(HardwareInputType.USBReader);
            bool isCcid = inputType == nameof(HardwareInputType.CCID);
            bool isQrCode = inputType == nameof(HardwareInputType.QRCode);

            // RFID und CCID verwenden beide DeviceID, QR-Code verwendet Kamera
            lblDeviceId.Visibility = (isRfid || isCcid) ? Visibility.Visible : Visibility.Collapsed;
            cmbMappingDeviceID.Visibility = (isRfid || isCcid) ? Visibility.Visible : Visibility.Collapsed;

            lblCamera.Visibility = isQrCode ? Visibility.Visible : Visibility.Collapsed;
            cmbMappingCamera.Visibility = isQrCode ? Visibility.Visible : Visibility.Collapsed;

            // Kamera-Auflösung und Backend nur bei QR-Code anzeigen
            if (lblCameraResolution != null)
                lblCameraResolution.Visibility = isQrCode ? Visibility.Visible : Visibility.Collapsed;
            if (cmbCameraResolution != null)
                cmbCameraResolution.Visibility = isQrCode ? Visibility.Visible : Visibility.Collapsed;
            if (lblCameraBackend != null)
                lblCameraBackend.Visibility = isQrCode ? Visibility.Visible : Visibility.Collapsed;
            if (cmbCameraBackend != null)
                cmbCameraBackend.Visibility = isQrCode ? Visibility.Visible : Visibility.Collapsed;

            // Geräte-Test: RFID- oder QR-Panel anzeigen
            if (panelRfidTest != null)
                panelRfidTest.Visibility = isRfid ? Visibility.Visible : Visibility.Collapsed;
            if (panelQrTest != null)
                panelQrTest.Visibility = isQrCode ? Visibility.Visible : Visibility.Collapsed;

            // "Card lesen" im Checkin-Test nur bei RFID sinnvoll
            // Repeat time nur bei RFID relevant (CCID ist event-getrieben)
            if (lblRepeatTimeMs != null)
                lblRepeatTimeMs.Visibility = isRfid ? Visibility.Visible : Visibility.Collapsed;
            if (txtRepeatTimeMs != null)
                txtRepeatTimeMs.Visibility = isRfid ? Visibility.Visible : Visibility.Collapsed;

            // HID-Profil nur bei RFID relevant
            if (lblHidProfile != null)
                lblHidProfile.Visibility = isRfid ? Visibility.Visible : Visibility.Collapsed;
            if (cmbHidProfile != null)
                cmbHidProfile.Visibility = isRfid ? Visibility.Visible : Visibility.Collapsed;

            // CardId-Modus nur bei RFID/CCID relevant (nicht bei QR-Code)
            if (lblCardIdMode != null)
                lblCardIdMode.Visibility = isQrCode ? Visibility.Collapsed : Visibility.Visible;
            if (panelCardIdMode != null)
                panelCardIdMode.Visibility = isQrCode ? Visibility.Collapsed : Visibility.Visible;

            // Bei CCID: DeviceID Label anpassen
            if (isCcid)
                lblDeviceId.Text = L.T("EditMapping_Lbl_PcScReader");
            else
                lblDeviceId.Text = L.T("EditMapping_Lbl_UsbDevice");
        }

        private void UpdateRelayControlsEnabled()
        {
            bool enabled = chkMappingRelayEnabled.IsChecked == true;
            if (cmbMappingRelayComPort != null) cmbMappingRelayComPort.IsEnabled = enabled;
            if (cmbMappingRelayBaudRate != null) cmbMappingRelayBaudRate.IsEnabled = enabled;
            if (cmbMappingRelayNumber != null) cmbMappingRelayNumber.IsEnabled = enabled;
            if (cmbMappingRelayTriggerTime != null) cmbMappingRelayTriggerTime.IsEnabled = enabled;
            if (cmbRelayTriggerAction != null) cmbRelayTriggerAction.IsEnabled = enabled;
            if (chkRelayShowErrorOnDisplay != null) chkRelayShowErrorOnDisplay.IsEnabled = enabled;
            if (btnTestMappingRelay != null) btnTestMappingRelay.IsEnabled = enabled;
        }

        #region Jablotron Programmable Gate

        private void UpdatePgControlsEnabled()
        {
            bool enabled = chkPgEnabled.IsChecked == true;
            if (cmbPgGate != null) cmbPgGate.IsEnabled = enabled;
            if (cmbPgTriggerTime != null) cmbPgTriggerTime.IsEnabled = enabled;
            if (cmbPgTriggerAction != null) cmbPgTriggerAction.IsEnabled = enabled;
            if (chkPgShowErrorOnDisplay != null) chkPgShowErrorOnDisplay.IsEnabled = enabled;
            if (btnTestPg != null) btnTestPg.IsEnabled = enabled;
            if (cmbPgConditionMode != null) cmbPgConditionMode.IsEnabled = enabled;
            UpdatePgConditionGateEnabled();
        }

        private void UpdatePgConditionGateEnabled()
        {
            bool pgEnabled = chkPgEnabled.IsChecked == true;
            string mode = (cmbPgConditionMode?.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "Always";
            bool needsGate = pgEnabled && (mode == "OnlyIfOn" || mode == "OnlyIfOff");
            bool needsTime = pgEnabled && mode == "TimeRange";
            if (cmbPgConditionGate != null) cmbPgConditionGate.IsEnabled = needsGate;
            if (lblPgConditionGate != null) lblPgConditionGate.Opacity = needsGate ? 1.0 : 0.4;
            var gateVis = needsGate ? Visibility.Visible : Visibility.Collapsed;
            var timeVis = needsTime ? Visibility.Visible : Visibility.Collapsed;
            if (cmbPgConditionGate != null) cmbPgConditionGate.Visibility = gateVis;
            if (lblPgConditionGate != null) lblPgConditionGate.Visibility = gateVis;
            if (lblPgConditionTimeFrom != null) lblPgConditionTimeFrom.Visibility = timeVis;
            if (txtPgConditionTimeFrom != null) txtPgConditionTimeFrom.Visibility = timeVis;
            if (lblPgConditionTimeTo != null) lblPgConditionTimeTo.Visibility = timeVis;
            if (txtPgConditionTimeTo != null) txtPgConditionTimeTo.Visibility = timeVis;
        }

        private void cmbPgConditionMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdatePgConditionGateEnabled();
        }

        private void LoadPgGatesFromJson()
        {
            try
            {
                PgGateLoader.ClearCache();
                _pgGates = PgGateLoader.Load();

                if (_pgGates.Count == 0)
                {
                    cmbPgGate.ItemsSource = null;
                    cmbPgConditionGate.ItemsSource = null;
                    SetPgStatus(L.T("EditMapping_PG_FileMissing"), Brushes.Orange);
                    return;
                }

                cmbPgGate.ItemsSource = _pgGates;
                    cmbPgConditionGate.ItemsSource = _pgGates;
                    if (_pgGates.Count > 0)
                    {
                        cmbPgGate.SelectedIndex = 0;
                        cmbPgConditionGate.SelectedIndex = 0;
                    }

                SetPgStatus(string.Format(L.T("Common_Pg_LoadedCount"), _pgGates.Count), Brushes.Gray);
            }
            catch (Exception ex)
            {
                cmbPgGate.ItemsSource = null;
                cmbPgConditionGate.ItemsSource = null;
                SetPgStatus(string.Format(L.T("Common_Pg_LoadError"), ex.Message), Brushes.Red);
            }
        }

        private void SelectPgGate(string componentId)
        {
            if (string.IsNullOrWhiteSpace(componentId) || _pgGates == null) return;

            for (int i = 0; i < _pgGates.Count; i++)
            {
                if (string.Equals(_pgGates[i].CloudComponentId, componentId, StringComparison.OrdinalIgnoreCase))
                {
                    cmbPgGate.SelectedIndex = i;
                    return;
                }
            }
        }

        private void SelectPgConditionGate(string componentId)
        {
            if (string.IsNullOrWhiteSpace(componentId) || _pgGates == null) return;

            for (int i = 0; i < _pgGates.Count; i++)
            {
                if (string.Equals(_pgGates[i].CloudComponentId, componentId, StringComparison.OrdinalIgnoreCase))
                {
                    cmbPgConditionGate.SelectedIndex = i;
                    return;
                }
            }
        }

        private void btnPgReloadGates_Click(object sender, RoutedEventArgs e)
        {
            LoadPgGatesFromJson();
        }

        private async void btnTestPg_Click(object sender, RoutedEventArgs e)
        {
            var selected = cmbPgGate.SelectedItem as PgGateEntry;
            if (selected == null)
            {
                SetPgStatus(L.T("EditMapping_PG_SelectGateFirst"), Brushes.Orange);
                return;
            }

            if (!selected.CanControl)
            {
                SetPgStatus(L.T("EditMapping_PG_NotControllable"), Brushes.Orange);
                return;
            }

            double triggerTime = double.TryParse(
                (cmbPgTriggerTime.SelectedItem as ComboBoxItem)?.Tag?.ToString(),
                System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture,
                out double tt) ? tt : 3;

            btnTestPg.IsEnabled = false;

            try
            {
                using (var client = new JablotronCloudService(
                    Settings.Default.JablotronApiUrl,
                    Settings.Default.JablotronApiUsername,
                    Settings.Default.JablotronApiPassword))
                {
                    client.PerformLogin();

                    int? serviceId = null;
                    if (int.TryParse(selected.ServiceId, out var parsedServiceId) && parsedServiceId > 0)
                        serviceId = parsedServiceId;

                    var ok = client.ControlProgrammableGate(serviceId, selected.CloudComponentId, "ON");
                    if (!ok)
                        throw new InvalidOperationException(L.T("EditMapping_PG_ControlNoSuccess"));

                    SetPgStatus(string.Format(L.T("EditMapping_PG_SetOnOk"), selected.Name), Brushes.Green);

                    if (triggerTime > 0)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(triggerTime));

                        var offOk = client.ControlProgrammableGate(serviceId, selected.CloudComponentId, "OFF");
                        if (!offOk)
                            throw new InvalidOperationException(L.T("EditMapping_PG_AutoOffNoSuccess"));

                        SetPgStatus(string.Format(L.T("EditMapping_PG_AutoOffDone"), selected.Name, triggerTime), Brushes.Green);
                    }
                }
            }
            catch (Exception ex)
            {
                SetPgStatus(string.Format(L.T("EditMapping_PG_Error"), ex.Message), Brushes.Red);
            }
            finally
            {
                btnTestPg.IsEnabled = chkPgEnabled.IsChecked == true;
            }
        }

        private void SetPgStatus(string message, System.Windows.Media.Brush color)
        {
            txtPgStatus.Text = message;
            txtPgStatus.Foreground = color;
        }



        #endregion

        private void txtMappingCheckinKey_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            UpdateApiModeInfo();
        }

        private void txtMappingName_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            UpdateApiModeInfo();
        }

        /// <summary>
        /// Aktualisiert die API-Modus-Anzeige und den Info-Tooltip basierend auf dem aktuellen CheckinKey.
        /// <list type="bullet">
        ///   <item><b>v0 API</b> (CheckinKey vorhanden): Die numerische device_id wird aus dem Key
        ///         extrahiert (z.B. "54042"). Virtuagym ordnet den Visit anhand dieser ID dem
        ///         Checkin-Client zu. Check-in/out wird per Toggle (PUT /devices) durchgeführt.</item>
        ///   <item><b>v1 API</b> (kein CheckinKey): Es gibt keine numerische device_id. Stattdessen
        ///         wird ein Slug aus dem Gerätenamen (z.B. "@eingang_haupttuer") im Feld
        ///         <c>status_message</c> des Visits mitgesendet, damit die Check-in-Quelle im
        ///         Virtuagym-Backend und in den Berichten identifizierbar bleibt.
        ///         Format: "@slug - Check-In" / "@slug - Check-Out".</item>
        /// </list>
        /// </summary>
        private void UpdateApiModeInfo()
        {
            if (txtApiModeInfo == null) return;

            string checkinKey = txtMappingCheckinKey?.Text?.Trim() ?? "";
            string numericDevId = VirtuagymApiBase.ExtractDeviceId(checkinKey);

            if (!string.IsNullOrEmpty(numericDevId))
            {
                // v0 API: device_id aus dem CheckinKey extrahiert (z.B. CS-61464-CHECKIN8837-...)
                txtApiModeInfo.Text = L.T("EditMapping_ApiMode_V0") + " · "
                    + string.Format(L.T("EditMapping_ApiMode_V0_DeviceId"), numericDevId);
                txtApiModeInfo.Foreground = Brushes.Green;
                txtApiModeTooltip.Text = L.T("EditMapping_Tip_ApiMode_V0");
            }
            else if (!string.IsNullOrWhiteSpace(checkinKey)
                && checkinKey.Contains("CHECKIN", StringComparison.OrdinalIgnoreCase))
            {
                // Fehler: CheckinKey sieht nach v0-Format aus (enthält "CHECKIN"),
                // aber die numerische device_id konnte nicht extrahiert werden.
                // Erwartetes Format: CS-{club_id}-CHECKIN{device_id}-{secret}
                txtApiModeInfo.Text = L.T("EditMapping_ApiMode_V0") + " · "
                    + L.T("EditMapping_ApiMode_V0_NoDeviceId");
                txtApiModeInfo.Foreground = Brushes.Red;
                txtApiModeTooltip.Text = L.T("EditMapping_Tip_ApiMode_V0_Warn");
            }
            else
            {
                // v1+ API: Kein CheckinKey oder CheckinKey ohne device_id (z.B. CS-61464-ACCESS-...)
                // → Slug aus dem Gerätenamen als device_id
                string name = txtMappingName?.Text?.Trim() ?? "";
                string slug = CheckinClientMapping.ToDeviceIdSlug(name);
                string display = !string.IsNullOrEmpty(slug) ? slug : L.T("EditMapping_ApiMode_V1_NoName");
                txtApiModeInfo.Text = L.T("EditMapping_ApiMode_V1") + " · "
                    + string.Format(L.T("EditMapping_ApiMode_V1_DeviceId"), display);
                txtApiModeInfo.Foreground = Brushes.SteelBlue;
                txtApiModeTooltip.Text = L.T("EditMapping_Tip_ApiMode_V1");
            }
        }

        #region Geräte-Test (Beep / Read)

        private void btnRefreshDevices_Click(object sender, RoutedEventArgs e)
        {
            if (RefreshDevicesCallback != null)
            {
                var currentValue = cmbMappingDeviceID.Text;
                _availableHidDevices = RefreshDevicesCallback();
                cmbMappingDeviceID.ItemsSource = _availableHidDevices;
                cmbMappingDeviceID.Text = currentValue;
            }
        }

        private void btnMappingTestBeep_Click(object sender, RoutedEventArgs e)
        {
            SendDeviceTestCommand(GetSelectedProfile()?.BeepCommandBytes ?? [], "Beep");
        }

        private void btnMappingTestRead_Click(object sender, RoutedEventArgs e)
        {
            SendDeviceTestCommand(GetSelectedProfile()?.ReadCommandBytes ?? [], "Read");
        }

        private async void SendDeviceTestCommand(byte[] command, string commandName)
        {
            string selectedDeviceId = GetSelectedDeviceId();
            if (string.IsNullOrWhiteSpace(selectedDeviceId))
            {
                txtDeviceTestResult.Text = L.T("EditMapping_DeviceTest_NoDevice");
                txtDeviceTestResult.Foreground = Brushes.Orange;
                return;
            }

            btnMappingTestBeep.IsEnabled = false;
            btnMappingTestRead.IsEnabled = false;
            txtDeviceTestResult.Text = string.Format(L.T("EditMapping_DeviceTest_SendingCmd"), commandName, selectedDeviceId);
            txtDeviceTestResult.Foreground = Brushes.Gray;

            try
            {
                string result = await Task.Run(() =>
                {
                    var hidDevices = HidDevices.Enumerate();
                    HidDevice targetDevice = null;

                    foreach (var device in hidDevices)
                    {
                        string escapedPattern = Regex.Escape(selectedDeviceId).Replace(Regex.Escape("&8"), "[#&]8");
                        if (Regex.IsMatch(device.DevicePath, escapedPattern, RegexOptions.IgnoreCase))
                        {
                            targetDevice = device;
                            break;
                        }
                    }

                    if (targetDevice == null)
                        return "ERROR|" + L.T("EditMapping_DeviceTest_DeviceNotFound");

                    targetDevice.OpenDevice();
                    if (!targetDevice.IsConnected)
                    {
                        targetDevice.CloseDevice();
                        return "ERROR|" + L.T("EditMapping_DeviceTest_CannotOpen");
                    }

                    bool writeSuccess = targetDevice.Write(command);

                    if (commandName == "Read")
                    {
                        var report = targetDevice.Read(3000);
                        targetDevice.CloseDevice();

                        if (report.Status == HidDeviceData.ReadStatus.Success)
                        {
                            var card = new Card(report.Data);
                            if (card.IsValidTag)
                            {
#pragma warning disable CS0618 // Type or member is obsolete
                                string info = L.T("EditMapping_DeviceTest_CardRead") + ":\n"
                                    + "  UID Hex:              " + card.UidHex + "\n"
                                    + "  Standard 10-stellig:  " + card.GetCardId(CardIdMode.Lower3Bytes) + "\n"
                                    + "  MIFARE Classic:       " + card.GetCardId(CardIdMode.Lower4Bytes) + "\n"
                                    + "  Volle UID Dezimal:    " + card.GetCardId(CardIdMode.FullDecimal) + "\n"
                                    + "  Volle UID Hex:        " + card.GetCardId(CardIdMode.FullHex) + "\n"
                                    + "  UID Legacy:           " + card.UidLegacy;
#pragma warning restore CS0618 // Type or member is obsolete
                                return "OK|" + info;
                            }
                            string hexData = BitConverter.ToString(report.Data);
                            return "OK|" + L.T("EditMapping_DeviceTest_Response") + ": " + hexData;
                        }
                        return "WARN|" + L.T("EditMapping_DeviceTest_NoCardDetected");
                    }
                    else
                    {
                        targetDevice.CloseDevice();
                        return writeSuccess ? "OK|" + L.T("EditMapping_DeviceTest_BeepSuccess") : "ERROR|" + L.T("EditMapping_DeviceTest_BeepFailed");
                    }
                });

                string[] parts = result.Split(new[] { '|' }, 2);
                string status = parts[0];
                string message = parts.Length > 1 ? parts[1] : result;

                txtDeviceTestResult.Text = message;
                txtDeviceTestResult.Foreground = status == "OK" ? Brushes.Green
                    : status == "WARN" ? Brushes.Orange
                    : Brushes.Red;

                // Bei Read-Erfolg: Card-ID im gewählten Format ins Test-Feld übernehmen
                if (commandName == "Read" && status == "OK" && message.StartsWith(L.T("EditMapping_DeviceTest_CardRead") + ":"))
                {
                    // Gewählten CardIdMode-Wert aus der ComboBox lesen
                    int selectedMode = int.TryParse(
                        (cmbCardIdMode.SelectedItem as ComboBoxItem)?.Tag?.ToString(), out int m) ? m : 0;
                    var modeEnum = Enum.IsDefined(typeof(CardIdMode), selectedMode)
                        ? (CardIdMode)selectedMode : CardIdMode.Lower3Bytes;

                    // Zeile mit dem passenden Format finden
                    string[] lines = message.Split('\n');
                    string modeLabel = modeEnum switch
                    {
                        CardIdMode.Lower3Bytes => "Standard 10-stellig:",
                        CardIdMode.Lower4Bytes => "MIFARE Classic:",
                        CardIdMode.FullDecimal => "Volle UID Dezimal:",
                        CardIdMode.FullHex => "Volle UID Hex:",
                        _ => "Standard 10-stellig:"
                    };
                    foreach (string line in lines)
                    {
                        if (line.Contains(modeLabel))
                        {
                            txtMappingTestCardId.Text = line.Substring(line.IndexOf(':') + 1).Trim();
                            break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                txtDeviceTestResult.Text = L.T("Msg_Error") + ": " + ex.Message;
                txtDeviceTestResult.Foreground = Brushes.Red;
            }
            finally
            {
                btnMappingTestBeep.IsEnabled = true;
                btnMappingTestRead.IsEnabled = true;
            }
        }

        #endregion

        #region QR-Code Kamera-Test

        private void btnMappingTestQrScan_Click(object sender, RoutedEventArgs e)
        {
            StartQrScanTest();
        }

        private void btnMappingTestQrStop_Click(object sender, RoutedEventArgs e)
        {
            StopQrScanPreview();
        }

        private async void StartQrScanTest()
        {
            var selectedCamera = cmbMappingCamera.SelectedItem as ComboBoxItem;
            if (selectedCamera == null)
            {
                txtDeviceTestResult.Text = L.T("EditMapping_QrTest_NoCamera");
                txtDeviceTestResult.Foreground = Brushes.Orange;
                return;
            }

            int cameraIndex = selectedCamera.Tag is int ci ? ci : 0;

            // Laufenden QR-Scanner für diese Kamera pausieren, damit der Test sie verwenden kann
            QrCodeScanner.PauseByCamera(cameraIndex);

            btnMappingTestQrScan.IsEnabled = false;
            btnMappingTestQrStop.IsEnabled = true;
            borderCameraPreview.Visibility = Visibility.Visible;
            imgCameraPreview.Source = null;
            txtDeviceTestResult.Text = L.T("EditMapping_QrTest_OpeningCamera");
            txtDeviceTestResult.Foreground = Brushes.Gray;

            // Temporären QrCodeScanner erstellen – identisches Verhalten wie im normalen Scan
            var (resW, resH) = GetSelectedResolution();
            var captureApi = GetSelectedCaptureApi();
            _testScanner = await Task.Run(() =>
            {
                try
                {
                    return new QrCodeScanner(Guid.NewGuid().ToString().Replace("-", ""), new NullHardwareLogger(), cameraIndex,
                        name: $"DeviceTest-Camera{cameraIndex}",
                        duplicateTimeoutSeconds: 0,
                        resolutionWidth: resW,
                        resolutionHeight: resH,
                        captureApi: captureApi);
                }
                catch { return null; }
            });

            if (_testScanner == null)
            {
                txtDeviceTestResult.Text = L.T("EditMapping_QrTest_NoCameraFound");
                txtDeviceTestResult.Foreground = Brushes.Red;
                btnMappingTestQrScan.IsEnabled = true;
                btnMappingTestQrStop.IsEnabled = false;
                QrCodeScanner.ResumeByCamera(cameraIndex);
                return;
            }

            string cameraName = $"Camera {cameraIndex}";
            txtDeviceTestResult.Text = L.T("EditMapping_QrTest_Scanning");
            txtDeviceTestResult.Foreground = Brushes.Gray;

            _qrScanCts = new CancellationTokenSource();
            var ct = _qrScanCts.Token;

            // Vorschau-Event abonnieren
            _testScanner.CameraPreview += (s, args) =>
            {
                var bitmapImage = new BitmapImage();
                using (var ms = new System.IO.MemoryStream(args.FrameData))
                {
                    bitmapImage.BeginInit();
                    bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
                    bitmapImage.StreamSource = ms;
                    bitmapImage.EndInit();
                }
                bitmapImage.Freeze();
                Dispatcher.BeginInvoke(() => imgCameraPreview.Source = bitmapImage);
            };

            // QR-Code-Event abonnieren – Code anzeigen, Vorschau läuft weiter
            _testScanner.QrCodeRead += (s, args) =>
            {
                Dispatcher.BeginInvoke(() =>
                {
                    txtDeviceTestResult.Text = L.T("EditMapping_QrTest_Detected") + ": " + args.QrCode;
                    txtDeviceTestResult.Foreground = Brushes.Green;
                    txtMappingTestCardId.Text = args.QrCode;
                });
            };

            // Warten bis Abbruch durch Benutzer, Tab-Wechsel oder Fenster-Schließen
            try
            {
                await Task.Delay(Timeout.Infinite, ct);
            }
            catch (OperationCanceledException)
            {
                txtDeviceTestResult.Text = L.T("EditMapping_QrTest_Cancelled");
                txtDeviceTestResult.Foreground = Brushes.Orange;
            }
            catch (Exception ex)
            {
                txtDeviceTestResult.Text = L.T("Msg_Error") + ": " + ex.Message;
                txtDeviceTestResult.Foreground = Brushes.Red;
            }

            // Scanner stoppen und Hauptscanner wieder fortsetzen
            StopTestCamera();
            QrCodeScanner.ResumeByCamera(cameraIndex);

            _qrScanCts?.Dispose();
            _qrScanCts = null;

            btnMappingTestQrScan.IsEnabled = true;
            btnMappingTestQrStop.IsEnabled = false;
        }

        /// <summary>
        /// Stoppt die laufende QR-Scan-Vorschau (wird bei Stop-Button, Tab-Wechsel und Fenster-Schließen aufgerufen).
        /// </summary>
        private void StopQrScanPreview()
        {
            _qrScanCts?.Cancel();
            StopTestCamera();
        }

        private void TabMain_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.Source == tabMain && _testScanner != null)
                StopQrScanPreview();
        }

        private void StopTestCamera()
        {
            _testScanner?.Dispose();
            _testScanner = null;
        }

        #endregion

        private void btnTestMappingRelay_Click(object sender, RoutedEventArgs e)
        {
            string comPort = cmbMappingRelayComPort.SelectedItem?.ToString();
            if (string.IsNullOrWhiteSpace(comPort))
            {
                MessageBox.Show(L.T("EditMapping_Relay_NoPort"), L.T("Msg_Note"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            int relayBaudRate = int.TryParse((cmbMappingRelayBaudRate.SelectedItem as ComboBoxItem)?.Tag?.ToString(), out int rbd) ? rbd : 1;
            int relayNumber = int.TryParse((cmbMappingRelayNumber.SelectedItem as ComboBoxItem)?.Tag?.ToString(), out int rn) ? rn : 1;
            double triggerTime = double.TryParse(
                (cmbMappingRelayTriggerTime.SelectedItem as ComboBoxItem)?.Tag?.ToString(),
                System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture,
                out double rt) ? rt : 0.5;

            try
            {
                var relay = new RelayController(null);
                relay.CycleRelay(comPort, relayNumber,relayBaudRate, triggerTime);
            }
            catch (Exception ex)
            {
                MessageBox.Show(L.T("EditMapping_Relay_TestError") + ": " + ex.Message, L.T("Msg_Error"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void btnTestMappingCheckin_Click(object sender, RoutedEventArgs e)
        {
            string cardId = txtMappingTestCardId.Text;
            if (string.IsNullOrWhiteSpace(cardId))
            {
                txtMappingCheckinTestResult.Text = L.T("EditMapping_Checkin_NoCardId");
                txtMappingCheckinTestResult.Foreground = Brushes.Orange;
                return;
            }

            string checkinKey = txtMappingCheckinKey.Text.Trim();
            if (string.IsNullOrWhiteSpace(checkinKey))
            {
                txtMappingCheckinTestResult.Text = L.T("EditMapping_Checkin_NoCheckinKey");
                txtMappingCheckinTestResult.Foreground = Brushes.Orange;
                return;
            }

            btnTestMappingCheckin.IsEnabled = false;
            txtMappingCheckinTestResult.Text = L.T("EditMapping_Checkin_Running");
            txtMappingCheckinTestResult.Foreground = Brushes.Gray;

            try
            {
                using (var api = VirtuagymApiServiceFactory.CreateWithClubSecret(checkinKey))
                {
                    var result = await api.CheckinMemberAsync(cardId);

                    if (result != null)
                    {
                        string memberName = result.member != null
                            ? (result.member.name ?? (result.member.firstname + " " + result.member.lastname))
                            : L.T("Checkin_UnknownMember");

                        txtMappingCheckinTestResult.Text = string.Format(L.T("EditMapping_Checkin_Success"), memberName) + " (Status: " + result.status + ")";
                        txtMappingCheckinTestResult.Foreground = Brushes.Green;
                    }
                    else
                    {
                        txtMappingCheckinTestResult.Text = string.Format(L.T("EditMapping_Checkin_NoResult"), cardId) + ".";
                        txtMappingCheckinTestResult.Foreground = Brushes.Orange;
                    }
                }
            }
            catch (VirtuagymApiException apiEx)
            {
                txtMappingCheckinTestResult.Text = L.T("EditMapping_Checkin_ApiError") + ": " + apiEx.ApiStatusMessage + " (Code: " + apiEx.ApiStatusCode + ")";
                txtMappingCheckinTestResult.Foreground = Brushes.Red;
            }
            catch (Exception ex)
            {
                txtMappingCheckinTestResult.Text = L.T("Msg_Error") + ": " + ex.Message;
                txtMappingCheckinTestResult.Foreground = Brushes.Red;
            }
            finally
            {
                btnTestMappingCheckin.IsEnabled = true;
            }
        }

        private void btnMappingSave_Click(object sender, RoutedEventArgs e)
        {
            string inputType = (cmbMappingInputType.SelectedItem as ComboBoxItem)?.Tag?.ToString()
                ?? nameof(HardwareInputType.USBReader);

            if (inputType == nameof(HardwareInputType.USBReader) && string.IsNullOrWhiteSpace(GetSelectedDeviceId()))
            {
                MessageBox.Show(L.T("EditMapping_Validation_SelectDevice"), L.T("EditMapping_Validation_Title"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (inputType == nameof(HardwareInputType.QRCode) && cmbMappingCamera.SelectedItem == null)
            {
                MessageBox.Show(L.T("EditMapping_Validation_SelectCamera"), L.T("EditMapping_Validation_Title"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (string.IsNullOrWhiteSpace(txtMappingCheckinKey.Text))
            {
                MessageBox.Show(L.T("EditMapping_Validation_EnterCheckinKey"), L.T("EditMapping_Validation_Title"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!string.IsNullOrWhiteSpace(txtRepeatTimeMs.Text))
            {
                if (!int.TryParse(txtRepeatTimeMs.Text, out int rtValidation) || rtValidation <= 0)
                {
                    MessageBox.Show(L.T("EditMapping_Msg_InvalidRepeatTime"), L.T("EditMapping_Validation_Title"), MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }

            // v0-Validierung: CheckinKey enthält "CHECKIN" (v0-Format), aber keine device_id extrahierbar
            string saveCheckinKey = txtMappingCheckinKey.Text.Trim();
            if (!string.IsNullOrWhiteSpace(saveCheckinKey)
                && saveCheckinKey.Contains("CHECKIN", StringComparison.OrdinalIgnoreCase)
                && string.IsNullOrEmpty(VirtuagymApiBase.ExtractDeviceId(saveCheckinKey)))
            {
                MessageBox.Show(L.T("EditMapping_Tip_ApiMode_V0_Warn"), L.T("EditMapping_Validation_Title"), MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            ResultMapping = BuildMapping();
            DialogResult = true;
            Close();
        }

        private void btnMappingCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void PopulateHidProfiles()
        {
            cmbHidProfile.Items.Clear();
            foreach (var profile in HidProfileLoader.Load())
            {
                cmbHidProfile.Items.Add(new ComboBoxItem
                {
                    Content = profile.DisplayName,
                    Tag = profile.ProfileId
                });
            }
            if (cmbHidProfile.Items.Count > 0)
                cmbHidProfile.SelectedIndex = 0;
        }

        private HidProfile GetSelectedProfile()
        {
            string profileId = (cmbHidProfile.SelectedItem as ComboBoxItem)?.Tag?.ToString();
            return HidProfileLoader.GetById(profileId);
        }

        #region Kamera-Einstellungen (QR-Code)

        private void PopulateCameraBackends()
        {
            cmbCameraBackend.Items.Clear();

            List<int> addedValues = new List<int>();
            foreach (var api in Enum.GetValues(typeof(OpenCvSharp.VideoCaptureAPIs)))
            {
                string name = Enum.GetName(typeof(OpenCvSharp.VideoCaptureAPIs), api);
                int value = (int)api;

                if(addedValues.Contains(value))
                    continue;

                string label = api switch
                {
                    OpenCvSharp.VideoCaptureAPIs.ANY => L.T("EditMapping_CameraBackend_ANY"),
                    OpenCvSharp.VideoCaptureAPIs.V4L => "V4L/V4L2 [Linux]",
                    OpenCvSharp.VideoCaptureAPIs.FIREWIRE => $"IEEE 1394 / FireWire [{L.T("EditMapping_VideoBackend_CrossPlatform")}]",
                    OpenCvSharp.VideoCaptureAPIs.DSHOW => "DirectShow (DSHOW) [Windows]",
                    OpenCvSharp.VideoCaptureAPIs.PVAPI => $"PvAPI / Prosilica GigE [{L.T("EditMapping_VideoBackend_CrossPlatform")}]",
                    OpenCvSharp.VideoCaptureAPIs.OPENNI => $"OpenNI [{L.T("EditMapping_VideoBackend_CrossPlatform")}]",
                    OpenCvSharp.VideoCaptureAPIs.OPENNI_ASUS => $"OpenNI ASUS Xtion [{L.T("EditMapping_VideoBackend_CrossPlatform")}]",
                    OpenCvSharp.VideoCaptureAPIs.ANDROID => "Android MediaNDK [Android]",
                    OpenCvSharp.VideoCaptureAPIs.XIAPI => $"XIMEA Camera API [{L.T("EditMapping_VideoBackend_CrossPlatform")}]",
                    OpenCvSharp.VideoCaptureAPIs.AVFOUNDATION => "AVFoundation [macOS/iOS]",
                    OpenCvSharp.VideoCaptureAPIs.GIGANETIX => $"Smartek Giganetix GigE [{L.T("EditMapping_VideoBackend_CrossPlatform")}]",
                    OpenCvSharp.VideoCaptureAPIs.MSMF => "Media Foundation (MSMF) [Windows]",
                    OpenCvSharp.VideoCaptureAPIs.WINRT => "Windows Runtime (WINRT) [Windows]",
                    OpenCvSharp.VideoCaptureAPIs.INTELPERC => $"Intel RealSense (PerC) [{L.T("EditMapping_VideoBackend_CrossPlatform")}]",
                    OpenCvSharp.VideoCaptureAPIs.OPENNI2 => $"OpenNI2 [{L.T("EditMapping_VideoBackend_CrossPlatform")}]",
                    OpenCvSharp.VideoCaptureAPIs.OPENNI2_ASUS => $"OpenNI2 ASUS [{L.T("EditMapping_VideoBackend_CrossPlatform")}]",
                    OpenCvSharp.VideoCaptureAPIs.GPHOTO2 => "gPhoto2 [Linux/macOS]",
                    OpenCvSharp.VideoCaptureAPIs.GSTREAMER => $"GStreamer [{L.T("EditMapping_VideoBackend_CrossPlatform")}]",
                    OpenCvSharp.VideoCaptureAPIs.FFMPEG => $"FFmpeg [{L.T("EditMapping_VideoBackend_CrossPlatform")}]",
                    OpenCvSharp.VideoCaptureAPIs.IMAGES => $"{L.T("EditMapping_VideoBackend_ImageSequence")} [{L.T("EditMapping_VideoBackend_CrossPlatform")}]",
                    OpenCvSharp.VideoCaptureAPIs.ARAVIS => "Aravis GigE [Linux]",
                    OpenCvSharp.VideoCaptureAPIs.INTEL_MFX => "Intel Media SDK [Windows/Linux]",
                    OpenCvSharp.VideoCaptureAPIs.XINE => "Xine [Linux]",
                    _ => name ?? api.ToString()
                };
                cmbCameraBackend.Items.Add(new ComboBoxItem { Content = label, Tag = api.ToString() });
                addedValues.Add(value);
            }

            if (cmbCameraBackend.Items.Count > 0)
                cmbCameraBackend.SelectedIndex = 0;
        }

        private (int width, int height) GetSelectedResolution()
        {
            string tag = (cmbCameraResolution.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "640x480";
            var parts = tag.Split('x');
            if (parts.Length == 2
                && int.TryParse(parts[0], out int w)
                && int.TryParse(parts[1], out int h))
                return (w, h);
            return (640, 480);
        }

        private OpenCvSharp.VideoCaptureAPIs GetSelectedCaptureApi()
        {
            string tag = (cmbCameraBackend.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "ANY";
            if (Enum.TryParse<OpenCvSharp.VideoCaptureAPIs>(tag, out var api))
                return api;
            return OpenCvSharp.VideoCaptureAPIs.ANY;
        }

        #endregion

        #region Credit-Dienstleistungen

        private void PopulateCreditServices()
        {
            cmbCreditService.Items.Clear();

            // Erster Eintrag: Deaktiviert (keine Credit-Prüfung)
            cmbCreditService.Items.Add(new ComboBoxItem { Content = L.T("EditMapping_CreditService_Disabled"), Tag = null });

            foreach (var svc in ClubServiceLoader.Load())
            {
                if (!svc.isActive) continue;
                cmbCreditService.Items.Add(new ComboBoxItem
                {
                    Content = svc.DisplayName,
                    Tag = svc
                });
            }

            if (cmbCreditService.Items.Count > 0)
                cmbCreditService.SelectedIndex = 0;
        }

        private void SelectCreditService(string serviceId)
        {
            if (string.IsNullOrWhiteSpace(serviceId))
            {
                cmbCreditService.SelectedIndex = 0;
                return;
            }

            foreach (ComboBoxItem item in cmbCreditService.Items)
            {
                var svc = item.Tag as ClubService;
                if (svc != null && string.Equals(svc.service_id, serviceId, StringComparison.OrdinalIgnoreCase))
                {
                    cmbCreditService.SelectedItem = item;
                    return;
                }
            }

            // Service-ID nicht in der Liste → als neuen Eintrag hinzufügen (Rückwärtskompatibilität)
            var fallbackItem = new ComboBoxItem { Content = serviceId, Tag = new ClubService { service_id = serviceId } };
            cmbCreditService.Items.Add(fallbackItem);
            cmbCreditService.SelectedItem = fallbackItem;
        }

        private string GetSelectedCreditServiceId()
        {
            var svc = (cmbCreditService.SelectedItem as ComboBoxItem)?.Tag as ClubService;
            return svc?.service_id ?? "";
        }

        private int GetSelectedCreditClubId()
        {
            var svc = (cmbCreditService.SelectedItem as ComboBoxItem)?.Tag as ClubService;
            return svc?.club_id ?? 0;
        }

        #endregion
    }
}
