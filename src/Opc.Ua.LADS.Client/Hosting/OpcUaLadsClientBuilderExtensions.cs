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
using Opc.Ua.LADS.Client;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// Hosting extensions for OPC 30500-1 LADS clients.
    /// </summary>
    public static class OpcUaLadsClientBuilderExtensions
    {
        /// <summary>
        /// Registers a <see cref="LadsClientFactory"/> and a
        /// <c>Func&lt;CancellationToken, Task&lt;LadsClient&gt;&gt;</c>, together
        /// with the Device Integration client.
        /// </summary>
        /// <remarks>
        /// Resolves <see cref="LadsClientOptions"/> when registered in the service
        /// collection; otherwise clients use <see cref="LadsClientOptions.Default"/>.
        /// </remarks>
        /// <param name="builder">The client builder; <c>AddClient</c> must be called first.</param>
        /// <returns>The same builder, for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <c>null</c>.</exception>
        public static IOpcUaClientBuilder AddLadsClient(this IOpcUaClientBuilder builder)
        {
            builder.ThrowIfNull(nameof(builder));

            builder.AddOpcUaDi();
            builder.Services.TryAddSingleton(sp =>
            {
                Func<CancellationToken, Task<ManagedSession>> sessionFactory =
                    sp.GetService<Func<CancellationToken, Task<ManagedSession>>>() ??
                    throw new InvalidOperationException("AddLadsClient requires AddClient to be called first.");
                ITelemetryContext telemetry = sp.GetRequiredService<ITelemetryContext>();
                return new LadsClientFactory(sessionFactory, telemetry, sp.GetService<LadsClientOptions>());
            });
            builder.Services.TryAddSingleton<Func<CancellationToken, Task<LadsClient>>>(sp =>
            {
                LadsClientFactory factory = sp.GetRequiredService<LadsClientFactory>();
                return factory.CreateAsync;
            });
            return builder;
        }

        /// <summary>
        /// Registers a LADS client factory with explicit options.
        /// </summary>
        /// <param name="builder">The client builder; <c>AddClient</c> must be called first.</param>
        /// <param name="options">The immutable options.</param>
        /// <returns>The same builder, for chaining.</returns>
        /// <exception cref="ArgumentNullException">An argument is <c>null</c>.</exception>
        /// <exception cref="ArgumentOutOfRangeException">A limit is not positive.</exception>
        public static IOpcUaClientBuilder AddLadsClient(
            this IOpcUaClientBuilder builder,
            LadsClientOptions options)
        {
            builder.ThrowIfNull(nameof(builder));
            options.ThrowIfNull(nameof(options));
            options.Validate();
            builder.Services.AddSingleton(options);
            return builder.AddLadsClient();
        }

        /// <summary>
        /// Registers a LADS client factory and configures its options.
        /// </summary>
        /// <param name="builder">The client builder; <c>AddClient</c> must be called first.</param>
        /// <param name="configure">Derives the options from <see cref="LadsClientOptions.Default"/>.</param>
        /// <returns>The same builder, for chaining.</returns>
        /// <exception cref="ArgumentNullException">An argument is <c>null</c>.</exception>
        /// <exception cref="ArgumentOutOfRangeException">A limit is not positive.</exception>
        public static IOpcUaClientBuilder AddLadsClient(
            this IOpcUaClientBuilder builder,
            Func<LadsClientOptions, LadsClientOptions> configure)
        {
            builder.ThrowIfNull(nameof(builder));
            configure.ThrowIfNull(nameof(configure));
            LadsClientOptions options = configure(LadsClientOptions.Default) ??
                throw new InvalidOperationException("The options callback returned no options.");
            return builder.AddLadsClient(options);
        }
    }
}
