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
using System.Threading;
using Opc.Ua.Client;

namespace Opc.Ua.Mcp
{
    /// <summary>
    /// Stops retries of rejected credentials or trust while retaining the host's transient-failure policy.
    /// </summary>
    internal sealed class McpJwtReconnectPolicy : IReconnectPolicy
    {
        /// <summary>
        /// Wraps the reconnect policy used for recoverable transport failures.
        /// </summary>
        public McpJwtReconnectPolicy(IReconnectPolicy policy)
        {
            m_policy = policy ?? throw new ArgumentNullException(nameof(policy));
        }

        /// <inheritdoc/>
        public TimeSpan? GetNextDelay(int attempt, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return m_policy.GetNextDelay(attempt, ct);
        }

        /// <inheritdoc/>
        public bool TryGetNextDelay(
            int attempt,
            StatusCode lastStatus,
            TimeSpan? serverRetryAfter,
            out TimeSpan? delay,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            var status = new StatusCode(lastStatus.CodeBits);
            if (status == StatusCodes.BadIdentityTokenInvalid ||
                status == StatusCodes.BadIdentityTokenRejected ||
                status == StatusCodes.BadUserAccessDenied ||
                status == StatusCodes.BadSecurityChecksFailed ||
                status == StatusCodes.BadCertificateInvalid ||
                status == StatusCodes.BadCertificateTimeInvalid ||
                status == StatusCodes.BadCertificateIssuerTimeInvalid ||
                status == StatusCodes.BadCertificateHostNameInvalid ||
                status == StatusCodes.BadCertificateUriInvalid ||
                status == StatusCodes.BadCertificateUseNotAllowed ||
                status == StatusCodes.BadCertificateIssuerUseNotAllowed ||
                status == StatusCodes.BadCertificateUntrusted ||
                status == StatusCodes.BadCertificateRevocationUnknown ||
                status == StatusCodes.BadCertificateIssuerRevocationUnknown ||
                status == StatusCodes.BadCertificateRevoked ||
                status == StatusCodes.BadCertificateIssuerRevoked ||
                status == StatusCodes.BadCertificateChainIncomplete ||
                status == StatusCodes.BadCertificatePolicyCheckFailed)
            {
                delay = null;
                return true;
            }

            return m_policy.TryGetNextDelay(attempt, lastStatus, serverRetryAfter, out delay, ct);
        }

        /// <inheritdoc/>
        public void Reset()
        {
            m_policy.Reset();
        }

        /// <summary>The host's recoverable-failure policy.</summary>
        private readonly IReconnectPolicy m_policy;
    }
}
