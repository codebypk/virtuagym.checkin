using Hardware.Interfaces;
using Hardware.Models;

namespace Hardware.Services
{
    /// <summary>
    /// No-Op-Logger für Szenarien, in denen kein Log benötigt wird (z.B. Geräte-Tests).
    /// </summary>
    public sealed class NullHardwareLogger : IHardwareLogger
    {
        public void Log(string message, HardwareLogLevel level = HardwareLogLevel.Info) { }
    }
}
