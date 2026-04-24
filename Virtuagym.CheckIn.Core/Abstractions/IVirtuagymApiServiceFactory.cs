using Virtuagym.API.Services;

namespace Virtuagym.CheckIn.Core.Abstractions;

/// <summary>
/// Factory abstraction for creating Virtuagym API service instances.
/// Decouples check-in logic from the concrete settings-based factory
/// (WPF Settings.Default / Web IOptions).
/// </summary>
public interface IVirtuagymApiServiceFactory
{
    /// <summary>
    /// Creates a standard API service instance.
    /// </summary>
    VirtuagymApiService Create();

    /// <summary>
    /// Creates an API service instance for check-in requests with a device-specific club secret.
    /// </summary>
    VirtuagymApiService CreateWithClubSecret(string deviceClubSecret);
}
