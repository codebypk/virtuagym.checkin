using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Virtuagym.API.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Jablotron.API.Services;
using Virtuagym.CheckIn.Core.Models;
using Virtuagym.CheckIn.Core.Helper;
using Virtuagym.CheckIn.Core.Models;
using Virtuagym.CheckIn.Core.Services;
using Virtuagym.CheckIn.Core.Models;
using Virtuagym.CheckIn.Core.Models;
using Virtuagym.CheckIn.Core.Models;
using Virtuagym.CheckIn.WPF.Properties;
using Jablotron.API.CloudApi.Models;
using Jablotron.API.SIA.Models;
using System.Collections.ObjectModel;
using System.Threading;

namespace Virtuagym.CheckIn.WPF.Controls
{
    public partial class JablotronClientControl : UserControl
    {
        private SiaUdpServer _siaServer;
        private readonly ObservableCollection<SiaMessageDisplayItem> _siaMessages = new();

        public JablotronClientControl()
        {
            InitializeComponent();
            dataGridSiaMessages.ItemsSource = _siaMessages;
        }

        public void ApplyLocalization()
        {
            tabJablotronApiConnection.Header = L.T("Settings_SubTab_JablotronApiConnection");
            tabJablotronServiceAndGates.Header = L.T("Settings_SubTab_JablotronServiceAndGates");
            tabJablotronServices.Header = L.T("Settings_SubTab_JablotronServices");
            tabJablotronProgrammableGates.Header = L.T("Settings_SubTab_JablotronProgrammableGates");
            grpJablotronApi.Header = L.T("Settings_Grp_JablotronApi");
            lblJablotronApiUrl.Text = L.T("Settings_Lbl_JablotronApiUrl");
            lblJablotronApiUsername.Text = L.T("Settings_Lbl_JablotronApiUsername");
            lblJablotronApiPassword.Text = L.T("Settings_Lbl_JablotronApiPassword");
            grpJablotronConnectionTest.Header = L.T("Settings_Grp_ConnectionTest");
            btnTestJablotronConnection.Content = L.T("Settings_Btn_TestConnection");
            btnLoadServices.Content = L.T("Settings_Btn_LoadServices");
            btnLoadProgrammableGates.Content = L.T("Settings_Btn_LoadProgrammableGates");
            tabJablotronFLink.Header = L.T("Settings_SubTab_JablotronFLink");
            developerJablotronControl.ApplyLocalization();

            // SIA DC-09
            tabSiaDc09.Header = L.T("Settings_SubTab_SiaDc09");
            lblSiaPort.Text = L.T("Settings_Lbl_SiaPort");
            btnSiaStartStop.Content = L.T("Settings_Btn_SiaStart");
            btnSiaClear.Content = L.T("Settings_Btn_SiaClear");

            // DataGrid column headers
            var siaCols = dataGridSiaMessages.Columns;
            if (siaCols.Count >= 8)
            {
                siaCols[0].Header = L.T("Settings_Col_SiaTime");
                siaCols[1].Header = L.T("Settings_Col_SiaRemoteEP");
                siaCols[2].Header = L.T("Settings_Col_SiaAccount");
                siaCols[3].Header = L.T("Settings_Col_SiaEventCode");
                siaCols[4].Header = L.T("Settings_Col_SiaDescription");
                siaCols[5].Header = L.T("Settings_Col_SiaZone");
                siaCols[6].Header = L.T("Settings_Col_SiaUser");
                siaCols[7].Header = L.T("Settings_Col_SiaRaw");
            }
        }

        public void LoadSettings()
        {
            txtJablotronApiUrl.Text = Settings.Default.JablotronApiUrl;
            txtJablotronApiUsername.Text = Settings.Default.JablotronApiUsername;
            txtJablotronApiPassword.Password = Settings.Default.JablotronApiPassword;
            txtJablotronApiPinCode.Password = Settings.Default.JablotronApiPinCode;
            developerJablotronControl.LoadSettings();
        }

        public void SaveSettings(Action<string, string> updateSetting)
        {
            updateSetting("JablotronApiUrl", txtJablotronApiUrl.Text);
            updateSetting("JablotronApiUsername", txtJablotronApiUsername.Text);
            updateSetting("JablotronApiPassword", txtJablotronApiPassword.Password);
            updateSetting("JablotronApiPinCode", txtJablotronApiPinCode.Password);
            developerJablotronControl.SaveSettings(updateSetting);
        }

