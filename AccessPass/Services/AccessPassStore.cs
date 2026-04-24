using AccessPass.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace AccessPass.Services;

/// <summary>
/// JSON file-based persistence for access passes.
/// Stores data in <c>Resources/access_pass/access_passes.json</c> relative to the base directory.
/// Avatars are stored in <c>Resources/access_pass/avatars/</c>.
/// Uses atomic write (temp file + rename) to prevent corruption.
/// </summary>
public class AccessPassStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _filePath;
    private readonly string _avatarDirectory;
    private readonly object _lock = new();
    private List<AccessPassEntry> _entries;

    /// <summary>
    /// Creates a new store instance.
    /// </summary>
    /// <param name="baseDirectory">
    /// Base directory for storage. Defaults to <see cref="AppDomain.CurrentDomain.BaseDirectory"/>.
    /// Files are stored under <c>{baseDirectory}/Resources/access_pass/</c>.
    /// </param>
    public AccessPassStore(string? baseDirectory = null)
    {
        var basePath = baseDirectory ?? AppDomain.CurrentDomain.BaseDirectory;
        var dir = Path.Combine(basePath, "Resources", "access_pass");
        Directory.CreateDirectory(dir);

        _filePath = Path.Combine(dir, "access_passes.json");
        _avatarDirectory = Path.Combine(dir, "avatars");
        Directory.CreateDirectory(_avatarDirectory);

        _entries = LoadFromDisk();
    }

    /// <summary>Full path to the avatars directory.</summary>
    public string AvatarDirectory => _avatarDirectory;

    /// <summary>Returns all stored passes.</summary>
    public List<AccessPassEntry> GetAll()
    {
        lock (_lock)
            return new List<AccessPassEntry>(_entries);
    }

    /// <summary>Finds a pass by its ID.</summary>
    public AccessPassEntry? GetById(string passId)
    {
        lock (_lock)
            return _entries.Find(e => string.Equals(e.Id, passId, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Finds a pass by card ID.</summary>
    public AccessPassEntry? GetByCardId(string cardId)
    {
        lock (_lock)
            return _entries.Find(e =>
                !string.IsNullOrWhiteSpace(e.CardId) &&
                string.Equals(e.CardId, cardId, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Adds or updates a pass entry and persists to disk.</summary>
    public void Upsert(AccessPassEntry entry)
    {
        lock (_lock)
        {
            var idx = _entries.FindIndex(e => string.Equals(e.Id, entry.Id, StringComparison.OrdinalIgnoreCase));
            if (idx >= 0)
                _entries[idx] = entry;
            else
                _entries.Add(entry);

            SaveToDisk();
        }
    }

    /// <summary>Removes a pass by ID and deletes its avatar.</summary>
    public bool Remove(string passId)
    {
        lock (_lock)
        {
            var idx = _entries.FindIndex(e => string.Equals(e.Id, passId, StringComparison.OrdinalIgnoreCase));
            if (idx < 0) return false;

            var entry = _entries[idx];
            _entries.RemoveAt(idx);
            SaveToDisk();

            DeleteAvatarFile(entry.PhotoFileName);
            return true;
        }
    }

    /// <summary>Saves avatar bytes and returns the file name.</summary>
    public string SaveAvatar(string passId, byte[] imageBytes, string extension = ".jpg")
    {
        var fileName = $"{passId}{extension}";
        var path = Path.Combine(_avatarDirectory, fileName);
        File.WriteAllBytes(path, imageBytes);
        return fileName;
    }

    /// <summary>Returns the full avatar path for a pass, or null if not found.</summary>
    public string? GetAvatarPath(string passId)
    {
        var entry = GetById(passId);
        if (entry == null || string.IsNullOrWhiteSpace(entry.PhotoFileName))
            return null;

        var path = Path.Combine(_avatarDirectory, entry.PhotoFileName);
        return File.Exists(path) ? path : null;
    }

    private List<AccessPassEntry> LoadFromDisk()
    {
        if (!File.Exists(_filePath))
            return [];

        try
        {
            var json = File.ReadAllText(_filePath);
            return JsonSerializer.Deserialize<List<AccessPassEntry>>(json, JsonOptions) ?? [];
        }
        catch
        {
            return [];
        }
    }

    private void SaveToDisk()
    {
        var json = JsonSerializer.Serialize(_entries, JsonOptions);
        var tempPath = _filePath + ".tmp";
        File.WriteAllText(tempPath, json);
        File.Move(tempPath, _filePath, overwrite: true);
    }

    private void DeleteAvatarFile(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)) return;
        var path = Path.Combine(_avatarDirectory, fileName);
        if (File.Exists(path))
            File.Delete(path);
    }
}
