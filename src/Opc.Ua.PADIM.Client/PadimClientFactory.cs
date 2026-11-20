/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Client;

namespace Opc.Ua.PADIM.Client
{
    /// <summary>
    /// Creates PA-DIM clients borrowing the container-owned managed session.
    /// </summary>
    public sealed class PadimClientFactory
    {
        /// <summary>
        /// Creates a factory over the session delegate registered by AddClient.
        /// </summary>
        public PadimClientFactory(
            Func<CancellationToken, Task<ManagedSession>> sessionFactory,
            ITelemetryContext telemetry)
        {
            m_sessionFactory = sessionFactory ?? throw new ArgumentNullException(nameof(sessionFactory));
            m_telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
        }

        /// <summary>
        /// Acquires the current shared session without transferring its ownership.
        /// </summary>
        public async Task<PadimClient> CreateAsync(CancellationToken cancellationToken = default)
        {
            ManagedSession session = await m_sessionFactory(cancellationToken).ConfigureAwait(false);
            return new PadimClient(session, m_telemetry);
        }

        /// <summary>The container-owned session acquisition delegate.</summary>
        private readonly Func<CancellationToken, Task<ManagedSession>> m_sessionFactory;

        /// <summary>The application's telemetry context.</summary>
        private readonly ITelemetryContext m_telemetry;
    }
}
