using Hardware.Interfaces;
using Hardware.Models;
using Hardware.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Virtuagym.API.Serialization;
using Virtuagym.CheckIn.Core.Models;
using Virtuagym.CheckIn.Core.Services;
using Virtuagym.CheckIn.WPF.Properties;

namespace Virtuagym.CheckIn.WPF.Controls
{
    public partial class CheckinMappingsControl : UserControl
    {
        private readonly IHidDeviceService _hidDeviceService = new HidSharpDeviceService();
        private ObservableCollection<CheckinClientMapping> _checkinClientMappings;
        private List<HidDeviceInfo> _availableHidDevices;

        public ObservableCollection<CheckinClientMapping> CheckinClientMappings => _checkinClientMappings;

        public CheckinMappingsControl()
        {
            InitializeComponent();
        }

        public void ApplyLocalization()
        {
            btnAddCheckinMapping.Content = L.T("Settings_Btn_AddMapping");
            btnEditCheckinMapping.Content = L.T("Settings_Btn_EditMapping");
            btnDeleteCheckinMapping.Content = L.T("Settings_Btn_DeleteMapping");

            if (dataGridCheckinMappings.Columns.Count >= 10)
            {
                dataGridCheckinMappings.Columns[0].Header = L.T("Settings_Col_Name");
                dataGridCheckinMappings.Columns[1].Header = L.T("Settings_Col_Type");
                dataGridCheckinMappings.Columns[2].Header = L.T("Settings_Col_Device");
                dataGridCheckinMappings.Columns[3].Header = L.T("Settings_Col_Manufacturer");
                dataGridCheckinMappings.Columns[4].Header = L.T("Settings_Col_CheckinKey");
                dataGridCheckinMappings.Columns[5].Header = L.T("Settings_Col_Relay");
                dataGridCheckinMappings.Columns[6].Header = L.T("Settings_Col_RelayCom");
                dataGridCheckinMappings.Columns[7].Header = L.T("Settings_Col_RelayNr");
                dataGridCheckinMappings.Columns[8].Header = L.T("Settings_Col_PG");
                dataGridCheckinMappings.Columns[9].Header = L.T("Settings_Col_PGGate");
            }
        }

        public void LoadSettings()
        {
            RefreshAvailableDevices();

            _checkinClientMappings = new ObservableCollection<CheckinClientMapping>();
            try
            {
                string mappingsJson = Settings.Default.CheckinClientMappings;
                if (!string.IsNullOrWhiteSpace(mappingsJson))
                {
                    var serializer = new JsonSerializerAdapter();
                    var loaded = serializer.Deserialize<List<CheckinClientMapping>>(mappingsJson);
                    if (loaded != null)
                    {
                        foreach (var m in loaded)
                        {
                            m.EnsureUuid();
                            _checkinClientMappings.Add(m);
                        }
                    }
                }
            }
            catch { }
            dataGridCheckinMappings.ItemsSource = _checkinClientMappings;
            UpdateMappingDeviceDescriptions();
        }

        public void SaveSettings(Action<string, string> updateSetting)
        {
            var serializer = new JsonSerializerAdapter();
            var mappingsList = _checkinClientMappings.ToList();
            string mappingsJson = serializer.Serialize(mappingsList);
            updateSetting("CheckinClientMappings", mappingsJson);
        }

        public bool ValidateMappings()
        {
            return _checkinClientMappings != null && _checkinClientMappings.Count > 0;
        }

        private void RefreshAvailableDevices()
        {
            _availableHidDevices = new List<HidDeviceInfo>();
            var hidDevices = _hidDeviceService.GetDevices();
            foreach (var device in hidDevices)
            {
                string deviceId = ExtractDeviceId(device.DevicePath);
                if (string.IsNullOrEmpty(deviceId))
                    continue;

                string manufacturer = device.Manufacturer?.Trim() ?? "";
                string product = device.ProductName?.Trim() ?? "";

                if (IsCommonNonRfidDevice(manufacturer, product, device.Description))
                    continue;

                string displayName = BuildDisplayName(deviceId, manufacturer, product, device.Description);
                _availableHidDevices.Add(new HidDeviceInfo
                {
                    DevicePath = device.DevicePath,
                    DeviceId = deviceId,
                    Manufacturer = manufacturer,
                    Product = product
                });
            }
        }

