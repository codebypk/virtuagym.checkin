using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Virtuagym.CheckIn.Core.Services;
using Virtuagym.CheckIn.WPF.Services;
using Virtuagym.CheckIn.Core.Helper;
using Virtuagym.API.Services;
using Virtuagym.CheckIn.WPF.Properties;

namespace Virtuagym.CheckIn.WPF.Controls
{
    public partial class ApiConnectionControl : UserControl
    {
        public ApiConnectionControl()
        {
            InitializeComponent();
        }

        public void ApplyLocalization()
        {
            grpApiGeneral.Header = L.T("Settings_Grp_ApiGeneral");
            lblApiKey.Text = L.T("Settings_Lbl_ApiKey");
            lblServerUrl.Text = L.T("Settings_Lbl_ServerUrl");
            lblClubSecret.Text = L.T("Settings_Lbl_ClubSecret");
            grpV0Api.Header = L.T("Settings_Grp_v0Api");
            lblCheckinApiKey.Text = L.T("Settings_Lbl_CheckinApiKey");
            lblCheckinApiUsername.Text = L.T("Settings_Lbl_CheckinApiUsername");
            lblCheckinApiPassword.Text = L.T("Settings_Lbl_CheckinApiPassword");
            txtCheckinApiUsername.ToolTip = L.T("Settings_Tip_CheckinApiUsername");
            txtCheckinApiPassword.ToolTip = L.T("Settings_Tip_CheckinApiPassword");
            grpConnectionTest.Header = L.T("Settings_Grp_ConnectionTest");
            btnTestVirtuagymConnection.Content = L.T("Settings_Btn_TestConnection");
            grpMemberCache.Header = L.T("Settings_Grp_MemberCache");
            txtCacheLocalDesc.Text = L.T("Settings_Lbl_CacheLocalDesc");
            chkMemberCacheEnabled.Content = L.T("Settings_Lbl_MemberCacheEnabled");
            chkCacheSyncEnabled.Content = L.T("Settings_Lbl_CacheSyncEnabled");
            lblSyncMode.Text = L.T("Settings_Lbl_SyncMode");
            cmbSyncModeInterval.Content = L.T("Settings_Lbl_SyncModeInterval");
            cmbSyncModeDailyTime.Content = L.T("Settings_Lbl_SyncModeDailyTime");
            lblSyncInterval.Text = L.T("Settings_Lbl_SyncInterval");
            lblSyncDailyTime.Text = L.T("Settings_Lbl_SyncDailyTime");
            txtCacheSyncDailyTime.ToolTip = L.T("Settings_Tip_SyncDailyTime");
            btnCacheSyncNow.Content = L.T("Settings_Btn_SyncNow");
            btnCacheClear.Content = L.T("Settings_Btn_ClearCache");
        }

        public void LoadSettings()
        {
            txtVirtuagymApiKey.Text = Settings.Default.VirtuagymApiKey;
            txtVirtuagymServerUrl.Text = Settings.Default.VirtuagymServerUrl;
            txtVirtuagymClubSecret.Text = Settings.Default.VirtuagymClubSecret;
            txtCheckinApiKey.Text = Settings.Default.MemberCheckinApiV0Key;
            txtCheckinApiUsername.Text = Settings.Default.MemberCheckinApiV0Username;
            txtCheckinApiPassword.Password = Settings.Default.MemberCheckinApiV0Password;
            chkMemberCacheEnabled.IsChecked = Settings.Default.MemberCacheEnabled;
            chkCacheSyncEnabled.IsChecked = Settings.Default.CacheSyncEnabled;
            txtCacheSyncIntervalMinutes.Text = Settings.Default.CacheSyncIntervalMinutes.ToString();
            txtCacheSyncDailyTime.Text = Settings.Default.CacheSyncDailyTime ?? "02:00";
            txtCreditsCacheTtlMinutes.Text = Settings.Default.CreditsCacheTtlMinutes.ToString();

            // Sync-Modus ComboBox setzen
            string mode = (Settings.Default.CacheSyncMode ?? "").Trim();
            if (string.Equals(mode, "DailyTime", StringComparison.OrdinalIgnoreCase))
                cmbSyncMode.SelectedIndex = 1;
            else
                cmbSyncMode.SelectedIndex = 0;

            UpdateCacheControlStates();
            UpdateCacheSyncStatus();
        }

