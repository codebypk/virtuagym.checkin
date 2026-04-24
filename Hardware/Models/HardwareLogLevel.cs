namespace Hardware.Models
{
    /// <summary>
    /// Log-Level für Hardware-Komponenten.
    /// Die Werte entsprechen den bisherigen Constants.LogInfo/Warning/Error/Success
    /// und können 1:1 als int an bestehende Logger weitergereicht werden.
    /// </summary>
    public enum HardwareLogLevel
    {
        Info = 1,
        Warning = 2,
        Error = 3,
        Success = 4
    }
}
