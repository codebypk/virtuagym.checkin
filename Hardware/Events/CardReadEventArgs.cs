using System;
using Hardware.Models;

namespace Hardware.Events
{
    /// <summary>
    /// Event-Daten wenn ein RFID-Tag oder eine Smartcard gelesen wurde.
    /// </summary>
    public class CardReadEventArgs : EventArgs
    {
        /// <summary>Geparste Karteninformationen (Hex, Dezimal, 8-stellig).</summary>
        public Card Card { get; }

        /// <summary>Anzeigename des Readers, der die Karte gelesen hat.</summary>
        public string ReaderName { get; }

        public CardReadEventArgs(Card card, string readerName)
        {
            Card = card;
            ReaderName = readerName;
        }
    }
}
