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

using System.Threading.Tasks;
using NUnit.Framework;

namespace Opc.Ua.Core.Tests.Types.UtilsTests
{
    /// <summary>
    /// Tests for numeric identifier utility methods in <see cref="Utils"/>.
    /// </summary>
    [TestFixture]
    [Category("Utils")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class UtilsIdentifierTests
    {
        #region IncrementIdentifier

        /// <summary>
        /// IncrementIdentifier increments a uint identifier correctly.
        /// </summary>
        [Test]
        public void IncrementIdentifierUIntIncrementsValue()
        {
            uint id = 5u;
            uint result = Utils.IncrementIdentifier(ref id);
            Assert.That(result, Is.EqualTo(6u));
            Assert.That(id, Is.EqualTo(6u));
        }

        /// <summary>
        /// IncrementIdentifier for uint wraps around uint.MaxValue and skips zero.
        /// </summary>
        [Test]
        public void IncrementIdentifierUIntWrapsAroundAndSkipsZero()
        {
            uint id = uint.MaxValue;
            uint result = Utils.IncrementIdentifier(ref id);
            // After MaxValue wraps to 0 (prohibited), it should become 1
            Assert.That(result, Is.EqualTo(1u));
        }

        /// <summary>
        /// IncrementIdentifier increments an int identifier correctly.
        /// </summary>
        [Test]
        public void IncrementIdentifierIntIncrementsValue()
        {
            int id = 5;
            int result = Utils.IncrementIdentifier(ref id);
            Assert.That(result, Is.EqualTo(6));
            Assert.That(id, Is.EqualTo(6));
        }

        /// <summary>
        /// IncrementIdentifier for int wraps around int.MaxValue and skips zero.
        /// </summary>
        [Test]
        public void IncrementIdentifierIntWrapsAroundAndSkipsZero()
        {
            int id = int.MaxValue;
            int result = Utils.IncrementIdentifier(ref id);
            // After MaxValue wraps to int.MinValue, increments continue until non-zero
            Assert.That(result, Is.Not.EqualTo(0));
        }

        /// <summary>
        /// IncrementIdentifier for int skips zero — starting from -1 it jumps to 1.
        /// </summary>
        [Test]
        public void IncrementIdentifierIntFromMinusOneSkipsZero()
        {
            int id = -1;
            int result = Utils.IncrementIdentifier(ref id);
            Assert.That(result, Is.EqualTo(1));
        }

        #endregion

        #region SetIdentifierToAtLeast

        /// <summary>
        /// SetIdentifierToAtLeast updates the identifier when it is below the lower limit.
        /// </summary>
        [Test]
        public void SetIdentifierToAtLeastBelowLimitUpdatesIdentifier()
        {
            uint id = 5u;
            uint result = Utils.SetIdentifierToAtLeast(ref id, 10u);
            Assert.That(result, Is.EqualTo(5u)); // returns the old value
            Assert.That(id, Is.EqualTo(10u));     // identifier is updated
        }

        /// <summary>
        /// SetIdentifierToAtLeast does not change the identifier when it already meets the limit.
        /// </summary>
        [Test]
        public void SetIdentifierToAtLeastAtLimitDoesNotChange()
        {
            uint id = 10u;
            uint result = Utils.SetIdentifierToAtLeast(ref id, 10u);
            Assert.That(result, Is.EqualTo(10u));
            Assert.That(id, Is.EqualTo(10u));
        }

        /// <summary>
        /// SetIdentifierToAtLeast does not change the identifier when it is above the lower limit.
        /// </summary>
        [Test]
        public void SetIdentifierToAtLeastAboveLimitDoesNotChange()
        {
            uint id = 20u;
            uint result = Utils.SetIdentifierToAtLeast(ref id, 10u);
            Assert.That(result, Is.EqualTo(20u));
            Assert.That(id, Is.EqualTo(20u));
        }

        #endregion

        #region SetIdentifier

        /// <summary>
        /// SetIdentifier updates the identifier to the new value and returns the old value.
        /// </summary>
        [Test]
        public void SetIdentifierReturnsOldValueAndSetsNew()
        {
            uint id = 5u;
            uint old = Utils.SetIdentifier(ref id, 42u);
            Assert.That(old, Is.EqualTo(5u));
            Assert.That(id, Is.EqualTo(42u));
        }

        /// <summary>
        /// SetIdentifier called concurrently by multiple threads produces consistent results.
        /// </summary>
        [Test]
        public void SetIdentifierConcurrentCallsProduceConsistentResults()
        {
            uint id = 0u;
            const int threadCount = 10;
            var tasks = new Task[threadCount];
            for (int i = 0; i < threadCount; i++)
            {
                uint value = (uint)(i + 1);
                tasks[i] = Task.Run(() => Utils.SetIdentifier(ref id, value));
            }
            Task.WaitAll(tasks);
            // After all threads have run, id must be one of 1..threadCount
            Assert.That(id, Is.GreaterThan(0u).And.LessThanOrEqualTo((uint)threadCount));
        }

        #endregion

        #region ToInt32 / ToUInt32

        /// <summary>
        /// ToInt32 converts values in the positive int range correctly.
        /// </summary>
        [Test]
        public void ToInt32PositiveValueIsUnchanged()
        {
            Assert.That(Utils.ToInt32(0u), Is.EqualTo(0));
            Assert.That(Utils.ToInt32(1u), Is.EqualTo(1));
            Assert.That(Utils.ToInt32((uint)int.MaxValue), Is.EqualTo(int.MaxValue));
        }

        /// <summary>
        /// ToInt32 converts values above int.MaxValue to negative ints.
        /// </summary>
        [Test]
        public void ToInt32ValueAboveIntMaxBecomesNegative()
        {
            int result = Utils.ToInt32(uint.MaxValue);
            Assert.That(result, Is.EqualTo(-1));
        }

        /// <summary>
        /// ToUInt32 converts non-negative int values to identical uint values.
        /// </summary>
        [Test]
        public void ToUInt32PositiveValueIsUnchanged()
        {
            Assert.That(Utils.ToUInt32(0), Is.EqualTo(0u));
            Assert.That(Utils.ToUInt32(1), Is.EqualTo(1u));
            Assert.That(Utils.ToUInt32(int.MaxValue), Is.EqualTo((uint)int.MaxValue));
        }

        /// <summary>
        /// ToUInt32 converts negative int values to large uint values.
        /// </summary>
        [Test]
        public void ToUInt32NegativeValueBecomesLargeUInt()
        {
            Assert.That(Utils.ToUInt32(-1), Is.EqualTo(uint.MaxValue));
        }

        /// <summary>
        /// ToInt32 and ToUInt32 round-trip correctly for a range of values.
        /// </summary>
        [Test]
        public void ToInt32ToUInt32RoundTrip()
        {
            foreach (uint value in new uint[] { 0u, 1u, (uint)int.MaxValue, (uint)int.MaxValue + 1u, uint.MaxValue })
            {
                int asInt = Utils.ToInt32(value);
                uint backToUInt = Utils.ToUInt32(asInt);
                Assert.That(backToUInt, Is.EqualTo(value), $"Round-trip failed for {value}");
            }
        }

        #endregion
    }
}
