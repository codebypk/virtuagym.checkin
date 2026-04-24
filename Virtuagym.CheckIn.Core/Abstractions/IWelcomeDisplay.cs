namespace Virtuagym.CheckIn.Core.Abstractions;

/// <summary>
/// Abstraction for the welcome/kiosk display.
/// Decouples check-in logic from the concrete UI implementation (WPF Window / Blazor page).
/// </summary>
public interface IWelcomeDisplay
{
    /// <summary>
    /// Shows a loading indicator while a check-in is being processed.
    /// </summary>
    void ShowLoader();

    /// <summary>
    /// Displays the check-in result (success, warning, reject, double-scan).
    /// </summary>
    /// <param name="status">Status code (ok, warn, reject, doublescan).</param>
    /// <param name="name">Member display name.</param>
    /// <param name="avatarUrl">Avatar URL or local path (may be null).</param>
    /// <param name="clientMessages">Messages to display.</param>
    /// <param name="readerName">Name of the input device.</param>
    void ShowCheckinResult(string status, string name, string? avatarUrl, string[] clientMessages, string? readerName = null);
}
