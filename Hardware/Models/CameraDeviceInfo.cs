namespace Hardware.Models
{
    /// <summary>
    /// Informationen über eine erkannte Kamera.
    /// </summary>
    public class CameraDeviceInfo
    {
        /// <summary>OpenCV-Kameraindex (0, 1, 2, …).</summary>
        public int Index { get; set; }

        /// <summary>Gerätename aus WMI (z.B. "Logitech HD Webcam C920") oder Fallback "Camera {Index}".</summary>
        public string Name { get; set; }

        /// <summary>Hersteller aus WMI (z.B. "Logitech") oder leer.</summary>
        public string Manufacturer { get; set; }

        /// <summary>Status aus WMI (z.B. "OK") oder leer.</summary>
        public string Status { get; set; }

        /// <summary>Anzeigename für ComboBoxen.</summary>
        public string DisplayName => !string.IsNullOrWhiteSpace(Manufacturer)
            ? $"{Index}: {Manufacturer} – {Name}"
            : $"{Index}: {Name}";

        public override string ToString() => DisplayName;
    }
}
