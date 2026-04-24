using System;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Jablotron.API.Odbo;
using Jablotron.API.Odbo.Models;

namespace Tests.Api
{
    [TestClass]
    public class JablotronDbServiceTests
    {
        /// <summary>
        /// Pfad zur Test-FDB-Datei relativ zum Build-Output.
        /// </summary>
        private static string GetTestFdbPath()
        {
            // Sucht die Datei ausgehend vom Build-Verzeichnis nach oben
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string path = Path.Combine(baseDir, @"..\..\..\..\VirtuagymMemberCheckIn\Resources\jablotron.fdb");
            return Path.GetFullPath(path);
        }

        [TestMethod]
        public void OpenFdb_ValidFile_DoesNotThrow()
        {
            string fdbPath = GetTestFdbPath();
            if (!File.Exists(fdbPath))
                Assert.Inconclusive($"Test-FDB-Datei nicht vorhanden: {fdbPath}");

            using (var db = new JablotronDbService(fdbPath))
            {
                Assert.IsNotNull(db.FileInfo);
            }
        }

        [TestMethod]
        public void GetUsers_ValidFile_ReturnsUsers()
        {
            string fdbPath = GetTestFdbPath();
            if (!File.Exists(fdbPath))
                Assert.Inconclusive($"Test-FDB-Datei nicht vorhanden: {fdbPath}");

            using (var db = new JablotronDbService(fdbPath))
            {
                var users = db.GetUsers();
                Assert.IsTrue(users.Count > 0, "Es sollte mindestens ein Benutzer vorhanden sein.");
            }
        }

        [TestMethod]
        public void GetUsers_ValidFile_UsersWithAccessCard()
        {
            string fdbPath = GetTestFdbPath();
            if (!File.Exists(fdbPath))
                Assert.Inconclusive($"Test-FDB-Datei nicht vorhanden: {fdbPath}");

            using (var db = new JablotronDbService(fdbPath))
            {
                var users = db.GetUsers();
                var usersWithCard = users.Where(u => u.AccessCard1 > 0).ToList();
                var user = usersWithCard.Where(u => u.Name == "Max Mustermann").FirstOrDefault();
                Assert.IsTrue(usersWithCard.Count > 0,
                    "Mindestens ein Benutzer sollte eine AccessCard1 zugewiesen haben.");
            }
        }

        [TestMethod]
        public void GetRawXml_ValidFile_ReturnsXml()
        {
            string fdbPath = GetTestFdbPath();
            if (!File.Exists(fdbPath))
                Assert.Inconclusive($"Test-FDB-Datei nicht vorhanden: {fdbPath}");
            using (var db = new JablotronDbService(fdbPath))
            {
                string xml = db.GetRawXml();
                Assert.IsFalse(string.IsNullOrWhiteSpace(xml), "Raw XML sollte nicht leer sein.");
                Assert.IsTrue(xml.StartsWith("<?xml"), "Raw XML sollte mit '<?xml' beginnen.");
                //File.WriteAllText(fdbPath.Replace(".fdb", ".xml"), xml);
            }
        }

        [TestMethod]
        public void OpenFdb_NonExistentFile_ThrowsFileNotFoundException()
        {
            try
            {
                using (var db = new JablotronDbService(@"C:\nonexistent\fake.fdb"))
                {
                }

                Assert.Fail("Expected FileNotFoundException was not thrown.");
            }
            catch (FileNotFoundException)
            {
            }
        }

        [TestMethod]
        public void OpenFdb_InvalidFile_ThrowsInvalidOperationException()
        {
            // Erstelle eine temporÃ¤re Datei mit ungÃ¼ltigem Inhalt
            string tempPath = Path.GetTempFileName();
            try
            {
                File.WriteAllBytes(tempPath, new byte[] { 0x00, 0x00, 0x00, 0x49, 0x4E, 0x56, 0x41, 0x4C,
                    0x49, 0x44, 0x20, 0x48, 0x45, 0x41, 0x44, 0x45, 0x52, 0x20, 0x44, 0x41, 0x54, 0x41,
                    0x20, 0x20, 0x20, 0x20, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 });

                try
                {
                    using (var db = new JablotronDbService(tempPath))
                    {
                    }

                    Assert.Fail("Expected InvalidOperationException was not thrown.");
                }
                catch (InvalidOperationException)
                {
                }
            }
            finally
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
            }
        }


        [TestMethod]
        public void AccessCard1Display_ConvertsRawIntToHexDisplayFormat()
        {
            // Jablotron speichert AccessCard intern als Integer, zeigt sie aber als
            // Hex-String (10-stellig, null-gepadded) an.
            // Beispiel: Rohwert 156844580 â†’ 0x09594224 â†’ "0009594224"
            var user = new JablotronUser { AccessCard1 = 156844580 };
            Assert.AreEqual("0009594224", user.AccessCard1Display);
        }
    }
}
