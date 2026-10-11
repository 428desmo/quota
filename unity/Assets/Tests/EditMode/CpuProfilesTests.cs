using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace Quota.Tests
{
    public class CpuProfilesTests
    {
        [Test]
        public void SixProfilesAreDistinctAndNominatableWithoutDuplication()
        {
            Assert.AreEqual(6, CpuProfiles.Names.Length);
            var used = new HashSet<int>();
            var rng = new Random(14);
            for (var i = 0; i < 4; i++)
            {
                var id = CpuProfiles.PickFresh(rng, used);
                Assert.That(id, Is.InRange(0, 5));
                Assert.IsFalse(string.IsNullOrEmpty(CpuProfiles.Names[id]));
            }
            Assert.AreEqual(4, used.Count);
        }
    }
}
