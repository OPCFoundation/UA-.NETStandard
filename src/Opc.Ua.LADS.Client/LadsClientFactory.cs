/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Client;

namespace Opc.Ua.LADS.Client
{
    /// <summary>
    /// Creates <see cref="LadsClient"/> instances on sessions a hosted client
    /// opens.
    /// </summary>
    public sealed class LadsClientFactory
    {
        /// <summary>
        /// Creates the factory with the default options.
        /// </summary>
        /// <param name="sessionFactory">Opens a session.</param>
        /// <param name="telemetry">The telemetry context.</param>
        /// <exception cref="ArgumentNullException">An argument is <c>null</c>.</exception>
        public LadsClientFactory(
            Func<CancellationToken, Task<ManagedSession>> sessionFactory,
            ITelemetryContext telemetry)
            : this(sessionFactory, telemetry, null)
        {
        }

        /// <summary>
        /// Creates the factory with options.
        /// </summary>
        /// <param name="sessionFactory">Opens a session.</param>
        /// <param name="telemetry">The telemetry context.</param>
        /// <param name="options">The options; <c>null</c> uses <see cref="LadsClientOptions.Default"/>.</param>
        /// <exception cref="ArgumentNullException">A required argument is <c>null</c>.</exception>
        /// <exception cref="ArgumentOutOfRangeException">A limit is not positive.</exception>
        public LadsClientFactory(
            Func<CancellationToken, Task<ManagedSession>> sessionFactory,
            ITelemetryContext telemetry,
            LadsClientOptions? options)
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
        public async Task<LadsClient> CreateAsync(CancellationToken cancellationToken = default)
        {
            ManagedSession session = await m_sessionFactory(cancellationToken).ConfigureAwait(false);
            return new LadsClient(session, m_telemetry, m_options);
        }

        private readonly Func<CancellationToken, Task<ManagedSession>> m_sessionFactory;
        private readonly ITelemetryContext m_telemetry;
        private readonly LadsClientOptions? m_options;
    }
}
