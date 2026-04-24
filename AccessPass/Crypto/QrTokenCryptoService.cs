using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AccessPass.Crypto;

/// <summary>
/// AES-256-CBC + HMAC-SHA256 encryption/decryption service for tokens
/// (e.g. QR codes). TokenPrefix, Salt and Secret are injected via the constructor.
/// </summary>
public class QrTokenCryptoService
{
    /// <summary>Default prefix for encrypted QR tokens.</summary>
    public const string DefaultTokenPrefix = "VGQR1:";

    /// <summary>Default salt for key derivation (PBKDF2).</summary>
    public static readonly byte[] DefaultSalt = Encoding.UTF8.GetBytes("ePY16gkBnWyoNCtOl7HggvY0bhwWsKLFfaj6hdYRopE=");

    private readonly string _tokenPrefix;
    private readonly byte[] _salt;
    private readonly string _secret;

    /// <summary>
    /// Creates a new instance of the crypto service.
    /// </summary>
    /// <param name="tokenPrefix">Prefix prepended to encrypted tokens (e.g. "VGQR1:").</param>
    /// <param name="salt">Salt for key derivation (PBKDF2).</param>
    /// <param name="secret">Secret key for encryption and HMAC.</param>
    public QrTokenCryptoService(string tokenPrefix, byte[] salt, string secret)
    {
        ArgumentNullException.ThrowIfNull(tokenPrefix);
        ArgumentNullException.ThrowIfNull(salt);
        ArgumentNullException.ThrowIfNull(secret);

        _tokenPrefix = tokenPrefix;
        _salt = salt;
        _secret = secret;
    }

    /// <summary>
    /// Creates an instance with default prefix and salt.
    /// Returns null if no secret is provided.
    /// </summary>
    public static QrTokenCryptoService? CreateDefault(string secret, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(secret))
        {
            error = "No secret configured for QR encryption.";
            return null;
        }

