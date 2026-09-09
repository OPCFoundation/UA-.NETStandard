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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Server;
using Opc.Ua.Server.Hosting;

namespace Opc.Ua.Redundancy.Server
{
    /// <summary>
    /// Assigns shared node identities under a fixed replica-set namespace and allocation contract.
    /// </summary>
    public sealed class ReplicaNodeIdFactory : IRebasableNodeIdFactory, INodeIdFactoryPolicy, IServerPreStartupTask
    {
        /// <summary>
        /// Creates a replica identity factory. Shared namespaces occupy slots starting at two.
        /// </summary>
        /// <param name="replicaSetId">Stable identifier of the replica set.</param>
        /// <param name="namespaceUris">Ordered shared model and instance namespaces.</param>
        /// <param name="mode">Deterministic assignment mode for named shared nodes.</param>
        /// <param name="writerElection">
        /// Optional active/passive election authorizing shared counter allocations only on its writer.
        /// Omit for independent active/active creation.
        /// </param>
        /// <param name="store">
        /// Optional shared active/passive state store bound to this contract before node creation.
        /// </param>
        /// <param name="protector">Protection for the stored contract; required with a replicated state store.</param>
        public ReplicaNodeIdFactory(
            string replicaSetId,
            ArrayOf<string> namespaceUris,
            NodeIdAssignmentMode mode = NodeIdAssignmentMode.Numeric,
            ILeaderElection? writerElection = null,
            ISharedKeyValueStore? store = null,
            IRecordProtector? protector = null)
            : this(
                new IdentityState(replicaSetId, namespaceUris, mode, writerElection, store, protector),
                new DefaultNodeIdFactory(mode))
        {
        }

        private ReplicaNodeIdFactory(IdentityState state, DefaultNodeIdFactory inner)
        {
            m_state = state;
            m_inner = IsSharedNamespace(inner.DefaultNamespaceIndex) ? inner.WithCollisionDetection(true) : inner;
        }

        /// <inheritdoc/>
        public NodeIdAssignmentMode Mode => m_inner.Mode;

        /// <inheritdoc/>
        public ushort DefaultNamespaceIndex => m_inner.DefaultNamespaceIndex;

        /// <inheritdoc/>
        public bool DetectsCollisions => m_inner.DetectsCollisions;

        /// <summary>
        /// Gets the immutable versioned replica identity descriptor, excluding replica-local namespace values.
        /// </summary>
        public ByteString Descriptor => m_state.Descriptor;

        /// <summary>
        /// Gets the fixed shared namespace URIs in slot order.
        /// </summary>
        public ArrayOf<string> NamespaceUris => m_state.NamespaceUris;

        /// <summary>
        /// Gets whether no incompatible peer has invalidated this replica's identity admission.
        /// </summary>
        public bool IsCompatible => Volatile.Read(ref m_state.Incompatible) == 0;

        internal bool UsesWriterAssignedIds => m_state.WriterElection != null;

        /// <inheritdoc/>
        public async ValueTask OnNodeManagersChangedAsync(
            IServerContext server,
            CancellationToken cancellationToken = default)
        {
            if (server == null)
            {
                throw new ArgumentNullException(nameof(server));
            }
            Func<IServerContext, CancellationToken, ValueTask>? rebind = m_state.Rebind;
            if (rebind == null)
            {
                return;
            }
            List<ILocalAddressSpaceSource> sources = CaptureSources(server);
            if (!m_state.RebindRequired && sources.Count == m_state.Sources.Count)
            {
                bool unchanged = true;
                for (int i = 0; i < sources.Count; i++)
                {
                    if (!ReferenceEquals(sources[i], m_state.Sources[i]))
                    {
                        unchanged = false;
                        break;
                    }
                }
                if (unchanged)
                {
                    return;
                }
            }
            m_state.RebindRequired = true;
            await rebind(server, cancellationToken).ConfigureAwait(false);
            m_state.Sources = sources;
            m_state.RebindRequired = false;
        }

