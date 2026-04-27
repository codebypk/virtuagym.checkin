using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Virtuagym.CheckIn.Core.Helper;
using Virtuagym.CheckIn.Core.Services;
using Virtuagym.CheckIn.WPF.Properties;
using Virtuagym.API;

namespace Virtuagym.CheckIn.WPF.Controls
{
    public partial class WelcomeScreenSettingsControl : UserControl
    {
        public WelcomeScreenSettingsControl()
        {
            InitializeComponent();
        }

        public void ApplyLocalization()
        {
            grpDisplay.Header = L.T("Settings_Grp_Display");
            lblWelcomeEnabled.Text = L.T("Settings_Lbl_WelcomeEnabled");
            lblShowClubLogo.Text = L.T("Settings_Lbl_ShowClubLogo");
            lblClubLogo.Text = L.T("Settings_Lbl_ClubLogo");
            lblMonitorLabel.Text = L.T("Settings_Lbl_Monitor");
            lblShowQrCameraPreview.Text = L.T("Settings_Lbl_ShowQrCameraPreview");
            lblDeviceHotplugPollInterval.Text = L.T("Settings_Lbl_DeviceHotplugPollIntervalSeconds");

            grpSounds.Header = L.T("Settings_Grp_Sounds");
            lblSoundSuccess.Text = L.T("Settings_Lbl_SoundSuccess");
            lblSoundWarn.Text = L.T("Settings_Lbl_SoundWarn");
            lblSoundError.Text = L.T("Settings_Lbl_SoundError");
            lblSoundDoubleScan.Text = L.T("Settings_Lbl_SoundDoubleScan");

            grpMessages.Header = L.T("Settings_Grp_Messages");
            lblIdleHintsHeader.Text = L.T("Settings_Lbl_IdleHintsHeader");
            lblIdleHintBoth.Text = L.T("Settings_Lbl_IdleHintBoth");
            lblIdleHintQrOnly.Text = L.T("Settings_Lbl_IdleHintQrOnly");
            lblIdleHintRfidOnly.Text = L.T("Settings_Lbl_IdleHintRfidOnly");
            lblCheckinSuccessText.Text = L.T("Settings_Lbl_CheckinSuccessText");
            lblCheckinNotFound.Text = L.T("Settings_Lbl_CheckinNotFound");
            lblCheckinUnknownErr.Text = L.T("Settings_Lbl_CheckinUnknownErr");

            grpApiMessages.Header = L.T("Settings_Grp_ApiMessages");
            lblApiMsgHeader.Text = L.T("Settings_Lbl_ApiMsgHeader");
            lblMsgMemberNotFound.Text = L.T("Settings_Lbl_MsgMemberNotFound");
            lblMsgDoubleScan.Text = L.T("Settings_Lbl_MsgDoubleScan");
            lblMsgCheckinSuccess.Text = L.T("Settings_Lbl_MsgCheckinSuccess");
            lblMsgCheckoutSuccess.Text = L.T("Settings_Lbl_MsgCheckoutSuccess");
            lblMsgCheckinFailed.Text = L.T("Settings_Lbl_MsgCheckinFailed");
            lblMsgCheckoutFailed.Text = L.T("Settings_Lbl_MsgCheckoutFailed");
            lblMsgMemberNotActive.Text = L.T("Settings_Lbl_MsgMemberNotActive");
            lblMsgInsufficientCredits.Text = L.T("Settings_Lbl_MsgInsufficientCredits");
        }

