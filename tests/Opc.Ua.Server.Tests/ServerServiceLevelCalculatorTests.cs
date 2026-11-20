// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using NUnit.Framework;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// Tests for load-based Server.ServiceLevel calculation.
    /// </summary>
    [TestFixture]
    public class ServerServiceLevelCalculatorTests
    {
        /// <summary>
        /// Verifies that empty and lightly loaded servers keep advertising full capacity.
        /// </summary>
        [TestCase(0, 100)]
        [TestCase(20, 100)]
        [TestCase(5, 0)]
        public void CalculateTargetReturnsFullServiceLevelForLowOrUnlimitedLoad(
            int currentSessionCount,
            int maxSessionCount)
        {
            byte serviceLevel = ServerServiceLevelCalculator.CalculateTarget(
                currentSessionCount,
                maxSessionCount);

            Assert.That(serviceLevel, Is.EqualTo(255));
        }

        /// <summary>
        /// Verifies that the advertised value falls as session capacity is consumed.
        /// </summary>
        [Test]
        public void CalculateTargetScalesDownAfterLowLoadBand()
        {
            byte halfLoadedServiceLevel = ServerServiceLevelCalculator.CalculateTarget(50, 100);
            byte nearCapacityServiceLevel = ServerServiceLevelCalculator.CalculateTarget(90, 100);

            Assert.That(halfLoadedServiceLevel, Is.LessThan(255));
            Assert.That(halfLoadedServiceLevel, Is.GreaterThan(nearCapacityServiceLevel));
            Assert.That(nearCapacityServiceLevel, Is.GreaterThanOrEqualTo(200));
        }

        /// <summary>
        /// Verifies that a full server remains operational and stays in the healthy range.
        /// </summary>
        [Test]
        public void CalculateTargetReturnsFloorAtOrAboveCapacity()
        {
            Assert.That(ServerServiceLevelCalculator.CalculateTarget(100, 100), Is.EqualTo(200));
            Assert.That(ServerServiceLevelCalculator.CalculateTarget(101, 100), Is.EqualTo(200));
        }

        /// <summary>
        /// Verifies that hysteresis suppresses small intermediate movements.
        /// </summary>
        [Test]
        public void ShouldUpdateSuppressesSmallIntermediateChanges()
        {
            Assert.That(ServerServiceLevelCalculator.ShouldUpdate(200, 203), Is.False);
            Assert.That(ServerServiceLevelCalculator.ShouldUpdate(200, 205), Is.True);
        }

        /// <summary>
        /// Verifies that hysteresis never delays returning to full or minimum healthy ServiceLevel.
        /// </summary>
        [Test]
        public void ShouldUpdateAlwaysPublishesExtrema()
        {
            Assert.That(ServerServiceLevelCalculator.ShouldUpdate(254, 255), Is.True);
            Assert.That(ServerServiceLevelCalculator.ShouldUpdate(200, 200), Is.False);
            Assert.That(ServerServiceLevelCalculator.ShouldUpdate(255, 255), Is.False);
        }
    }
}