        /// <inheritdoc/>
        public async ValueTask PrepareNodeManagerAsync(
            IServerContext server,
            IAsyncNodeManager nodeManager,
            IAsyncNodeManager? replacedNodeManager,
            CancellationToken cancellationToken = default)
        {
            if (server == null)
            {
                throw new ArgumentNullException(nameof(server));
            }
            if (nodeManager == null)
            {
                throw new ArgumentNullException(nameof(nodeManager));
            }
            if (m_state.PrepareManager is { } prepare)
            {
                m_state.RebindRequired = true;
                await prepare(server, nodeManager, replacedNodeManager, cancellationToken).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Establishes shared namespace slots without reordering existing entries.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="namespaces"/> is <c>null</c>.</exception>
        /// <exception cref="ServiceResultException">
        /// The namespace table or replica admission is incompatible with the configured identity contract.
        /// </exception>
        public void PrepareNamespaces(NamespaceTable namespaces)
        {
            if (namespaces == null)
            {
                throw new ArgumentNullException(nameof(namespaces));
            }
            if (namespaces.Count < 2 || namespaces.GetString(0) != Namespaces.OpcUa)
            {
                throw ConfigurationError("The server's standard and local application namespaces must exist first.");
            }
            for (int i = 0; i < NamespaceUris.Count; i++)
            {
                string uri = NamespaceUris[i];
                int expected = i + 2;
                int current = namespaces.GetIndex(uri);
                if ((current >= 0 && current != expected) ||
                    (namespaces.Count > expected && namespaces.GetString((uint)expected) != uri))
                {
                    throw ConfigurationError($"Shared namespace '{uri}' must occupy index {expected}.");
                }
            }
            for (int i = 0; i < NamespaceUris.Count; i++)
            {
                if (namespaces.Count == i + 2)
                {
                    namespaces.Append(NamespaceUris[i]);
                }
            }
            ValidateNamespaces(namespaces);
        }

        /// <summary>
        /// Rejects a namespace table whose shared slots differ from the configured layout.
        /// </summary>
        /// <exception cref="ServiceResultException">
        /// The replica has been rejected or the shared namespace slots differ from the configured layout.
        /// </exception>
        /// <exception cref="ArgumentNullException"><paramref name="namespaces"/> is <c>null</c>.</exception>
        public void ValidateNamespaces(NamespaceTable namespaces)
        {
            if (!IsCompatible)
            {
                throw ConfigurationError(
                    "This replica received an incompatible identity contract and must not join the set.");
            }
            if (namespaces == null)
            {
                throw new ArgumentNullException(nameof(namespaces));
            }
            for (int i = 0; i < NamespaceUris.Count; i++)
            {
                if (namespaces.GetString((uint)(i + 2)) != NamespaceUris[i])
                {
                    throw ConfigurationError($"Replica namespace slot {i + 2} must contain '{NamespaceUris[i]}'.");
                }
            }
        }

        /// <summary>
        /// Returns whether the identifier belongs to the configured shared namespace layout.
        /// </summary>
        public bool IsShared(NodeId nodeId)
        {
            return !nodeId.IsNull && nodeId.NamespaceIndex >= 2 && nodeId.NamespaceIndex < NamespaceUris.Count + 2;
        }

        /// <inheritdoc/>
        public NodeId New(ISystemContext context, NodeState node)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            if (node == null)
            {
                throw new ArgumentNullException(nameof(node));
            }
            ValidateNamespaces(context.NamespaceUris);
            NodeId original = node.NodeId;
            bool preserved = !original.IsNull &&
                (original.NamespaceIndex == DefaultNamespaceIndex ||
                    node is not BaseInstanceState { Parent: not null });
            if (!preserved && Mode != NodeIdAssignmentMode.None && IsTransientEvent(node))
            {
                return m_inner.WithDefaultNamespaceIndex(1).WithCollisionDetection(false).NextCounterNodeId();
            }
            if (!preserved && IsSharedNamespace(DefaultNamespaceIndex))
            {
                ValidatePath(context.NamespaceUris, node);
                using ReplicaNamespaceValidator validator = CreateNamespaceValidator(context);
                validator.ValidateNode(context, node);
            }
            NodeId result = !preserved &&
                Mode != NodeIdAssignmentMode.None &&
                (Mode == NodeIdAssignmentMode.Counter || !m_inner.HasDerivablePath(node))
                ? NextCounterNodeId()
                : m_inner.New(context, node);
            if (!preserved &&
                IsShared(result) &&
                (Mode == NodeIdAssignmentMode.Counter || !m_inner.HasDerivablePath(node)))
            {
                m_state.CounterIds.TryAdd(result, true);
            }
            return result;
        }

        /// <inheritdoc/>
        public NodeId NextCounterNodeId()
        {
            NodeId nodeId;
            do
            {
                nodeId = m_inner.NextCounterNodeId();
            }
            while (m_state.Registered.ContainsKey(nodeId) || m_state.RetainedIds.ContainsKey(nodeId));
            if (IsShared(nodeId))
            {
                m_state.CounterIds.TryAdd(nodeId, true);
            }
            return nodeId;
        }

        /// <inheritdoc/>
        public NodeId CreateChildNodeId(
            NodeId parentNodeId,
            QualifiedName browseName,
            ushort namespaceIndex,
            NamespaceTable namespaceUris)
        {
            ValidateNamespaces(namespaceUris);
            ValidateMode(namespaceIndex, Mode);
            if (IsSharedNamespace(namespaceIndex))
            {
                ValidateNamespaceReference(parentNodeId.NamespaceIndex, namespaceUris);
                ValidateNamespaceReference(browseName.NamespaceIndex, namespaceUris);
            }
            if (Mode == NodeIdAssignmentMode.Counter)
            {
                return NextCounterNodeId();
            }
            return m_inner.CreateChildNodeId(parentNodeId, browseName, namespaceIndex, namespaceUris);
        }

        /// <inheritdoc/>
        public IRebasableNodeIdFactory WithDefaultNamespaceIndex(ushort defaultNamespaceIndex)
        {
            ValidateMode(defaultNamespaceIndex, Mode);
            return new ReplicaNodeIdFactory(m_state, m_inner.WithDefaultNamespaceIndex(defaultNamespaceIndex));
        }

        /// <inheritdoc/>
        public IRebasableNodeIdFactory WithMode(NodeIdAssignmentMode mode)
        {
            ValidateMode(DefaultNamespaceIndex, mode);
            return new ReplicaNodeIdFactory(m_state, m_inner.WithMode(mode));
        }

        /// <inheritdoc/>
        public IRebasableNodeIdFactory WithCollisionDetection(bool detectCollisions)
        {
            return IsSharedNamespace(DefaultNamespaceIndex)
                ? this
                : new ReplicaNodeIdFactory(m_state, m_inner.WithCollisionDetection(detectCollisions));
        }

        /// <inheritdoc/>
        public IRebasableNodeIdFactory Apply(IRebasableNodeIdFactory factory)
        {
            if (factory == null)
            {
                throw new ArgumentNullException(nameof(factory));
            }
            if (factory is ReplicaNodeIdFactory replica)
            {
                if (!ReferenceEquals(replica.m_state, m_state))
                {
                    throw ConfigurationError("A node manager cannot replace the configured replica identity contract.");
                }
                return replica;
            }
            if (factory.GetType() != typeof(DefaultNodeIdFactory))
            {
                throw ConfigurationError("A replacement factory must preserve the server's replica identity policy.");
            }
            return WithDefaultNamespaceIndex(factory.DefaultNamespaceIndex).WithMode(factory.Mode);
        }

        /// <inheritdoc/>
        public void ValidateRegistration(ISystemContext context, NodeState node, bool isServerInfrastructure = false)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            if (node == null)
            {
                throw new ArgumentNullException(nameof(node));
            }
            if (isServerInfrastructure && !IsShared(node.NodeId))
            {
                return;
            }
            ValidateNamespaces(context.NamespaceUris);
            using ReplicaNamespaceValidator validator = CreateNamespaceValidator(context);
            ValidateRegistrationTree(context, node, validator);
        }

