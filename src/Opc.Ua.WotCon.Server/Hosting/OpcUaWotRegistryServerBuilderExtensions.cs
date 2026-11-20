// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

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
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
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
            services.TryAddSingleton<IWotProjectionEventPublisher, WotProjectionEventPublisher>();
            services.TryAddSingleton<IWotProjectionConditionFactory, WotProjectionConditionFactory>();
            services.TryAddSingleton(new WotProjectionBindingRuntimeOptions());

            services.TryAddSingleton<IWotProjectionBindingRuntimeFactory>(sp =>
                new WotProjectionBindingRuntimeFactory(
                    sp.GetRequiredService<IWotBindingChannelFactory>(),
                    sp.GetRequiredService<IWotTargetVariableResolver>(),
                    sp.GetRequiredService<IWotProjectionEventPublisher>(),
                    sp.GetRequiredService<IWotProjectionConditionFactory>(),
                    sp.GetRequiredService<WotProjectionBindingRuntimeOptions>()));

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
                return new WotRegistryService(
                    store, options.Bounds, options.IdentityBindings);
            });

            services.TryAddSingleton(sp =>
                sp.GetRequiredService<IWotRegistryService>() as IWotTypedRegistryService ??
                throw new InvalidOperationException("The registered registry does not support typed provisioning."));

            services.TryAddSingleton(sp =>
                sp.GetRequiredService<IWotRegistryService>() as IWotRegistryVersionLeaseProvider ??
                throw new InvalidOperationException("The registered registry does not support Version leases."));

            services.TryAddSingleton(sp =>
                sp.GetRequiredService<IWotRegistryService>() as IWotRegistryDependencySnapshotProvider ??
                throw new InvalidOperationException("The registered registry does not support dependency snapshots."));

            services.TryAddSingleton(sp =>
                sp.GetRequiredService<IWotRegistryService>() as IWotRegistryRecoveryResolver ??
                throw new InvalidOperationException("The registered registry cannot resolve recovery evidence."));

            services.TryAddSingleton<IWotProjectionHost>(sp =>
                new LifecycleWotProjectionHost(
                    sp.GetRequiredService<INodeManagerLifecycle>(),
                    sp.GetRequiredService<IWotProjectionBindingRuntimeFactory>()));

            // The live view host publishes projection Views into the running address space
            // via the NodeManager lifecycle; a coordinator built without DI falls back to
            // the in-memory host, which is also the seam's test double.
            services.TryAddSingleton<IWotViewProjectionHost>(sp =>
                new LifecycleWotViewProjectionHost(
                    sp.GetRequiredService<INodeManagerLifecycle>(),
                    sp.GetRequiredService<WotRegistryServerOptions>().RetirementPolicy));

            services.TryAddSingleton(sp =>
                sp.GetRequiredService<IWotViewProjectionHost>() as IWotPreparedViewProjectionHost ??
                throw new InvalidOperationException("The registered View host does not support prepared publication."));

            services.TryAddSingleton<IWotUnitMappingProvider, WotQudtUnitMappingProvider>();
            services.TryAddSingleton(sp =>
            {
                WotRegistryServerOptions options =
                    sp.GetRequiredService<WotRegistryServerOptions>();
                // A converter options instance registered in DI replaces these defaults (TryAdd), so a
                // deployment holding a large partitioned model states its resolver budget explicitly.
                var converterOptions = new WotNodeSetConverterOptions
                {
                    MaxJsonDepth = options.Bounds.MaxJsonDepth,
                    MaxJsonDocumentSize = options.Bounds.MaxDocumentBytes,
                    MaxResolverDocumentBytes = options.Bounds.MaxDocumentBytes,
                    DocumentSetMode = options.DocumentSetMode,
                    ProjectionFormProvider = sp.GetService<IWotProjectionFormProvider>() ??
                        options.ProjectionFormProvider,
                    UnitMappingProvider = sp.GetRequiredService<IWotUnitMappingProvider>(),
                    ValueEncodingContext = sp.GetService<IServiceMessageContext>()
                };
                converterOptions.Validate();
                return converterOptions;
            });

            services.TryAddSingleton(sp =>
                new WotMaterializationCoordinator(
                    sp.GetRequiredService<IWotRegistryService>(),
                    sp.GetRequiredService<IWotProjectionHost>(),
                    sp.GetRequiredService<IWotBinderRegistry>(),
                    sp.GetRequiredService<WotNodeSetConverterOptions>(),
                    // Both seams are optional: a deployment that registers neither gets exactly
                    // the previous behaviour. Registering an IWotDocumentConverter replaces the
                    // Thing Description to NodeSet conversion; registering IWotNodeSetContributor
                    // instances adds nodes (typically controller-specific StructureType DataTypes)
                    // to the converted NodeSet before it is materialized.
                    sp.GetService<IWotDocumentConverter>(),
                    sp.GetServices<IWotNodeSetContributor>(),
                    sp.GetService<IWotNodeSetResolver>(),
                    sp.GetService<IWotViewProjectionHost>()));

            services.TryAddSingleton<IWotRefreshCaptureProvider>(sp =>
                sp.GetRequiredService<WotMaterializationCoordinator>());

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
