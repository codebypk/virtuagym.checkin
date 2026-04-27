using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AccessPass.Models;
using AccessPass.Services;
using Hardware.Events;
using Hardware.Services;
using Microsoft.Win32;
using Virtuagym.CheckIn.Core.Services;
using Virtuagym.CheckIn.WPF.Properties;

namespace Virtuagym.CheckIn.WPF
{
    public partial class EditAccessPassWindow : Window
    {
        public AccessPassEntry? ResultEntry { get; private set; }
        public byte[]? PhotoBytes { get; private set; }
        public bool PhotoRemoved { get; private set; }
        private readonly AccessPassEntry _entry;
        private readonly bool _isNew;
        private readonly List<CcidSmartCardReader> _ccidReaders;
        private readonly List<HidCardReader> _hidReaders;
        private readonly AccessPassStore? _store;
        private bool _isReadingCard;
        private string _cardReadBuffer = "";
        private DispatcherTimer? _cardReadTimer;
        private readonly AccessPassQrService _qrService = new();

        public EditAccessPassWindow(AccessPassEntry entry, bool isNew,
            List<CcidSmartCardReader>? ccidReaders = null,
            List<HidCardReader>? hidReaders = null,
            AccessPassStore? store = null)
        {
            InitializeComponent();
            _entry = entry;
            _isNew = isNew;
            _ccidReaders = ccidReaders ?? [];
            _hidReaders = hidReaders ?? [];
            _store = store;

            Title = isNew ? L.T("AP_Edit_Title_New") : L.T("AP_Edit_Title_Edit");

            txtName.Text = entry.Name ?? "";
            txtCardId.Text = entry.CardId ?? "";
            txtTotalUses.Text = entry.TotalUses.ToString();
            txtRemainingUses.Text = entry.RemainingUses.ToString();
            chkIsActive.IsChecked = entry.IsActive;

            if (DateTime.TryParse(entry.ValidFrom, null, DateTimeStyles.RoundtripKind, out var vf))
                dpValidFrom.SelectedDate = vf.ToLocalTime();
            if (DateTime.TryParse(entry.ValidUntil, null, DateTimeStyles.RoundtripKind, out var vu))
                dpValidUntil.SelectedDate = vu.ToLocalTime();

            chkUnlimited.IsChecked = entry.IsUnlimited;
            UpdateUnlimitedState();

            // Load existing avatar
            LoadExistingAvatar();

            // Apply localization to UI elements
            ApplyLocalization();

            // When editing, Card-ID cannot be changed
            if (!isNew && !string.IsNullOrWhiteSpace(entry.CardId))
            {
                btnGenerateCardId.Visibility = Visibility.Collapsed;
                btnReadCard.Visibility = Visibility.Collapsed;
            }
        }

        private void LoadExistingAvatar()
        {
            if (_store == null || string.IsNullOrWhiteSpace(_entry.PhotoFileName)) return;
            var avatarPath = _store.GetAvatarPath(_entry.Id);
            if (avatarPath != null && File.Exists(avatarPath))
            {
                SetAvatarPreview(File.ReadAllBytes(avatarPath));
                btnRemovePhoto.Visibility = Visibility.Visible;
            }
        }

        private void ApplyLocalization()
        {
            btnUploadPhoto.Content = L.T("AP_Edit_Btn_UploadPhoto");
            btnCapturePhoto.Content = L.T("AP_Edit_Btn_Webcam");
            btnRemovePhoto.Content = L.T("AP_Edit_Btn_RemovePhoto");
            btnGenerateCardId.Content = L.T("AP_Edit_Btn_Generate");
            btnReadCard.Content = L.T("AP_Edit_Btn_ReadCard");
            txtCardIdHint.Text = L.T("AP_Edit_Hint_CardId");
            chkUnlimited.Content = L.T("AP_Edit_Lbl_Unlimited");
            chkIsActive.Content = L.T("AP_Edit_Lbl_Active");
            btnGenerateQr.Content = L.T("AP_Edit_Btn_GenerateQr");
            btnSave.Content = L.T("AP_Edit_Btn_Save");
            btnCancel.Content = L.T("Btn_Cancel");
        }

        private void SetAvatarPreview(byte[] bytes)
        {
            var bitmap = new BitmapImage();
            using var ms = new MemoryStream(bytes);
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = ms;
            bitmap.EndInit();
            bitmap.Freeze();
            imgAvatar.Source = bitmap;
        }

