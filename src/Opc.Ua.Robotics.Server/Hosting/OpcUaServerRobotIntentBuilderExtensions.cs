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
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Opc.Ua.Robotics.Server;
using Opc.Ua.Robotics.Server.Hosting;
using Opc.Ua.RobotIntent;
using Opc.Ua.Server;
using Opc.Ua.Server.Hosting;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// Hosting extensions for OPC UA Robot Intent servers.
    /// </summary>
    public static class OpcUaServerRobotIntentBuilderExtensions
    {
        /// <summary>
        /// Registers the standalone Robot Intent node manager.
        /// </summary>
        public static IOpcUaServerBuilder AddRobotIntent(
            this IOpcUaServerBuilder builder,
            Action<RobotIntentServerOptions>? configure = null)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            builder.Services.AddOptions<RobotIntentServerOptions>();
            if (configure != null)
            {
                builder.Services.Configure(configure);
            }
            builder.Services.TryAddEnumerable(
                ServiceDescriptor.Singleton<IRobotIntentModelProvider, RobotIntentModelProvider>());
            builder.Services.TryAddEnumerable(
                ServiceDescriptor.Singleton<IServerStartupTask, RobotIntentHostStartupTask>());
            builder.Services.TryAddSingleton<IRobotIntentPostSetupRunner, RobotIntentPostSetupRunner>();
            builder.Services.TryAddSingleton<IIntentExecutor, RobotIntentRejectingExecutor>();
            builder.Services.AddSingleton(static services =>
            {
                RobotIntentServerOptions options = services
                    .GetRequiredService<IOptions<RobotIntentServerOptions>>()
                    .Value;
                IRobotIntentModelProvider[] providers = [.. services.GetServices<IRobotIntentModelProvider>()];
                return new RobotIntentNodeManagerFactory(
                    providers,
                    options,
                    services.GetService<IRobotIntentPostSetupRunner>());
            });
            builder.Services.AddSingleton(static services =>
            {
                RobotIntentServerOptions options = services
                    .GetRequiredService<IOptions<RobotIntentServerOptions>>()
                    .Value;
                IRobotIntentModelProvider[] providers = [.. services.GetServices<IRobotIntentModelProvider>()];
                return new RobotIntentHostedNodeManagerFactory(
                    providers,
                    options,
                    services.GetService<IRobotIntentPostSetupRunner>(),
                    services);
            });
            builder.Services.AddSingleton(static services =>
                new OpcUaServerNodeManagerRegistration(
                    services.GetRequiredService<RobotIntentHostedNodeManagerFactory>()));
            return builder;
        }

        /// <summary>
        /// Registers an application executor for Robot Intent controllers.
        /// </summary>
        /// <typeparam name="TExecutor">
        /// The executor implementation type.
        /// </typeparam>
        public static IOpcUaServerBuilder AddRobotIntentExecutor<
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TExecutor>(
            this IOpcUaServerBuilder builder)
            where TExecutor : class, IIntentExecutor
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            // Register the concrete type and resolve the interface from it, so an
            // application that also injects TExecutor directly - to observe the arm
            // it is driving, for example - shares the instance the intents run on.
            // Registering IIntentExecutor against the type would construct a second
            // executor, leaving the application watching a device that never moves.
            builder.Services.AddSingleton<TExecutor>();
            builder.Services.AddSingleton<IIntentExecutor>(
                services => services.GetRequiredService<TExecutor>());
            return builder;
        }

        /// <summary>
        /// Registers an application executor for one Robot Intent controller browse name.
        /// </summary>
        /// <typeparam name="TExecutor">
        /// The executor implementation type.
        /// </typeparam>
        public static IOpcUaServerBuilder AddRobotIntentExecutor<
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TExecutor>(
            this IOpcUaServerBuilder builder,
            string controllerBrowseName)
            where TExecutor : class, IIntentExecutor
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            builder.Services.AddSingleton<TExecutor>();
            builder.Services.AddSingleton(services => new RobotIntentControllerExecutorRegistration(
                controllerBrowseName,
                services.GetRequiredService<TExecutor>()));
            return builder;
        }

        /// <summary>
        /// Registers an executor instance for one Robot Intent controller browse name.
        /// </summary>
        public static IOpcUaServerBuilder AddRobotIntentExecutor(
            this IOpcUaServerBuilder builder,
            string controllerBrowseName,
            IIntentExecutor executor)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            builder.Services.AddSingleton(
                new RobotIntentControllerExecutorRegistration(controllerBrowseName, executor));
            return builder;
        }

        /// <summary>
        /// Registers a Robot Intent configurator for the standalone manager.
        /// </summary>
        public static IOpcUaServerBuilder ConfigureRobotIntent(
            this IOpcUaServerBuilder builder,
            Func<IRobotIntentBuildContext, CancellationToken, ValueTask> configure)
        {
            return builder.ConfigureRobotIntentFor<RobotIntentNodeManager>(configure);
        }

        /// <summary>
        /// Registers a Robot Intent configurator for the standalone manager.
        /// </summary>
        public static IOpcUaServerBuilder ConfigureRobotIntent(
            this IOpcUaServerBuilder builder,
            Action<IRobotIntentBuildContext> configure)
        {
            if (configure == null)
            {
                throw new ArgumentNullException(nameof(configure));
            }
            return builder.ConfigureRobotIntent((context, _) =>
            {
                configure(context);
                return default;
            });
        }

        /// <summary>
        /// Registers a Robot Intent configurator for the standalone Robot Intent node manager.
        /// </summary>
        /// <typeparam name="TNodeManager">
        /// The standalone Robot Intent node manager type.
        /// </typeparam>
        public static IOpcUaServerBuilder ConfigureRobotIntentFor<TNodeManager>(
            this IOpcUaServerBuilder builder,
            Func<IRobotIntentBuildContext, CancellationToken, ValueTask> configure)
            where TNodeManager : AsyncCustomNodeManager
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            if (configure == null)
            {
                throw new ArgumentNullException(nameof(configure));
            }
            if (typeof(TNodeManager) != typeof(RobotIntentNodeManager))
            {
                throw new NotSupportedException(
                    "ConfigureRobotIntentFor<TNodeManager> is supported only for RobotIntentNodeManager. " +
                    "Use ConfigureRobotIntent for the standalone manager.");
            }
            builder.Services.TryAddSingleton<IRobotIntentPostSetupRunner, RobotIntentPostSetupRunner>();
            builder.Services.TryAddSingleton<IIntentExecutor, RobotIntentRejectingExecutor>();
            builder.Services.AddSingleton<IRobotIntentPostSetupConfigurator>(
                new DelegateRobotIntentConfigurator(typeof(TNodeManager), configure));
            return builder;
        }

        private sealed class DelegateRobotIntentConfigurator : IRobotIntentPostSetupConfigurator
        {
            public DelegateRobotIntentConfigurator(
                Type targetManagerType,
                Func<IRobotIntentBuildContext, CancellationToken, ValueTask> configure)
            {
                TargetManagerType = targetManagerType;
                m_configure = configure;
            }

            public Type TargetManagerType { get; }

            public ValueTask RunAsync(IRobotIntentBuildContext context)
            {
                return m_configure(context, context.CancellationToken);
            }

            private readonly Func<IRobotIntentBuildContext, CancellationToken, ValueTask> m_configure;
        }
    }
}
