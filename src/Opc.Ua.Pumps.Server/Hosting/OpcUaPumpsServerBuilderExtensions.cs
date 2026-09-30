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
using Opc.Ua;
using Opc.Ua.Di.Server.Hosting;
using Opc.Ua.Pumps.Server;
using Opc.Ua.Pumps.Server.Hosting;
using Opc.Ua.Server.Hosting;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// Hosting extensions for OPC 40223 Pumps servers.
    /// </summary>
    public static class OpcUaPumpsServerBuilderExtensions
    {
        /// <summary>
        /// Registers the OPC 40223 node manager, which loads the Device
        /// Integration, Industrial Automation, Machinery and Pumps models into
        /// one address space.
        /// </summary>
        /// <remarks>
        /// The manager owns the Device Integration namespace, so this cannot
        /// be combined with <c>AddOpcUaDi</c> or any other DI-owning
        /// registration on the same server. A server that needs both should
        /// load the Pumps model into its existing manager instead.
        /// </remarks>
        /// <param name="builder">The server builder.</param>
        /// <param name="configure">Configures the Pumps options.</param>
        /// <exception cref="InvalidOperationException">
        /// Another registration already owns the Device Integration address
        /// space.
        /// </exception>
        public static IOpcUaServerBuilder AddPumps(
            this IOpcUaServerBuilder builder,
            Action<PumpsServerOptions>? configure = null)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            var options = new PumpsServerOptions();
            configure?.Invoke(options);
            options.Validate();

            ClaimDiAddressSpace(builder.Services);

            // The post-setup runner is registered by ConfigureDevicesFor /
            // ConfigurePumps, which is the only thing that needs it. Doing it
            // here would mean reaching for the internal DiPostSetupRunner, and
            // a server that registers no configurator does not need a runner
            // at all - the factory takes it as an optional dependency.
            builder.Services.AddOptions<PumpsServerOptions>();
            if (configure != null)
            {
                builder.Services.Configure(configure);
            }

            return builder.AddNodeManager<PumpsNodeManagerFactory>();
        }

        /// <summary>
        /// Registers a configuration delegate that runs once the OPC 40223
        /// address space is initialised. Multiple registrations run in
        /// registration order, and any exception aborts server startup.
        /// </summary>
        /// <param name="builder">The server builder.</param>
        /// <param name="configure">Creates and configures pumps.</param>
        public static IOpcUaServerBuilder ConfigurePumps(
            this IOpcUaServerBuilder builder,
            Func<IPumpsSetupContext, ValueTask> configure)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            if (configure is null)
            {
                throw new ArgumentNullException(nameof(configure));
            }

            return builder.ConfigureDevicesFor<PumpsNodeManager>(context =>
            {
                if (context.Manager is not PumpsNodeManager manager)
                {
                    // The DI runner only dispatches to the target type, so
                    // this is unreachable unless that contract changes.
                    return default;
                }
                return configure(new PumpsSetupContext(manager, context));
            });
        }

        /// <summary>
        /// Synchronous overload of
        /// <see cref="ConfigurePumps(IOpcUaServerBuilder, Func{IPumpsSetupContext, ValueTask})"/>.
        /// </summary>
        /// <param name="builder">The server builder.</param>
        /// <param name="configure">Creates and configures pumps.</param>
        public static IOpcUaServerBuilder ConfigurePumps(
            this IOpcUaServerBuilder builder,
            Action<IPumpsSetupContext> configure)
        {
            if (configure is null)
            {
                throw new ArgumentNullException(nameof(configure));
            }
            return builder.ConfigurePumps(context =>
            {
                configure(context);
                return default;
            });
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
                        $"The OPC UA DI namespace and address space are already owned by '{ownerName}'. AddPumps cannot register a second DI-owning manager. Load the Pumps model into the existing manager with nodes.AddOpcUaPumps(context) and configure it with ConfigureDevicesFor<TNodeManager>().");
                }
            }

            services.AddSingleton(new DiAddressSpaceOwnership(nameof(AddPumps)));
        }

        private sealed class PumpsSetupContext : IPumpsSetupContext
        {
            public PumpsSetupContext(PumpsNodeManager manager, IDiPostSetupContext context)
            {
                Manager = manager;
                DiContext = context;
            }

            public PumpsNodeManager Manager { get; }

            public IDiPostSetupContext DiContext { get; }

            public CancellationToken CancellationToken => DiContext.CancellationToken;

            public T GetRequiredService<T>() where T : notnull
            {
                return DiContext.GetRequiredService<T>();
            }
        }
    }
}
