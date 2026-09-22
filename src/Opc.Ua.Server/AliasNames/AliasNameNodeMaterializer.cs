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
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Opc.Ua.Server.AliasNames
{
    /// <summary>
    /// The node-manager capabilities the
    /// <see cref="AliasNameNodeMaterializer"/> needs from its host. Both
    /// <c>DiagnosticsNodeManager</c> (which owns the standard well-known
    /// Part 17 nodes) and <see cref="AliasNameNodeManager"/> (which owns an
    /// application namespace) implement this over their
    /// <c>AsyncCustomNodeManager</c> surface, so the descriptor walk,
    /// capability handling and alias materialization exist exactly once.
    /// </summary>
    internal interface IAliasNameMaterializerHost
    {
        /// <summary>
        /// The host manager's system context.
        /// </summary>
        ISystemContext SystemContext { get; }

        /// <summary>
        /// The server type tree used for reference-type filter matching.
        /// </summary>
        ITypeTable TypeTree { get; }

        /// <summary>
        /// The server namespace table, for resolving namespace-URI-form
        /// <see cref="ExpandedNodeId"/> targets.
        /// </summary>
        NamespaceTable NamespaceUris { get; }

        /// <summary>
        /// The server table of server URIs; remote alias targets register
        /// their URI here and reference it via <c>ServerIndex</c>.
        /// </summary>
        StringTable ServerUris { get; }

        /// <summary>
        /// The single namespace the materializer may mint NodeIds and
        /// BrowseNames into. Descriptors whose category NodeId lies in any
        /// other namespace resolve to predefined nodes or are skipped.
        /// </summary>
        ushort MaterializationNamespaceIndex { get; }

        /// <summary>
        /// Resolves an already-registered category node, or null.
        /// </summary>
        /// <param name="nodeId">The category NodeId to resolve.</param>
        /// <returns>The registered category node, or null when no category is registered at that NodeId.</returns>
        AliasNameCategoryState? FindCategoryNode(NodeId nodeId);

        /// <summary>
        /// Looks up any registered node at the given id — used to detect
        /// occupants that are not alias nodes before minting.
        /// </summary>
        /// <param name="nodeId">The NodeId to look up.</param>
        /// <param name="node">The registered node, or null when no node was found.</param>
        /// <returns>True when a registered node was found; otherwise, false.</returns>
        bool TryGetNode(NodeId nodeId, out NodeState? node);

        /// <summary>
        /// Registers a node (and its children) with the host manager so it
        /// serves Browse and Call for it.
        /// </summary>
        /// <param name="node">The node whose subtree must be registered.</param>
        /// <param name="cancellationToken">The token used to cancel registration.</param>
        /// <returns>A task that completes after registration.</returns>
        ValueTask RegisterNodeAsync(NodeState node, CancellationToken cancellationToken);

        /// <summary>
        /// Removes a previously materialized node and its references.
        /// </summary>
        /// <param name="nodeId">The materialized node to remove.</param>
        /// <param name="cancellationToken">The token used to cancel removal.</param>
        /// <returns>A task that completes after removal.</returns>
        ValueTask RemoveNodeAsync(NodeId nodeId, CancellationToken cancellationToken);

        /// <summary>
        /// Mints a fresh NodeId for a node whose deterministic id
        /// collided.
        /// </summary>
        /// <param name="node">The node that needs a fresh identifier.</param>
        /// <returns>A new NodeId allocated by the host.</returns>
        NodeId MintNodeId(NodeState node);

        /// <summary>
        /// Links a root category (one with no parent in the descriptor
        /// tree) into the browse hierarchy — typically under the standard
        /// <c>Aliases (i=23470)</c> object, directly when the host owns
        /// that node and via <paramref name="externalReferences"/>
        /// otherwise. Hosts may make this a no-op (an opt-out option, or a
        /// root that is itself the standard Aliases node).
        /// </summary>
        /// <param name="root">The root category to link.</param>
        /// <param name="externalReferences">The references to install through other node managers.</param>
        void LinkRootCategory(
            AliasNameCategoryState root,
            IDictionary<NodeId, IList<IReference>> externalReferences);

        /// <summary>
        /// Queues the inverse <c>HasAlias</c> (<c>AliasFor</c> inverse)
        /// reference on a local target node, which usually lives in
        /// another node manager and therefore travels through
        /// <paramref name="externalReferences"/>.
        /// </summary>
        /// <param name="targetId">The local target of the alias.</param>
        /// <param name="aliasNodeId">The alias node referenced by the inverse link.</param>
        /// <param name="externalReferences">The references to install through other node managers.</param>
        void AddInverseAliasReference(
            NodeId targetId,
            NodeId aliasNodeId,
            IDictionary<NodeId, IList<IReference>> externalReferences);

        /// <summary>
        /// Updates a live target's inverse reference after address-space startup.
        /// </summary>
        /// <param name="targetId">The local target whose inverse reference must change.</param>
        /// <param name="aliasNodeId">The alias node referenced by the inverse link.</param>
        /// <param name="remove">True to remove the link; false to add it.</param>
        /// <param name="cancellationToken">The token used to cancel the update.</param>
        /// <returns>A task that completes after the reference update.</returns>
        ValueTask UpdateInverseAliasReferenceAsync(
            NodeId targetId,
            NodeId aliasNodeId,
            bool remove,
            CancellationToken cancellationToken);

        /// <summary>
        /// Notifies the host that a category's <c>LastChange</c> property
        /// was created or seeded, so the host can keep it current from its
        /// store/registry Changed events.
        /// </summary>
        /// <param name="categoryId">The category whose LastChange property was bound.</param>
        /// <param name="lastChange">The property instance to keep current.</param>
        void OnLastChangeBound(NodeId categoryId, PropertyState<uint> lastChange);
    }

    /// <summary>
    /// Shared OPC UA Part 17 address-space builder: walks an
    /// <see cref="IAliasNameStore"/>'s descriptor tree and materializes it
    /// as browsable nodes — an <c>AliasNameCategoryType</c> instance per
    /// category the host does not already ship (with the optional
    /// <c>FindAliasVerbose</c>/<c>AddAliasesToCategory</c>/<c>DeleteAliasesFromCategory</c>/<c>LastChange</c>
    /// children its <see cref="AliasNameCapabilities"/> declare, wired to
    /// an <see cref="IAliasNameStoreRegistry"/>) and, when enabled, one
    /// <c>AliasNameType</c> instance per alias carrying the
    /// <c>AliasFor</c> references to its targets.
    /// </summary>
    /// <remarks>
    /// The walk is idempotent and DAG-safe: a category reached twice (a
    /// shared sub-category descriptor, or a repeat call after another
    /// store registered) only gains any missing parent linkage, and alias
    /// nodes registered by an earlier pass are reused. Server-defined
    /// BrowseNames a descriptor left in namespace 0 — reserved by Part 3
    /// for OPC-Foundation-defined names — are re-homed into the host's
    /// materialization namespace; Part 17 §6.2 clients compare alias names
    /// ignoring the namespace, so this is transparent to them.
    /// </remarks>
    internal sealed class AliasNameNodeMaterializer
    {
        /// <summary>
        /// Initializes the materializer for one host manager.
        /// </summary>
        /// <param name="host">The owning node manager's capabilities.</param>
        /// <param name="dispatchRegistry">The registry every created
        /// method handler dispatches through — the server-wide registry
        /// for the standard nodes, or a local single-store registry for a
        /// standalone manager.</param>
        /// <param name="authorizeMutations">Authorization gate consulted
        /// by <c>AddAliasesToCategory</c>/<c>DeleteAliasesFromCategory</c>
        /// handlers; a denied call returns <c>BadUserAccessDenied</c>.</param>
        /// <param name="logger">Log sink for skip/collision warnings.</param>
        public AliasNameNodeMaterializer(
            IAliasNameMaterializerHost host,
            IAliasNameStoreRegistry dispatchRegistry,
            Func<ISystemContext, bool> authorizeMutations,
            ILogger logger)
        {
            m_host = host ?? throw new ArgumentNullException(nameof(host));
            m_dispatchRegistry = dispatchRegistry ??
                throw new ArgumentNullException(nameof(dispatchRegistry));
            m_authorizeMutations = authorizeMutations ??
                throw new ArgumentNullException(nameof(authorizeMutations));
            m_logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Materializes one store's root categories, their capability
        /// children and — when <paramref name="materializeAliasNodes"/> is
        /// set — their alias instance nodes.
        /// </summary>
        /// <remarks>
        /// A timed-out initial snapshot read is retried once before materializing its root.
        /// Repeated timeouts, other errors, and cancellation still abort materialization.
        /// </remarks>
        /// <param name="store">The store whose descriptor tree is walked;
        /// aliases are read through one recursive
        /// <c>FindAliasVerbose</c> query per root.</param>
        /// <param name="externalReferences">The dictionary supplied to
        /// <c>CreateAddressSpaceAsync</c>, used for references into other
        /// node managers.</param>
        /// <param name="created">Receives every node this call created.</param>
        /// <param name="visited">Category ids already handled — pass one
        /// set across stores (and across repeat calls within one pass) so
        /// shared categories materialize once.</param>
        /// <param name="materializeAliasNodes">When set, one
        /// <c>AliasNameType</c> node is created per alias (Part 17 §6.2
        /// browse discovery); otherwise only the category tree is built.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        public async ValueTask MaterializeStoreAsync(
            IAliasNameStore store,
            IDictionary<NodeId, IList<IReference>> externalReferences,
            List<NodeState> created,
            HashSet<NodeId> visited,
            bool materializeAliasNodes,
            CancellationToken cancellationToken)
        {
            foreach (AliasNameCategoryDescriptor root in store.RootCategories)
            {
                // One recursive query per root — every alias in the
                // subtree comes back tagged with its home category, so
                // per-category re-queries (each of which would scan the
                // whole subtree again) are unnecessary. The query is
                // restricted to AliasFor and its subtypes because that is
                // the reference the materialized nodes carry; entries a
                // store holds under unrelated reference types are not
                // Part 17 §6.2 alias associations and are served by
                // FindAlias only.
                Dictionary<NodeId, List<AliasNameVerboseDataType>>? aliasesByCategory = null;
                if (materializeAliasNodes)
                {
                    try
                    {
                        aliasesByCategory = await QueryAliasesAsync(store, root.NodeId, cancellationToken)
                            .ConfigureAwait(false);
                    }
                    catch (ServiceResultException ex) when (
                        ex.StatusCode == StatusCodes.BadTimeout && !cancellationToken.IsCancellationRequested)
                    {
                        m_logger.RetryingInitialAliasSnapshotAfterTimeout(root.NodeId, ex);
                        aliasesByCategory = await QueryAliasesAsync(store, root.NodeId, cancellationToken)
                            .ConfigureAwait(false);
                    }
                }

                await MaterializeCategoryAsync(
                    root,
                    parent: null,
                    aliasesByCategory,
                    visited,
                    externalReferences,
                    created,
                    cancellationToken).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Queries a category subtree before reconciling its alias nodes and live inverse references.
        /// </summary>
        public async ValueTask RefreshCategoryAsync(
            IAliasNameStore store,
            NodeId categoryId,
            bool materializeAliasNodes,
            Func<bool>? isCurrentGeneration = null,
            CancellationToken cancellationToken = default)
        {
            AliasNameCategoryState? category = m_host.FindCategoryNode(categoryId);
            if (!materializeAliasNodes || category == null)
            {
                return;
            }

            Dictionary<NodeId, List<AliasNameVerboseDataType>> aliases = await QueryAliasesAsync(
                store, categoryId, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (isCurrentGeneration != null && !isCurrentGeneration())
            {
                return;
            }

            var pending = new Queue<AliasNameCategoryState>();
            var visited = new HashSet<NodeId>();
            pending.Enqueue(category);
            while (pending.Count != 0)
            {
                category = pending.Dequeue();
                if (!visited.Add(category.NodeId))
                {
                    continue;
                }
                cancellationToken.ThrowIfCancellationRequested();
                var existing = new Dictionary<string, AliasNameState>(StringComparer.Ordinal);
                var references = new List<IReference>();
                category.GetReferences(m_host.SystemContext, references);
                foreach (IReference reference in references)
                {
                    if (reference.IsInverse ||
                        reference.ReferenceTypeId != ReferenceTypeIds.Organizes ||
                        !m_host.TryGetNode(ExpandedNodeId.ToNodeId(reference.TargetId, m_host.NamespaceUris),
                            out NodeState? node))
                    {
                        continue;
                    }
                    if (node is AliasNameCategoryState child && store.OwnsCategory(child.NodeId))
                    {
                        pending.Enqueue(child);
                    }
                    else if (node is AliasNameState alias && !string.IsNullOrEmpty(alias.BrowseName.Name))
                    {
                        existing.TryAdd(alias.BrowseName.Name!, alias);
                    }
                }

                var desired = new Dictionary<string, (QualifiedName Name, HashSet<ExpandedNodeId> Targets)>(
                    StringComparer.Ordinal);
                if (aliases.TryGetValue(category.NodeId, out List<AliasNameVerboseDataType>? bucket))
                {
                    foreach (AliasNameVerboseDataType alias in bucket)
                    {
                        string? name = alias.AliasName.Name;
                        if (string.IsNullOrEmpty(name))
                        {
                            continue;
                        }
                        if (!desired.TryGetValue(name!, out (QualifiedName Name, HashSet<ExpandedNodeId> Targets) item))
                        {
                            item = (alias.AliasName, []);
                            desired.Add(name!, item);
                        }
                        for (int ii = 0; ii < alias.ReferencedNodes.Count; ii++)
                        {
                            ExpandedNodeId target = ResolveAliasTarget(alias.ReferencedNodes[ii],
                                ii < alias.ServerUris.Count ? alias.ServerUris[ii] : null);
                            if (!target.IsNull)
                            {
                                item.Targets.Add(target);
                            }
                        }
                    }
                }

                foreach (KeyValuePair<string, (QualifiedName Name, HashSet<ExpandedNodeId> Targets)> item in desired)
                {
                    AliasNameState alias = existing.TryGetValue(item.Key, out AliasNameState? current)
                        ? current
                        : await GetOrCreateAliasNodeAsync(
                            category.NodeId, category, item.Value.Name, item.Key, [], cancellationToken)
                            .ConfigureAwait(false);
                    await ReconcileTargetsAsync(alias, item.Value.Targets, cancellationToken).ConfigureAwait(false);
                }
                foreach (KeyValuePair<string, AliasNameState> item in existing)
                {
                    if (desired.ContainsKey(item.Key))
                    {
                        continue;
                    }
                    await ReconcileTargetsAsync(item.Value, [], cancellationToken).ConfigureAwait(false);
                    await m_host.RemoveNodeAsync(item.Value.NodeId, cancellationToken).ConfigureAwait(false);
                    category.RemoveReference(ReferenceTypeIds.Organizes, false, item.Value.NodeId);
                }
                category.ClearChangeMasks(m_host.SystemContext, false);
            }
        }

        private async ValueTask<Dictionary<NodeId, List<AliasNameVerboseDataType>>> QueryAliasesAsync(
            IAliasNameStore store,
            NodeId categoryId,
            CancellationToken cancellationToken)
        {
            IReadOnlyList<AliasNameVerboseDataType> aliases = await store.FindAliasVerboseAsync(
                categoryId, AllAliasesPattern, ReferenceTypeIds.AliasFor, m_host.TypeTree, cancellationToken)
                .ConfigureAwait(false) ??
                throw ServiceResultException.Unexpected("The alias store returned a null snapshot.");
            var result = new Dictionary<NodeId, List<AliasNameVerboseDataType>>();
            foreach (AliasNameVerboseDataType alias in aliases)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!result.TryGetValue(alias.AliasNameCategoryId, out List<AliasNameVerboseDataType>? bucket))
                {
                    bucket = [];
                    result.Add(alias.AliasNameCategoryId, bucket);
                }
                bucket.Add(alias);
            }
            return result;
        }

        private async ValueTask ReconcileTargetsAsync(
            AliasNameState alias,
            HashSet<ExpandedNodeId> desired,
            CancellationToken cancellationToken)
        {
            var references = new List<IReference>();
            alias.GetReferences(m_host.SystemContext, references);
            foreach (IReference reference in references)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (reference.IsInverse ||
                    reference.ReferenceTypeId != ReferenceTypeIds.AliasFor ||
                    desired.Contains(reference.TargetId))
                {
                    continue;
                }
                await UpdateInverseAsync(reference.TargetId, alias.NodeId, remove: true, cancellationToken)
                    .ConfigureAwait(false);
                alias.RemoveReference(ReferenceTypeIds.AliasFor, false, reference.TargetId);
            }
            foreach (ExpandedNodeId target in desired)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!alias.ReferenceExists(ReferenceTypeIds.AliasFor, false, target))
                {
                    await UpdateInverseAsync(target, alias.NodeId, remove: false, cancellationToken)
                        .ConfigureAwait(false);
                    alias.AddReference(ReferenceTypeIds.AliasFor, false, target);
                }
            }
            alias.ClearChangeMasks(m_host.SystemContext, false);
        }

        private ValueTask UpdateInverseAsync(
            ExpandedNodeId target,
            NodeId aliasNodeId,
            bool remove,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (target.ServerIndex != 0)
            {
                return default;
            }
            var localTarget = ExpandedNodeId.ToNodeId(target, m_host.NamespaceUris);
            return localTarget.IsNull
                ? default
                : m_host.UpdateInverseAliasReferenceAsync(localTarget, aliasNodeId, remove, cancellationToken);
        }

        internal static async ValueTask UpdateInverseAliasReferenceAsync(
            IServerInternal server,
            ISystemContext context,
            NodeState? target,
            NodeId targetId,
            NodeId aliasNodeId,
            bool remove,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (target != null)
            {
                if (remove)
                {
                    target.RemoveReference(ReferenceTypeIds.AliasFor, true, aliasNodeId);
                }
                else if (!target.ReferenceExists(ReferenceTypeIds.AliasFor, true, aliasNodeId))
                {
                    target.AddReference(ReferenceTypeIds.AliasFor, true, aliasNodeId);
                }
                target.ClearChangeMasks(context, false);
            }
            else if (remove)
            {
                await server.NodeManager.DeleteReferencesAsync(aliasNodeId,
                    [new ReferenceNode { ReferenceTypeId = ReferenceTypeIds.AliasFor, TargetId = targetId }],
                    cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await server.NodeManager.AddReferencesAsync(targetId,
                    [new NodeStateReference(ReferenceTypeIds.AliasFor, true, aliasNodeId)], cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Materializes one category — reusing the host's predefined node
        /// when one exists — then its aliases and sub-categories.
        /// </summary>
        private async ValueTask MaterializeCategoryAsync(
            AliasNameCategoryDescriptor descriptor,
            AliasNameCategoryState? parent,
            IReadOnlyDictionary<NodeId, List<AliasNameVerboseDataType>>? aliasesByCategory,
            HashSet<NodeId> visited,
            IDictionary<NodeId, IList<IReference>> externalReferences,
            List<NodeState> created,
            CancellationToken cancellationToken)
        {
            AliasNameCategoryState? category = m_host.FindCategoryNode(descriptor.NodeId);

            if (!visited.Add(descriptor.NodeId))
            {
                // Reached a second time — a descriptor shared by two
                // parents, or a repeat materialization call. The node and
                // its aliases already exist; only this parent's linkage
                // can be missing.
                if (category != null && parent != null)
                {
                    LinkOrganizes(parent, category);
                }
                return;
            }

            bool isNewCategory = category == null;

            if (category == null)
            {
                bool creatable;
                if (descriptor.NodeId.NamespaceIndex != m_host.MaterializationNamespaceIndex)
                {
                    // Only nodes in the host's own namespace may be minted
                    // here. Anything else is either a namespace owned by
                    // another node manager or a mis-seeded well-known id —
                    // e.g. a ns=0 descriptor pointing at a node that is
                    // not an alias category, which creating would silently
                    // replace.
                    m_logger.SkippedAliasCategoryOutsideNamespace(descriptor.NodeId);
                    creatable = false;
                }
                else if (m_host.TryGetNode(descriptor.NodeId, out NodeState? occupant))
                {
                    // The id exists but is not an AliasNameCategoryState —
                    // creating the category would replace the occupant.
                    m_logger.SkippedAliasCategoryOccupiedNodeId(
                        descriptor.NodeId, occupant?.GetType().Name ?? "unknown");
                    creatable = false;
                }
                else
                {
                    creatable = true;
                }

                if (!creatable)
                {
                    // The category itself cannot be materialized, but its
                    // sub-tree may still hold categories the host owns —
                    // do not swallow them. With no parent node to hang
                    // off, they link into the hierarchy via the host's
                    // root linkage.
                    foreach (AliasNameCategoryDescriptor child in descriptor.SubCategories)
                    {
                        await MaterializeCategoryAsync(
                            child,
                            parent: null,
                            aliasesByCategory,
                            visited,
                            externalReferences,
                            created,
                            cancellationToken).ConfigureAwait(false);
                    }
                    return;
                }

                category = m_host.SystemContext.CreateInstanceOfAliasNameCategoryType();
                category.BrowseName = RehomeServerDefinedName(descriptor.BrowseName);
                category.DisplayName = new LocalizedText(descriptor.BrowseName.Name);
                category.SymbolicName = descriptor.BrowseName.Name!;
                category.TypeDefinitionId = ObjectTypeIds.AliasNameCategoryType;
                category.ReferenceTypeId = ReferenceTypeIds.Organizes;

                // Mint ids for the mandatory children first, then pin the
                // category to the descriptor's NodeId, which is the key the
                // store and the registry dispatch on. Assigning it before the
                // children would derive their ids from it, so the order is
                // deliberate rather than incidental.
                category.AssignNodeIds(m_host.SystemContext, []);
                category.NodeId = descriptor.NodeId;
            }

            // The mandatory FindAlias is wired for every materialized
            // category — including one that pre-existed as a predefined
            // node (from a companion NodeSet) without a handler; without
            // this a client would see the optional methods succeed while
            // the mandatory one returns BadNotImplemented.
            WireFindAlias(category, descriptor.NodeId, m_dispatchRegistry, m_host.TypeTree);

            // Add and wire the optional Part 17 children the store's
            // descriptor declares. For a category created above this only
            // grows the subtree before it is registered; for a
            // pre-existing category the new children are registered
            // individually.
            await ApplyCategoryCapabilitiesAsync(
                descriptor,
                category,
                registerNewChildren: !isNewCategory,
                created,
                cancellationToken).ConfigureAwait(false);

            if (isNewCategory)
            {
                // A root category from the store has no parent in the
                // descriptor tree — the host links it into the browse
                // hierarchy (typically under the standard Aliases
                // (i=23470) object). Without an incoming hierarchical
                // reference the node would exist but be reachable only by
                // NodeId, defeating the Part 17 §6.2 browse-discovery
                // purpose of materialization.
                if (parent != null)
                {
                    LinkOrganizes(parent, category);
                }
                else
                {
                    m_host.LinkRootCategory(category, externalReferences);
                }

                await m_host.RegisterNodeAsync(category, cancellationToken)
                    .ConfigureAwait(false);
                created.Add(category);
            }
            else if (parent != null)
            {
                LinkOrganizes(parent, category);
            }

            if (aliasesByCategory != null)
            {
                await MaterializeAliasesAsync(
                    descriptor,
                    category,
                    aliasesByCategory.TryGetValue(
                        descriptor.NodeId, out List<AliasNameVerboseDataType>? bucket)
                        ? bucket
                        : null,
                    externalReferences,
                    created,
                    cancellationToken).ConfigureAwait(false);
            }

            foreach (AliasNameCategoryDescriptor child in descriptor.SubCategories)
            {
                await MaterializeCategoryAsync(
                    child,
                    category,
                    aliasesByCategory,
                    visited,
                    externalReferences,
                    created,
                    cancellationToken).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Instantiates and wires the optional Part 17 children a category
        /// declares through <see cref="AliasNameCategoryDescriptor.Capabilities"/>:
        /// <c>FindAliasVerbose</c> (§6.3.3), <c>AddAliasesToCategory</c>
        /// (§6.3.4), <c>DeleteAliasesFromCategory</c> (§6.3.5) and
        /// <c>LastChange</c> (§6.3.1). Each child is created AND wired only
        /// under its capability flag: a child that already exists on the
        /// node (a companion NodeSet may ship one) but that the descriptor
        /// did not declare stays unwired — otherwise a category declared
        /// read-only would silently accept mutations. Children on the
        /// well-known standard categories are created at the NodeIds
        /// Part 17 §9 reserves for them
        /// (<see cref="AliasNameReservedChildIds"/>).
        /// </summary>
        private async ValueTask ApplyCategoryCapabilitiesAsync(
            AliasNameCategoryDescriptor descriptor,
            AliasNameCategoryState category,
            bool registerNewChildren,
            List<NodeState> created,
            CancellationToken cancellationToken)
        {
            ISystemContext context = m_host.SystemContext;
            AliasNameCapabilities capabilities = descriptor.Capabilities;
            var reserved =
                AliasNameReservedChildIds.For(descriptor.NodeId);
            NodeId categoryId = descriptor.NodeId;
            IAliasNameStoreRegistry registry = m_dispatchRegistry;
            ITypeTable typeTree = m_host.TypeTree;
            Func<ISystemContext, bool> authorize = m_authorizeMutations;
            var addedChildren = new List<NodeState>();

            if ((capabilities & AliasNameCapabilities.FindAliasVerbose) != 0)
            {
                if (category.FindAliasVerbose == null)
                {
                    category.AddFindAliasVerbose(context, reserved.FindAliasVerbose.Method);
                    ApplyReservedArgumentIds(
                        category.FindAliasVerbose, reserved.FindAliasVerbose);
                    addedChildren.Add(category.FindAliasVerbose!);
                }

                category.FindAliasVerbose!.OnCallAsync =
                    (ctx, method, objId, pattern, refType, ct) =>
                        AliasNameMethodDispatcher.FindAliasVerboseAsync(
                            registry,
                            typeTree,
                            objId.IsNull ? categoryId : objId,
                            pattern,
                            refType,
                            ct);
            }

            if ((capabilities & AliasNameCapabilities.AddAliasesToCategory) != 0)
            {
                if (category.AddAliasesToCategory == null)
                {
                    category.AddAddAliasesToCategory(
                        context, reserved.AddAliasesToCategory.Method);
                    ApplyReservedArgumentIds(
                        category.AddAliasesToCategory, reserved.AddAliasesToCategory);
                    addedChildren.Add(category.AddAliasesToCategory!);
                }

                category.AddAliasesToCategory!.OnCallAsync =
                    (ctx, method, objId, names, targets, servers, refType, ct) =>
                        !authorize(ctx)
                            ? new ValueTask<AddAliasesToCategoryMethodStateResult>(
                                new AddAliasesToCategoryMethodStateResult
                                {
                                    ServiceResult = new ServiceResult(StatusCodes.BadUserAccessDenied),
                                    ErrorCodes = default
                                })
                            : AliasNameMethodDispatcher.AddAliasesAsync(
                                registry,
                                objId.IsNull ? categoryId : objId,
                                names,
                                targets,
                                servers,
                                refType,
                                ct);
            }

            if ((capabilities & AliasNameCapabilities.DeleteAliasesFromCategory) != 0)
            {
                if (category.DeleteAliasesFromCategory == null)
                {
                    category.AddDeleteAliasesFromCategory(
                        context, reserved.DeleteAliasesFromCategory.Method);
                    ApplyReservedArgumentIds(
                        category.DeleteAliasesFromCategory, reserved.DeleteAliasesFromCategory);
                    addedChildren.Add(category.DeleteAliasesFromCategory!);
                }

                category.DeleteAliasesFromCategory!.OnCallAsync =
                    (ctx, method, objId, names, targets, ct) =>
                        !authorize(ctx)
                            ? new ValueTask<DeleteAliasesFromCategoryMethodStateResult>(
                                new DeleteAliasesFromCategoryMethodStateResult
                                {
                                    ServiceResult = new ServiceResult(StatusCodes.BadUserAccessDenied),
                                    ErrorCodes = default
                                })
                            : AliasNameMethodDispatcher.DeleteAliasesAsync(
                                registry,
                                objId.IsNull ? categoryId : objId,
                                names,
                                targets,
                                ct);
            }

            if ((capabilities & AliasNameCapabilities.LastChange) != 0)
            {
                if (category.LastChange == null)
                {
                    category.AddLastChange(context, reserved.LastChange);
                    addedChildren.Add(category.LastChange!);
                }

                SeedLastChange(
                    category, categoryId, registry, context, m_host.OnLastChangeBound);
            }

            // Every added child is reported to the caller; only children
            // of an already-registered category also need individual
            // registration — a category not yet registered covers its
            // whole subtree when it is.
            created.AddRange(addedChildren);
            if (registerNewChildren)
            {
                foreach (NodeState child in addedChildren)
                {
                    await m_host.RegisterNodeAsync(child, cancellationToken)
                        .ConfigureAwait(false);
                }
            }
        }

        /// <summary>
        /// Points a category's mandatory <c>FindAlias</c> method at the
        /// supplied registry. Shared by the standard well-known categories
        /// (wired at startup by <c>DiagnosticsNodeManager</c>) and by the
        /// categories materialized from a store's descriptor tree.
        /// </summary>
        internal static void WireFindAlias(
            AliasNameCategoryState category,
            NodeId categoryId,
            IAliasNameStoreRegistry registry,
            ITypeTable typeTree)
        {
            category.FindAlias?.OnCallAsync = (ctx, method, objId, pattern, refType, ct) =>
                    AliasNameMethodDispatcher.FindAliasAsync(
                        registry,
                        typeTree,
                        objId.IsNull ? categoryId : objId,
                        pattern,
                        refType,
                        ct);
        }

        /// <summary>
        /// Seeds a category's <c>LastChange</c> property from the store
        /// that owns it and hands the node to
        /// <paramref name="onBound"/> so the caller can keep it current.
        /// No-op for a category without the property. The registry's
        /// ownership map is authoritative — asking every store in turn
        /// would seed from whichever store happens to answer first for a
        /// category it does not own.
        /// </summary>
        internal static void SeedLastChange(
            AliasNameCategoryState category,
            NodeId categoryId,
            IAliasNameStoreRegistry registry,
            ISystemContext context,
            Action<NodeId, PropertyState<uint>>? onBound)
        {
            if (category.LastChange == null)
            {
                return;
            }

            if (onBound != null)
            {
                onBound(categoryId, category.LastChange);
                return;
            }
            uint? seed = registry.GetStoreForCategory(categoryId)?.GetLastChange(categoryId);
            if (seed.HasValue)
            {
                category.LastChange.Value = seed.Value;
                category.LastChange.ClearChangeMasks(context, false);
            }
        }

        /// <summary>
        /// Adds the forward and inverse <c>Organizes</c> pair between two
        /// category nodes, tolerating either half already being present —
        /// <c>NodeState.AddReference</c> throws on duplicates.
        /// </summary>
        internal static void LinkOrganizes(
            AliasNameCategoryState parent,
            AliasNameCategoryState child)
        {
            if (!parent.ReferenceExists(ReferenceTypeIds.Organizes, false, child.NodeId))
            {
                parent.AddReference(ReferenceTypeIds.Organizes, false, child.NodeId);
            }
            if (!child.ReferenceExists(ReferenceTypeIds.Organizes, true, parent.NodeId))
            {
                child.AddReference(ReferenceTypeIds.Organizes, true, parent.NodeId);
            }
        }

        /// <summary>
        /// Pins a method's argument properties to their reserved NodeIds.
        /// The generated <c>Add…</c> helper rebases the arguments through
        /// the node-id factory when it is handed an explicit method NodeId,
        /// so they have to be re-pinned afterwards. A no-op when the
        /// category has no reserved allocation.
        /// </summary>
        private static void ApplyReservedArgumentIds(
            MethodState? method,
            AliasNameReservedMethodIds reserved)
        {
            if (method == null || reserved.Method.IsNull)
            {
                return;
            }
            method.InputArguments?.NodeId = reserved.InputArguments;
            method.OutputArguments?.NodeId = reserved.OutputArguments;
        }

        /// <summary>
        /// Creates one <c>AliasNameType</c> instance per alias whose home
        /// category is <paramref name="descriptor"/> — the bucket handed in
        /// holds exactly those. Aliases contributed by sub-categories are
        /// materialized under their own category.
        /// </summary>
        private async ValueTask MaterializeAliasesAsync(
            AliasNameCategoryDescriptor descriptor,
            AliasNameCategoryState category,
            List<AliasNameVerboseDataType>? aliases,
            IDictionary<NodeId, IList<IReference>> externalReferences,
            List<NodeState> created,
            CancellationToken cancellationToken)
        {
            if (aliases == null || aliases.Count == 0)
            {
                return;
            }

            // One node per alias name; the store reports a separate record
            // per reference type, and a name may carry several targets.
            // Duplicate targets — including the same node seeded once in
            // namespace-URI form and once in index form — are absorbed by
            // the ReferenceExists guards in AddAliasForReference.
            var byName = new Dictionary<string, AliasNameState>();

            foreach (AliasNameVerboseDataType alias in aliases)
            {
                if (string.IsNullOrEmpty(alias.AliasName.Name))
                {
                    continue;
                }

                string name = alias.AliasName.Name!;
                if (!byName.TryGetValue(name, out AliasNameState? aliasNode))
                {
                    aliasNode = await GetOrCreateAliasNodeAsync(
                        descriptor.NodeId, category, alias.AliasName, name, created,
                        cancellationToken).ConfigureAwait(false);
                    byName[name] = aliasNode;
                }

                for (int ii = 0; ii < alias.ReferencedNodes.Count; ii++)
                {
                    AddAliasForReference(
                        aliasNode,
                        alias.ReferencedNodes[ii],
                        ii < alias.ServerUris.Count ? alias.ServerUris[ii] : null,
                        externalReferences);
                }
            }
        }

        /// <summary>
        /// Resolves or builds the <c>AliasNameType</c> instance for one
        /// alias. Per Part 17 §6.2 the BrowseName carries the alias name
        /// and the DisplayName repeats it with an empty locale.
        /// </summary>
        /// <remarks>
        /// The deterministic string id minted from category id + alias name
        /// is ambiguous — <c>"…s=A"</c> with alias <c>"B.C"</c> and
        /// <c>"…s=A.B"</c> with alias <c>"C"</c> concatenate identically —
        /// and a repeat materialization revisits ids it minted before. A
        /// node already registered under the minted id is therefore reused
        /// when it is the same alias, and a different occupant makes this
        /// alias fall back to a factory-minted id instead of silently
        /// replacing the occupant.
        /// </remarks>
        private async ValueTask<AliasNameState> GetOrCreateAliasNodeAsync(
            NodeId categoryId,
            AliasNameCategoryState category,
            QualifiedName aliasName,
            string name,
            List<NodeState> created,
            CancellationToken cancellationToken)
        {
            var mintedId = new NodeId(
                Utils.Format("{0}.{1}", categoryId, name),
                m_host.MaterializationNamespaceIndex);

            if (m_host.TryGetNode(mintedId, out NodeState? existing))
            {
                if (existing is AliasNameState existingAlias &&
                    existingAlias.BrowseName.Name == name)
                {
                    LinkAliasToCategory(category, existingAlias);
                    return existingAlias;
                }

                m_logger.AliasNameNodeIdCollision(mintedId, name);
                mintedId = NodeId.Null;
            }

            AliasNameState aliasNode =
                m_host.SystemContext.CreateInstanceOfAliasNameType();

            if (mintedId.IsNull)
            {
                // The no-argument helper does no instance assignment, so the
                // node still carries the standard AliasNameType NodeId. It has
                // no parent either, and a factory keeps an identifier on a node
                // that stands on its own - so without clearing it first the
                // alias would advertise the type's own NodeId.
                aliasNode.NodeId = NodeId.Null;
                aliasNode.NodeId = m_host.MintNodeId(aliasNode);
            }
            else
            {
                aliasNode.NodeId = mintedId;
            }
            aliasNode.BrowseName = RehomeServerDefinedName(aliasName);
            aliasNode.DisplayName = new LocalizedText(string.Empty, name);
            aliasNode.SymbolicName = name;
            aliasNode.TypeDefinitionId = ObjectTypeIds.AliasNameType;
            aliasNode.ReferenceTypeId = ReferenceTypeIds.Organizes;

            LinkAliasToCategory(category, aliasNode);

            await m_host.RegisterNodeAsync(aliasNode, cancellationToken)
                .ConfigureAwait(false);
            created.Add(aliasNode);
            return aliasNode;
        }

        /// <summary>
        /// Adds the forward and inverse <c>Organizes</c> pair between a
        /// category and an alias node, tolerating either half already
        /// being present.
        /// </summary>
        private static void LinkAliasToCategory(
            AliasNameCategoryState category,
            AliasNameState aliasNode)
        {
            if (!category.ReferenceExists(ReferenceTypeIds.Organizes, false, aliasNode.NodeId))
            {
                category.AddReference(ReferenceTypeIds.Organizes, false, aliasNode.NodeId);
            }
            if (!aliasNode.ReferenceExists(ReferenceTypeIds.Organizes, true, category.NodeId))
            {
                aliasNode.AddReference(ReferenceTypeIds.Organizes, true, category.NodeId);
            }
        }

        /// <summary>
        /// Adds the forward <c>AliasFor</c> reference to a target and — for
        /// targets on this server — the inverse <c>HasAlias</c> reference
        /// back through the host.
        /// </summary>
        private void AddAliasForReference(
            AliasNameState aliasNode,
            ExpandedNodeId target,
            string? serverUri,
            IDictionary<NodeId, IList<IReference>> externalReferences)
        {
            ExpandedNodeId resolved = ResolveAliasTarget(target, serverUri);
            if (resolved.IsNull || aliasNode.ReferenceExists(ReferenceTypeIds.AliasFor, false, resolved))
            {
                return;
            }
            aliasNode.AddReference(ReferenceTypeIds.AliasFor, false, resolved);
            if (resolved.ServerIndex != 0 || !string.IsNullOrEmpty(serverUri))
            {
                return;
            }
            var localTarget = ExpandedNodeId.ToNodeId(resolved, m_host.NamespaceUris);
            if (!localTarget.IsNull)
            {
                m_host.AddInverseAliasReference(localTarget, aliasNode.NodeId, externalReferences);
            }
        }

        private ExpandedNodeId ResolveAliasTarget(ExpandedNodeId target, string? serverUri)
        {
            if (target.IsNull)
            {
                return target;
            }
            if (!string.IsNullOrEmpty(serverUri))
            {
                return target.WithServerIndex(m_host.ServerUris.GetIndexOrAppend(serverUri!));
            }
            if (target.ServerIndex != 0)
            {
                return target;
            }
            var localTarget = ExpandedNodeId.ToNodeId(target, m_host.NamespaceUris);
            return localTarget.IsNull ? target : new ExpandedNodeId(localTarget);
        }

        /// <summary>
        /// Moves a server-defined name out of namespace 0, which Part 3
        /// reserves for OPC-Foundation-defined names. Store descriptors
        /// commonly inherit namespace 0 from the well-known categories'
        /// BrowseNames; publishing server-specific aliases there is a
        /// conformance violation and can even duplicate a standard child
        /// name (an alias literally called "FindAlias"). Part 17 §6.2
        /// clients compare alias names ignoring the namespace, so
        /// re-homing is transparent to them.
        /// </summary>
        private QualifiedName RehomeServerDefinedName(QualifiedName name)
        {
            return name.NamespaceIndex == 0 && !string.IsNullOrEmpty(name.Name)
                ? new QualifiedName(name.Name, m_host.MaterializationNamespaceIndex)
                : name;
        }

        private const string AllAliasesPattern = "%";
        private readonly IAliasNameMaterializerHost m_host;
        private readonly IAliasNameStoreRegistry m_dispatchRegistry;
        private readonly Func<ISystemContext, bool> m_authorizeMutations;
        private readonly ILogger m_logger;
    }

    /// <summary>
    /// The reserved NodeIds of one optional method and its two argument
    /// properties. <see cref="Method"/> is <see cref="NodeId.Null"/> for
    /// a category with no reserved allocation, which makes the generated
    /// <c>Add…</c> helper fall back to the node-id factory.
    /// </summary>
    internal readonly record struct AliasNameReservedMethodIds(
        NodeId Method,
        NodeId InputArguments,
        NodeId OutputArguments);

    /// <summary>
    /// The NodeIds the OPC Foundation reserves for the optional Part 17
    /// children of the three well-known categories.
    /// </summary>
    /// <remarks>
    /// <para>
    /// These identifiers are allocated in the standard identifier
    /// registry — <c>StandardTypes.csv</c>, rows <c>Aliases_FindAliasVerbose,24054</c>
    /// through <c>Topics_DeleteAliasesFromCategory_OutputArguments,24080</c>,
    /// plus <c>TagVariables_LastChange,32854</c> and
    /// <c>Topics_LastChange,32856</c> — so a server that instantiates
    /// one of these optional children has a canonical NodeId for it.
    /// </para>
    /// <para>
    /// They are deliberately absent from both the ModelDesign and the
    /// published NodeSet: the standard address space does not
    /// instantiate optional children, so the source generator has no
    /// parent-to-instance mapping to emit and no <c>MethodIds</c>
    /// constant exists for them. The argument properties do have
    /// generated <c>VariableIds</c> constants and are referenced through
    /// those; only the nine method identifiers and the two
    /// <c>LastChange</c> identifiers are spelled out here, each
    /// traceable to its registry row.
    /// </para>
    /// </remarks>
    internal readonly record struct AliasNameReservedChildIds(
        AliasNameReservedMethodIds FindAliasVerbose,
        AliasNameReservedMethodIds AddAliasesToCategory,
        AliasNameReservedMethodIds DeleteAliasesFromCategory,
        NodeId LastChange)
    {
        /// <summary>
        /// Returns the reserved child ids for a well-known category, or a
        /// default (all-null) set for any other category id.
        /// </summary>
        public static AliasNameReservedChildIds For(NodeId categoryId)
        {
            return s_reserved.TryGetValue(categoryId, out AliasNameReservedChildIds ids)
                ? ids
                : default;
        }

        /// <summary>
        /// One row per well-known category; the numeric literals are the
        /// Method rows of StandardTypes.csv, the VariableIds constants
        /// its Variable rows.
        /// </summary>
        private static readonly Dictionary<NodeId, AliasNameReservedChildIds> s_reserved = new()
        {
            [ObjectIds.Aliases] = new AliasNameReservedChildIds(
                new AliasNameReservedMethodIds(
                    new NodeId(24054u),
                    VariableIds.Aliases_FindAliasVerbose_InputArguments,
                    VariableIds.Aliases_FindAliasVerbose_OutputArguments),
                new AliasNameReservedMethodIds(
                    new NodeId(24057u),
                    VariableIds.Aliases_AddAliasesToCategory_InputArguments,
                    VariableIds.Aliases_AddAliasesToCategory_OutputArguments),
                new AliasNameReservedMethodIds(
                    new NodeId(24060u),
                    VariableIds.Aliases_DeleteAliasesFromCategory_InputArguments,
                    VariableIds.Aliases_DeleteAliasesFromCategory_OutputArguments),
                VariableIds.Aliases_LastChange),
            [ObjectIds.TagVariables] = new AliasNameReservedChildIds(
                new AliasNameReservedMethodIds(
                    new NodeId(24063u),
                    VariableIds.TagVariables_FindAliasVerbose_InputArguments,
                    VariableIds.TagVariables_FindAliasVerbose_OutputArguments),
                new AliasNameReservedMethodIds(
                    new NodeId(24066u),
                    VariableIds.TagVariables_AddAliasesToCategory_InputArguments,
                    VariableIds.TagVariables_AddAliasesToCategory_OutputArguments),
                new AliasNameReservedMethodIds(
                    new NodeId(24069u),
                    VariableIds.TagVariables_DeleteAliasesFromCategory_InputArguments,
                    VariableIds.TagVariables_DeleteAliasesFromCategory_OutputArguments),
                new NodeId(32854u)),
            [ObjectIds.Topics] = new AliasNameReservedChildIds(
                new AliasNameReservedMethodIds(
                    new NodeId(24072u),
                    VariableIds.Topics_FindAliasVerbose_InputArguments,
                    VariableIds.Topics_FindAliasVerbose_OutputArguments),
                new AliasNameReservedMethodIds(
                    new NodeId(24075u),
                    VariableIds.Topics_AddAliasesToCategory_InputArguments,
                    VariableIds.Topics_AddAliasesToCategory_OutputArguments),
                new AliasNameReservedMethodIds(
                    new NodeId(24078u),
                    VariableIds.Topics_DeleteAliasesFromCategory_InputArguments,
                    VariableIds.Topics_DeleteAliasesFromCategory_OutputArguments),
                new NodeId(32856u))
        };
    }

    /// <summary>
    /// Source-generated log messages for the shared Part 17
    /// materializer.
    /// </summary>
    internal static partial class AliasNameNodeMaterializerLog
    {
        [LoggerMessage(EventId = ServerEventIds.AliasNameNodeManager + 1, Level = LogLevel.Warning,
            Message = "Skipped alias category {CategoryId}: its NodeId is outside the materialization namespace, so it belongs to another node manager or is a mis-seeded well-known id.")]
        public static partial void SkippedAliasCategoryOutsideNamespace(this ILogger logger, NodeId categoryId);

        [LoggerMessage(EventId = ServerEventIds.AliasNameNodeManager + 2, Level = LogLevel.Warning,
            Message = "Skipped alias category {CategoryId}: the NodeId is already registered by a node of type {OccupantType}.")]
        public static partial void SkippedAliasCategoryOccupiedNodeId(this ILogger logger, NodeId categoryId, string occupantType);

        [LoggerMessage(EventId = ServerEventIds.AliasNameNodeManager + 3, Level = LogLevel.Warning,
            Message = "Alias name NodeId {MintedId} for alias '{AliasName}' collides with an existing node; a factory-minted NodeId is used instead.")]
        public static partial void AliasNameNodeIdCollision(this ILogger logger, NodeId mintedId, string aliasName);

        [LoggerMessage(EventId = ServerEventIds.AliasNameNodeManager + 4, Level = LogLevel.Information,
            Message = "Materialized {Count} Part 17 alias name nodes from the alias store(s).")]
        public static partial void MaterializedAliasNameNodes(this ILogger logger, int count);

        [LoggerMessage(EventId = ServerEventIds.AliasNameNodeManager + 6, Level = LogLevel.Warning,
            Message = "Initial alias snapshot for category {CategoryId} timed out; retrying once before materialization.")]
        public static partial void RetryingInitialAliasSnapshotAfterTimeout(
            this ILogger logger, NodeId categoryId, Exception exception);
    }
}
