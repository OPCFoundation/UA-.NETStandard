// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Opc.Ua;
using Opc.Ua.AMB.Client;
using Opc.Ua.Client;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// Hosting extensions for OPC 10000-110 Asset Management Basics clients.
    /// </summary>
    public static class OpcUaAmbClientBuilderExtensions
    {
        /// <summary>
        /// Registers an <see cref="AmbClientFactory"/> and a
        /// <c>Func&lt;CancellationToken, Task&lt;AmbClient&gt;&gt;</c>, together
        /// with the Device Integration client.
        /// </summary>
        /// <remarks>
        /// Resolves <see cref="AmbClientOptions"/> when registered in the service
        /// collection; otherwise clients retain their tolerant behavior.
        /// </remarks>
        /// <param name="builder">The client builder; <c>AddClient</c> must be called first.</param>
        /// <returns>The same builder, for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <c>null</c>.</exception>
        public static IOpcUaClientBuilder AddAssetManagementClient(this IOpcUaClientBuilder builder)
        {
            builder.ThrowIfNull(nameof(builder));

            builder.AddOpcUaDi();
            builder.Services.TryAddSingleton(sp =>
            {
                Func<CancellationToken, Task<ManagedSession>> sessionFactory =
                    sp.GetService<Func<CancellationToken, Task<ManagedSession>>>() ??
                    throw new InvalidOperationException(
                        "AddAssetManagementClient requires AddClient to be called first.");
                ITelemetryContext telemetry = sp.GetRequiredService<ITelemetryContext>();
                return new AmbClientFactory(sessionFactory, telemetry, sp.GetService<AmbClientOptions>());
            });
            builder.Services.TryAddSingleton<Func<CancellationToken, Task<AmbClient>>>(sp =>
            {
                AmbClientFactory factory = sp.GetRequiredService<AmbClientFactory>();
                return factory.CreateAsync;
            });
            return builder;
        }

        /// <summary>
        /// Registers an Asset Management Basics client factory with bounded,
        /// strictly validated reads.
        /// </summary>
        /// <param name="builder">The client builder; <c>AddClient</c> must be called first.</param>
        /// <param name="options">The immutable traversal limits.</param>
        /// <returns>The same builder, for chaining.</returns>
        /// <exception cref="ArgumentNullException">An argument is <c>null</c>.</exception>
        /// <exception cref="ArgumentOutOfRangeException">A traversal limit is not positive.</exception>
        public static IOpcUaClientBuilder AddAssetManagementClient(
            this IOpcUaClientBuilder builder,
            AmbClientOptions options)
        {
            builder.ThrowIfNull(nameof(builder));
            options.ThrowIfNull(nameof(options));
            options.Validate();
            builder.Services.AddSingleton(options);
            return builder.AddAssetManagementClient();
        }
    }
}
