// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.GMS.Client;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// Integrates native GMS workflows with the existing injectable managed-session pipeline.
    /// </summary>
    public static class OpcUaGmsClientExtensions
    {
        /// <summary>
        /// Registers the native GMS client factory without adding server runtimes or an independent connection stack.
        /// </summary>
        public static IOpcUaClientBuilder AddGmsClient(
            this IOpcUaClientBuilder builder, GmsClientOptions? options = null)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            GmsClientOptions selected = options ?? new GmsClientOptions();
            selected.Validate();
            builder.Services.TryAddSingleton(TimeProvider.System);
            builder.Services.TryAddSingleton<IGmsClientFactory>(services => new GmsClientFactory(
                services.GetRequiredService<Func<CancellationToken, Task<ManagedSession>>>(),
                services.GetRequiredService<ITelemetryContext>(), selected,
                services.GetRequiredService<TimeProvider>()));
            return builder;
        }
    }
}
