using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AccessPass.Models;
using AccessPass.Services;
using Hardware.Services;
using Virtuagym.CheckIn.Core.Services;
using Virtuagym.CheckIn.WPF.Properties;

namespace Virtuagym.CheckIn.WPF.Controls
{
    public partial class AccessPassControl : UserControl
    {
        private ObservableCollection<AccessPassEntry> _passes = [];
        private AccessPassStore _store;
        private IAccessPassService _service;
        private readonly AccessPassQrService _qrService = new();
        private readonly Services.WpfAppSettings _settings = new();
        private List<CcidSmartCardReader> _ccidReaders = [];
        private List<HidCardReader> _hidReaders = [];

        public void SetCardReaders(List<CcidSmartCardReader> ccidReaders, List<HidCardReader> hidReaders)
        {
            _ccidReaders = ccidReaders;
            _hidReaders = hidReaders;
        }

        public AccessPassControl()
        {
            _store = new AccessPassStore();
            _service = new AccessPassService(_store);
            InitializeComponent();
            _service.DeleteExpiredOrDepletedPasses(_settings.AccessPassDeletionMode, _settings.AccessPassDeletionDays); // Run cleanup at startup
        }

        public void SetAccessPassDependencies(AccessPassStore store, IAccessPassService service)
        {
            _store = store;
            _service = service;
        }

        public void ApplyLocalization()
        {
            // Localization can be extended later
        }

        public void LoadSettings()
        {
            Refresh();
        }

        private void Refresh()
        {
            var all = _service.GetAll()
                .OrderByDescending(p => p.IsActive)
                .ThenByDescending(p => p.CreatedAt)
                .ToList();
            _passes = new ObservableCollection<AccessPassEntry>(all);
            dataGridAccessPasses.ItemsSource = _passes;
            txtStatus.Text = string.Format(L.T("AccessPass_Status_Total"), _passes.Count);
            txtStatus.Foreground = Brushes.Gray;
        }

        private void btnAddPass_Click(object sender, RoutedEventArgs e)
        {
            var newEntry = new AccessPassEntry
            {
                TotalUses = 10,
                RemainingUses = 10,
                ValidFrom = DateTime.UtcNow.ToString("o"),
                ValidUntil = DateTime.UtcNow.AddMonths(3).ToString("o"),
                CreatedAt = DateTime.UtcNow.ToString("o")
            };

            var dialog = new EditAccessPassWindow(newEntry, isNew: true, _ccidReaders, _hidReaders, _store)
            {
                Owner = Window.GetWindow(this)
            };

            if (dialog.ShowDialog() == true && dialog.ResultEntry != null)
            {
                _service.Create(dialog.ResultEntry);
                if (dialog.PhotoBytes != null)
                {
                    var fileName = _service.SaveAvatar(dialog.ResultEntry.Id, dialog.PhotoBytes);
                    dialog.ResultEntry.PhotoFileName = fileName;
                    _service.Update(dialog.ResultEntry);
                }
                Refresh();
                txtStatus.Text = string.Format(L.T("AccessPass_Msg_Created"), dialog.ResultEntry.DisplayName);
                txtStatus.Foreground = Brushes.Green;
            }
        }

