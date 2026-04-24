using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Virtuagym.CheckIn.Core.Services;
using Virtuagym.CheckIn.WPF.Services;
using Virtuagym.CheckIn.Core.Helper;
using Virtuagym.CheckIn.Core.Services;
using Virtuagym.CheckIn.WPF.Services;
using Virtuagym.API.Services;
using Virtuagym.API.v1.Models;
using Virtuagym.CheckIn.WPF.Properties;
using Virtuagym.API.Cache.Models;

namespace Virtuagym.CheckIn.WPF.Controls
{
    public partial class MemberToolsControl : UserControl
    {
        private List<MemberResult> _allMembers;
        private List<MemberCacheEntry> _allCacheEntries;

        public MemberToolsControl()
        {
            InitializeComponent();
        }


        public void ApplyLocalization()
        {
            // Member-Cache tab
            devleoperSubTabMembersCache.Header = L.T("Dev_Tab_MemberCache");
            btnCacheLoad.Content = L.T("Settings_Btn_LoadCache");
            btnCacheExportCsv.Content = L.T("Settings_Btn_ExportCsv");
            btnCacheEditEntry.Content = L.T("Btn_Edit");
            btnCacheDeleteEntry.Content = L.T("Btn_Delete");

            // Members (Online) tab
            devleoperSubTabMembers.Header = L.T("Dev_Tab_MembersOnline");
            btnGetMembers.Content = L.T("Settings_Btn_GetMembers");
            btnExportMembersCsv.Content = L.T("Settings_Btn_ExportCsv");


            if (dataGridCache.Columns.Count >= 8)
            {
                dataGridCache.Columns[0].Header = L.T("Settings_Col_MemberId");
                dataGridCache.Columns[1].Header = L.T("Settings_Col_FirstName");
                dataGridCache.Columns[2].Header = L.T("Settings_Col_LastName");
                dataGridCache.Columns[3].Header = L.T("Settings_Col_Email");
                dataGridCache.Columns[4].Header = L.T("Settings_Col_RfidLocal");
                dataGridCache.Columns[5].Header = L.T("Settings_Col_RfidVirtuagym");
                dataGridCache.Columns[6].Header = L.T("Settings_Col_IsActive");
                dataGridCache.Columns[7].Header = L.T("Settings_Col_IsCheckedIn");
            }

            if (dataGridMembers.Columns.Count >= 7)
            {
                dataGridMembers.Columns[0].Header = L.T("Settings_Col_ID");
                dataGridMembers.Columns[1].Header = L.T("Settings_Col_FirstName");
                dataGridMembers.Columns[2].Header = L.T("Settings_Col_LastName");
                dataGridMembers.Columns[3].Header = L.T("Settings_Col_Email");
                dataGridMembers.Columns[4].Header = L.T("Settings_Col_CheckinKey");
                dataGridMembers.Columns[5].Header = L.T("Settings_Col_IsActive");
                dataGridMembers.Columns[6].Header = L.T("Settings_Col_MemberSince");
            }
        }

        private VirtuagymApiService CreateTestApiService()
        {
            return VirtuagymApiServiceFactory.Create(Settings.Default.VirtuagymApiKey, Settings.Default.VirtuagymServerUrl, Settings.Default.VirtuagymClubSecret);
        }

        private static string Escape(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            if (value.Contains(";") || value.Contains("\"") || value.Contains("\n"))
                return "\"" + value.Replace("\"", "\"\"") + "\"";
            return value;
        }

        #region Member-Cache

        private async void btnCacheLoad_Click(object sender, RoutedEventArgs e)
        {
            btnCacheLoad.IsEnabled = false;
            txtCacheLoadStatus.Text = "Cache wird geladen...";
            txtCacheLoadStatus.Foreground = Brushes.Gray;

            try
            {
                var loadedEntries = await Task.Run(() =>
                {
                    using (var cache = new MemberCacheService())
                    {
                        return cache.GetAll();
                    }
                });

                _allCacheEntries = loadedEntries;
                dataGridCache.ItemsSource = _allCacheEntries;
                txtCacheSearch.Text = "";
                txtCacheLoadStatus.Text = $"{_allCacheEntries.Count} Einträge geladen.";
                txtCacheLoadStatus.Foreground = Brushes.Green;
                txtCacheSearchStatus.Text = $"{_allCacheEntries.Count} Einträge";
                txtCacheSearchStatus.Foreground = Brushes.Gray;
                btnCacheExportCsv.IsEnabled = _allCacheEntries.Count > 0;
            }
            catch (Exception ex)
            {
                txtCacheLoadStatus.Text = $"Fehler: {ex.Message}";
                txtCacheLoadStatus.Foreground = Brushes.Red;
            }
            finally
            {
                btnCacheLoad.IsEnabled = true;
            }
        }

