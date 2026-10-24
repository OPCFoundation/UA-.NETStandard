/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 * ======================================================================*/

using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Opc.Ua.DataChannels;
using Opc.Ua.Server.DataChannels;

namespace Opc.Ua.Server.Hosting
{
    /// <summary>
    /// Direct and DI opt-in for flow policy, keeping native bindings and the optional model independent.
    /// </summary>
    public static class OpcUaServerDataChannelBuilderExtensions
    {
        /// <summary>
        /// Registers application-scoped flow authorization and optional Session-control model hosting.
        /// Transport packages remain explicit independent registrations.
        /// </summary>
        public static IOpcUaServerBuilder AddDataChannels(
            this IOpcUaServerBuilder builder,
            bool enableModel = false,
            DataChannelOptions? options = null,
            DataChannelApplicationLimits? applicationLimits = null)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            if (options == null)
            {
                builder.Services.TryAddSingleton(new DataChannelOptions());
            }
            else
            {
                builder.Services.Replace(ServiceDescriptor.Singleton(options));
            }
            if (applicationLimits != null)
            {
                builder.Services.Replace(ServiceDescriptor.Singleton(applicationLimits));
                builder.Services.Replace(
                    ServiceDescriptor.Singleton(new DataChannelApplicationBudget(applicationLimits)));
            }
            else
            {
                builder.Services.TryAddSingleton<DataChannelApplicationBudget>();
            }
            builder.Services.TryAddSingleton(services =>
            {
                var registry = new DataChannelSourceRegistry();
                foreach (DataChannelSource source in services.GetServices<DataChannelSource>())
                {
                    registry.Register(source);
                }
                return registry;
            });
            builder.Services.TryAddSingleton<ServerDataChannelProvider>();
            builder.Services.Replace(ServiceDescriptor.Singleton<IDataChannelContextFactory>(
                static services => services.GetRequiredService<ServerDataChannelProvider>()));
            builder.Services.Replace(ServiceDescriptor.Singleton(new DataChannelModelOptions(enableModel)));
            return builder;
        }

        /// <summary>
        /// Registers a resource declaration before the application provider and optional model are created.
        /// </summary>
        public static IOpcUaServerBuilder AddDataChannelSource(
            this IOpcUaServerBuilder builder,
            DataChannelSource source)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }
            builder.Services.AddSingleton(source);
            return builder;
        }

        /// <summary>
        /// Attaches a caller-owned provider with an independent optional-model switch.
        /// </summary>
        public static StandardServer UseDataChannels(
            this StandardServer server,
            ServerDataChannelProvider provider,
            bool enableModel = false)
        {
            if (server == null)
            {
                throw new ArgumentNullException(nameof(server));
            }
            server.DataChannelProvider = provider ?? throw new ArgumentNullException(nameof(provider));
            server.EnableDataChannelModel = enableModel;
            return server;
        }
    }

    /// <summary>
    /// The optional model switch is separate from transport or configured-flow support.
    /// </summary>
    internal sealed record DataChannelModelOptions(bool Enabled);
}
