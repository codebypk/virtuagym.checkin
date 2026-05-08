using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Virtuagym.API.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Jablotron.API.Services;
using Virtuagym.CheckIn.Core.Helper;
using Virtuagym.CheckIn.Core.Services;
using Virtuagym.CheckIn.WPF.Properties;

namespace Virtuagym.CheckIn.WPF.Controls
{
    public partial class DeveloperJablotronGateTestControl : UserControl
    {
        private sealed class GateEntry
        {
            public string ServiceId { get; set; }
            public string CloudComponentId { get; set; }
            public string Name { get; set; }
            public bool CanControl { get; set; }
            public bool NeedAuthorization { get; set; }
            public string Display => string.Format("{0} ({1})", Name ?? "-", CloudComponentId ?? "-");
        }

        public DeveloperJablotronGateTestControl()
        {
            InitializeComponent();
            LoadGatesFromJson();
        }

        public void ApplyLocalization()
        {
            lblGate.Text = L.T("Dev_JabGate_LblGate");
            lblDuration.Text = L.T("Dev_JabGate_LblDuration");
            lblState.Text = L.T("Dev_JabGate_LblState");
            chkToggleState.Content = L.T("Dev_JabGate_StateOn");
            btnReloadGates.Content = L.T("Dev_JabGate_BtnReload");
            btnApplyToggle.Content = L.T("Dev_JabGate_BtnToggle");
            txtHint.Text = L.T("Dev_JabGate_Hint");
        }

        private void btnReloadGates_Click(object sender, RoutedEventArgs e)
        {
            LoadGatesFromJson();
        }

        private async void btnApplyToggle_Click(object sender, RoutedEventArgs e)
        {
            var selected = cmbGates.SelectedItem as GateEntry;
            if (selected == null)
            {
                SetStatus(L.T("Dev_JabGate_SelectGateFirst"), Brushes.Orange);
                return;
            }

            if (!selected.CanControl)
            {
                SetStatus(L.T("Dev_JabGate_NotControllable"), Brushes.Orange);
                return;
            }

            if (!int.TryParse(txtDurationSeconds.Text?.Trim(), out var seconds) || seconds < 0)
            {
                SetStatus(L.T("Dev_JabGate_InvalidSeconds"), Brushes.Orange);
                return;
            }

            var targetStateOn = chkToggleState.IsChecked == true;
            btnApplyToggle.IsEnabled = false;

            try
            {
                using (var client = new JablotronCloudService(
                    Settings.Default.JablotronApiUrl,
                    Settings.Default.JablotronApiUsername,
                    Settings.Default.JablotronApiPassword,
                    Settings.Default.JablotronApiPinCode))
                {
                    client.PerformLogin();

                    int? serviceId = null;
                    if (int.TryParse(selected.ServiceId, out var parsedServiceId) && parsedServiceId > 0)
                        serviceId = parsedServiceId;

                    var initialState = targetStateOn ? "ON" : "OFF";
                    var ok = client.ControlProgrammableGate(serviceId, selected.CloudComponentId, initialState);
                    if (!ok)
                        throw new InvalidOperationException(L.T("Dev_JabGate_ControlNoSuccess"));

                    SetStatus(string.Format(L.T("Dev_JabGate_SetStateOk"), selected.Name, initialState), Brushes.Green);

                    if (targetStateOn && seconds > 0)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(seconds));

                        var offOk = client.ControlProgrammableGate(serviceId, selected.CloudComponentId, "OFF");
                        if (!offOk)
                            throw new InvalidOperationException(L.T("Dev_JabGate_AutoOffNoSuccess"));

                        chkToggleState.IsChecked = false;
                        SetStatus(string.Format(L.T("Dev_JabGate_AutoOffDone"), selected.Name, seconds), Brushes.Green);
                    }
                }
            }
            catch (Exception ex)
            {
                SetStatus(string.Format(L.T("Dev_JabGate_ErrorWithMessage"), ex.Message), Brushes.Red);
            }
            finally
            {
                btnApplyToggle.IsEnabled = true;
            }
        }

        private void LoadGatesFromJson()
        {
            try
            {
                var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, Constants.JablotronGatesFilePath);
                if (!File.Exists(path))
                {
                    cmbGates.ItemsSource = null;
                    SetStatus(L.T("Dev_JabGate_FileMissing"), Brushes.Orange);
                    return;
                }

                var json = File.ReadAllText(path);
                var serializer = new JsonSerializerAdapter();
                var rows = serializer.Deserialize<List<Dictionary<string, object>>>(json) ?? new List<Dictionary<string, object>>();

                var gates = new List<GateEntry>();
                foreach (var row in rows)
                {
                    gates.Add(new GateEntry
                    {
                        ServiceId = GetString(row, "service-id"),
                        CloudComponentId = GetString(row, "cloud-component-id"),
                        Name = GetString(row, "name"),
                        CanControl = GetBool(row, "can-control"),
                        NeedAuthorization = GetBool(row, "need-authorization")
                    });
                }

                cmbGates.ItemsSource = gates;
                if (gates.Count > 0)
                    cmbGates.SelectedIndex = 0;

                SetStatus(string.Format(L.T("Common_Pg_LoadedCount"), gates.Count), Brushes.Gray);
            }
            catch (Exception ex)
            {
                cmbGates.ItemsSource = null;
                SetStatus(string.Format(L.T("Common_Pg_LoadError"), ex.Message), Brushes.Red);
            }
        }

        private static string GetString(Dictionary<string, object> row, string key)
        {
            if (row == null || !row.ContainsKey(key) || row[key] == null)
                return string.Empty;
            return Convert.ToString(row[key]);
        }

        private static bool GetBool(Dictionary<string, object> row, string key)
        {
            if (row == null || !row.ContainsKey(key) || row[key] == null)
                return false;

            var value = row[key];
            if (value is bool b)
                return b;

            bool parsed;
            return bool.TryParse(Convert.ToString(value), out parsed) && parsed;
        }

        private void SetStatus(string message, Brush color)
        {
            txtStatus.Text = message;
            txtStatus.Foreground = color;
        }
    }
}
