// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using NUnit.Framework;

namespace Opc.Ua.Core.Tests.Types.Constants
{
    /// <summary>
    /// Tests for the OPC UA ServiceLevel subrange helpers.
    /// </summary>
    [TestFixture]
    [Category("ServiceLevel")]
    [Parallelizable]
    public sealed class ServiceLevelsTests
    {
        /// <summary>
        /// Verifies the maintenance and no-data singleton boundaries.
        /// </summary>
        [Test]
        public void GetSubrangeClassifiesMaintenanceAndNoDataBoundaries()
        {
            Assert.That(ServiceLevels.GetSubrange(ServiceLevels.Maintenance), Is.EqualTo(ServiceLevelSubrange.Maintenance));
            Assert.That(ServiceLevels.IsMaintenance(ServiceLevels.Maintenance), Is.True);
            Assert.That(ServiceLevels.GetSubrange(ServiceLevels.NoData), Is.EqualTo(ServiceLevelSubrange.NoData));
            Assert.That(ServiceLevels.IsNoData(ServiceLevels.NoData), Is.True);
            Assert.That(ServiceLevels.IsOperational(ServiceLevels.NoData), Is.False);
        }

        /// <summary>
        /// Verifies degraded subrange boundaries.
        /// </summary>
        [Test]
        public void GetSubrangeClassifiesDegradedBoundaries()
        {
            Assert.That(
                ServiceLevels.GetSubrange(ServiceLevels.DegradedMinimum),
                Is.EqualTo(ServiceLevelSubrange.Degraded));
            Assert.That(
                ServiceLevels.GetSubrange(ServiceLevels.DegradedMaximum),
                Is.EqualTo(ServiceLevelSubrange.Degraded));
            Assert.That(ServiceLevels.IsDegraded(ServiceLevels.DegradedMinimum), Is.True);
            Assert.That(ServiceLevels.IsOperational(ServiceLevels.DegradedMaximum), Is.True);
        }

        /// <summary>
        /// Verifies healthy subrange boundaries.
        /// </summary>
        [Test]
        public void GetSubrangeClassifiesHealthyBoundaries()
        {
            Assert.That(
                ServiceLevels.GetSubrange(ServiceLevels.HealthyMinimum),
                Is.EqualTo(ServiceLevelSubrange.Healthy));
            Assert.That(
                ServiceLevels.GetSubrange(ServiceLevels.Maximum),
                Is.EqualTo(ServiceLevelSubrange.Healthy));
            Assert.That(ServiceLevels.IsHealthy(ServiceLevels.Maximum), Is.True);
            Assert.That(ServiceLevels.IsOperational(ServiceLevels.HealthyMinimum), Is.True);
        }
    }
}
