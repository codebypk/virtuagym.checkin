using System;
using System.IO;
using System.Linq;
using Virtuagym.CheckIn.Core.Helper;

namespace Virtuagym.CheckIn.Core.Services
{
    /// <summary>
    /// Cleans up old log files from the log directory.
    /// Called on application startup.
    /// </summary>
    public static class LogCleanupService
    {
        /// <summary>
        /// Deletes log files older than the specified number of days.
        /// </summary>
        /// <param name="maxAgeDays">Maximum age in days (must be > 0).</param>
        /// <returns>Number of deleted files.</returns>
        public static int CleanupOldLogs(int maxAgeDays)
        {
            if (maxAgeDays <= 0)
                return 0;
            try
            {
                if (!Directory.Exists(Constants.LogFolder))
                    return 0;

                var cutoff = DateTime.Now.AddDays(-maxAgeDays);
                var oldFiles = Directory.GetFiles(Constants.LogFolder, "*" + Constants.LogFileSuffix)
                    .Select(f => new FileInfo(f))
                    .Where(f => f.LastWriteTime < cutoff)
                    .ToArray();

                int deleted = 0;
                foreach (var file in oldFiles)
                {
                    try
                    {
                        file.Delete();
                        deleted++;
                    }
                    catch
                    {
                        // File may be locked – ignore
                    }
                }

                return deleted;
            }
            catch
            {
                return 0;
            }
        }
    }
}
