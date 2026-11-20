// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Client;

namespace Opc.Ua.MachineTool.Client
{
    /// <summary>
    /// Creates clients that borrow application- or container-owned managed sessions.
    /// </summary>
    public sealed class MachineToolClientFactory
    {
        /// <summary>
        /// Creates an injectable or directly constructed client factory.
        /// </summary>
        /// <param name="sessionFactory">Acquires a managed session whose ownership remains with its provider.</param>
        /// <param name="telemetry">The telemetry context.</param>
        /// <param name="timeProvider">The clock used for bounded observations.</param>
        public MachineToolClientFactory(
            Func<CancellationToken, Task<ManagedSession>> sessionFactory,
            ITelemetryContext telemetry,
            TimeProvider? timeProvider = null)
        {
            m_sessionFactory = sessionFactory ?? throw new ArgumentNullException(nameof(sessionFactory));
            m_telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
            m_timeProvider = timeProvider ?? TimeProvider.System;
        }

        /// <summary>
        /// Acquires a session without taking ownership; asynchronously dispose the client to drain its observations.
        /// </summary>
        /// <param name="cancellationToken">Cancels connection setup.</param>
        /// <returns>A client that borrows the acquired session.</returns>
        public async Task<MachineToolClient> CreateAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ManagedSession session = await m_sessionFactory(cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return new MachineToolClient(session, m_telemetry, m_timeProvider);
        }

        /// <summary>The application's managed-session accessor.</summary>
        private readonly Func<CancellationToken, Task<ManagedSession>> m_sessionFactory;

        /// <summary>The telemetry context.</summary>
        private readonly ITelemetryContext m_telemetry;

        /// <summary>The observation clock.</summary>
        private readonly TimeProvider m_timeProvider;
    }
}