        private void btnUploadPhoto_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Filter = $"{L.T("AP_Edit_Filter_Images")}|*.jpg;*.jpeg;*.png;*.bmp|{L.T("AP_Edit_Filter_AllFiles")}|*.*",
                Title = L.T("AP_Edit_Dlg_SelectPhoto")
            };
            if (dlg.ShowDialog() == true)
            {
                var bytes = File.ReadAllBytes(dlg.FileName);
                PhotoBytes = bytes;
                PhotoRemoved = false;
                SetAvatarPreview(bytes);
                btnRemovePhoto.Visibility = Visibility.Visible;
            }
        }

        private void btnCapturePhoto_Click(object sender, RoutedEventArgs e)
        {
            var captureWindow = new WebcamCaptureWindow { Owner = this };
            if (captureWindow.ShowDialog() == true && captureWindow.CapturedBytes != null)
            {
                PhotoBytes = captureWindow.CapturedBytes;
                PhotoRemoved = false;
                SetAvatarPreview(captureWindow.CapturedBytes);
                btnRemovePhoto.Visibility = Visibility.Visible;
            }
        }

        private void btnRemovePhoto_Click(object sender, RoutedEventArgs e)
        {
            PhotoBytes = null;
            PhotoRemoved = true;
            imgAvatar.Source = null;
            btnRemovePhoto.Visibility = Visibility.Collapsed;
        }

        private void btnGenerateCardId_Click(object sender, RoutedEventArgs e)
        {
            txtCardId.Text = Guid.NewGuid().ToString("N");
        }

        private void chkUnlimited_Changed(object sender, RoutedEventArgs e)
        {
            UpdateUnlimitedState();
        }

        private void UpdateUnlimitedState()
        {
            bool unlimited = chkUnlimited.IsChecked == true;
            pnlTotalUses.IsEnabled = !unlimited;
            pnlRemainingUses.IsEnabled = !unlimited;
            pnlValidDates.IsEnabled = !unlimited;

            if (unlimited)
            {
                pnlTotalUses.Opacity = 0.5;
                pnlRemainingUses.Opacity = 0.5;
                pnlValidDates.Opacity = 0.5;
            }
            else
            {
                pnlTotalUses.Opacity = 1;
                pnlRemainingUses.Opacity = 1;
                pnlValidDates.Opacity = 1;
            }
        }

        private void btnReadCard_Click(object sender, RoutedEventArgs e)
        {
            if (_isReadingCard)
            {
                StopCardReading();
                return;
            }

            _isReadingCard = true;
            _cardReadBuffer = "";
            btnReadCard.Content = L.T("Btn_Cancel");
            txtCardIdHint.Text = L.T("AP_Edit_Hint_CardReading");
            txtCardIdHint.Foreground = System.Windows.Media.Brushes.DarkOrange;

            // HID keyboard-wedge capture
            PreviewKeyDown += OnCardReadKeyDown;

            // CCID + HID event subscriptions
            foreach (var ccid in _ccidReaders)
                ccid.CardRead += OnDeviceCardRead;
            foreach (var hid in _hidReaders)
                hid.CardRead += OnDeviceCardRead;

            _cardReadTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
            _cardReadTimer.Tick += (_, _) => StopCardReading();
            _cardReadTimer.Start();
        }

        private void OnCardReadKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter || e.Key == Key.Return)
            {
                if (!string.IsNullOrWhiteSpace(_cardReadBuffer))
                {
                    txtCardId.Text = _cardReadBuffer.Trim();
                }
                StopCardReading();
                e.Handled = true;
                return;
            }

            if (e.Key >= Key.D0 && e.Key <= Key.D9)
                _cardReadBuffer += (char)('0' + (e.Key - Key.D0));
            else if (e.Key >= Key.NumPad0 && e.Key <= Key.NumPad9)
                _cardReadBuffer += (char)('0' + (e.Key - Key.NumPad0));
            else if (e.Key >= Key.A && e.Key <= Key.Z)
                _cardReadBuffer += (char)('A' + (e.Key - Key.A));

            e.Handled = true;
        }

        private void OnDeviceCardRead(object? sender, CardReadEventArgs e)
        {
            Dispatcher.BeginInvoke(() =>
            {
                if (!_isReadingCard) return;
                txtCardId.Text = e.Card.UidHex;
                StopCardReading();
            });
        }

        private void StopCardReading()
        {
            _isReadingCard = false;
            _cardReadBuffer = "";
            btnReadCard.Content = L.T("AP_Edit_Btn_ReadCard");
            txtCardIdHint.Text = L.T("AP_Edit_Hint_CardId");
            txtCardIdHint.Foreground = System.Windows.Media.Brushes.Gray;
            PreviewKeyDown -= OnCardReadKeyDown;

            foreach (var ccid in _ccidReaders)
                ccid.CardRead -= OnDeviceCardRead;
            foreach (var hid in _hidReaders)
                hid.CardRead -= OnDeviceCardRead;

            _cardReadTimer?.Stop();
            _cardReadTimer = null;
        }

        private void btnSave_Click(object sender, RoutedEventArgs e)
        {
            txtValidationMessage.Visibility = Visibility.Collapsed;

            if (string.IsNullOrWhiteSpace(txtName.Text))
            {
                ShowValidationError(L.T("AP_Edit_Err_NameRequired"));
                txtName.Focus();
                return;
            }

            if (string.IsNullOrWhiteSpace(txtCardId.Text))
            {
                ShowValidationError(L.T("AP_Edit_Err_CardIdRequired"));
                return;
            }

            bool unlimited = chkUnlimited.IsChecked == true;

            int totalUses = 0;
            int remainingUses = 0;

            if (!unlimited)
            {
                if (!int.TryParse(txtTotalUses.Text, out totalUses) || totalUses < 0)
                {
                    ShowValidationError(L.T("AP_Edit_Err_TotalUsesPositive"));
                    txtTotalUses.Focus();
                    return;
                }

                if (!int.TryParse(txtRemainingUses.Text, out remainingUses) || remainingUses < 0)
                {
                    ShowValidationError(L.T("AP_Edit_Err_RemainingUsesPositive"));
                    txtRemainingUses.Focus();
                    return;
                }
            }

            _entry.Name = string.IsNullOrWhiteSpace(txtName.Text) ? null : txtName.Text.Trim();
            _entry.CardId = string.IsNullOrWhiteSpace(txtCardId.Text) ? null : txtCardId.Text.Trim();
            _entry.IsUnlimited = unlimited;
            _entry.TotalUses = totalUses;
            _entry.RemainingUses = remainingUses;
            _entry.IsActive = chkIsActive.IsChecked == true;

            _entry.ValidFrom = !unlimited && dpValidFrom.SelectedDate.HasValue
                ? dpValidFrom.SelectedDate.Value.ToUniversalTime().ToString("o")
                : null;
            _entry.ValidUntil = !unlimited && dpValidUntil.SelectedDate.HasValue
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

        private void btnGenerateQr_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(txtCardId.Text))
            {
                txtQrStatus.Text = L.T("AP_Edit_Err_QrNoCardId");
                txtQrStatus.Foreground = System.Windows.Media.Brushes.Red;
                return;
            }

            string secret = Settings.Default.VirtuagymClubSecret;
            if (string.IsNullOrWhiteSpace(secret))
            {
                txtQrStatus.Text = L.T("AP_Edit_Err_QrNoSecret");
                txtQrStatus.Foreground = System.Windows.Media.Brushes.Red;
                return;
            }

            // Build a temporary entry with current form values for QR generation
            var tempEntry = new AccessPassEntry
            {
                Id = _entry.Id,
                CardId = txtCardId.Text.Trim(),
                IsUnlimited = (bool)chkUnlimited.IsChecked,
                ValidUntil = dpValidUntil.SelectedDate?.ToUniversalTime().ToString("o"),
                ValidFrom = dpValidFrom.SelectedDate?.ToUniversalTime().ToString("o"),
            };

            var (token, dataUri) = _qrService.GenerateTokenAndQr(tempEntry, secret);
            if (string.IsNullOrEmpty(dataUri))
            {
                txtQrStatus.Text = L.T("AP_Edit_Err_QrFailed");
                txtQrStatus.Foreground = System.Windows.Media.Brushes.Red;
                return;
            }

            string base64 = dataUri.Substring(dataUri.IndexOf(",") + 1);
            byte[] imageBytes = Convert.FromBase64String(base64);
            var bitmap = new BitmapImage();
            using (var ms = new MemoryStream(imageBytes))
            {
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.StreamSource = ms;
                bitmap.EndInit();
            }

            imgQrCode.Source = bitmap;
            imgQrCode.Visibility = Visibility.Visible;
            txtQrStatus.Text = L.T("AP_Edit_QrGenerated");
            txtQrStatus.Foreground = System.Windows.Media.Brushes.Green;
        }

        private void btnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
