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
using Opc.Ua.Pumps.Client;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// Registers the Pumps client over the managed OPC UA session.
    /// </summary>
    public static class OpcUaPumpsClientBuilderExtensions
    {
        /// <summary>
        /// Registers a <see cref="PumpsClient"/> factory. The Pumps client
        /// composes the Device Integration client - pump discovery starts at
        /// the DeviceSet - so the DI client services are registered as well.
        /// </summary>
        /// <param name="builder">The client builder returned by <c>AddClient</c>.</param>
        public static IOpcUaClientBuilder AddPumpsClient(
            this IOpcUaClientBuilder builder)
        {
            builder.ThrowIfNull(nameof(builder));

            builder.AddOpcUaDi();
            builder.Services.TryAddSingleton(sp =>
            {
                Func<CancellationToken, Task<ManagedSession>> sessionFactory =
                    sp.GetService<Func<CancellationToken, Task<ManagedSession>>>() ??
                    throw new InvalidOperationException(
                        "AddPumpsClient requires AddClient to be called first.");
                ITelemetryContext telemetry = sp.GetRequiredService<ITelemetryContext>();
                return new PumpsClientFactory(sessionFactory, telemetry);
            });

            builder.Services.TryAddSingleton<
                Func<CancellationToken, Task<PumpsClient>>>(sp =>
            {
                PumpsClientFactory factory = sp.GetRequiredService<PumpsClientFactory>();
                return factory.CreateAsync;
            });

            return builder;
        }
    }
}
