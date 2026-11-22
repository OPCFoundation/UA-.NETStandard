// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Server;

namespace Opc.Ua.PackML.Server
{
    /// <summary>
    /// Composes standalone PackML units into an existing address-space owner.
    /// The server loads AddOpcUaPackML once and the owner disposes this runtime on shutdown.
    /// </summary>
    public sealed class PackMLRuntime : IAsyncDisposable, IConformanceContributor
    {
        /// <summary>
        /// Creates a runtime borrowing an existing asynchronous owner.
        /// </summary>
        public PackMLRuntime(AsyncCustomNodeManager manager)
        {
            m_manager = manager ?? throw new ArgumentNullException(nameof(manager));
            m_claims = s_ownerClaims.GetValue(manager, static _ => new OwnerClaims());
        }

        /// <summary>
        /// Gets the committed units in creation order, including after shutdown.
        /// </summary>
        public ArrayOf<PackMLUnit> Units
        {
            get
            {
                lock (m_unitsLock)
                {
                    return m_units.ToArray().ToArrayOf();
                }
            }
        }

        /// <summary>
        /// Gets only conformance units shared by every published PackML unit.
        /// An empty runtime contributes no PackML support.
        /// </summary>
        public ArrayOf<QualifiedName> ConformanceUnits
        {
            get
            {
                ArrayOf<PackMLUnit> units = Units;
                if (units.Count == 0)
                {
                    return [];
                }
                var common = new HashSet<QualifiedName>(units[0].ConformanceUnits.Span.ToArray());
                for (int index = 1; index < units.Count; index++)
                {
                    common.IntersectWith(units[index].ConformanceUnits.Span.ToArray());
                }
                return common.ToArrayOf();
            }
        }

        /// <summary>
        /// Gets no blanket server profile URI.
        /// </summary>
        public ArrayOf<string> ServerProfiles => [];

        /// <summary>
        /// Creates and registers a unit under PackMLObjects and a chosen parent,
        /// or directly under Objects when parent is null.
        /// </summary>
        /// <remarks>
        /// A non-null parent must be the indexed node owned by this runtime's node manager.
        /// A parent owned by another node manager is rejected before configuration or node creation.
        /// The owner must not dispose or delete an individual unit while this runtime is active.
        /// Shut down the runtime before deleting its unit and mode DataType nodes.
        /// The equipment provider remains application-owned.
        /// A matching parent and qualified browse name claimed by another runtime borrowing the same owner
        /// is rejected with BadBrowseNameDuplicated before configuration or provider access while composition
        /// is pending or either published node is still indexed.
        /// Other NodeId conflicts in the owner are rejected with BadNodeIdExists after unit creation, so
        /// configuration or provider access may already have occurred. Rejecting conflicts with other node
        /// managers before side effects requires shared server-wide admission, which is not yet available.
        /// </remarks>
        /// <exception cref="ArgumentException">
        /// <paramref name="parent"/> is not an indexed node owned by this runtime's node manager.
        /// </exception>
        /// <exception cref="ObjectDisposedException">
        /// The runtime has started shutting down.
        /// </exception>
        /// <exception cref="ServiceResultException">
        /// The name is already used under the parent, or another runtime borrowing the owner still owns it.
        /// </exception>
        public ValueTask<PackMLUnit> AddUnitAsync(
            QualifiedName browseName,
            IPackMLEquipmentProvider provider,
            Action<PackMLUnitOptions>? configure = null,
            NodeState? parent = null,
            CancellationToken cancellationToken = default)
        {
            return m_lifetime.RunAsync(
                token => AddUnitCoreAsync(browseName, provider, configure, parent, token), cancellationToken);
        }

