// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Opc.Ua;
using Opc.Ua.AI.Client;
using Opc.Ua.Client;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// Registers the AI Model Management client over the managed OPC UA session.
    /// </summary>
    public static class OpcUaAIClientBuilderExtensions
    {
        /// <summary>
        /// Registers an <see cref="AIClientFactory"/> and a
        /// <c>Func&lt;CancellationToken, Task&lt;AIClient&gt;&gt;</c> so
        /// downstream services can request AI clients.
        /// </summary>
        /// <param name="builder">The client builder returned by <c>AddClient</c>.</param>
        /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <c>null</c>.</exception>
        public static IOpcUaClientBuilder AddAIClient(
            this IOpcUaClientBuilder builder)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            builder.Services.TryAddSingleton<AIClientFactory>(sp =>
            {
                Func<CancellationToken, Task<ManagedSession>> sessionFactory =
                    sp.GetService<Func<CancellationToken, Task<ManagedSession>>>()
                    ?? throw new InvalidOperationException(
                        "AddAIClient requires AddClient to be called first.");
                ITelemetryContext telemetry =
                    sp.GetRequiredService<ITelemetryContext>();
                return new AIClientFactory(sessionFactory, telemetry);
            });

            builder.Services.TryAddSingleton<
                Func<CancellationToken, Task<AIClient>>>(sp =>
            {
                AIClientFactory factory =
                    sp.GetRequiredService<AIClientFactory>();
                return factory.CreateAsync;
            });

            return builder;
        }
    }
}
