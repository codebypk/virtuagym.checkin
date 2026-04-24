using Virtuagym.CheckIn.Core.Helper;

namespace Virtuagym.CheckIn.Web.Services;

/// <summary>
/// Represents a single log entry displayed in the web UI.
/// </summary>
public sealed class LogEntry
{
    public string Text { get; init; } = "";
    public int Type { get; init; } = Constants.LogInfo;
    public DateTime Timestamp { get; init; } = DateTime.Now;

    /// <summary>
    /// CSS class name derived from the log type.
    /// </summary>
    public string CssClass => Type switch
    {
        Constants.LogWarning => "warning",
        Constants.LogError => "error",
        Constants.LogSuccess => "success",
        _ => "info"
    };
}

/// <summary>
/// Server-side log service that broadcasts log entries to connected Blazor clients.
/// Replaces the WPF ListView-based logging.
/// </summary>
public sealed class WebLogService : Virtuagym.CheckIn.Core.Abstractions.ILogWriter
{
    private readonly List<LogEntry> _entries = new();
    private readonly object _lock = new();

    /// <summary>
    /// Raised when a new log entry is added. Subscribers (Blazor pages) should call
    /// <c>InvokeAsync</c> + <c>StateHasChanged</c>.
    /// </summary>
    public event Action<LogEntry>? OnLogEntry;

    public void WriteToLog(string text, int type = Constants.LogInfo)
    {
        string prefix = type switch
        {
            Constants.LogWarning => "WARN",
            Constants.LogError => "ERROR",
            Constants.LogSuccess => "OK",
            _ => "INFO"
        };

        var entry = new LogEntry
        {
            Text = $"{DateTime.Now:dd.MM.yyyy HH:mm:ss} # {prefix}: {text}",
            Type = type,
            Timestamp = DateTime.Now
        };

        lock (_lock)
        {
            _entries.Add(entry);
            if (_entries.Count > 200)
                _entries.RemoveAt(0);
        }

        // File logging for errors
        if (type == Constants.LogError)
        {
            try
            {
                string logDir = Constants.LogFolder;
                Directory.CreateDirectory(logDir);
                string logFile = Path.Combine(logDir, DateTime.Now.ToString(Constants.LogDateFormat) + Constants.LogFileSuffix);
                File.AppendAllText(logFile, entry.Text + Environment.NewLine);
            }
            catch { /* Best effort */ }
        }

        OnLogEntry?.Invoke(entry);
    }

    public void WriteToRejectedLog(string cardId, string status, string reason)
    {
        try
        {
            string logDir = Constants.LogFolder;
            Directory.CreateDirectory(logDir);
            string filePath = Path.Combine(logDir, Constants.RejectedCheckinLogFile);
            string line = $"{DateTime.Now:dd.MM.yyyy HH:mm:ss} ; {cardId ?? ""} ; {status ?? ""} ; {reason ?? ""}";
            File.AppendAllText(filePath, line + Environment.NewLine);
        }
        catch { /* Best effort */ }
    }

    /// <summary>
    /// Returns the most recent log entries for initial page load.
    /// </summary>
    public IReadOnlyList<LogEntry> GetRecentEntries()
    {
        lock (_lock)
        {
            return _entries.ToList().AsReadOnly();
        }
    }
}
