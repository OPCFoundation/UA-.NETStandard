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
using Opc.Ua.Machinery.Client;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// Registers the Machinery client over the managed OPC UA session.
    /// </summary>
    public static class OpcUaMachineryClientBuilderExtensions
    {
        /// <summary>
        /// Registers a <see cref="MachineryClient"/> factory. The Machinery
        /// client composes the Device Integration client, so the DI client
        /// services are registered as well.
        /// </summary>
        /// <param name="builder">The client builder returned by <c>AddClient</c>.</param>
        public static IOpcUaClientBuilder AddMachineryClient(
            this IOpcUaClientBuilder builder)
        {
            return AddMachineryClientCore(builder, null);
        }

        /// <summary>
        /// Registers Machinery clients with bounded discovery and strict, evidenced reads.
        /// </summary>
        /// <param name="builder">The builder returned by AddClient.</param>
        /// <param name="options">Positive limits, validated and copied during registration.</param>
        /// <exception cref="ArgumentNullException"></exception>
        public static IOpcUaClientBuilder AddMachineryClient(
            this IOpcUaClientBuilder builder,
            MachineryClientOptions options)
        {
            return AddMachineryClientCore(
                builder, (options ?? throw new ArgumentNullException(nameof(options))).CopyAndValidate());
        }

        private static IOpcUaClientBuilder AddMachineryClientCore(
            IOpcUaClientBuilder builder,
            MachineryClientOptions? options)
        {
            builder.ThrowIfNull(nameof(builder));

            builder.AddOpcUaDi();
            builder.Services.TryAddSingleton(sp =>
            {
                Func<CancellationToken, Task<ManagedSession>> sessionFactory =
                    sp.GetService<Func<CancellationToken, Task<ManagedSession>>>() ??
                    throw new InvalidOperationException(
                        "AddMachineryClient requires AddClient to be called first.");
                ITelemetryContext telemetry = sp.GetRequiredService<ITelemetryContext>();
                return options is null
                    ? new MachineryClientFactory(sessionFactory, telemetry)
                    : new MachineryClientFactory(sessionFactory, telemetry, options);
            });

            builder.Services.TryAddSingleton<
                Func<CancellationToken, Task<MachineryClient>>>(sp =>
            {
                MachineryClientFactory factory =
                    sp.GetRequiredService<MachineryClientFactory>();
                return factory.CreateAsync;
            });

            return builder;
        }
    }
}
