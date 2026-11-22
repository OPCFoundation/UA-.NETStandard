// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.PackML.Client;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// Fluent dependency-injection registration for the standalone PackML client.
    /// </summary>
    public static class OpcUaPackMLClientBuilderExtensions
    {
        /// <summary>
        /// Adds a PackML client factory over the managed session configured by AddClient.
        /// The PackML client never disposes the container's session.
        /// </summary>
        public static IOpcUaClientBuilder AddPackMLClient(this IOpcUaClientBuilder builder)
        {
            builder.ThrowIfNull(nameof(builder));
            builder.Services.TryAddSingleton(sp =>
            {
                Func<CancellationToken, Task<ManagedSession>> sessionFactory =
                    sp.GetService<Func<CancellationToken, Task<ManagedSession>>>() ??
                    throw new InvalidOperationException("AddPackMLClient requires AddClient to be called first.");
                return new PackMLClientFactory(sessionFactory, sp.GetRequiredService<ITelemetryContext>());
            });
            builder.Services.TryAddSingleton<Func<CancellationToken, Task<PackMLClient>>>(sp =>
            {
                PackMLClientFactory factory = sp.GetRequiredService<PackMLClientFactory>();
                return factory.CreateAsync;
            });
            return builder;
        }
    }
}