        private Dictionary<NodeId, NodeState> CheckLocalNodeIds(NodeState unit, NodeState modeDataType)
        {
            var claimed = new Dictionary<NodeId, NodeState>();
            CheckTree(unit);
            CheckTree(modeDataType);
            return claimed;

            void CheckTree(NodeState node)
            {
                if (node.NodeId.IsNull)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadNodeIdInvalid, "A PackML instance node has no assigned NodeId.");
                }
                if (claimed.ContainsKey(node.NodeId) || m_manager.Find(node.NodeId) is not null)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadNodeIdExists, "A PackML instance NodeId is already in use.");
                }
                claimed.Add(node.NodeId, node);

                var children = new List<BaseInstanceState>();
                node.GetChildren(m_manager.SystemContext, children);
                foreach (BaseInstanceState child in children)
                {
                    CheckTree(child);
                }
            }
        }

        /// <summary>
        /// Creates and registers one admitted composition operation.
        /// </summary>
        private async ValueTask<PackMLUnit> AddUnitCoreAsync(
            QualifiedName browseName,
            IPackMLEquipmentProvider provider,
            Action<PackMLUnitOptions>? configure,
            NodeState? parent,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (parent is not null && !ReferenceEquals(m_manager.Find(parent.NodeId), parent))
            {
                throw new ArgumentException(
                    "The parent must be an indexed node owned by this runtime's node manager.", nameof(parent));
            }
            var children = new List<BaseInstanceState>();
            parent?.GetChildren(m_manager.SystemContext, children);
            lock (m_unitsLock)
            {
                if (children.Exists(child => child.BrowseName == browseName) ||
                    m_units.Exists(unit => unit.Node.Parent == parent && unit.Node.BrowseName == browseName))
                {
                    throw new ServiceResultException(StatusCodes.BadBrowseNameDuplicated);
                }
            }
            UnitClaim? claim = m_claims.TryClaim(m_manager, parent, browseName)
                ?? throw new ServiceResultException(
                    StatusCodes.BadBrowseNameDuplicated,
                    "Another PackML runtime still owns a unit with this name under the same parent.");
            NodeIdReservations.Reservation? reservation = null;
            Dictionary<NodeId, NodeState>? reservedNodes = null;
            PackMLUnit unit;
            try
            {
                var options = new PackMLUnitOptions();
                configure?.Invoke(options);
                unit = await PackMLUnit.CreateCoreAsync(
                    m_manager.SystemContext, parent, browseName, provider, options,
                    AdmitNodesAsync, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch
            {
                reservation?.ReleaseAfterRollback();
                m_claims.Release(claim);
                throw;
            }
            bool ownsNodeIds = false;
            try
            {
                CheckLocalNodeIds(unit.Node, unit.ModeDataType);
                ownsNodeIds = true;
                lock (m_unitsLock)
                {
                    m_pendingUnits.Add(unit);
                }

                await m_manager.AddPredefinedNodeAsync(unit.ModeDataType, cancellationToken).ConfigureAwait(false);
                await m_manager.AddExternalReferenceAsync(
                    unit.ModeDataType,
                    Ua.DataTypeIds.Enumeration,
                    Ua.ReferenceTypeIds.HasSubtype,
                    false,
                    cancellationToken).ConfigureAwait(false);
                unit.Node.ReferenceTypeId = parent is null
                    ? Ua.ReferenceTypeIds.Organizes
                    : Ua.ReferenceTypeIds.HasComponent;
                if (parent is null)
                {
                    unit.Node.AddReference(Ua.ReferenceTypeIds.Organizes, true, Ua.ObjectIds.ObjectsFolder);
                }
                parent?.AddChild(unit.Node);
                if (parent is not null)
                {
                    parent.AddNotifier(m_manager.SystemContext, Ua.ReferenceTypeIds.HasEventSource, false, unit.Node);
                    unit.Node.AddNotifier(m_manager.SystemContext, Ua.ReferenceTypeIds.HasEventSource, true, parent);
                }
                await m_manager.AddPredefinedNodeAsync(unit.Node, cancellationToken).ConfigureAwait(false);
                if (parent is null)
                {
                    await m_manager.AddExternalReferenceAsync(
                        unit.Node,
                        Ua.ObjectIds.ObjectsFolder,
                        Ua.ReferenceTypeIds.Organizes,
                        false,
                        cancellationToken).ConfigureAwait(false);
                }
                NodeId folderId = ExpandedNodeId.ToNodeId(
                    ObjectIds.PackMLObjects, m_manager.SystemContext.NamespaceUris);
                if (folderId.IsNull)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadNodeIdUnknown, "The PackMLObjects folder is unavailable.");
                }
                await m_manager.AddReferenceFromNodeAsync(
                    unit.Node,
                    folderId,
                    Ua.ReferenceTypeIds.Organizes,
                    false,
                    cancellationToken).ConfigureAwait(false);
                // Parentless units have no notifier to propagate through, so they
                // become Server roots. The standalone owner keeps registering every unit.
                if (parent is null || m_manager is PackMLNodeManager)
                {
                    await m_manager.RegisterRootNotifierAsync(unit.Node, cancellationToken).ConfigureAwait(false);
                }
                if (parent is not null)
                {
                    await parent.ClearChangeMasksAsync(m_manager.SystemContext, false, CancellationToken.None)
                        .ConfigureAwait(false);
                }
                // Composition is serialized, so committing after the parent
                // notification cannot admit a duplicate; the Units snapshot
                // never lists a unit that a failed notification rolls back.
                // The node itself is already reachable in the address space.
                lock (m_unitsLock)
                {
                    m_units.Add(unit);
                    m_pendingUnits.Remove(unit);
                }
                m_claims.Commit(claim, unit.Node, unit.ModeDataType);
                claim = null;
                return unit;
            }
            catch (Exception creationError)
            {
                var errors = new List<Exception> { creationError };
                try
                {
                    await unit.ShutdownAsync().ConfigureAwait(false);
                }
                catch (Exception cleanupError)
                {
                    errors.Add(cleanupError);
                }
                try
                {
                    parent?.RemoveChild(unit.Node);
                    parent?.RemoveNotifier(m_manager.SystemContext, unit.Node, true);
                }
                catch (Exception cleanupError)
                {
                    errors.Add(cleanupError);
                }
                NodeState[] registeredNodes = ownsNodeIds ? [unit.Node, unit.ModeDataType] : [];
                foreach (NodeState registeredNode in registeredNodes)
                {
                    try
                    {
                        // Never delete a node that another owner indexed under the same NodeId.
                        if (ReferenceEquals(m_manager.Find(registeredNode.NodeId), registeredNode))
                        {
                            await m_manager.DeleteNodeAsync(
                                m_manager.SystemContext, registeredNode.NodeId, CancellationToken.None)
                                .ConfigureAwait(false);
                        }
                    }
                    catch (Exception cleanupError)
                    {
                        errors.Add(cleanupError);
                    }
                }
                if (reservedNodes is not null)
                {
                    foreach (KeyValuePair<NodeId, NodeState> entry in reservedNodes)
                    {
                        if (ReferenceEquals(m_manager.Find(entry.Key), entry.Value))
                        {
                            errors.Add(new InvalidOperationException(
                                $"PackML rollback left node {entry.Key} indexed."));
                            break;
                        }
                    }
                }
                if (errors.Count > 1)
                {
                    throw new AggregateException("PackML composition failed and rollback could not finish.", errors);
                }
                reservation?.ReleaseAfterRollback();
                throw;
            }
            finally
            {
                lock (m_unitsLock)
                {
                    m_pendingUnits.Remove(unit);
                }
                if (claim is not null)
                {
                    m_claims.Release(claim);
                }
            }

            async ValueTask AdmitNodesAsync(NodeState root, NodeState modeType, CancellationToken token)
            {
                Dictionary<NodeId, NodeState> nodes = CheckLocalNodeIds(root, modeType);
                if (m_manager.Server.NodeManager is MasterNodeManager master)
                {
                    reservation = await master.ReserveManagedNodeIdsAsync(m_manager, [.. nodes.Keys], nodes, token)
                        .ConfigureAwait(false);
                    reservedNodes = nodes;
                }
            }
        }

        /// <summary>
        /// Stops creation and provider operations before the owning address space
        /// is deleted. Providers are borrowed, not disposed here.
        /// The owner deletes the unit and mode DataType nodes after shutdown.
        /// Shutdown does not clear <see cref="Units"/> or update a server's conformance publication.
        /// </summary>
        public ValueTask DisposeAsync()
        {
            m_lifetime.RejectReentry();
            lock (m_unitsLock)
            {
                foreach (PackMLUnit unit in m_units)
                {
                    unit.RejectShutdownReentry();
                }
                foreach (PackMLUnit unit in m_pendingUnits)
                {
                    unit.RejectShutdownReentry();
                }
            }
            return PackMLLifetime.CompleteShutdownAsync(DisposeCompositionAsync, DisposeUnitsAsync);
        }

        /// <summary>
        /// Closes composition admission and drains admitted creation operations.
        /// </summary>
        private async ValueTask DisposeCompositionAsync()
        {
            await m_lifetime.DisposeAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// Starts cancellation on every committed unit before joining their shutdown.
        /// </summary>
        private async ValueTask DisposeUnitsAsync()
        {
            ArrayOf<PackMLUnit> units = Units;
            var shutdown = new Task[units.Count];
            for (int index = 0; index < units.Count; index++)
            {
                shutdown[index] = units[index].ShutdownAsync().AsTask();
            }
            Task all = Task.WhenAll(shutdown);
            try
            {
                await all.ConfigureAwait(false);
            }
            catch
            {
                if (all.Exception is { } errors)
                {
                    throw errors;
                }
                throw;
            }
        }

        /// <summary>The borrowed address-space owner.</summary>
        private readonly AsyncCustomNodeManager m_manager;

        /// <summary>Protects the committed and pending unit lists.</summary>
        private readonly System.Threading.Lock m_unitsLock = new();

        /// <summary>The committed units in creation order.</summary>
        private readonly List<PackMLUnit> m_units = [];

        /// <summary>Units being published or rolled back.</summary>
        private readonly List<PackMLUnit> m_pendingUnits = [];

        /// <summary>Serializes composition and drains it during shutdown.</summary>
        private readonly PackMLLifetime m_lifetime = new();

        /// <summary>Unit-name claims shared by every runtime borrowing the same owner.</summary>
        private readonly OwnerClaims m_claims;

        /// <summary>Claims keyed by borrowed owner without extending the owner's lifetime.</summary>
        private static readonly ConditionalWeakTable<AsyncCustomNodeManager, OwnerClaims> s_ownerClaims = new();

        /// <summary>
        /// Records one unit name that a runtime is composing or has published.
        /// </summary>
        private sealed class UnitClaim
        {
            /// <summary>
            /// Creates a pending claim.
            /// </summary>
            public UnitClaim(NodeState? parent, QualifiedName browseName)
            {
                Parent = parent;
                BrowseName = browseName;
            }

            /// <summary>
            /// Gets the parent, or null for a parentless unit.
            /// </summary>
            public NodeState? Parent { get; }

            /// <summary>
            /// Gets the claimed browse name.
            /// </summary>
            public QualifiedName BrowseName { get; }

            /// <summary>
            /// Gets or sets the published unit node; null while composition is pending.
            /// </summary>
            public NodeState? UnitNode { get; set; }

            /// <summary>
            /// Gets or sets the published mode DataType node.
            /// </summary>
            public NodeState? ModeDataType { get; set; }
        }

        /// <summary>
        /// Tracks unit-name ownership across runtimes that borrow one owner.
        /// </summary>
        private sealed class OwnerClaims
        {
            /// <summary>
            /// Claims a name unless a pending or still-indexed unit owns it.
            /// </summary>
            /// <returns>The claim, or null when the name is still owned.</returns>
            public UnitClaim? TryClaim(AsyncCustomNodeManager manager, NodeState? parent, QualifiedName browseName)
            {
                lock (m_lock)
                {
                    for (int index = m_claims.Count - 1; index >= 0; index--)
                    {
                        UnitClaim existing = m_claims[index];
                        if (!ReferenceEquals(existing.Parent, parent) || existing.BrowseName != browseName)
                        {
                            continue;
                        }
                        if (existing.UnitNode is null || IsIndexed(manager, existing.UnitNode) ||
                            (existing.ModeDataType is not null && IsIndexed(manager, existing.ModeDataType)))
                        {
                            return null;
                        }
                        // Neither published node instance is currently indexed by this owner.
                        m_claims.RemoveAt(index);
                    }
                    var claim = new UnitClaim(parent, browseName);
                    m_claims.Add(claim);
                    return claim;
                }
            }

            /// <summary>
            /// Marks a pending claim as published by the given nodes.
            /// </summary>
            public void Commit(UnitClaim claim, NodeState unitNode, NodeState modeDataType)
            {
                lock (m_lock)
                {
                    claim.UnitNode = unitNode;
                    claim.ModeDataType = modeDataType;
                }
            }

            /// <summary>
            /// Releases a claim whose composition did not publish.
            /// </summary>
            public void Release(UnitClaim claim)
            {
                lock (m_lock)
                {
                    m_claims.Remove(claim);
                }
            }

            /// <summary>
            /// Determines whether the owner still indexes this exact node instance.
            /// </summary>
            private static bool IsIndexed(AsyncCustomNodeManager manager, NodeState node)
            {
                return ReferenceEquals(manager.Find(node.NodeId), node);
            }

            private readonly System.Threading.Lock m_lock = new();
            private readonly List<UnitClaim> m_claims = [];
        }
    }
}
