using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Virtuagym.CheckIn.Core.Models;
using Virtuagym.CheckIn.Core.Services;
using Virtuagym.CheckIn.Core.Models;
using Virtuagym.CheckIn.WPF.Services;
using Virtuagym.CheckIn.Core.Models;
using Virtuagym.CheckIn.Core.Models;
using Virtuagym.API.Services;
using Jablotron.API.Odbo.Models;
using Jablotron.API.Odbo;

namespace Virtuagym.CheckIn.WPF.Controls
{
    public partial class DeveloperJablotronControl : UserControl
    {
        private JablotronDbService _jablotronDb;
        private List<RfidCompareResult> _jablotronCompareResults;
        private System.Data.DataTable _jablotronCurrentDataTable;

        public DeveloperJablotronControl()
        {
            InitializeComponent();
        }

        public void ApplyLocalization()
        {
            lblFdbFile.Text = L.T("Settings_Lbl_FdbFile");
            tabJablonetSektionen.Header = L.T("Settings_Tab_Sektionen");
            tabJablonetRfidAbgleich.Header = L.T("Settings_Tab_RfidAbgleich");
            btnJablotronConnect.Content = L.T("Settings_Btn_JablonetLoad");
            lblJablonetSectionLabel.Text = L.T("Settings_Lbl_JablonetSectionLabel");
            lblJablonetSearchLabel.Text = L.T("Settings_Lbl_JablonetSearchLabel");
            btnJablotronExecuteSql.Content = L.T("Settings_Btn_JablonetSearch");
            lblJablonetFilterLabel.Text = L.T("Settings_Lbl_JablonetFilterLabel");
            lblJablonetStatusLabel.Text = L.T("Settings_Lbl_JablonetStatusLabel");
        }

        public void LoadSettings()
        {
            txtJablotronDbPath.Text = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "fdb");
        }

        public void SaveSettings(Action<string, string> updateSetting)
        {
        }

        private static string Escape(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            if (value.Contains(";") || value.Contains("\"") || value.Contains("\n"))
                return "\"" + value.Replace("\"", "\"\"") + "\"";
            return value;
        }

