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
using Opc.Ua.MachineTool.Client;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// Registers typed clients over the application's existing managed-session factory.
    /// </summary>
    public static class OpcUaMachineToolClientExtensions
    {
        /// <summary>
        /// Registers the injectable factory and asynchronous client-creation delegate.
        /// </summary>
        /// <param name="builder">The configured OPC UA client builder.</param>
        /// <returns>The client builder.</returns>
        public static IOpcUaClientBuilder AddMachineToolClient(this IOpcUaClientBuilder builder)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            builder.Services.TryAddSingleton(services => new MachineToolClientFactory(
                services.GetRequiredService<Func<CancellationToken, Task<ManagedSession>>>(),
                services.GetRequiredService<ITelemetryContext>(),
                services.GetService<TimeProvider>()));
            builder.Services.TryAddSingleton<Func<CancellationToken, Task<MachineToolClient>>>(services =>
                services.GetRequiredService<MachineToolClientFactory>().CreateAsync);
            return builder;
        }
    }
}
