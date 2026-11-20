// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.CranesHoists;

namespace CranesHoistsSample.Server
{
    /// <summary>
    /// Denies writes unless the operator explicitly enables this local, anonymous demonstration policy.
    /// </summary>
    internal sealed class DemoWriteAuthorizer : ICraneWriteAuthorizer
    {
        /// <summary>
        /// Captures the operator's opt-in independently of provider acceptance.
        /// </summary>
        public DemoWriteAuthorizer(CraneServerOptions options)
        {
            m_options = options ?? throw new ArgumentNullException(nameof(options));
        }

        /// <inheritdoc/>
        public ValueTask<bool> AuthorizeAsync(
            ISystemContext caller,
            CraneLimitRequest request,
            CancellationToken cancellationToken = default)
        {
            if (caller == null)
            {
                throw new ArgumentNullException(nameof(caller));
            }
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }
            cancellationToken.ThrowIfCancellationRequested();
            return new ValueTask<bool>(
                m_options.AllowLimitRequests &&
                m_options.DemoTrust &&
                caller is ISessionSystemContext { UserIdentity.TokenType: UserTokenType.Anonymous });
        }

        /// <summary>The operator's explicit local demonstration policy.</summary>
        private readonly CraneServerOptions m_options;
    }
}
