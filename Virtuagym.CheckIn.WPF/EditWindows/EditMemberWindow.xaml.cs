using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Virtuagym.API;
using Virtuagym.API.Services;
using Virtuagym.API.v1.Models;
using Virtuagym.CheckIn.Core.Models;
using Virtuagym.CheckIn.Core.Helper;
using Virtuagym.CheckIn.Core.Models;
using Virtuagym.CheckIn.Core.Logic;
using Virtuagym.CheckIn.Core.Models;
using Virtuagym.CheckIn.Core.Models;
using Virtuagym.CheckIn.Core.Models;
using Virtuagym.CheckIn.Core.Services;
using Virtuagym.CheckIn.Core.Models;
using Virtuagym.CheckIn.WPF.Services;

namespace Virtuagym.CheckIn.WPF
{
    /// <summary>
    /// Dialog zum Bearbeiten eines Virtuagym-Mitglieds.
    /// Zeigt Profilbild, Stammdaten und Anmelde-History.
    /// Änderungen werden über die v1 API (PUT /member/{id}) gespeichert.
    /// </summary>
    public partial class EditMemberWindow : Window
    {
        private readonly MemberResult _member;
        private readonly Func<VirtuagymApiService> _apiFactory;
        private List<ClubService> _availableCreditServices = new List<ClubService>();
        private bool _isDirty;
        private bool _suppressDirtyTracking;

        /// <summary>
        /// Gibt das aktualisierte Mitglied nach erfolgreichem Speichern zurück.
        /// </summary>
        public MemberResult UpdatedMember { get; private set; }

        public EditMemberWindow(MemberResult member, Func<VirtuagymApiService> apiFactory)
        {
            ArgumentNullException.ThrowIfNull(member);
            ArgumentNullException.ThrowIfNull(apiFactory);
            _member = member;
            _apiFactory = apiFactory;

            InitializeComponent();
            ApplyLocalization();
            PopulateCreditServiceOptions();
            RegisterEventHandlers();
            LoadMemberData();
            UpdateDirtyState();
            LoadAvatarAsync().ContinueWith(t =>
            {
                if (t.IsFaulted)
                    System.Diagnostics.Debug.WriteLine($"Avatar load error: {t.Exception?.InnerException?.Message}");
            }, TaskContinuationOptions.OnlyOnFaulted);
        }

