using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Virtuagym.API.Cache;

namespace Tests.Services
{
    /// <summary>
    /// Tests für CacheSyncScheduler: Berechnung des täglichen Sync-Zeitpunkts.
    /// </summary>
    [TestClass]
    public class CacheSyncSchedulerTests
    {
        [TestMethod]
        public void CalculateDailyDueTime_TargetInFuture_ReturnsPositiveTimeSpan()
        {
            // Zielzeit liegt in der Zukunft → DueTime muss positiv sein und < 24h.
            var targetTime = DateTime.Now.TimeOfDay.Add(TimeSpan.FromHours(2));
            if (targetTime >= TimeSpan.FromHours(24))
                targetTime = targetTime - TimeSpan.FromHours(24);

            var due = CacheSyncScheduler.CalculateDailyDueTime(targetTime);

            Assert.IsTrue(due > TimeSpan.Zero, "DueTime muss positiv sein.");
            Assert.IsTrue(due <= TimeSpan.FromHours(24), "DueTime darf nicht mehr als 24h betragen.");
        }

        [TestMethod]
        public void CalculateDailyDueTime_TargetInPast_SchedulesForTomorrow()
        {
            // Zielzeit liegt 2 Stunden in der Vergangenheit → muss für morgen geplant werden (~22h).
            var targetTime = DateTime.Now.TimeOfDay.Subtract(TimeSpan.FromHours(2));
            if (targetTime < TimeSpan.Zero)
                targetTime = targetTime + TimeSpan.FromHours(24);

            var due = CacheSyncScheduler.CalculateDailyDueTime(targetTime);

            Assert.IsTrue(due > TimeSpan.Zero, "DueTime muss positiv sein.");
            // Sollte ungefähr 22 Stunden betragen (±Toleranz für Testlaufzeit).
            Assert.IsTrue(due.TotalHours > 20 && due.TotalHours <= 24,
                $"DueTime sollte ca. 22h sein, war aber {due.TotalHours:F1}h.");
        }

        [TestMethod]
        public void CalculateDailyDueTime_SkipToday_AlwaysSchedulesTomorrow()
        {
            // Auch wenn die Zielzeit in der Zukunft liegt, muss bei skipToday=true
            // frühestens morgen geplant werden.
            var targetTime = DateTime.Now.TimeOfDay.Add(TimeSpan.FromHours(1));
            if (targetTime >= TimeSpan.FromHours(24))
                targetTime = targetTime - TimeSpan.FromHours(24);

            var due = CacheSyncScheduler.CalculateDailyDueTime(targetTime, skipToday: true);

            Assert.IsTrue(due.TotalHours > 20,
                $"Mit skipToday muss DueTime > 20h sein, war aber {due.TotalHours:F1}h.");
        }
    }
}
