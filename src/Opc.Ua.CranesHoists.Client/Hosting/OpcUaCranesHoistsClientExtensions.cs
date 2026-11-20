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
using Opc.Ua.CranesHoists.Client;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// Registers crane client workflows over the existing managed-session services.
    /// </summary>
    public static class OpcUaCranesHoistsClientExtensions
    {
        /// <summary>
        /// Adds an injectable typed client factory without constructing or owning another session.
        /// </summary>
        public static IOpcUaClientBuilder AddCranesHoistsClient(
            this IOpcUaClientBuilder builder,
            CranesHoistsClientOptions? options = null)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            CranesHoistsClientOptions selected = options ?? new CranesHoistsClientOptions();
            selected.Validate();
            builder.Services.TryAddSingleton(services => new CranesHoistsClientFactory(
                services.GetRequiredService<Func<CancellationToken, Task<ManagedSession>>>(),
                services.GetRequiredService<ITelemetryContext>(),
                selected,
                services.GetService<TimeProvider>()));
            builder.Services.TryAddSingleton<Func<CancellationToken, Task<CranesHoistsClient>>>(services =>
                services.GetRequiredService<CranesHoistsClientFactory>().CreateAsync);
            return builder;
        }
    }
}
