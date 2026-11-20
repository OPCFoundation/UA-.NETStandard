// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Client;

namespace Opc.Ua.Machinery.Client
{
    /// <summary>
    /// Session-bound factory for creating Machinery clients through dependency
    /// injection.
    /// </summary>
    public sealed class MachineryClientFactory
    {
        /// <summary>
        /// Creates a Machinery client factory.
        /// </summary>
        /// <param name="sessionFactory">
        /// The managed-session factory registered by <c>AddClient</c>.
        /// </param>
        /// <param name="telemetry">
        /// The telemetry context used by generated proxies.
        /// </param>
        public MachineryClientFactory(
            Func<CancellationToken, Task<ManagedSession>> sessionFactory,
            ITelemetryContext telemetry)
        {
            m_sessionFactory = sessionFactory ??
                throw new ArgumentNullException(nameof(sessionFactory));
            m_telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
        }

        /// <summary>
        /// Creates a factory whose clients use copied, validated strict read limits.
        /// </summary>
        /// <param name="sessionFactory">The managed-session factory registered by AddClient.</param>
        /// <param name="telemetry">The telemetry context.</param>
        /// <param name="options">Positive bounds for each created client.</param>
        public MachineryClientFactory(
            Func<CancellationToken, Task<ManagedSession>> sessionFactory,
            ITelemetryContext telemetry,
            MachineryClientOptions options)
            : this(sessionFactory, telemetry)
        {
            m_options = (options ?? throw new ArgumentNullException(nameof(options))).CopyAndValidate();
        }

        /// <summary>
        /// Creates a Machinery client over the current managed session.
        /// </summary>
        /// <param name="cancellationToken">Cancels session acquisition.</param>
        public async Task<MachineryClient> CreateAsync(
            CancellationToken cancellationToken = default)
        {
            ManagedSession session = await m_sessionFactory(cancellationToken)
                .ConfigureAwait(false);
            return m_options is null
                ? new MachineryClient(session, m_telemetry)
                : new MachineryClient(session, m_telemetry, m_options);
        }

        private readonly Func<CancellationToken, Task<ManagedSession>> m_sessionFactory;
        private readonly ITelemetryContext m_telemetry;
        private readonly MachineryClientOptions? m_options;
    }
}