        private void ValidateRegistrationTree(
            ISystemContext context,
            NodeState node,
            ReplicaNamespaceValidator validator)
        {
            if (node.NodeId.NamespaceIndex > 1 && !IsShared(node.NodeId))
            {
                throw ConfigurationError($"Node '{node.NodeId}' uses an undeclared shared namespace.");
            }
            if (!IsShared(node.NodeId))
            {
                return;
            }
            if (m_state.CounterIds.ContainsKey(node.NodeId) &&
                m_state.WriterElection?.IsLeader != true &&
                !m_state.InboundNodes.TryGetValue(node, out _))
            {
                throw ConfigurationError(
                    "A shared counter/no-path NodeId must be assigned once by the active writer, " +
                    "or supplied explicitly from stable application identity.");
            }
            ValidateNamespaceReference(node.BrowseName.NamespaceIndex, context.NamespaceUris);
            validator.ValidateNode(context, node);
            NodeId parentId = (node as BaseInstanceState)?.Parent?.NodeId ?? NodeId.Null;
            ValidateNamespaceReference(parentId.NamespaceIndex, context.NamespaceUris);
            string identity = DefaultNodeIdFactory.CreateCanonicalPath(
                parentId == ObjectIds.ObjectsFolder ? NodeId.Null : parentId,
                node.BrowseName,
                node.NodeId.NamespaceIndex,
                context.NamespaceUris);
            var registration = new Registration(new WeakReference<NodeState>(node), identity);
            Registration existing = m_state.Registered.GetOrAdd(node.NodeId, registration);
            if ((!existing.Node.TryGetTarget(out NodeState? known) || !ReferenceEquals(known, node)) &&
                existing.Identity != identity)
            {
                throw ConfigurationError($"NodeId '{node.NodeId}' already identifies a different shared node.");
            }
            var children = new List<BaseInstanceState>();
            node.GetChildren(context, children);
            foreach (BaseInstanceState child in children)
            {
                if (!IsShared(child.NodeId))
                {
                    throw ConfigurationError("A shared subtree contains a local or undeclared child identity.");
                }
                ValidateRegistrationTree(context, child, validator);
            }
        }

