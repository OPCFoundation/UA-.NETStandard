/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Opc.Ua.Configuration;
using Opc.Ua.Server;
using Opc.Ua.Server.Hosting;

namespace Opc.Ua.Redundancy.Server
{
    /// <summary>
    /// Fluent registration of OPC 10000-4 §6.6 server redundancy metadata publishing on the
    /// <see cref="IOpcUaServerBuilder"/>.
    /// </summary>
    public static class ServerRedundancyBuilderExtensions
    {
        /// <summary>
        /// Populates the live <c>Server.ServerRedundancy</c> model from the
        /// supplied configuration after the hosted server starts. Non-transparent redundancy depends on
        /// <c>Server.ServiceLevel</c> changes for client Failover decisions; pair this with
        /// <c>AddServerServiceLevel(new LeaderServiceLevelProvider(...))</c> or another
        /// <see cref="IServiceLevelProvider"/> registered in dependency injection.
        /// </summary>
        /// <param name="builder">The server builder.</param>
        /// <param name="configure">Optional server redundancy configuration.</param>
        /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <c>null</c>.</exception>
        public static IOpcUaServerBuilder AddServerRedundancy(
            this IOpcUaServerBuilder builder,
            Action<ServerRedundancyOptions>? configure = null)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            var options = new ServerRedundancyOptions();
            configure?.Invoke(options);
            AddDiscoveryCapabilityConfiguration(builder, options);
            builder.Services.AddSingleton(options);
            builder.Services.AddSingleton<ServerRedundancyController>();
            builder.Services.TryAddSingleton<IServerRedundancyController>(
                sp => sp.GetRequiredService<ServerRedundancyController>());
            builder.Services.TryAddSingleton<IRedundantServerSetProvider>(
                new ConfiguredRedundantServerSetProvider(options));
            builder.Services.AddSingleton<IServerStartupTask>(sp =>
                new ServerRedundancyStartupTask(
                    sp.GetRequiredService<ServerRedundancyController>(),
                    !sp.GetServices<IServiceLevelProvider>().Any()));
            return builder;
        }

        /// <summary>
        /// Wires the standard <c>Server.RequestServerStateChange</c> method for OPC 10000-4 §6.6.5
        /// administrator-driven manual Failover to Maintenance or NoData.
        /// </summary>
        /// <param name="builder">The server builder.</param>
        /// <param name="configure">Optional method wiring configuration.</param>
        /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <c>null</c>.</exception>
        public static IOpcUaServerBuilder AddRequestServerStateChange(
            this IOpcUaServerBuilder builder,
            Action<RequestServerStateChangeOptions>? configure = null)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            var options = new RequestServerStateChangeOptions();
            configure?.Invoke(options);
            builder.Services.AddSingleton<IServerStartupTask>(sp =>
                new RequestServerStateChangeStartupTask(
                    options,
                    sp.GetService<IServiceLevelProvider>() as IServiceLevelController));
            return builder;
        }

        /// <summary>
        /// Wires the standard <c>Server.RequestServerStateChange</c> method for OPC 10000-4 §6.6.5
        /// administrator-driven manual Failover to Maintenance or NoData.
        /// </summary>
        /// <param name="builder">The server builder.</param>
        /// <param name="configure">Optional method wiring configuration.</param>
        /// <remarks>
        /// Use <see cref="AddRequestServerStateChange(IOpcUaServerBuilder, Action{RequestServerStateChangeOptions}?)"/>.
        /// </remarks>
        [Obsolete("Use AddRequestServerStateChange.")]
        public static IOpcUaServerBuilder AddManualFailover(
            this IOpcUaServerBuilder builder,
            Action<RequestServerStateChangeOptions>? configure = null)
        {
            return AddRequestServerStateChange(builder, configure);
        }

        private static void AddDiscoveryCapabilityConfiguration(
            IOpcUaServerBuilder builder,
            ServerRedundancyOptions redundancyOptions)
        {
            builder.Services.AddOptions<OpcUaServerOptions>().Configure(serverOptions =>
            {
                Action<IApplicationConfigurationBuilderServerSelected>? previous =
                    serverOptions.ConfigureBuilder;
                serverOptions.ConfigureBuilder = configurationBuilder =>
                {
                    previous?.Invoke(configurationBuilder);
                    if (redundancyOptions.IsNonTransparentMode &&
                        redundancyOptions.AdvertiseNtrsCapability)
                    {
                        configurationBuilder.AddServerCapabilities("NTRS");
                    }
                };
            });
        }
    }
}