        public void SaveSettings(Action<string, string> updateSetting)
        {
            updateSetting("VirtuagymApiKey", txtVirtuagymApiKey.Text);
            updateSetting("VirtuagymServerUrl", txtVirtuagymServerUrl.Text);
            updateSetting("VirtuagymClubSecret", txtVirtuagymClubSecret.Text);
            updateSetting("MemberCheckinApiV0Key", txtCheckinApiKey.Text);
            updateSetting("MemberCheckinApiV0Username", txtCheckinApiUsername.Text);
            updateSetting("MemberCheckinApiV0Password", txtCheckinApiPassword.Password);
            updateSetting("MemberCacheEnabled", chkMemberCacheEnabled.IsChecked.ToString());
            updateSetting("CacheSyncEnabled", chkCacheSyncEnabled.IsChecked.ToString());
            updateSetting("CacheSyncIntervalMinutes", txtCacheSyncIntervalMinutes.Text);

            var selectedMode = (cmbSyncMode.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "Interval";
            updateSetting("CacheSyncMode", selectedMode);
            updateSetting("CacheSyncDailyTime", txtCacheSyncDailyTime.Text);
            updateSetting("CreditsCacheTtlMinutes", txtCreditsCacheTtlMinutes.Text);
        }

        private void UpdateCacheSyncStatus()
        {
            try
            {
                var cache = new MemberCacheService();
                int count = cache.GetCount();
                var lastSync = cache.GetLastSyncTime();
                cache.Dispose();

                string status = $"{count} Einträge im Cache.";
                if (lastSync.HasValue)
                    status += $" Letzte Synchronisation: {lastSync.Value:dd.MM.yyyy HH:mm}";
                txtCacheSyncStatus.Text = status;
            }
            catch
            {
                txtCacheSyncStatus.Text = "Cache-Status konnte nicht gelesen werden.";
            }
        }

        private async void btnTestVirtuagymConnection_Click(object sender, RoutedEventArgs e)
        {
            btnTestVirtuagymConnection.IsEnabled = false;
            txtVirtuagymTestResult.Text = L.T("Settings_Msg_TestConnecting");
            txtVirtuagymTestResult.Foreground = Brushes.Gray;

            try
            {
                using (var api = VirtuagymApiServiceFactory.Create(
                    txtVirtuagymApiKey.Text,
                    txtVirtuagymServerUrl.Text,
                    txtVirtuagymClubSecret.Text))
                {
                    var result = await api.TestConnectionAsync();

                    if (result.Success)
                    {
                        txtVirtuagymTestResult.Text = result.Message;
                        txtVirtuagymTestResult.Foreground = Brushes.Green;
                    }
                    else
                    {
                        txtVirtuagymTestResult.Text = result.Message;
                        txtVirtuagymTestResult.Foreground = Brushes.Red;
                    }
                }
            }
            catch (Exception ex)
            {
                txtVirtuagymTestResult.Text = string.Format(L.T("Settings_Msg_TestError"), ex.Message);
                txtVirtuagymTestResult.Foreground = Brushes.Red;
            }
            finally
            {
                btnTestVirtuagymConnection.IsEnabled = true;
            }
        }

        private void chkMemberCacheEnabled_Changed(object sender, RoutedEventArgs e)
        {
            UpdateCacheControlStates();
        }

        private void chkCacheSyncEnabled_Changed(object sender, RoutedEventArgs e)
        {
            UpdateCacheControlStates();
        }

        private void cmbSyncMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateCacheControlStates();
        }

