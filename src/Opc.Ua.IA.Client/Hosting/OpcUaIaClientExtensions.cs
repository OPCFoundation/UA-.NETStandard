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
using Opc.Ua.IA.Client;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// Registers the IA facade over the existing managed-session factory.
    /// </summary>
    public static class OpcUaIaClientExtensions
    {
        /// <summary>
        /// Adds injectable IA clients without creating an additional session.
        /// </summary>
        public static IOpcUaClientBuilder AddIndustrialAutomationClient(this IOpcUaClientBuilder builder)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            builder.Services.TryAddSingleton<IndustrialAutomationClientFactory>();
            builder.Services.TryAddSingleton<Func<CancellationToken, Task<IndustrialAutomationClient>>>(services =>
                services.GetRequiredService<IndustrialAutomationClientFactory>().CreateAsync);
            return builder;
        }
    }
}

namespace Opc.Ua.IA.Client
{
    /// <summary>
    /// Creates typed IA facades that borrow the container's cached managed session.
    /// </summary>
    public sealed class IndustrialAutomationClientFactory
    {
        /// <summary>
        /// Creates the factory using the configured managed-session acquisition function.
        /// </summary>
        public IndustrialAutomationClientFactory(
            Func<CancellationToken, Task<ManagedSession>> sessions,
            ITelemetryContext telemetry)
        {
            m_sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
            m_telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
        }

        /// <summary>
        /// Resolves the managed session and creates a facade; callers must not dispose its session.
        /// </summary>
        public async Task<IndustrialAutomationClient> CreateAsync(CancellationToken cancellationToken = default)
        {
            ManagedSession session = await m_sessions(cancellationToken).ConfigureAwait(false);
            return new IndustrialAutomationClient(session, m_telemetry);
        }

        /// <summary>The container-owned session acquisition function.</summary>
        private readonly Func<CancellationToken, Task<ManagedSession>> m_sessions;

        /// <summary>The injected telemetry context.</summary>
        private readonly ITelemetryContext m_telemetry;
    }
}
