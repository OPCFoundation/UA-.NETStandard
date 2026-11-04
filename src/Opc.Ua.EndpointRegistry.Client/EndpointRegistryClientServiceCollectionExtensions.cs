/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 * SPDX-License-Identifier: MIT
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Opc.Ua.EndpointRegistry.Client;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// Registers native client factories; this never opens a session or starts a registry.
    /// </summary>
    public static class EndpointRegistryClientServiceCollectionExtensions
    {
        /// <summary>
        /// Adds the idempotent Endpoint Registry client factory. Register telemetry with AddOpcUa.
        /// </summary>
        public static IServiceCollection AddEndpointRegistryClient(this IServiceCollection services)
        {
            if (services is null)
            {
                throw new ArgumentNullException(nameof(services));
            }
            services.TryAddSingleton(static provider =>
                new EndpointRegistryClientFactory(provider.GetRequiredService<Opc.Ua.ITelemetryContext>()));
            return services;
        }
    }
}
