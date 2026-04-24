using System;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using AccessPass.Crypto;
using AccessPass.Models;

namespace Tests.Services
{
    [TestClass]
    public class QrTokenCryptoServiceTests
    {
        private static QrTokenCryptoService CreateService()
        {
            return new QrTokenCryptoService(
                "VGQR1:",
                Encoding.UTF8.GetBytes("ePY16gkBnWyoNCtOl7HggvY0bhwWsKLFfaj6hdYRopE="),
                "test-secret");
        }

        [TestMethod]
        public void TryEncryptAndDecrypt_RoundTrip_Succeeds()
        {
            var crypto = CreateService();
            var payload = new QrCodePayload
            {
                card_id = "0009594224",
                member_id = "42",
                valid_until = DateTime.UtcNow.AddDays(1).ToString("o")
            };

            bool encrypted = crypto.TryEncrypt(payload, out string token, out string encryptError);
            bool decrypted = crypto.TryDecrypt<QrCodePayload>(token, out QrCodePayload roundTrip, out string decryptError);

            Assert.IsTrue(encrypted, encryptError);
            Assert.IsTrue(crypto.IsEncryptedToken(token));
            Assert.IsTrue(decrypted, decryptError);
            Assert.IsNotNull(roundTrip);
            Assert.AreEqual(payload.card_id, roundTrip.card_id);
            Assert.AreEqual(payload.member_id, roundTrip.member_id);
            Assert.AreEqual(payload.valid_until, roundTrip.valid_until);
        }

        [TestMethod]
        public void TryEncrypt_NullPayload_ReturnsFalse()
        {
            var crypto = CreateService();
            bool result = crypto.TryEncrypt<QrCodePayload>(null, out string token, out string error);

            Assert.IsFalse(result);
            Assert.IsNull(token);
        }

        [TestMethod]
        public void TryDecrypt_InvalidPrefix_ReturnsFalse()
        {
            var crypto = CreateService();
            bool result = crypto.TryDecrypt<QrCodePayload>("plain-text", out QrCodePayload payload, out string error);

            Assert.IsFalse(result);
            Assert.IsNull(payload);
        }
    }
}
