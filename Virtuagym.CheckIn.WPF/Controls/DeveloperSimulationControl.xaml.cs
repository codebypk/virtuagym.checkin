using System;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Virtuagym.API.Services;
using Virtuagym.CheckIn.Core.Services;
using Virtuagym.CheckIn.WPF.Services;
using Hardware.Models;
using Virtuagym.CheckIn.Core.Helper;
using Virtuagym.CheckIn.Core.Models;
using Virtuagym.API.Cache.Models;
using AccessPass.Services;

namespace Virtuagym.CheckIn.WPF.Controls
{
    public partial class DeveloperSimulationControl : UserControl
    {
        private ObservableCollection<CheckinClientMapping> _checkinClientMappings;

        public DeveloperSimulationControl()
        {
            InitializeComponent();
        }

        public void ApplyLocalization()
        {
            grpSimulation.Header = L.T("DeveloperSimulation_GroupHeader");
            lblSimMapping.Text = L.T("DeveloperSimulation_LblMapping");
            cmbSimulationMapping.ToolTip = L.T("DeveloperSimulation_TipMapping");
            lblSimCardId.Text = L.T("DeveloperSimulation_LblCardId");
            txtSimulationCardId.ToolTip = L.T("DeveloperSimulation_TipCardId");
            chkSimByMemberId.Content = L.T("DeveloperSimulation_ByMemberId");
            chkSimByMemberId.ToolTip = L.T("DeveloperSimulation_ByMemberIdTooltip");
            chkSimulationClearCache.Content = L.T("DeveloperSimulation_ClearCache");
            chkSimulationClearCache.ToolTip = L.T("DeveloperSimulation_ClearCacheTooltip");
            chkForceOffline.Content = L.T("DeveloperSimulation_ForceOffline");
            chkForceOffline.ToolTip = L.T("DeveloperSimulation_ForceOfflineTooltip");
            btnSimulationRun.Content = "\u25B6 " + L.T("DeveloperSimulation_BtnRun");
            btnSimulationRun.ToolTip = L.T("DeveloperSimulation_TipRun");
            btnSimulationClearLog.Content = L.T("DeveloperSimulation_BtnClearLog");
            lblSimLog.Text = L.T("DeveloperSimulation_LblLog");
            colSimLog.Header = L.T("DeveloperSimulation_ColLog");
        }

        public void SetMappings(ObservableCollection<CheckinClientMapping> mappings)
        {
            _checkinClientMappings = mappings;
            PopulateSimulationComboBox();
        }

        private void PopulateSimulationComboBox()
        {
            if (_checkinClientMappings == null) return;
            cmbSimulationMapping.Items.Clear();
            foreach (var mapping in _checkinClientMappings)
                cmbSimulationMapping.Items.Add(mapping);
            cmbSimulationMapping.DisplayMemberPath = "Name";
            if (cmbSimulationMapping.Items.Count > 0)
                cmbSimulationMapping.SelectedIndex = 0;
        }

