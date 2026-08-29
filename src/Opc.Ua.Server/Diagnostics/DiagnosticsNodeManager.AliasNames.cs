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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Server.AliasNames;

namespace Opc.Ua.Server
{
    /// <summary>
    /// OPC UA Part 17 (Alias Names) integration for the standard
    /// well-known nodes loaded by <c>DiagnosticsNodeManager</c> from
    /// <c>Opc.Ua.NodeSet2.xml</c>.
    /// </summary>
    /// <remarks>
    /// The standard NodeSet ships three <c>AliasNameCategoryType</c>
    /// instances under the Server object (Part 17 §9):
    /// <list type="bullet">
    ///   <item><description><c>Aliases (i=23470)</c> — with mandatory
    ///   <c>FindAlias (i=23476)</c> and a <c>LastChange (i=32852)</c>
    ///   property;</description></item>
    ///   <item><description><c>TagVariables (i=23479)</c> — with mandatory
    ///   <c>FindAlias (i=23485)</c>;</description></item>
    ///   <item><description><c>Topics (i=23488)</c> — with mandatory
    ///   <c>FindAlias (i=23494)</c>.</description></item>
    /// </list>
    /// None of the optional Part 17 children
    /// (<c>FindAliasVerbose</c>/<c>AddAliasesToCategory</c>/<c>DeleteAliasesFromCategory</c>)
    /// are instantiated by the standard NodeSet on these well-known nodes,
    /// so the always-on binder — <see cref="WireStandardAliasMethods"/> —
    /// wires <c>FindAlias</c> and (for <c>Aliases</c>) <c>LastChange</c>
    /// only. A server that wants the optional methods and browsable alias
    /// nodes calls <see cref="MaterializeRegisteredAliasNameNodesAsync"/>,
    /// which delegates to the shared
    /// <see cref="AliasNameNodeMaterializer"/> — the same walker
    /// <see cref="AliasNameNodeManager"/> uses for application-defined
    /// namespaces — for every category whose store descriptor declares the
    /// matching <see cref="AliasNameCapabilities"/>.
    /// </remarks>
    public partial class DiagnosticsNodeManager : IAliasNameMaterializerHost
    {
        /// <summary>
        /// Resolves the server's <see cref="IAliasNameStoreRegistry"/>
        /// (via the optional <see cref="IAliasNameStoreRegistryProvider"/>
        /// interface) and wires the standard well-known
        /// <c>Aliases</c>/<c>TagVariables</c>/<c>Topics</c> <c>FindAlias</c>
        /// methods (plus <c>Aliases.LastChange</c>) to dispatch through
        /// it. The wiring is "live" — the registry is queried at each
        /// call rather than snapshotted at load time — so stores
        /// registered after this point are also reachable.
        /// </summary>
        private void WireStandardAliasMethods()
        {
            IAliasNameStoreRegistry? registry =
                (Server as IAliasNameStoreRegistryProvider)?.AliasNameStoreRegistry;
            if (registry == null)
            {
                return;
            }

            m_aliasRegistry = registry;
            m_aliasRegistry.Changed += OnAliasRegistryChanged;

            WireStandardCategory(ObjectIds.Aliases, includeLastChange: true);
            WireStandardCategory(ObjectIds.TagVariables, includeLastChange: false);
            WireStandardCategory(ObjectIds.Topics, includeLastChange: false);
        }

        private void WireStandardCategory(NodeId categoryId, bool includeLastChange)
        {
            AliasNameCategoryState? category =
                FindPredefinedNode<AliasNameCategoryState>(categoryId);
            if (category == null)
            {
                return;
            }

            AliasNameNodeMaterializer.WireFindAlias(
                category, categoryId, m_aliasRegistry!, Server.TypeTree);

            if (includeLastChange)
            {
                AliasNameNodeMaterializer.SeedLastChange(
                    category,
                    categoryId,
                    m_aliasRegistry!,
                    SystemContext,
                    (id, lastChange) => m_lastChangeNodes[id] = lastChange);
            }
        }

