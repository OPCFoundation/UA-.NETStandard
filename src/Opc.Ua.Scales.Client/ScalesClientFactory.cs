// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Client;

namespace Opc.Ua.Scales.Client
{
    /// <summary>
    /// Creates <see cref="ScalesClient"/> instances over managed sessions.
    /// </summary>
    public sealed class ScalesClientFactory
    {
        /// <summary>
        /// Creates the factory.
        /// </summary>
        /// <param name="sessionFactory">Opens a managed session.</param>
        /// <param name="telemetry">The telemetry context.</param>
        public ScalesClientFactory(
            Func<CancellationToken, Task<ManagedSession>> sessionFactory,
            ITelemetryContext telemetry)
        {
            m_sessionFactory = sessionFactory ?? throw new ArgumentNullException(nameof(sessionFactory));
            m_telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
        }

        /// <summary>
        /// Opens a session and returns a Scales client over it.
        /// </summary>
        /// <param name="cancellationToken">Cancels the operation.</param>
        public async Task<ScalesClient> CreateAsync(CancellationToken cancellationToken = default)
        {
            ManagedSession session = await m_sessionFactory(cancellationToken).ConfigureAwait(false);
            return new ScalesClient(session, m_telemetry);
        }

        private readonly Func<CancellationToken, Task<ManagedSession>> m_sessionFactory;
        private readonly ITelemetryContext m_telemetry;
    }
}
