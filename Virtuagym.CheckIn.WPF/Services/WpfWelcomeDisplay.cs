using Virtuagym.CheckIn.Core.Abstractions;

namespace Virtuagym.CheckIn.WPF.Services;

/// <summary>
/// WPF implementation of <see cref="IWelcomeDisplay"/> that delegates to the <see cref="WelcomeWindow"/>.
/// </summary>
public class WpfWelcomeDisplay(WelcomeWindow window) : IWelcomeDisplay
{
    public void ShowLoader(string? readerName = null) => window.ShowLoader();

    public void ShowCheckinResult(string status, string name, string? avatarUrl, string[] clientMessages, string? readerName = null)
        => window.ShowCheckinResult(status, name, avatarUrl, clientMessages, readerName);

    public void ShowHardwareError(string message, string? readerName = null)
        => window.ShowHardwareError(message, readerName);
}