        private void ApplyLocalization()
        {
            Title = L.T("EditMember_Title");
            tabMasterData.Header = L.T("EditMember_Tab_MasterData");
            tabHistory.Header = L.T("EditMember_Tab_History");
            tabCredits.Header = L.T("EditMember_Tab_Credits");
            grpPersonalData.Header = L.T("EditMember_Grp_PersonalData");
            grpContact.Header = L.T("EditMember_Grp_Contact");
            grpRFID.Header = L.T("EditMember_Grp_RFID");
            grpAssignCredits.Header = L.T("EditMember_CreditsAssignGroup");
            lblMemberId.Text = L.T("Lbl_MemberId");
            lblFirstname.Text = L.T("Lbl_Firstname");
            lblLastname.Text = L.T("Lbl_Lastname");
            lblEmail.Text = L.T("Lbl_Email");
            lblGender.Text = L.T("EditMember_Lbl_Gender");
            lblBirthday.Text = L.T("EditMember_Lbl_Birthday");
            lblMemberLang.Text = L.T("EditMember_Lbl_Language");
            lblPhone.Text = L.T("Lbl_Phone");
            lblMobile.Text = L.T("EditMember_Lbl_Mobile");
            lblStreet.Text = L.T("EditMember_Lbl_Street");
            lblStreetExtra.Text = L.T("EditMember_Lbl_StreetExtra");
            lblZIP.Text = L.T("EditMember_Lbl_ZIP");
            lblCity.Text = L.T("EditMember_Lbl_City");
            lblRfidTag.Text = L.T("EditMember_Lbl_RfidTag");
            lblExternalId.Text = L.T("EditMember_Lbl_ExternalId");
            lblIsPro.Text = L.T("EditMember_Lbl_IsPro");
            lblActive.Text = L.T("Lbl_Active");
            lblCreditAmount.Text = L.T("EditMember_Lbl_CreditAmount");
            lblCreditUnlimited.Text = L.T("EditMember_Lbl_CreditUnlimited");
            lblCreditServiceType.Text = L.T("EditMember_Lbl_CreditServiceType");
            lblCreditValidUntil.Text = L.T("EditMember_Lbl_CreditValidUntil");
            lblCreditNotes.Text = L.T("EditMember_Lbl_CreditNotes");
            btnLoadVisits.Content = L.T("EditMember_Btn_LoadHistory");
            btnLoadCredits.Content = L.T("EditMember_Btn_LoadCredits");
            btnAssignCredits.Content = L.T("EditMember_Btn_AssignCredits");
            btnMemberSave.Content = L.T("Btn_Save");
            btnMemberCancel.Content = L.T("Btn_Cancel");

            txtEmail.ToolTip = L.T("EditMember_Tip_Email");
            txtBirthday.ToolTip = L.T("EditMember_Tip_Birthday");
            txtLang.ToolTip = L.T("EditMember_Tip_Language");
            txtPhone.ToolTip = L.T("EditMember_Tip_Phone");
            txtMobile.ToolTip = L.T("EditMember_Tip_Phone");
            txtZip.ToolTip = L.T("EditMember_Tip_Zip");
            txtRfidTag.ToolTip = L.T("EditMember_Tip_RfidTag");
            txtExternalId.ToolTip = L.T("EditMember_Tip_ExternalId");
            txtCreditAmount.ToolTip = L.T("EditMember_Tip_CreditAmount");
            cmbCreditServiceType.ToolTip = L.T("EditMember_Tip_CreditServiceType");
            txtCreditValidUntil.ToolTip = L.T("EditMember_Tip_CreditValidUntil");

            ((ComboBoxItem)cmbGender.Items[0]).Content = string.Empty;
            ((ComboBoxItem)cmbGender.Items[1]).Content = L.T("EditMember_Gender_Male");
            ((ComboBoxItem)cmbGender.Items[2]).Content = L.T("EditMember_Gender_Female");

            if (dataGridVisits.Columns.Count >= 4)
            {
                dataGridVisits.Columns[0].Header = L.T("EditMember_Col_Date");
                dataGridVisits.Columns[1].Header = L.T("EditMember_Col_CheckOut");
                dataGridVisits.Columns[2].Header = L.T("EditMember_Col_Status");
                dataGridVisits.Columns[3].Header = L.T("EditMember_Col_Message");
            }

            if (dataGridCredits.Columns.Count >= 5)
            {
                dataGridCredits.Columns[0].Header = L.T("EditMember_Col_Count");
                dataGridCredits.Columns[1].Header = L.T("EditMember_Col_Unlimited");
                dataGridCredits.Columns[2].Header = L.T("EditMember_Col_ServiceType");
                dataGridCredits.Columns[3].Header = L.T("EditMember_Col_ValidUntil");
                dataGridCredits.Columns[4].Header = L.T("EditMember_Col_Notes");
            }
        }

        private void PopulateCreditServiceOptions()
        {
            _availableCreditServices = ClubServiceLoader.Load()
                .Where(s => s != null && s.isActive && !string.IsNullOrWhiteSpace(s.service_id))
                .OrderBy(s => s.servicename ?? s.service_id)
                .ToList();

            cmbCreditServiceType.ItemsSource = _availableCreditServices;
            cmbCreditServiceType.DisplayMemberPath = nameof(ClubService.DisplayName);
        }