        /// <summary>
        /// Validates incoming shared topology without allocating replacement identifiers.
        /// </summary>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="context"/> or <paramref name="node"/> is <c>null</c>.
        /// </exception>
        /// <exception cref="ServiceResultException">
        /// The incoming tree or namespace table is incompatible with the shared identity contract.
        /// </exception>
        public void ValidateReplicatedTree(ISystemContext context, NodeState node)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            if (node == null)
            {
                throw new ArgumentNullException(nameof(node));
            }
            ValidateNamespaces(context.NamespaceUris);
            using ReplicaNamespaceValidator validator = CreateNamespaceValidator(context);
            ValidateReplicatedTree(context, node, validator);
        }

        private void ValidateReplicatedTree(
            ISystemContext context,
            NodeState node,
            ReplicaNamespaceValidator validator)
        {
            if (!IsShared(node.NodeId))
            {
                throw ConfigurationError($"Replicated node '{node.NodeId}' is outside the shared namespace layout.");
            }
            ValidateNamespaceReference(node.BrowseName.NamespaceIndex, context.NamespaceUris);
            validator.ValidateNode(context, node);
            var children = new List<BaseInstanceState>();
            node.GetChildren(context, children);
            foreach (BaseInstanceState child in children)
            {
                ValidateReplicatedTree(context, child, validator);
            }
        }

