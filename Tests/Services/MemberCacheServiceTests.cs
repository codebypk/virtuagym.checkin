using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Virtuagym.API.Services;
using Virtuagym.API.Cache;
using Virtuagym.API.Cache.Models;

namespace Tests.Services
{
    /// <summary>
    /// Tests für den MemberCacheService: CRUD-Operationen, Persistenz,
    /// gerätespezifische Timestamps und Offline-Checkin (Pending-Queue).
    /// </summary>
    [TestClass]
    public class MemberCacheServiceTests
    {
        private string _tempFilePath;

        [TestInitialize]
        public void Setup()
        {
            _tempFilePath = Path.Combine(Path.GetTempPath(), "test_membercache_" + Guid.NewGuid().ToString("N") + ".json");
        }

        [TestCleanup]
        public void Cleanup()
        {
            if (File.Exists(_tempFilePath))
                File.Delete(_tempFilePath);
        }

        #region Lookup

        [TestMethod]
        public void GetByRfidTag_FindsByLocalTag()
        {
            using (var cache = new MemberCacheService(_tempFilePath))
            {
                cache.Upsert(new MemberCacheEntry
                {
                    MemberId = 42,
                    RfidTag = "0009594224"
                });

                var result = cache.GetByRfidTag("0009594224");
                Assert.IsNotNull(result);
                Assert.AreEqual(42, result.MemberId);
            }
        }

        #endregion

        #region Visit-Timestamps und gerätespezifische Timestamps

        [TestMethod]
        public void UpdateVisitTimestamps_SetsGlobalTimestamps()
        {
            using (var cache = new MemberCacheService(_tempFilePath))
            {
                cache.Upsert(new MemberCacheEntry { MemberId = 1 });

                long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                cache.UpdateVisitTimestamps(1, now, 0, "device1");

                var entry = cache.GetByMemberId(1);
                var deviceData = entry.DeviceCheckins.FirstOrDefault(d => d.DeviceId == "device1");
                Assert.IsNotNull(deviceData);
                Assert.AreEqual(now, deviceData.CheckInTimestamp);
                Assert.AreEqual(0, deviceData.CheckOutTimestamp);
            }
        }

        [TestMethod]
        public void UpdateVisitTimestamps_WithDeviceId_SetsDeviceSpecificTimestamp()
        {
            using (var cache = new MemberCacheService(_tempFilePath))
            {
                cache.Upsert(new MemberCacheEntry { MemberId = 1 });

                long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                cache.UpdateVisitTimestamps(1, now, 0, "Eingang Haupttür");

                var entry = cache.GetByMemberId(1);
                Assert.IsNotNull(entry.DeviceCheckins);
                var deviceData = entry.DeviceCheckins.FirstOrDefault(d => d.DeviceId == "Eingang Haupttür");
                Assert.IsNotNull(deviceData);
                Assert.AreEqual(now, deviceData.CheckInTimestamp);
                Assert.AreEqual(0, deviceData.CheckOutTimestamp);
            }
        }

        [TestMethod]
        public void UpdateVisitTimestamps_MultipleDevices_StoresAllTimestamps()
        {
            using (var cache = new MemberCacheService(_tempFilePath))
            {
                cache.Upsert(new MemberCacheEntry { MemberId = 1 });

                long t1 = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                long t2 = t1 + 120_000; // 2 Minuten später

                cache.UpdateVisitTimestamps(1, t1, 0, "Eingang Haupttür");
                cache.UpdateVisitTimestamps(1, t2, 0, "Eingang Seitentür");

                var entry = cache.GetByMemberId(1);
                Assert.AreEqual(2, entry.DeviceCheckins.Count);
                var d1 = entry.DeviceCheckins.FirstOrDefault(d => d.DeviceId == "Eingang Haupttür");
                var d2 = entry.DeviceCheckins.FirstOrDefault(d => d.DeviceId == "Eingang Seitentür");
                Assert.IsNotNull(d1);
                Assert.IsNotNull(d2);
                Assert.AreEqual(t1, d1.CheckInTimestamp);
                Assert.AreEqual(t2, d2.CheckInTimestamp);
            }
        }

