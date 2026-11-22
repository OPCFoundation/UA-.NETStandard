// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Opc.Ua;
using Opc.Ua.AutoID.Client;
using Opc.Ua.Client;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// Registers the AutoID client over the managed OPC UA session.
    /// </summary>
    public static class OpcUaAutoIdClientBuilderExtensions
    {
        /// <summary>
        /// Registers an <see cref="AutoIdClient"/> factory.
        /// </summary>
        /// <param name="builder">The client builder returned by <c>AddClient</c>.</param>
        public static IOpcUaClientBuilder AddAutoIdClient(this IOpcUaClientBuilder builder)
        {
            builder.ThrowIfNull(nameof(builder));

            builder.Services.TryAddSingleton(sp =>
            {
                Func<CancellationToken, Task<ManagedSession>> sessionFactory =
                    sp.GetService<Func<CancellationToken, Task<ManagedSession>>>() ??
                    throw new InvalidOperationException("AddAutoIdClient requires AddClient to be called first.");
                ITelemetryContext telemetry = sp.GetRequiredService<ITelemetryContext>();
                return new AutoIdClientFactory(sessionFactory, telemetry);
            });

            builder.Services.TryAddSingleton<Func<CancellationToken, Task<AutoIdClient>>>(sp =>
            {
                AutoIdClientFactory factory = sp.GetRequiredService<AutoIdClientFactory>();
                return factory.CreateAsync;
            });

            return builder;
        }
    }
}
