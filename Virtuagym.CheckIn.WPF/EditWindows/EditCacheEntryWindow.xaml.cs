using System;
using System.Windows;
using Virtuagym.API.Cache.Models;
using Virtuagym.CheckIn.Core.Helper;
using Virtuagym.CheckIn.Core.Services;

namespace Virtuagym.CheckIn.WPF
{
    /// <summary>
    /// Interaction logic for EditCacheEntryWindow.xaml
    /// </summary>
    public partial class EditCacheEntryWindow : Window
    {
        /// <summary>
        /// Der bearbeitete Cache-Eintrag (nach Speichern aktualisiert).
        /// </summary>
        public MemberCacheEntry Entry { get; private set; }

        public EditCacheEntryWindow(MemberCacheEntry entry)
        {
            InitializeComponent();
            ApplyLocalization();
            ArgumentNullException.ThrowIfNull(entry);
            Entry = entry;
            LoadEntry();
        }

        private void ApplyLocalization()
        {
            Title                    = L.T("EditCache_Title");
            grpCacheEntry.Header     = L.T("EditCache_Grp_Entry");
            lblCacheMemberId.Text    = L.T("Lbl_MemberId");
            lblCacheFirstname.Text   = L.T("Lbl_Firstname");
            lblCacheLastname.Text    = L.T("Lbl_Lastname");
            lblCacheEmail.Text       = L.T("Lbl_Email");
            lblCachePhone.Text       = L.T("Lbl_Phone");
            lblRfidLocal.Text        = L.T("EditCache_Lbl_RfidLocal");
            lblCacheActive.Text      = L.T("Lbl_Active");
            btnSave.Content          = L.T("Btn_Save");
            btnCancel.Content        = L.T("Btn_Cancel");
        }

        private void LoadEntry()
        {
            txtMemberId.Text = Entry.MemberId.ToString();
            txtFirstname.Text = Entry.Firstname ?? "";
            txtLastname.Text = Entry.Lastname ?? "";
            txtEmail.Text = Entry.Email ?? "";
            txtPhone.Text = Entry.Phone ?? "";
            txtRfidTag.Text = Entry.RfidTag ?? "";
            chkActive.IsChecked = Entry.Active;
        }

        private void btnSave_Click(object sender, RoutedEventArgs e)
        {
            Entry.Firstname = txtFirstname.Text.Trim();
            Entry.Lastname = txtLastname.Text.Trim();
            Entry.Email = txtEmail.Text.Trim();
            Entry.Phone = txtPhone.Text.Trim();
            Entry.RfidTag = txtRfidTag.Text.Trim();
            Entry.Active = chkActive.IsChecked == true;

            DialogResult = true;
            Close();
        }

        private void btnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
