using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Xml;
using Virtuagym.CheckIn.Core.Models;
using Virtuagym.CheckIn.WPF.Controls;
using Hardware.Services;
using Virtuagym.CheckIn.Core.Models;
using Virtuagym.CheckIn.Core.Helper;
using Virtuagym.CheckIn.Core.Models;
using Virtuagym.CheckIn.Core.Services;
using Virtuagym.CheckIn.Core.Models;
using Virtuagym.CheckIn.Core.Abstractions;
using Virtuagym.CheckIn.Core.Models;
using Virtuagym.CheckIn.WPF.Properties;

namespace Virtuagym.CheckIn.WPF
{
    /// <summary>
    /// Interaction logic for SettingsWindow.xaml
    /// </summary>
    public partial class SettingsWindow : Window
    {
        private bool _suppressLangChange = false;

        public void SetCardReaders(List<CcidSmartCardReader> ccidReaders, List<HidCardReader> hidReaders)
        {
            accessPassControl.SetCardReaders(ccidReaders, hidReaders);
        }

        public SettingsWindow()
        {
            InitializeComponent();
            ApplyLocalization();

            welcomeScreenControl.LoadSettings();
            apiConnectionControl.LoadSettings();
            checkinMappingsControl.LoadSettings();
            clubServicesControl.LoadSettings();
            accessPassControl.LoadSettings();
            jablotronClientControl.LoadSettings();

            developerSimulationControl.SetMappings(checkinMappingsControl.CheckinClientMappings);

            // General settings (language, debug mode) owned by window
            chkDebugMode.IsChecked = Settings.Default.DebugMode;
            chkClearTransientDataOnStartup.IsChecked = Settings.Default.ClearTransientMemberDataOnStartup;
            if (Debugger.IsAttached)
                chkDebugMode.IsChecked = Settings.Default.DebugMode;
        }