        private void RegisterEventHandlers()
        {
            foreach (var textBox in new[]
            {
                txtFirstname, txtLastname, txtEmail, txtPhone, txtMobile, txtStreet,
                txtStreetExtra, txtZip, txtPlace, txtBirthday, txtLang, txtRfidTag, txtExternalId
            })
            {
                textBox.TextChanged += MemberDataChanged;
            }

            cmbGender.SelectionChanged += MemberDataChanged;
            chkIsPro.Checked += MemberDataChanged;
            chkIsPro.Unchecked += MemberDataChanged;
            chkActive.Checked += MemberDataChanged;
            chkActive.Unchecked += MemberDataChanged;
            chkCreditUnlimited.Checked += CreditUnlimitedChanged;
            chkCreditUnlimited.Unchecked += CreditUnlimitedChanged;
        }

        private void LoadMemberData()
        {
            _suppressDirtyTracking = true;
            try
            {
                txtMemberId.Text = _member.member_id.ToString();
                txtFirstname.Text = _member.firstname ?? string.Empty;
                txtLastname.Text = _member.lastname ?? string.Empty;
                txtEmail.Text = _member.email ?? string.Empty;
                txtPhone.Text = _member.phone ?? string.Empty;
                txtMobile.Text = _member.mobile ?? string.Empty;
                txtStreet.Text = _member.street ?? string.Empty;
                txtStreetExtra.Text = _member.street_extra ?? string.Empty;
                txtZip.Text = _member.zip ?? string.Empty;
                txtPlace.Text = _member.place ?? string.Empty;
                txtBirthday.Text = _member.birthday ?? string.Empty;
                txtLang.Text = _member.lang ?? string.Empty;
                txtRfidTag.Text = _member.rfid_tag ?? string.Empty;
                txtExternalId.Text = _member.external_id ?? string.Empty;
                chkIsPro.IsChecked = _member.is_pro;
                chkActive.IsChecked = _member.active;

                string gender = (_member.gender ?? string.Empty).ToLowerInvariant();
                cmbGender.SelectedIndex = 0;
                foreach (ComboBoxItem item in cmbGender.Items)
                {
                    if ((item.Tag as string) == gender)
                    {
                        cmbGender.SelectedItem = item;
                        break;
                    }
                }

                ResetCreditAssignmentInputs();
                UpdateHeaderPreview();
                UpdateCreditAmountState();
            }
            finally
            {
                _suppressDirtyTracking = false;
            }
        }

        private async Task LoadAvatarAsync()
        {
            string avatarSource = (_member.user_avatar ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(avatarSource))
            {
                imgAvatar.Source = null;
                imgAvatar.ToolTip = L.T("EditMember_AvatarUnavailable");
                return;
            }

            try
            {
                var image = await Task.Run(() => LoadAvatarImage(avatarSource));
                imgAvatar.Source = image;
                imgAvatar.ToolTip = avatarSource;
                WriteLog("Avatar geladen: " + avatarSource, Constants.LogInfo);
            }
            catch (Exception ex)
            {
                imgAvatar.Source = null;
                imgAvatar.ToolTip = L.T("EditMember_AvatarLoadError") + ": " + ex.Message;
                WriteLog("Avatar konnte nicht geladen werden: " + ex.Message, Constants.LogWarning);
            }
        }

        private static BitmapImage LoadAvatarImage(string avatarSource)
        {
            if (File.Exists(avatarSource))
            {
                using (var stream = File.OpenRead(avatarSource))
                {
                    return CreateBitmapImage(stream);
                }
            }

            using var client = new HttpClient();
            var bytes = client.GetByteArrayAsync(avatarSource).GetAwaiter().GetResult();
            using var stream2 = new MemoryStream(bytes);
            return CreateBitmapImage(stream2);
        }

        private static BitmapImage CreateBitmapImage(Stream source)
        {
            var buffer = new MemoryStream();
            source.CopyTo(buffer);
            buffer.Position = 0;

            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = buffer;
            image.EndInit();
            image.Freeze();
            return image;
        }

        #region Anmelde-History

        private async void btnLoadVisits_Click(object sender, RoutedEventArgs e)
        {
            await LoadVisitsAsync();
        }

