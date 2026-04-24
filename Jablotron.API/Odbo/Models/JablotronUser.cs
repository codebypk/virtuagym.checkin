namespace Jablotron.API.Odbo.Models
{
    /// <summary>
    /// Ein Benutzer aus der Jablotron ODBO-Link Datenbank.
    /// </summary>
    public class JablotronUser
    {
        public int ID { get; set; }
        public string Name { get; set; }
        public string Code { get; set; }
        public long AccessCard1 { get; set; }
        public long AccessCard2 { get; set; }
        public bool IsNull { get; set; }
        public bool IsBlocked { get; set; }
        public string Permissions { get; set; }

        /// <summary>
        /// Jablotron-Anzeige der AccessCard1: Der Rohwert wird als Hex konvertiert und
        /// als 10-stelliger Dezimal-String dargestellt (z.B. 156844580 → 0x09594224 → "0009594224").
        /// </summary>
        public string AccessCard1Display => AccessCard1 > 0
            ? AccessCard1.ToString("X8").PadLeft(10, '0')
            : "";

        /// <summary>
        /// Jablotron-Anzeige der AccessCard2 (gleiche Konvertierung wie AccessCard1).
        /// </summary>
        public string AccessCard2Display => AccessCard2 > 0
            ? AccessCard2.ToString("X8").PadLeft(10, '0')
            : "";
    }
}
