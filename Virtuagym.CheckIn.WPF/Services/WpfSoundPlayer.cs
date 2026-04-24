using System;
using System.IO;
using System.Media;
using Virtuagym.CheckIn.Core.Abstractions;
using Virtuagym.CheckIn.Core.Helper;

namespace Virtuagym.CheckIn.WPF.Services;

/// <summary>
/// WPF implementation of <see cref="ISoundPlayer"/> using <see cref="SoundPlayer"/>.
/// </summary>
public class WpfSoundPlayer(ILogWriter? logger = null) : ISoundPlayer
{
    public void Play(string soundPath)
    {
        try
        {
            string fullPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, soundPath);
            if (File.Exists(fullPath))
            {
                using var player = new SoundPlayer(fullPath);
                player.Play();
            }
        }
        catch (Exception ex)
        {
            logger?.WriteToLog($"Sound error: {ex.Message}", Constants.LogWarning);
        }
    }
}
