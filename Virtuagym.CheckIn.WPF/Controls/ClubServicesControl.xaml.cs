using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Text.Json;
using Virtuagym.API.Serialization;
using Virtuagym.CheckIn.Core.Models;
using Virtuagym.CheckIn.Core.Helper;
using Virtuagym.CheckIn.Core.Models;
using Virtuagym.CheckIn.Core.Models;
using Virtuagym.CheckIn.Core.Models;
using Virtuagym.CheckIn.Core.Services;

namespace Virtuagym.CheckIn.WPF.Controls
{
    public partial class ClubServicesControl : UserControl
    {
        private ObservableCollection<ClubService> _clubServices;

        public ClubServicesControl()
        {
            InitializeComponent();
        }

        public void ApplyLocalization()
        {
            txtDescription.Text = L.T("Settings_ClubServices_Description");
            btnAddService.Content = L.T("Settings_Btn_AddService");
            btnEditService.Content = L.T("Btn_Edit");
            btnDeleteService.Content = L.T("Btn_Delete");
            btnReloadServices.Content = L.T("Settings_Btn_Reload");

            if (dataGridClubServices.Columns.Count >= 5)
            {
                dataGridClubServices.Columns[0].Header = L.T("Settings_Col_ClubId");
                dataGridClubServices.Columns[1].Header = L.T("Settings_Col_ServiceId");
                dataGridClubServices.Columns[2].Header = L.T("Settings_Col_Servicename");
                dataGridClubServices.Columns[3].Header = L.T("Settings_Col_MinCredits");
                dataGridClubServices.Columns[4].Header = L.T("Settings_Col_IsActive");
            }
        }

        public void LoadSettings()
        {
            _clubServices = new ObservableCollection<ClubService>();

            try
            {
                var loaded = ClubServiceLoader.Load();
                if (loaded != null)
                {
                    foreach (var service in loaded)
                    {
                        _clubServices.Add(service);
                    }
                }

                txtServiceStatus.Text = string.Format(L.T("Settings_ClubServices_Loaded"), _clubServices.Count);
                txtServiceStatus.Foreground = Brushes.Green;
            }
            catch (Exception ex)
            {
                txtServiceStatus.Text = string.Format(L.T("Settings_ClubServices_LoadError"), ex.Message);
                txtServiceStatus.Foreground = Brushes.Red;
            }

            dataGridClubServices.ItemsSource = _clubServices;
        }

        public void SaveSettings(Action<string, string> updateSetting)
        {
            try
            {
                var servicesList = _clubServices.ToList();
                var serializer = new JsonSerializerAdapter();
                string json = serializer.Serialize(servicesList);

                // Stelle sicher, dass das Resources-Verzeichnis existiert
                var clubServiceFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, Constants.ClubServiceFilePath);
                string resourcesDir = Path.GetDirectoryName(clubServiceFilePath);
                if (!Directory.Exists(resourcesDir))
                {
                    Directory.CreateDirectory(resourcesDir);
                }

                // Speichere die JSON-Datei mit Formatierung
                string formattedJson = FormatJson(json);
                File.WriteAllText(clubServiceFilePath, formattedJson, Encoding.UTF8);

                // Cache leeren, damit die Änderungen übernommen werden
                ClubServiceLoader.ClearCache();

                txtServiceStatus.Text = string.Format(L.T("Settings_ClubServices_Saved"), servicesList.Count);
                txtServiceStatus.Foreground = Brushes.Green;
            }
            catch (Exception ex)
            {
                txtServiceStatus.Text = string.Format(L.T("Settings_ClubServices_SaveError"), ex.Message);
                txtServiceStatus.Foreground = Brushes.Red;
                throw;
            }
        }

        private string FormatJson(string json)
        {
            try
            {
                using var document = JsonDocument.Parse(json);
                return JsonSerializer.Serialize(document.RootElement, new JsonSerializerOptions
                {
                    WriteIndented = true
                });
            }
            catch
            {
                return json;
            }
        }

        private void btnAddService_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new EditClubServiceWindow(null);
            dialog.Owner = Window.GetWindow(this);

            if (dialog.ShowDialog() == true && dialog.ResultService != null)
            {
                _clubServices.Add(dialog.ResultService);
                txtServiceStatus.Text = string.Format(L.T("Settings_ClubServices_Added"), dialog.ResultService.servicename);
                txtServiceStatus.Foreground = Brushes.Green;
            }
        }

        private void btnEditService_Click(object sender, RoutedEventArgs e)
        {
            var selected = dataGridClubServices.SelectedItem as ClubService;
            if (selected == null)
            {
                MessageBox.Show(L.T("Settings_ClubServices_SelectService"), L.T("Msg_Note"), 
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            int index = _clubServices.IndexOf(selected);
            var dialog = new EditClubServiceWindow(selected);
            dialog.Owner = Window.GetWindow(this);

            if (dialog.ShowDialog() == true && dialog.ResultService != null)
            {
                _clubServices[index] = dialog.ResultService;
                dataGridClubServices.Items.Refresh();
                txtServiceStatus.Text = string.Format(L.T("Settings_ClubServices_Updated"), dialog.ResultService.servicename);
                txtServiceStatus.Foreground = Brushes.Green;
            }
        }

        private void btnDeleteService_Click(object sender, RoutedEventArgs e)
        {
            var selected = dataGridClubServices.SelectedItem as ClubService;
            if (selected == null)
            {
                MessageBox.Show(L.T("Settings_ClubServices_SelectService"), L.T("Msg_Note"), 
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var result = MessageBox.Show(
                string.Format(L.T("Settings_ClubServices_DeleteConfirm"), selected.servicename, selected.service_id),
                L.T("Settings_ClubServices_DeleteTitle"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                _clubServices.Remove(selected);
                txtServiceStatus.Text = string.Format(L.T("Settings_ClubServices_Deleted"), selected.servicename);
                txtServiceStatus.Foreground = Brushes.Green;
            }
        }

        private void btnReloadServices_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show(
                L.T("Settings_ClubServices_ReloadConfirm"),
                L.T("Settings_ClubServices_ReloadTitle"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                LoadSettings();
            }
        }
    }
}
