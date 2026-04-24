using System;

namespace AccessPass.Models;

/// <summary>
/// Payload of an encrypted access pass QR code.
/// </summary>
public class QrCodePayload
{
    /// <summary>Card number / RFID tag (TagNumber_10 format).</summary>
    public string? card_id { get; set; }

    /// <summary>Member ID (optional, for Virtuagym-linked passes).</summary>
    public string? member_id { get; set; }

    /// <summary>Access pass ID (used to resolve against local store).</summary>
    public string? pass_id { get; set; }

    /// <summary>
    /// Valid-until date (ISO 8601). Empty = no expiration.
    /// </summary>
    public string? valid_until { get; set; }

    /// <summary>
    /// Indicates whether the QR code is still valid.
    /// If <see cref="valid_until"/> is empty, the code is always considered valid.
    /// </summary>
    public bool IsValid()
    {
        if (string.IsNullOrWhiteSpace(valid_until))
            return true;

        if (DateTime.TryParse(valid_until, null,
            System.Globalization.DateTimeStyles.RoundtripKind, out DateTime expiry))
        {
            return DateTime.UtcNow <= expiry;
        }

        return false;
    }
}
