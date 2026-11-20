// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

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
            yield return new TestCaseData(StatusCodes.BadTimeout, true);
            yield return new TestCaseData(StatusCodes.BadRequestTimeout, true);
            yield return new TestCaseData(StatusCodes.BadUserAccessDenied, false);
            yield return new TestCaseData(StatusCodes.BadNotFound, false);
            yield return new TestCaseData(StatusCodes.BadInvalidArgument, false);
            yield return new TestCaseData(StatusCodes.BadConfigurationError, false);
        }
    }
}