        private async Task LoadVisitsAsync()
        {
            btnLoadVisits.IsEnabled = false;
            SetStatus(txtVisitStatus, L.T("EditMember_Loading"), Brushes.Gray);
            dataGridVisits.ItemsSource = null;

            try
            {
                using (var api = _apiFactory())
                {
                    var visits = await api.Visits.GetAllAsync($"member_id={_member.member_id}", paginate: true);

                    if (visits != null && visits.Count > 0)
                    {
                        visits.Sort((a, b) => b.check_in_timestamp.CompareTo(a.check_in_timestamp));
                        dataGridVisits.ItemsSource = visits;
                        SetStatus(txtVisitStatus, string.Format(L.T("EditMember_VisitsLoaded"), visits.Count), Brushes.Green);
                        WriteLog("Besuche geladen: " + visits.Count, Constants.LogInfo);
                    }
                    else
                    {
                        SetStatus(txtVisitStatus, L.T("EditMember_NoVisitsFound"), Brushes.Orange);
                    }
                }
            }
            catch (VirtuagymApiException apiEx)
            {
                SetStatus(txtVisitStatus, L.T("EditMember_ApiError") + ": " + apiEx.ApiStatusMessage, Brushes.Red);
                WriteLog("Besuche laden API-Fehler: " + apiEx.ApiStatusMessage, Constants.LogError);
            }
            catch (Exception ex)
            {
                SetStatus(txtVisitStatus, L.T("Msg_Error") + ": " + ex.Message, Brushes.Red);
                WriteLog("Besuche laden Fehler: " + ex.Message, Constants.LogError);
            }
            finally
            {
                btnLoadVisits.IsEnabled = true;
            }
        }

        #endregion

        #region Speichern

        private async void btnMemberSave_Click(object sender, RoutedEventArgs e)
        {
            await SaveMemberAsync();
        }

        private async Task SaveMemberAsync()
        {
            var updateData = CaptureMemberFromForm();
            if (EditMemberWindowLogic.AreEquivalent(_member, updateData))
            {
                SetStatus(txtSaveStatus, L.T("EditMember_Msg_NoChanges"), Brushes.Orange);
                return;
            }

            if (!EditMemberWindowLogic.ValidateMemberUpdate(updateData, out string errorField, out _))
            {
                SetStatus(txtSaveStatus, GetValidationMessage(errorField), Brushes.OrangeRed);
                FocusField(errorField);
                return;
            }

            var confirm = MessageBox.Show(
                L.T("EditMember_Msg_ConfirmSave"),
                L.T("EditMember_Msg_ConfirmSaveTitle"),
                MessageBoxButton.OKCancel,
                MessageBoxImage.Question);

            if (confirm != MessageBoxResult.OK)
                return;

            btnMemberSave.IsEnabled = false;
            btnMemberCancel.IsEnabled = false;
            SetStatus(txtSaveStatus, L.T("EditMember_Saving"), Brushes.Gray);

            try
            {
                using (var api = _apiFactory())
                {
                    var result = await api.Members.UpdateAsync(_member.member_id.ToString(), updateData);

                    if (result != null)
                    {
                        result.user_avatar = string.IsNullOrWhiteSpace(result.user_avatar) ? _member.user_avatar : result.user_avatar;
                        UpdatedMember = result;
                        _isDirty = false;
                        Title = L.T("EditMember_Title");
                        SetStatus(txtSaveStatus, L.T("EditMember_SavedSuccessfully"), Brushes.Green);
                        WriteLog("Mitglied gespeichert: " + _member.member_id, Constants.LogSuccess);

                        DialogResult = true;
                        Close();
                    }
                    else
                    {
                        SetStatus(txtSaveStatus, L.T("EditMember_NoApiResponse"), Brushes.Red);
                        WriteLog("Speichern ohne API-Antwort für Mitglied " + _member.member_id, Constants.LogError);
                    }
                }
            }
            catch (VirtuagymApiException apiEx)
            {
                SetStatus(txtSaveStatus, L.T("EditMember_ApiError") + ": " + apiEx.ApiStatusMessage + " (Code: " + apiEx.ApiStatusCode + ")", Brushes.Red);
                WriteLog("Speichern API-Fehler: " + apiEx.ApiStatusMessage + " (" + apiEx.ApiStatusCode + ")", Constants.LogError);
            }
            catch (Exception ex)
            {
                SetStatus(txtSaveStatus, L.T("Msg_Error") + ": " + ex.Message, Brushes.Red);
                WriteLog("Speichern Fehler: " + ex.Message, Constants.LogError);
            }
            finally
            {
                btnMemberCancel.IsEnabled = true;
                btnMemberSave.IsEnabled = _isDirty;
            }
        }

