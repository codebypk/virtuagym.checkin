using System;
using System.Text.Json.Serialization;

namespace AccessPass.Models;

/// <summary>
/// Represents a temporary access pass with a limited number of uses.
/// </summary>
public class AccessPassEntry
{
    /// <summary>Unique pass identifier.</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..12];

    /// <summary>Optional display name of the pass holder.</summary>
    public string? Name { get; set; }

    /// <summary>Card ID or QR token identifier used for scanning.</summary>
    public string? CardId { get; set; }

    /// <summary>Avatar / photo file name stored in the avatars folder.</summary>
    public string? PhotoFileName { get; set; }

    /// <summary>Total number of uses allowed.</summary>
    public int TotalUses { get; set; }

    /// <summary>Remaining number of uses.</summary>
    public int RemainingUses { get; set; }

    /// <summary>Pass valid from (UTC, ISO 8601). Null = immediately valid.</summary>
    public string? ValidFrom { get; set; }

    /// <summary>Pass valid until (UTC, ISO 8601). Null = no expiration.</summary>
    public string? ValidUntil { get; set; }

    /// <summary>Whether the pass is active (can be manually deactivated).</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>Timestamp of the last check-in (UTC, ISO 8601).</summary>
    public string? LastCheckIn { get; set; }

    /// <summary>Timestamp of the last check-out (UTC, ISO 8601).</summary>
    public string? LastCheckOut { get; set; }

    /// <summary>Timestamp when the pass was created (UTC, ISO 8601).</summary>
    public string? CreatedAt { get; set; }

    /// <summary>
    /// Returns true if the pass is currently valid (active, not expired, has remaining uses).
    /// </summary>
    [JsonIgnore]
    public bool IsValid
    {
        get
        {
            if (!IsActive) return false;
            if (RemainingUses <= 0) return false;

            var now = DateTime.UtcNow;

            if (!string.IsNullOrWhiteSpace(ValidFrom) &&
                DateTime.TryParse(ValidFrom, null, System.Globalization.DateTimeStyles.RoundtripKind, out var from) &&
                now < from)
                return false;

            if (!string.IsNullOrWhiteSpace(ValidUntil) &&
                DateTime.TryParse(ValidUntil, null, System.Globalization.DateTimeStyles.RoundtripKind, out var until) &&
                now > until)
                return false;

            return true;
        }
    }

    /// <summary>
    /// Returns true if the pass holder is currently checked in (check-in without matching check-out).
    /// </summary>
    [JsonIgnore]
    public bool IsCheckedIn
    {
        get
        {
            if (string.IsNullOrWhiteSpace(LastCheckIn)) return false;
            if (string.IsNullOrWhiteSpace(LastCheckOut)) return true;

            if (DateTime.TryParse(LastCheckIn, null, System.Globalization.DateTimeStyles.RoundtripKind, out var cin) &&
                DateTime.TryParse(LastCheckOut, null, System.Globalization.DateTimeStyles.RoundtripKind, out var cout))
            {
                return cin > cout;
            }

            return false;
        }
    }

    /// <summary>Display name or fallback.</summary>
    [JsonIgnore]
    public string DisplayName => !string.IsNullOrWhiteSpace(Name) ? Name : $"Pass {Id}";
}