        private void tabControlMain_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.Source != tabControlMain)
                return;
        }

        private void cmbLanguage_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressLangChange) return;
            // Language change preview â€” no restart needed here; applied on Save
        }

        private void ApplyLocalization()
        {
            Title = L.T("Settings_Title");
            tabVirtuagymClient.Header  = L.T("Settings_Tab_VirtuagymClient");
            tabAccessPass.Header       = L.T("Settings_Tab_AccessPass");
            tabJablotronClient.Header  = L.T("Settings_Tab_JablotronClient");
            tabDeveloper.Header        = L.T("Settings_Tab_Developer");
            tabSubWelcomeScreen.Header  = L.T("Settings_SubTab_WelcomeScreen");
            tabSubApiConnection.Header  = L.T("Settings_SubTab_ApiConnection");
            tabSubCheckinMappings.Header = L.T("Settings_SubTab_CheckinMappings");
            tabSubClubServices.Header = L.T("Settings_SubTab_ClubServices");
            tabSubMemberTools.Header = L.T("Settings_SubTab_MemberTools");
            tabDeveloperSimulation.Header = L.T("Settings_SubTab_DeveloperSimulation");
            tabDeveloperJablotronGateTest.Header = L.T("Settings_SubTab_DeveloperJablotronGateTest");

            lblLanguage.Text  = L.T("Settings_Lbl_Language");
            lblDebugMode.Text = L.T("Settings_Lbl_DebugMode");
            lblClearTransientDataOnStartup.Text = L.T("Settings_Lbl_ClearTransientDataOnStartup");

            btnSave.Content   = L.T("Settings_Btn_Save");
            btnCancel.Content = L.T("Settings_Btn_Cancel");
            lblRestartHint.Text = L.T("Settings_Hint_RestartRequired");

            // Populate language ComboBox dynamically from available lang_*.json files.
            // New languages are auto-detected – just add a lang_XX.json with a "Lang_SelfName" key.
            _suppressLangChange = true;
            cmbLanguage.Items.Clear();
            var availableLanguages = L.GetAvailableLanguages();
            foreach (var lang in availableLanguages)
                cmbLanguage.Items.Add(new ComboBoxItem { Content = lang.Value, Tag = lang.Key });
            string currentLang = Settings.Default.AppLanguage;
            foreach (ComboBoxItem item in cmbLanguage.Items)
                if ((string)item.Tag == currentLang) { cmbLanguage.SelectedItem = item; break; }
            if (cmbLanguage.SelectedIndex < 0) cmbLanguage.SelectedIndex = 0;
            _suppressLangChange = false;

            welcomeScreenControl.ApplyLocalization();
            apiConnectionControl.ApplyLocalization();
            checkinMappingsControl.ApplyLocalization();
            clubServicesControl.ApplyLocalization();
            jablotronClientControl.ApplyLocalization();
            memberToolsControl.ApplyLocalization();
            accessPassControl.ApplyLocalization();
            developerSimulationControl.ApplyLocalization();
            developerJablotronGateTestControl.ApplyLocalization();
        }

        private void btnSave_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!checkinMappingsControl.ValidateMappings())
                    return;

                string configPath = typeof(Settings).Assembly.Location + ".config";
                SaveAllSettings(configPath);

                var result = MessageBox.Show(L.T("Settings_Msg_SavedRestart"),
                    L.T("Settings_Msg_SavedRestartTitle"), MessageBoxButton.OKCancel, MessageBoxImage.Information);

                if (result == MessageBoxResult.OK)
                    RestartApplication();
                else
                {
                    DialogResult = true;
                    Close();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(string.Format(L.T("Settings_Msg_SaveError"), ex.Message), L.T("Msg_Error"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void SaveAllSettings(string configPath)
        {
            XmlDocument doc = new XmlDocument();
            doc.Load(configPath);

            XmlNode settingsNode = doc.SelectSingleNode("//applicationSettings/Virtuagym.CheckIn.Properties.Settings");
            if (settingsNode == null) return;

            void update(string key, string val) => UpdateSettingValue(settingsNode, key, val);

            // Window-eigene Einstellungen
            update("DebugMode", chkDebugMode.IsChecked.ToString());
            update("ClearTransientMemberDataOnStartup", chkClearTransientDataOnStartup.IsChecked.ToString());
            string selectedLang = (cmbLanguage.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "en";
            update("AppLanguage", selectedLang);

            // Delegate to each UserControl
            welcomeScreenControl.SaveSettings(update);
            apiConnectionControl.SaveSettings(update);
            checkinMappingsControl.SaveSettings(update);
            clubServicesControl.SaveSettings(update);
            jablotronClientControl.SaveSettings(update);

            doc.Save(configPath);
        }

        private void UpdateSettingValue(XmlNode settingsNode, string settingName, string value)
        {
            XmlNode settingNode = settingsNode.SelectSingleNode($"setting[@name='{settingName}']/value");
            if (settingNode != null)
                settingNode.InnerText = value;
        }

        private void RestartApplication()
        {
            string exePath = System.Windows.Forms.Application.ExecutablePath;

            if (Owner is MainWindow mainWindow)
                mainWindow.IsRestarting = true;

            Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/C timeout /t 2 /nobreak >nul & \"{exePath}\"",
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            });

            Application.Current.Shutdown();
        }

        private void btnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }

    /// <summary>
    /// ILogWriter-Implementierung die in eine ListView im SettingsWindow schreibt.
    /// Wird fÃ¼r die Simulation und die TÃ¼rÃ¶ffnung im Settings-Fenster verwendet.
    /// </summary>
    /// <summary>
    /// ILogWriter-Implementierung die in eine ListView im SettingsWindow schreibt.
    /// Wird fÃ¼r die Simulation und die TÃ¼rÃ¶ffnung im Settings-Fenster verwendet.
    /// </summary>
    internal class SettingsLogWriter : ILogWriter
    {
        private readonly System.Windows.Controls.ListView _listView;

        public SettingsLogWriter(System.Windows.Controls.ListView listView)
        {
            _listView = listView;
        }

        public void WriteToLog(string text, int type = Constants.LogInfo)
        {
            try
            {
                Application.Current.Dispatcher.BeginInvoke(
                    System.Windows.Threading.DispatcherPriority.Background,
                    new Action(() =>
                    {
                        if (_listView.Items.Count > Constants.MaxLogEntries)
                            _listView.Items.Clear();

                        string sType = "Info: ";
                        Brush textColor = Brushes.Black;
                        if (type == Constants.LogWarning)
                        {
                            sType = "Warning!: ";
                            textColor = Brushes.Orange;
                        }
                        else if (type == Constants.LogError)
                        {
                            sType = "Error!: ";
                            textColor = Brushes.Red;
                        }
                        else if (type == Constants.LogSuccess)
                        {
                            textColor = Brushes.Green;
                        }

                        string logText = DateTime.Now.ToString(Constants.LogTimestampFormat) + " # " + sType + text;

                        _listView.Items.Add(new System.Windows.Controls.ListViewItem()
                        {
                            Foreground = textColor,
                            Content = logText,
                        });
                    }));
            }
            catch
            {
                // UI-Logging darf die Anwendung niemals crashen
            }
        }

        public void WriteToRejectedLog(string cardId, string status, string reason)
        {
            WriteToLog($"REJECTED: {cardId} ({status}: {reason})", Constants.LogWarning);
        }
    }
}
