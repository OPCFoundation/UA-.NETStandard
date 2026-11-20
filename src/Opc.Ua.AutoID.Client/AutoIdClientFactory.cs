/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Client;

namespace Opc.Ua.AutoID.Client
{
    /// <summary>
    /// Session-bound factory for creating AutoID clients through dependency injection.
    /// </summary>
    public sealed class AutoIdClientFactory
    {
        /// <summary>
        /// Creates an AutoID client factory.
        /// </summary>
        /// <param name="sessionFactory">The managed-session factory registered by <c>AddClient</c>.</param>
        /// <param name="telemetry">The telemetry context.</param>
        public AutoIdClientFactory(
            Func<CancellationToken, Task<ManagedSession>> sessionFactory,
            ITelemetryContext telemetry)
        {
            m_sessionFactory = sessionFactory ?? throw new ArgumentNullException(nameof(sessionFactory));
            m_telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
        }

        /// <summary>
        /// Creates an AutoID client over the current managed session.
        /// </summary>
        /// <param name="cancellationToken">Cancels session acquisition.</param>
        public async Task<AutoIdClient> CreateAsync(CancellationToken cancellationToken = default)
        {
            ManagedSession session = await m_sessionFactory(cancellationToken).ConfigureAwait(false);
            return new AutoIdClient(session, m_telemetry);
        }

        private readonly Func<CancellationToken, Task<ManagedSession>> m_sessionFactory;
        private readonly ITelemetryContext m_telemetry;
    }
}