        /// <inheritdoc/>
        public async ValueTask OnServerStartingAsync(
            IServerContext server,
            CancellationToken cancellationToken = default)
        {
            if (server == null)
            {
                throw new ArgumentNullException(nameof(server));
            }
            cancellationToken.ThrowIfCancellationRequested();
            if (m_state.WriterElection != null && m_state.Store == null)
            {
                throw ConfigurationError(
                    "Writer-assigned identities require the shared address-space store at startup.");
            }
            PrepareNamespaces(server.MessageContext.NamespaceUris);
            m_state.Server = new WeakReference<IServerContext>(server);
            if (m_state.Store != null)
            {
                await ReplicaIdentityStore.VerifyAsync(
                    m_state.Store,
                    m_state.Protector,
                    Descriptor,
                    cancellationToken).ConfigureAwait(false);
                using var nodes = new InMemoryNodeStateStore(m_state.Store, server.MessageContext, m_state.Protector);
                await foreach ((IStoredNode stored, _) in nodes.EnumerateRetainedNodesAsync(cancellationToken)
                    .ConfigureAwait(false))
                {
                    ReserveId(stored.NodeId);
                    if (!stored.Payload.IsEmpty)
                    {
                        NodeState node = NodeStateSerializer.Deserialize(server.DefaultSystemContext, stored.Payload);
                        ValidateReplicatedTree(server.DefaultSystemContext, node);
                        if (node.NodeId != stored.NodeId)
                        {
                            throw ConfigurationError("Stored topology does not match its original node identity.");
                        }
                        ReserveTree(server.DefaultSystemContext, node);
                    }
                }
            }
            if (m_state.WriterElection != null)
            {
                await m_state.WriterElection.TryAcquireOrRenewAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Provisions the protected contract on a verified new linearizable backend before composing a hybrid store.
        /// </summary>
        /// <remarks>
        /// Run this administrative step before attaching any replicated payload backend. That backend must also be
        /// new; this operation cannot inspect disconnected replicas or adopt an existing unknown namespace layout.
        /// Startup never performs this provisioning from an empty eventual scan.
        /// </remarks>
        /// <param name="store">The verified new linearizable backend.</param>
        /// <param name="protector">The replica set's record protector.</param>
        /// <param name="cancellationToken">Cancels provisioning without overwriting an existing contract.</param>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="store"/> or <paramref name="protector"/> is <c>null</c>.
        /// </exception>
        public ValueTask InitializeNewStoreAsync(
            ISharedKeyValueStore store,
            IRecordProtector protector,
            CancellationToken cancellationToken = default)
        {
            if (store == null)
            {
                throw new ArgumentNullException(nameof(store));
            }
            if (protector == null)
            {
                throw new ArgumentNullException(nameof(protector));
            }
            return ReplicaIdentityStore.VerifyAsync(store, protector, Descriptor, cancellationToken);
        }

        internal void RejectPeer()
        {
            Interlocked.Exchange(ref m_state.Incompatible, 1);
            if (m_state.Server?.TryGetTarget(out IServerContext? server) == true &&
                server.ServerObject?.ServiceLevel is { } serviceLevel)
            {
                serviceLevel.Value = 0;
                serviceLevel.ClearChangeMasks(server.DefaultSystemContext, false);
            }
        }

        internal void ReserveId(NodeId nodeId)
        {
            if (IsShared(nodeId))
            {
                m_state.RetainedIds.TryAdd(nodeId, true);
            }
        }

        internal void SetAddressSpaceRebinder(
            IServerContext server,
            Func<IServerContext, CancellationToken, ValueTask>? rebind)
        {
            m_state.Sources = CaptureSources(server);
            m_state.Rebind = rebind;
        }

        internal void ClearAddressSpaceRebinder()
        {
            m_state.Rebind = null;
            m_state.PrepareManager = null;
            m_state.Sources = [];
        }

        internal void SetNodeManagerPreparer(
            Func<IServerContext, IAsyncNodeManager, IAsyncNodeManager?, CancellationToken, ValueTask> prepare)
        {
            m_state.PrepareManager = prepare;
        }

        private static List<ILocalAddressSpaceSource> CaptureSources(IServerContext server)
        {
            var sources = new List<ILocalAddressSpaceSource>();
            sources.AddRange(server.FindNodeManagers<ILocalAddressSpaceSource>());
            return sources;
        }

        internal void ValidateValue(ISystemContext context, in DataValue value)
        {
            using ReplicaNamespaceValidator validator = CreateNamespaceValidator(context);
            validator.WriteDataValue(null, value);
        }

        private ReplicaNamespaceValidator CreateNamespaceValidator(ISystemContext context)
        {
            IServiceMessageContext messages = context is ServerSystemContext serverContext
                ? serverContext.Server.MessageContext
                : context.AsMessageContext();
            return new ReplicaNamespaceValidator(messages, NamespaceUris);
        }

        internal void AuthorizeReplicatedTree(ISystemContext context, NodeState node)
        {
            ValidateReplicatedTree(context, node);
            MarkInboundTree(context, node);
            ValidateRegistration(context, node);
        }

        private void MarkInboundTree(ISystemContext context, NodeState node)
        {
            _ = m_state.InboundNodes.GetValue(node, static _ => InboundRegistration.Instance);
            ReserveId(node.NodeId);
            var children = new List<BaseInstanceState>();
            node.GetChildren(context, children);
            foreach (BaseInstanceState child in children)
            {
                MarkInboundTree(context, child);
            }
        }

        private void ReserveTree(ISystemContext context, NodeState node)
        {
            ReserveId(node.NodeId);
            var children = new List<BaseInstanceState>();
            node.GetChildren(context, children);
            foreach (BaseInstanceState child in children)
            {
                ReserveTree(context, child);
            }
        }

        private bool IsSharedNamespace(ushort index)
        {
            return index >= 2 && index < NamespaceUris.Count + 2;
        }

        private void ValidateMode(ushort index, NodeIdAssignmentMode mode)
        {
            if (IsSharedNamespace(index) &&
                mode != m_state.Mode &&
                mode is not NodeIdAssignmentMode.Counter and not NodeIdAssignmentMode.None)
            {
                throw ConfigurationError("Shared deterministic assignment mode differs from the replica contract.");
            }
        }

        private void ValidatePath(NamespaceTable namespaces, NodeState node)
        {
            ValidateNamespaceReference(node.BrowseName.NamespaceIndex, namespaces);
            if (node is BaseInstanceState { Parent: { } parent } && !parent.NodeId.IsNull)
            {
                ValidateNamespaceReference(parent.NodeId.NamespaceIndex, namespaces);
            }
        }

        private void ValidateNamespaceReference(ushort index, NamespaceTable namespaces)
        {
            if (index != 0 && !IsSharedNamespace(index))
            {
                throw ConfigurationError(
                    "Shared node identity cannot depend on a replica-local or undeclared namespace.");
            }
            if (string.IsNullOrEmpty(namespaces.GetString(index)))
            {
                throw ConfigurationError("Shared node identity requires registered namespace URIs.");
            }
        }

        private static bool IsTransientEvent(NodeState node)
        {
            while (node is BaseInstanceState { Parent: { } parent })
            {
                node = parent;
            }
            return node is BaseEventState;
        }

        private static ServiceResultException ConfigurationError(string message)
        {
            return new ServiceResultException(StatusCodes.BadConfigurationError, message);
        }

        private sealed class IdentityState
        {
            public IdentityState(
                string replicaSetId,
                ArrayOf<string> namespaceUris,
                NodeIdAssignmentMode mode,
                ILeaderElection? election,
                ISharedKeyValueStore? store,
                IRecordProtector? protector)
            {
                if (string.IsNullOrWhiteSpace(replicaSetId) || namespaceUris.IsEmpty || namespaceUris.Count > 65534)
                {
                    throw new ArgumentException(
                        "A replica-set identity and a nonempty fixed namespace list are required.");
                }
                if (mode is not NodeIdAssignmentMode.Numeric and not NodeIdAssignmentMode.String and
                    not NodeIdAssignmentMode.Guid and not NodeIdAssignmentMode.Opaque)
                {
                    throw new ArgumentException(
                        "Shared named nodes require a deterministic assignment mode.",
                        nameof(mode));
                }
                var seen = new HashSet<string>(StringComparer.Ordinal) { Namespaces.OpcUa };
                string[] copy = new string[namespaceUris.Count];
                for (int i = 0; i < copy.Length; i++)
                {
                    string uri = namespaceUris[i];
                    if (string.IsNullOrWhiteSpace(uri) ||
                        !Uri.IsWellFormedUriString(uri, UriKind.Absolute) ||
                        !seen.Add(uri))
                    {
                        throw new ArgumentException("Shared namespaces must be unique, non-standard absolute URIs.");
                    }
                    copy[i] = uri;
                }
                NamespaceUris = new ArrayOf<string>(copy);
                Mode = mode;
                WriterElection = election;
                Store = store;
                Protector = protector ?? NullRecordProtector.Instance;
                if (store is ISharedKeyValueStoreConsistency consistency &&
                    !consistency.IsProcessLocal(ReplicaIdentityStore.Key) &&
                    Protector is NullRecordProtector)
                {
                    throw new ArgumentException(
                        "A replicated identity contract requires record protection.",
                        nameof(protector));
                }
                var descriptor = new StringBuilder("replica-nodeid/v1;");
                Append(descriptor, replicaSetId);
                Append(descriptor, DefaultNodeIdFactory.CanonicalPathVersion);
                Append(descriptor, ((int)mode).ToString(CultureInfo.InvariantCulture));
                Append(descriptor, election == null ? "independent" : "writer-assigned");
                foreach (string uri in copy)
                {
                    Append(descriptor, uri);
                }
                Descriptor = new ByteString(Encoding.UTF8.GetBytes(descriptor.ToString()));
            }

            public ArrayOf<string> NamespaceUris { get; }

            public NodeIdAssignmentMode Mode { get; }

            public ILeaderElection? WriterElection { get; }

            public ISharedKeyValueStore? Store { get; }

            public IRecordProtector Protector { get; }

            public ByteString Descriptor { get; }

            public ConcurrentDictionary<NodeId, bool> CounterIds { get; } = new();

            public ConcurrentDictionary<NodeId, Registration> Registered { get; } = new();

            public ConcurrentDictionary<NodeId, bool> RetainedIds { get; } = new();

            public ConditionalWeakTable<NodeState, InboundRegistration> InboundNodes { get; } =
#if NETSTANDARD2_1_OR_GREATER || NET5_0_OR_GREATER
                [];
#else
                new();
#endif

            public WeakReference<IServerContext>? Server { get; set; }

            public Func<IServerContext, CancellationToken, ValueTask>? Rebind { get; set; }

            public Func<IServerContext, IAsyncNodeManager, IAsyncNodeManager?, CancellationToken, ValueTask>?
                PrepareManager
            { get; set; }

            public List<ILocalAddressSpaceSource> Sources { get; set; } = [];

            public bool RebindRequired { get; set; }

            public int Incompatible;

            private static void Append(StringBuilder builder, string value)
            {
                builder.Append(value.Length.ToString(CultureInfo.InvariantCulture))
                    .Append(':')
                    .Append(value)
                    .Append(';');
            }
        }

        private sealed record Registration(WeakReference<NodeState> Node, string Identity);

        private sealed class InboundRegistration
        {
            public static InboundRegistration Instance { get; } = new();
        }

        private readonly IdentityState m_state;
        private readonly DefaultNodeIdFactory m_inner;
    }
}
