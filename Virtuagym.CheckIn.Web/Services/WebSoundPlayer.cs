using Microsoft.JSInterop;
using Virtuagym.CheckIn.Core.Abstractions;

namespace Virtuagym.CheckIn.Web.Services;

/// <summary>
/// Browser-based implementation of <see cref="ISoundPlayer"/> using JS Interop.
/// Calls <c>window.playSound(url)</c> defined in <c>wwwroot/js/sound.js</c>.
/// Sound files must be placed in <c>wwwroot/sounds/</c> and referenced by relative path
/// (e.g. <c>sounds/login_succes.wav</c>).
/// </summary>
/// <remarks>
/// Because <see cref="ISoundPlayer.Play"/> is synchronous but JS Interop is async,
/// the call is fire-and-forget. This is safe for audio playback.
/// <para>
/// This service must be registered as <b>scoped</b> (per-circuit) because
/// <see cref="IJSRuntime"/> is circuit-scoped in Blazor Server.
/// </para>
/// </remarks>
public sealed class WebSoundPlayer(IJSRuntime js) : ISoundPlayer
{
    public void Play(string soundPath)
    {
        if (string.IsNullOrWhiteSpace(soundPath))
            return;

        // Fire-and-forget: audio playback is non-critical
        _ = PlayAsync(soundPath);
    }

    private async Task PlayAsync(string soundPath)
    {
        try
        {
            await js.InvokeVoidAsync("playSound", soundPath);
        }
        catch
        {
            // Suppress: circuit may be disconnected or prerendering
        }
    }
}
