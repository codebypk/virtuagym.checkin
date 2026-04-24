using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Virtuagym.API.Serialization;
using Virtuagym.API.Models;
using Virtuagym.API.v0;
using Virtuagym.API.v0.Models;
using Virtuagym.API.v1;
using Virtuagym.API.v1.Models;
using System.Configuration;
using Virtuagym.API.Cache.Models;

namespace Virtuagym.API.Services
{
    /// <summary>
    /// Facade for the Virtuagym API.
    /// Provides all sub-services (v0/v1) and delegates existing calls for backward compatibility.
    /// </summary>
    public class VirtuagymApiService : IDisposable
    {
        private readonly HttpClient _httpClient;
        private readonly JsonSerializerAdapter _json;

        // v0 Sub-Services
        [Obsolete("v0 API is deprecated. Use the v1 API.")]
        public DeviceApiService Devices { get; private set; }

        [Obsolete("v0 API is deprecated. Use the v1 API.")]
        public UserApiService Users { get; private set; }

        // v1 Sub-Services
        public ClubApiService Club { get; private set; }
        public MemberApiService Members { get; private set; }
        public EmployeeApiService Employees { get; private set; }
        public VisitsApiService Visits { get; private set; }
        public CreditApiService Credits { get; private set; }

        /// <summary>
        /// Creates the service with explicit parameters.
        /// </summary>
        /// <param name="apiKey">API key for the selected environment.</param>
        /// <param name="apiBaseUrl">Base URL of the API (e.g. https://api.virtuagym.com/api).</param>
        /// <param name="clubSecret">Club secret (e.g. CS-61464-...).</param>
        /// <param name="timeoutSeconds">HTTP timeout in seconds (default: 30).</param>
        public VirtuagymApiService(string apiKey, string apiBaseUrl, string clubSecret,int timeoutSeconds = 30,
            string checkinApiKey = null, string checkinClubSecret = null, string v0Username = null, string v0Password = null)
        {
            apiBaseUrl = (apiBaseUrl ?? "").TrimEnd('/');
            string clubId = VirtuagymApiBase.ExtractClubId(clubSecret);

            _json = new JsonSerializerAdapter();
            _httpClient = new HttpClient();
            _httpClient.Timeout = TimeSpan.FromSeconds(timeoutSeconds);

            InitializeServices(apiKey, clubSecret, apiBaseUrl, clubId,  checkinApiKey, checkinClubSecret,v0Username,v0Password);
        }

        private void InitializeServices(string apiKey, string clubSecret, string apiBaseUrl, string clubId,  
            string checkinApiKey = null, string checkinClubSecret = null, string v0Username = null, string v0Password = null)
        {

            // v0 Services
            Devices = new DeviceApiService(_httpClient, _json, checkinApiKey, checkinClubSecret, apiBaseUrl, clubId);
            Users = new UserApiService(_httpClient, _json, apiKey, clubSecret, apiBaseUrl,clubId, v0Username, v0Password);

            // v1 Services
            Club = new ClubApiService(_httpClient, _json, apiKey, clubSecret, apiBaseUrl, clubId);
            Members = new MemberApiService(_httpClient, _json, apiKey, clubSecret, apiBaseUrl, clubId);
            Employees = new EmployeeApiService(_httpClient, _json, apiKey, clubSecret, apiBaseUrl, clubId);
            Visits = new VisitsApiService(_httpClient, _json, apiKey, clubSecret, apiBaseUrl, clubId);
            Credits = new CreditApiService(_httpClient, _json, apiKey, clubSecret, apiBaseUrl, clubId);
        }

        /// <summary>
        /// Tests the connection to v0 and v1 API.
        /// Succeeds if at least one API version is reachable.
        /// </summary>
        public async Task<ApiTestResult> TestConnectionAsync()
        {
            var result = new ApiTestResult();
            var messages = new List<string>();
            bool v0Ok = false;
            bool v1Ok = false;

            // v0 Test: GET /api/v0/club/{club_id}
            try
            {
                var v0Result = await Users.GetByIdAsync();
                v0Ok = v0Result != null && !String.IsNullOrEmpty(v0Result.name);
                if (v0Ok)
                {
                    messages.Add($"✅ v0 API: OK");
                }
                else
                {
                    messages.Add($"❌ v0 API: Unknown");
                }
            }
            catch (Exception ex)
            {
                messages.Add($"❌ v0 API: {ex.Message}");
            }

            // v1 Test: GET /member (erste Seite)
            try
            {
                var v1Result = await Members.GetAllAsync(paginate: false);
                v1Ok = v1Result != null && v1Result.Count > 0;
                if (v1Ok)
                {
                    messages.Add($"✅ v1 API: OK");
                }
                else
                {
                    messages.Add($"❌ v1 API: Unknown");
                }
            }
            catch (Exception ex)
            {
                messages.Add($"❌ v1 API: {ex.Message}");
            }

            result.Success = v0Ok || v1Ok;
            result.Message = string.Join("\n", messages);

            return result;
        }

