using Hardware.Interfaces;
using Hardware.Models;
using Virtuagym.CheckIn.Core.Abstractions;
using Virtuagym.CheckIn.Core.Helper;

namespace Virtuagym.CheckIn.Core.Services;

/// <summary>
/// Adapter between <see cref="IHardwareLogger"/> (Hardware project) and <see cref="ILogWriter"/> (Core project).
/// Allows reusing existing ILogWriter implementations for hardware components.
/// </summary>
public class HardwareLoggerAdapter : IHardwareLogger
{
    private readonly ILogWriter _logWriter;

    public HardwareLoggerAdapter(ILogWriter logWriter)
    {
        _logWriter = logWriter;
    }

    public void Log(string message, HardwareLogLevel level = HardwareLogLevel.Info)
    {
        _logWriter.WriteToLog(message, (int)level);
    }
}
