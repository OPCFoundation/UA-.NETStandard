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
using Microsoft.Extensions.Logging;
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
    /// only. A server that wants the optional methods calls
    /// <see cref="MaterializeRegisteredAliasNameNodesAsync"/>, which adds
    /// them through the generated optional-child helpers — at the reserved
    /// NodeIds, see <see cref="ReservedChildIds"/> — for every category
    /// whose store descriptor declares the matching
    /// <see cref="AliasNameCapabilities"/>.
    /// </remarks>
    public partial class DiagnosticsNodeManager
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

            WireFindAlias(categoryId, category, m_aliasRegistry!);

            if (includeLastChange)
            {
                BindLastChange(categoryId, category, m_aliasRegistry!);
            }
        }

        /// <summary>
        /// Seeds a category's <c>LastChange</c> property from the store that
        /// owns it and remembers the node so
        /// <see cref="OnAliasRegistryChanged"/> can keep it current. No-op
        /// for a category that does not expose the property.
        /// </summary>
        private void BindLastChange(
            NodeId categoryId,
            AliasNameCategoryState category,
            IAliasNameStoreRegistry registry)
        {
            if (category.LastChange == null)
            {
                return;
            }

            // The registry's ownership map is authoritative — asking every
            // store in turn would seed from whichever store happens to
            // answer first for a category it does not own.
            uint? seed = registry.GetStoreForCategory(categoryId)?.GetLastChange(categoryId);
            if (seed.HasValue)
            {
                category.LastChange.Value = seed.Value;
                category.LastChange.ClearChangeMasks(SystemContext, false);
            }

            m_lastChangeNodes[categoryId] = category.LastChange;
        }

        /// <summary>
        /// Points a category's mandatory <c>FindAlias</c> method at the
        /// supplied registry. Shared by the standard well-known categories
        /// and by the categories materialized from a store's descriptor
        /// tree.
        /// </summary>
        private void WireFindAlias(
            NodeId categoryId,
            AliasNameCategoryState category,
            IAliasNameStoreRegistry registry)
        {
            category.FindAlias?.OnCallAsync = (ctx, method, objId, pattern, refType, ct) =>
                    AliasNameMethodDispatcher.FindAliasAsync(
                        registry,
                        Server.TypeTree,
                        objId.IsNull ? categoryId : objId,
                        pattern,
                        refType,
                        ct);
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

            var created = new List<NodeState>();
            var visited = new HashSet<NodeId>();

            foreach (IAliasNameStore store in registry.Stores)
            {
                foreach (AliasNameCategoryDescriptor root in store.RootCategories)
                {
                    // One recursive query per root — every alias in the
                    // subtree comes back tagged with its home category, so
                    // per-category re-queries (each of which would scan the
                    // whole subtree again) are unnecessary. The query is
                    // restricted to AliasFor and its subtypes because that
                    // is the reference the materialized nodes carry;
                    // entries a store holds under unrelated reference
                    // types are not Part 17 §6.2 alias associations and
                    // are served by FindAlias only.
                    IReadOnlyList<AliasNameVerboseDataType> aliases = await store
                        .FindAliasVerboseAsync(
                            root.NodeId,
                            AllAliasesPattern,
                            ReferenceTypeIds.AliasFor,
                            Server.TypeTree,
                            cancellationToken).ConfigureAwait(false);

                    var aliasesByCategory =
                        new Dictionary<NodeId, List<AliasNameVerboseDataType>>();
                    foreach (AliasNameVerboseDataType alias in aliases)
                    {
                        if (!aliasesByCategory.TryGetValue(
                                alias.AliasNameCategoryId,
                                out List<AliasNameVerboseDataType>? bucket))
                        {
                            bucket = [];
                            aliasesByCategory[alias.AliasNameCategoryId] = bucket;
                        }
                        bucket.Add(alias);
                    }

                    await MaterializeCategoryAsync(
                        registry,
                        root,
                        parent: null,
                        aliasesByCategory,
                        visited,
                        externalReferences,
                        created,
                        cancellationToken).ConfigureAwait(false);
                }
            }

            m_logger.MaterializedAliasNameNodes(created.Count);
            return created.ToArrayOf();
        }

        /// <summary>
        /// Materializes one category — reusing the predefined node when the
        /// standard NodeSet ships it — then its aliases and sub-categories.
        /// </summary>
        private async ValueTask MaterializeCategoryAsync(
            IAliasNameStoreRegistry registry,
            AliasNameCategoryDescriptor descriptor,
            AliasNameCategoryState? parent,
            IReadOnlyDictionary<NodeId, List<AliasNameVerboseDataType>> aliasesByCategory,
            HashSet<NodeId> visited,
            IDictionary<NodeId, IList<IReference>> externalReferences,
            List<NodeState> created,
            CancellationToken cancellationToken)
        {
            AliasNameCategoryState? category =
                FindPredefinedNode<AliasNameCategoryState>(descriptor.NodeId);

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
                if (descriptor.NodeId.NamespaceIndex != m_namespaceIndex)
                {
                    // Only nodes in this manager's own namespace may be
                    // minted here. Anything else is either a namespace
                    // owned by another node manager (typically an
                    // AliasNameNodeManager building its own nodes) or a
                    // mis-seeded well-known id — e.g. a ns=0 descriptor
                    // pointing at a node that is not an alias category,
                    // which creating would silently replace.
                    m_logger.SkippedAliasCategoryOutsideNamespace(descriptor.NodeId);
                    creatable = false;
                }
                else if (PredefinedNodes.TryGetValue(descriptor.NodeId, out NodeState? occupant))
                {
                    // The id exists but is not an AliasNameCategoryState —
                    // creating the category would replace the occupant.
                    m_logger.SkippedAliasCategoryOccupiedNodeId(
                        descriptor.NodeId, occupant.GetType().Name);
                    creatable = false;
                }
                else
                {
                    creatable = true;
                }

                if (!creatable)
                {
                    // The category itself cannot be materialized, but its
                    // sub-tree may still hold categories this manager owns
                    // — do not swallow them. With no parent node to hang
                    // off, they link under the standard Aliases root via
                    // the organizer fallback below.
                    foreach (AliasNameCategoryDescriptor child in descriptor.SubCategories)
                    {
                        await MaterializeCategoryAsync(
                            registry,
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

                category = SystemContext.CreateInstanceOfAliasNameCategoryType();
                category.BrowseName = RehomeServerDefinedName(descriptor.BrowseName);
                category.DisplayName = new LocalizedText(descriptor.BrowseName.Name);
                category.SymbolicName = descriptor.BrowseName.Name!;
                category.TypeDefinitionId = ObjectTypeIds.AliasNameCategoryType;
                category.ReferenceTypeId = ReferenceTypeIds.Organizes;

                // Mint ids for the mandatory children first — New() does not
                // preserve a caller-assigned id — then pin the category to
                // the descriptor's NodeId, which is the key the store and
                // the registry dispatch on.
                category.AssignNodeIds(SystemContext, []);
                category.NodeId = descriptor.NodeId;
            }

            // The mandatory FindAlias is wired for every materialized
            // category — including one that pre-existed as a predefined
            // node (from a companion NodeSet) without a handler; without
            // this a client would see the optional methods succeed while
            // the mandatory one returns BadNotImplemented.
            WireFindAlias(descriptor.NodeId, category, registry);

            // Add and wire the optional Part 17 children the store's
            // descriptor declares. For a category created above this only
            // grows the subtree before it is registered; for a standard
            // predefined category the new children are registered
            // individually.
            await ApplyCategoryCapabilitiesAsync(
                descriptor,
                category,
                registry,
                registerNewChildren: !isNewCategory,
                created,
                cancellationToken).ConfigureAwait(false);

            if (isNewCategory)
            {
                // A root category from the store has no parent in the
                // descriptor tree — link it under the standard Aliases
                // (i=23470) object so it is discoverable by browsing (the
                // same default AliasNameNodeManager applies via
                // LinkToStandardAliasesObject). Without an incoming
                // hierarchical reference the node would exist but be
                // reachable only by NodeId, defeating the Part 17 §6.2
                // browse-discovery purpose of materialization.
                AliasNameCategoryState? organizer = parent ??
                    (category.NodeId != ObjectIds.Aliases
                        ? FindPredefinedNode<AliasNameCategoryState>(ObjectIds.Aliases)
                        : null);
                if (organizer != null)
                {
                    LinkOrganizes(organizer, category);
                }

                await AddPredefinedNodeAsync(SystemContext, category, cancellationToken)
                    .ConfigureAwait(false);
                created.Add(category);
            }
            else if (parent != null)
            {
                LinkOrganizes(parent, category);
            }

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

            foreach (AliasNameCategoryDescriptor child in descriptor.SubCategories)
            {
                await MaterializeCategoryAsync(
                    registry,
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
        /// Adds the forward and inverse <c>Organizes</c> pair between two
        /// category nodes, tolerating either half already being present —
        /// <c>NodeState.AddReference</c> throws on duplicates.
        /// </summary>
        private static void LinkOrganizes(
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
                ? new QualifiedName(name.Name, m_namespaceIndex)
                : name;
        }

        /// <summary>
        /// Instantiates and wires the optional Part 17 children a category
        /// declares through <see cref="AliasNameCategoryDescriptor.Capabilities"/>:
        /// <c>FindAliasVerbose</c> (§6.3.3), <c>AddAliasesToCategory</c>
        /// (§6.3.4), <c>DeleteAliasesFromCategory</c> (§6.3.5) and
        /// <c>LastChange</c> (§6.3.1).
        /// </summary>
        /// <remarks>
        /// The standard NodeSet instantiates none of these on the
        /// well-known <c>Aliases</c>/<c>TagVariables</c>/<c>Topics</c>
        /// objects (only <c>FindAlias</c>, plus <c>LastChange</c> on
        /// <c>Aliases</c>), so the generated <c>Add…</c> helpers create them
        /// here — at the NodeIds
        /// <see cref="ReservedChildIds"/> supplies for the well-known
        /// parents, and at factory-minted ids for an application-defined
        /// category.
        /// </remarks>
        private async ValueTask ApplyCategoryCapabilitiesAsync(
            AliasNameCategoryDescriptor descriptor,
            AliasNameCategoryState category,
            IAliasNameStoreRegistry registry,
            bool registerNewChildren,
            List<NodeState> created,
            CancellationToken cancellationToken)
        {
            AliasNameCapabilities capabilities = descriptor.Capabilities;
            ReservedChildIds reserved = ReservedChildIds.For(descriptor.NodeId);
            NodeId categoryId = descriptor.NodeId;
            var addedChildren = new List<NodeState>();

            // Each optional child is created AND wired only under its
            // capability flag: a child that already exists on the node (a
            // companion NodeSet may ship one) but that the descriptor did
            // not declare stays unwired — otherwise a category declared
            // read-only would silently accept mutations.
            if ((capabilities & AliasNameCapabilities.FindAliasVerbose) != 0)
            {
                if (category.FindAliasVerbose == null)
                {
                    category.AddFindAliasVerbose(
                        SystemContext, reserved.FindAliasVerbose.Method);
                    ApplyReservedArgumentIds(
                        category.FindAliasVerbose, reserved.FindAliasVerbose);
                    addedChildren.Add(category.FindAliasVerbose!);
                }

                category.FindAliasVerbose!.OnCallAsync =
                    (ctx, method, objId, pattern, refType, ct) =>
                        AliasNameMethodDispatcher.FindAliasVerboseAsync(
                            registry,
                            Server.TypeTree,
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
                        SystemContext, reserved.AddAliasesToCategory.Method);
                    ApplyReservedArgumentIds(
                        category.AddAliasesToCategory, reserved.AddAliasesToCategory);
                    addedChildren.Add(category.AddAliasesToCategory!);
                }

                category.AddAliasesToCategory!.OnCallAsync =
                    (ctx, method, objId, names, targets, servers, refType, ct) =>
                        !AliasNameMethodDispatcher.HasSecureAdminAccess(ctx)
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
                        SystemContext, reserved.DeleteAliasesFromCategory.Method);
                    ApplyReservedArgumentIds(
                        category.DeleteAliasesFromCategory, reserved.DeleteAliasesFromCategory);
                    addedChildren.Add(category.DeleteAliasesFromCategory!);
                }

                category.DeleteAliasesFromCategory!.OnCallAsync =
                    (ctx, method, objId, names, targets, ct) =>
                        !AliasNameMethodDispatcher.HasSecureAdminAccess(ctx)
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
                    category.AddLastChange(SystemContext, reserved.LastChange);
                    addedChildren.Add(category.LastChange!);
                }

                BindLastChange(categoryId, category, registry);
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
                    await AddPredefinedNodeAsync(SystemContext, child, cancellationToken)
                        .ConfigureAwait(false);
                }
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
            ReservedMethodIds reserved)
        {
            if (method == null || reserved.Method.IsNull)
            {
                return;
            }
            if (method.InputArguments != null)
            {
                method.InputArguments.NodeId = reserved.InputArguments;
            }
            if (method.OutputArguments != null)
            {
                method.OutputArguments.NodeId = reserved.OutputArguments;
            }
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
                        descriptor, category, alias.AliasName, name, created,
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
        /// replacing the occupant (<c>AddPredefinedNode</c> overwrites).
        /// </remarks>
        private async ValueTask<AliasNameState> GetOrCreateAliasNodeAsync(
            AliasNameCategoryDescriptor descriptor,
            AliasNameCategoryState category,
            QualifiedName aliasName,
            string name,
            List<NodeState> created,
            CancellationToken cancellationToken)
        {
            var mintedId = new NodeId(
                Utils.Format("{0}.{1}", descriptor.NodeId, name),
                m_namespaceIndex);

            if (PredefinedNodes.TryGetValue(mintedId, out NodeState? existing))
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

            AliasNameState aliasNode = SystemContext.CreateInstanceOfAliasNameType();

            aliasNode.NodeId = mintedId.IsNull
                ? New(SystemContext, aliasNode)
                : mintedId;
            aliasNode.BrowseName = RehomeServerDefinedName(aliasName);
            aliasNode.DisplayName = new LocalizedText(string.Empty, name);
            aliasNode.SymbolicName = name;
            aliasNode.TypeDefinitionId = ObjectTypeIds.AliasNameType;
            aliasNode.ReferenceTypeId = ReferenceTypeIds.Organizes;

            LinkAliasToCategory(category, aliasNode);

            await AddPredefinedNodeAsync(SystemContext, aliasNode, cancellationToken)
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
        /// back, which usually lands in another node manager and therefore
        /// travels through <paramref name="externalReferences"/>.
        /// </summary>
        private void AddAliasForReference(
            AliasNameState aliasNode,
            ExpandedNodeId target,
            string? serverUri,
            IDictionary<NodeId, IList<IReference>> externalReferences)
        {
            if (target.IsNull)
            {
                return;
            }

            if (!string.IsNullOrEmpty(serverUri))
            {
                // A target on a remote server has no local node to carry
                // the inverse reference. Register the URI in the server
                // table and stamp its index onto the reference target, so
                // clients can resolve which server owns the node via the
                // ServerArray — an ExpandedNodeId with ServerIndex 0 would
                // misreport the target as local.
                uint serverIndex = Server.ServerUris.GetIndexOrAppend(serverUri!);
                ExpandedNodeId remoteTarget = target.WithServerIndex(serverIndex);
                if (!aliasNode.ReferenceExists(ReferenceTypeIds.AliasFor, false, remoteTarget))
                {
                    aliasNode.AddReference(ReferenceTypeIds.AliasFor, false, remoteTarget);
                }
                return;
            }

            // Resolve the namespace-uri form a store may have been seeded
            // with, so the reference carries a plain local NodeId. The
            // duplicate check runs on the RESOLVED id — the same target
            // seeded once in URI form and once in index form must produce
            // one reference, and AddReference throws on duplicates.
            var localTarget = ExpandedNodeId.ToNodeId(target, Server.NamespaceUris);
            if (localTarget.IsNull)
            {
                if (!aliasNode.ReferenceExists(ReferenceTypeIds.AliasFor, false, target))
                {
                    aliasNode.AddReference(ReferenceTypeIds.AliasFor, false, target);
                }
                return;
            }

            if (aliasNode.ReferenceExists(ReferenceTypeIds.AliasFor, false, localTarget))
            {
                return;
            }

            aliasNode.AddReference(ReferenceTypeIds.AliasFor, false, localTarget);

            // The inverse reference travels only with a newly added
            // forward reference, so repeat materializations do not queue
            // duplicate external references for the target's manager.
            AddExternalReference(
                localTarget,
                ReferenceTypeIds.AliasFor,
                true,
                aliasNode.NodeId,
                externalReferences);
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

        /// <summary>
        /// The Part 4 §7.40 Like-pattern that matches every alias name.
        /// </summary>
        private const string AllAliasesPattern = "%";

        /// <summary>
        /// The reserved NodeIds of one optional method and its two argument
        /// properties. <see cref="Method"/> is <see cref="NodeId.Null"/> for
        /// a category with no reserved allocation, which makes the generated
        /// <c>Add…</c> helper fall back to the node-id factory.
        /// </summary>
        private readonly record struct ReservedMethodIds(
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
        private readonly record struct ReservedChildIds(
            ReservedMethodIds FindAliasVerbose,
            ReservedMethodIds AddAliasesToCategory,
            ReservedMethodIds DeleteAliasesFromCategory,
            NodeId LastChange)
        {
            public static ReservedChildIds For(NodeId categoryId)
            {
                return s_reserved.TryGetValue(categoryId, out ReservedChildIds ids)
                    ? ids
                    : default;
            }

            // One row per well-known category; the numeric literals are the
            // Method rows of StandardTypes.csv, the VariableIds constants
            // its Variable rows.
            private static readonly Dictionary<NodeId, ReservedChildIds> s_reserved = new()
            {
                [ObjectIds.Aliases] = new ReservedChildIds(
                    new ReservedMethodIds(
                        new NodeId(24054u),
                        VariableIds.Aliases_FindAliasVerbose_InputArguments,
                        VariableIds.Aliases_FindAliasVerbose_OutputArguments),
                    new ReservedMethodIds(
                        new NodeId(24057u),
                        VariableIds.Aliases_AddAliasesToCategory_InputArguments,
                        VariableIds.Aliases_AddAliasesToCategory_OutputArguments),
                    new ReservedMethodIds(
                        new NodeId(24060u),
                        VariableIds.Aliases_DeleteAliasesFromCategory_InputArguments,
                        VariableIds.Aliases_DeleteAliasesFromCategory_OutputArguments),
                    VariableIds.Aliases_LastChange),
                [ObjectIds.TagVariables] = new ReservedChildIds(
                    new ReservedMethodIds(
                        new NodeId(24063u),
                        VariableIds.TagVariables_FindAliasVerbose_InputArguments,
                        VariableIds.TagVariables_FindAliasVerbose_OutputArguments),
                    new ReservedMethodIds(
                        new NodeId(24066u),
                        VariableIds.TagVariables_AddAliasesToCategory_InputArguments,
                        VariableIds.TagVariables_AddAliasesToCategory_OutputArguments),
                    new ReservedMethodIds(
                        new NodeId(24069u),
                        VariableIds.TagVariables_DeleteAliasesFromCategory_InputArguments,
                        VariableIds.TagVariables_DeleteAliasesFromCategory_OutputArguments),
                    new NodeId(32854u)),
                [ObjectIds.Topics] = new ReservedChildIds(
                    new ReservedMethodIds(
                        new NodeId(24072u),
                        VariableIds.Topics_FindAliasVerbose_InputArguments,
                        VariableIds.Topics_FindAliasVerbose_OutputArguments),
                    new ReservedMethodIds(
                        new NodeId(24075u),
                        VariableIds.Topics_AddAliasesToCategory_InputArguments,
                        VariableIds.Topics_AddAliasesToCategory_OutputArguments),
                    new ReservedMethodIds(
                        new NodeId(24078u),
                        VariableIds.Topics_DeleteAliasesFromCategory_InputArguments,
                        VariableIds.Topics_DeleteAliasesFromCategory_OutputArguments),
                    new NodeId(32856u))
            };
        }

        private IAliasNameStoreRegistry? m_aliasRegistry;

        /// <summary>
        /// Serializes the <c>LastChange</c> node updates raised by store
        /// mutations, which arrive on arbitrary client-call threads.
        /// </summary>
        private readonly object m_lastChangeSync = new();

        /// <summary>
        /// The <c>LastChange</c> property of every category that exposes
        /// one, keyed by category NodeId. Concurrent because
        /// <see cref="OnAliasRegistryChanged"/> reads it on client-call
        /// threads while <see cref="UnwireStandardAliasMethods"/> may clear
        /// it during disposal.
        /// </summary>
        private readonly ConcurrentDictionary<NodeId, PropertyState<uint>> m_lastChangeNodes = [];
    }

    /// <summary>
    /// Source-generated log messages for the Part 17 parts of
    /// DiagnosticsNodeManager.
    /// </summary>
    internal static partial class DiagnosticsNodeManagerAliasNamesLog
    {
        [LoggerMessage(EventId = ServerEventIds.DiagnosticsNodeManager + 1, Level = LogLevel.Information,
            Message = "Materialized {Count} Part 17 alias name nodes from the registered alias stores.")]
        public static partial void MaterializedAliasNameNodes(this ILogger logger, int count);

        [LoggerMessage(EventId = ServerEventIds.DiagnosticsNodeManager + 2, Level = LogLevel.Warning,
            Message = "Skipped alias category {CategoryId}: its NodeId is outside the diagnostics namespace, so it belongs to another node manager or is a mis-seeded well-known id.")]
        public static partial void SkippedAliasCategoryOutsideNamespace(this ILogger logger, NodeId categoryId);

        [LoggerMessage(EventId = ServerEventIds.DiagnosticsNodeManager + 3, Level = LogLevel.Warning,
            Message = "Skipped alias category {CategoryId}: the NodeId is already registered by a node of type {OccupantType}.")]
        public static partial void SkippedAliasCategoryOccupiedNodeId(this ILogger logger, NodeId categoryId, string occupantType);

        [LoggerMessage(EventId = ServerEventIds.DiagnosticsNodeManager + 4, Level = LogLevel.Warning,
            Message = "Alias name NodeId {MintedId} for alias '{AliasName}' collides with an existing node; a factory-minted NodeId is used instead.")]
        public static partial void AliasNameNodeIdCollision(this ILogger logger, NodeId mintedId, string aliasName);
    }
}
