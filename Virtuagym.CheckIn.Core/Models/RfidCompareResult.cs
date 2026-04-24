using System.ComponentModel;

namespace Virtuagym.CheckIn.Core.Models
{
    /// <summary>
    /// Result of an RFID comparison between Virtuagym and Jablotron.
    /// </summary>
    public class RfidCompareResult : INotifyPropertyChanged
    {
        private bool _isSelected;

        /// <summary>Selected by the user for update.</summary>
        public bool IsSelected
        {
            get => _isSelected;
            set { _isSelected = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected))); }
        }

        /// <summary>Virtuagym Member-Name.</summary>
        public string VirtuagymName { get; set; }

        /// <summary>Virtuagym Member-ID.</summary>
        public string VirtuagymMemberId { get; set; }

        /// <summary>RFID-Tag aktuell in Virtuagym (rfid_tag).</summary>
        public string VirtuagymRfidTag { get; set; }

        /// <summary>Ist der Virtuagym Member aktiv?</summary>
        public bool VirtuagymActive { get; set; }

        /// <summary>Passender Jablotron-Benutzername (oder leer).</summary>
        public string JablotronName { get; set; }

        /// <summary>Jablotron AccessCard1-Wert (Anzeige-Format, z.B. "0009594224").</summary>
        public string JablotronAccessCard { get; set; }

        /// <summary>Ist der Jablotron-Benutzer aktiv (nicht blockiert)?</summary>
        public bool JablotronActive { get; set; }

        /// <summary>Status des Abgleichs.</summary>
        public string Status { get; set; }

        /// <summary>Detailinfo zum Matching.</summary>
        public string MatchInfo { get; set; }

        public event PropertyChangedEventHandler PropertyChanged;
    }
}
