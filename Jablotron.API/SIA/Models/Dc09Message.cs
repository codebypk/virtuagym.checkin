using System;
using System.Collections.Generic;

namespace Jablotron.API.SIA.Models
{
    public sealed class Dc09Message
    {
        public byte[] RawBytes { get; set; } = Array.Empty<byte>();
        public string IdSpravy { get; set; }
        public string LinkNumber { get; set; }
        public string RawText { get; set; }
        public string Payload { get; set; }
        public string ProtocolId { get; set; }
        public string AccountNumber { get; set; }
        public string EventCode { get; set; }
        public string EventCodeKey { get; set; }
        public IReadOnlyList<string> EventDescriptions { get; set; } = Array.Empty<string>();
        public string ExtraMessage { get; set; }
        public string TypeMessage { get; set; }
        public string Time { get; set; }
        public string Date { get; set; }
        public string Terminator { get; set; }
        public string User { get; set; }
        public string Zone { get; set; }
        public string NriId { get; set; }
    }
}
