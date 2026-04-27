using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using OpenCvSharp;
using OpenCvSharp.Extensions;
using Virtuagym.CheckIn.Core.Services;
using System.Runtime.InteropServices;
using Hardware.Services;

namespace Virtuagym.CheckIn.WPF
{
    public partial class WebcamCaptureWindow : System.Windows.Window
    {
        [DllImport("gdi32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DeleteObject(IntPtr hObject);

        /// <summary>Captured JPEG bytes, or null if cancelled.</summary>
        public byte[]? CapturedBytes { get; private set; }

        private VideoCapture? _capture;
        private CancellationTokenSource? _cts;
        private bool _cameraReady;
        private bool _scannerResumePending;
        private int? _pausedScannerCameraIndex;
        private bool _cameraSwitchInProgress;

        public WebcamCaptureWindow()
        {
            InitializeComponent();
            Title = L.T("AP_Edit_Webcam_Title");
            txtStatus.Text = L.T("AP_Edit_Webcam_Starting");
            btnCapture.Content = L.T("AP_Edit_Webcam_Btn_Capture");
            btnCancel.Content = L.T("Btn_Cancel");
            Loaded += OnLoaded;
            Closing += OnClosing;
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            try
            {
                var cameras = await CameraDiscoveryService.GetCamerasAsync();
                var cameraItems = cameras
                    .Select(c => new CameraSelectionItem
                    {
                        Index = c.Index,
                        DisplayName = string.IsNullOrWhiteSpace(c.Name)
                            ? $"Camera {c.Index}"
                            : $"{c.Name} (#{c.Index})"
                    })
                    .ToList();

                if (cameraItems.Count == 0)
                {
                    txtStatus.Text = L.T("AP_Edit_Webcam_NoCamera");
                    btnCapture.IsEnabled = false;
                    return;
                }

                var cameraCombo = FindName("cmbCamera") as ComboBox;
                if (cameraCombo == null)
                {
                    txtStatus.Text = L.T("AP_Edit_Webcam_NoCamera");
                    btnCapture.IsEnabled = false;
                    return;
                }

                cameraCombo.ItemsSource = cameraItems;
                cameraCombo.DisplayMemberPath = nameof(CameraSelectionItem.DisplayName);
                cameraCombo.SelectedIndex = 0;
                cameraCombo.IsEnabled = cameraItems.Count > 1;

                await SwitchToSelectedCameraAsync();
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
                    using var bmp = BitmapConverter.ToBitmap(frame);
                    IntPtr hBitmap = bmp.GetHbitmap();
                    try
                    {
                        var bmpImage = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(
                            hBitmap, IntPtr.Zero,
                            System.Windows.Int32Rect.Empty,
                            BitmapSizeOptions.FromEmptyOptions());
                        bmpImage.Freeze();
                        await Dispatcher.InvokeAsync(() => imgPreview.Source = bmpImage);
                    }
                    finally
                    {
                        DeleteObject(hBitmap);
                    }
                }
                await Task.Delay(33, ct);
            }
        }

        private async void cmbCamera_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!IsLoaded) return;
            await SwitchToSelectedCameraAsync();
        }

        private async Task SwitchToSelectedCameraAsync()
        {
            if (_cameraSwitchInProgress) return;

            var cameraCombo = FindName("cmbCamera") as ComboBox;
            if (cameraCombo?.SelectedItem is not CameraSelectionItem selectedCamera) return;

            _cameraSwitchInProgress = true;
            try
            {
                StopCamera();
                imgPreview.Source = null;

                _cts = new CancellationTokenSource();

                QrCodeScanner.PauseByCamera(selectedCamera.Index);
                _pausedScannerCameraIndex = selectedCamera.Index;
                _scannerResumePending = true;

                _capture = new VideoCapture(selectedCamera.Index);
                if (!_capture.IsOpened())
                {
                    txtStatus.Text = L.T("AP_Edit_Webcam_NoCamera");
                    btnCapture.IsEnabled = false;
                    return;
                }

                _cameraReady = true;
                btnCapture.IsEnabled = true;
                txtStatus.Text = L.T("AP_Edit_Webcam_Ready");

                _ = PreviewLoop(_cts.Token);
                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                txtStatus.Text = string.Format(L.T("AP_Edit_Webcam_Error"), ex.Message);
                btnCapture.IsEnabled = false;
            }
            finally
            {
                _cameraSwitchInProgress = false;
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
            _cameraReady = false;
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
            _capture?.Release();
            _capture?.Dispose();
            _capture = null;

            if (_scannerResumePending && _pausedScannerCameraIndex.HasValue)
            {
                QrCodeScanner.ResumeByCamera(_pausedScannerCameraIndex.Value);
                _scannerResumePending = false;
                _pausedScannerCameraIndex = null;
            }
        }

        private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            StopCamera();
        }

        private sealed class CameraSelectionItem
        {
            public int Index { get; init; }
            public string DisplayName { get; init; } = "";
        }
    }
}
