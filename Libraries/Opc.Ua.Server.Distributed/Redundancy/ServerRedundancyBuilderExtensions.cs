/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person
 * obtaining a copy of this software and associated documentation
 * files (the "Software"), to deal in the Software without
 * restriction, including without limitation the rights to use,
 * copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following
 * conditions:
 *
 * The above copyright notice and this permission notice shall be
 * included in all copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
 * EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES
 * OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
 * NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
 * HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
 * WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
 * OTHER DEALINGS IN THE SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using Microsoft.Extensions.DependencyInjection;
using Opc.Ua.Configuration;
using Opc.Ua.Server;
using Opc.Ua.Server.Hosting;

namespace Opc.Ua.Server.Distributed
{
    /// <summary>
    /// Fluent registration of OPC 10000-4 §6.6 server redundancy metadata publishing on the
    /// <see cref="IOpcUaServerBuilder"/>.
    /// </summary>
    public static class ServerRedundancyBuilderExtensions
    {
        /// <summary>
        /// Populates the live <c>Server.ServerRedundancy</c> model from the
        /// supplied configuration after the hosted server starts.
        /// </summary>
        /// <param name="builder">The server builder.</param>
        /// <param name="configure">Optional server redundancy configuration.</param>
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
            builder.Services.AddSingleton<IRedundantServerSetProvider>(
                new ConfiguredRedundantServerSetProvider(options));
            builder.Services.AddSingleton<IServerStartupTask>(
                new ServerRedundancyStartupTask(options));
            return builder;
        }

        /// <summary>
        /// Wires the standard <c>Server.RequestServerStateChange</c> method for OPC 10000-4 §6.6.5
        /// administrator-driven Maintenance or NoData Failover.
        /// </summary>
        /// <param name="builder">The server builder.</param>
        /// <param name="configure">Optional method wiring configuration.</param>
        public static IOpcUaServerBuilder AddManualFailover(
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
