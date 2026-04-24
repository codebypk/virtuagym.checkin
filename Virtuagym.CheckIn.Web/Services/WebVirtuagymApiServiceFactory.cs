using Microsoft.Extensions.Options;
using Virtuagym.CheckIn.Core.Abstractions;
using Virtuagym.CheckIn.Web.Models;
using ApiService = Virtuagym.API.Services.VirtuagymApiService;

namespace Virtuagym.CheckIn.Web.Services;

/// <summary>
/// Factory for creating Virtuagym API service instances based on <see cref="AppSettings"/>.
/// Web equivalent of the WPF <c>VirtuagymApiServiceFactory</c>.
/// </summary>
public sealed class WebVirtuagymApiServiceFactory(IOptions<AppSettings> options) : IVirtuagymApiServiceFactory
{
    private AppSettings Settings => options.Value;

    /// <summary>
    /// Creates the service based on the stored settings.
    /// </summary>
    public ApiService Create()
    {
        string apiBaseUrl = (Settings.VirtuagymServerUrl ?? "").TrimEnd('/');
        return new ApiService(
            Settings.VirtuagymApiKey,
            apiBaseUrl,
            Settings.VirtuagymClubSecret,
            checkinApiKey: Settings.MemberCheckinApiV0Key,
            v0Username: Settings.MemberCheckinApiV0Username,
            v0Password: Settings.MemberCheckinApiV0Password);
    }

    /// <summary>
    /// Creates the service for check-in requests with a device-specific club secret.
    /// </summary>
    public ApiService CreateWithClubSecret(string deviceClubSecret)
    {
        string apiBaseUrl = (Settings.VirtuagymServerUrl ?? "").TrimEnd('/');
        return new ApiService(
            Settings.VirtuagymApiKey,
            apiBaseUrl,
            Settings.VirtuagymClubSecret,
            checkinApiKey: Settings.MemberCheckinApiV0Key,
            checkinClubSecret: deviceClubSecret,
            v0Username: Settings.MemberCheckinApiV0Username,
            v0Password: Settings.MemberCheckinApiV0Password);
    }

    /// <summary>
    /// Creates the service with explicit parameters (for connection testing).
    /// </summary>
    public static ApiService Create(string apiKey, string serverUrl, string clubSecret)
    {
        string apiBaseUrl = serverUrl.TrimEnd('/');
        return new ApiService(apiKey, apiBaseUrl, clubSecret);
    }
}
