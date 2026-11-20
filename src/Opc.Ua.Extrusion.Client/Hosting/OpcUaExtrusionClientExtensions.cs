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
using Opc.Ua.Extrusion.Client;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// Registers extrusion clients on the existing ManagedSession factory.
    /// </summary>
    public static class OpcUaExtrusionClientExtensions
    {
        /// <summary>
        /// Adds the typed extrusion facade without opening a second connection or changing identity.
        /// </summary>
        public static IOpcUaClientBuilder AddExtrusionClient(this IOpcUaClientBuilder builder)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            builder.Services.TryAddSingleton<ExtrusionClientFactory>();
            builder.Services.TryAddSingleton<Func<CancellationToken, Task<ExtrusionClient>>>(services =>
                services.GetRequiredService<ExtrusionClientFactory>().CreateAsync);
            return builder;
        }
    }
}

namespace Opc.Ua.Extrusion.Client
{
    /// <summary>
    /// Injectable factory backed by the existing managed-session provider.
    /// </summary>
    public sealed class ExtrusionClientFactory
    {
        /// <summary>
        /// Creates a typed client factory.
        /// </summary>
        public ExtrusionClientFactory(
            Func<CancellationToken, Task<ManagedSession>> sessions,
            ITelemetryContext telemetry)
        {
            m_sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
            m_telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
        }

        /// <summary>
        /// Resolves the caller's managed session and creates the typed facade.
        /// </summary>
        public async Task<ExtrusionClient> CreateAsync(CancellationToken cancellationToken = default)
        {
            ManagedSession session = await m_sessions(cancellationToken).ConfigureAwait(false);
            return new ExtrusionClient(session, m_telemetry);
        }

        /// <summary>The caller-configured session acquisition function.</summary>
        private readonly Func<CancellationToken, Task<ManagedSession>> m_sessions;

        /// <summary>The injected telemetry context.</summary>
        private readonly ITelemetryContext m_telemetry;
    }
}
