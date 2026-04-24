using System;

namespace Hardware.Events
{
    /// <summary>
    /// Event-Daten wenn ein QR-Code gelesen wurde.
    /// </summary>
    public class QrCodeReadEventArgs : EventArgs
    {
        /// <summary>Der gelesene QR-Code-Inhalt.</summary>
        public string QrCode { get; }

        /// <summary>Anzeigename des Scanners.</summary>
        public string ReaderName { get; }

        public QrCodeReadEventArgs(string qrCode, string readerName)
        {
            QrCode = qrCode;
            ReaderName = readerName;
        }
    }
}
