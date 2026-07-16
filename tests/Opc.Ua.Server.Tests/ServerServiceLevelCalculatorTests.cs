/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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
            Assert.That(nearCapacityServiceLevel, Is.GreaterThan(1));
        }

        /// <summary>
        /// Verifies that a full server remains discoverable but advertises minimal headroom.
        /// </summary>
        [Test]
        public void CalculateTargetReturnsFloorAtOrAboveCapacity()
        {
            Assert.That(ServerServiceLevelCalculator.CalculateTarget(100, 100), Is.EqualTo(1));
            Assert.That(ServerServiceLevelCalculator.CalculateTarget(101, 100), Is.EqualTo(1));
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
        /// Verifies that hysteresis never delays returning to full or minimal ServiceLevel.
        /// </summary>
        [Test]
        public void ShouldUpdateAlwaysPublishesExtrema()
        {
            Assert.That(ServerServiceLevelCalculator.ShouldUpdate(254, 255), Is.True);
            Assert.That(ServerServiceLevelCalculator.ShouldUpdate(2, 1), Is.True);
            Assert.That(ServerServiceLevelCalculator.ShouldUpdate(255, 255), Is.False);
        }
    }
}
