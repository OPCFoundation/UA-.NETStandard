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

using System;
using NUnit.Framework;
using Opc.Ua.Perf.ServerLoadHarness;

namespace Opc.Ua.Client.Tests.Perf
{
    /// <summary>
    /// Checks the delivery fields emitted by the load harness.
    /// </summary>
    [TestFixture]
    [Parallelizable(ParallelScope.All)]
    [SetCulture("de-DE")]
    public sealed class DeliveryReportTests
    {
        /// <summary>
        /// Reports the measured counts and percentage using invariant formatting.
        /// </summary>
        [TestCase(0L, 0L, 50, 15, "received/expected=0.0 % received=0 expected=0")]
        [TestCase(0L, 4L, 0, 15, "received/expected=0.0 % received=0 expected=0")]
        [TestCase(0L, 4L, 50, 0, "received/expected=0.0 % received=0 expected=0")]
        [TestCase(0L, long.MaxValue, 2, 0, "received/expected=0.0 % received=0 expected=0")]
        [TestCase(3L, 0L, 1, 1, "received/expected=0.0 % received=3 expected=0")]
        [TestCase(1L, 1L, 1, 1, "received/expected=100.0 % received=1 expected=1")]
        [TestCase(45L, 4L, 5, 3, "received/expected=75.0 % received=45 expected=60")]
        [TestCase(90L, 4L, 2, 10, "received/expected=112.5 % received=90 expected=80")]
        [TestCase(9299L, 10000L, 1, 1, "received/expected=93.0 % received=9299 expected=10000")]
        [TestCase(1L, 1000000000L, 1000, 1000,
            "received/expected=0.0 % received=1 expected=1000000000000000")]
        [TestCase(0L, 1L, int.MaxValue, int.MaxValue,
            "received/expected=0.0 % received=0 expected=4611686014132420609")]
        public void FormatsCountsAndPercentage(
            long received,
            long produced,
            int sessions,
            int subscriptions,
            string expected)
        {
            Assert.That(DeliveryReport.Format(received, produced, sessions, subscriptions), Is.EqualTo(expected));
        }

        /// <summary>
        /// Rejects negative counts instead of reporting an invalid delivery percentage.
        /// </summary>
        [TestCase(-1L, 0L, 0, 0, "received")]
        [TestCase(0L, -1L, 0, 0, "produced")]
        [TestCase(0L, 0L, -1, 0, "sessions")]
        [TestCase(0L, 0L, 0, -1, "subscriptions")]
        public void RejectsNegativeCounts(
            long received,
            long produced,
            int sessions,
            int subscriptions,
            string parameter)
        {
            ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(
                () => DeliveryReport.Format(received, produced, sessions, subscriptions))!;

            Assert.That(exception.ParamName, Is.EqualTo(parameter));
        }

        /// <summary>
        /// Rejects an expected count that cannot be represented.
        /// </summary>
        [TestCase(long.MaxValue, 2, 1)]
        [TestCase(3L, int.MaxValue, int.MaxValue)]
        public void RejectsExpectedCountOverflow(long produced, int sessions, int subscriptions)
        {
            Assert.Throws<OverflowException>(() => DeliveryReport.Format(0, produced, sessions, subscriptions));
        }
    }
}
