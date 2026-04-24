using Virtuagym.CheckIn.Core.Abstractions;

namespace Virtuagym.CheckIn.Web.Services;

/// <summary>
/// Server-side <see cref="ISoundPlayer"/> that captures the sound path
/// so it can be forwarded to the browser through <see cref="WebWelcomeDisplay"/>.
/// Since the hosted service has no JS runtime, the sound path is stored
/// and later included in <see cref="CheckinResultInfo.SoundPath"/>.
/// </summary>
public sealed class ServerSoundPlayer : ISoundPlayer
{
    private readonly WebWelcomeDisplay _display;

    public ServerSoundPlayer(WebWelcomeDisplay display) => _display = display;

    public void Play(string soundPath) => _display.LastSoundPath = soundPath;
}
