using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Virtuagym.CheckIn.Core.Helper;
using Virtuagym.CheckIn.Core.Services;

namespace Tests.Services
{
    [TestClass]
    [DoNotParallelize]
    public class LogCleanupServiceTests
    {
        private string _originalCurrentDirectory;
        private string _tempDirectory;

        [TestInitialize]
        public void Setup()
        {
            _originalCurrentDirectory = Directory.GetCurrentDirectory();
            _tempDirectory = Path.Combine(Path.GetTempPath(), "LogCleanupTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDirectory);
            Directory.SetCurrentDirectory(_tempDirectory);
            Directory.CreateDirectory(Constants.LogFolder);
        }

        [TestCleanup]
        public void Cleanup()
        {
            Directory.SetCurrentDirectory(_originalCurrentDirectory);
            if (Directory.Exists(_tempDirectory))
                Directory.Delete(_tempDirectory, true);
        }

        [TestMethod]
        public void CleanupOldLogs_RemovesOnlyOldMatchingFiles()
        {
            string oldLog = Path.Combine(Constants.LogFolder, "old" + Constants.LogFileSuffix);
            string recentLog = Path.Combine(Constants.LogFolder, "recent" + Constants.LogFileSuffix);
            string otherFile = Path.Combine(Constants.LogFolder, "keep.txt");

            File.WriteAllText(oldLog, "old");
            File.WriteAllText(recentLog, "recent");
            File.WriteAllText(otherFile, "other");
            File.SetLastWriteTime(oldLog, DateTime.Now.AddDays(-40));
            File.SetLastWriteTime(recentLog, DateTime.Now.AddDays(-2));
            File.SetLastWriteTime(otherFile, DateTime.Now.AddDays(-40));

            int deleted = LogCleanupService.CleanupOldLogs(30);

            Assert.AreEqual(1, deleted);
            Assert.IsFalse(File.Exists(oldLog));
            Assert.IsTrue(File.Exists(recentLog));
            Assert.IsTrue(File.Exists(otherFile));
        }
    }
}