        public Task<DeviceCheckinResult> CheckinMemberAsync(string cardId, string memberId = null) => Devices.CheckinMemberAsync(cardId, memberId);
        public Task<List<MemberResult>> GetMembersAsync(bool paginate = true) => Members.GetAllAsync(paginate);
        public Task<MemberResult> GetMemberAsync(string memberId) => Members.GetByIdAsync(memberId);
        public Task<MemberResult> UpdateMemberAsync(MemberResult member) => Members.UpdateAsync(member.member_id.ToString(), member);
        public Task<List<VisitResult>> GetVisitsAsync(string queryParams = null, bool paginate = true) => Visits.GetAllAsync(queryParams, paginate);

        #region Toggle-Checkin (v0 + v1)

        /// <summary>
        /// v0-based toggle check-in via RFID tag (PUT /devices).
        /// Requires a valid CheckinKey (club_secret).
        /// For v1 devices (without CheckinKey) use <see cref="ToggleCheckinByRfidV1Async"/>.
        /// </summary>
        public async Task<CheckinToggleResult> ToggleCheckinByRfidAsync(
            string rfidTag,
            long doubleScanThresholdMs,
            ApiMessageOverrides messageOverrides = null,
            CachedMemberInfo cachedMember = null,
            string deviceId = null)
        {
            var m = messageOverrides ?? new ApiMessageOverrides();
            var lookup = await ResolveMemberAndCheckDoubleScanAsync(rfidTag, doubleScanThresholdMs, messageOverrides, cachedMember, deviceId);
            if (lookup.EarlyReturn != null)
                return lookup.EarlyReturn;

            var checkInOutResult = await CheckinMemberAsync(rfidTag, lookup.MemberId != 0 ? lookup.MemberId.ToString() : null);

            bool isSuccess = checkInOutResult != null
              && (checkInOutResult.status == ApiConstants.StatusOk || checkInOutResult.status == ApiConstants.StatusWarn);
            string[] clientMessages = checkInOutResult?.client_message?.ToArray() ?? [];
            string[] employeeMessages = checkInOutResult?.employee_message?.ToArray() ?? [];
            string avatar = checkInOutResult?.member?.avatar ?? lookup.MemberAvatar;

            // Use member data from API response if no cache hit was available
            if (string.IsNullOrEmpty(lookup.MemberName) && checkInOutResult?.member != null)
            {
                lookup.MemberName = checkInOutResult.member.name
                    ?? ((checkInOutResult.member.firstname ?? "") + " " + (checkInOutResult.member.lastname ?? "")).Trim();
                lookup.MemberAvatar = checkInOutResult.member.avatar;
                if (long.TryParse(checkInOutResult.member.member_id, out long mid))
                    lookup.MemberId = mid;
            }

            string msgCheckinOk = ApiMessageOverrides.Resolve(m.MsgCheckinSuccess, ApiConstants.MsgCheckinSuccess);
            string msgCheckoutOk = ApiMessageOverrides.Resolve(m.MsgCheckoutSuccess, ApiConstants.MsgCheckoutSuccess);
            string msgCheckinFail = ApiMessageOverrides.Resolve(m.MsgCheckinFailed, ApiConstants.MsgCheckinFailed);
            string msgCheckoutFail = ApiMessageOverrides.Resolve(m.MsgCheckoutFailed, ApiConstants.MsgCheckoutFailed);

            string successMsg = clientMessages.Length > 0
                ? string.Join(" | ", clientMessages)
                : (lookup.ActiveVisitExists
                    ? string.Format(msgCheckoutOk, lookup.MemberName)
                    : string.Format(msgCheckinOk, lookup.MemberName));
            string failMsg = clientMessages.Length > 0
                ? string.Join(" | ", clientMessages)
                : (lookup.ActiveVisitExists ? msgCheckoutFail : msgCheckinFail);

            return new CheckinToggleResult
            {
                VisitId = checkInOutResult?.id,
                Success = isSuccess,
                Action = lookup.ActiveVisitExists ? ApiConstants.ActionCheckout : ApiConstants.ActionCheckin,
                Status = checkInOutResult?.status ?? ApiConstants.StatusUnknown,
                MemberName = lookup.MemberName,
                MemberAvatar = avatar,
                MemberId = lookup.MemberId,
                Message = isSuccess ? successMsg : failMsg,
                ClientMessages = clientMessages,
                EmployeeMessage = employeeMessages
            };
        }