        private void btnEditPass_Click(object sender, RoutedEventArgs e)
        {
            var selected = dataGridAccessPasses.SelectedItem as AccessPassEntry;
            if (selected == null)
            {
                MessageBox.Show(L.T("AccessPass_Msg_SelectPass"), L.T("Msg_Note"),
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // Clone for editing
            var clone = new AccessPassEntry
            {
                Id = selected.Id,
                Name = selected.Name,
                CardId = selected.CardId,
                PhotoFileName = selected.PhotoFileName,
                TotalUses = selected.TotalUses,
                RemainingUses = selected.RemainingUses,
                ValidFrom = selected.ValidFrom,
                ValidUntil = selected.ValidUntil,
                IsActive = selected.IsActive,
                IsUnlimited = selected.IsUnlimited,
                LastCheckIn = selected.LastCheckIn,
                LastCheckOut = selected.LastCheckOut,
                CreatedAt = selected.CreatedAt,
                ModifiedAt = selected.ModifiedAt
            };

            var dialog = new EditAccessPassWindow(clone, isNew: false, _ccidReaders, _hidReaders, _store)
            {
                Owner = Window.GetWindow(this)
            };

            if (dialog.ShowDialog() == true && dialog.ResultEntry != null)
            {
                if (dialog.PhotoRemoved)
                {
                    dialog.ResultEntry.PhotoFileName = null;
                }
                else if (dialog.PhotoBytes != null)
                {
                    var fileName = _service.SaveAvatar(dialog.ResultEntry.Id, dialog.PhotoBytes);
                    dialog.ResultEntry.PhotoFileName = fileName;
                }

                _service.Update(dialog.ResultEntry);
                Refresh();
                txtStatus.Text = string.Format(L.T("AccessPass_Msg_Updated"), dialog.ResultEntry.DisplayName);
                txtStatus.Foreground = Brushes.Green;
            }
        }

        private void btnShowQr_Click(object sender, RoutedEventArgs e)
        {
            var selected = dataGridAccessPasses.SelectedItem as AccessPassEntry;
            if (selected == null)
            {
                MessageBox.Show(L.T("AccessPass_Msg_SelectPass"), L.T("Msg_Note"),
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            string secret = Settings.Default.VirtuagymClubSecret;
            var (token, dataUri) = _qrService.GenerateTokenAndQr(selected, secret);

            if (string.IsNullOrEmpty(dataUri))
            {
                MessageBox.Show(L.T("AccessPass_Msg_QrFailed"),
                    L.T("Msg_Error"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Show QR in a simple window
            var qrWindow = new Window
            {
                Title = string.Format(L.T("AccessPass_Msg_QrWindowTitle"), selected.DisplayName),
                Width = 400,
                Height = 480,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = Window.GetWindow(this),
                ResizeMode = ResizeMode.NoResize
            };

            var stack = new StackPanel { Margin = new Thickness(15), HorizontalAlignment = HorizontalAlignment.Center };

            // Convert data URI to BitmapImage
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

            stack.Children.Add(new Image { Source = bitmap, Width = 280, Height = 280, Margin = new Thickness(0, 0, 0, 10) });
            stack.Children.Add(new TextBlock
            {
                Text = token,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 10,
                Foreground = Brushes.Gray,
                MaxWidth = 350,
                Margin = new Thickness(0, 0, 0, 10)
            });
            stack.Children.Add(new Button
            {
                Content = L.T("Btn_Close"),
                Width = 100,
                Height = 28,
                HorizontalAlignment = HorizontalAlignment.Center
            });
            ((Button)stack.Children[2]).Click += (_, _) => qrWindow.Close();

            qrWindow.Content = stack;
            qrWindow.ShowDialog();
        }

        private void btnDeactivatePass_Click(object sender, RoutedEventArgs e)
        {
            var selected = dataGridAccessPasses.SelectedItem as AccessPassEntry;
            if (selected == null)
            {
                MessageBox.Show(L.T("AccessPass_Msg_SelectPass"), L.T("Msg_Note"),
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (!selected.IsActive)
            {
                MessageBox.Show(L.T("AccessPass_Msg_AlreadyDeactivated"), L.T("Msg_Note"),
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var result = MessageBox.Show(
                string.Format(L.T("AccessPass_Msg_DeactivateConfirm"), selected.DisplayName),
                L.T("AccessPass_Msg_DeactivateTitle"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                _service.Deactivate(selected.Id);
                Refresh();
                txtStatus.Text = string.Format(L.T("AccessPass_Msg_Deactivated"), selected.DisplayName);
                txtStatus.Foreground = Brushes.Orange;
            }
        }

        private void btnDeletePass_Click(object sender, RoutedEventArgs e)
        {
            var selected = dataGridAccessPasses.SelectedItem as AccessPassEntry;
            if (selected == null)
            {
                MessageBox.Show(L.T("AccessPass_Msg_SelectPass"), L.T("Msg_Note"),
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var result = MessageBox.Show(
                string.Format(L.T("AccessPass_Msg_DeleteConfirm"), selected.DisplayName),
                L.T("AccessPass_Msg_DeleteTitle"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                _service.Delete(selected.Id);
                Refresh();
                txtStatus.Text = string.Format(L.T("AccessPass_Msg_Deleted"), selected.DisplayName);
                txtStatus.Foreground = Brushes.Green;
            }
        }
    }
}
