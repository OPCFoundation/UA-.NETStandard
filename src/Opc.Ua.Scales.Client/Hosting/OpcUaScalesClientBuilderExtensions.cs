// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Scales.Client;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// Hosting extensions for OPC 40200 Scales clients.
    /// </summary>
    public static class OpcUaScalesClientBuilderExtensions
    {
        /// <summary>
        /// Registers a <see cref="ScalesClientFactory"/> and a
        /// <c>Func&lt;CancellationToken, Task&lt;ScalesClient&gt;&gt;</c> over the
        /// client's managed session factory. Requires <c>AddClient</c>.
        /// </summary>
        /// <param name="builder">The client builder.</param>
        public static IOpcUaClientBuilder AddScalesClient(this IOpcUaClientBuilder builder)
        {
            builder.ThrowIfNull(nameof(builder));
            builder.AddOpcUaDi();
            builder.Services.TryAddSingleton(sp =>
            {
                Func<CancellationToken, Task<ManagedSession>> sessionFactory =
                    sp.GetService<Func<CancellationToken, Task<ManagedSession>>>() ??
                    throw new InvalidOperationException("AddScalesClient requires AddClient to be called first.");
                ITelemetryContext telemetry = sp.GetRequiredService<ITelemetryContext>();
                return new ScalesClientFactory(sessionFactory, telemetry);
            });
            builder.Services.TryAddSingleton<Func<CancellationToken, Task<ScalesClient>>>(sp =>
            {
                ScalesClientFactory factory = sp.GetRequiredService<ScalesClientFactory>();
                return factory.CreateAsync;
            });
            return builder;
        }
    }
}
