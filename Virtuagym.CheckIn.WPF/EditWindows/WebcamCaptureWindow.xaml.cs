using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using OpenCvSharp;
using OpenCvSharp.Extensions;
using Virtuagym.CheckIn.Core.Services;

namespace Virtuagym.CheckIn.WPF
{
    public partial class WebcamCaptureWindow : System.Windows.Window
    {
        /// <summary>Captured JPEG bytes, or null if cancelled.</summary>
        public byte[]? CapturedBytes { get; private set; }

        private VideoCapture? _capture;
        private CancellationTokenSource? _cts;
        private bool _cameraReady;

        public WebcamCaptureWindow()
        {
            InitializeComponent();
            Title = L.T("AP_Edit_Webcam_Title");
            txtStatus.Text = L.T("AP_Edit_Webcam_Starting");
            btnCapture.Content = L.T("AP_Edit_Webcam_Btn_Capture");
            btnCancel.Content = L.T("AP_Edit_Webcam_Btn_Cancel");
            Loaded += OnLoaded;
            Closing += OnClosing;
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            _cts = new CancellationTokenSource();
            try
            {
                _capture = new VideoCapture(0);
                if (!_capture.IsOpened())
                {
                    txtStatus.Text = L.T("AP_Edit_Webcam_NoCamera");
                    return;
                }

                _cameraReady = true;
                btnCapture.IsEnabled = true;
                txtStatus.Text = L.T("AP_Edit_Webcam_Ready");

                await PreviewLoop(_cts.Token);
            }
            catch (Exception ex)
            {
                txtStatus.Text = string.Format(L.T("AP_Edit_Webcam_Error"), ex.Message);
            }
        }

        private async Task PreviewLoop(CancellationToken ct)
        {
            using var frame = new Mat();
            while (!ct.IsCancellationRequested)
            {
                if (_capture == null || !_capture.IsOpened()) break;
                if (_capture.Read(frame) && !frame.Empty())
                {
                    var bmp = BitmapConverter.ToBitmap(frame);
                    var bmpImage = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(
                        bmp.GetHbitmap(), IntPtr.Zero,
                        System.Windows.Int32Rect.Empty,
                        BitmapSizeOptions.FromEmptyOptions());
                    bmpImage.Freeze();
                    imgPreview.Source = bmpImage;
                    bmp.Dispose();
                }
                await Task.Delay(33, ct).ConfigureAwait(false);
                await Dispatcher.InvokeAsync(() => { }); // yield to UI
            }
        }

        private void btnCapture_Click(object sender, RoutedEventArgs e)
        {
            if (!_cameraReady || _capture == null) return;

            using var frame = new Mat();
            if (_capture.Read(frame) && !frame.Empty())
            {
                Cv2.ImEncode(".jpg", frame, out var buf);
                CapturedBytes = buf;
            }

            StopCamera();
            DialogResult = CapturedBytes != null;
            Close();
        }

        private void btnCancel_Click(object sender, RoutedEventArgs e)
        {
            StopCamera();
            DialogResult = false;
            Close();
        }

        private void StopCamera()
        {
            _cts?.Cancel();
            _capture?.Release();
            _capture?.Dispose();
            _capture = null;
        }

        private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            StopCamera();
        }
    }
}
