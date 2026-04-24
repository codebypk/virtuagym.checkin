namespace Virtuagym.CheckIn.Core.Abstractions;

/// <summary>
/// Abstraction for playing sound effects.
/// Decouples check-in logic from the concrete audio implementation
/// (WPF SoundPlayer / Web JS Interop).
/// </summary>
public interface ISoundPlayer
{
    /// <summary>
    /// Plays a sound file identified by a relative path or URL.
    /// </summary>
    /// <param name="soundPath">Relative path to the sound file.</param>
    void Play(string soundPath);
}
