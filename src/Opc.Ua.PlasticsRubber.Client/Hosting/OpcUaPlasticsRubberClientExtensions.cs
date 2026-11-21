// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.PlasticsRubber.Client;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// Registers common-model clients over the application's existing managed session.
    /// </summary>
    public static class OpcUaPlasticsRubberClientExtensions
    {
        /// <summary>
        /// Adds a typed factory without creating a second session or changing its credentials.
        /// </summary>
        public static IOpcUaClientBuilder AddPlasticsRubberClient(this IOpcUaClientBuilder builder)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            builder.Services.TryAddSingleton(services => new PlasticsRubberClientFactory(
                services.GetRequiredService<Func<CancellationToken, Task<ManagedSession>>>(),
                services.GetRequiredService<ITelemetryContext>()));
            builder.Services.TryAddSingleton<Func<CancellationToken, Task<PlasticsRubberClient>>>(
                services => services.GetRequiredService<PlasticsRubberClientFactory>().CreateAsync);
            return builder;
        }
    }
}
