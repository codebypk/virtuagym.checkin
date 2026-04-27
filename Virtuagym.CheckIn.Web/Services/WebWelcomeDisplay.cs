using Virtuagym.CheckIn.Core.Abstractions;

namespace Virtuagym.CheckIn.Web.Services;

/// <summary>
/// Web implementation of <see cref="IWelcomeDisplay"/> that bridges check-in results
/// to <see cref="WebCheckinService"/> events so the Welcome page can display them.
/// </summary>
public sealed class WebWelcomeDisplay(WebCheckinService checkinService) : IWelcomeDisplay
{
    /// <summary>The sound path collected by the last <see cref="ShowCheckinResult"/> call.</summary>
    public string? LastSoundPath { get; set; }

    public void ShowLoader(string? readerName = null) => checkinService.RaiseShowLoader(readerName);

    public void ShowCheckinResult(string status, string name, string? avatarUrl, string[] clientMessages, string? readerName = null)
    {
        checkinService.RaiseCheckinResult(new CheckinResultInfo
        {
            MemberName = name,
            StatusLines = clientMessages,
            Status = status,
            AvatarUrl = avatarUrl,
            ReaderName = readerName,
            SoundPath = LastSoundPath
        });
        LastSoundPath = null;
    }
}
