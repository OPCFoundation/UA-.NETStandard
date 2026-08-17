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
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.Server;
using Opc.Ua.Server.Fluent;
using Opc.Ua.Vision.Server.Hosting;

namespace Opc.Ua.Vision.Server
{
    /// <summary>
    /// Standalone node manager for the OPC UA Vision companion model.
    /// </summary>
    /// <remarks>
    /// The node manager loads the Vision NodeSet through
    /// <see cref="IVisionModelProvider"/> instances, materialises the
    /// well-known <c>Server/Vision</c> root and lets configurators
    /// populate sensors, coordinate frames and inference pipelines
    /// through the fluent <see cref="IVisionBuildContext"/> surface.
    /// </remarks>
    public sealed class VisionNodeManager :
        FluentNodeManagerBase,
        INodeIdFactory,
        IAsyncDisposable,
        IConformanceContributor
    {
        /// <summary>
        /// Creates a standalone Vision node manager loading only the
        /// built-in model provider.
        /// </summary>
        public VisionNodeManager(IServerInternal server, ApplicationConfiguration configuration)
            : this(
                server,
                configuration,
                new IVisionModelProvider[] { new VisionModelProvider() },
                new VisionServerOptions())
        {
        }

        /// <summary>
        /// Creates a Vision node manager with explicit services.
        /// </summary>
        public VisionNodeManager(
            IServerInternal server,
            ApplicationConfiguration configuration,
            ArrayOf<IVisionModelProvider> providers,
            VisionServerOptions options,
            IVisionPostSetupRunner? runner = null,
            IServiceProvider? services = null)
            : base(
                server,
                configuration,
                server.Telemetry.CreateLogger<VisionNodeManager>(),
                GetNamespaceUris(providers, options))
        {
            m_providers = NormalizeProviders(providers);
            m_options = options ?? throw new ArgumentNullException(nameof(options));
            m_options.Validate();
            m_runner = runner;
            m_services = services;
            m_registry = new VisionRegistry();
            m_dispatcherLogger = server.Telemetry.CreateLogger<VisionMethodDispatcher>();
            m_dispatcher = new VisionMethodDispatcher(m_registry, m_dispatcherLogger);
            SystemContext.NodeIdFactory = this;
            RegisterEncodeables(SystemContext);
        }

        /// <summary>
        /// Gets the Vision root object.
        /// </summary>
        public VisionRootState Root => m_root ??
            throw ServiceResultException.Create(
                StatusCodes.BadConfigurationError,
                "The Vision address space is not available yet.");

        /// <inheritdoc/>
        public ArrayOf<QualifiedName> ConformanceUnits => ArrayOf<QualifiedName>.Empty;

        /// <inheritdoc/>
        public ArrayOf<string> ServerProfiles => ComputeServerProfileArrayEntries();

        /// <inheritdoc/>
        public override NodeId New(ISystemContext context, NodeState node)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            if (node == null)
            {
                throw new ArgumentNullException(nameof(node));
            }
            if (node is BaseInstanceState instance && instance.Parent != null)
            {
                return new NodeId(
                    $"{instance.Parent.NodeId.IdentifierAsString}_{instance.SymbolicName}",
                    GetInstanceNamespaceIndex(context));
            }
            if (node.NodeId.IsNull)
            {
                return new NodeId(Guid.NewGuid(), GetInstanceNamespaceIndex(context));
            }
            return node.NodeId;
        }

        /// <summary>
        /// Creates a direct build context for non-DI configuration.
        /// </summary>
        /// <remarks>
        /// Nodes the builder grafts onto an already created address space
        /// are only browsable by their own <see cref="NodeId"/> once they
        /// have been registered with the node manager. A context created
        /// here never registers anything on its own, so prefer
        /// <see cref="ConfigureVisionAsync"/>, which runs the same fluent
        /// surface and then registers everything it built.
        /// </remarks>
        public IVisionBuildContext CreateVisionBuildContext(CancellationToken cancellationToken = default)
        {
            return CreateBuildContextCore(cancellationToken);
        }

