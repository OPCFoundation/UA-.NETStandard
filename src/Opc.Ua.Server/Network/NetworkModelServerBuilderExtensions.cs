/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using Microsoft.Extensions.DependencyInjection;
using Opc.Ua.Server.Hosting;

namespace Opc.Ua.Server.Network
{
    /// <summary>
    /// Fluent registration for the provider-backed Part 22 server model.
    /// </summary>
    public static class NetworkModelServerBuilderExtensions
    {
        /// <summary>
        /// Registers one provider-backed Part 22 address-space manager.
        /// Mapping mutation is denied unless the application's explicit policy allows it.
        /// </summary>
        /// <param name="builder">The regular OPC UA server builder.</param>
        /// <param name="provider">Application-owned snapshot provider.</param>
        /// <param name="authorizeMappingEdits">Policy evaluated for every mapping-method call.</param>
        /// <returns>The builder for fluent composition.</returns>
        public static IOpcUaServerBuilder AddNetworkModel(
            this IOpcUaServerBuilder builder,
            INetworkModelProvider provider,
            Func<ISystemContext, bool>? authorizeMappingEdits = null)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            if (provider is null)
            {
                throw new ArgumentNullException(nameof(provider));
            }

            var factory = new NetworkModelNodeManagerFactory(
                provider,
                authorizeMappingEdits ?? (static _ => false));
            return builder.AddNodeManager(factory);
        }

        /// <summary>
        /// Resolves an application-owned provider from dependency injection when the server starts.
        /// Mapping mutation is denied unless the application's explicit policy allows it.
        /// </summary>
        public static IOpcUaServerBuilder AddNetworkModel(
            this IOpcUaServerBuilder builder,
            Func<IServiceProvider, INetworkModelProvider> providerFactory,
            Func<ISystemContext, bool>? authorizeMappingEdits = null)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            if (providerFactory is null)
            {
                throw new ArgumentNullException(nameof(providerFactory));
            }
            return builder.AddNodeManagers((services, _) =>
            [
                new NetworkModelNodeManagerFactory(
                    providerFactory(services) ??
                        throw new InvalidOperationException("The network model provider factory returned null."),
                    authorizeMappingEdits ?? (static _ => false))
            ]);
        }
    }
}
