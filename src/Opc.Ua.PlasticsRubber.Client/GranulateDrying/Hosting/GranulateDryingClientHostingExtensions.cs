/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.GranulateDrying.Client;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// Registers drying clients over the existing managed-session factory.
    /// </summary>
    public static class GranulateDryingClientHostingExtensions
    {
        /// <summary>
        /// Adds a typed client factory without creating or owning a second session transport.
        /// </summary>
        public static IOpcUaClientBuilder AddGranulateDryingClient(this IOpcUaClientBuilder builder)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            builder.Services.TryAddSingleton<GranulateDryingClientFactory>(services =>
                new GranulateDryingClientFactory(
                    services.GetRequiredService<Func<CancellationToken, Task<ManagedSession>>>(),
                    services.GetRequiredService<ITelemetryContext>()));
            builder.Services.TryAddSingleton<Func<CancellationToken, Task<GranulateDryingClient>>>(services =>
                services.GetRequiredService<GranulateDryingClientFactory>().CreateAsync);
            return builder;
        }
    }
}

namespace Opc.Ua.GranulateDrying.Client
{
    /// <summary>
    /// Acquires the existing ManagedSession and opens the complete typed drying interface.
    /// </summary>
    public sealed class GranulateDryingClientFactory
    {
        /// <summary>
        /// Creates an injectable or directly constructed session-bound factory.
        /// </summary>
        public GranulateDryingClientFactory(
            Func<CancellationToken, Task<ManagedSession>> sessionFactory,
            ITelemetryContext telemetry)
        {
            m_sessionFactory = sessionFactory ?? throw new ArgumentNullException(nameof(sessionFactory));
            m_telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
        }

        /// <summary>
        /// Returns a fresh facade over the managed session without transferring session ownership.
        /// </summary>
        public async Task<GranulateDryingClient> CreateAsync(CancellationToken cancellationToken = default)
        {
            ManagedSession session = await m_sessionFactory(cancellationToken).ConfigureAwait(false);
            return new GranulateDryingClient(session, m_telemetry);
        }

        /// <summary>
        /// The existing session factory.
        /// </summary>
        private readonly Func<CancellationToken, Task<ManagedSession>> m_sessionFactory;

        /// <summary>
        /// The telemetry passed to generated proxies.
        /// </summary>
        private readonly ITelemetryContext m_telemetry;
    }
}
