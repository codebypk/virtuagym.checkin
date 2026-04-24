using System;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Virtuagym.CheckIn.Core.Helper;
using Virtuagym.CheckIn.Core.Services;

namespace Tests.Utility
{
    [TestClass]
    public class LocalizationManagerTests
    {
        private string _tempLangFile;

        [TestCleanup]
        public void Cleanup()
        {
            if (!string.IsNullOrWhiteSpace(_tempLangFile) && File.Exists(_tempLangFile))
                File.Delete(_tempLangFile);

            L.Initialize("en");
        }

        [TestMethod]
        public void Initialize_ExternalLanguageFile_UsesFileAndFallsBackToEnglish()
        {
            string code = "zz" + Guid.NewGuid().ToString("N").Substring(0, 4);
            Directory.CreateDirectory(L.LangFileDirectory);
            _tempLangFile = Path.Combine(L.LangFileDirectory, "lang_" + code + ".json");
            File.WriteAllText(_tempLangFile,
                "{\n  \"Lang_SelfName\": \"Test Language\",\n  \"EditMember_Title\": \"Externer Titel\"\n}",
                Encoding.UTF8);

            L.Initialize(code);

            Assert.AreEqual("Externer Titel", L.T("EditMember_Title"));
            Assert.AreEqual("Save", L.T("Btn_Save"));
        }

        [TestMethod]
        public void GetAvailableLanguages_ExternalLanguage_IsIncluded()
        {
            string code = "zz" + Guid.NewGuid().ToString("N").Substring(0, 4);
            Directory.CreateDirectory(L.LangFileDirectory);
            _tempLangFile = Path.Combine(L.LangFileDirectory, "lang_" + code + ".json");
            File.WriteAllText(_tempLangFile,
                "{\n  \"Lang_SelfName\": \"Temporary Test Language\"\n}",
                Encoding.UTF8);

            var languages = L.GetAvailableLanguages();

            Assert.IsTrue(languages.Any(x => x.Key.Equals(code, StringComparison.OrdinalIgnoreCase) && x.Value == "Temporary Test Language"));
        }
    }
}