        private void btnTestJablotronConnection_Click(object sender, RoutedEventArgs e)
        {
            btnTestJablotronConnection.IsEnabled = false;
            txtJablotronTestResult.Text = L.T("Settings_Msg_TestConnecting");
            txtJablotronTestResult.Foreground = Brushes.Gray;

            try
            {
                using (var client = new JablotronCloudService(
                    txtJablotronApiUrl.Text,
                    txtJablotronApiUsername.Text,
                    txtJablotronApiPassword.Password))
                {
                    client.PerformLogin();
                    var services = client.GetServices();

                    txtJablotronTestResult.Text = string.Format(
                        L.T("Settings_Msg_JablotronTestSuccess"),
                        services.Count);
                    txtJablotronTestResult.Foreground = Brushes.Green;
                }
            }
            catch (Exception ex)
            {
                txtJablotronTestResult.Text = string.Format(L.T("Settings_Msg_TestError"), ex.Message);
                txtJablotronTestResult.Foreground = Brushes.Red;
            }
            finally
            {
                btnTestJablotronConnection.IsEnabled = true;
            }
        }

        private void btnLoadServices_Click(object sender, RoutedEventArgs e)
        {
            btnLoadServices.IsEnabled = false;
            txtServicesStatus.Text = L.T("Settings_Msg_LoadingServices");
            txtServicesStatus.Foreground = Brushes.Gray;
            dataGridServices.ItemsSource = null;

            try
            {
                using (var client = new JablotronCloudService(
                    txtJablotronApiUrl.Text,
                    txtJablotronApiUsername.Text,
                    txtJablotronApiPassword.Password))
                {
                    client.PerformLogin();
                    var services = client.GetServices();

                    if (services != null && services.Count > 0)
                    {
                        dataGridServices.ItemsSource = services;
                        txtServicesStatus.Text = string.Format(L.T("Settings_Msg_ServicesLoaded"), services.Count);
                        txtServicesStatus.Foreground = Brushes.Green;
                    }
                    else
                    {
                        txtServicesStatus.Text = L.T("Settings_Msg_NoServicesFound");
                        txtServicesStatus.Foreground = Brushes.Orange;
                    }
                }
            }
            catch (Exception ex)
            {
                txtServicesStatus.Text = string.Format(L.T("Settings_Msg_TestError"), ex.Message);
                txtServicesStatus.Foreground = Brushes.Red;
            }
            finally
            {
                btnLoadServices.IsEnabled = true;
            }
        }

        private void btnLoadProgrammableGates_Click(object sender, RoutedEventArgs e)
        {
            btnLoadProgrammableGates.IsEnabled = false;
            txtProgrammableGatesStatus.Text = L.T("Settings_Msg_LoadingGates");
            txtProgrammableGatesStatus.Foreground = Brushes.Gray;
            dataGridProgrammableGates.ItemsSource = null;

            try
            {
                using (var client = new JablotronCloudService(
                    txtJablotronApiUrl.Text,
                    txtJablotronApiUsername.Text,
                    txtJablotronApiPassword.Password))
                {
                    client.PerformLogin();
                    var gatesData = client.GetProgrammableGates();

                    if (gatesData?.ProgrammableGates != null && gatesData.ProgrammableGates.Count > 0)
                    {
                        var statesLookup = (gatesData.States ?? new List<ServiceState>())
                            .ToDictionary(s => s.CloudComponentId ?? string.Empty, s => s.State ?? string.Empty);

                        var displayItems = gatesData.ProgrammableGates.Select(g => new ProgrammableGateDisplayItem
                        {
                            CloudComponentId = g.CloudComponentId,
                            Name = g.Name,
                            CanControl = g.CanControl,
                            NeedAuthorization = g.NeedAuthorization,
                            State = statesLookup.TryGetValue(g.CloudComponentId ?? string.Empty, out var state) ? state : string.Empty
                        }).ToList();

                        dataGridProgrammableGates.ItemsSource = displayItems;

                        SaveProgrammableGatesToJson(gatesData,client.CurrentServiceId);

                        txtProgrammableGatesStatus.Text = string.Format(
                            L.T("Settings_Msg_GatesLoaded"),
                            gatesData.ProgrammableGates.Count);
                        txtProgrammableGatesStatus.Foreground = Brushes.Green;
                    }
                    else
                    {
                        txtProgrammableGatesStatus.Text = L.T("Settings_Msg_NoGatesFound");
                        txtProgrammableGatesStatus.Foreground = Brushes.Orange;
                    }
                }
            }
            catch (Exception ex)
            {
                txtProgrammableGatesStatus.Text = string.Format(L.T("Settings_Msg_TestError"), ex.Message);
                txtProgrammableGatesStatus.Foreground = Brushes.Red;
            }
            finally
            {
                btnLoadProgrammableGates.IsEnabled = true;
            }
        }

