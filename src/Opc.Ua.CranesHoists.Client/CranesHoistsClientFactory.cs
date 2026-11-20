// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Client;

namespace Opc.Ua.CranesHoists.Client
{
    /// <summary>
    /// Creates typed crane clients over the application's managed-session factory.
    /// </summary>
    public sealed class CranesHoistsClientFactory
    {
        /// <summary>
        /// Captures injected session acquisition and telemetry.
        /// </summary>
        public CranesHoistsClientFactory(
            Func<CancellationToken, Task<ManagedSession>> sessionFactory,
            ITelemetryContext telemetry,
            CranesHoistsClientOptions? options = null,
            TimeProvider? timeProvider = null)
        {
            m_sessionFactory = sessionFactory ?? throw new ArgumentNullException(nameof(sessionFactory));
            m_telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
            m_options = options ?? new CranesHoistsClientOptions();
            m_options.Validate();
            m_timeProvider = timeProvider ?? TimeProvider.System;
        }

        /// <summary>
        /// Acquires a session without taking ownership of that session.
        /// </summary>
        public async Task<CranesHoistsClient> CreateAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ManagedSession session = await m_sessionFactory(cancellationToken).ConfigureAwait(false);
            return new CranesHoistsClient(session, m_telemetry, m_options, m_timeProvider);
        }

        private readonly Func<CancellationToken, Task<ManagedSession>> m_sessionFactory;
        private readonly ITelemetryContext m_telemetry;
        private readonly CranesHoistsClientOptions m_options;
        private readonly TimeProvider m_timeProvider;
    }

    /// <summary>
    /// Creates typed crane clients without a server-package dependency.
    /// </summary>
    public static class SessionCranesHoistsExtensions
    {
        /// <summary>
        /// Extends an existing session with bounded crane workflows.
        /// </summary>
        public static CranesHoistsClient CranesHoists(
            this ISession session,
            ITelemetryContext telemetry,
            CranesHoistsClientOptions? options = null)
        {
            return new CranesHoistsClient(session, telemetry, options);
        }
    }
}
