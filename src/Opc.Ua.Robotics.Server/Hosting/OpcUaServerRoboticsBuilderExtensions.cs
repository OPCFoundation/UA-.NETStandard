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
using Opc.Ua.Robotics.Server;
using Opc.Ua.Server.Hosting;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// Fluent hosting extensions for the OPC UA Robotics server model.
    /// </summary>
    public static class OpcUaServerRoboticsBuilderExtensions
    {
        /// <summary>
        /// Registers the stock Robotics node manager, built-in model provider,
        /// and Robotics configuration pipeline.
        /// </summary>
        public static IOpcUaServerBuilder AddRobotics(
            this IOpcUaServerBuilder builder,
            Action<RoboticsServerOptions>? configure = null)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            ClaimDiAddressSpace(builder.Services);
            builder.Services.AddOptions<RoboticsServerOptions>();
            if (configure != null)
            {
                builder.Services.Configure(configure);
            }

            builder.Services.TryAddEnumerable(
                ServiceDescriptor.Singleton<IRoboticsModelProvider, RoboticsModelProvider>());
            EnsureAggregatePipeline<RoboticsNodeManager>(builder);

            builder.Services.AddSingleton(static services =>
            {
                RoboticsServerOptions options = services
                    .GetRequiredService<IOptions<RoboticsServerOptions>>()
                    .Value;
                IRoboticsModelProvider[] providers = services
                    .GetServices<IRoboticsModelProvider>()
                    .ToArray();
                IDiPostSetupRunner? runner = services.GetService<IDiPostSetupRunner>();
                return new RoboticsNodeManagerFactory(providers, options, runner);
            });
            builder.Services.AddSingleton(static services =>
                new OpcUaServerNodeManagerRegistration(
                    services.GetRequiredService<RoboticsNodeManagerFactory>()));
            return builder;
        }

        /// <summary>
        /// Registers an additional compiled Robotics model provider.
        /// </summary>
        /// <typeparam name="TProvider">
        /// The compiled model provider type.
        /// </typeparam>
        public static IOpcUaServerBuilder AddRoboticsModel<
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TProvider>(
                this IOpcUaServerBuilder builder)
            where TProvider : class, IRoboticsModelProvider
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            builder.Services.TryAddEnumerable(
                ServiceDescriptor.Singleton<IRoboticsModelProvider, TProvider>());
            return builder;
        }

        /// <summary>
        /// Registers a synchronous configurator for the stock Robotics manager.
        /// </summary>
        public static IOpcUaServerBuilder ConfigureRobotics(
            this IOpcUaServerBuilder builder,
            Action<IRoboticsBuildContext> configure)
        {
            return builder.ConfigureRoboticsFor<RoboticsNodeManager>(configure);
        }

        /// <summary>
        /// Registers a one-parameter asynchronous configurator for the stock
        /// Robotics manager. The returned task is awaited during server startup.
        /// </summary>
        public static IOpcUaServerBuilder ConfigureRobotics(
            this IOpcUaServerBuilder builder,
            Func<IRoboticsBuildContext, ValueTask> configure)
        {
            return builder.ConfigureRoboticsFor<RoboticsNodeManager>(configure);
        }

        /// <summary>
        /// Registers an asynchronous configurator for the stock Robotics manager.
        /// </summary>
        public static IOpcUaServerBuilder ConfigureRobotics(
            this IOpcUaServerBuilder builder,
            Func<IRoboticsBuildContext, CancellationToken, ValueTask> configure)
        {
            return builder.ConfigureRoboticsFor<RoboticsNodeManager>(configure);
        }

        /// <summary>
        /// Registers a class-based configurator for the stock Robotics manager.
        /// </summary>
        /// <typeparam name="TConfigurator">
        /// The Robotics configurator type.
        /// </typeparam>
        public static IOpcUaServerBuilder ConfigureRobotics<
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
            TConfigurator>(
                this IOpcUaServerBuilder builder)
            where TConfigurator : class, IRoboticsConfigurator
        {
            return builder.ConfigureRoboticsFor<RoboticsNodeManager, TConfigurator>();
        }

        /// <summary>
        /// Registers a synchronous configurator for an exact custom manager type.
        /// </summary>
        /// <typeparam name="TNodeManager">
        /// The exact custom DI node manager type.
        /// </typeparam>
        public static IOpcUaServerBuilder ConfigureRoboticsFor<TNodeManager>(
            this IOpcUaServerBuilder builder,
            Action<IRoboticsBuildContext> configure)
            where TNodeManager : DiNodeManager
        {
            if (configure == null)
            {
                throw new ArgumentNullException(nameof(configure));
            }
            return builder.ConfigureRoboticsFor<TNodeManager>((context, _) =>
            {
                configure(context);
                return default;
            });
        }

        /// <summary>
        /// Registers a one-parameter asynchronous configurator for an exact custom
        /// manager type. The returned task is awaited during server startup.
        /// </summary>
        /// <typeparam name="TNodeManager">
        /// The exact custom DI node manager type.
        /// </typeparam>
        public static IOpcUaServerBuilder ConfigureRoboticsFor<TNodeManager>(
            this IOpcUaServerBuilder builder,
            Func<IRoboticsBuildContext, ValueTask> configure)
            where TNodeManager : DiNodeManager
        {
            if (configure == null)
            {
                throw new ArgumentNullException(nameof(configure));
            }

            return builder.ConfigureRoboticsFor<TNodeManager>(
                (context, _) => configure(context));
        }

        /// <summary>
        /// Registers an asynchronous configurator for an exact custom manager type.
        /// </summary>
        /// <typeparam name="TNodeManager">
        /// The exact custom DI node manager type.
        /// </typeparam>
        public static IOpcUaServerBuilder ConfigureRoboticsFor<TNodeManager>(
            this IOpcUaServerBuilder builder,
            Func<IRoboticsBuildContext, CancellationToken, ValueTask> configure)
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
            builder.Services.AddSingleton<IRoboticsConfigurationRegistration>(
                new DelegateRoboticsConfigurationRegistration(
                    typeof(TNodeManager),
                    configure));
            return builder;
        }

        /// <summary>
        /// Registers a class-based configurator for an exact custom manager type.
        /// </summary>
        /// <typeparam name="TNodeManager">
        /// The exact custom DI node manager type.
        /// </typeparam>
        /// <typeparam name="TConfigurator">
        /// The Robotics configurator type.
        /// </typeparam>
        public static IOpcUaServerBuilder ConfigureRoboticsFor<
            TNodeManager,
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
            TConfigurator>(
                this IOpcUaServerBuilder builder)
            where TNodeManager : DiNodeManager
            where TConfigurator : class, IRoboticsConfigurator
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            EnsureOptionsRegistered(builder.Services);
            EnsureAggregatePipeline<TNodeManager>(builder);
            builder.Services.TryAddSingleton<TConfigurator>();
            builder.Services.AddSingleton<IRoboticsConfigurationRegistration>(
                new ClassRoboticsConfigurationRegistration<TConfigurator>(
                    typeof(TNodeManager)));
            return builder;
        }

        private static void EnsureAggregatePipeline<TNodeManager>(
            IOpcUaServerBuilder builder)
            where TNodeManager : DiNodeManager
        {
            foreach (ServiceDescriptor descriptor in builder.Services)
            {
                if (descriptor.ServiceType == typeof(RoboticsPipelineMarker<TNodeManager>))
                {
                    return;
                }
            }

            builder.Services.AddSingleton(new RoboticsPipelineMarker<TNodeManager>());
            builder.ConfigureDevicesFor<TNodeManager>(
                RunRoboticsConfigurationsAsync<TNodeManager>);
        }

        private static void EnsureOptionsRegistered(IServiceCollection services)
        {
            services.AddOptions<RoboticsServerOptions>();
        }

        private static async ValueTask RunRoboticsConfigurationsAsync<TNodeManager>(
            IDiPostSetupContext postSetupContext)
            where TNodeManager : DiNodeManager
        {
            if (postSetupContext.Manager.GetType() != typeof(TNodeManager))
            {
                return;
            }

            RoboticsServerOptions options = postSetupContext
                .GetRequiredService<IOptions<RoboticsServerOptions>>()
                .Value;
            var context = new RoboticsBuildContext(
                postSetupContext.Manager,
                options,
                postSetupContext.CancellationToken,
                postSetupContext);
            IEnumerable<IRoboticsConfigurationRegistration> registrations =
                postSetupContext.GetRequiredService<
                    IEnumerable<IRoboticsConfigurationRegistration>>();

            foreach (IRoboticsConfigurationRegistration registration in registrations)
            {
                if (registration.TargetManagerType == typeof(TNodeManager))
                {
                    await registration.ConfigureAsync(context).ConfigureAwait(false);
                }
            }
            context.Seal();
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
                        $"'{ownerName}'. AddRobotics cannot register a second DI-owning manager.");
                }
            }

            services.AddSingleton(new DiAddressSpaceOwnership(nameof(AddRobotics)));
        }

        private interface IRoboticsConfigurationRegistration
        {
            Type TargetManagerType { get; }

            ValueTask ConfigureAsync(IRoboticsBuildContext context);
        }

        private sealed class DelegateRoboticsConfigurationRegistration
            : IRoboticsConfigurationRegistration
        {
            private readonly Func<
                IRoboticsBuildContext,
                CancellationToken,
                ValueTask> m_configure;

            public DelegateRoboticsConfigurationRegistration(
                Type targetManagerType,
                Func<IRoboticsBuildContext, CancellationToken, ValueTask> configure)
            {
                TargetManagerType = targetManagerType;
                m_configure = configure;
            }

            public Type TargetManagerType { get; }

            public ValueTask ConfigureAsync(IRoboticsBuildContext context)
            {
                return m_configure(context, context.CancellationToken);
            }
        }

        private sealed class ClassRoboticsConfigurationRegistration<TConfigurator>
            : IRoboticsConfigurationRegistration
            where TConfigurator : class, IRoboticsConfigurator
        {
            public ClassRoboticsConfigurationRegistration(Type targetManagerType)
            {
                TargetManagerType = targetManagerType;
            }

            public Type TargetManagerType { get; }

            public ValueTask ConfigureAsync(IRoboticsBuildContext context)
            {
                return context.GetRequiredService<TConfigurator>().ConfigureAsync(
                    context,
                    context.CancellationToken);
            }
        }

        private sealed class RoboticsPipelineMarker<TNodeManager>
            where TNodeManager : DiNodeManager;

    }
}
