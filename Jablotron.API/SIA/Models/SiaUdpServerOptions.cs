namespace Jablotron.API.SIA.Models
{
    public sealed class SiaUdpServerOptions
    {
        public int Port { get; set; } = 33300;
        public bool SendAck { get; set; } = true;
    }
}