        /// <summary>
        /// v1-based toggle check-in via RFID tag (POST /visits).
        /// No CheckinKey (club_secret) needed. Device mapping via status_message
        /// (e.g. "@eingang_haupttuer - Check-In").
        /// </summary>
        public async Task<CheckinToggleResult> ToggleCheckinByRfidV1Async(
            string rfidTag,
            long doubleScanThresholdMs,
            ApiMessageOverrides messageOverrides = null,
            CachedMemberInfo cachedMember = null,
            string deviceId = null)
        {
            var m = messageOverrides ?? new ApiMessageOverrides();
            var lookup = await ResolveMemberAndCheckDoubleScanAsync(rfidTag, doubleScanThresholdMs, messageOverrides, cachedMember, deviceId);
            if (lookup.EarlyReturn != null)
                return lookup.EarlyReturn;

            string msgCheckinOk = ApiMessageOverrides.Resolve(m.MsgCheckinSuccess, ApiConstants.MsgCheckinSuccess);
            string msgCheckoutOk = ApiMessageOverrides.Resolve(m.MsgCheckoutSuccess, ApiConstants.MsgCheckoutSuccess);
            string msgCheckinFail = ApiMessageOverrides.Resolve(m.MsgCheckinFailed, ApiConstants.MsgCheckinFailed);
            string msgCheckoutFail = ApiMessageOverrides.Resolve(m.MsgCheckoutFailed, ApiConstants.MsgCheckoutFailed);

            // status_message for v1: device slug + action (e.g. "@eingang_haupttuer - Check-In").
            // Since v1 has no device_id concept when creating, the slug is transported
            // in status_message so the check-in source remains identifiable in the Virtuagym backend.
            string statusMsgSuffix = lookup.ActiveVisitExists ? " - Check-Out" : " - Check-In";
            string statusMessage = !string.IsNullOrEmpty(deviceId) ? deviceId + statusMsgSuffix : null;

            try
            {
                if (!lookup.ActiveVisitExists)
                {
                    // Check-in via v1 API (POST /visits)
                    long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                    var visit = new VisitResult
                    {
                        member_id = lookup.MemberId,
                        check_in_timestamp = now,
                        status_message = statusMessage
                    };
                    var result = await Visits.CreateAsync(visit);
                    bool isSuccess = result != null && result.id != 0;

                    return new CheckinToggleResult
                    {
                        VisitId = isSuccess ? (long?)result.id : null,
                        Success = isSuccess,
                        Action = ApiConstants.ActionCheckin,
                        Status = isSuccess ? ApiConstants.StatusOk : ApiConstants.StatusUnknown,
                        MemberName = lookup.MemberName,
                        MemberAvatar = lookup.MemberAvatar,
                        MemberId = lookup.MemberId,
                        Message = isSuccess
                            ? string.Format(msgCheckinOk, lookup.MemberName)
                            : msgCheckinFail,
                        ClientMessages = [],
                        EmployeeMessage = []
                    };
                }
                else
                {
                    // Check-out via v1 API (POST /visits with check_out_timestamp)
                    long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                    bool isSuccess = false;

                    var checkoutData = new VisitRequest()
                    {
                        action = ApiConstants.VisitActionCheckout,
                        member_id = lookup.MemberId.ToString(),
                        status = ApiConstants.StatusOk,
                        status_message = statusMessage,
                    };

                    if (lookup.ActiveVisitId.HasValue && lookup.ActiveVisitId.Value != 0)
                    {
                        // Precise checkout: Visit ID is known (from cache)
                        try
                        {
                            var result = await Visits.UpdateAsync(checkoutData);
                            isSuccess = result != null && result.id != 0;
                        }
                        catch (VirtuagymApiException apiEx)
                        {
                            // Visit no longer exists online → API reports error.
                            // Search for active visit via API – if none found,
                            // the member is already checked out (success).
                            var fallbackVisits = await Visits.GetActiveMemberVisitAsync(lookup.MemberId);
                            if (fallbackVisits != null && fallbackVisits.Count > 0)
                            {
                                // Still an active visit → check out again
                                var retryResult = await Visits.UpdateAsync(checkoutData);
                                isSuccess = retryResult != null && retryResult.id != 0;
                            }
                            else
                            {
                                // No active visit → already checked out (cache was stale)
                                isSuccess = true;
                            }
                        }
                    }
                    else
                    {
                        // Fallback: Search for active visit via API and check out
                        var activeVisits = await Visits.GetActiveMemberVisitAsync(lookup.MemberId);
                        if (activeVisits != null && activeVisits.Count > 0)
                        {
                            var result = await Visits.UpdateAsync(checkoutData);
                            isSuccess = result != null && result.id != 0;
                        }
                        else
                        {
                            // No active visit → treat as success (already checked out)
                            isSuccess = true;
                        }
                    }

                    return new CheckinToggleResult
                    {
                        Success = isSuccess,
                        Action = ApiConstants.ActionCheckout,
                        Status = isSuccess ? ApiConstants.StatusOk : ApiConstants.StatusUnknown,
                        MemberName = lookup.MemberName,
                        MemberAvatar = lookup.MemberAvatar,
                        MemberId = lookup.MemberId,
                        Message = isSuccess
                            ? string.Format(msgCheckoutOk, lookup.MemberName)
                            : msgCheckoutFail,
                        ClientMessages = [],
                        EmployeeMessage = []
                    };
                }
            }
            catch (Exception ex)
            {
                string action = lookup.ActiveVisitExists ? ApiConstants.ActionCheckout : ApiConstants.ActionCheckin;
                string failMsg = lookup.ActiveVisitExists ? msgCheckoutFail : msgCheckinFail;

                return new CheckinToggleResult
                {
                    Success = false,
                    Action = action,
                    Status = ApiConstants.StatusUnknown,
                    MemberName = lookup.MemberName,
                    MemberAvatar = lookup.MemberAvatar,
                    MemberId = lookup.MemberId,
                    Message = failMsg + " (" + ex.Message + ")",
                    ClientMessages = [],
                    EmployeeMessage = []
                };
            }
        }


