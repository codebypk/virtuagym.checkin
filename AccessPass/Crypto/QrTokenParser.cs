using AccessPass.Models;

namespace AccessPass.Crypto;

/// <summary>
/// Error codes for QR token validation.
/// </summary>
public enum QrTokenError
{
    None,
    NotEncryptedToken,
    CryptoUnavailable,
    DecryptFailed,
    EmptyPayload,
    Expired,
    MissingIdentifier
}

/// <summary>
/// Result of QR token validation.
/// </summary>
public sealed class QrTokenParseResult
{
    public bool Success => Error == QrTokenError.None;
    public QrTokenError Error { get; init; }
    public string? ErrorMessage { get; init; }
    public QrCodePayload? Payload { get; init; }
}

/// <summary>
/// Validates and decrypts encrypted QR tokens (VGQR1:...).
/// Encapsulates prefix check, decryption, payload validation and expiry check.
/// </summary>
public static class QrTokenParser
{
    /// <summary>
    /// Checks whether the value is an encrypted QR token with the default prefix.
    /// </summary>
    public static bool IsEncryptedToken(string value)
    {
        return !string.IsNullOrWhiteSpace(value)
            && value.StartsWith(QrTokenCryptoService.DefaultTokenPrefix, System.StringComparison.Ordinal);
    }

    /// <summary>
    /// Validates and decrypts a QR token completely.
    /// </summary>
    /// <param name="token">The QR code value.</param>
    /// <param name="secret">The secret for decryption.</param>
    public static QrTokenParseResult Parse(string token, string secret)
    {
        if (!IsEncryptedToken(token))
        {
            return new QrTokenParseResult
            {
                Error = QrTokenError.NotEncryptedToken,
                ErrorMessage = "Not an encrypted token."
            };
        }

        var crypto = QrTokenCryptoService.CreateDefault(secret, out string? cryptoError);
        if (crypto == null)
        {
            return new QrTokenParseResult
            {
                Error = QrTokenError.CryptoUnavailable,
                ErrorMessage = cryptoError
            };
        }

        bool decrypted = crypto.TryDecrypt<QrCodePayload>(token, out var payload, out var decryptError);
        if (!decrypted)
        {
            return new QrTokenParseResult
            {
                Error = QrTokenError.DecryptFailed,
                ErrorMessage = decryptError
            };
        }

        if (payload == null)
        {
            return new QrTokenParseResult
            {
                Error = QrTokenError.EmptyPayload,
                ErrorMessage = "Payload is empty."
            };
        }

        if (!payload.IsValid())
        {
            return new QrTokenParseResult
            {
                Error = QrTokenError.Expired,
                ErrorMessage = "Payload expired.",
                Payload = payload
            };
        }

        bool hasPassId = !string.IsNullOrWhiteSpace(payload.pass_id);
        bool hasCardId = !string.IsNullOrWhiteSpace(payload.card_id);
        bool hasMemberId = !string.IsNullOrWhiteSpace(payload.member_id);

        if (!hasPassId && !hasCardId && !hasMemberId)
        {
            return new QrTokenParseResult
            {
                Error = QrTokenError.MissingIdentifier,
                ErrorMessage = "No pass_id, card_id or member_id present.",
                Payload = payload
            };
        }

        return new QrTokenParseResult
        {
            Error = QrTokenError.None,
            Payload = payload
        };
    }
}