        private static string NormalizeName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "";
            return name.Trim().ToLowerInvariant()
                .Replace("ä", "ae").Replace("ö", "oe").Replace("ü", "ue").Replace("ß", "ss")
                .Replace("-", " ").Replace("  ", " ");
        }

        private void EnsureJablotronLoaded()
        {
            string dbPath = txtJablotronDbPath.Text;
            if (string.IsNullOrWhiteSpace(dbPath))
            {
                dbPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "resources", "fdb");
                txtJablotronDbPath.Text = dbPath;
            }

            if (!File.Exists(dbPath))
                throw new FileNotFoundException("FDB-Datei nicht gefunden. Bitte zuerst ein F-Link Backup erstellen und hier laden.", dbPath);

            if (_jablotronDb == null)
                _jablotronDb = new JablotronDbService(dbPath);
        }

        private void btnJablotronBrowseDb_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "ODBO-Link Datei (*.fdb)|*.fdb|Alle Dateien (*.*)|*.*",
                Title = "Jablotron FDB-Datei auswählen"
            };
            if (!string.IsNullOrWhiteSpace(txtJablotronDbPath.Text) && File.Exists(txtJablotronDbPath.Text))
                dialog.InitialDirectory = Path.GetDirectoryName(txtJablotronDbPath.Text);

            if (dialog.ShowDialog() == true)
            {
                txtJablotronDbPath.Text = dialog.FileName;
                _jablotronDb?.Dispose();
                _jablotronDb = null;
            }
        }

        #region Sektionen-Tab

        private void btnJablotronConnect_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _jablotronDb?.Dispose();
                _jablotronDb = null;
                EnsureJablotronLoaded();

                txtJablotronTableInfo.Text = _jablotronDb.FileInfo.ToString();

                var sections = _jablotronDb.GetSectionNames();
                cmbJablotronTables.ItemsSource = sections;
                cmbJablotronTables.SelectedIndex = sections.Count > 0 ? 0 : -1;

                txtJablotronStatus.Text = $"Geladen: {sections.Count} Sektionen.";
                txtJablotronStatus.Foreground = Brushes.Green;
            }
            catch (Exception ex)
            {
                txtJablotronStatus.Text = $"Fehler: {ex.Message}";
                txtJablotronStatus.Foreground = Brushes.Red;
                cmbJablotronTables.ItemsSource = null;
                _jablotronCurrentDataTable = null;
                dataGridJablotron.ItemsSource = null;
                txtJablotronColumns.Text = "";
                txtJablotronTableInfo.Text = "";
            }
        }

        private void cmbJablotronTables_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_jablotronDb == null || cmbJablotronTables.SelectedItem == null)
                return;

            string sectionName = cmbJablotronTables.SelectedItem.ToString();

            try
            {
                var columns = _jablotronDb.GetColumns(sectionName);
                txtJablotronColumns.Text = string.Join(", ",
                    columns.Select(c => $"{c.Name} ({c.TypeName})"));

                int itemCount = _jablotronDb.GetItemCount(sectionName);
                txtJablotronTableInfo.Text = $"{itemCount} Einträge, {columns.Count} Properties";

                var dt = _jablotronDb.ReadSection(sectionName);
                _jablotronCurrentDataTable = dt;
                dataGridJablotron.ItemsSource = dt.DefaultView;
                txtJablotronRowFilter.Text = "";
                txtJablotronRowFilterInfo.Text = "";

                txtJablotronStatus.Text = $"Sektion '{sectionName}': {itemCount} Einträge geladen.";
                txtJablotronStatus.Foreground = Brushes.Green;
            }
            catch (Exception ex)
            {
                txtJablotronStatus.Text = $"Fehler beim Laden von '{sectionName}': {ex.Message}";
                txtJablotronStatus.Foreground = Brushes.Red;
                _jablotronCurrentDataTable = null;
                dataGridJablotron.ItemsSource = null;
                txtJablotronColumns.Text = "";
                txtJablotronTableInfo.Text = "";
            }
        }

        private void btnJablotronExecuteSql_Click(object sender, RoutedEventArgs e)
        {
            if (_jablotronDb == null)
            {
                txtJablotronStatus.Text = "Bitte zuerst die Datei laden.";
                txtJablotronStatus.Foreground = Brushes.Orange;
                return;
            }

            string query = txtJablotronSql.Text.Trim();
            if (string.IsNullOrWhiteSpace(query))
            {
                txtJablotronStatus.Text = "Bitte einen Sektionsnamen oder Suchbegriff eingeben.";
                txtJablotronStatus.Foreground = Brushes.Orange;
                return;
            }

            try
            {
                var sections = _jablotronDb.GetSectionNames();
                var match = sections.FirstOrDefault(s =>
                    s.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0);

                if (match != null)
                {
                    var dt = _jablotronDb.ReadSection(match);
                    _jablotronCurrentDataTable = dt;
                    dataGridJablotron.ItemsSource = dt.DefaultView;
                    txtJablotronRowFilter.Text = "";
                    txtJablotronRowFilterInfo.Text = "";
                    txtJablotronStatus.Text = $"Sektion '{match}': {dt.Rows.Count} Einträge.";
                    txtJablotronStatus.Foreground = Brushes.Green;
                }
                else
                {
                    txtJablotronStatus.Text = $"Keine Sektion gefunden die '{query}' enthält.";
                    txtJablotronStatus.Foreground = Brushes.Orange;
                }
            }
            catch (Exception ex)
            {
                txtJablotronStatus.Text = $"Fehler: {ex.Message}";
                txtJablotronStatus.Foreground = Brushes.Red;
            }
        }

        private void txtJablotronRowFilter_TextChanged(object sender, TextChangedEventArgs e)
        {
            ApplyJablotronRowFilter();
        }

        private void btnJablotronRowFilterClear_Click(object sender, RoutedEventArgs e)
        {
            txtJablotronRowFilter.Text = "";
        }

        private void ApplyJablotronRowFilter()
        {
            if (_jablotronCurrentDataTable == null)
            {
                txtJablotronRowFilterInfo.Text = "";
                return;
            }

            string filter = txtJablotronRowFilter.Text?.Trim();

            if (string.IsNullOrEmpty(filter))
            {
                _jablotronCurrentDataTable.DefaultView.RowFilter = "";
                txtJablotronRowFilterInfo.Text = "";
                return;
            }

            string escaped = filter.Replace("'", "''");
            var conditions = new List<string>();
            foreach (System.Data.DataColumn col in _jablotronCurrentDataTable.Columns)
            {
                conditions.Add($"CONVERT([{col.ColumnName}], 'System.String') LIKE '%{escaped}%'");
            }

            try
            {
                string rowFilter = string.Join(" OR ", conditions);
                _jablotronCurrentDataTable.DefaultView.RowFilter = rowFilter;

                int shown = _jablotronCurrentDataTable.DefaultView.Count;
                int total = _jablotronCurrentDataTable.Rows.Count;
                txtJablotronRowFilterInfo.Text = $"{shown} von {total} Einträgen";
            }
            catch
            {
                _jablotronCurrentDataTable.DefaultView.RowFilter = "";
                txtJablotronRowFilterInfo.Text = "";
            }
        }

        #endregion

        #region RFID-Abgleich-Tab

        private void btnJablotronCompareRfid_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                txtJablotronCompareStatus.Text = "Lade Daten...";
                txtJablotronCompareStatus.Foreground = Brushes.Gray;
                _jablotronCompareResults = null;

                EnsureJablotronLoaded();
                var jablotronUsers = _jablotronDb.GetUsers();

                var cache = new MemberCacheService();
                var members = cache.GetAll().Where(m => m.Active).ToList();

                var results = new List<RfidCompareResult>();

                var jabUsersWithCard = jablotronUsers.Where(ju => ju.AccessCard1 > 0).ToList();

                var jabByName = new Dictionary<string, JablotronUser>();
                var jabDuplicateNames = new HashSet<string>();
                foreach (var ju in jabUsersWithCard)
                {
                    if (string.IsNullOrWhiteSpace(ju.Name)) continue;
                    string key = NormalizeName(ju.Name);
                    if (jabByName.ContainsKey(key))
                        jabDuplicateNames.Add(key);
                    else
                        jabByName[key] = ju;
                }

                var memberNameCounts = new Dictionary<string, int>();
                foreach (var member in members)
                {
                    string fn = (member.Firstname ?? "").Trim();
                    string ln = (member.Lastname ?? "").Trim();
                    if (string.IsNullOrEmpty(fn) && string.IsNullOrEmpty(ln)) continue;
                    string key = NormalizeName(ln + " " + fn);
                    if (memberNameCounts.ContainsKey(key))
                        memberNameCounts[key]++;
                    else
                        memberNameCounts[key] = 1;
                }

                var matchedJabUsers = new HashSet<int>();

                foreach (var member in members)
                {
                    string firstname = (member.Firstname ?? "").Trim();
                    string lastname = (member.Lastname ?? "").Trim();
                    if (string.IsNullOrEmpty(firstname) && string.IsNullOrEmpty(lastname)) continue;

                    string nameKey1 = NormalizeName(lastname + " " + firstname);
                    string nameKey2 = NormalizeName(firstname + " " + lastname);

                    int vgNameCount = 0;
                    memberNameCounts.TryGetValue(nameKey1, out vgNameCount);
                    if (vgNameCount > 1)
                    {
                        results.Add(new RfidCompareResult
                        {
                            VirtuagymName = $"{firstname} {lastname}".Trim(),
                            VirtuagymMemberId = member.MemberId.ToString(),
                            VirtuagymRfidTag = member.RfidTag ?? "",
                            VirtuagymActive = member.Active,
                            JablotronName = "",
                            JablotronAccessCard = "",
                            JablotronActive = false,
                            Status = "Nicht eindeutig",
                            MatchInfo = $"Name \"{firstname} {lastname}\" existiert {vgNameCount}x in Virtuagym"
                        });
                        continue;
                    }

                    if (jabDuplicateNames.Contains(nameKey1) || jabDuplicateNames.Contains(nameKey2))
                    {
                        results.Add(new RfidCompareResult
                        {
                            VirtuagymName = $"{firstname} {lastname}".Trim(),
                            VirtuagymMemberId = member.MemberId.ToString(),
                            VirtuagymRfidTag = member.RfidTag ?? "",
                            VirtuagymActive = member.Active,
                            JablotronName = "",
                            JablotronAccessCard = "",
                            JablotronActive = false,
                            Status = "Nicht eindeutig",
                            MatchInfo = $"Name existiert mehrfach in Jablotron"
                        });
                        continue;
                    }

                    JablotronUser jabMatch = null;
                    jabByName.TryGetValue(nameKey1, out jabMatch);
                    if (jabMatch == null) jabByName.TryGetValue(nameKey2, out jabMatch);
                    if (jabMatch == null && !string.IsNullOrEmpty(lastname))
                    {
                        string nameKey3 = NormalizeName(lastname);
                        jabByName.TryGetValue(nameKey3, out jabMatch);
                    }

                    if (jabMatch == null)
                    {
                        results.Add(new RfidCompareResult
                        {
                            VirtuagymName = $"{firstname} {lastname}".Trim(),
                            VirtuagymMemberId = member.MemberId.ToString(),
                            VirtuagymRfidTag = member.RfidTag ?? "",
                            VirtuagymActive = member.Active,
                            JablotronName = "",
                            JablotronAccessCard = "",
                            JablotronActive = false,
                            Status = "Nicht zugeordnet",
                            MatchInfo = $"Kein Jablotron-Benutzer gefunden"
                        });
                        continue;
                    }

                    matchedJabUsers.Add(jabMatch.ID);

                    string jabDisplayName = jabMatch.Name;
                    if (!string.IsNullOrWhiteSpace(jabMatch.Name) && jabMatch.Name.Contains(" "))
                    {
                        var parts = jabMatch.Name.Trim().Split(new[] { ' ' }, 2);
                        if (parts.Length == 2)
                            jabDisplayName = $"{parts[1]} {parts[0]}";
                    }

                    string currentRfid = (member.RfidTag ?? "").Trim();
                    string jabCardDisplay = jabMatch.AccessCard1Display;

                    if (currentRfid == jabCardDisplay)
                    {
                        results.Add(new RfidCompareResult
                        {
                            VirtuagymName = $"{firstname} {lastname}".Trim(),
                            VirtuagymMemberId = member.MemberId.ToString(),
                            VirtuagymRfidTag = currentRfid,
                            VirtuagymActive = member.Active,
                            JablotronName = jabDisplayName,
                            JablotronAccessCard = jabCardDisplay,
                            JablotronActive = !jabMatch.IsBlocked,
                            Status = "OK",
                            MatchInfo = "RFID-Tags stimmen überein"
                        });
                    }
                    else
                    {
                        results.Add(new RfidCompareResult
                        {
                            IsSelected = true,
                            VirtuagymName = $"{firstname} {lastname}".Trim(),
                            VirtuagymMemberId = member.MemberId.ToString(),
                            VirtuagymRfidTag = currentRfid,
                            VirtuagymActive = member.Active,
                            JablotronName = jabDisplayName,
                            JablotronAccessCard = jabCardDisplay,
                            JablotronActive = !jabMatch.IsBlocked,
                            Status = "RFID abweichend",
                            MatchInfo = string.IsNullOrEmpty(currentRfid)
                                ? $"Virtuagym leer → Jablotron {jabCardDisplay}"
                                : $"Virtuagym {currentRfid} ≠ Jablotron {jabCardDisplay}"
                        });
                    }
                }

                var sortOrder = new Dictionary<string, int>
                {
                    { "RFID abweichend", 0 }, { "Nicht eindeutig", 1 }, { "Nicht zugeordnet", 2 }, { "OK", 3 }
                };
                results.Sort((a, b) =>
                {
                    int oa = 9, ob = 9;
                    sortOrder.TryGetValue(a.Status, out oa);
                    sortOrder.TryGetValue(b.Status, out ob);
                    int cmp = oa.CompareTo(ob);
                    return cmp != 0 ? cmp : string.Compare(a.VirtuagymName, b.VirtuagymName, StringComparison.OrdinalIgnoreCase);
                });

                _jablotronCompareResults = results;
                dataGridJablotronCompare.ItemsSource = results;
                txtJablotronCompareFilter.Text = "";

                int okCount = results.Count(r => r.Status == "OK");
                int mismatch = results.Count(r => r.Status == "RFID abweichend");
                int noMatch = results.Count(r => r.Status == "Nicht zugeordnet");
                int ambiguous = results.Count(r => r.Status == "Nicht eindeutig");

                txtJablotronCompareSummary.Text =
                    $"{okCount} OK | {mismatch} RFID abweichend | {ambiguous} nicht eindeutig | {noMatch} nicht zugeordnet   " +
                    $"(Virtuagym: {members.Count} aktive Member, Jablotron: {jabUsersWithCard.Count} Benutzer mit Karte)";

                if (mismatch > 0)
                {
                    txtJablotronCompareStatus.Text = $"{mismatch} Abweichungen gefunden (vorausgewählt). Filter: Alle {results.Count} Einträge angezeigt.";
                    txtJablotronCompareStatus.Foreground = Brushes.OrangeRed;
                }
                else
                {
                    txtJablotronCompareStatus.Text = $"Alle zugeordneten RFID-Tags stimmen überein. {results.Count} Einträge angezeigt.";
                    txtJablotronCompareStatus.Foreground = Brushes.Green;
                }

                btnJablotronSelectAll.IsEnabled = mismatch > 0;
                btnJablotronSelectNone.IsEnabled = mismatch > 0;
                btnJablotronUpdateSelected.IsEnabled = mismatch > 0;
                btnJablotronExportCsv.IsEnabled = results.Count > 0;
            }
            catch (Exception ex)
            {
                txtJablotronCompareStatus.Text = $"Fehler: {ex.Message}";
                txtJablotronCompareStatus.Foreground = Brushes.Red;
                dataGridJablotronCompare.ItemsSource = null;
                txtJablotronCompareSummary.Text = "";
                btnJablotronSelectAll.IsEnabled = false;
                btnJablotronSelectNone.IsEnabled = false;
                btnJablotronUpdateSelected.IsEnabled = false;
                btnJablotronExportCsv.IsEnabled = false;
            }
        }

        private void btnJablotronSelectAll_Click(object sender, RoutedEventArgs e)
        {
            if (_jablotronCompareResults == null) return;
            foreach (var r in _jablotronCompareResults.Where(r => r.Status == "RFID abweichend"))
                r.IsSelected = true;
        }

        private void btnJablotronSelectNone_Click(object sender, RoutedEventArgs e)
        {
            if (_jablotronCompareResults == null) return;
            foreach (var r in _jablotronCompareResults)
                r.IsSelected = false;
        }

        private void txtJablotronCompareFilter_TextChanged(object sender, TextChangedEventArgs e)
        {
            ApplyJablotronCompareFilters();
        }

        private void cmbJablotronStatusFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_jablotronCompareResults == null) return;
            ApplyJablotronCompareFilters();
        }

        private void ApplyJablotronCompareFilters()
        {
            if (_jablotronCompareResults == null) return;

            var textFilter = txtJablotronCompareFilter.Text?.Trim();
            string statusFilter = (cmbJablotronStatusFilter.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "";

            IEnumerable<RfidCompareResult> filtered = _jablotronCompareResults;

            if (!string.IsNullOrEmpty(statusFilter))
                filtered = filtered.Where(r => r.Status == statusFilter);

            if (!string.IsNullOrEmpty(textFilter))
            {
                var lower = textFilter.ToLowerInvariant();
                filtered = filtered.Where(r =>
                    (r.VirtuagymName ?? "").ToLowerInvariant().Contains(lower) ||
                    (r.JablotronName ?? "").ToLowerInvariant().Contains(lower) ||
                    (r.VirtuagymRfidTag ?? "").ToLowerInvariant().Contains(lower) ||
                    (r.JablotronAccessCard ?? "").ToLowerInvariant().Contains(lower) ||
                    (r.MatchInfo ?? "").ToLowerInvariant().Contains(lower)
                );
            }

            if (string.IsNullOrEmpty(textFilter) && string.IsNullOrEmpty(statusFilter))
            {
                dataGridJablotronCompare.ItemsSource = _jablotronCompareResults;
                txtJablotronCompareFilterInfo.Text = "";
            }
            else
            {
                var result = filtered.ToList();
                dataGridJablotronCompare.ItemsSource = result;
                txtJablotronCompareFilterInfo.Text = $"{result.Count} von {_jablotronCompareResults.Count} Einträgen";
            }
        }

        private void btnJablotronCompareFilterClear_Click(object sender, RoutedEventArgs e)
        {
            txtJablotronCompareFilter.Text = "";
            cmbJablotronStatusFilter.SelectedIndex = 0;
        }

        private void btnJablotronExportCsv_Click(object sender, RoutedEventArgs e)
        {
            var entries = dataGridJablotronCompare.ItemsSource as IEnumerable<RfidCompareResult>;
            if (entries == null || !entries.Any())
            {
                txtJablotronCompareStatus.Text = "Keine Daten zum Exportieren vorhanden.";
                txtJablotronCompareStatus.Foreground = Brushes.Orange;
                return;
            }

            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "CSV-Dateien (*.csv)|*.csv",
                DefaultExt = ".csv",
                FileName = "rfid_abgleich_export.csv"
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    var sb = new StringBuilder();
                    sb.AppendLine("Status;Virtuagym Member;VG Aktiv;Virtuagym RFID;Jablotron Name;Jab Aktiv;Jablotron Karte;Info");

                    foreach (var entry in entries)
                    {
                        sb.AppendLine(string.Join(";",
                            Escape(entry.Status),
                            Escape(entry.VirtuagymName),
                            entry.VirtuagymActive ? "Ja" : "Nein",
                            Escape(entry.VirtuagymRfidTag),
                            Escape(entry.JablotronName),
                            entry.JablotronActive ? "Ja" : "Nein",
                            Escape(entry.JablotronAccessCard),
                            Escape(entry.MatchInfo)
                        ));
                    }

                    File.WriteAllText(dialog.FileName, sb.ToString(), Encoding.UTF8);

                    txtJablotronCompareStatus.Text = $"{entries.Count()} Einträge nach {Path.GetFileName(dialog.FileName)} exportiert.";
                    txtJablotronCompareStatus.Foreground = Brushes.Green;
                }
                catch (Exception ex)
                {
                    txtJablotronCompareStatus.Text = $"Export-Fehler: {ex.Message}";
                    txtJablotronCompareStatus.Foreground = Brushes.Red;
                }
            }
        }

        private async void btnJablotronUpdateSelected_Click(object sender, RoutedEventArgs e)
        {
            if (_jablotronCompareResults == null) return;

            var toUpdate = _jablotronCompareResults
                .Where(r => r.IsSelected && r.Status == "RFID abweichend" && !string.IsNullOrEmpty(r.JablotronAccessCard))
                .ToList();

            if (toUpdate.Count == 0)
            {
                txtJablotronCompareStatus.Text = L.T("Dev_Jablotron_NoEntriesSelected");
                txtJablotronCompareStatus.Foreground = Brushes.Orange;
                return;
            }

            var confirm = MessageBox.Show(
                string.Format(L.T("Dev_Jablotron_UpdateConfirm"), toUpdate.Count),
                L.T("Dev_Jablotron_UpdateTitle"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (confirm != MessageBoxResult.Yes) return;

            btnJablotronUpdateSelected.IsEnabled = false;
            btnJablotronCompareRfid.IsEnabled = false;
            progressJablotronUpdate.Visibility = Visibility.Visible;
            progressJablotronUpdate.Minimum = 0;
            progressJablotronUpdate.Maximum = toUpdate.Count;
            progressJablotronUpdate.Value = 0;

            int successCount = 0;
            int errorCount = 0;
            var errors = new List<string>();

            try
            {
                var cache = new MemberCacheService();

                using (var api = VirtuagymApiServiceFactory.Create())
                {
                    for (int i = 0; i < toUpdate.Count; i++)
                    {
                        var item = toUpdate[i];
                        string newRfid = item.JablotronAccessCard;

                        txtJablotronUpdateProgress.Text = $"({i + 1}/{toUpdate.Count}) {item.VirtuagymName}...";
                        progressJablotronUpdate.Value = i;

                        try
                        {
                            if (long.TryParse(item.VirtuagymMemberId, out long vgMemberId))
                            {
                                cache.UpdateRfidTag(vgMemberId, newRfid);
                            }

                            await api.Members.UpdateRfidTagAsync(item.VirtuagymMemberId, newRfid);

                            item.VirtuagymRfidTag = newRfid;
                            item.Status = "OK";
                            item.IsSelected = false;
                            item.MatchInfo = $"Aktualisiert: {newRfid}";
                            successCount++;
                        }
                        catch (Exception ex)
                        {
                            errorCount++;
                            item.MatchInfo = $"Fehler: {ex.Message}";
                            errors.Add($"{item.VirtuagymName}: {ex.Message}");
                        }

                        await Task.Delay(50);
                    }
                }

                cache.Dispose();
            }
            catch (Exception ex)
            {
                errors.Add("Allgemeiner Fehler: " + ex.Message);
            }
            finally
            {
                progressJablotronUpdate.Value = toUpdate.Count;
                progressJablotronUpdate.Visibility = Visibility.Collapsed;

                dataGridJablotronCompare.Items.Refresh();

                string msg = $"{successCount} aktualisiert";
                if (errorCount > 0)
                    msg += $", {errorCount} {L.T("Log_TypeError")}";

                txtJablotronUpdateProgress.Text = msg;
                txtJablotronCompareStatus.Text = msg;
                txtJablotronCompareStatus.Foreground = errorCount > 0 ? Brushes.OrangeRed : Brushes.Green;

                if (errors.Count > 0)
                    MessageBox.Show(string.Join("\n", errors), L.T("Dev_Jablotron_UpdateErrors"), MessageBoxButton.OK, MessageBoxImage.Warning);

                btnJablotronUpdateSelected.IsEnabled = true;
                btnJablotronCompareRfid.IsEnabled = true;
            }
        }

        #endregion
    }
}
