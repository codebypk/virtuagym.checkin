using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Virtuagym.API;
using Virtuagym.API.v1.Models;

namespace Tests.Api
{
    [TestClass]
    public class VirtuagymApiTests
    {
        #region ExtractClubId

        [TestMethod]
        public void ExtractClubId_ValidSecret_ReturnsId()
        {
            string result = VirtuagymApiBase.ExtractClubId("CS-54321-CHECKIN1234-retertert");
            Assert.AreEqual("54321", result);
        }

        [TestMethod]
        public void ExtractClubId_DifferentId_ReturnsId()
        {
            string result = VirtuagymApiBase.ExtractClubId("CS-12345-KEY-abc");
            Assert.AreEqual("12345", result);
        }

        [TestMethod]
        public void ExtractClubId_NullInput_ReturnsNull()
        {
            string result = VirtuagymApiBase.ExtractClubId(null);
            Assert.IsNull(result);

            string result2 = VirtuagymApiBase.ExtractClubId("");
            Assert.IsNull(result2);

            string result3 = VirtuagymApiBase.ExtractClubId("   ");
            Assert.IsNull(result3);

            string result4 = VirtuagymApiBase.ExtractClubId("INVALID-KEY");
            Assert.IsNull(result);
        }

        #endregion

        #region VisitResult Timestamp-Formatierung

        [TestMethod]
        public void VisitResult_CheckInFormatted_Seconds_FormatsCorrectly()
        {
            var visit = new VisitResult
            {
                check_in_timestamp = 1700000000 // 14.11.2023 ~22:13 UTC
            };

            string formatted = visit.CheckInFormatted;
            Assert.IsFalse(string.IsNullOrWhiteSpace(formatted), "Formatierter Timestamp darf nicht leer sein.");
            Assert.IsTrue(formatted.Contains("2023"), "Datum sollte 2023 enthalten: " + formatted);
        }

        [TestMethod]
        public void VisitResult_CheckInFormatted_Milliseconds_FormatsCorrectly()
        {
            var visit = new VisitResult
            {
                check_in_timestamp = 1700000000000 // Gleicher Zeitpunkt in ms
            };

            string formatted = visit.CheckInFormatted;
            Assert.IsFalse(string.IsNullOrWhiteSpace(formatted));
            Assert.IsTrue(formatted.Contains("2023"), "Datum sollte 2023 enthalten: " + formatted);
        }

        #endregion

        #region MemberResult Timestamp-Formatierung

        [TestMethod]
        public void MemberResult_MemberSinceFormatted_ValidTimestamp()
        {
            var member = new MemberResult
            {
                member_since = 1700000000000 // Millisekunden
            };

            string formatted = member.MemberSinceFormatted;
            Assert.IsFalse(string.IsNullOrWhiteSpace(formatted));
            Assert.IsTrue(formatted.Contains("2023"), "Datum sollte 2023 enthalten: " + formatted);
        }

        [TestMethod]
        public void MemberResult_MemberSinceFormatted_Zero_ReturnsEmpty()
        {
            var member = new MemberResult
            {
                member_since = 0
            };

            Assert.AreEqual("", member.MemberSinceFormatted);
        }

        [TestMethod]
        public void MemberResult_TimestampEditFormatted_ValidTimestamp()
        {
            var member = new MemberResult
            {
                timestamp_edit = 1700000000000
            };

            Assert.IsFalse(string.IsNullOrWhiteSpace(member.TimestampEditFormatted));
        }

        [TestMethod]
        public void MemberResult_RegistrationDateFormatted_NullInput()
        {
            var member = new MemberResult
            {
                registration_date = 0
            };

            Assert.AreEqual("", member.RegistrationDateFormatted);
        }

        #endregion
    }
}
