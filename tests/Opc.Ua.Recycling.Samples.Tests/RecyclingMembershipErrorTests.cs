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
using System.IO;
using NUnit.Framework;

namespace Opc.Ua.Recycling.Samples.Tests
{
    /// <summary>
    /// Keeps planned coordinator shutdown recoverable without hiding authorization or model faults.
    /// </summary>
    [TestFixture]
    public sealed class RecyclingMembershipErrorTests
    {
        /// <summary>
        /// Classifies the exact protocol failure rather than retrying every service error.
        /// </summary>
        [TestCaseSource(nameof(ServiceFailures))]
        public void CoordinatorShutdownIsRecoverableButProtocolRejectionIsTerminal(StatusCode status, bool retry)
        {
            Assert.That(RecyclingRuntime.IsTransportFailure(new ServiceResultException(status)), Is.EqualTo(retry));
        }

        /// <summary>
        /// I/O interruption is recoverable; application bugs are not transport failures.
        /// </summary>
        [Test]
        public void ApplicationFailuresAreNotHiddenByReconnect()
        {
            Assert.Multiple(() =>
            {
                Assert.That(RecyclingRuntime.IsTransportFailure(new IOException("Connection interrupted.")), Is.True);
                Assert.That(
                    RecyclingRuntime.IsTransportFailure(new InvalidOperationException("Missing native binding.")),
                    Is.False);
            });
        }

        /// <summary>
        /// Supplies both temporary connection states and failures that must remain visible.
        /// </summary>
        private static IEnumerable<TestCaseData> ServiceFailures()
        {
            yield return new TestCaseData(StatusCodes.BadServerHalted, true);
            yield return new TestCaseData(StatusCodes.BadShutdown, true);
            yield return new TestCaseData(StatusCodes.BadSecureChannelClosed, true);
            yield return new TestCaseData(StatusCodes.BadNoCommunication, true);
            yield return new TestCaseData(StatusCodes.BadUserAccessDenied, false);
            yield return new TestCaseData(StatusCodes.BadNotFound, false);
            yield return new TestCaseData(StatusCodes.BadInvalidArgument, false);
            yield return new TestCaseData(StatusCodes.BadConfigurationError, false);
        }
    }
}
