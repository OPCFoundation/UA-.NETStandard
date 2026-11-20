// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Client;

namespace Opc.Ua.AMB.Client
{
    /// <summary>
    /// Creates <see cref="AmbClient"/> instances on sessions a hosted client
    /// opens.
    /// </summary>
    public sealed class AmbClientFactory
    {
        /// <summary>
        /// Creates the factory.
        /// </summary>
        /// <param name="sessionFactory">Opens a session.</param>
        /// <param name="telemetry">The telemetry context.</param>
        /// <exception cref="ArgumentNullException">An argument is <c>null</c>.</exception>
        public AmbClientFactory(
            Func<CancellationToken, Task<ManagedSession>> sessionFactory,
            ITelemetryContext telemetry)
            : this(sessionFactory, telemetry, null)
        {
        }

        /// <summary>
        /// Creates a factory with optional strict-read traversal limits.
        /// </summary>
        /// <param name="sessionFactory">Opens a session.</param>
        /// <param name="telemetry">The telemetry context.</param>
        /// <param name="options">The immutable limits; <c>null</c> retains tolerant behavior.</param>
        /// <exception cref="ArgumentNullException">A required argument is <c>null</c>.</exception>
        /// <exception cref="ArgumentOutOfRangeException">A traversal limit is not positive.</exception>
        public AmbClientFactory(
            Func<CancellationToken, Task<ManagedSession>> sessionFactory,
            ITelemetryContext telemetry,
            AmbClientOptions? options)
        {
            m_sessionFactory = sessionFactory ?? throw new ArgumentNullException(nameof(sessionFactory));
            m_telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
            options?.Validate();
            m_options = options;
        }

        /// <summary>
        /// Opens a session and creates a client on it.
        /// </summary>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The client.</returns>
        public async Task<AmbClient> CreateAsync(CancellationToken cancellationToken = default)
        {
            ManagedSession session = await m_sessionFactory(cancellationToken).ConfigureAwait(false);
            return m_options is null
                ? new AmbClient(session, m_telemetry)
                : new AmbClient(session, m_telemetry, m_options);
        }

        private readonly Func<CancellationToken, Task<ManagedSession>> m_sessionFactory;
        private readonly ITelemetryContext m_telemetry;
        private readonly AmbClientOptions? m_options;
    }
}