        private void btnSiaStartStop_Click(object sender, RoutedEventArgs e)
        {
            if (_siaServer != null && _siaServer.IsRunning)
            {
                _siaServer.StopAsync().GetAwaiter().GetResult();
                _siaServer.Dispose();
                _siaServer = null;
                btnSiaStartStop.Content = L.T("Settings_Btn_SiaStart");
                txtSiaStatus.Text = L.T("Settings_Msg_SiaStopped");
                txtSiaStatus.Foreground = Brushes.Gray;
                txtSiaPort.IsEnabled = true;
            }
            else
            {
                if (!int.TryParse(txtSiaPort.Text, out var port) || port < 1 || port > 65535)
                {
                    txtSiaStatus.Text = L.T("Settings_Msg_SiaInvalidPort");
                    txtSiaStatus.Foreground = Brushes.Red;
                    return;
                }

                try
                {
                    _siaServer = new SiaUdpServer(new SiaUdpServerOptions { Port = port });
                    _siaServer.MessageReceived += SiaServer_MessageReceived;
                    _siaServer.ReceiveError += SiaServer_ReceiveError;
                    _siaServer.StartAsync().GetAwaiter().GetResult();

                    btnSiaStartStop.Content = L.T("Settings_Btn_SiaStop");
                    txtSiaStatus.Text = string.Format(L.T("Settings_Msg_SiaRunning"), port);
                    txtSiaStatus.Foreground = Brushes.Green;
                    txtSiaPort.IsEnabled = false;
                }
                catch (Exception ex)
                {
                    txtSiaStatus.Text = string.Format(L.T("Settings_Msg_SiaReceiveError"), ex.Message);
                    txtSiaStatus.Foreground = Brushes.Red;
                    _siaServer?.Dispose();
                    _siaServer = null;
                }
            }
        }

        private void SiaServer_MessageReceived(object sender, SiaMessageReceivedEventArgs e)
        {
            Dispatcher.BeginInvoke(() =>
            {
                _siaMessages.Insert(0, new SiaMessageDisplayItem
                {
                    ReceivedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                    RemoteEndPoint = e.RemoteEndPoint?.ToString() ?? "",
                    AccountNumber = e.Message?.AccountNumber ?? "",
                    EventCode = e.Message?.EventCode ?? "",
                    EventDescription = e.Message?.EventDescriptions != null ? string.Join(", ", e.Message.EventDescriptions) : "",
                    Zone = e.Message?.Zone ?? "",
                    User = e.Message?.User ?? "",
                    RawText = e.Message?.RawText ?? ""
                });
            });
        }

        private void SiaServer_ReceiveError(object sender, Exception ex)
        {
            Dispatcher.BeginInvoke(() =>
            {
                txtSiaStatus.Text = string.Format(L.T("Settings_Msg_SiaReceiveError"), ex.Message);
                txtSiaStatus.Foreground = Brushes.Orange;
            });
        }

        private void btnSiaClear_Click(object sender, RoutedEventArgs e)
        {
            _siaMessages.Clear();
        }

        private void SaveProgrammableGatesToJson(ProgrammableGatesData gatesData, int? serviceId)
        {
            var exportList = (gatesData.ProgrammableGates ?? new List<ProgrammableGateItem>())
                .Select(g => new Dictionary<string, object>
                {
                    { "service-id", serviceId },
                    { "cloud-component-id", g.CloudComponentId ?? string.Empty },
                    { "name", g.Name ?? string.Empty },
                    { "can-control", g.CanControl },
                    { "need-authorization", g.NeedAuthorization }
                })
                .ToList();

            var outputPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, Constants.JablotronGatesFilePath);
            var outputDir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrWhiteSpace(outputDir) && !Directory.Exists(outputDir))
                Directory.CreateDirectory(outputDir);

            var serializer = new JsonSerializerAdapter();
            var json = serializer.Serialize(exportList);
            File.WriteAllText(outputPath, json);
        }

        private sealed class SiaMessageDisplayItem
        {
            public string ReceivedAt { get; set; } = "";
            public string RemoteEndPoint { get; set; } = "";
            public string AccountNumber { get; set; } = "";
            public string EventCode { get; set; } = "";
            public string EventDescription { get; set; } = "";
            public string Zone { get; set; } = "";
            public string User { get; set; } = "";
            public string RawText { get; set; } = "";
        }
    }
}
