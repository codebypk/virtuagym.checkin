using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AccessPass.Models;
using AccessPass.Services;
using Virtuagym.CheckIn.WPF.Properties;

namespace Virtuagym.CheckIn.WPF.Controls
{
    public partial class AccessPassControl : UserControl
    {
        private ObservableCollection<AccessPassEntry> _passes = [];
        private readonly AccessPassStore _store = new();
        private readonly AccessPassService _service;
        private readonly AccessPassQrService _qrService = new();

        public AccessPassControl()
        {
            _service = new AccessPassService(_store);
            InitializeComponent();
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
            txtStatus.Text = $"{_passes.Count} Pass(e) gesamt";
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

            var dialog = new EditAccessPassWindow(newEntry, isNew: true)
            {
                Owner = Window.GetWindow(this)
            };

            if (dialog.ShowDialog() == true && dialog.ResultEntry != null)
            {
                _service.Create(dialog.ResultEntry);
                Refresh();
                txtStatus.Text = $"Pass '{dialog.ResultEntry.DisplayName}' erstellt.";
                txtStatus.Foreground = Brushes.Green;
            }
        }

        private void btnEditPass_Click(object sender, RoutedEventArgs e)
        {
            var selected = dataGridAccessPasses.SelectedItem as AccessPassEntry;
            if (selected == null)
            {
                MessageBox.Show("Bitte wählen Sie einen Pass aus.", "Hinweis",
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
                LastCheckIn = selected.LastCheckIn,
                LastCheckOut = selected.LastCheckOut,
                CreatedAt = selected.CreatedAt
            };

            var dialog = new EditAccessPassWindow(clone, isNew: false)
            {
                Owner = Window.GetWindow(this)
            };

            if (dialog.ShowDialog() == true && dialog.ResultEntry != null)
            {
                _service.Update(dialog.ResultEntry);
                Refresh();
                txtStatus.Text = $"Pass '{dialog.ResultEntry.DisplayName}' aktualisiert.";
                txtStatus.Foreground = Brushes.Green;
            }
        }

        private void btnShowQr_Click(object sender, RoutedEventArgs e)
        {
            var selected = dataGridAccessPasses.SelectedItem as AccessPassEntry;
            if (selected == null)
            {
                MessageBox.Show("Bitte wählen Sie einen Pass aus.", "Hinweis",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            string secret = Settings.Default.VirtuagymClubSecret;
            var (token, dataUri) = _qrService.GenerateTokenAndQr(selected, secret);

            if (string.IsNullOrEmpty(dataUri))
            {
                MessageBox.Show("QR-Code konnte nicht generiert werden. Prüfen Sie den Club Secret in den API-Einstellungen.",
                    "Fehler", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Show QR in a simple window
            var qrWindow = new Window
            {
                Title = $"QR-Code: {selected.DisplayName}",
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
                Content = "Schließen",
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
                MessageBox.Show("Bitte wählen Sie einen Pass aus.", "Hinweis",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (!selected.IsActive)
            {
                MessageBox.Show("Dieser Pass ist bereits deaktiviert.", "Hinweis",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var result = MessageBox.Show(
                $"Soll der Pass '{selected.DisplayName}' deaktiviert werden?",
                "Pass deaktivieren",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                _service.Deactivate(selected.Id);
                Refresh();
                txtStatus.Text = $"Pass '{selected.DisplayName}' deaktiviert.";
                txtStatus.Foreground = Brushes.Orange;
            }
        }

        private void btnDeletePass_Click(object sender, RoutedEventArgs e)
        {
            var selected = dataGridAccessPasses.SelectedItem as AccessPassEntry;
            if (selected == null)
            {
                MessageBox.Show("Bitte wählen Sie einen Pass aus.", "Hinweis",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var result = MessageBox.Show(
                $"Soll der Pass '{selected.DisplayName}' endgültig gelöscht werden?",
                "Pass löschen",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                _service.Delete(selected.Id);
                Refresh();
                txtStatus.Text = $"Pass '{selected.DisplayName}' gelöscht.";
                txtStatus.Foreground = Brushes.Green;
            }
        }
    }
}
