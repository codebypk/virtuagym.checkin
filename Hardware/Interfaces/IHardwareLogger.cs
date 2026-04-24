using Hardware.Models;

namespace Hardware.Interfaces
{
    /// <summary>
    /// Logger-Schnittstelle für Hardware-Komponenten.
    /// Entkoppelt die Hardware-Klassen von konkreten UI- oder Datei-Loggern.
    /// </summary>
    public interface IHardwareLogger
    {
        void Log(string message, HardwareLogLevel level = HardwareLogLevel.Info);
    }
}