        /// <summary>
        /// Materializes the alias hierarchy described by every registered
        /// <see cref="IAliasNameStore"/> as browsable address-space nodes:
        /// an <c>AliasNameType</c> (i=23455) instance per alias — carrying
        /// the <c>AliasFor</c> (i=23469) references to its targets — and an
        /// <c>AliasNameCategoryType</c> (i=23456) instance for every store
        /// category that the standard NodeSet does not already ship.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Opt-in: servers that only need the well-known <c>FindAlias</c>
        /// methods to answer from their store — the default wired by
        /// <see cref="WireStandardAliasMethods"/> — do not have to call
        /// this. Servers under conformance test do, because Part 17 §6.2
        /// clients discover aliases by browsing, not only by calling
        /// <c>FindAlias</c>.
        /// </para>
        /// <para>
        /// The materialized nodes are a snapshot taken at address-space
        /// creation time. Aliases added or removed through
        /// <c>AddAliasesToCategory</c>/<c>DeleteAliasesFromCategory</c>
        /// afterwards change what <c>FindAlias</c> returns and advance
        /// <c>LastChange</c>, but do not add or remove
        /// <c>AliasNameType</c> nodes — so a mutated category's browse view
        /// and its query results diverge until the next restart. This
        /// applies to the standard well-known categories too whenever their
        /// descriptor declares the mutation capabilities, because this
        /// method instantiates those methods for them.
        /// </para>
        /// <para>
        /// Call from an overridden <c>CreateAddressSpaceAsync</c> after
        /// <c>base.CreateAddressSpaceAsync</c> — the standard categories
        /// must already be loaded and the stores registered. The created
        /// nodes are registered as predefined nodes of this manager, which
        /// serves Browse and Call for them; they are also returned for
        /// inspection. The method is idempotent: a repeat call (after
        /// another store registered, say) only adds what is missing.
        /// </para>
        /// </remarks>
        /// <param name="externalReferences">The dictionary supplied to
        /// <c>CreateAddressSpaceAsync</c>; used to add the inverse
        /// <c>HasAlias</c> reference on target nodes owned by other node
        /// managers.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The nodes created by this call.</returns>
        protected async ValueTask<ArrayOf<NodeState>> MaterializeRegisteredAliasNameNodesAsync(
            IDictionary<NodeId, IList<IReference>> externalReferences,
            CancellationToken cancellationToken = default)
        {
            // Materialization requires the registry wiring performed by
            // WireStandardAliasMethods — without it the categories'
            // LastChange would never update and the standard FindAlias
            // methods would stay unwired, a half-functional Part 17
            // surface. That method ran from base.CreateAddressSpaceAsync,
            // so a null here means the server does not implement
            // IAliasNameStoreRegistryProvider at all.
            IAliasNameStoreRegistry? registry = m_aliasRegistry;
            if (registry == null)
            {
                return [];
            }

            var materializer = new AliasNameNodeMaterializer(
                this,
                registry,
                AliasNameMethodDispatcher.HasSecureAdminAccess,
                m_logger);

            var created = new List<NodeState>();
            var visited = new HashSet<NodeId>();

            foreach (IAliasNameStore store in registry.Stores)
            {
                await materializer.MaterializeStoreAsync(
                    store,
                    externalReferences,
                    created,
                    visited,
                    materializeAliasNodes: true,
                    cancellationToken).ConfigureAwait(false);
            }

            m_logger.MaterializedAliasNameNodes(created.Count);
            return created.ToArrayOf();
        }

        private void OnAliasRegistryChanged(object? sender, AliasStoreChangedEventArgs e)
        {
            // Mirror the store value onto whichever categories expose a
            // LastChange property: the standard Aliases (i=23470) node from
            // the shipped NodeSet, plus any category that declared the
            // capability and had one added during materialization.
            if (m_lastChangeNodes.TryGetValue(
                    e.CategoryId, out PropertyState<uint>? lastChange))
            {
                // The store raises Changed synchronously on the mutating
                // caller's thread; serialize concurrent mutations so two
                // clients updating the same category cannot interleave the
                // value write and the change-mask notification.
                lock (m_lastChangeSync)
                {
                    lastChange.Value = e.LastChange;
                    lastChange.ClearChangeMasks(SystemContext, false);
                }
            }
        }