        #endregion

        #region Credits

        private async void btnLoadCredits_Click(object sender, RoutedEventArgs e)
        {
            await LoadCreditsAsync();
        }

        private async Task LoadCreditsAsync()
        {
            btnLoadCredits.IsEnabled = false;
            SetStatus(txtCreditStatus, L.T("EditMember_Loading"), Brushes.Gray);
            dataGridCredits.ItemsSource = null;

            try
            {
                using (var api = _apiFactory())
                {
                    var credits = await api.Credits.GetByMemberAsync(_member.member_id.ToString());

                    if (credits != null && credits.Count > 0)
                    {
                        dataGridCredits.ItemsSource = credits;
                        SetStatus(txtCreditStatus, string.Format(L.T("EditMember_CreditsLoaded"), credits.Count), Brushes.Green);
                        WriteLog("Credits geladen: " + credits.Count, Constants.LogInfo);
                    }
                    else
                    {
                        SetStatus(txtCreditStatus, L.T("EditMember_NoCreditsFound"), Brushes.Orange);
                    }
                }
            }
            catch (VirtuagymApiException apiEx)
            {
                SetStatus(txtCreditStatus, L.T("EditMember_ApiError") + ": " + apiEx.ApiStatusMessage, Brushes.Red);
                WriteLog("Credits laden API-Fehler: " + apiEx.ApiStatusMessage, Constants.LogError);
            }
            catch (Exception ex)
            {
                SetStatus(txtCreditStatus, L.T("Msg_Error") + ": " + ex.Message, Brushes.Red);
                WriteLog("Credits laden Fehler: " + ex.Message, Constants.LogError);
            }
            finally
            {
                btnLoadCredits.IsEnabled = true;
            }
        }

        private async void btnAssignCredits_Click(object sender, RoutedEventArgs e)
        {
            await AssignCreditsAsync();
        }

        private async Task AssignCreditsAsync()
        {
            if (!EditMemberWindowLogic.TryValidateCreditAssignment(
                txtCreditAmount.Text,
                chkCreditUnlimited.IsChecked == true,
                GetSelectedCreditServiceType(),
                txtCreditValidUntil.Text,
                out int amount,
                out string serviceType,
                out string validUntil,
                out string errorField,
                out _))
            {
                SetStatus(txtAssignCreditStatus, GetValidationMessage(errorField), Brushes.OrangeRed);
                FocusField(errorField);
                return;
            }

            btnAssignCredits.IsEnabled = false;
            SetStatus(txtAssignCreditStatus, L.T("EditMember_AssigningCredits"), Brushes.Gray);

            try
            {
                var credit = new CreditResult
                {
                    member_id = _member.member_id,
                    member_email = _member.email,
                    credit_amount = amount,
                    credit_unlimited = chkCreditUnlimited.IsChecked == true,
                    service_type = serviceType,
                    valid_until = validUntil,
                    notes = (txtCreditNotes.Text ?? string.Empty).Trim()
                };

                using (var api = _apiFactory())
                {
                    var result = await api.Credits.AssignAsync(credit);

                    if (result != null)
                    {
                        SetStatus(txtAssignCreditStatus, L.T("EditMember_CreditsAssigned"), Brushes.Green);
                        WriteLog("Credits zugewiesen für Mitglied " + _member.member_id + ": " + serviceType, Constants.LogSuccess);
                        ResetCreditAssignmentInputs();
                        await LoadCreditsAsync();
                    }
                    else
                    {
                        SetStatus(txtAssignCreditStatus, L.T("EditMember_NoApiResponse"), Brushes.Red);
                        WriteLog("Credit-Zuweisung ohne API-Antwort für Mitglied " + _member.member_id, Constants.LogError);
                    }
                }
            }
            catch (VirtuagymApiException apiEx)
            {
                SetStatus(txtAssignCreditStatus, L.T("EditMember_ApiError") + ": " + apiEx.ApiStatusMessage, Brushes.Red);
                WriteLog("Credit-Zuweisung API-Fehler: " + apiEx.ApiStatusMessage, Constants.LogError);
            }
            catch (Exception ex)
            {
                SetStatus(txtAssignCreditStatus, L.T("Msg_Error") + ": " + ex.Message, Brushes.Red);
                WriteLog("Credit-Zuweisung Fehler: " + ex.Message, Constants.LogError);
            }
            finally
            {
                btnAssignCredits.IsEnabled = true;
            }
        }

