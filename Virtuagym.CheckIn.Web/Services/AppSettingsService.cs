using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using Virtuagym.CheckIn.Web.Models;

namespace Virtuagym.CheckIn.Web.Services;

/// <summary>
/// Service that persists <see cref="AppSettings"/> to <c>appsettings.json</c>.
/// ASP.NET Core's file-change watcher automatically reloads <c>IOptions&lt;AppSettings&gt;</c>
/// after the file is updated on disk.
/// </summary>
public sealed class AppSettingsService
{
    private readonly IWebHostEnvironment _env;
    private readonly IOptionsMonitor<AppSettings> _optionsMonitor;
    private readonly object _lock = new();

    public AppSettingsService(IWebHostEnvironment env, IOptionsMonitor<AppSettings> optionsMonitor)
    {
        _env = env;
        _optionsMonitor = optionsMonitor;
    }

    /// <summary>
    /// Returns the current (live) settings instance.
    /// </summary>
    public AppSettings Current => _optionsMonitor.CurrentValue;

    /// <summary>
    /// Persists the provided <paramref name="settings"/> to <c>appsettings.json</c>.
    /// </summary>
    public void Save(AppSettings settings)
    {
        lock (_lock)
        {
            string path = Path.Combine(_env.ContentRootPath, "appsettings.json");
            string json = File.ReadAllText(path);

            var root = JsonNode.Parse(json, documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip })
                       ?? new JsonObject();

            // Serialize the settings object to a JsonNode and replace the AppSettings section
            var serializerOptions = new JsonSerializerOptions { WriteIndented = true };
            var settingsNode = JsonSerializer.SerializeToNode(settings, serializerOptions);
            root["AppSettings"] = settingsNode;

            string output = root.ToJsonString(serializerOptions);
            File.WriteAllText(path, output);
        }
    }
}
