/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Client;

namespace Opc.Ua.PlasticsRubber.Client
{
    /// <summary>
    /// Resolves the caller's managed session for common-model clients.
    /// </summary>
    public sealed class PlasticsRubberClientFactory
    {
        /// <summary>
        /// Creates a factory without taking ownership of the shared managed session.
        /// </summary>
        public PlasticsRubberClientFactory(
            Func<CancellationToken, Task<ManagedSession>> sessionFactory,
            ITelemetryContext telemetry)
        {
            m_sessionFactory = sessionFactory ?? throw new ArgumentNullException(nameof(sessionFactory));
            m_telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
        }

        /// <summary>
        /// Acquires the managed session and returns its typed common-capability facade.
        /// </summary>
        public async Task<PlasticsRubberClient> CreateAsync(CancellationToken cancellationToken = default)
        {
            ManagedSession session = await m_sessionFactory(cancellationToken).ConfigureAwait(false);
            return new PlasticsRubberClient(session, m_telemetry);
        }

        /// <summary>
        /// The application's managed-session factory.
        /// </summary>
        private readonly Func<CancellationToken, Task<ManagedSession>> m_sessionFactory;

        /// <summary>
        /// The shared telemetry context.
        /// </summary>
        private readonly ITelemetryContext m_telemetry;
    }
}
