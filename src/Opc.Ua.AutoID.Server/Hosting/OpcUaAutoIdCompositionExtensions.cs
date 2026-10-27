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
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Opc.Ua.AutoID.Server;
using Opc.Ua.AutoID.Server.Hosting;
using Opc.Ua.Di.Server;
using Opc.Ua.Server.Hosting;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// Composes OPC 30010 AutoID readers into any Device Integration based node manager that
    /// owns the address space, instead of registering a node manager of their own.
    /// </summary>
    public static class OpcUaAutoIdCompositionExtensions
    {
        /// <summary>
        /// Registers a delegate that creates readers on the selected Device Integration owner once
        /// its address space is initialised. Successive delegates share one
        /// <see cref="AutoIdRuntime"/> and run in registration order; any exception aborts server
        /// startup.
        /// </summary>
        /// <remarks>
        /// The delegates run as a Device Integration post-setup step, in registration order with
        /// the other <c>ConfigureDevicesFor</c> registrations. Register the owner's own
        /// configuration, such as the Machinery configuration, first: it then runs before the
        /// readers are created. For <see cref="AutoIdNodeManager"/> the delegates receive the
        /// stand-alone manager's runtime, like <c>ConfigureAutoId</c>.
        /// </remarks>
        /// <typeparam name="TNodeManager">The exact Device Integration owner type.</typeparam>
        /// <param name="builder">The server builder.</param>
        /// <param name="configure">Creates and configures readers.</param>
        /// <exception cref="ArgumentNullException"></exception>
        public static IOpcUaServerBuilder ConfigureAutoIdFor<TNodeManager>(
            this IOpcUaServerBuilder builder,
            Func<IAutoIdBuildContext, CancellationToken, ValueTask> configure)
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
            builder.Services.AddOptions<AutoIdServerOptions>();
            if (typeof(TNodeManager) == typeof(AutoIdNodeManager))
            {
                return builder.ConfigureDevicesFor<TNodeManager>(context => configure(
                    new AutoIdBuildContext(
                        context.Manager.CreateAutoIdRuntime(),
                        context.GetRequiredService<IServiceProvider>(),
                        context.CancellationToken),
                    context.CancellationToken));
            }
            return builder.ConfigureDevicesFor<TNodeManager>(context =>
            {
                if (context.Manager.GetType() != typeof(TNodeManager))
                {
                    return default;
                }
                return configure(
                    new AutoIdBuildContext(
                        context.Manager.CreateAutoIdRuntime(
                            context.GetRequiredService<IOptions<AutoIdServerOptions>>().Value),
                        context.GetRequiredService<IServiceProvider>(),
                        context.CancellationToken),
                    context.CancellationToken);
            });
        }

        /// <summary>
        /// Adds the AutoID model to any Device Integration based owner, so readers share its
        /// <c>DeviceSet</c>.
        /// </summary>
        /// <param name="builder">The server builder.</param>
        /// <param name="configure">Configures the AutoID options.</param>
        /// <exception cref="ArgumentNullException"></exception>
        public static IOpcUaServerBuilder AddAutoIdModel(
            this IOpcUaServerBuilder builder,
            Action<AutoIdServerOptions>? configure = null)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            var options = new AutoIdServerOptions();
            configure?.Invoke(options);
            options.Validate();
            builder.Services.AddOptions<AutoIdServerOptions>();
            if (configure != null)
            {
                builder.Services.Configure(configure);
            }
            return builder.AddDiModel<AutoIdModelProvider>();
        }

        /// <summary>
        /// Carries a configurator's services and cancellation without extending the runtime's
        /// lifetime.
        /// </summary>
        private sealed class AutoIdBuildContext : IAutoIdBuildContext
        {
            /// <summary>
            /// Creates a context for the current configurator.
            /// </summary>
            public AutoIdBuildContext(
                AutoIdRuntime runtime,
                IServiceProvider services,
                CancellationToken cancellationToken)
            {
                Runtime = runtime;
                m_services = services;
                CancellationToken = cancellationToken;
            }

            /// <summary>
            /// Gets the reader runtime shared by callbacks for this owner.
            /// </summary>
            public AutoIdRuntime Runtime { get; }

            /// <summary>
            /// Gets the DI owner hosting the reader runtime.
            /// </summary>
            public DiNodeManager Manager => Runtime.Manager;

            /// <summary>
            /// Gets the current configuration cancellation token.
            /// </summary>
            public CancellationToken CancellationToken { get; }

            /// <summary>
            /// Resolves a required service from the hosting container.
            /// </summary>
            /// <typeparam name="T">The required service type.</typeparam>
            public T GetRequiredService<T>() where T : notnull
            {
                return m_services.GetRequiredService<T>();
            }

            /// <summary>The hosting container.</summary>
            private readonly IServiceProvider m_services;
        }
    }
}