        private void txtSimulationCardId_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter || e.Key == Key.Return)
                btnSimulationRun_Click(sender, e);
        }

        private async void btnSimulationRun_Click(object sender, RoutedEventArgs e)
        {
            string cardId = txtSimulationCardId.Text?.Trim();
            if (string.IsNullOrWhiteSpace(cardId))
            {
                txtSimulationStatus.Text = L.T("DeveloperSimulation_CardIdRequired");
                txtSimulationStatus.Foreground = Brushes.Orange;
                return;
            }

            var mapping = cmbSimulationMapping.SelectedItem as CheckinClientMapping;
            if (mapping == null)
            {
                txtSimulationStatus.Text = L.T("DeveloperSimulation_MappingRequired");
                txtSimulationStatus.Foreground = Brushes.Orange;
                return;
            }

            bool byMemberId = chkSimByMemberId.IsChecked == true;

            btnSimulationRun.IsEnabled = false;
            txtSimulationStatus.Text = L.T("DeveloperSimulation_Running");
            txtSimulationStatus.Foreground = Brushes.Gray;

            var simLogger = new SettingsLogWriter(listViewSimulationLog);

            try
            {
                simLogger.WriteToLog($"{L.T("DeveloperSimulation_Started")}: '{cardId}' (Mapping: {mapping.Name ?? "unnamed"}{(byMemberId ? ", MemberId" : "")})", Constants.LogInfo);

                MemberCacheService cache = null;
                try
                {
                    cache = new MemberCacheService();
                    int cacheCount = cache.GetCount();
                    simLogger.WriteToLog($"[SIM] {string.Format(L.T("DeveloperSimulation_CacheLoaded"), cacheCount)}", Constants.LogInfo);

                    if (byMemberId)
                    {
                        if ( long.TryParse(cardId, out long memberId) && memberId > 0)
                        {
                            var cachedEntry = cache.GetByMemberId(memberId);
                            if (cachedEntry != null && chkSimulationClearCache.IsChecked == true)
                            {
                                cache.DeleteByMemberId(cachedEntry.MemberId);
                                simLogger.WriteToLog($"[SIM] {string.Format(L.T("DeveloperSimulation_CacheEntryRemoved"), cachedEntry.DisplayName, cachedEntry.MemberId)}", Constants.LogWarning);
                                cachedEntry = null;
                            }

                            if (cachedEntry != null)
                                simLogger.WriteToLog($"[SIM] {string.Format(L.T("DeveloperSimulation_CacheHit"), cachedEntry.DisplayName, cachedEntry.MemberId, cachedEntry.Active)}", Constants.LogSuccess);
                            else
                                simLogger.WriteToLog($"[SIM] {string.Format(L.T("DeveloperSimulation_CacheMissMemberId"), memberId)}", Constants.LogWarning);
                        }
                        else
                        {
                            simLogger.WriteToLog($"[SIM] {L.T("DeveloperSimulation_InvalidMemberId")}", Constants.LogWarning);
                        }
                    }
                    else
                    {
                        var cachedEntry = cache.GetByRfidTag(cardId);
                        if (cachedEntry != null && chkSimulationClearCache.IsChecked == true)
                        {
                            cache.DeleteByMemberId(cachedEntry.MemberId);
                            simLogger.WriteToLog($"[SIM] {string.Format(L.T("DeveloperSimulation_CacheEntryRemoved"), cachedEntry.DisplayName, cachedEntry.MemberId)}", Constants.LogWarning);
                            cachedEntry = null;
                        }

                        if (cachedEntry != null)
                            simLogger.WriteToLog($"[SIM] {string.Format(L.T("DeveloperSimulation_CacheHit"), cachedEntry.DisplayName, cachedEntry.MemberId, cachedEntry.Active)}", Constants.LogSuccess);
                        else
                            simLogger.WriteToLog($"[SIM] {string.Format(L.T("DeveloperSimulation_CacheMissRfid"), cardId)}", Constants.LogWarning);
                    }
                }
                catch (Exception cacheEx)
                {
                    simLogger.WriteToLog($"[SIM] {L.T("DeveloperSimulation_CacheLoadError")}: {cacheEx.Message}", Constants.LogWarning);
                }

                var appSettings = new WpfAppSettings();
                var soundPlayer = new WpfSoundPlayer(simLogger);
                var apiFactory = new WpfVirtuagymApiServiceFactory();
                var accessPassService = new AccessPassService(new AccessPassStore());
                var handler = new CheckinHandler(simLogger, mapping, null, appSettings, soundPlayer, apiFactory, cache, accessPassService);
                handler.ForceOffline = chkForceOffline.IsChecked == true;

                if (byMemberId)
                {
                    if (long.TryParse(cardId, out long memberId) && memberId > 0)
                    {
                        CachedMemberInfo precached = null;
                        var entry = cache?.GetByMemberId(memberId);
                        if (entry != null)
                        {
                            precached = new CachedMemberInfo
                            {
                                MemberId = entry.MemberId,
                                UserId = entry.UserId,
                                RfidTag = entry.RfidTag,
                                Firstname = entry.Firstname,
                                Lastname = entry.Lastname,
                                Avatar = entry.AvatarUrl,
                                Active = entry.Active,
                                TimestampEdit = entry.TimestampEdit,
                                DeviceCheckins = entry.DeviceCheckins,
                                ServiceCredits = entry.ServiceCredits,
                                CreditsLastSyncTimestamp = entry.CreditsLastSyncTimestamp
                            };
                        }
                        else
                        {
                            precached = new CachedMemberInfo { MemberId = memberId, Active = true };
                        }

                        string rfidTag = precached.RfidTag ?? "";
                        await handler.PerformCheckinAsync(new Card(rfidTag), $"SIM-MID {mapping.Name ?? mapping.DeviceID}", precached);
                    }
                    else
                    {
                        simLogger.WriteToLog($"[SIM] {L.T("DeveloperSimulation_InvalidMemberId")}", Constants.LogError);
                    }
                }
                else if (mapping.InputType == nameof(HardwareInputType.QRCode))
                {
                    await handler.PerformCheckinAsync(cardId, $"SIM-QR {mapping.Name ?? "QR"}");
                }
                else
                {
                    await handler.PerformCheckinAsync(new Card(cardId), $"SIM {mapping.Name ?? mapping.DeviceID}");
                }

                cache?.Dispose();

                txtSimulationStatus.Text = $"{L.T("DeveloperSimulation_Completed")} '{cardId}'.";
                txtSimulationStatus.Foreground = Brushes.Green;
            }
            catch (Exception ex)
            {
                simLogger.WriteToLog($"{L.T("Log_Error")}: {ex.Message}", Constants.LogError);
                txtSimulationStatus.Text = $"{L.T("Msg_Error")}: {ex.Message}";
                txtSimulationStatus.Foreground = Brushes.Red;
            }
            finally
            {
                btnSimulationRun.IsEnabled = true;
            }
        }

        private void btnSimulationClearLog_Click(object sender, RoutedEventArgs e)
        {
            listViewSimulationLog.Items.Clear();
            txtSimulationStatus.Text = "";
        }
    }
}
