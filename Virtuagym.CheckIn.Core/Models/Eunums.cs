using System;
using System.Collections.Generic;
using System.Text;

namespace Virtuagym.CheckIn.Core.Models
{
    /// <summary>
    /// Input device type for a check-in mapping.
    /// JSON values deliberately kept for backwards-compatibility with existing config files.
    /// </summary>
    public enum HardwareInputType
    {
        USBReader = 0,  // ← JSON-Wert beibehalten, nicht "Rfid"
        QRCode = 1,
        CCID = 2
    }

    /// <summary>
    /// Defines when a hardware trigger (relay or PG gate) should fire.
    /// </summary>
    public enum HardwareTriggerAction
    {
        /// <summary>Trigger on both check-in and check-out.</summary>
        Always = 0,

        /// <summary>Trigger only on check-in.</summary>
        CheckinOnly = 1,

        /// <summary>Trigger only on check-out.</summary>
        CheckoutOnly = 2
    }
}
