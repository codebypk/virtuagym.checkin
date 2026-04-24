using System;
using System.Globalization;
using System.Linq;

namespace Hardware.Models
{
    public class Card
    {
        /// <summary>Full NFC UID as hex string (e.g. "5D00926570").</summary>
        public string UidHex { get; }

        /// <summary>Decimal value of the full UID as string (e.g. "6160613" for 4-byte, "1297036692382455" for 7-byte UIDs).</summary>
        public string UidDecimal { get; }

        /// <summary>Legacy format (max 4 bytes): second-to-last byte pair as decimal + last byte pair as 5-digit. Lossy for UIDs > 4 bytes.</summary>
        [Obsolete("Lossy for UIDs > 4 bytes. Use UidDecimal instead.")]
        public string UidLegacy { get; }

        /// <summary>True if the UID is valid (not null and not InvalidTagHex).</summary>
        public bool IsValidTag { get; }

        /// <summary>
        /// True if the value is a raw passthrough string (e.g. Virtuagym QR code).
        /// In this case the v0 API must always be used.
        /// </summary>
        public bool IsRawValue { get; }

        /// <summary>
        /// Internal default value for Lower3Bytes card number.
        /// Externally accessible only via <see cref="GetCardId(CardIdMode)"/>.
        /// </summary>
        private string CardId { get; }

        /// <summary>
        /// Returns the card number in the specified format.
        /// When <see cref="IsRawValue"/> is set, the raw value is always returned regardless of mode.
        /// </summary>
        public string GetCardId(CardIdMode mode)
        {
            if (IsRawValue)
                return UidDecimal;

            return mode switch
            {
                CardIdMode.FullDecimal => UidDecimal,
                CardIdMode.FullHex => UidHex,
                CardIdMode.Lower4Bytes => CalculateCardId4BytesFromHex(UidHex),
                _ => CardId
            };
        }

        /// <summary>
        /// Creates a Card instance with a raw passthrough value.
        /// The value is used directly as UidDecimal without conversion.
        /// Hex and legacy formats are not calculated.
        /// Suitable for Virtuagym-native QR codes whose value is sent directly as card_id to the API.
        /// </summary>
        public static Card FromRawValue(string rawValue)
        {
            return new Card(rawValue, isRaw: true);
        }

        private Card(string rawValue, bool isRaw)
        {
            UidHex = string.Empty;
            UidDecimal = rawValue ?? string.Empty;
            IsRawValue = true;
            UidLegacy = string.Empty;
            CardId = rawValue ?? string.Empty;
            IsValidTag = !string.IsNullOrWhiteSpace(rawValue);
        }

        /// <summary>
        /// Creates a Card instance from a decimal string.
        /// Automatically detects UidDecimal (10-digit) or UidLegacy (legacy format).
        /// </summary>
        public Card(string tagNumberString)
        {
            if (string.IsNullOrEmpty(tagNumberString))
            {
                UidHex = string.Empty;
                UidDecimal = "0000000000";
                UidLegacy = string.Empty;
                CardId = "0000000000";
                IsValidTag = false;
                return;
            }

            // Check if the format is hex (only when A-F letters are present)
            bool isHexFormat = tagNumberString.Any(c => (c >= 'A' && c <= 'F') || (c >= 'a' && c <= 'f'));

            if (isHexFormat)
            {
                tagNumberString = tagNumberString.Replace("-", "").Replace(" ", "").ToUpperInvariant();

                if (!ulong.TryParse(tagNumberString, NumberStyles.HexNumber, null, out _))
                {
                    UidHex = string.Empty;
                    UidDecimal = "0000000000";
                    UidLegacy = string.Empty;
                    CardId = "0000000000";
                    IsValidTag = false;
                    return;
                }

                UidHex = tagNumberString;
                UidDecimal = CalculateUidDecimalFromHex(tagNumberString);
                CardId = CalculateCardIdFromHex(tagNumberString);

                if (tagNumberString.Length >= 8)
                {
                    UidLegacy = CalculateUidLegacyFromHex(tagNumberString);
                }
                else
                {
                    uint dec10Value = uint.Parse(tagNumberString.Length >= 6 ? tagNumberString.Substring(tagNumberString.Length - 6, 6) : tagNumberString.PadLeft(6, '0'), 
                        NumberStyles.HexNumber);
                    UidLegacy = dec10Value.ToString();
                }

                IsValidTag = ulong.Parse(tagNumberString, NumberStyles.HexNumber) > 0;
            }
            else
            {
                if (!ulong.TryParse(tagNumberString, out ulong decimalValue))
                {
                    UidHex = string.Empty;
                    UidDecimal = tagNumberString;
                    UidLegacy = string.Empty;
                    CardId = tagNumberString;
                    IsValidTag = false;
                    return;
                }

                bool isTagNumber10 = tagNumberString.Length == 10 
                    || (tagNumberString.TrimStart('0').Length < 8 && decimalValue <= 0xFFFFFF);

                if (isTagNumber10 || tagNumberString.Length == 10)
                {
                    string hexValue = decimalValue.ToString("X").PadLeft(6, '0').ToUpperInvariant();
                    string hexPadded = hexValue.PadLeft(8, '0');

                    UidHex = hexValue;
                    UidDecimal = tagNumberString;
                    CardId = tagNumberString;
                    UidLegacy = CalculateUidLegacyFromHex(hexPadded);
                    IsValidTag = tagNumberString.Length > 0;
                }
                else
                {
                    if (tagNumberString.Length < 6)
                    {
                        UidHex = string.Empty;
                        UidDecimal = "0000000000";
                        UidLegacy = string.Empty;
                        CardId = "0000000000";
                        IsValidTag = false;
                        return;
                    }

                    string lowerStr = tagNumberString.Substring(tagNumberString.Length - 5, 5);
                    string upperStr = tagNumberString.Substring(0, tagNumberString.Length - 5);

                    if (!uint.TryParse(upperStr, out uint upperValue) || !uint.TryParse(lowerStr, out uint lowerValue))
                    {
                        UidHex = string.Empty;
                        UidDecimal = "0000000000";
                        UidLegacy = string.Empty;
                        CardId = "0000000000";
                        IsValidTag = false;
                        return;
                    }

                    string upperHex = upperValue.ToString("X4");
                    string lowerHex = lowerValue.ToString("X4");
                    string hexLower4Bytes = (upperHex + lowerHex).ToUpperInvariant();

                    UidHex = hexLower4Bytes;
                    UidDecimal = CalculateUidDecimalFromHex(hexLower4Bytes);
                    CardId = CalculateCardIdFromHex(hexLower4Bytes);
                    UidLegacy = tagNumberString;
                    IsValidTag = ulong.Parse(hexLower4Bytes, NumberStyles.HexNumber) > 0;
                }
            }
        }

