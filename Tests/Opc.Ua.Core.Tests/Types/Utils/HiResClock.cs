/* ========================================================================
 * Copyright (c) 2005-2018 The OPC Foundation, Inc. All rights reserved.
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

using System;
using System.Diagnostics;
using NUnit.Framework;

namespace Opc.Ua.Core.Tests.Types.UtilsTests
{
    /// <summary>
    /// Tests for the BuiltIn Types.
    /// </summary>
    [TestFixture, Category("Utils")]
    [SetCulture("en-us"), SetUICulture("en-us")]
    public class HiResClockTests
    {
        /// <summary>
        /// How long the tests are running.
        /// </summary>
        public const int HiResClockTestDuration = 2000;

        #region Test Setup
        [OneTimeTearDown]
        protected void OneTimeTearDown()
        {
            HiResClock.Disabled = false;
        }

        [TearDown]
        protected void TearDown()
        {
            HiResClock.Disabled = false;
        }
        #endregion

        #region Test Methods
        /// <summary>
        /// Validate HiResClock defaults, platform dependant.
        /// </summary>
        [Test, Order(100)]
        public void HiResParameters()
        {
            Assert.LessOrEqual(1.0, HiResClock.TicksPerMillisecond);
            Assert.LessOrEqual(1000, HiResClock.Frequency);
            Assert.False(HiResClock.Disabled);
            Assert.LessOrEqual(1.0, TimeSpan.TicksPerMillisecond);
            HiResClock.Disabled = true;
            Assert.True(HiResClock.Disabled);
            Assert.AreEqual(TimeSpan.TicksPerSecond, HiResClock.Frequency);
            Assert.AreEqual(TimeSpan.TicksPerMillisecond, HiResClock.TicksPerMillisecond);
            HiResClock.Disabled = false;
            Assert.False(HiResClock.Disabled);
        }

        /// <summary>
        /// Validate tick counts forward only and has at least one tick per millsecond resolution.
        /// </summary>
        [Theory, Order(200)]
        public void HiResClockTickCount(bool disabled)
        {
            HiResClock.Disabled = disabled;
            Assert.AreEqual(disabled, HiResClock.Disabled);
            Stopwatch stopWatch = new Stopwatch();
            stopWatch.Start();
            long lastTickCount = HiResClock.TickCount64;
            long firstTickCount = lastTickCount;
            int counts = 0;
            while (stopWatch.ElapsedMilliseconds <= HiResClockTestDuration)
            {
                long tickCount;
                do
                {
                    tickCount = HiResClock.TickCount64;
                }
                while (tickCount == lastTickCount);
                Assert.LessOrEqual(lastTickCount, tickCount);
                lastTickCount = tickCount;
                counts++;
            }
            Assert.LessOrEqual(1000, counts);
            stopWatch.Stop();
            long elapsed = lastTickCount - firstTickCount;
            TestContext.Out.WriteLine("HiResClock counts: {0} resolution: {1}µs", counts, stopWatch.ElapsedMilliseconds * 1000 / counts);
            // test accuracy of counter vs. stop watch
            Assert.That(elapsed, Is.EqualTo(stopWatch.ElapsedMilliseconds).Within(2).Percent);
        }

        /// <summary>
        /// Validate DateTime.UtcNow counts forward and has a high resolution.
        /// </summary>
        [Theory, Order(300)]
        public void HiResUtcNowTickCount(bool disabled)
        {
            HiResClock.Disabled = disabled;
            Assert.AreEqual(disabled, HiResClock.Disabled);
            Stopwatch stopWatch = new Stopwatch();
            stopWatch.Start();
            long lastTickCount = HiResClock.UtcNow.Ticks;
            long firstTickCount = lastTickCount;
            int counts = 0;
            while (stopWatch.ElapsedMilliseconds <= HiResClockTestDuration)
            {
                long tickCount;
                do
                {
                    tickCount = HiResClock.UtcNow.Ticks;
                }
                while (tickCount == lastTickCount);
                Assert.LessOrEqual(lastTickCount, tickCount);
                lastTickCount = tickCount;
                counts++;
            }
            Assert.LessOrEqual(1000, counts);
            stopWatch.Stop();
            long elapsed = (lastTickCount - firstTickCount) / TimeSpan.TicksPerMillisecond;
            TestContext.Out.WriteLine("HiResClock counts: {0} resolution: {1}µs", counts, stopWatch.ElapsedMilliseconds * 1000 / counts);
            // test accuracy of counter vs. stop watch
            Assert.That(elapsed, Is.EqualTo(stopWatch.ElapsedMilliseconds).Within(2).Percent);
        }
        #endregion
    }
}
