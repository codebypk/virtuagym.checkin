using Virtuagym.CheckIn.Core.Helper;

namespace Virtuagym.CheckIn.Core.Abstractions
{
    /// <summary>
    /// Interface for application logging.
    /// Decouples hardware components (RFID reader, QR scanner, relay) from the MainWindow.
    /// </summary>
    public interface ILogWriter
    {
        void WriteToLog(string text, int type = Constants.LogInfo);
        void WriteToRejectedLog(string cardId, string status, string reason);
    }
}
