/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Opc.Ua;
using Opc.Ua.Server;
using Opc.Ua.Server.Hosting;
using Opc.Ua.Wot;
using Opc.Ua.WotCon.Bindings;
using Opc.Ua.WotCon.Server;
using Opc.Ua.WotCon.Server.Materialization;
using Opc.Ua.WotCon.Server.Registry;
using Opc.Ua.XRegistry.Server;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// <see cref="IOpcUaBuilder"/> extensions that host the WoT Connectivity 1.1
    /// registry (<c>WoTRegistry</c>) on the OPC UA server registered via
    /// <c>.AddServer(...)</c>. The registry service, materialization coordinator,
    /// binder registry and projection host are registered as singletons; the
    /// stable <see cref="WotRegistryNodeManager"/> is attached at server start.
    /// </summary>
    public static class OpcUaWotRegistryServerBuilderExtensions
    {
        /// <summary>
        /// Default configuration section for the registry options.
        /// </summary>
        public const string DefaultConfigurationSection = "OpcUa:WotConRegistry:Server";

        /// <summary>
        /// Registers the WoT Connectivity 1.1 registry NodeManager configured by
        /// <paramref name="configure"/>.
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        /// <exception cref="ArgumentNullException"></exception>
        public static IOpcUaBuilder AddWotRegistryServer(
            this IOpcUaBuilder builder,
            Action<WotRegistryServerOptions>? configure = null)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            if (configure is not null)
            {
                builder.Services.AddOptions<WotRegistryServerOptions>().Configure(configure);
            }
            else
            {
                builder.Services.AddOptions<WotRegistryServerOptions>();
            }
            RegisterCommonServices(builder.Services);
            return builder;
        }

        /// <summary>
        /// Registers the WoT Connectivity 1.1 registry NodeManager with options
        /// bound from the supplied configuration section.
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        /// <exception cref="ArgumentNullException"></exception>
        public static IOpcUaBuilder AddWotRegistryServer(
            this IOpcUaBuilder builder,
            IConfiguration configuration)
        {
            if (configuration is null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }
            return builder.AddWotRegistryServer(
                configuration.GetSection(DefaultConfigurationSection));
        }

        /// <summary>
        /// Registers the WoT Connectivity 1.1 registry NodeManager with options
        /// bound from the supplied configuration section.
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        /// <exception cref="ArgumentNullException"></exception>
        public static IOpcUaBuilder AddWotRegistryServer(
            this IOpcUaBuilder builder,
            IConfigurationSection section)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            if (section is null)
            {
                throw new ArgumentNullException(nameof(section));
            }
            builder.Services.AddOptions<WotRegistryServerOptions>().Bind(section);
            RegisterCommonServices(builder.Services);
            return builder;
        }

        private static void RegisterCommonServices(IServiceCollection services)
        {
            services.TryAddSingleton(sp =>
                sp.GetRequiredService<IOptions<WotRegistryServerOptions>>().Value
                ?? new WotRegistryServerOptions());

            services.EnsureWotBinderRegistry();

            services.TryAddSingleton<IWotTargetVariableResolver>(new WotTargetVariableResolver());

            services.TryAddSingleton<IWotProjectionBindingRuntimeFactory>(sp =>
                new WotProjectionBindingRuntimeFactory(
                    sp.GetRequiredService<IWotBindingChannelFactory>(),
                    sp.GetRequiredService<IWotTargetVariableResolver>()));

            services.TryAddSingleton<IWotRegistryService>(sp =>
            {
                WotRegistryServerOptions options =
                    sp.GetRequiredService<WotRegistryServerOptions>();
                // A store registered in DI wins over one set on the options, so a deployment can
                // supply a shared store the same way the xRegistry server does.
                IXRegistryResourceStore? resourceStore =
                    sp.GetService<IXRegistryResourceStore>() ?? options.ResourceStore;
                IWotRegistryStore store = string.IsNullOrEmpty(options.StorageFolder)
                    ? new InMemoryWotRegistryStore()
                    : resourceStore is null
                        ? new FileWotRegistryStore(options.StorageFolder!)
                        : new FileWotRegistryStore(options.StorageFolder!, resourceStore);
                return new WotRegistryService(store, options.Bounds);
            });

            services.TryAddSingleton<IWotProjectionHost>(sp =>
                new LifecycleWotProjectionHost(
                    sp.GetRequiredService<INodeManagerLifecycle>(),
                    sp.GetRequiredService<IWotProjectionBindingRuntimeFactory>()));

            services.TryAddSingleton(sp =>
            {
                WotRegistryServerOptions options =
                    sp.GetRequiredService<WotRegistryServerOptions>();
                var converterOptions = new WotNodeSetConverterOptions
                {
                    MaxJsonDocumentSize = options.Bounds.MaxDocumentBytes,
                    MaxResolverDocumentBytes = options.Bounds.MaxDocumentBytes
                };
                return new WotMaterializationCoordinator(
                    sp.GetRequiredService<IWotRegistryService>(),
                    sp.GetRequiredService<IWotProjectionHost>(),
                    sp.GetRequiredService<IWotBinderRegistry>(),
                    converterOptions,
                    // Both seams are optional: a deployment that registers neither gets exactly
                    // the previous behaviour. Registering an IWotDocumentConverter replaces the
                    // Thing Description to NodeSet conversion; registering IWotNodeSetContributor
                    // instances adds nodes (typically controller-specific StructureType DataTypes)
                    // to the converted NodeSet before it is materialized.
                    sp.GetService<IWotDocumentConverter>(),
                    sp.GetServices<IWotNodeSetContributor>(),
                    sp.GetService<IWotNodeSetResolver>());
            });

            services.TryAddSingleton(sp =>
                new WotRegistryNodeManagerFactory(
                    sp.GetRequiredService<WotRegistryServerOptions>(),
                    sp.GetRequiredService<IWotRegistryService>(),
                    sp.GetRequiredService<WotMaterializationCoordinator>()));

            services.AddSingleton(sp =>
                new OpcUaServerNodeManagerRegistration(
                    sp.GetRequiredService<WotRegistryNodeManagerFactory>()));

            services.AddOpcUa();
        }
    }
}
