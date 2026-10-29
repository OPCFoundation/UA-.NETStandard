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
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Opc.Ua;
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
    ///         .ConfigureDevicesFor&lt;DiNodeManager&gt;(async ctx =&gt;
    ///         {
    ///             await ctx.CreateDeviceAsync(...);
    ///         });
    /// </code>
    /// <para>
    /// For companion-spec managers that already embed DI (e.g. a pump
    /// manager that loads DI + Machinery + Pumps node sets), <em>do not</em>
    /// call <see cref="AddOpcUaDi"/> — register the companion factory
    /// directly via
    /// <see cref="IOpcUaServerBuilder.AddNodeManager{TFactory}"/> and use
    /// <c>ConfigureDevicesFor</c> against the companion manager type.
    /// The companion hosting extension must register
    /// <see cref="DiAddressSpaceOwnership"/> and fail if that marker is already
    /// present, preventing two managers from owning the DI namespace.
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
            builder.Services.ClaimDiAddressSpace(nameof(AddOpcUaDi));
            EnsureRunnerRegistered(builder);

            // Register the factory through the same registration shape as
            // AddNodeManager<TFactory>(), adding the registered model providers.
            builder.Services.AddSingleton(static services => new DiNodeManagerFactory(
                services.GetService<IDiPostSetupRunner>(),
                services.GetServices<IDiModelProvider>().ToArray()));
            builder.Services.AddSingleton(static services => new OpcUaServerNodeManagerRegistration(
                services.GetRequiredService<DiNodeManagerFactory>()));
            return builder;
        }

        /// <summary>
        /// Registers a compiled model provider that every DI-owning node
        /// manager loads after the DI model, for example the types of a
        /// DI-based companion specification.
        /// </summary>
        /// <typeparam name="TProvider">The model provider type.</typeparam>
        /// <param name="builder">The server builder.</param>
        /// <returns>The same builder, for chaining.</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="builder"/> is null.
        /// </exception>
        public static IOpcUaServerBuilder AddDiModel<
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TProvider>(
                this IOpcUaServerBuilder builder)
            where TProvider : class, IDiModelProvider
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            builder.Services.TryAddEnumerable(
                ServiceDescriptor.Singleton<IDiModelProvider, TProvider>());
            return builder;
        }

        /// <summary>
        /// Registers a companion model and a synchronous configuration callback for a DI owner.
        /// </summary>
        /// <typeparam name="TProvider">The companion model provider.</typeparam>
        /// <typeparam name="TNodeManager">The owner type targeted by the callback.</typeparam>
        /// <param name="builder">The server builder.</param>
        /// <param name="configure">Configures instances after the owner's models are loaded.</param>
        /// <returns>The same builder, for chaining.</returns>
        /// <remarks>
        /// Model registration is shared by all DI owners and deduplicated by provider type.
        /// Each call adds a callback in registration order. Register the owner's configuration
        /// before dependent companion callbacks.
        /// </remarks>
        /// <exception cref="ArgumentNullException">
        /// The builder or configuration callback is null.
        /// </exception>
        public static IOpcUaServerBuilder AddDiModel<
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TProvider,
            TNodeManager>(
                this IOpcUaServerBuilder builder,
                Action<IDiPostSetupContext> configure)
            where TProvider : class, IDiModelProvider
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
            return builder.AddDiModel<TProvider, TNodeManager>(context =>
            {
                configure(context);
                return default;
            });
        }

        /// <summary>
        /// Registers a companion model and an awaited configuration callback for a DI owner.
        /// </summary>
        /// <typeparam name="TProvider">The companion model provider.</typeparam>
        /// <typeparam name="TNodeManager">The owner type targeted by the callback.</typeparam>
        /// <param name="builder">The server builder.</param>
        /// <param name="configure">Configures instances after the owner's models are loaded.</param>
        /// <returns>The same builder, for chaining.</returns>
        /// <remarks>
        /// Callbacks run in registration order with <c>ConfigureDevicesFor</c> callbacks.
        /// Exceptions abort startup. The callback receives services and cancellation through its context.
        /// </remarks>
        /// <exception cref="ArgumentNullException">
        /// The builder or configuration callback is null.
        /// </exception>
        public static IOpcUaServerBuilder AddDiModel<
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TProvider,
            TNodeManager>(
                this IOpcUaServerBuilder builder,
                Func<IDiPostSetupContext, ValueTask> configure)
            where TProvider : class, IDiModelProvider
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
            return builder.AddDiModel<TProvider>().ConfigureDevicesFor<TNodeManager>(configure);
        }

        /// <summary>
        /// Gets the model providers a DI-owning node manager of a companion family
        /// should load: the providers registered for the family plus the plain
        /// <see cref="IDiModelProvider"/> registrations made with
        /// <see cref="AddDiModel{TProvider}"/>.
        /// </summary>
        /// <typeparam name="TFamily">
        /// The family provider interface, which derives from <see cref="IDiModelProvider"/>.
        /// </typeparam>
        /// <param name="services">The service provider.</param>
        /// <param name="adapt">
        /// Wraps a plain provider that does not implement <typeparamref name="TFamily"/>.
        /// </param>
        /// <returns>
        /// The family providers followed by the plain providers, each instance once.
        /// </returns>
        public static ArrayOf<TFamily> GetDiModelProviders<TFamily>(
            this IServiceProvider services,
            Func<IDiModelProvider, TFamily> adapt)
            where TFamily : class, IDiModelProvider
        {
            if (services is null)
            {
                throw new ArgumentNullException(nameof(services));
            }
            if (adapt is null)
            {
                throw new ArgumentNullException(nameof(adapt));
            }
            var result = new List<TFamily>();
            var seen = new List<IDiModelProvider>();
            foreach (TFamily family in services.GetServices<TFamily>())
            {
                if (!seen.Exists(existing => ReferenceEquals(existing, family)))
                {
                    seen.Add(family);
                    result.Add(family);
                }
            }
            foreach (IDiModelProvider plain in services.GetServices<IDiModelProvider>())
            {
                if (!seen.Exists(existing => ReferenceEquals(existing, plain)))
                {
                    seen.Add(plain);
                    result.Add(plain as TFamily ?? adapt(plain));
                }
            }
            return result.ToArray();
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
        /// <exception cref="ArgumentNullException"></exception>
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
            return builder.ConfigureDevicesFor<TNodeManager>(ctx =>
            {
                configure(ctx);
                return default;
            });
        }

        /// <summary>
        /// Asynchronous overload of
        /// <see cref="ConfigureDevicesFor{TNodeManager}(IOpcUaServerBuilder, Action{IDiPostSetupContext})"/>.
        /// The delegate receives the same context and is awaited inline
        /// by the runner; thrown exceptions abort hosted-server startup.
        /// </summary>
        /// <typeparam name="TNodeManager">Target node-manager subclass.</typeparam>
        /// <exception cref="ArgumentNullException"></exception>
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
        /// Delegate-backed configurator created by the
        /// <c>ConfigureDevicesFor</c> extension methods.
        /// </summary>
        private sealed class DelegateDiPostSetupConfigurator : IDiPostSetupConfigurator
        {
            private readonly Func<IDiPostSetupContext, ValueTask> m_configure;

            /// <summary>
            /// Creates a registration for a callback and its target owner type.
            /// </summary>
            public DelegateDiPostSetupConfigurator(
                Type targetManagerType,
                Func<IDiPostSetupContext, ValueTask> configure)
            {
                TargetManagerType = targetManagerType
                    ?? throw new ArgumentNullException(nameof(targetManagerType));
                m_configure = configure
                    ?? throw new ArgumentNullException(nameof(configure));
            }

            /// <summary>
            /// Gets the owner type matched by the post-setup runner.
            /// </summary>
            public Type TargetManagerType { get; }

            /// <summary>
            /// Invokes the registered callback with the owner's context.
            /// </summary>
            public ValueTask RunAsync(IDiPostSetupContext context)
            {
                return m_configure(context);
            }
        }
    }
}
