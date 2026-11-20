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
using Opc.Ua.Vision.Client;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// Registers the Vision client over the managed OPC UA session.
    /// </summary>
    public static class OpcUaVisionClientBuilderExtensions
    {
        /// <summary>
        /// Registers a <see cref="VisionClientFactory"/> and a
        /// <c>Func&lt;CancellationToken, Task&lt;VisionClient&gt;&gt;</c> so
        /// downstream services can request Vision clients.
        /// </summary>
        /// <param name="builder">
        /// The client builder returned by <c>AddClient</c>.
        /// </param>
        public static IOpcUaClientBuilder AddVisionClient(
            this IOpcUaClientBuilder builder)
        {
            builder.ThrowIfNull(nameof(builder));

            builder.Services.TryAddSingleton<VisionClientFactory>(sp =>
            {
                Func<CancellationToken, Task<ManagedSession>> sessionFactory =
                    sp.GetService<Func<CancellationToken, Task<ManagedSession>>>()
                    ?? throw new InvalidOperationException(
                        "AddVisionClient requires AddClient to be called first.");
                ITelemetryContext telemetry =
                    sp.GetRequiredService<ITelemetryContext>();
                return new VisionClientFactory(sessionFactory, telemetry);
            });

            builder.Services.TryAddSingleton<
                Func<CancellationToken, Task<VisionClient>>>(sp =>
            {
                VisionClientFactory factory =
                    sp.GetRequiredService<VisionClientFactory>();
                return factory.CreateAsync;
            });

            return builder;
        }
    }
}