        /// <summary>
        /// Configures the Vision address space through the fluent builder
        /// and registers every node the builder created, so each one can be
        /// browsed and read by its own <see cref="NodeId"/>.
        /// </summary>
        /// <param name="configure">
        /// Populates sensors, coordinate frames and inference pipelines.
        /// </param>
        /// <param name="cancellationToken">
        /// Cancels the configuration.
        /// </param>
        public async ValueTask ConfigureVisionAsync(
            Action<IVisionBuildContext> configure,
            CancellationToken cancellationToken = default)
        {
            if (configure == null)
            {
                throw new ArgumentNullException(nameof(configure));
            }
            VisionBuildContext context = CreateBuildContextCore(cancellationToken);
            configure(context);
            await context.FlushPendingRegistrationsAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Registers a node, giving it and its children the reference type and
        /// type definition an instance node is not valid without.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The generated <c>CreateOrReplace</c> helpers materialise an optional
        /// child by constructing the state object directly, which leaves both
        /// unset. A child with no <c>ReferenceTypeId</c> is referenced by
        /// nothing, so it cannot be browsed from its parent; one with no
        /// <c>TypeDefinitionId</c> is a malformed Object that any client
        /// filtering by type silently skips.
        /// </para>
        /// <para>
        /// Doing it here rather than in the builder covers the case the builder
        /// cannot see: a result published at runtime, long after the address
        /// space was created, by an inference provider that assembled the node
        /// itself. That path produced results a client could list but not read,
        /// which is how this was found.
        /// </para>
        /// </remarks>
        /// <param name="context">
        /// The system context to resolve default type definitions against.
        /// </param>
        /// <param name="node">The node to register.</param>
        /// <param name="cancellationToken">Cancels the registration.</param>
        protected override ValueTask AddPredefinedNodeAsync(
            ISystemContext context,
            NodeState node,
            CancellationToken cancellationToken = default)
        {
            if (node != null)
            {
                NormalizeInstanceMetadata(context, node);
            }
            return base.AddPredefinedNodeAsync(context, node!, cancellationToken);
        }

        internal static void NormalizeInstanceMetadata(ISystemContext context, NodeState node)
        {
            var children = new List<BaseInstanceState>();
            node.GetChildren(context, children);
            for (int ii = 0; ii < children.Count; ii++)
            {
                BaseInstanceState child = children[ii];
                if (child.ReferenceTypeId.IsNull)
                {
                    child.ReferenceTypeId = child is PropertyState
                        ? global::Opc.Ua.ReferenceTypeIds.HasProperty
                        : global::Opc.Ua.ReferenceTypeIds.HasComponent;
                }
                if (child.TypeDefinitionId.IsNull)
                {
                    child.TypeDefinitionId = child.GetDefaultTypeDefinitionId(context);
                }
                NormalizeInstanceMetadata(context, child);
            }
        }

        internal VisionBuildContext CreateBuildContextCore(CancellationToken cancellationToken)
        {
            return new VisionBuildContext(
                this,
                Root,
                m_options,
                m_registry,
                m_dispatcher,
                cancellationToken,
                m_services);
        }

        /// <summary>
        /// Asynchronously disposes the node manager.
        /// </summary>
        public ValueTask DisposeAsync()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
            return default;
        }

        /// <inheritdoc/>
        public override async ValueTask CreateAddressSpaceAsync(
            IDictionary<NodeId, IList<IReference>> externalReferences,
            CancellationToken cancellationToken = default)
        {
            await base.CreateAddressSpaceAsync(externalReferences, cancellationToken).ConfigureAwait(false);
            RegisterEncodeables(SystemContext);
            m_root = await GetOrCreateRootAsync(externalReferences, cancellationToken).ConfigureAwait(false);
            if (m_runner != null)
            {
                await m_runner.RunAsync(this, m_root, m_options, cancellationToken).ConfigureAwait(false);
            }
            PublishServerProfiles();
            m_dispatcherLogger.NodeManagerReady();
        }

