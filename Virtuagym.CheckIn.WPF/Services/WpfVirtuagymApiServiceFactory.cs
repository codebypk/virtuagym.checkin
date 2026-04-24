using Virtuagym.CheckIn.Core.Abstractions;
using ApiService = Virtuagym.API.Services.VirtuagymApiService;

namespace Virtuagym.CheckIn.WPF.Services;

/// <summary>
/// WPF implementation of <see cref="IVirtuagymApiServiceFactory"/>
/// that delegates to the static <see cref="VirtuagymApiServiceFactory"/>.
/// </summary>
public class WpfVirtuagymApiServiceFactory : IVirtuagymApiServiceFactory
{
    public ApiService Create() => VirtuagymApiServiceFactory.Create();
    public ApiService CreateWithClubSecret(string deviceClubSecret) => VirtuagymApiServiceFactory.CreateWithClubSecret(deviceClubSecret);
}