        [TestMethod]
        public void UpdateVisitTimestamps_NonExistentMember_DoesNotThrow()
        {
            using (var cache = new MemberCacheService(_tempFilePath))
            {
                // Sollte keinen Fehler werfen, auch wenn der Member nicht im Cache ist
                cache.UpdateVisitTimestamps(999, 12345, 0, "device1");
                Assert.AreEqual(0, cache.GetCount());
            }
        }

        #endregion

        #region MemberCacheEntry – Model-Logik

        [TestMethod]
        public void MemberCacheEntry_IsCheckedIn_True()
        {
            var entry = new MemberCacheEntry
            {
                DeviceCheckins = new List<DeviceCheckinData>
                {
                    new DeviceCheckinData
                    {
                        DeviceId = "device1",
                        CheckInTimestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                        CheckOutTimestamp = 0
                    }
                }
            };
            Assert.IsTrue(entry.IsCheckedIn);
        }

        [TestMethod]
        public void MemberCacheEntry_IsCheckedIn_FalseWhenCheckedOut()
        {
            var entry = new MemberCacheEntry
            {
                DeviceCheckins = new List<DeviceCheckinData>
                {
                    new DeviceCheckinData
                    {
                        DeviceId = "device1",
                        CheckInTimestamp = DateTimeOffset.UtcNow.AddHours(-1).ToUnixTimeMilliseconds(),
                        CheckOutTimestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                    }
                }
            };
            Assert.IsFalse(entry.IsCheckedIn);
        }

        [TestMethod]
        public void MemberCacheEntry_GetCheckInTimestampForDevice_ReturnsDeviceSpecific()
        {
            var entry = new MemberCacheEntry
            {
                DeviceCheckins = new List<DeviceCheckinData>
                {
                    new DeviceCheckinData { DeviceId = "device1", CheckInTimestamp = 2000, CheckOutTimestamp = 0 },
                    new DeviceCheckinData { DeviceId = "device2", CheckInTimestamp = 3000, CheckOutTimestamp = 0 }
                }
            };

            Assert.AreEqual(2000, entry.GetCheckInTimestampForDevice("device1"));
            Assert.AreEqual(3000, entry.GetCheckInTimestampForDevice("device2"));
        }

        #endregion

        #region Persistenz

        [TestMethod]
        public void Cache_PersistsAndReloads()
        {
            // Schreiben
            using (var cache = new MemberCacheService(_tempFilePath))
            {
                cache.Upsert(new MemberCacheEntry
                {
                    MemberId = 1,
                    Firstname = "Max",
                    Lastname = "Mustermann",
                    RfidTag = "TAG123"
                });
            }

            // Neuladen
            using (var cache = new MemberCacheService(_tempFilePath))
            {
                Assert.AreEqual(1, cache.GetCount());
                var entry = cache.GetByMemberId(1);
                Assert.AreEqual("Max", entry.Firstname);
                Assert.AreEqual("TAG123", entry.RfidTag);
            }
        }

        [TestMethod]
        public void Cache_DeviceTimestamps_PersistAndReload()
        {
            long ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            using (var cache = new MemberCacheService(_tempFilePath))
            {
                cache.Upsert(new MemberCacheEntry { MemberId = 1 });
                cache.UpdateVisitTimestamps(1, ts, 0, "Eingang A");
            }

            using (var cache = new MemberCacheService(_tempFilePath))
            {
                var entry = cache.GetByMemberId(1);
                Assert.IsNotNull(entry.DeviceCheckins);
                var deviceData = entry.DeviceCheckins.FirstOrDefault(d => d.DeviceId == "Eingang A");
                Assert.IsNotNull(deviceData);
                Assert.AreEqual(ts, deviceData.CheckInTimestamp);
            }
        }

        [TestMethod]
        public void Cache_CorruptFile_StartsEmpty()
        {
            File.WriteAllText(_tempFilePath, "this is not valid json {{{}}}");

            using (var cache = new MemberCacheService(_tempFilePath))
            {
                Assert.AreEqual(0, cache.GetCount());
            }
        }

        [TestMethod]
        public void Cache_DatabasePath_ReturnsConfiguredPath()
        {
            using (var cache = new MemberCacheService(_tempFilePath))
            {
                Assert.AreEqual(_tempFilePath, cache.DatabasePath);
            }
        }

        #endregion

        #region Offline Checkin (Pending Queue)

