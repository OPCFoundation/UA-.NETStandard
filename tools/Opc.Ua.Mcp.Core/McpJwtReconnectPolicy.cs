/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
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
