using System;
using System.Linq;

namespace Hardware.Models
{
    /// <summary>
    /// HID-Protokoll-Profil für einen USB-RFID-Reader.
    /// Definiert die gerätespezifischen Byte-Sequenzen für Lese- und Beep-Befehle
    /// sowie den Hex-Wert eines ungültigen/leeren Tags.
    /// </summary>
    public class HidProfile
    {
        /// <summary>Eindeutige Profil-ID (z.B. "wCopy_NS122").</summary>
        public string ProfileId { get; set; }

        /// <summary>Anzeigename für die UI (z.B. "wCopy NS122 (USB HID)").</summary>
        public string DisplayName { get; set; }

        /// <summary>Lese-Befehl als Leerzeichen-separierter Hex-String (z.B. "01 01 13 34 ...").</summary>
        public string ReadCommand { get; set; }

        /// <summary>Beep-Befehl als Leerzeichen-separierter Hex-String.</summary>
        public string BeepCommand { get; set; }

        /// <summary>ReadCommand als Byte-Array (lazy parsed).</summary>
        public byte[] ReadCommandBytes => ParseHex(ReadCommand);

        /// <summary>BeepCommand als Byte-Array (lazy parsed).</summary>
        public byte[] BeepCommandBytes => ParseHex(BeepCommand);

        private static byte[] ParseHex(string hex)
        {
            if (string.IsNullOrWhiteSpace(hex)) return [];
            return hex.Split(new[] { ' ', '-' }, StringSplitOptions.RemoveEmptyEntries)
                      .Select(h => Convert.ToByte(h, 16))
                      .ToArray();
        }
    }
}
