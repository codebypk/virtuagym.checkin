using System;

namespace Hardware.Events
{
    /// <summary>
    /// Event-Daten für die Kamera-Vorschau des QR-Scanners.
    /// </summary>
    public class CameraPreviewEventArgs : EventArgs
    {
        /// <summary>Aktuelles Kamerabild als JPEG-Bytes (plattformunabhängig).</summary>
        public byte[] FrameData { get; }

        /// <summary>Anzeigename der Kamera.</summary>
        public string CameraLabel { get; }

        public CameraPreviewEventArgs(byte[] frameData, string cameraLabel)
        {
            FrameData = frameData;
            CameraLabel = cameraLabel;
        }
    }
}
