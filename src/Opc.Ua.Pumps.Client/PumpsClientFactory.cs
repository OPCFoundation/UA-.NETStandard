// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Client;

namespace Opc.Ua.Pumps.Client
{
    /// <summary>
    /// Session-bound factory for creating Pumps clients through dependency
    /// injection.
    /// </summary>
    public sealed class PumpsClientFactory
    {
        /// <summary>
        /// Creates a Pumps client factory.
        /// </summary>
        /// <param name="sessionFactory">
        /// The managed-session factory registered by <c>AddClient</c>.
        /// </param>
        /// <param name="telemetry">The telemetry context.</param>
        public PumpsClientFactory(
            Func<CancellationToken, Task<ManagedSession>> sessionFactory,
            ITelemetryContext telemetry)
        {
            m_sessionFactory = sessionFactory ??
                throw new ArgumentNullException(nameof(sessionFactory));
            m_telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
        }

        /// <summary>
        /// Creates a Pumps client over the current managed session.
        /// </summary>
        /// <param name="cancellationToken">Cancels session acquisition.</param>
        public async Task<PumpsClient> CreateAsync(
            CancellationToken cancellationToken = default)
        {
            ManagedSession session = await m_sessionFactory(cancellationToken)
                .ConfigureAwait(false);
            return new PumpsClient(session, m_telemetry);
        }

        /// <summary>
        /// Creates a bounded, strictly validated client over the current managed session.
        /// </summary>
        /// <param name="maximumBrowseReferences">An inclusive limit from one through 65536.</param>
        /// <param name="cancellationToken">Cancels session acquisition.</param>
        /// <exception cref="ArgumentOutOfRangeException">The reference limit is outside its bounds.</exception>
        public async Task<PumpsClient> CreateAsync(
            int maximumBrowseReferences,
            CancellationToken cancellationToken = default)
        {
            if (maximumBrowseReferences is < 1 or > 65536)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumBrowseReferences));
            }
            ManagedSession session = await m_sessionFactory(cancellationToken).ConfigureAwait(false);
            return new PumpsClient(session, m_telemetry, maximumBrowseReferences);
        }

        private readonly Func<CancellationToken, Task<ManagedSession>> m_sessionFactory;
        private readonly ITelemetryContext m_telemetry;
    }
}