        private void txtCacheSearch_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter || e.Key == Key.Return)
                ApplyCacheFilter();
        }

        private void btnCacheSearchClear_Click(object sender, RoutedEventArgs e)
        {
            txtCacheSearch.Text = "";
            if (_allCacheEntries != null)
            {
                dataGridCache.ItemsSource = _allCacheEntries;
                txtCacheSearchStatus.Text = $"{_allCacheEntries.Count} Einträge";
                txtCacheSearchStatus.Foreground = Brushes.Gray;
            }
        }

        private void ApplyCacheFilter()
        {
            if (_allCacheEntries == null || _allCacheEntries.Count == 0)
            {
                txtCacheSearchStatus.Text = "Bitte zuerst Cache laden.";
                txtCacheSearchStatus.Foreground = Brushes.Orange;
                return;
            }

            string searchText = txtCacheSearch.Text.Trim();
            if (string.IsNullOrWhiteSpace(searchText))
            {
                dataGridCache.ItemsSource = _allCacheEntries;
                txtCacheSearchStatus.Text = $"{_allCacheEntries.Count} Einträge";
                txtCacheSearchStatus.Foreground = Brushes.Gray;
                return;
            }

            string search = searchText.ToLowerInvariant();
            var filtered = _allCacheEntries.Where(m =>
                (m.Firstname != null && m.Firstname.ToLowerInvariant().Contains(search)) ||
                (m.Lastname != null && m.Lastname.ToLowerInvariant().Contains(search)) ||
                (m.Email != null && m.Email.ToLowerInvariant().Contains(search)) ||
                (m.RfidTag != null && m.RfidTag.ToLowerInvariant().Contains(search)) ||
                (m.Phone != null && m.Phone.Contains(search)) ||
                m.MemberId.ToString() == search
            ).ToList();

            dataGridCache.ItemsSource = filtered;
            txtCacheSearchStatus.Text = $"{filtered.Count} von {_allCacheEntries.Count} Einträgen";
            txtCacheSearchStatus.Foreground = filtered.Count > 0 ? Brushes.Green : Brushes.Orange;
        }

        private void btnCacheEditEntry_Click(object sender, RoutedEventArgs e)
        {
            OpenEditCacheEntryDialog();
        }

        private void dataGridCache_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            OpenEditCacheEntryDialog();
        }

        private void dataGridCache_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
        }

        private void OpenEditCacheEntryDialog()
        {
            var selected = dataGridCache.SelectedItem as MemberCacheEntry;
            if (selected == null)
            {
                MessageBox.Show(L.T("Dev_SelectEntry"), L.T("Msg_Note"), MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dialog = new EditCacheEntryWindow(selected);
            dialog.Owner = Window.GetWindow(this);

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    var cache = new MemberCacheService();
                    cache.Upsert(dialog.Entry);
                    cache.Dispose();

                    btnCacheLoad_Click(null, null);

                    txtCacheSearchStatus.Text = string.Format(L.T("Dev_EntrySaved"), dialog.Entry.DisplayName);
                    txtCacheSearchStatus.Foreground = Brushes.Green;
                }
                catch (Exception ex)
                {
                    txtCacheSearchStatus.Text = string.Format(L.T("Dev_SaveError"), ex.Message);
                    txtCacheSearchStatus.Foreground = Brushes.Red;
                }
            }
        }

        private void btnCacheDeleteEntry_Click(object sender, RoutedEventArgs e)
        {
            var selected = dataGridCache.SelectedItem as MemberCacheEntry;
            if (selected == null)
            {
                MessageBox.Show(L.T("Dev_SelectEntry"), L.T("Msg_Note"), MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var result = MessageBox.Show(
                string.Format(L.T("Dev_CacheDeleteConfirm"), selected.DisplayName, selected.MemberId),
                L.T("Dev_CacheDeleteTitle"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    var cache = new MemberCacheService();
                    cache.DeleteByMemberId(selected.MemberId);
                    cache.Dispose();

                    btnCacheLoad_Click(null, null);

                    txtCacheSearchStatus.Text = string.Format(L.T("Dev_EntryDeleted"), selected.DisplayName);
                    txtCacheSearchStatus.Foreground = Brushes.Green;
                }
                catch (Exception ex)
                {
                    txtCacheSearchStatus.Text = string.Format(L.T("Dev_DeleteError"), ex.Message);
                    txtCacheSearchStatus.Foreground = Brushes.Red;
                }
            }
        }

        private void btnCacheExportCsv_Click(object sender, RoutedEventArgs e)
        {
            var entries = dataGridCache.ItemsSource as IEnumerable<MemberCacheEntry>;
            if (entries == null || !entries.Any())
            {
                txtCacheLoadStatus.Text = L.T("Dev_NoDataToExport");
                txtCacheLoadStatus.Foreground = Brushes.Orange;
                return;
            }

            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "CSV (*.csv)|*.csv",
                DefaultExt = ".csv",
                FileName = "member_cache_export.csv"
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    var sb = new StringBuilder();
                    sb.AppendLine("MemberId;" + L.T("Settings_Col_FirstName") + ";" + L.T("Settings_Col_LastName") + ";" + L.T("Settings_Col_Email") + ";" + L.T("Lbl_Phone") + ";" + L.T("Settings_Col_RfidLocal") + ";" + L.T("Settings_Col_IsActive") + ";" + L.T("Settings_Col_IsCheckedIn"));

                    foreach (var entry in entries)
                    {
                        sb.AppendLine(string.Join(";",
                            entry.MemberId,
                            Escape(entry.Firstname),
                            Escape(entry.Lastname),
                            Escape(entry.Email),
                            Escape(entry.Phone),
                            Escape(entry.RfidTag),
                            entry.Active ? L.T("EditMember_Active") : L.T("EditMember_Inactive"),
                            entry.IsCheckedIn ? L.T("EditMember_Active") : L.T("EditMember_Inactive")
                        ));
                    }

                    File.WriteAllText(dialog.FileName, sb.ToString(), Encoding.UTF8);

                    txtCacheLoadStatus.Text = string.Format(L.T("Dev_ExportSuccess"), entries.Count(), Path.GetFileName(dialog.FileName));
                    txtCacheLoadStatus.Foreground = Brushes.Green;
                }
                catch (Exception ex)
                {
                    txtCacheLoadStatus.Text = string.Format(L.T("Dev_ExportError"), ex.Message);
                    txtCacheLoadStatus.Foreground = Brushes.Red;
                }
            }
        }

        #endregion

        #region Mitglieder

        private async void btnGetMembers_Click(object sender, RoutedEventArgs e)
        {
            btnGetMembers.IsEnabled = false;
            txtMembersLoadStatus.Text = L.T("Settings_Msg_LoadingMembers");
            txtMembersLoadStatus.Foreground = Brushes.Gray;
            dataGridMembers.ItemsSource = null;

            try
            {
                using (var api = CreateTestApiService())
                {
                    var members = await api.GetMembersAsync();

                    if (members != null && members.Count > 0)
                    {
                        _allMembers = members;
                        dataGridMembers.ItemsSource = members;
                        txtMemberSearch.Text = "";
                        txtMembersLoadStatus.Text = string.Format(L.T("Settings_Msg_MembersFound"), members.Count);
                        txtMembersLoadStatus.Foreground = Brushes.Green;
                        txtMemberSearchStatus.Text = string.Format(L.T("Settings_Msg_MembersFound"), members.Count).TrimEnd('.');
                        txtMemberSearchStatus.Foreground = Brushes.Gray;
                        btnExportMembersCsv.IsEnabled = true;
                    }
                    else
                    {
                        _allMembers = null;
                        txtMembersLoadStatus.Text = L.T("Settings_Msg_NoMembersFound");
                        txtMembersLoadStatus.Foreground = Brushes.Orange;
                        txtMemberSearchStatus.Text = "";
                        btnExportMembersCsv.IsEnabled = false;
                    }
                }
            }
            catch (Exception ex)
            {
                txtMembersLoadStatus.Text = string.Format(L.T("Settings_Msg_TestError"), ex.Message);
                txtMembersLoadStatus.Foreground = Brushes.Red;
            }
            finally
            {
                btnGetMembers.IsEnabled = true;
            }
        }

        private void txtMemberSearch_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter || e.Key == Key.Return)
                ApplyMemberFilter();
        }

        private void btnMemberSearchClear_Click(object sender, RoutedEventArgs e)
        {
            txtMemberSearch.Text = "";
            if (_allMembers != null)
            {
                dataGridMembers.ItemsSource = _allMembers;
                txtMemberSearchStatus.Text = string.Format(L.T("Settings_Msg_MembersFound"), _allMembers.Count);
                txtMemberSearchStatus.Foreground = Brushes.Gray;
            }
        }

        private void ApplyMemberFilter()
        {
            if (_allMembers == null || _allMembers.Count == 0)
            {
                txtMemberSearchStatus.Text = L.T("Dev_LoadMembersFirst");
                txtMemberSearchStatus.Foreground = Brushes.Orange;
                return;
            }

            string searchText = txtMemberSearch.Text.Trim();
            if (string.IsNullOrWhiteSpace(searchText))
            {
                dataGridMembers.ItemsSource = _allMembers;
                txtMemberSearchStatus.Text = string.Format(L.T("Settings_Msg_MembersFound"), _allMembers.Count);
                txtMemberSearchStatus.Foreground = Brushes.Gray;
                return;
            }

            string search = searchText.ToLowerInvariant();
            var filtered = _allMembers.Where(m =>
                (m.firstname != null && m.firstname.ToLowerInvariant().Contains(search)) ||
                (m.lastname != null && m.lastname.ToLowerInvariant().Contains(search)) ||
                (m.email != null && m.email.ToLowerInvariant().Contains(search)) ||
                (m.rfid_tag != null && m.rfid_tag.ToLowerInvariant().Contains(search)) ||
                (m.phone != null && m.phone.Contains(search)) ||
                (m.mobile != null && m.mobile.Contains(search)) ||
                m.member_id.ToString() == search ||
                (m.club_member_id != null && m.club_member_id.ToLowerInvariant().Contains(search)) ||
                (m.external_id != null && m.external_id.ToLowerInvariant().Contains(search))
            ).ToList();

            dataGridMembers.ItemsSource = filtered;
            txtMemberSearchStatus.Text = string.Format(L.T("Dev_FilterResult"), filtered.Count, _allMembers.Count);
            txtMemberSearchStatus.Foreground = filtered.Count > 0 ? Brushes.Green : Brushes.Orange;
        }

        private void btnMemberEdit_Click(object sender, RoutedEventArgs e)
        {
            OpenEditMemberDialog();
        }

        private void dataGridMembers_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            OpenEditMemberDialog();
        }

        private void dataGridMembers_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
        }

        private void OpenEditMemberDialog()
        {
            var selected = dataGridMembers.SelectedItem as MemberResult;
            if (selected == null)
            {
                MessageBox.Show(L.T("Dev_SelectMember"), L.T("Msg_Note"), MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dialog = new EditMemberWindow(selected, () => CreateTestApiService());
            dialog.Owner = Window.GetWindow(this);

            if (dialog.ShowDialog() == true && dialog.UpdatedMember != null)
            {
                if (_allMembers != null)
                {
                    int index = _allMembers.FindIndex(m => m.member_id == selected.member_id);
                    if (index >= 0)
                        _allMembers[index] = dialog.UpdatedMember;
                }

                string currentSearch = txtMemberSearch.Text.Trim();
                if (!string.IsNullOrWhiteSpace(currentSearch))
                    ApplyMemberFilter();
                else if (_allMembers != null)
                {
                    dataGridMembers.ItemsSource = null;
                    dataGridMembers.ItemsSource = _allMembers;
                }

                txtMemberSearchStatus.Text = $"Mitglied '{dialog.UpdatedMember.firstname} {dialog.UpdatedMember.lastname}' aktualisiert.";
                txtMemberSearchStatus.Foreground = Brushes.Green;
            }
        }

        private void btnExportMembersCsv_Click(object sender, RoutedEventArgs e)
        {
            var members = _allMembers;
            if (members == null || members.Count == 0)
            {
                txtMembersLoadStatus.Text = "Keine Mitglieder zum Exportieren vorhanden.";
                txtMembersLoadStatus.Foreground = Brushes.Orange;
                return;
            }

            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "CSV-Datei (*.csv)|*.csv",
                DefaultExt = ".csv",
                FileName = $"Virtuagym_Members_" + DateTime.Now.ToString(Constants.CsvExportDateFormat) + ".csv"
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    var sb = new StringBuilder();
                    sb.AppendLine("member_id;club_member_id;firstname;lastname;email;gender;birthday;rfid_tag;phone;mobile;active;club_id;lang;user_avatar;last_visit;member_since;timestamp_edit;registration_date");

                    foreach (var m in members)
                    {
                        sb.AppendLine(string.Join(";",
                            Escape(m.member_id.ToString()),
                            Escape(m.club_member_id),
                            Escape(m.firstname),
                            Escape(m.lastname),
                            Escape(m.email),
                            Escape(m.gender),
                            Escape(m.birthday),
                            Escape(m.rfid_tag),
                            Escape(m.phone),
                            Escape(m.mobile),
                            Escape(m.active.ToString()),
                            Escape(m.club_id.ToString()),
                            Escape(m.lang),
                            Escape(m.user_avatar),
                            Escape(m.LastVisitFormatted),
                            Escape(m.MemberSinceFormatted),
                            Escape(m.TimestampEditFormatted),
                            Escape(m.RegistrationDateFormatted)
                        ));
                    }

                    File.WriteAllText(dialog.FileName, sb.ToString(), Encoding.UTF8);

                    txtMembersLoadStatus.Text = $"{members.Count} Mitglieder nach {dialog.FileName} exportiert.";
                    txtMembersLoadStatus.Foreground = Brushes.Green;
                }
                catch (Exception ex)
                {
                    txtMembersLoadStatus.Text = $"Export-Fehler: {ex.Message}";
                    txtMembersLoadStatus.Foreground = Brushes.Red;
                }
            }
        }

        #endregion
    }
}
