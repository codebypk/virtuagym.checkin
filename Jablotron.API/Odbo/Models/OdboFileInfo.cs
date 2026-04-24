namespace Jablotron.API.Odbo.Models
{
    /// <summary>
    /// Information about an ODBO-Link file.
    /// </summary>
    public class OdboFileInfo
    {
        public string DatabaseName { get; set; }
        public string DatabaseVersion { get; set; }
        public long CompressedSize { get; set; }
        public long DecompressedSize { get; set; }
        public int SectionCount { get; set; }

        public override string ToString()
        {
            string size = CompressedSize > 1024 * 1024
                ? $"{CompressedSize / (1024.0 * 1024.0):F1} MB"
                : $"{CompressedSize / 1024.0:F0} KB";
            return $"ODBO-Link \"{DatabaseName}\" v{DatabaseVersion}, {SectionCount} Sektionen, {size} → {DecompressedSize / (1024.0 * 1024.0):F1} MB";
        }
    }
}
