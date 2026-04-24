using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ZXing;
using ZXing.Common;
using ZXing.Rendering;
using ZXing.Windows.Compatibility;

namespace Tests.Utility
{
    /// <summary>
    /// Tests für QR-Code Bild-Dekodierung.
    /// 
    /// Diese Tests dekodieren echte QR-Code Bilder und validieren die Inhalte.
    /// Die QR-Codes enthalten either card_id oder member_id für das Check-In.
    /// 
    /// Die ZXing.Net NuGet-Paket wird für QR-Code Dekodierung verwendet.
    /// </summary>
    [TestClass]
    public class QrCodeImageTests
    {
        private readonly BarcodeReaderGeneric _barcodeReader = new BarcodeReaderGeneric
        {
            AutoRotate = true,
            Options = new DecodingOptions
            {
                TryHarder = true,
                PossibleFormats = new[] { BarcodeFormat.QR_CODE }
            }
        };
        private readonly string _testResourcesPath = Path.Combine(
            Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location),
            "TestResources", "QrCodes");

        #region QR-Code Decoding

        [TestMethod]
        public void QrCodeImage_CardId_DecodesCorrectly()
        {
            // Arrange
            string imagePath = Path.Combine(_testResourcesPath, "qrcode_cardid_0009594224.png");

            if (!File.Exists(imagePath))
            {
                Assert.Inconclusive($"QR-Code image not found: {imagePath}");
            }

            // Act
            var result = DecodeQrCodeImage(imagePath);

            // Assert
            Assert.IsNotNull(result);
            Assert.AreEqual("0009594224", result.Trim());
        }

        [TestMethod]
        public void QrCodeImage_MemberId_DecodesCorrectly()
        {
            // Arrange
            string imagePath = Path.Combine(_testResourcesPath, "qrcode_memberid_33965531.png");

            if (!File.Exists(imagePath))
            {
                Assert.Inconclusive($"QR-Code image not found: {imagePath}");
            }

            // Act
            var result = DecodeQrCodeImage(imagePath);

            // Assert
            Assert.IsNotNull(result);
            Assert.AreEqual("33965531", result.Trim());
        }

        #endregion

        #region QR-Code Generation and Verification

        [TestMethod]
        public void QrCodeGeneration_EncodeAndDecode_RoundTrip()
        {
            var testValues = new[] { "0009594224", "33965531" };

            foreach (var value in testValues)
            {
                var writer = new ZXing.BarcodeWriter<System.Drawing.Bitmap>
                {
                    Format = BarcodeFormat.QR_CODE,
                    Renderer = new BitmapRenderer(),
                    Options = new EncodingOptions
                    {
                        Height = 200,
                        Width = 200,
                        Margin = 10
                    }
                };

                var bitmap = writer.Write(value);
                Assert.IsNotNull(bitmap);
                Assert.IsTrue(bitmap.Width > 0);
                Assert.IsTrue(bitmap.Height > 0);

                var reader = new ZXing.BarcodeReaderGeneric();
                var result = reader.Decode(new BitmapLuminanceSource(bitmap));
                Assert.IsNotNull(result, $"QR-Code konnte nicht dekodiert werden: {value}");
                Assert.AreEqual(value, result.Text);
            }
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// Dekodiert einen QR-Code aus einem Bild und gibt den Text zurück.
        /// </summary>
        private string DecodeQrCodeImage(string imagePath)
        {
            if (!File.Exists(imagePath))
            {
                throw new FileNotFoundException($"QR-Code image not found: {imagePath}");
            }

            using (var image = Image.FromFile(imagePath))
            using (var bitmap = new Bitmap(image))
            {
                var result = _barcodeReader.Decode(new BitmapLuminanceSource(bitmap));
                return result?.Text;
            }
        }

        #endregion
    }
}
