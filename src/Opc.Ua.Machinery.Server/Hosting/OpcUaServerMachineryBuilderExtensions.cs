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
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Opc.Ua.Di.Server;
using Opc.Ua.Di.Server.Hosting;
using Opc.Ua.ISA95.Server.Hosting;
using Opc.Ua.Machinery.Server;
using Opc.Ua.Machinery.Server.Results;
using Opc.Ua.Server.Hosting;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// Fluent hosting extensions for the OPC 40001 Machinery server models.
    /// </summary>
    public static class OpcUaServerMachineryBuilderExtensions
    {
        /// <summary>
        /// Registers the stock Machinery node manager, the built-in model
        /// provider and the Machinery configuration pipeline.
        /// </summary>
        /// <param name="builder">The server builder.</param>
        /// <param name="configure">Configures the Machinery options.</param>
        /// <exception cref="InvalidOperationException">
        /// Another registration already owns the Device Integration or ISA-95
        /// address space.
        /// </exception>
        public static IOpcUaServerBuilder AddMachinery(
            this IOpcUaServerBuilder builder,
            Action<MachineryServerOptions>? configure = null)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            var options = new MachineryServerOptions();
            configure?.Invoke(options);
            options.Validate();

            ClaimDiAddressSpace(builder.Services);
            if (options.Parts.HasFlag(MachineryParts.Jobs))
            {
                // OPC 40001-3 loads the ISA-95 Job Control V2 model into this
                // manager, so it owns those namespaces for the server.
                Isa95AddressSpaceOwnership.Claim(builder.Services, nameof(AddMachinery));
            }

            builder.Services.AddOptions<MachineryServerOptions>();
            if (configure != null)
            {
                builder.Services.Configure(configure);
            }

            // The descriptor carries the implementation type explicitly:
            // TryAddEnumerable refuses a factory-only registration because it
            // cannot tell two of them apart.
            builder.Services.TryAddEnumerable(
                ServiceDescriptor.Singleton<IMachineryModelProvider, MachineryModelProvider>(
                    _ => new MachineryModelProvider(options.Parts)));
            EnsureAggregatePipeline<MachineryNodeManager>(builder);

            builder.Services.AddSingleton(static services =>
            {
                MachineryServerOptions resolved = services
                    .GetRequiredService<IOptions<MachineryServerOptions>>()
                    .Value;
                IMachineryModelProvider[] providers = services
                    .GetServices<IMachineryModelProvider>()
                    .ToArray();
                IDiPostSetupRunner? runner = services.GetService<IDiPostSetupRunner>();
                return new MachineryNodeManagerFactory(providers, resolved, runner);
            });
            builder.Services.AddSingleton(static services =>
                new OpcUaServerNodeManagerRegistration(
                    services.GetRequiredService<MachineryNodeManagerFactory>()));
            return builder;
        }

        /// <summary>
        /// Registers the stand-alone OPC 40001-101 result node manager. Use
        /// this for a server whose whole job is to hand out measurement
        /// results: the part needs UA core only, so the server carries neither
        /// Device Integration nor the OPC 40001-1 machine model.
        /// </summary>
        /// <param name="builder">The server builder.</param>
        /// <param name="configure">Configures the result-server options.</param>
        public static IOpcUaServerBuilder AddMachineryResults(
            this IOpcUaServerBuilder builder,
            Action<MachineryResultServerOptions>? configure = null)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            builder.Services.AddOptions<MachineryResultServerOptions>();
            if (configure != null)
            {
                builder.Services.Configure(configure);
            }
            builder.Services.TryAddSingleton<IMachineryResultStore>(
                _ => new InMemoryMachineryResultStore());
            builder.Services.AddSingleton(static services =>
            {
                MachineryResultServerOptions options = services
                    .GetRequiredService<IOptions<MachineryResultServerOptions>>()
                    .Value;
                return new MachineryResultNodeManagerFactory(
                    options,
                    services.GetRequiredService<IMachineryResultStore>());
            });
            builder.Services.AddSingleton(static services =>
                new OpcUaServerNodeManagerRegistration(
                    services.GetRequiredService<MachineryResultNodeManagerFactory>()));
            return builder;
        }

        /// <summary>
        /// Registers an additional compiled Machinery model provider.
        /// </summary>
        /// <typeparam name="TProvider">The compiled model provider type.</typeparam>
        public static IOpcUaServerBuilder AddMachineryModel<
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TProvider>(
                this IOpcUaServerBuilder builder)
            where TProvider : class, IMachineryModelProvider
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            builder.Services.TryAddEnumerable(
                ServiceDescriptor.Singleton<IMachineryModelProvider, TProvider>());
            return builder;
        }

        /// <summary>
        /// Registers a synchronous configurator for the stock Machinery manager.
        /// </summary>
        public static IOpcUaServerBuilder ConfigureMachinery(
            this IOpcUaServerBuilder builder,
            Action<IMachineryBuildContext> configure)
        {
            return builder.ConfigureMachineryFor<MachineryNodeManager>(configure);
        }

        /// <summary>
        /// Registers a one-parameter asynchronous configurator for the stock
        /// Machinery manager. The returned task is awaited during server
        /// startup.
        /// </summary>
        public static IOpcUaServerBuilder ConfigureMachinery(
            this IOpcUaServerBuilder builder,
            Func<IMachineryBuildContext, ValueTask> configure)
        {
            return builder.ConfigureMachineryFor<MachineryNodeManager>(configure);
        }

        /// <summary>
        /// Registers an asynchronous configurator for the stock Machinery
        /// manager.
        /// </summary>
        public static IOpcUaServerBuilder ConfigureMachinery(
            this IOpcUaServerBuilder builder,
            Func<IMachineryBuildContext, CancellationToken, ValueTask> configure)
        {
            return builder.ConfigureMachineryFor<MachineryNodeManager>(configure);
        }

        /// <summary>
        /// Registers a class-based configurator for the stock Machinery manager.
        /// </summary>
        /// <typeparam name="TConfigurator">The Machinery configurator type.</typeparam>
        public static IOpcUaServerBuilder ConfigureMachinery<
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
            TConfigurator>(
                this IOpcUaServerBuilder builder)
            where TConfigurator : class, IMachineryConfigurator
        {
            return builder.ConfigureMachineryFor<MachineryNodeManager, TConfigurator>();
        }

        /// <summary>
        /// Registers a synchronous configurator for an exact custom manager type.
        /// </summary>
        /// <typeparam name="TNodeManager">The exact custom DI node manager type.</typeparam>
        public static IOpcUaServerBuilder ConfigureMachineryFor<TNodeManager>(
            this IOpcUaServerBuilder builder,
            Action<IMachineryBuildContext> configure)
            where TNodeManager : DiNodeManager
        {
            if (configure == null)
            {
                throw new ArgumentNullException(nameof(configure));
            }
            return builder.ConfigureMachineryFor<TNodeManager>((context, _) =>
            {
                configure(context);
                return default;
            });
        }

        /// <summary>
        /// Registers a one-parameter asynchronous configurator for an exact
        /// custom manager type.
        /// </summary>
        /// <typeparam name="TNodeManager">The exact custom DI node manager type.</typeparam>
        public static IOpcUaServerBuilder ConfigureMachineryFor<TNodeManager>(
            this IOpcUaServerBuilder builder,
            Func<IMachineryBuildContext, ValueTask> configure)
            where TNodeManager : DiNodeManager
        {
            if (configure == null)
            {
                throw new ArgumentNullException(nameof(configure));
            }
            return builder.ConfigureMachineryFor<TNodeManager>(
                (context, _) => configure(context));
        }

        /// <summary>
        /// Registers an asynchronous configurator for an exact custom manager
        /// type.
        /// </summary>
        /// <typeparam name="TNodeManager">The exact custom DI node manager type.</typeparam>
        public static IOpcUaServerBuilder ConfigureMachineryFor<TNodeManager>(
            this IOpcUaServerBuilder builder,
            Func<IMachineryBuildContext, CancellationToken, ValueTask> configure)
            where TNodeManager : DiNodeManager
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            if (configure == null)
            {
                throw new ArgumentNullException(nameof(configure));
            }

            EnsureOptionsRegistered(builder.Services);
            EnsureAggregatePipeline<TNodeManager>(builder);
            builder.Services.AddSingleton<IMachineryConfigurationRegistration>(
                new DelegateMachineryConfigurationRegistration(typeof(TNodeManager), configure));
            return builder;
        }

        /// <summary>
        /// Registers a class-based configurator for an exact custom manager type.
        /// </summary>
        /// <typeparam name="TNodeManager">The exact custom DI node manager type.</typeparam>
        /// <typeparam name="TConfigurator">The Machinery configurator type.</typeparam>
        public static IOpcUaServerBuilder ConfigureMachineryFor<
            TNodeManager,
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
            TConfigurator>(
                this IOpcUaServerBuilder builder)
            where TNodeManager : DiNodeManager
            where TConfigurator : class, IMachineryConfigurator
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            EnsureOptionsRegistered(builder.Services);
            EnsureAggregatePipeline<TNodeManager>(builder);
            builder.Services.TryAddSingleton<TConfigurator>();
            builder.Services.AddSingleton<IMachineryConfigurationRegistration>(
                new ClassMachineryConfigurationRegistration<TConfigurator>(typeof(TNodeManager)));
            return builder;
        }

        private static void EnsureAggregatePipeline<TNodeManager>(IOpcUaServerBuilder builder)
            where TNodeManager : DiNodeManager
        {
            foreach (ServiceDescriptor descriptor in builder.Services)
            {
                if (descriptor.ServiceType == typeof(MachineryPipelineMarker<TNodeManager>))
                {
                    return;
                }
            }

            builder.Services.AddSingleton(new MachineryPipelineMarker<TNodeManager>());
            builder.ConfigureDevicesFor<TNodeManager>(
                RunMachineryConfigurationsAsync<TNodeManager>);
        }

        private static void EnsureOptionsRegistered(IServiceCollection services)
        {
            services.AddOptions<MachineryServerOptions>();
        }

        private static async ValueTask RunMachineryConfigurationsAsync<TNodeManager>(
            IDiPostSetupContext postSetupContext)
            where TNodeManager : DiNodeManager
        {
            if (postSetupContext.Manager.GetType() != typeof(TNodeManager))
            {
                return;
            }

            MachineryServerOptions options = postSetupContext
                .GetRequiredService<IOptions<MachineryServerOptions>>()
                .Value;
            var context = new MachineryBuildContext(
                postSetupContext.Manager,
                options,
                postSetupContext.CancellationToken,
                postSetupContext);
            IEnumerable<IMachineryConfigurationRegistration> registrations =
                postSetupContext.GetRequiredService<
                    IEnumerable<IMachineryConfigurationRegistration>>();

            foreach (IMachineryConfigurationRegistration registration in registrations)
            {
                if (registration.TargetManagerType == typeof(TNodeManager))
                {
                    await registration.ConfigureAsync(context).ConfigureAwait(false);
                }
            }
            await context.SealAsync(postSetupContext.CancellationToken).ConfigureAwait(false);
        }

        private static void ClaimDiAddressSpace(IServiceCollection services)
        {
            foreach (ServiceDescriptor descriptor in services)
            {
                if (descriptor.ServiceType == typeof(DiAddressSpaceOwnership))
                {
                    string ownerName =
                        (descriptor.ImplementationInstance as DiAddressSpaceOwnership)?.OwnerName ??
                        "another DI-aware hosting registration";
                    throw new InvalidOperationException(
                        $"The OPC UA DI namespace and address space are already owned by " +
                        $"'{ownerName}'. AddMachinery cannot register a second DI-owning " +
                        $"manager. Load the Machinery models into the existing manager with " +
                        $"nodes.AddMachineryTypeSystem(context, parts) and configure them " +
                        $"with ConfigureMachineryFor<TNodeManager>().");
                }
            }

            services.AddSingleton(new DiAddressSpaceOwnership(nameof(AddMachinery)));
        }

        private interface IMachineryConfigurationRegistration
        {
            Type TargetManagerType { get; }

            ValueTask ConfigureAsync(IMachineryBuildContext context);
        }

        private sealed class DelegateMachineryConfigurationRegistration
            : IMachineryConfigurationRegistration
        {
            private readonly Func<IMachineryBuildContext, CancellationToken, ValueTask> m_configure;

            public DelegateMachineryConfigurationRegistration(
                Type targetManagerType,
                Func<IMachineryBuildContext, CancellationToken, ValueTask> configure)
            {
                TargetManagerType = targetManagerType;
                m_configure = configure;
            }

            public Type TargetManagerType { get; }

            public ValueTask ConfigureAsync(IMachineryBuildContext context)
            {
                return m_configure(context, context.CancellationToken);
            }
        }

        private sealed class ClassMachineryConfigurationRegistration<TConfigurator>
            : IMachineryConfigurationRegistration
            where TConfigurator : class, IMachineryConfigurator
        {
            public ClassMachineryConfigurationRegistration(Type targetManagerType)
            {
                TargetManagerType = targetManagerType;
            }

            public Type TargetManagerType { get; }

            public ValueTask ConfigureAsync(IMachineryBuildContext context)
            {
                return context.GetRequiredService<TConfigurator>().ConfigureAsync(
                    context,
                    context.CancellationToken);
            }
        }

        private sealed class MachineryPipelineMarker<TNodeManager>
            where TNodeManager : DiNodeManager;
    }
}