        /// <summary>
        /// Shared member lookup (cache or API) + double-scan protection.
        /// Returns a <see cref="MemberLookupResult"/>. If <see cref="MemberLookupResult.EarlyReturn"/>
        /// is set, the caller should return this result directly.
        /// </summary>
        private async Task<MemberLookupResult> ResolveMemberAndCheckDoubleScanAsync(
            string rfidTag,
            long doubleScanThresholdMs,
            ApiMessageOverrides messageOverrides,
            CachedMemberInfo cachedMember,
            string deviceId)
        {
            var m = messageOverrides ?? new ApiMessageOverrides();
            var lookup = new MemberLookupResult();

            if (cachedMember != null)
            {
                // Cache hit: Use member data and visit status from cache
                lookup.UserId = cachedMember.UserId;
                lookup.MemberId = cachedMember.MemberId;
                lookup.MemberName = ((cachedMember.Firstname ?? "") + " " + (cachedMember.Lastname ?? "")).Trim();
                lookup.MemberAvatar = cachedMember.Avatar;

                // Check visit status per device: A member can be checked in at multiple devices
                // simultaneously – for the toggle only the current device matters.
                var devData = cachedMember.GetDeviceData(deviceId);
                if (devData != null)
                {
                    lookup.ActiveVisitExists = devData.CheckInTimestamp > 0 && devData.CheckOutTimestamp == 0;
                    if (devData.VisitId.HasValue && devData.VisitId.Value != 0)
                        lookup.ActiveVisitId = devData.VisitId;
                }
                else
                {
                    lookup.ActiveVisitExists = false;
                }

                // Use device-specific timestamp: Member can check in at multiple devices
                // within the threshold (double-scan protection applies per device only).
                lookup.LastCheckInTimestamp = cachedMember.GetCheckInTimestampForDevice(deviceId);
            }
            else
            {
                // No cache: Member data is not pre-loaded.
                // The v0 API (PUT /devices) handles lookup + optional RFID tag assignment.
                // Member data is taken from the DeviceCheckinResult.
            }

            // Double-scan protection: Check if the last check-in is within the configured threshold
            if (doubleScanThresholdMs > 0
                && lookup.LastCheckInTimestamp > 0
                && (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - lookup.LastCheckInTimestamp) < doubleScanThresholdMs)
            {
                long minutes = doubleScanThresholdMs / 60_000;
                string thresholdDisplay = doubleScanThresholdMs >= 60_000
                    ? minutes + (minutes == 1 ? " minute" : " minutes")
                    : (doubleScanThresholdMs / 1_000) + " seconds";
                string msg = string.Format(
                    ApiMessageOverrides.Resolve(m.MsgDoubleScanBlocked, ApiConstants.MsgDoubleScanBlocked),
                    thresholdDisplay);

                lookup.EarlyReturn = new CheckinToggleResult
                {
                    Success = false,
                    Action = ApiConstants.ActionCheckin,
                    Status = ApiConstants.StatusUnknown,
                    MemberName = lookup.MemberName,
                    MemberAvatar = lookup.MemberAvatar,
                    UserId = lookup.UserId,
                    UserTimestampEdit = lookup.UserTimestampEdit,
                    MemberId = lookup.MemberId,
                    Message = msg,
                    ClientMessages = [msg],
                    EmployeeMessage = [msg]
                };
            }

            return lookup;
        }
        #endregion

        public void Dispose()
        {
            _httpClient?.Dispose();
        }
    }
}
