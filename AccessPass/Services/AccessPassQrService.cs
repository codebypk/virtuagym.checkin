using System;
using AccessPass.Crypto;
using AccessPass.Models;
using QRCoder;

namespace AccessPass.Services;

/// <summary>
/// Generates encrypted QR code tokens and QR code images (Base64 PNG) for access passes.
/// </summary>
public class AccessPassQrService
{
    /// <summary>
    /// Creates an encrypted QR token for the given access pass entry.
    /// </summary>
    /// <param name="entry">The access pass entry.</param>
    /// <param name="secret">Encryption secret (club secret).</param>
    /// <param name="validHours">How many hours the QR token stays valid. 0 = no expiration.</param>
    /// <returns>The encrypted token string, or null if encryption failed.</returns>
    public string? GenerateToken(AccessPassEntry entry, string secret, int validHours = 720)
    {
        if (entry == null || string.IsNullOrWhiteSpace(secret))
            return null;

        var crypto = QrTokenCryptoService.CreateDefault(secret, out _);
        if (crypto == null)
            return null;

        var payload = new QrCodePayload
        {
            pass_id = entry.Id,
            card_id = entry.CardId,
            valid_until = validHours > 0
                ? DateTime.UtcNow.AddHours(validHours).ToString("o")
                : ""
        };

        return crypto.TryEncrypt(payload, out var token, out _) ? token : null;
    }

    /// <summary>
    /// Generates a QR code as a Base64-encoded PNG data URI for the given token string.
    /// </summary>
    /// <param name="token">The encrypted token (or any string).</param>
    /// <param name="pixelsPerModule">Size of each QR module in pixels.</param>
    /// <returns>A data URI like "data:image/png;base64,..."</returns>
    public string? GenerateQrCodeDataUri(string token, int pixelsPerModule = 10)
    {
        if (string.IsNullOrWhiteSpace(token))
            return null;

        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(token, QRCodeGenerator.ECCLevel.M);
        using var pngCode = new PngByteQRCode(data);
        var pngBytes = pngCode.GetGraphic(pixelsPerModule);
        return $"data:image/png;base64,{Convert.ToBase64String(pngBytes)}";
    }

    /// <summary>
    /// Convenience: generates the encrypted token AND the QR image in one call.
    /// </summary>
    public (string? Token, string? QrDataUri) GenerateTokenAndQr(AccessPassEntry entry, string secret, int validHours = 720, int pixelsPerModule = 10)
    {
        var token = GenerateToken(entry, secret, validHours);
        if (token == null)
            return (null, null);

        var dataUri = GenerateQrCodeDataUri(token, pixelsPerModule);
        return (token, dataUri);
    }
}
