namespace Virtuagym.CheckIn.Core.Models
{
    public class ProgrammableGateDisplayItem
    {
        public string CloudComponentId { get; set; }
        public string Name { get; set; }
        public bool CanControl { get; set; }
        public bool NeedAuthorization { get; set; }
        public string State { get; set; }
    }
}