        /// <inheritdoc/>
        protected override ValueTask<NodeStateCollection> LoadPredefinedNodesAsync(
            ISystemContext context,
            CancellationToken cancellationToken = default)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            var nodes = new NodeStateCollection();
            for (int ii = 0; ii < m_providers.Count; ii++)
            {
                m_providers[ii].AddPredefinedNodes(nodes, context);
            }
            return new ValueTask<NodeStateCollection>(nodes);
        }

        internal static ArrayOf<IVisionModelProvider> NormalizeProviders(
            ArrayOf<IVisionModelProvider> providers)
        {
            if (providers.IsNull || providers.Count == 0)
            {
                return new IVisionModelProvider[] { new VisionModelProvider() };
            }
            var sorted = new List<IVisionModelProvider>();
            for (int ii = 0; ii < providers.Count; ii++)
            {
                sorted.Add(providers[ii]);
            }
            sorted.Sort(static (left, right) => left.Order.CompareTo(right.Order));
            return sorted.ToArray().ToArrayOf();
        }

        internal static string[] GetNamespaceUris(
            ArrayOf<IVisionModelProvider> providers,
            VisionServerOptions options)
        {
            options ??= new VisionServerOptions();
            options.Validate();
            var uris = new List<string>();
            ArrayOf<IVisionModelProvider> normalized = NormalizeProviders(providers);
            for (int ii = 0; ii < normalized.Count; ii++)
            {
                ArrayOf<string> providerUris = normalized[ii].NamespaceUris;
                for (int jj = 0; jj < providerUris.Count; jj++)
                {
                    if (!uris.Contains(providerUris[jj]))
                    {
                        uris.Add(providerUris[jj]);
                    }
                }
            }
            if (!uris.Contains(options.InstanceNamespaceUri))
            {
                uris.Add(options.InstanceNamespaceUri);
            }
            return [.. uris];
        }

        internal void PublishServerProfiles()
        {
            ServerObjectState? serverObject = Server?.ServerObject;
            BaseVariableState? profileArray = serverObject?.ServerCapabilities?.ServerProfileArray;
            if (profileArray == null)
            {
                return;
            }
            ArrayOf<string> profiles = ServerProfiles;
            var merged = new List<string>();
            if (profileArray.Value.TryGetValue(out ArrayOf<string> existing))
            {
                for (int ii = 0; ii < existing.Count; ii++)
                {
                    if (!string.IsNullOrEmpty(existing[ii]) && !merged.Contains(existing[ii]))
                    {
                        merged.Add(existing[ii]);
                    }
                }
            }
            for (int ii = 0; ii < profiles.Count; ii++)
            {
                if (!string.IsNullOrEmpty(profiles[ii]) && !merged.Contains(profiles[ii]))
                {
                    merged.Add(profiles[ii]);
                }
            }
            profileArray.Value = Variant.From(merged.ToArrayOf());
            profileArray.ClearChangeMasks(SystemContext, false);
        }

        private static void RegisterEncodeables(ServerSystemContext context)
        {
            var probe = new global::Opc.Ua.Vision.VisionPose3DDataType();
            if (!context.EncodeableFactory.TryGetEncodeableType(probe.BinaryEncodingId, out _))
            {
                context.EncodeableFactory.Builder.AddOpcUaVision().Commit();
            }
        }

        private async ValueTask<VisionRootState> GetOrCreateRootAsync(
            IDictionary<NodeId, IList<IReference>> externalReferences,
            CancellationToken cancellationToken)
        {
            NodeId rootId = ExpandedNodeId.ToNodeId(
                global::Opc.Ua.Vision.ObjectIds.Vision,
                SystemContext.NamespaceUris);
            if (FindPredefinedNode<VisionRootState>(rootId) is VisionRootState existing)
            {
                await EnsureRootChildrenAsync(existing, cancellationToken).ConfigureAwait(false);
                return existing;
            }
            VisionRootState root = CreateRoot();
            await AddPredefinedNodeAsync(root, cancellationToken).ConfigureAwait(false);
            if (!externalReferences.TryGetValue(global::Opc.Ua.ObjectIds.Server, out IList<IReference>? references))
            {
                externalReferences[global::Opc.Ua.ObjectIds.Server] = references = [];
            }
            references.Add(new NodeStateReference(
                global::Opc.Ua.ReferenceTypeIds.HasComponent,
                false,
                root.NodeId));
            return root;
        }

        private async ValueTask EnsureRootChildrenAsync(VisionRootState root, CancellationToken cancellationToken)
        {
            root.CreateOrReplaceSensors(SystemContext, null);
            if (root.Sensors is FolderState sensors && FindPredefinedNode<NodeState>(sensors.NodeId) == null)
            {
                await AddPredefinedNodeAsync(sensors, cancellationToken).ConfigureAwait(false);
            }
        }

        private VisionRootState CreateRoot()
        {
            var browseName = new QualifiedName("Vision", GetVisionNamespaceIndex(SystemContext));
            VisionRootState root = global::Opc.Ua.Vision.OpcUaVisionExtensions.CreateInstanceOfVisionRootType(
                SystemContext,
                Server.ServerObject,
                browseName);
            root.ReferenceTypeId = global::Opc.Ua.ReferenceTypeIds.HasComponent;
            root.CreateOrReplaceSensors(SystemContext, null);
            root.AddReference(global::Opc.Ua.ReferenceTypeIds.HasComponent, true, global::Opc.Ua.ObjectIds.Server);
            return root;
        }

        private ushort GetInstanceNamespaceIndex(ISystemContext context)
        {
            return (ushort)context.NamespaceUris.GetIndex(m_options.InstanceNamespaceUri);
        }

        private ushort GetVisionNamespaceIndex(ServerSystemContext context)
        {
            return (ushort)context.NamespaceUris.GetIndex(global::Opc.Ua.Vision.Namespaces.Vision);
        }

        private ArrayOf<string> ComputeServerProfileArrayEntries()
        {
            ArrayOf<string> facets = VisionFacetCalculator.Compute(m_registry);
            var entries = new List<string>();
            var facetNames = new HashSet<string>(StringComparer.Ordinal);
            for (int ii = 0; ii < facets.Count; ii++)
            {
                facetNames.Add(facets[ii]);
            }
            ArrayOf<string> additional = m_options.AdditionalFacets;
            for (int ii = 0; ii < additional.Count; ii++)
            {
                if (!string.IsNullOrEmpty(additional[ii]))
                {
                    facetNames.Add(additional[ii]);
                }
            }
            ArrayOf<string> profiles = VisionFacetCalculator.ComputeProfiles(facetNames.ToArrayOf());
            for (int ii = 0; ii < profiles.Count; ii++)
            {
                if (!entries.Contains(profiles[ii]))
                {
                    entries.Add(profiles[ii]);
                }
            }
            foreach (string facetName in facetNames)
            {
                if (VisionConformanceUris.TryGetFacetUri(facetName, out string facetUri) &&
                    !entries.Contains(facetUri))
                {
                    entries.Add(facetUri);
                }
            }
            return entries.ToArrayOf();
        }

        private readonly ArrayOf<IVisionModelProvider> m_providers;
        private readonly VisionServerOptions m_options;
        private readonly IVisionPostSetupRunner? m_runner;
        private readonly IServiceProvider? m_services;
        private readonly VisionRegistry m_registry;
        private readonly VisionMethodDispatcher m_dispatcher;
        private readonly ILogger<VisionMethodDispatcher> m_dispatcherLogger;
        private VisionRootState? m_root;
    }
}
