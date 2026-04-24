namespace Jablotron.API.Odbo.Models
{
    /// <summary>
    /// Ein Zugangsereignis aus der Jablotron ODBO-Link Datenbank.
    /// </summary>
    public class JablotronAccessEvent
    {
        public string Date { get; set; }
        public string Time { get; set; }
        public string EventText { get; set; }
        public string Source { get; set; }
        public string UserName { get; set; }
        public int UserID { get; set; }
        public long CardCode { get; set; }
        public string Segment { get; set; }

        /// <summary>Zugeordneter Virtuagym-Member-Name (nach dem Mapping).</summary>
        public string VirtuagymMemberName { get; set; }

        /// <summary>Zugeordnete Virtuagym-Member-ID (nach dem Mapping).</summary>
        public long VirtuagymMemberId { get; set; }

        /// <summary>Jablotron-Anzeige der CardCode (Hex-Format wie AccessCard1Display).</summary>
        public string CardCodeDisplay => CardCode > 0
            ? CardCode.ToString("X8").PadLeft(10, '0')
            : "";
    }
}