        private static bool IsCommonNonRfidDevice(string manufacturer, string product, string description)
        {
            // Keyword-Filter auf Beschreibung, Hersteller und Produktname
            string combined = $"{manufacturer} {product} {description}".ToLowerInvariant();
            string[] excludeKeywords =
            {
                "keyboard", "tastatur",
                "mouse", "maus",
                "audio", "speaker", "headset", "headphone", "microphone",
                "video", "webcam", "camera",
                "printer", "drucker",
                "storage", "disk", "drive"
            };
            return excludeKeywords.Any(k => combined.Contains(k));
        }

        private string BuildDisplayName(string deviceId, string manufacturer, string product, string description)
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(manufacturer)) parts.Add(manufacturer.Trim());
            if (!string.IsNullOrWhiteSpace(product)) parts.Add(product.Trim());
            if (parts.Count == 0 && !string.IsNullOrWhiteSpace(description)) parts.Add(description);
            if (parts.Count == 0) parts.Add(L.T("DeviceMapping_HidDevice"));
            return $"{deviceId} - {string.Join(" / ", parts)}";
        }

        private async void UpdateMappingDeviceDescriptions()
        {
            if (_checkinClientMappings == null) return;
            IReadOnlyList<CameraDeviceInfo> cameras = null;

            foreach (var mapping in _checkinClientMappings)
            {
                if (mapping.InputType == nameof(HardwareInputType.QRCode))
                {
                    if (cameras == null) cameras = await CameraDiscoveryService.GetCamerasAsync();
                    var cam = cameras.FirstOrDefault(c => c.Index == mapping.CameraIndex);
                    if (cam != null)
                    {
                        mapping.DeviceDescription = cam.DisplayName;
                        mapping.IsDeviceAvailable = true;
                    }
                    else
                    {
                        mapping.DeviceDescription = string.Format(L.T("DeviceMapping_CameraUnavailable"), mapping.CameraIndex);
                        mapping.IsDeviceAvailable = false;
                    }
                }
                else
                {
                    var hidDevice = _availableHidDevices?.FirstOrDefault(d => d.DeviceId == mapping.DeviceID);
                    if (hidDevice != null)
                    {
                        mapping.Manufacturer = hidDevice.Manufacturer?.Trim() ?? "";
                        var parts = new List<string>();
                        if (!string.IsNullOrWhiteSpace(hidDevice.Manufacturer)) parts.Add(hidDevice.Manufacturer.Trim());
                        if (!string.IsNullOrWhiteSpace(hidDevice.Product)) parts.Add(hidDevice.Product.Trim());
                        string description = parts.Count > 0 ? string.Join(" / ", parts) : L.T("DeviceMapping_HidDevice");
                        mapping.DeviceDescription = $"{mapping.DeviceID} - {description}";
                        mapping.IsDeviceAvailable = true;
                    }
                    else
                    {
                        mapping.Manufacturer = "";
                        mapping.DeviceDescription = !string.IsNullOrWhiteSpace(mapping.DeviceID)
                            ? string.Format(L.T("DeviceMapping_Disconnected"), mapping.DeviceID) 
                            : L.T("DeviceMapping_NoDevice");
                        mapping.IsDeviceAvailable = false;
                    }
                }
            }
        }

        private string ExtractDeviceId(string devicePath)
        {
            if (string.IsNullOrEmpty(devicePath)) return null;
            string lowerPath = devicePath.ToLower();
            int vidIndex = lowerPath.IndexOf("vid_");
            if (vidIndex >= 0)
            {
                int endIndex = lowerPath.IndexOf("#{", vidIndex);
                if (endIndex < 0) endIndex = devicePath.Length;
                string extracted = devicePath.Substring(vidIndex, endIndex - vidIndex);
                extracted = extracted.Replace("\\", "&").Replace("#", "&");
                return extracted.TrimEnd('&');
            }
            return null;
        }

        /// <summary>
        /// Gibt die HID-Geräte zurück, die noch nicht von anderen Mappings verwendet werden.
        /// <paramref name="excludeDeviceId"/> wird nicht herausgefiltert (eigenes Gerät beim Bearbeiten).
        /// </summary>
        private HidDeviceInfo[] GetUnusedHidDevices(string excludeDeviceId = null)
        {
            var usedDeviceIds = new HashSet<string>(
                (_checkinClientMappings ?? Enumerable.Empty<CheckinClientMapping>())
                    .Where(m => !string.IsNullOrWhiteSpace(m.DeviceID))
                    .Select(m => m.DeviceID),
                StringComparer.OrdinalIgnoreCase);

            if (!string.IsNullOrWhiteSpace(excludeDeviceId))
                usedDeviceIds.Remove(excludeDeviceId);

            return (_availableHidDevices ?? new List<HidDeviceInfo>())
                .Where(d => !usedDeviceIds.Contains(d.DeviceId))
                .ToArray();
        }

        private void btnAddCheckinMapping_Click(object sender, RoutedEventArgs e)
        {
            var hidDevices = GetUnusedHidDevices();
            var deviceIds = hidDevices.Select(d => d.DeviceId).Distinct().ToArray();
            var dialog = new EditCheckinClientMappingWindow(null, deviceIds, hidDevices);
            dialog.RefreshDevicesCallback = () =>
            {
                RefreshAvailableDevices();
                return GetUnusedHidDevices();
            };
            dialog.Owner = Window.GetWindow(this);

            if (dialog.ShowDialog() == true && dialog.ResultMapping != null)
            {
                dialog.ResultMapping.EnsureUuid();
                _checkinClientMappings.Add(dialog.ResultMapping);
                UpdateMappingDeviceDescriptions();
            }
        }

        private void btnEditCheckinMapping_Click(object sender, RoutedEventArgs e)
        {
            var selected = dataGridCheckinMappings.SelectedItem as CheckinClientMapping;
            if (selected == null)
            {
                MessageBox.Show(L.T("Settings_Msg_SelectMapping"), L.T("Msg_Note"), MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            int index = _checkinClientMappings.IndexOf(selected);
            var hidDevices = GetUnusedHidDevices(selected.DeviceID);
            var deviceIds = hidDevices.Select(d => d.DeviceId).Distinct().ToArray();
            var dialog = new EditCheckinClientMappingWindow(selected, deviceIds, hidDevices);
            dialog.RefreshDevicesCallback = () =>
            {
                RefreshAvailableDevices();
                return GetUnusedHidDevices(selected.DeviceID);
            };
            dialog.Owner = Window.GetWindow(this);

            if (dialog.ShowDialog() == true && dialog.ResultMapping != null)
            {
                dialog.ResultMapping.EnsureUuid();
                _checkinClientMappings[index] = dialog.ResultMapping;
                UpdateMappingDeviceDescriptions();
            }
        }

        private void btnDeleteCheckinMapping_Click(object sender, RoutedEventArgs e)
        {
            var selected = dataGridCheckinMappings.SelectedItem as CheckinClientMapping;
            if (selected == null)
            {
                MessageBox.Show(L.T("Settings_Msg_SelectMapping"), L.T("Msg_Note"), MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var result = MessageBox.Show(L.T("Settings_Msg_DeleteConfirm"), L.T("Settings_Msg_DeleteTitle"), MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result == MessageBoxResult.Yes)
            {
                _checkinClientMappings.Remove(selected);
            }
        }
    }
}