        private void ResetCreditAssignmentInputs()
        {
            txtCreditAmount.Text = string.Empty;
            chkCreditUnlimited.IsChecked = false;
            cmbCreditServiceType.SelectedItem = null;
            cmbCreditServiceType.Text = string.Empty;
            txtCreditValidUntil.Text = string.Empty;
            txtCreditNotes.Text = string.Empty;
            UpdateCreditAmountState();
        }

        private string GetSelectedCreditServiceType()
        {
            if (cmbCreditServiceType.SelectedItem is ClubService selected)
                return selected.service_id;

            string typedText = (cmbCreditServiceType.Text ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(typedText))
                return string.Empty;

            var matchingService = _availableCreditServices.FirstOrDefault(s =>
                string.Equals(s.service_id, typedText, StringComparison.OrdinalIgnoreCase)
                || string.Equals(s.servicename, typedText, StringComparison.OrdinalIgnoreCase)
                || string.Equals(s.DisplayName, typedText, StringComparison.OrdinalIgnoreCase));

            return matchingService != null ? matchingService.service_id : typedText;
        }

        #endregion

        private MemberResult CaptureMemberFromForm()
        {
            return EditMemberWindowLogic.CreateSanitizedMember(new MemberResult
            {
                firstname = txtFirstname.Text,
                lastname = txtLastname.Text,
                email = txtEmail.Text,
                gender = (cmbGender.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? string.Empty,
                birthday = txtBirthday.Text,
                lang = txtLang.Text,
                phone = txtPhone.Text,
                mobile = txtMobile.Text,
                street = txtStreet.Text,
                street_extra = txtStreetExtra.Text,
                zip = txtZip.Text,
                place = txtPlace.Text,
                rfid_tag = txtRfidTag.Text,
                external_id = txtExternalId.Text,
                is_pro = chkIsPro.IsChecked == true,
                active = chkActive.IsChecked == true
            });
        }

        private void MemberDataChanged(object sender, RoutedEventArgs e)
        {
            if (_suppressDirtyTracking)
                return;

            UpdateHeaderPreview();
            UpdateDirtyState();
        }

        private void CreditUnlimitedChanged(object sender, RoutedEventArgs e)
        {
            UpdateCreditAmountState();
        }

        private void UpdateCreditAmountState()
        {
            bool unlimited = chkCreditUnlimited.IsChecked == true;
            txtCreditAmount.IsEnabled = !unlimited;
            if (unlimited)
                txtCreditAmount.Text = string.Empty;
        }

        private void UpdateHeaderPreview()
        {
            string firstName = (txtFirstname.Text ?? string.Empty).Trim();
            string lastName = (txtLastname.Text ?? string.Empty).Trim();
            string email = (txtEmail.Text ?? string.Empty).Trim();
            bool active = chkActive.IsChecked == true;

            txtHeaderName.Text = (firstName + " " + lastName).Trim();
            txtHeaderInfo.Text = "ID: " + _member.member_id
                + (!string.IsNullOrWhiteSpace(email) ? "  •  " + email : string.Empty)
                + (active ? "  •  " + L.T("EditMember_Active") : "  •  " + L.T("EditMember_Inactive"));
        }

        private void UpdateDirtyState()
        {
            _isDirty = !EditMemberWindowLogic.AreEquivalent(_member, CaptureMemberFromForm());
            Title = L.T("EditMember_Title") + (_isDirty ? " *" : string.Empty);
            btnMemberSave.IsEnabled = _isDirty;
        }

        private void FocusField(string fieldName)
        {
            switch (fieldName)
            {
                case "Email":
                    txtEmail.Focus();
                    break;
                case "Birthday":
                    txtBirthday.Focus();
                    break;
                case "Phone":
                    txtPhone.Focus();
                    break;
                case "Mobile":
                    txtMobile.Focus();
                    break;
                case "Zip":
                    txtZip.Focus();
                    break;
                case "Language":
                    txtLang.Focus();
                    break;
                case "CreditAmount":
                    txtCreditAmount.Focus();
                    break;
                case "CreditServiceType":
                    cmbCreditServiceType.Focus();
                    break;
                case "CreditValidUntil":
                    txtCreditValidUntil.Focus();
                    break;
            }
        }

        private string GetValidationMessage(string fieldName)
        {
            switch (fieldName)
            {
                case "Email":
                    return L.T("EditMember_Validation_Email");
                case "Birthday":
                    return L.T("EditMember_Validation_Birthday");
                case "Phone":
                    return L.T("EditMember_Validation_Phone");
                case "Mobile":
                    return L.T("EditMember_Validation_Mobile");
                case "Zip":
                    return L.T("EditMember_Validation_Zip");
                case "Language":
                    return L.T("EditMember_Validation_Language");
                case "CreditAmount":
                    return L.T("EditMember_Validation_CreditAmount");
                case "CreditServiceType":
                    return L.T("EditMember_Validation_CreditServiceType");
                case "CreditValidUntil":
                    return L.T("EditMember_Validation_CreditValidUntil");
                default:
                    return L.T("Msg_Error");
            }
        }

        private static void SetStatus(TextBlock target, string message, Brush brush)
        {
            target.Text = message;
            target.Foreground = brush;
        }

        private void WriteLog(string message, int type)
        {
            try
            {
                Directory.CreateDirectory(Constants.LogFolder);
                string logFile = Path.Combine(Constants.LogFolder,
                    DateTime.Now.ToString(Constants.LogDateFormat) + Constants.LogFileSuffix);
                string typeText = type == Constants.LogError ? L.T("Log_TypeError")
                    : type == Constants.LogWarning ? L.T("Log_TypeWarning")
                    : type == Constants.LogSuccess ? L.T("Log_TypeSuccess")
                    : L.T("Log_TypeInfo");
                File.AppendAllText(logFile,
                    DateTime.Now.ToString(Constants.LogTimestampFormat) + " # " + typeText + ": [EditMember] " + message + Environment.NewLine);
            }
            catch
            {
            }
        }

        private void btnMemberCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (DialogResult == true || !_isDirty)
                return;

            var result = MessageBox.Show(
                L.T("EditMember_Msg_UnsavedChanges"),
                L.T("EditMember_Msg_UnsavedChangesTitle"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes)
                e.Cancel = true;
        }

        private async void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.S)
            {
                e.Handled = true;
                if (btnMemberSave.IsEnabled)
                    await SaveMemberAsync();
                return;
            }

            if (Keyboard.Modifiers == ModifierKeys.None && e.Key == Key.Escape)
            {
                e.Handled = true;
                btnMemberCancel_Click(sender, new RoutedEventArgs());
            }
        }
    }
}