        [TestMethod]
        public void PendingCheckin_AddAndRetrieve()
        {
            using (var cache = new MemberCacheService(_tempFilePath))
            {
                long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                cache.AddPendingCheckin(42, 12345, "0009594224", "Eingang Haupttür", now);

                var pending = cache.GetPendingCheckins();
                Assert.AreEqual(1, pending.Count);
                Assert.AreEqual(42, pending[0].MemberId);
                Assert.AreEqual(12345, pending[0].UserId);
                Assert.AreEqual("0009594224", pending[0].RfidTag);
                Assert.AreEqual("Eingang Haupttür", pending[0].DeviceId);
                Assert.AreEqual(now, pending[0].Timestamp);
            }
        }

        [TestMethod]
        public void PendingCheckin_MultipleEntries()
        {
            using (var cache = new MemberCacheService(_tempFilePath))
            {
                long t1 = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                long t2 = t1 + 60_000;

                cache.AddPendingCheckin(1, 12345, "TAG_A", "device1", t1);
                cache.AddPendingCheckin(2, 67890, "TAG_B", "device2", t2);

                Assert.AreEqual(2, cache.GetPendingCheckinCount());
            }
        }

        [TestMethod]
        public void PendingCheckin_RemoveSingle()
        {
            using (var cache = new MemberCacheService(_tempFilePath))
            {
                long t1 = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                long t2 = t1 + 60_000;

                cache.AddPendingCheckin(1, 12345, "TAG_A", "device1", t1);
                cache.AddPendingCheckin(2, 67890, "TAG_B", "device2", t2);

                bool removed = cache.RemovePendingCheckin(1, t1);
                Assert.IsTrue(removed);
                Assert.AreEqual(1, cache.GetPendingCheckinCount());

                var remaining = cache.GetPendingCheckins();
                Assert.AreEqual(2, remaining[0].MemberId);
            }
        }

        [TestMethod]
        public void PendingCheckin_ClearAll()
        {
            using (var cache = new MemberCacheService(_tempFilePath))
            {
                cache.AddPendingCheckin(1, 12345, "A", "d1", 1000);
                cache.AddPendingCheckin(2, 67890, "B", "d2", 2000);

                cache.ClearPendingCheckins();

                Assert.AreEqual(0, cache.GetPendingCheckinCount());
            }
        }

        [TestMethod]
        public void PendingCheckin_PersistsAndReloads()
        {
            long ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            using (var cache = new MemberCacheService(_tempFilePath))
            {
                cache.AddPendingCheckin(42, 12345, "0009594224", "Eingang", ts);
            }

            // Neuladen
            using (var cache = new MemberCacheService(_tempFilePath))
            {
                Assert.AreEqual(1, cache.GetPendingCheckinCount());
                var pending = cache.GetPendingCheckins();
                Assert.AreEqual(42, pending[0].MemberId);
                Assert.AreEqual(ts, pending[0].Timestamp);
            }
        }

        [TestMethod]
        public void PendingCheckin_StoresCheckinKey()
        {
            using (var cache = new MemberCacheService(_tempFilePath))
            {
                long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                cache.AddPendingCheckin(42, 12345, "TAG1", "Eingang A", now, "checkin", "CS-12345-abc");

                var pending = cache.GetPendingCheckins();
                Assert.AreEqual(1, pending.Count);
                Assert.AreEqual("CS-12345-abc", pending[0].CheckinKey);
                Assert.AreEqual("checkin", pending[0].Action);
            }
        }

        [TestMethod]
        public void PendingCheckin_CheckinKey_PersistsAndReloads()
        {
            long ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            using (var cache = new MemberCacheService(_tempFilePath))
            {
                cache.AddPendingCheckin(42, 12345, "TAG1", "Eingang A", ts, "checkout", "CS-99999-xyz");
            }

            // Neuladen
            using (var cache = new MemberCacheService(_tempFilePath))
            {
                var pending = cache.GetPendingCheckins();
                Assert.AreEqual(1, pending.Count);
                Assert.AreEqual("CS-99999-xyz", pending[0].CheckinKey);
                Assert.AreEqual("checkout", pending[0].Action);
                Assert.AreEqual("Eingang A", pending[0].DeviceId);
            }
        }

        #endregion
    }
}
