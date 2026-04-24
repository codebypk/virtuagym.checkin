using System;
using System.Globalization;
using System.Windows;
using AccessPass.Models;

namespace Virtuagym.CheckIn.WPF
{
    public partial class EditAccessPassWindow : Window
    {
        public AccessPassEntry? ResultEntry { get; private set; }
        private readonly AccessPassEntry _entry;
        private readonly bool _isNew;

        public EditAccessPassWindow(AccessPassEntry entry, bool isNew)
        {
            InitializeComponent();
            _entry = entry;
            _isNew = isNew;

            Title = isNew ? "Neuen Access Pass erstellen" : "Access Pass bearbeiten";

            txtName.Text = entry.Name ?? "";
            txtCardId.Text = entry.CardId ?? "";
            txtTotalUses.Text = entry.TotalUses.ToString();
            txtRemainingUses.Text = entry.RemainingUses.ToString();
            chkIsActive.IsChecked = entry.IsActive;

            if (DateTime.TryParse(entry.ValidFrom, null, DateTimeStyles.RoundtripKind, out var vf))
                dpValidFrom.SelectedDate = vf.ToLocalTime();
            if (DateTime.TryParse(entry.ValidUntil, null, DateTimeStyles.RoundtripKind, out var vu))
                dpValidUntil.SelectedDate = vu.ToLocalTime();
        }

        private void btnSave_Click(object sender, RoutedEventArgs e)
        {
            txtValidationMessage.Visibility = Visibility.Collapsed;

            if (!int.TryParse(txtTotalUses.Text, out int totalUses) || totalUses < 0)
            {
                ShowValidationError("Gesamtanzahl Nutzungen muss eine positive Zahl sein.");
                txtTotalUses.Focus();
                return;
            }

            if (!int.TryParse(txtRemainingUses.Text, out int remainingUses) || remainingUses < 0)
            {
                ShowValidationError("Verbleibende Nutzungen muss eine positive Zahl sein.");
                txtRemainingUses.Focus();
                return;
            }

            _entry.Name = string.IsNullOrWhiteSpace(txtName.Text) ? null : txtName.Text.Trim();
            _entry.CardId = string.IsNullOrWhiteSpace(txtCardId.Text) ? null : txtCardId.Text.Trim();
            _entry.TotalUses = totalUses;
            _entry.RemainingUses = remainingUses;
            _entry.IsActive = chkIsActive.IsChecked == true;

            _entry.ValidFrom = dpValidFrom.SelectedDate.HasValue
                ? dpValidFrom.SelectedDate.Value.ToUniversalTime().ToString("o")
                : null;
            _entry.ValidUntil = dpValidUntil.SelectedDate.HasValue
                ? dpValidUntil.SelectedDate.Value.ToUniversalTime().ToString("o")
                : null;

            ResultEntry = _entry;
            DialogResult = true;
            Close();
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
