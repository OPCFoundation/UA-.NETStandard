/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person
 * obtaining a copy of this software and associated documentation
 * files (the "Software"), to deal in the Software without
 * restriction, including without limitation the rights to use,
 * copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following
 * conditions:
 *
 * The above copyright notice and this permission notice shall be
 * included in all copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
 * EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES
 * OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
 * NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
 * HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
 * WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
 * OTHER DEALINGS IN THE SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

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