        /// <summary>
        /// Detaches the <see cref="OnAliasRegistryChanged"/> handler.
        /// Invoked from <c>Dispose(bool)</c> so the registry — which
        /// outlives this manager when other node managers share the same
        /// server — does not hold a stale reference into the disposed
        /// <see cref="DiagnosticsNodeManager"/>.
        /// </summary>
        private void UnwireStandardAliasMethods()
        {
            IAliasNameStoreRegistry? registry = m_aliasRegistry;
            if (registry != null)
            {
                registry.Changed -= OnAliasRegistryChanged;
                m_aliasRegistry = null;
            }
            m_lastChangeNodes.Clear();

            // Detach the dispatch handlers too — the OnCallAsync closures
            // capture the registry, so a call racing disposal would
            // otherwise still dispatch into stores this manager no longer
            // tracks. Without a handler the method reports itself as not
            // implemented instead.
            foreach (NodeState node in PredefinedNodes.Values)
            {
                if (node is AliasNameCategoryState category)
                {
                    category.FindAlias?.OnCallAsync = null;
                    category.FindAliasVerbose?.OnCallAsync = null;
                    category.AddAliasesToCategory?.OnCallAsync = null;
                    category.DeleteAliasesFromCategory?.OnCallAsync = null;
                }
            }
        }

        ISystemContext IAliasNameMaterializerHost.SystemContext => SystemContext;

        ITypeTable IAliasNameMaterializerHost.TypeTree => Server.TypeTree;

        NamespaceTable IAliasNameMaterializerHost.NamespaceUris => Server.NamespaceUris;

        StringTable IAliasNameMaterializerHost.ServerUris => Server.ServerUris;

        ushort IAliasNameMaterializerHost.MaterializationNamespaceIndex => m_namespaceIndex;

        AliasNameCategoryState? IAliasNameMaterializerHost.FindCategoryNode(NodeId nodeId)
        {
            return FindPredefinedNode<AliasNameCategoryState>(nodeId);
        }

        bool IAliasNameMaterializerHost.TryGetNode(NodeId nodeId, out NodeState? node)
        {
            return PredefinedNodes.TryGetValue(nodeId, out node);
        }

        ValueTask IAliasNameMaterializerHost.RegisterNodeAsync(
            NodeState node, CancellationToken cancellationToken)
        {
            return AddPredefinedNodeAsync(SystemContext, node, cancellationToken);
        }

        NodeId IAliasNameMaterializerHost.MintNodeId(NodeState node)
        {
            return New(SystemContext, node);
        }

        void IAliasNameMaterializerHost.LinkRootCategory(
            AliasNameCategoryState root,
            IDictionary<NodeId, IList<IReference>> externalReferences)
        {
            // The standard Aliases (i=23470) object is one of this
            // manager's own predefined nodes, so the linkage is direct —
            // no external references needed. A root that IS the standard
            // Aliases node is the hierarchy root itself.
            if (root.NodeId == ObjectIds.Aliases)
            {
                return;
            }

            AliasNameCategoryState? aliasesRoot =
                FindPredefinedNode<AliasNameCategoryState>(ObjectIds.Aliases);
            if (aliasesRoot != null)
            {
                AliasNameNodeMaterializer.LinkOrganizes(aliasesRoot, root);
            }
        }

        void IAliasNameMaterializerHost.AddInverseAliasReference(
            NodeId targetId,
            NodeId aliasNodeId,
            IDictionary<NodeId, IList<IReference>> externalReferences)
        {
            AddExternalReference(
                targetId,
                ReferenceTypeIds.AliasFor,
                true,
                aliasNodeId,
                externalReferences);
        }

        void IAliasNameMaterializerHost.OnLastChangeBound(
            NodeId categoryId, PropertyState<uint> lastChange)
        {
            m_lastChangeNodes[categoryId] = lastChange;
        }

        private IAliasNameStoreRegistry? m_aliasRegistry;

        /// <summary>
        /// Serializes the <c>LastChange</c> node updates raised by store
        /// mutations, which arrive on arbitrary client-call threads.
        /// </summary>
        private readonly Lock m_lastChangeSync = new();

        /// <summary>
        /// The <c>LastChange</c> property of every category that exposes
        /// one, keyed by category NodeId. Concurrent because
        /// <see cref="OnAliasRegistryChanged"/> reads it on client-call
        /// threads while <see cref="UnwireStandardAliasMethods"/> may clear
        /// it during disposal.
        /// </summary>
        private readonly ConcurrentDictionary<NodeId, PropertyState<uint>> m_lastChangeNodes = [];
    }
}
