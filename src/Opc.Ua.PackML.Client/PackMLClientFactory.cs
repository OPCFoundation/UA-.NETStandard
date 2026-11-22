// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Client;

namespace Opc.Ua.PackML.Client
{
    /// <summary>
    /// Creates typed PackML clients over the application's managed-session factory.
    /// Session ownership remains with the application or its dependency-injection container.
    /// </summary>
    public sealed class PackMLClientFactory
    {
        /// <summary>
        /// Creates a factory over the existing session and telemetry services.
        /// </summary>
        public PackMLClientFactory(
            Func<CancellationToken, Task<ManagedSession>> sessionFactory,
            ITelemetryContext telemetry)
        {
            m_sessionFactory = sessionFactory ?? throw new ArgumentNullException(nameof(sessionFactory));
            m_telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
        }

        /// <summary>
        /// Borrows a managed session and creates a typed client; it never takes ownership of that session.
        /// </summary>
        public async Task<PackMLClient> CreateAsync(CancellationToken cancellationToken = default)
        {
            ManagedSession session = await m_sessionFactory(cancellationToken).ConfigureAwait(false);
            return new PackMLClient(session, m_telemetry);
        }

        /// <summary>The application's session factory.</summary>
        private readonly Func<CancellationToken, Task<ManagedSession>> m_sessionFactory;

        /// <summary>The application's telemetry context.</summary>
        private readonly ITelemetryContext m_telemetry;
    }

    /// <summary>
    /// Direct typed-client creation over an existing session.
    /// </summary>
    public static class SessionPackMLExtensions
    {
        /// <summary>
        /// Creates a PackML client borrowing this session.
        /// </summary>
        public static PackMLClient PackML(this ISession session, ITelemetryContext telemetry)
        {
            return new PackMLClient(session, telemetry);
        }
    }
}
