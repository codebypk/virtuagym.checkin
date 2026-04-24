using System;
using System.Windows;
using Virtuagym.CheckIn.Core.Models;
using Virtuagym.CheckIn.Core.Helper;
using Virtuagym.CheckIn.Core.Models;
using Virtuagym.CheckIn.Core.Services;
using Virtuagym.CheckIn.Core.Models;
using Virtuagym.CheckIn.Core.Models;

namespace Virtuagym.CheckIn.WPF
{
    public partial class EditClubServiceWindow : Window
    {
        public ClubService ResultService { get; private set; }

        public EditClubServiceWindow(ClubService service)
        {
            InitializeComponent();
            ApplyLocalization();

            if (service != null)
            {
                // Bearbeitungsmodus - Werte laden
                Title = L.T("EditClubService_Title_Edit");
                txtClubId.Text = service.club_id.ToString();
                txtServiceId.Text = service.service_id;
                txtServicename.Text = service.servicename;
                txtMinCredits.Text = service.min_credits.ToString();
                chkIsActive.IsChecked = service.isActive;
            }
            else
            {
                // Neu-Modus - Standardwerte
                Title = L.T("EditClubService_Title_Add");
                txtClubId.Text = "0";
                txtMinCredits.Text = "-1";
                chkIsActive.IsChecked = true;
            }
        }

        private void ApplyLocalization()
        {
            lblClubId.Text = L.T("EditClubService_Lbl_ClubId");
            lblServiceId.Text = L.T("EditClubService_Lbl_ServiceId");
            lblServiceIdHint.Text = L.T("EditClubService_Hint_ServiceId");
            lblServicename.Text = L.T("EditClubService_Lbl_Servicename");
            lblServicenameHint.Text = L.T("EditClubService_Hint_Servicename");
            lblMinCredits.Text = L.T("EditClubService_Lbl_MinCredits");
            lblMinCreditsHint.Text = L.T("EditClubService_Hint_MinCredits");
            chkIsActive.Content = L.T("EditClubService_Lbl_IsActive");
            btnSave.Content = L.T("Btn_Save");
            btnCancel.Content = L.T("Btn_Cancel");
        }

        private void btnSave_Click(object sender, RoutedEventArgs e)
        {
            if (!ValidateInput())
                return;

            ResultService = new ClubService
            {
                club_id = int.Parse(txtClubId.Text),
                service_id = txtServiceId.Text.Trim(),
                servicename = txtServicename.Text.Trim(),
                min_credits = int.Parse(txtMinCredits.Text),
                isActive = chkIsActive.IsChecked == true
            };

            DialogResult = true;
            Close();
        }

        private bool ValidateInput()
        {
            txtValidationMessage.Visibility = Visibility.Collapsed;

            // Club-ID validieren
            if (!int.TryParse(txtClubId.Text, out int clubId))
            {
                ShowValidationError(L.T("EditClubService_Error_ClubIdInvalid"));
                txtClubId.Focus();
                return false;
            }

            // Service-ID validieren
            if (string.IsNullOrWhiteSpace(txtServiceId.Text))
            {
                ShowValidationError(L.T("EditClubService_Error_ServiceIdEmpty"));
                txtServiceId.Focus();
                return false;
            }

            // Servicename validieren
            if (string.IsNullOrWhiteSpace(txtServicename.Text))
            {
                ShowValidationError(L.T("EditClubService_Error_ServicenameEmpty"));
                txtServicename.Focus();
                return false;
            }

            // Min. Credits validieren
            if (!int.TryParse(txtMinCredits.Text, out int minCredits))
            {
                ShowValidationError(L.T("EditClubService_Error_MinCreditsInvalid"));
                txtMinCredits.Focus();
                return false;
            }

            return true;
        }

        private void ShowValidationError(string message)
        {
            txtValidationMessage.Text = message;
            txtValidationMessage.Visibility = Visibility.Visible;
        }

        private void btnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
