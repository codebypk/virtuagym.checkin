namespace AccessPass.Models;

/// <summary>
/// Result of an access pass check-in or check-out attempt.
/// </summary>
public sealed class AccessPassResult
{
    /// <summary>Whether the operation succeeded.</summary>
    public bool Success { get; init; }

    /// <summary>The action performed: "checkin" or "checkout".</summary>
    public string? Action { get; init; }

    /// <summary>Display name of the pass holder.</summary>
    public string? DisplayName { get; init; }

    /// <summary>Path to the avatar photo (if available).</summary>
    public string? AvatarPath { get; init; }

    /// <summary>Remaining uses after this operation.</summary>
    public int RemainingUses { get; init; }

    /// <summary>Total uses configured on the pass.</summary>
    public int TotalUses { get; init; }

    /// <summary>User-facing message(s).</summary>
    public string[] Messages { get; init; } = [];

    /// <summary>Error code if the operation failed.</summary>
    public AccessPassError Error { get; init; }
}

/// <summary>
/// Error codes for access pass operations.
/// </summary>
public enum AccessPassError
{
    None,
    NotFound,
    Inactive,
    Expired,
    NoRemainingUses,
    InvalidToken,
    AlreadyCheckedIn,
    NotCheckedIn
}