        private void UpdateCacheControlStates()
        {
            bool cacheEnabled = chkMemberCacheEnabled.IsChecked == true;
            bool syncEnabled = chkCacheSyncEnabled.IsChecked == true;

            var selectedMode = (cmbSyncMode.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "Interval";
            bool isInterval = string.Equals(selectedMode, "Interval", StringComparison.OrdinalIgnoreCase);
            bool isDailyTime = string.Equals(selectedMode, "DailyTime", StringComparison.OrdinalIgnoreCase);

            chkCacheSyncEnabled.IsEnabled = cacheEnabled;
            cmbSyncMode.IsEnabled = cacheEnabled && syncEnabled;

            // Intervall-Felder
            lblSyncInterval.Visibility = isInterval ? Visibility.Visible : Visibility.Collapsed;
            txtCacheSyncIntervalMinutes.Visibility = isInterval ? Visibility.Visible : Visibility.Collapsed;
            txtCacheSyncIntervalMinutes.IsEnabled = cacheEnabled && syncEnabled && isInterval;

            // Tägliche Uhrzeit-Felder
            lblSyncDailyTime.Visibility = isDailyTime ? Visibility.Visible : Visibility.Collapsed;
            txtCacheSyncDailyTime.Visibility = isDailyTime ? Visibility.Visible : Visibility.Collapsed;
            txtCacheSyncDailyTime.IsEnabled = cacheEnabled && syncEnabled && isDailyTime;

            btnCacheSyncNow.IsEnabled = cacheEnabled;
            btnCacheClear.IsEnabled = cacheEnabled;
        }

        private async void btnCacheSyncNow_Click(object sender, RoutedEventArgs e)
        {
            btnCacheSyncNow.IsEnabled = false;
            btnCacheClear.IsEnabled = false;
            txtCacheSyncStatus.Text = "Synchronisation läuft...";
            pgbCacheSync.Value = 0;
            pgbCacheSync.IsIndeterminate = true;
            pgbCacheSync.Visibility = System.Windows.Visibility.Visible;
            txtCacheSyncProgress.Text = "";
            txtCacheSyncProgress.Visibility = System.Windows.Visibility.Visible;

            try
            {
                using (var api = VirtuagymApiServiceFactory.Create(
                    txtVirtuagymApiKey.Text,
                    txtVirtuagymServerUrl.Text,
                    txtVirtuagymClubSecret.Text))
                {
                    var cache = new MemberCacheService();

                    cache.SyncStarted += (msg) =>
                    {
                        Dispatcher.Invoke(() => txtCacheSyncStatus.Text = msg);
                    };

                    cache.SyncProgress += (current, total) =>
                    {
                        Dispatcher.Invoke(() =>
                        {
                            pgbCacheSync.IsIndeterminate = false;
                            pgbCacheSync.Maximum = total;
                            pgbCacheSync.Value = current;
                            txtCacheSyncProgress.Text = $"{current} / {total} Mitglieder verarbeitet ({(total > 0 ? current * 100 / total : 0)}%)";
                        });
                    };

                    cache.SyncCompleted += (msg) =>
                    {
                        Dispatcher.Invoke(() =>
                        {
                            txtCacheSyncStatus.Text = msg;
                            pgbCacheSync.Value = pgbCacheSync.Maximum;
                        });
                    };

                    cache.SyncError += (msg, ex) =>
                    {
                        Dispatcher.Invoke(() =>
                        {
                            txtCacheSyncStatus.Text = msg;
                            txtCacheSyncStatus.Foreground = Brushes.Red;
                        });
                    };

                    int count = await cache.SyncFromApiAsync(api);
                    cache.Dispose();
                }
            }
            catch (Exception ex)
            {
                txtCacheSyncStatus.Text = $"Fehler: {ex.Message}";
                txtCacheSyncStatus.Foreground = Brushes.Red;
            }
            finally
            {
                btnCacheSyncNow.IsEnabled = true;
                btnCacheClear.IsEnabled = true;
                pgbCacheSync.IsIndeterminate = false;
                pgbCacheSync.Visibility = System.Windows.Visibility.Collapsed;
                txtCacheSyncProgress.Visibility = System.Windows.Visibility.Collapsed;
                txtCacheSyncStatus.Foreground = Brushes.Gray;
            }
        }

        private void btnCacheClear_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show(L.T("Settings_Cache_ClearConfirm"),
                L.T("Settings_Btn_ClearCache"), MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    var cache = new MemberCacheService();
                    cache.Clear();
                    cache.Dispose();
                    txtCacheSyncStatus.Text = L.T("Settings_Cache_Cleared");
                }
                catch (Exception ex)
                {
                    txtCacheSyncStatus.Text = string.Format(L.T("Settings_Msg_TestError"), ex.Message);
                }
            }
        }
    }
}