        return new QrTokenCryptoService(DefaultTokenPrefix, DefaultSalt, secret);
    }

    /// <summary>
    /// Checks whether the value is an encrypted token with the configured prefix.
    /// </summary>
    public bool IsEncryptedToken(string value)
    {
        return !string.IsNullOrWhiteSpace(value)
            && value.StartsWith(_tokenPrefix, StringComparison.Ordinal);
    }

    /// <summary>
    /// Encrypts raw bytes and returns a token with prefix.
    /// Format: [Version(1)] [IV(16)] [Cipher(...)] [HMAC-SHA256(32)]
    /// </summary>
    public bool TryEncrypt(byte[] plainBytes, out string? token, out string? error)
    {
        token = null;
        error = null;

        try
        {
            if (plainBytes == null || plainBytes.Length == 0)
            {
                error = "Empty payload.";
                return false;
            }

            byte[] iv = new byte[16];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(iv);
            }

            byte[] keyMaterial = DeriveKeyMaterial();
            byte[] encryptionKey = new byte[32];
            byte[] hmacKey = new byte[32];
            Buffer.BlockCopy(keyMaterial, 0, encryptionKey, 0, 32);
            Buffer.BlockCopy(keyMaterial, 32, hmacKey, 0, 32);

            byte[] cipherBytes;
            using (var aes = Aes.Create())
            {
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;
                aes.KeySize = 256;
                aes.BlockSize = 128;
                aes.Key = encryptionKey;
                aes.IV = iv;

                using (var ms = new MemoryStream())
                using (var cs = new CryptoStream(ms, aes.CreateEncryptor(), CryptoStreamMode.Write))
                {
                    cs.Write(plainBytes, 0, plainBytes.Length);
                    cs.FlushFinalBlock();
                    cipherBytes = ms.ToArray();
                }
            }

            byte[] body = new byte[1 + iv.Length + cipherBytes.Length];
            body[0] = 1; // Version
            Buffer.BlockCopy(iv, 0, body, 1, iv.Length);
            Buffer.BlockCopy(cipherBytes, 0, body, 1 + iv.Length, cipherBytes.Length);

            byte[] signature;
            using (var hmac = new HMACSHA256(hmacKey))
            {
                signature = hmac.ComputeHash(body);
            }

            byte[] full = new byte[body.Length + signature.Length];
            Buffer.BlockCopy(body, 0, full, 0, body.Length);
            Buffer.BlockCopy(signature, 0, full, body.Length, signature.Length);

            token = _tokenPrefix + Convert.ToBase64String(full);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    /// <summary>
    /// Decrypts a token and returns the raw bytes.
    /// </summary>
    public bool TryDecrypt(string token, out byte[]? plainBytes, out string? error)
    {
        plainBytes = null;
        error = null;

        try
        {
            if (!IsEncryptedToken(token))
            {
                error = "Not an encrypted token.";
                return false;
            }

            string base64 = token.Substring(_tokenPrefix.Length);
            byte[] full = Convert.FromBase64String(base64);
            if (full.Length < (1 + 16 + 32))
            {
                error = "Token is too short.";
                return false;
            }

            int bodyLength = full.Length - 32;
            byte[] body = new byte[bodyLength];
            byte[] signature = new byte[32];
            Buffer.BlockCopy(full, 0, body, 0, bodyLength);
            Buffer.BlockCopy(full, bodyLength, signature, 0, signature.Length);

            byte[] keyMaterial = DeriveKeyMaterial();
            byte[] encryptionKey = new byte[32];
            byte[] hmacKey = new byte[32];
            Buffer.BlockCopy(keyMaterial, 0, encryptionKey, 0, 32);
            Buffer.BlockCopy(keyMaterial, 32, hmacKey, 0, 32);

            byte[] expectedSignature;
            using (var hmac = new HMACSHA256(hmacKey))
            {
                expectedSignature = hmac.ComputeHash(body);
            }

            if (!CryptographicOperations.FixedTimeEquals(expectedSignature, signature))
            {
                error = "Invalid signature.";
                return false;
            }

            byte version = body[0];
            if (version != 1)
            {
                error = "Unknown token version.";
                return false;
            }

            byte[] iv = new byte[16];
            Buffer.BlockCopy(body, 1, iv, 0, iv.Length);

            int cipherLength = bodyLength - 1 - iv.Length;
            if (cipherLength <= 0)
            {
                error = "Empty token content.";
                return false;
            }

            byte[] cipherBytes = new byte[cipherLength];
            Buffer.BlockCopy(body, 1 + iv.Length, cipherBytes, 0, cipherLength);

            using (var aes = Aes.Create())
            {
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;
                aes.KeySize = 256;
                aes.BlockSize = 128;
                aes.Key = encryptionKey;
                aes.IV = iv;

                using (var ms = new MemoryStream())
                using (var cs = new CryptoStream(ms, aes.CreateDecryptor(), CryptoStreamMode.Write))
                {
                    cs.Write(cipherBytes, 0, cipherBytes.Length);
                    cs.FlushFinalBlock();
                    plainBytes = ms.ToArray();
                }
            }

            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private byte[] DeriveKeyMaterial()
    {
        return Rfc2898DeriveBytes.Pbkdf2(_secret, _salt, 100000, HashAlgorithmName.SHA256, 64);
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Serializes an object as JSON, encrypts it and returns a token.
    /// </summary>
    public bool TryEncrypt<T>(T payload, out string? token, out string? error)
    {
        token = null;
        error = null;

        if (payload == null)
        {
            error = "Payload is empty.";
            return false;
        }

        byte[] plainBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload, JsonOptions));
        return TryEncrypt(plainBytes, out token, out error);
    }

    /// <summary>
    /// Decrypts a token and deserializes the JSON content.
    /// </summary>
    public bool TryDecrypt<T>(string token, out T? payload, out string? error)
    {
        payload = default;
        error = null;

        if (!TryDecrypt(token, out byte[]? plainBytes, out error))
            return false;

        string json = Encoding.UTF8.GetString(plainBytes!);
        payload = JsonSerializer.Deserialize<T>(json, JsonOptions);
        if (payload == null)
        {
            error = "Payload invalid.";
            return false;
        }

        return true;
    }
}
