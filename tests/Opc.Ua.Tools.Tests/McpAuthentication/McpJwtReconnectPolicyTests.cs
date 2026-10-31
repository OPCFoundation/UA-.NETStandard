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

#if NET10_0
using System;
using System.Threading;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Mcp;

namespace Opc.Ua.Tools.Tests.McpAuthentication
{
    /// <summary>
    /// Verifies JWT retry termination without changing transient-failure backoff.
    /// </summary>
    [TestFixture]
    public sealed class McpJwtReconnectPolicyTests
    {
        /// <summary>
        /// Nonrecoverable authentication and certificate errors end the connect attempt.
        /// </summary>
        [TestCaseSource(nameof(s_rejectedStatuses))]
        public void RejectedCredentialsAndTrustStopRetries(StatusCode status)
        {
            var policy = new McpJwtReconnectPolicy(new ReconnectPolicy());
            Assert.That(policy.TryGetNextDelay(0, status, null, out TimeSpan? delay), Is.True);
            Assert.That(delay, Is.Null);
        }

        /// <summary>
        /// Good, transient and server-busy outcomes retain the configured backoff and retry budget.
        /// </summary>
        [TestCaseSource(nameof(s_transientStatuses))]
        public void TransientFailuresPreserveHostBackoffAndBudget(StatusCode status)
        {
            var inner = new ReconnectPolicy(new ReconnectPolicyOptions
            {
                InitialDelay = TimeSpan.FromSeconds(2),
                MaxRetries = 3,
                JitterFactor = 0
            });
            var policy = new McpJwtReconnectPolicy(inner);
            var hint = TimeSpan.FromSeconds(7);
            bool expected = inner.TryGetNextDelay(1, status, hint, out TimeSpan? expectedDelay);
            Assert.That(policy.TryGetNextDelay(1, status, hint, out TimeSpan? delay), Is.EqualTo(expected));
            Assert.That(delay, Is.EqualTo(expectedDelay));
            Assert.That(policy.GetNextDelay(1), Is.EqualTo(TimeSpan.FromSeconds(4)));
            Assert.That(policy.TryGetNextDelay(3, status, hint, out delay), Is.True);
            Assert.That(delay, Is.Null);
            policy.Reset();
            Assert.That(policy.GetNextDelay(0), Is.EqualTo(TimeSpan.FromSeconds(2)));
        }

        /// <summary>
        /// Cancellation wins over both rejected credentials and ordinary backoff.
        /// </summary>
        [Test]
        public void CancellationIsNotConvertedIntoRetryTermination()
        {
            var policy = new McpJwtReconnectPolicy(new ReconnectPolicy());
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            Assert.That(() => policy.GetNextDelay(0, cancellation.Token),
                Throws.TypeOf<OperationCanceledException>());
            Assert.That(() => policy.TryGetNextDelay(
                0, StatusCodes.BadIdentityTokenRejected, null, out _, cancellation.Token),
                Throws.TypeOf<OperationCanceledException>());
        }

        private static readonly StatusCode[] s_rejectedStatuses =
        [
            StatusCodes.BadIdentityTokenInvalid,
            StatusCodes.BadIdentityTokenRejected,
            StatusCodes.BadUserAccessDenied,
            StatusCodes.BadSecurityChecksFailed,
            StatusCodes.BadCertificateInvalid,
            StatusCodes.BadCertificateTimeInvalid,
            StatusCodes.BadCertificateIssuerTimeInvalid,
            StatusCodes.BadCertificateHostNameInvalid,
            StatusCodes.BadCertificateUriInvalid,
            StatusCodes.BadCertificateUseNotAllowed,
            StatusCodes.BadCertificateIssuerUseNotAllowed,
            StatusCodes.BadCertificateUntrusted,
            StatusCodes.BadCertificateRevocationUnknown,
            StatusCodes.BadCertificateIssuerRevocationUnknown,
            StatusCodes.BadCertificateRevoked,
            StatusCodes.BadCertificateIssuerRevoked,
            StatusCodes.BadCertificateChainIncomplete,
            StatusCodes.BadCertificatePolicyCheckFailed
        ];

        private static readonly StatusCode[] s_transientStatuses =
        [
            StatusCodes.Good,
            StatusCodes.BadNoCommunication,
            StatusCodes.BadServerTooBusy
        ];
    }
}
#endif
