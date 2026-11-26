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
using System.Collections.Generic;
using NUnit.Framework;
using Opc.Ua.Perf.ServerLoadHarness;

namespace Opc.Ua.Client.Tests.Perf
{
    /// <summary>
    /// Checks the process CPU fields emitted and consumed by the load harness.
    /// </summary>
    [TestFixture]
    [Parallelizable(ParallelScope.All)]
    [SetCulture("de-DE")]
    public sealed class ProcessCpuReportTests
    {
        /// <summary>
        /// Preserves raw TimeSpan ticks alongside the legacy rounded millisecond field.
        /// </summary>
        [TestCase(0L, "cpuMs=0 cpuTicks=0")]
        [TestCase(1234L, "cpuMs=0 cpuTicks=1234")]
        [TestCase(123456L, "cpuMs=12 cpuTicks=123456")]
        public void FormatsCpuTime(long ticks, string expected)
        {
            Assert.That(ProcessCpuReport.Format(TimeSpan.FromTicks(ticks)), Is.EqualTo(expected));
        }

        /// <summary>
        /// CPU time is read from ticks rather than the rounded compatibility field.
        /// </summary>
        [Test]
        public void PrefersTicksOverRoundedMilliseconds()
        {
            var fields = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["cpuMs"] = "0",
                ["cpuTicks"] = "1234"
            };

            Assert.That(ProcessCpuReport.GetMilliseconds(fields), Is.EqualTo(0.1234).Within(0.000000000001));
        }

        /// <summary>
        /// A legacy report without ticks retains its invariant millisecond value.
        /// </summary>
        [Test]
        public void ReadsLegacyMilliseconds()
        {
            var fields = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["cpuMs"] = "12.5"
            };

            Assert.That(ProcessCpuReport.GetMilliseconds(fields), Is.EqualTo(12.5));
        }

        /// <summary>
        /// Legacy CPU input must remain finite and nonnegative.
        /// </summary>
        [TestCase("-1")]
        [TestCase("NaN")]
        public void RejectsInvalidLegacyMilliseconds(string milliseconds)
        {
            var fields = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["cpuMs"] = milliseconds
            };

            Assert.Throws<FormatException>(() => ProcessCpuReport.GetMilliseconds(fields));
        }

        /// <summary>
        /// A CPU report cannot be read from a missing field collection.
        /// </summary>
        [Test]
        public void RejectsMissingFields()
        {
            Assert.Throws<ArgumentNullException>(() => ProcessCpuReport.GetMilliseconds(null!));
        }

        /// <summary>
        /// Invalid ticks are not replaced with the rounded compatibility value.
        /// </summary>
        [TestCase("")]
        [TestCase("-1")]
        [TestCase("invalid")]
        public void RejectsMalformedTicks(string ticks)
        {
            var fields = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["cpuMs"] = "1",
                ["cpuTicks"] = ticks
            };

            Assert.Throws<FormatException>(() => ProcessCpuReport.GetMilliseconds(fields));
        }

        /// <summary>
        /// An out-of-range tick count is not interpreted as a valid report.
        /// </summary>
        [Test]
        public void RejectsTickOverflow()
        {
            var fields = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["cpuMs"] = "1",
                ["cpuTicks"] = "9223372036854775808"
            };

            Assert.Throws<OverflowException>(() => ProcessCpuReport.GetMilliseconds(fields));
        }

        /// <summary>
        /// Per-operation CPU retains sub-millisecond input and uses invariant formatting.
        /// </summary>
        [TestCase(0.0, 100.0, "0.000000")]
        [TestCase(0.1234, 100.0, "1.234000")]
        [TestCase(0.1235, 100.0, "1.235000")]
        public void FormatsPerOperation(double milliseconds, double operations, string expected)
        {
            Assert.That(ProcessCpuReport.FormatPerOperation(milliseconds, operations), Is.EqualTo(expected));
        }

        /// <summary>
        /// Invalid CPU measurements and operation counts are rejected.
        /// </summary>
        [TestCase(-1.0, 1.0, "milliseconds")]
        [TestCase(double.NaN, 1.0, "milliseconds")]
        [TestCase(double.PositiveInfinity, 1.0, "milliseconds")]
        [TestCase(1.0, 0.0, "operations")]
        [TestCase(1.0, -1.0, "operations")]
        [TestCase(1.0, double.NaN, "operations")]
        [TestCase(1.0, double.PositiveInfinity, "operations")]
        public void RejectsInvalidPerOperationInputs(double milliseconds, double operations, string parameter)
        {
            ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(
                () => ProcessCpuReport.FormatPerOperation(milliseconds, operations))!;

            Assert.That(exception.ParamName, Is.EqualTo(parameter));
        }

        /// <summary>
        /// A negative duration cannot be reported as process CPU time.
        /// </summary>
        [Test]
        public void RejectsNegativeCpuTime()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => ProcessCpuReport.Format(TimeSpan.FromTicks(-1)));
        }

        /// <summary>
        /// A per-operation value that overflows is not printed as a measurement.
        /// </summary>
        [Test]
        public void RejectsPerOperationOverflow()
        {
            Assert.Throws<OverflowException>(() => ProcessCpuReport.FormatPerOperation(double.MaxValue, 1));
        }
    }
}
