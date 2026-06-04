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
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Opc.Ua.Di.Server;
using Opc.Ua.Di.Server.Hosting;
using Opc.Ua.Server.Hosting;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// <see cref="IOpcUaServerBuilder"/> extensions that integrate the
    /// OPC UA Device Integration (DI, OPC 10000-100) server with the
    /// unified <c>AddOpcUa()</c> Microsoft.Extensions DI hosting model.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Usage:
    /// </para>
    /// <code>
    /// services.AddOpcUa()
    ///         .AddServer(o => { ... })
    ///         .AddOpcUaDi()                         // plain DI manager
    ///         .ConfigureDevicesFor&lt;DiNodeManager&gt;(ctx =&gt;
    ///         {
    ///             ctx.CreateDeviceAsync(...).AsTask().Wait();
    ///         });
    /// </code>
    /// <para>
    /// For companion-spec managers that already embed DI (e.g. a pump
    /// manager that loads DI + Machinery + Pumps node sets), <em>do not</em>
    /// call <see cref="AddOpcUaDi"/> — register the companion factory
    /// directly via
    /// <see cref="IOpcUaServerBuilder.AddNodeManager{TFactory}"/> and use
    /// <c>ConfigureDevicesFor</c> against the companion manager type.
    /// Calling <c>AddOpcUaDi()</c> alongside a companion-spec manager
    /// would double-register the DI namespace.
    /// </para>
    /// </remarks>
    public static class OpcUaServerDiBuilderExtensions
    {
        /// <summary>
        /// Registers the <see cref="DiNodeManagerFactory"/> so a plain
        /// DI server (no companion specs) can be hosted, plus the
        /// shared <see cref="IDiPostSetupRunner"/> used by every
        /// DI-aware manager to dispatch
        /// <see cref="IDiPostSetupConfigurator"/> instances.
        /// </summary>
        /// <param name="builder">The server builder.</param>
        /// <returns>The same builder, for chaining.</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="builder"/> is null.
        /// </exception>
        /// <exception cref="InvalidOperationException">
        /// <see cref="AddOpcUaDi"/> has already been called on this
        /// service collection.
        /// </exception>
        public static IOpcUaServerBuilder AddOpcUaDi(this IOpcUaServerBuilder builder)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            // Add the shared runner exactly once. Multiple calls to
            // ConfigureDevicesFor share the same runner.
            EnsureRunnerRegistered(builder);

            // Fail fast on duplicate AddOpcUaDi calls.
            foreach (ServiceDescriptor d in builder.Services)
            {
                if (d.ServiceType == typeof(DiNodeManagerRegistrationMarker))
                {
                    throw new InvalidOperationException(
                        "AddOpcUaDi has already been called. " +
                        "At most one DiNodeManagerFactory may be registered per service collection.");
                }
            }
            builder.Services.AddSingleton<DiNodeManagerRegistrationMarker>();

            // Register the factory through the existing AddNodeManager
            // pipeline so the hosted service picks it up.
            return builder.AddNodeManager<DiNodeManagerFactory>();
        }

        /// <summary>
        /// Registers a synchronous device-configuration delegate that
        /// runs once the address space of a
        /// <typeparamref name="TNodeManager"/> instance is initialised.
        /// Multiple registrations run in registration order.
        /// </summary>
        /// <typeparam name="TNodeManager">
        /// The exact (or base) DI node-manager type to target. Use
        /// <see cref="DiNodeManager"/> for plain DI managers or a
        /// companion-spec subclass for targeted configuration.
        /// </typeparam>
        /// <param name="builder">The server builder.</param>
        /// <param name="configure">Configuration delegate.</param>
        /// <returns>The same builder, for chaining.</returns>
        public static IOpcUaServerBuilder ConfigureDevicesFor<TNodeManager>(
            this IOpcUaServerBuilder builder,
            Action<IDiPostSetupContext> configure)
            where TNodeManager : DiNodeManager
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            if (configure is null)
            {
                throw new ArgumentNullException(nameof(configure));
            }
            return builder.ConfigureDevicesFor<TNodeManager>((Func<IDiPostSetupContext, ValueTask>)(ctx =>
            {
                configure(ctx);
                return default;
            }));
        }

        /// <summary>
        /// Asynchronous overload of
        /// <see cref="ConfigureDevicesFor{TNodeManager}(IOpcUaServerBuilder, Action{IDiPostSetupContext})"/>.
        /// The delegate receives the same context and is awaited inline
        /// by the runner; thrown exceptions abort hosted-server startup.
        /// </summary>
        /// <typeparam name="TNodeManager">Target node-manager subclass.</typeparam>
        public static IOpcUaServerBuilder ConfigureDevicesFor<TNodeManager>(
            this IOpcUaServerBuilder builder,
            Func<IDiPostSetupContext, ValueTask> configure)
            where TNodeManager : DiNodeManager
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            if (configure is null)
            {
                throw new ArgumentNullException(nameof(configure));
            }
            EnsureRunnerRegistered(builder);

            builder.Services.AddSingleton<IDiPostSetupConfigurator>(
                new DelegateDiPostSetupConfigurator(typeof(TNodeManager), configure));

            return builder;
        }

        private static void EnsureRunnerRegistered(IOpcUaServerBuilder builder)
        {
            builder.Services.TryAddSingleton<IDiPostSetupRunner, DiPostSetupRunner>();
        }

        /// <summary>
        /// Marker type used to detect duplicate <see cref="AddOpcUaDi"/>
        /// calls.
        /// </summary>
        private sealed class DiNodeManagerRegistrationMarker
        {
        }

        /// <summary>
        /// Delegate-backed configurator created by the
        /// <c>ConfigureDevicesFor</c> extension methods.
        /// </summary>
        private sealed class DelegateDiPostSetupConfigurator : IDiPostSetupConfigurator
        {
            private readonly Func<IDiPostSetupContext, ValueTask> m_configure;

            public DelegateDiPostSetupConfigurator(
                Type targetManagerType,
                Func<IDiPostSetupContext, ValueTask> configure)
            {
                TargetManagerType = targetManagerType
                    ?? throw new ArgumentNullException(nameof(targetManagerType));
                m_configure = configure
                    ?? throw new ArgumentNullException(nameof(configure));
            }

            public Type TargetManagerType { get; }

            public ValueTask RunAsync(IDiPostSetupContext context)
            {
                return m_configure(context);
            }
        }
    }
}
