using System;

namespace Virtuagym.API.Qr
{
    /// <summary>
    /// Detection and validation of Virtuagym-native QR codes (vg_checkin_qr=...).
    /// </summary>
    public static class VirtuagymNativeQr
    {
        /// <summary>Prefix for Virtuagym-native QR codes.</summary>
        public const string Prefix = "vg_checkin_qr=";

        /// <summary>
        /// Checks if the value is a Virtuagym-native QR code.
        /// </summary>
        public static bool IsMatch(string value)
        {
            return !string.IsNullOrWhiteSpace(value)
                && value.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Extracts the card ID from a Virtuagym-native QR code.
        /// Returns false if the value is not a valid Virtuagym QR or the card ID is empty.
        /// </summary>
        public static bool TryExtractCardId(string value, out string cardId)
        {
            cardId = null;

            if (!IsMatch(value))
                return false;

            cardId = value.Substring(Prefix.Length).Trim();
            return !string.IsNullOrWhiteSpace(cardId);
        }
    }
}
