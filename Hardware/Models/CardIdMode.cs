namespace Hardware.Models
{
    /// <summary>
    /// Bestimmt, wie die Kartennummer (CardId) aus der UID berechnet wird.
    /// Verschiedene Reader-Hardware gibt die UID in unterschiedlichen Formaten aus.
    /// </summary>
    public enum CardIdMode
    {
        /// <summary>
        /// Untere 3 Bytes der UID als 10-stelliger Dezimalwert.
        /// Kompatibel mit Keyboard-Wedge-Readern wie JA-190T.
        /// Beispiel: UID 5D:00:92:65:70 → "0009594224".
        /// </summary>
        Lower3Bytes = 0,

        /// <summary>
        /// Vollständige UID als Dezimalwert (mindestens 10-stellig).
        /// Beispiel: UID 5D:00:92:65:70 → "0399441552752".
        /// </summary>
        FullDecimal = 1,

        /// <summary>
        /// Vollständige UID als Hex-String (Großbuchstaben, ohne Trennzeichen).
        /// Beispiel: UID 5D:00:92:65:70 → "5D00926570".
        /// </summary>
        FullHex = 2,

        /// <summary>
        /// Untere 4 Bytes der UID als Dezimalwert (mindestens 10-stellig).
        /// Kompatibel mit MIFARE-Classic-Readern, die die volle 4-Byte NUID ausgeben.
        /// Beispiel: UID 5D:00:92:65:70 → "0009594224" (3 Bytes) vs "0000152986992" (4 Bytes: 00:92:65:70).
        /// </summary>
        Lower4Bytes = 3
    }
}