        public void LoadSettings()
        {
            // Display
            chkWelcomeScreenEnabled.IsChecked = Settings.Default.WelcomeScreenEnabled;
            chkShowClubLogo.IsChecked = Settings.Default.ShowClubLogo;
            chkShowQrCameraPreview.IsChecked = Settings.Default.ShowQrCameraPreviewOnWelcome;
            txtClubLogoPath.Text = Settings.Default.ClubLogoPath;
            txtDeviceHotplugPollIntervalSeconds.Text = Settings.Default.DeviceHotplugPollIntervalSeconds.ToString();

            // Monitor-Auswahl befüllen
            cmbWelcomeScreenMonitor.Items.Clear();
            var allScreens = System.Windows.Forms.Screen.AllScreens;
            for (int i = 0; i < allScreens.Length; i++)
            {
                var screen = allScreens[i];
                string label = "Monitor " + (i + 1) + ": " + screen.Bounds.Width + "x" + screen.Bounds.Height;
                if (screen.Primary)
                    label += " (Primär)";
                cmbWelcomeScreenMonitor.Items.Add(new ComboBoxItem { Content = label, Tag = i });
            }
            int savedMonitor = Settings.Default.WelcomeScreenMonitor;
            if (savedMonitor >= 0 && savedMonitor < cmbWelcomeScreenMonitor.Items.Count)
                cmbWelcomeScreenMonitor.SelectedIndex = savedMonitor;
            else if (cmbWelcomeScreenMonitor.Items.Count > 0)
                cmbWelcomeScreenMonitor.SelectedIndex = 0;

            // Sounds
            txtSoundCheckinSuccess.Text = Settings.Default.SoundCheckinSuccess;
            txtSoundCheckinWarn.Text = Settings.Default.SoundCheckinWarn;
            txtSoundCheckinError.Text = Settings.Default.SoundCheckinError;
            txtSoundCheckinDoubleScan.Text = Settings.Default.SoundCheckinDoubleScan;

            // Messages
            txtIdleHintRfidAndQrCode.Text = Settings.Default.IdleHintRfidAndQrCode;
            txtIdleHintQrCode.Text = Settings.Default.IdleHintQrCode;
            txtIdleHintRfid.Text = Settings.Default.IdleHintRfid;
            txtCheckinSuccessText.Text = Settings.Default.CheckinSuccessText;
            txtCheckinNotFoundText.Text = Settings.Default.CheckinNotFoundText;
            txtCheckinUnknownErrorText.Text = Settings.Default.CheckinUnknownErrorText;

            // API Messages
            txtApiMsgMemberNotFoundByRfid.Text = !string.IsNullOrWhiteSpace(Settings.Default.ApiMsgMemberNotFoundByRfid)
                ? Settings.Default.ApiMsgMemberNotFoundByRfid : ApiConstants.MsgMemberNotFoundByRfid;
            txtApiMsgDoubleScanBlocked.Text = !string.IsNullOrWhiteSpace(Settings.Default.ApiMsgDoubleScanBlocked)
                ? Settings.Default.ApiMsgDoubleScanBlocked : ApiConstants.MsgDoubleScanBlocked;
            txtApiMsgCheckinSuccess.Text = !string.IsNullOrWhiteSpace(Settings.Default.ApiMsgCheckinSuccess)
                ? Settings.Default.ApiMsgCheckinSuccess : ApiConstants.MsgCheckinSuccess;
            txtApiMsgCheckoutSuccess.Text = !string.IsNullOrWhiteSpace(Settings.Default.ApiMsgCheckoutSuccess)
                ? Settings.Default.ApiMsgCheckoutSuccess : ApiConstants.MsgCheckoutSuccess;
            txtApiMsgCheckinFailed.Text = !string.IsNullOrWhiteSpace(Settings.Default.ApiMsgCheckinFailed)
                ? Settings.Default.ApiMsgCheckinFailed : ApiConstants.MsgCheckinFailed;
            txtApiMsgCheckoutFailed.Text = !string.IsNullOrWhiteSpace(Settings.Default.ApiMsgCheckoutFailed)
                ? Settings.Default.ApiMsgCheckoutFailed : ApiConstants.MsgCheckoutFailed;
            txtApiMsgMemberNotActive.Text = !string.IsNullOrWhiteSpace(Settings.Default.ApiMsgMemberNotActive)
                ? Settings.Default.ApiMsgMemberNotActive : ApiConstants.MsgMemberNotActive;
            txtApiMsgInsufficientCredits.Text = !string.IsNullOrWhiteSpace(Settings.Default.ApiMsgInsufficientCredits)
                ? Settings.Default.ApiMsgInsufficientCredits : ApiConstants.MsgInsufficientCredits;
        }

