using AccessPass.Crypto;
using AccessPass.Models;
using System;
using System.Collections.Generic;

namespace AccessPass.Services;

/// <summary>
/// Implements access pass logic: resolve scanned input, consume uses on check-in,
/// allow check-out without consuming, and CRUD operations.
/// </summary>
public class AccessPassService : IAccessPassService
{
    private readonly AccessPassStore _store;

    public AccessPassService(AccessPassStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
    }

    /// <inheritdoc />
    public AccessPassResult? TryResolve(string scannedInput, string secret)
    {
        if (string.IsNullOrWhiteSpace(scannedInput))
            return null;

        AccessPassEntry? entry = null;

        // 1) Try encrypted QR token
        if (QrTokenParser.IsEncryptedToken(scannedInput))
        {
            var parsed = QrTokenParser.Parse(scannedInput, secret);
            if (!parsed.Success)
            {
                return parsed.Error switch
                {
                    QrTokenError.Expired => Fail(AccessPassError.Expired, "Access pass QR code has expired."),
                    QrTokenError.DecryptFailed => Fail(AccessPassError.InvalidToken, "Could not decrypt QR code."),
                    _ => null // Not an access pass token
                };
            }

            var payload = parsed.Payload!;

            // Resolve by pass_id first, then card_id
            if (!string.IsNullOrWhiteSpace(payload.pass_id))
                entry = _store.GetById(payload.pass_id);

            if (entry == null && !string.IsNullOrWhiteSpace(payload.card_id))
                entry = _store.GetByCardId(payload.card_id);

            if (entry == null)
                return Fail(AccessPassError.NotFound, "Access pass not found.");
        }
        else
        {
            // 2) Try direct card ID lookup
            entry = _store.GetByCardId(scannedInput);
            if (entry == null)
                return null; // Not an access pass — let the regular flow handle it
        }

        return ProcessCheckinToggle(entry);
    }

    /// <inheritdoc />
    public List<AccessPassEntry> GetAll() => _store.GetAll();

    /// <inheritdoc />
    public AccessPassEntry Create(AccessPassEntry entry)
    {
        entry.CreatedAt = DateTime.UtcNow.ToString("o");
        if (entry.RemainingUses == 0 && entry.TotalUses > 0)
            entry.RemainingUses = entry.TotalUses;
        _store.Upsert(entry);
        return entry;
    }

    /// <inheritdoc />
    public bool Update(AccessPassEntry entry)
    {
        var existing = _store.GetById(entry.Id);
        if (existing == null) return false;
        entry.ModifiedAt = DateTime.UtcNow.ToString("o");
        _store.Upsert(entry);
        return true;
    }

    /// <inheritdoc />
    public bool Deactivate(string passId)
    {
        var entry = _store.GetById(passId);
        if (entry == null) return false;
        entry.IsActive = false;
        entry.ModifiedAt = DateTime.UtcNow.ToString("o");
        _store.Upsert(entry);
        return true;
    }

    /// <inheritdoc />
    public bool Delete(string passId) => _store.Remove(passId);

    /// <inheritdoc />
    public string? GetAvatarPath(string passId) => _store.GetAvatarPath(passId);

    /// <inheritdoc />
    public string SaveAvatar(string passId, byte[] imageBytes, string extension = ".jpg")
    {
        var fileName = _store.SaveAvatar(passId, imageBytes, extension);
        var entry = _store.GetById(passId);
        if (entry != null)
        {
            entry.PhotoFileName = fileName;
            _store.Upsert(entry);
        }
        return fileName;
    }

    private AccessPassResult ProcessCheckinToggle(AccessPassEntry entry)
    {
        if (!entry.IsActive)
            return Fail(AccessPassError.Inactive, "Access pass is deactivated.", entry);

        var avatarPath = _store.GetAvatarPath(entry.Id);

        // Check-out (no use consumed)
        if (entry.IsCheckedIn)
        {
            entry.LastCheckOut = DateTime.UtcNow.ToString("o");
            _store.Upsert(entry);

            return new AccessPassResult
            {
                Success = true,
                Action = "checkout",
                DisplayName = entry.DisplayName,
                AvatarPath = avatarPath,
                RemainingUses = entry.RemainingUses,
                TotalUses = entry.TotalUses,
                Messages = [$"Check-out: {entry.DisplayName}. {entry.RemainingUses}/{entry.TotalUses} uses remaining."]
            };
        }

        // Check-in (consumes one use)
        if (!entry.IsUnlimited && entry.RemainingUses <= 0)
            return Fail(AccessPassError.NoRemainingUses, "No remaining uses on this pass.", entry);

        if (!entry.IsValid)
            return Fail(AccessPassError.Expired, "Access pass has expired.", entry);

        if (!entry.IsUnlimited)
            entry.RemainingUses--;
        entry.LastCheckIn = DateTime.UtcNow.ToString("o");
        _store.Upsert(entry);

        return new AccessPassResult
        {
            Success = true,
            Action = "checkin",
            DisplayName = entry.DisplayName,
            AvatarPath = avatarPath,
            RemainingUses = entry.RemainingUses,
            TotalUses = entry.TotalUses,
            Messages = [$"Check-in: {entry.DisplayName}. {entry.RemainingUses}/{entry.TotalUses} uses remaining."]
        };
    }

    private static AccessPassResult Fail(AccessPassError error, string message, AccessPassEntry? entry = null)
    {
        return new AccessPassResult
        {
            Success = false,
            Error = error,
            DisplayName = entry?.DisplayName,
            RemainingUses = entry?.RemainingUses ?? 0,
            TotalUses = entry?.TotalUses ?? 0,
            Messages = [message]
        };
    }
}
