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
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Opc.Ua;
using Opc.Ua.Di.Server;
using Opc.Ua.Machinery.Server;
using Opc.Ua.Server.FileSystem;
using Opc.Ua.Server.Hosting;
using Opc.Ua.Vision;
using Opc.Ua.Vision.MachineVision;
using Opc.Ua.Vision.Perception;
using Opc.Ua.Vision.Server;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// Composes the Vision package family into a Machinery manager without a second
    /// owner for DI, Machinery, or their shared address-space roots.
    /// </summary>
    public static class OpcUaServerVisionCompositionExtensions
    {
        /// <summary>
        /// Registers the released Vision models with an existing AddMachinery registration.
        /// Configure instances with ConfigureVisionFor&lt;MachineryNodeManager&gt;.
        /// The owner's application namespace is used for all composed instances.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <c>null</c>.</exception>
        /// <exception cref="InvalidOperationException"></exception>
        internal static IOpcUaServerBuilder AddVisionComposition(
            this IOpcUaServerBuilder builder,
            Action<VisionServerOptions>? configure = null)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            bool registered = false;
            foreach (ServiceDescriptor service in builder.Services)
            {
                registered |= service.ServiceType == typeof(IMachineryModelProvider);
            }
            if (!registered)
            {
                throw new InvalidOperationException(
                    "Register Machinery or an application-owned Machinery model provider before AddVisionToMachinery.");
            }
            builder.Services.AddOptions<VisionServerOptions>().Configure(options =>
            {
                options.MachineVisionParts = MachineVisionParts.All;
                configure?.Invoke(options);
                options.Validate();
            });
            builder.Services.TryAddSingleton<IFileTypeBinder, FileTypeBinder>();
            return builder.AddMachineryModel<VisionMachineryModelProvider>();
        }

        /// <summary>
        /// Configures Vision on a DI/Machinery manager that loads the Vision models.
        /// Custom managers use their existing Machinery resource-disposal integration.
        /// </summary>
        /// <typeparam name="TNodeManager">The exact owning DI manager type.</typeparam>
        /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <c>null</c>.</exception>
        public static IOpcUaServerBuilder ConfigureVisionOnMachinery<TNodeManager>(
            this IOpcUaServerBuilder builder,
            Func<IVisionBuildContext, CancellationToken, ValueTask> configure)
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
            return builder.ConfigureMachineryFor<TNodeManager>(async (machinery, ct) =>
            {
                VisionServerOptions selected = machinery.GetService<IOptions<VisionServerOptions>>()?.Value ??
                    new VisionServerOptions();
                var options = new VisionServerOptions
                {
                    MachineVisionParts = selected.MachineVisionParts,
                    SpecificationVersion = selected.SpecificationVersion,
                    AdditionalFacets = selected.AdditionalFacets,
                    InstanceNamespaceUri =
                        machinery.Context.NamespaceUris.GetString(machinery.InstanceNamespaceIndex) ??
                        throw new InvalidOperationException("The Machinery instance namespace is not registered.")
                };
                Composition composition = s_compositions.GetValue(machinery.Manager,
                    manager => new Composition(manager, options, machinery.GetService<IServiceProvider>()));
                composition.RegisterLifetime(machinery.Manager);
                VisionBuildContext context = composition.CreateContext(machinery.Manager, ct);
                await configure(context, ct).ConfigureAwait(false);
                context.Registry.ResolveDeferredExtrinsics();
                await context.FlushPendingRegistrationsAsync(ct).ConfigureAwait(false);
            });
        }

        private static readonly ConditionalWeakTable<DiNodeManager, Composition> s_compositions = new();

        private sealed class Composition : IAsyncDisposable
        {
            public Composition(DiNodeManager manager, VisionServerOptions options, IServiceProvider? services)
            {
                m_options = options;
                m_services = services;
                var rootId = ExpandedNodeId.ToNodeId(
                    Opc.Ua.Vision.Perception.ObjectIds.Vision, manager.SystemContext.NamespaceUris);
                m_root = manager.FindPredefinedNode<VisionRootState>(rootId) ??
                    throw ServiceResultException.Create(
                        StatusCodes.BadConfigurationError,
                        "Load the Vision models with AddVisionToMachinery " +
                        "before configuring composed Vision instances.");
                m_dispatcher = new VisionMethodDispatcher(
                    m_registry, manager.SystemContext.Telemetry.CreateLogger<VisionMethodDispatcher>());
            }

            public VisionBuildContext CreateContext(DiNodeManager manager, CancellationToken ct)
            {
                return new VisionBuildContext(manager, m_root, m_options, m_registry, m_dispatcher, ct, m_services);
            }

            public void RegisterLifetime(DiNodeManager manager)
            {
                if (Interlocked.Exchange(ref m_registered, 1) == 0)
                {
                    manager.RegisterMachineryResource(this);
                }
            }

            public ValueTask DisposeAsync()
            {
                return m_registry.DisposeMachineVisionResourcesAsync();
            }

            private readonly VisionServerOptions m_options;
            private readonly IServiceProvider? m_services;
            private readonly VisionRootState m_root;
            private readonly VisionRegistry m_registry = new();
            private readonly VisionMethodDispatcher m_dispatcher;
            private int m_registered;
        }
    }
}

namespace Opc.Ua.Vision.Server
{
    /// <summary>
    /// Loads Vision into a Machinery model-provider pipeline, reusing its existing dependency models.
    /// </summary>
    public sealed class VisionMachineryModelProvider : IMachineryModelProvider
    {
        /// <summary>
        /// Creates the provider from the configured Vision options.
        /// </summary>
        public VisionMachineryModelProvider(IOptions<VisionServerOptions> options)
        {
            m_options = options?.Value ?? throw new ArgumentNullException(nameof(options));
            m_options.Validate();
        }

        /// <inheritdoc/>
        public int Order => 100;

        /// <inheritdoc/>
        public ArrayOf<string> NamespaceUris => new VisionModelProvider(m_options.MachineVisionParts).NamespaceUris;

        /// <inheritdoc/>
        public void AddPredefinedNodes(NodeStateCollection nodes, ISystemContext context)
        {
            if (nodes == null)
            {
                throw new ArgumentNullException(nameof(nodes));
            }
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            nodes.AddOpcUaVisionPerception(context);
            context.EncodeableFactory.Builder.AddOpcUaVisionPerception().Commit();
            if ((m_options.MachineVisionParts & MachineVisionParts.Control) != 0)
            {
                nodes.AddOpcUaVisionMachineVision(context);
                context.EncodeableFactory.Builder.AddOpcUaVisionMachineVision().Commit();
            }
        }

        private readonly VisionServerOptions m_options;
    }
}
