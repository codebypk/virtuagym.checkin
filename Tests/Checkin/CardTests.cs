using System;
using System.Globalization;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Hardware;
using Hardware.Events;
using Hardware.Interfaces;
using Hardware.Models;
using Hardware.Services;

namespace Tests.Checkin
{
    [TestClass]
    public class CardTests
    {
        [TestMethod]
        public void Card_ValidTag_ParsesHexCorrectly()
        {
            // Arrange - Protokoll: Byte 5 = UID-Länge (0x05), ab Byte 6 folgen die UID-Bytes (5D00926570)
            byte[] cardData = [ 0x00, 0x02, 0x0C, 0x35, 0x00, 0x05, 0x5D, 0x00, 0x92, 0x65, 0x70 ];

            // Act
            Card card = new (cardData);

            // Assert
            Assert.AreEqual("5D00926570", card.UidHex);
            Assert.AreEqual("399441552752", card.UidDecimal);
            Assert.AreEqual("0009594224", card.GetCardId(CardIdMode.Lower3Bytes));
#pragma warning disable CS0618 // UidLegacy is obsolete
            Assert.AreEqual("14625968", card.UidLegacy);
#pragma warning restore CS0618
            Assert.IsTrue(card.IsValidTag);
        }

        [TestMethod]
        public void Card_Test_AutoDetect_TagNumberHex()
        {
            //byte[] tagBytes = HexToBytes("4F006EF3C9");
            //byte[] cardData = new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x05, tagBytes[0], tagBytes[1], tagBytes[2], tagBytes[3], tagBytes[4] };

            Card card = new ("4F006EF3C9");

            // Assert
            Assert.AreEqual("4F006EF3C9", card.UidHex);
            Assert.AreEqual("339309687753", card.UidDecimal);
            Assert.AreEqual("0007271369", card.GetCardId(CardIdMode.Lower3Bytes));
#pragma warning disable CS0618 // UidLegacy is obsolete
            Assert.AreEqual("11062409", card.UidLegacy);
#pragma warning restore CS0618
            Assert.IsTrue(card.IsValidTag);
        }

        [TestMethod]
        public void Card_Test_AutoDetect_TagNumber10()
        {
            // Act
            Card card = new ("0009594224");

            // Assert
            Assert.AreEqual("926570", card.UidHex);  // 3 Bytes
            Assert.AreEqual("0009594224", card.UidDecimal);
            Assert.AreEqual("0009594224", card.GetCardId(CardIdMode.Lower3Bytes));
#pragma warning disable CS0618 // UidLegacy is obsolete
            Assert.AreEqual("14625968", card.UidLegacy);
#pragma warning restore CS0618
            Assert.IsTrue(card.IsValidTag);
        }

        [TestMethod]
        public void Card_Test_AutoDetect_TagNumber8()
        {
            // Act
            Card card = new("14625968");

            // Assert
            Assert.AreEqual("00926570", card.UidHex);  // 4 Bytes
            Assert.AreEqual("0009594224", card.UidDecimal);
            Assert.AreEqual("0009594224", card.GetCardId(CardIdMode.Lower3Bytes));
#pragma warning disable CS0618 // UidLegacy is obsolete
            Assert.AreEqual("14625968", card.UidLegacy);
#pragma warning restore CS0618
            Assert.IsTrue(card.IsValidTag);
        }

        [TestMethod]
        public void Card_GetCardId_Lower3Bytes_MatchesJA190T()
        {
            // 5-Byte UID: JA-190T gibt "0009594224" aus (untere 3 Bytes: 92:65:70)
            byte[] cardData = [ 0x00, 0x02, 0x0C, 0x35, 0x00, 0x05, 0x5D, 0x00, 0x92, 0x65, 0x70 ];
            Card card = new (cardData);

            Assert.AreEqual("0009594224", card.GetCardId(CardIdMode.Lower3Bytes));
            Assert.AreEqual("0009594224", card.GetCardId(CardIdMode.Lower3Bytes)); // Default = Lower3Bytes
        }

        [TestMethod]
        public void Card_GetCardId_FullDecimal_ReturnsFullUid()
        {
            byte[] cardData = [ 0x00, 0x02, 0x0C, 0x35, 0x00, 0x05, 0x5D, 0x00, 0x92, 0x65, 0x70 ];
            Card card = new (cardData);

            Assert.AreEqual("399441552752", card.GetCardId(CardIdMode.FullDecimal));
        }

        [TestMethod]
        public void Card_GetCardId_FullHex_ReturnsHexUid()
        {
            byte[] cardData = [ 0x00, 0x02, 0x0C, 0x35, 0x00, 0x05, 0x5D, 0x00, 0x92, 0x65, 0x70 ];
            Card card = new (cardData);

            Assert.AreEqual("5D00926570", card.GetCardId(CardIdMode.FullHex));
        }

        [TestMethod]
        public void Card_GetCardId_RawValue_IgnoresMode()
        {
            Card card = Card.FromRawValue("ABC123");

            Assert.AreEqual("ABC123", card.GetCardId(CardIdMode.Lower3Bytes));
            Assert.AreEqual("ABC123", card.GetCardId(CardIdMode.FullDecimal));
            Assert.AreEqual("ABC123", card.GetCardId(CardIdMode.FullHex));
            Assert.AreEqual("ABC123", card.GetCardId(CardIdMode.Lower4Bytes));
        }

        [TestMethod]
        public void Card_GetCardId_Lower4Bytes_ReturnsFourByteDecimal()
        {
            // UID 5D:00:92:65:70 → untere 4 Bytes: 00:92:65:70 = 9594224
            byte[] cardData = [ 0x00, 0x02, 0x0C, 0x35, 0x00, 0x05, 0x5D, 0x00, 0x92, 0x65, 0x70 ];
            Card card = new (cardData);

            Assert.AreEqual("0009594224", card.GetCardId(CardIdMode.Lower4Bytes));
            // Vergleich: Lower3Bytes hat den gleichen Wert bei dieser UID (weil Byte 4 = 0x00)
            Assert.AreEqual(card.GetCardId(CardIdMode.Lower3Bytes), card.GetCardId(CardIdMode.Lower4Bytes));
        }

        [TestMethod]
        public void Card_GetCardId_Lower4Bytes_DiffersFromLower3Bytes()
        {
            // UID mit nicht-null 4. Byte von rechts: AB:CD:EF:12
            Card card = new ("ABCDEF12");

            // Lower 3 Bytes: CD:EF:12 = 13496082
            Assert.AreEqual("0013496082", card.GetCardId(CardIdMode.Lower3Bytes));
            // Lower 4 Bytes: AB:CD:EF:12 = 2882400018
            Assert.AreEqual("2882400018", card.GetCardId(CardIdMode.Lower4Bytes));
        }
    }
}
