// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Opc.Ua.Di.Server;
using Opc.Ua.Machinery.Server;
using Opc.Ua.Pumps.Server;
using Opc.Ua.Pumps.Server.Hosting;
using Opc.Ua.Server.Hosting;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// Composes Pumps into the existing Machinery/DI owner without another node manager.
    /// </summary>
    public static class OpcUaPumpsCompositionExtensions
    {
        /// <summary>
        /// Adds the Pumps model to an existing AddMachinery registration.
        /// </summary>
        /// <remarks>
        /// Select MachineryParts.BuildingBlocks on the owner. DI, IA and Machinery are loaded
        /// by that owner, regardless of PumpsServerOptions.LoadIndustrialAutomationModel.
        /// </remarks>
        public static IOpcUaServerBuilder AddPumpsToMachinery(
            this IOpcUaServerBuilder builder,
            Action<PumpsServerOptions>? configure = null)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            if (!builder.Services.Any(service => service.ServiceType == typeof(MachineryNodeManagerFactory)))
            {
                throw new InvalidOperationException("Register AddMachinery before AddPumpsToMachinery.");
            }
            var options = new PumpsServerOptions();
            configure?.Invoke(options);
            options.Validate();
            builder.Services.AddOptions<PumpsServerOptions>();
            if (configure != null)
            {
                builder.Services.Configure(configure);
            }
            return builder.AddMachineryModel<PumpsMachineryModelProvider>();
        }

        /// <summary>
        /// Configures the shared Pumps runtime on the exact selected DI owner.
        /// </summary>
        /// <remarks>
        /// Machinery configurators share its ordered configuration pipeline and finish before sealing.
        /// Custom Machinery-compatible DI owners load the models and dispose their Machinery resources.
        /// Existing ConfigurePumps delegates and standalone managers remain supported unchanged.
        /// </remarks>
        /// <typeparam name="TNodeManager">The exact DI/Machinery owner type.</typeparam>
        public static IOpcUaServerBuilder ConfigurePumpsFor<TNodeManager>(
            this IOpcUaServerBuilder builder,
            Func<IPumpsBuildContext, CancellationToken, ValueTask> configure)
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
            builder.Services.AddOptions<PumpsServerOptions>();
            if (typeof(TNodeManager) == typeof(PumpsNodeManager))
            {
                return builder.ConfigureDevicesFor<TNodeManager>(context => configure(
                    new PumpsBuildContext(
                        context.Manager.CreatePumpsRuntime(),
                        context.GetRequiredService<IServiceProvider>(),
                        context.CancellationToken),
                    context.CancellationToken));
            }
            return builder.ConfigureMachineryFor<TNodeManager>((machinery, ct) => configure(
                new PumpsBuildContext(
                    machinery.Manager.CreatePumpsRuntime(
                        machinery.GetRequiredService<IOptions<PumpsServerOptions>>().Value),
                    machinery.GetRequiredService<IServiceProvider>(),
                    ct),
                ct));
        }

        /// <summary>
        /// Carries a configurator's services and cancellation without extending runtime lifetime.
        /// </summary>
        private sealed class PumpsBuildContext : IPumpsBuildContext
        {
            /// <summary>
            /// Creates a context for the current configurator.
            /// </summary>
            public PumpsBuildContext(
                PumpsRuntime runtime,
                IServiceProvider services,
                CancellationToken cancellationToken)
            {
                Runtime = runtime;
                m_services = services;
                CancellationToken = cancellationToken;
            }

            /// <inheritdoc/>
            public PumpsRuntime Runtime { get; }

            /// <inheritdoc/>
            public DiNodeManager Manager => Runtime.Manager;

            /// <inheritdoc/>
            public CancellationToken CancellationToken { get; }

            /// <inheritdoc/>
            public T GetRequiredService<T>() where T : notnull
            {
                return m_services.GetRequiredService<T>();
            }

            /// <summary>The hosting container.</summary>
            private readonly IServiceProvider m_services;
        }
    }
}
