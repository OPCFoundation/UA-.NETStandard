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

using System;
using System.Globalization;
using NUnit.Framework;
using Opc.Ua;
using UaLens.Views;

namespace UaLens.Tests.Presentation
{
    [TestFixture]
    public sealed class MonitoredItemValidationTests
    {
        [TestCase("abc")]
        [TestCase("")]
        [TestCase("-2")]
        [TestCase("-0.5")]
        [TestCase("NaN")]
        [TestCase("Infinity")]
        [TestCase("3600001")]
        [SetCulture("en-US")]
        public void InvalidSamplingNamesFieldAndAcceptedRange(string input)
        {
            Assert.That(MonitoredItemValidation.TrySampling(input, out _, out string error), Is.False);
            Assert.That(error, Does.Contain("Sampling interval (ms)").And.Contain("3600000").And.Contain("-1"));
        }

        [TestCase(-1)]
        [TestCase(0)]
        [TestCase(0.5)]
        [TestCase(3600000)]
        public void ValidSamplingPreservesBoundaries(double value)
        {
            Assert.That(MonitoredItemValidation.TrySampling(
                value.ToString(CultureInfo.CurrentCulture), out TimeSpan interval, out string error), Is.True);
            Assert.That(interval.TotalMilliseconds, Is.EqualTo(value));
            Assert.That(error, Is.Empty);
        }

        [TestCase("abc", DeadbandType.Absolute)]
        [TestCase("-1", DeadbandType.Absolute)]
        [TestCase("NaN", DeadbandType.Absolute)]
        [TestCase("Infinity", DeadbandType.Absolute)]
        [TestCase("101", DeadbandType.Percent)]
        [TestCase("-0.1", DeadbandType.Percent)]
        [SetCulture("en-US")]
        public void InvalidDeadbandNamesFieldAndRange(string input, DeadbandType type)
        {
            Assert.That(MonitoredItemValidation.TryDeadband(input, type, out _, out string error), Is.False);
            Assert.That(error, Does.Contain("Deadband value").And.Contain("finite"));
            if (type == DeadbandType.Percent)
            {
                Assert.That(error, Does.Contain("0 to 100"));
            }
        }

        [TestCase("0", DeadbandType.Percent, 0)]
        [TestCase("100", DeadbandType.Percent, 100)]
        [TestCase("0", DeadbandType.Absolute, 0)]
        [TestCase("101", DeadbandType.Absolute, 101)]
        [TestCase("unused", DeadbandType.None, 0)]
        public void ValidDeadbandBoundariesAndDisabledInput(string input, DeadbandType type, double expected)
        {
            Assert.That(MonitoredItemValidation.TryDeadband(input, type, out double value, out string error), Is.True);
            Assert.That(value, Is.EqualTo(expected));
            Assert.That(error, Is.Empty);
        }

        [Test]
        [SetCulture("de-DE")]
        public void SharedValidationUsesCurrentDecimalSeparator()
        {
            Assert.That(MonitoredItemValidation.TrySampling("1,5", out TimeSpan interval, out _), Is.True);
            Assert.That(interval.TotalMilliseconds, Is.EqualTo(1.5));
            Assert.That(MonitoredItemValidation.TryDeadband("0,5", DeadbandType.Percent, out double value, out _), Is.True);
            Assert.That(value, Is.EqualTo(0.5));
        }
    }
}
