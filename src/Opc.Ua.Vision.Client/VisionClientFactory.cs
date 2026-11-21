// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Client;

namespace Opc.Ua.Vision.Client
{
    /// <summary>
    /// Session-bound factory for creating Vision clients through DI.
    /// </summary>
    public sealed class VisionClientFactory
    {
        private readonly Func<CancellationToken, Task<ManagedSession>> m_sessionFactory;
        private readonly ITelemetryContext m_telemetry;

        /// <summary>
        /// Creates a Vision client factory.
        /// </summary>
        /// <param name="sessionFactory">
        /// The managed-session factory registered by <c>AddClient</c>.
        /// </param>
        /// <param name="telemetry">
        /// The telemetry context used by generated proxies.
        /// </param>
        public VisionClientFactory(
            Func<CancellationToken, Task<ManagedSession>> sessionFactory,
            ITelemetryContext telemetry)
        {
            m_sessionFactory = sessionFactory
                ?? throw new ArgumentNullException(nameof(sessionFactory));
            m_telemetry = telemetry
                ?? throw new ArgumentNullException(nameof(telemetry));
        }

        /// <summary>
        /// Creates a Vision client over the current managed session.
        /// </summary>
        /// <param name="cancellationToken">
        /// Cancels session acquisition.
        /// </param>
        public async Task<VisionClient> CreateAsync(
            CancellationToken cancellationToken = default)
        {
            ManagedSession session = await m_sessionFactory(cancellationToken)
                .ConfigureAwait(false);
            return new VisionClient(session, m_telemetry);
        }
    }
}