        /// <summary>
        /// Parses the HID report buffer of an NFC/RFID reader.
        /// </summary>
        public Card(byte[] cardData)
        {
            if (cardData == null || cardData.Length < 7)
                throw new ArgumentException(
                    $"cardData too short ({cardData?.Length ?? 0} bytes) – at least 7 bytes expected.",
                    nameof(cardData));

            int uidLength = cardData[5];

            if (uidLength <= 0 || cardData.Length < 6 + uidLength)
            {
                UidHex = string.Empty;
                UidDecimal = "0000000000";
                UidLegacy = string.Empty;
                CardId = "0000000000";
                IsValidTag = false;
                return;
            }

            byte[] uidBytes = new byte[uidLength];
            Array.Copy(cardData, 6, uidBytes, 0, uidLength);
            string hexUid = BitConverter.ToString(uidBytes).Replace("-", "").ToUpperInvariant();

            UidHex = hexUid;
            UidDecimal = CalculateUidDecimalFromHex(hexUid);
            CardId = CalculateCardIdFromHex(hexUid);

            if (hexUid.Length >= 8)
            {
                UidLegacy = CalculateUidLegacyFromHex(hexUid);
            }
            else
            {
                uint dec10Value = uint.Parse(hexUid.Length >= 6 ? hexUid.Substring(hexUid.Length - 6, 6) : hexUid.PadLeft(6, '0'), 
                    NumberStyles.HexNumber);
                UidLegacy = dec10Value.ToString();
            }

            IsValidTag = ulong.Parse(hexUid, NumberStyles.HexNumber) > 0;
        }

        private static string CalculateUidDecimalFromHex(string hexUid)
        {
            ulong dec10Value = ulong.Parse(hexUid, NumberStyles.HexNumber);
            return dec10Value.ToString("D10");
        }

        /// <summary>
        /// Calculates the card number from the lower 3 bytes of the hex UID (10-digit, decimal).
        /// Corresponds to the output format of keyboard-wedge readers like the JA-190T.
        /// </summary>
        private static string CalculateCardIdFromHex(string hexUid)
        {
            string hexLower3 = hexUid.Length >= 6
                ? hexUid.Substring(hexUid.Length - 6, 6)
                : hexUid.PadLeft(6, '0');
            uint cardIdValue = uint.Parse(hexLower3, NumberStyles.HexNumber);
            return cardIdValue.ToString("D10");
        }

        /// <summary>
        /// Calculates the card number from the lower 4 bytes of the hex UID (10-digit, decimal).
        /// Compatible with MIFARE Classic readers that output the full 4-byte NUID.
        /// </summary>
        private static string CalculateCardId4BytesFromHex(string hexUid)
        {
            string hexLower4 = hexUid.Length >= 8
                ? hexUid.Substring(hexUid.Length - 8, 8)
                : hexUid.PadLeft(8, '0');
            uint cardIdValue = uint.Parse(hexLower4, NumberStyles.HexNumber);
            return cardIdValue.ToString("D10");
        }

        private static string CalculateUidLegacyFromHex(string hexUid)
        {
            int hexLen = hexUid.Length;
            string upper = hexUid.Substring(hexLen - 8, 4);
            string lower = hexUid.Substring(hexLen - 4, 4);
            string upperDec = uint.Parse(upper, NumberStyles.HexNumber).ToString();
            string lowerDec = uint.Parse(lower, NumberStyles.HexNumber).ToString("D5");
            return upperDec + lowerDec;
        }
    }
}