        public void SaveSettings(Action<string, string> updateSetting)
        {
            updateSetting("WelcomeScreenEnabled", chkWelcomeScreenEnabled.IsChecked.ToString());
            updateSetting("ShowClubLogo", chkShowClubLogo.IsChecked.ToString());
            updateSetting("ShowQrCameraPreviewOnWelcome", chkShowQrCameraPreview.IsChecked.ToString());
            updateSetting("WelcomeScreenMonitor", ((cmbWelcomeScreenMonitor.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "0"));
            updateSetting("ClubLogoPath", txtClubLogoPath.Text);
            updateSetting("DeviceHotplugPollIntervalSeconds", txtDeviceHotplugPollIntervalSeconds.Text);

            // Sounds
            updateSetting("SoundCheckinSuccess", txtSoundCheckinSuccess.Text);
            updateSetting("SoundCheckinWarn", txtSoundCheckinWarn.Text);
            updateSetting("SoundCheckinError", txtSoundCheckinError.Text);
            updateSetting("SoundCheckinDoubleScan", txtSoundCheckinDoubleScan.Text);

            // Messages
            updateSetting("IdleHintRfidAndQrCode", txtIdleHintRfidAndQrCode.Text);
            updateSetting("IdleHintQrCode", txtIdleHintQrCode.Text);
            updateSetting("IdleHintRfid", txtIdleHintRfid.Text);
            updateSetting("CheckinSuccessText", txtCheckinSuccessText.Text);
            updateSetting("CheckinNotFoundText", txtCheckinNotFoundText.Text);
            updateSetting("CheckinUnknownErrorText", txtCheckinUnknownErrorText.Text);

            // API Messages
            updateSetting("ApiMsgMemberNotFoundByRfid", txtApiMsgMemberNotFoundByRfid.Text);
            updateSetting("ApiMsgDoubleScanBlocked", txtApiMsgDoubleScanBlocked.Text);
            updateSetting("ApiMsgCheckinSuccess", txtApiMsgCheckinSuccess.Text);
            updateSetting("ApiMsgCheckoutSuccess", txtApiMsgCheckoutSuccess.Text);
            updateSetting("ApiMsgCheckinFailed", txtApiMsgCheckinFailed.Text);
            updateSetting("ApiMsgCheckoutFailed", txtApiMsgCheckoutFailed.Text);
            updateSetting("ApiMsgMemberNotActive", txtApiMsgMemberNotActive.Text);
            updateSetting("ApiMsgInsufficientCredits", txtApiMsgInsufficientCredits.Text);
        }

        private void btnBrowseClubLogo_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Bilddateien (*.png;*.jpg;*.jpeg;*.bmp;*.gif)|*.png;*.jpg;*.jpeg;*.bmp;*.gif|Alle Dateien (*.*)|*.*",
                Title = "Club-Logo auswählen"
            };
            if (!string.IsNullOrWhiteSpace(txtClubLogoPath.Text) && File.Exists(txtClubLogoPath.Text))
                dialog.InitialDirectory = System.IO.Path.GetDirectoryName(txtClubLogoPath.Text);

            if (dialog.ShowDialog() == true)
                txtClubLogoPath.Text = dialog.FileName;
        }

        private void btnClearClubLogo_Click(object sender, RoutedEventArgs e)
        {
            txtClubLogoPath.Text = "";
        }

        private void btnBrowseSoundCheckinSuccess_Click(object sender, RoutedEventArgs e)
        {
            BrowseSoundFile(txtSoundCheckinSuccess);
        }

        private void btnBrowseSoundCheckinWarn_Click(object sender, RoutedEventArgs e)
        {
            BrowseSoundFile(txtSoundCheckinWarn);
        }

        private void btnBrowseSoundCheckinError_Click(object sender, RoutedEventArgs e)
        {
            BrowseSoundFile(txtSoundCheckinError);
        }

        private void btnBrowseSoundCheckinDoubleScan_Click(object sender, RoutedEventArgs e)
        {
            BrowseSoundFile(txtSoundCheckinDoubleScan);
        }

        private void BrowseSoundFile(TextBox targetTextBox)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "WAV-Dateien (*.wav)|*.wav|Alle Dateien (*.*)|*.*",
                Title = "Sound-Datei auswählen"
            };

            string currentPath = targetTextBox.Text;
            if (!string.IsNullOrWhiteSpace(currentPath))
            {
                string fullPath = System.IO.Path.IsPathRooted(currentPath)
                    ? currentPath
                    : System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, currentPath);
                if (File.Exists(fullPath))
                    dialog.InitialDirectory = System.IO.Path.GetDirectoryName(fullPath);
            }

            if (dialog.ShowDialog() == true)
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string selectedPath = dialog.FileName;
                if (selectedPath.StartsWith(baseDir, StringComparison.OrdinalIgnoreCase))
                    targetTextBox.Text = selectedPath.Substring(baseDir.Length);
                else
                    targetTextBox.Text = selectedPath;
            }
        }
    }
}
