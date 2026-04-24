namespace Virtuagym.CheckIn.Core.Models
{
    /// <summary>
    /// Jablotron Programmable Gate entry loaded from Resources\data\jablotron_gates.json.
    /// Used in the mapping configuration for Programmable Gate selection.
    /// </summary>
    public class PgGateEntry
    {
        /// <summary>Service ID for the Programmable Gate (Jablotron Cloud).</summary>
        public string ServiceId { get; set; }

        /// <summary>Cloud component ID of the Programmable Gate (e.g. "PG-319050193").</summary>
        public string CloudComponentId { get; set; }

        /// <summary>Display name of the gate (e.g. "EINGANSTUER").</summary>
        public string Name { get; set; }

        /// <summary>Whether the gate can be remotely controlled.</summary>
        public bool CanControl { get; set; }

        /// <summary>Whether authorization is required.</summary>
        public bool NeedAuthorization { get; set; }

        /// <summary>Display name for the ComboBox: "Name (CloudComponentId)".</summary>
        public string Display => string.Format("{0} ({1})", Name ?? "-", CloudComponentId ?? "-");
    }
}
