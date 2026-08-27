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

            foreach (IAliasNameStore store in registry.Stores)
            {
                uint? seed = store.GetLastChange(categoryId);
                if (seed.HasValue)
                {
                    category.LastChange.Value = seed.Value;
                    category.LastChange.ClearChangeMasks(SystemContext, false);
                    break;
                }
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
        /// nodes are imported into the <c>CoreNodeManager</c> so they are
        /// reachable by Browse, and are also returned for inspection.
        /// </para>
        /// </remarks>
        /// <param name="externalReferences">The dictionary supplied to
        /// <c>CreateAddressSpaceAsync</c>; used to add the inverse
        /// <c>HasAlias</c> reference on target nodes owned by other node
        /// managers.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The nodes created by this call.</returns>
        protected async ValueTask<IReadOnlyList<NodeState>> MaterializeRegisteredAliasNameNodesAsync(
            IDictionary<NodeId, IList<IReference>> externalReferences,
            CancellationToken cancellationToken = default)
        {
            var created = new List<NodeState>();

            IAliasNameStoreRegistry? registry = m_aliasRegistry ??
                (Server as IAliasNameStoreRegistryProvider)?.AliasNameStoreRegistry;
            if (registry == null)
            {
                return created;
            }

            foreach (IAliasNameStore store in registry.Stores)
            {
                foreach (AliasNameCategoryDescriptor root in store.RootCategories)
                {
                    await MaterializeCategoryAsync(
                        store,
                        registry,
                        root,
                        parent: null,
                        externalReferences,
                        created,
                        cancellationToken).ConfigureAwait(false);
                }
            }

            if (created.Count > 0)
            {
                await Server.CoreNodeManager.ImportNodesAsync(
                    SystemContext,
                    created,
                    true,
                    cancellationToken).ConfigureAwait(false);
            }

            m_logger.MaterializedAliasNameNodes(created.Count);
            return created;
        }

        /// <summary>
        /// Materializes one category — reusing the predefined node when the
        /// standard NodeSet ships it — then its aliases and sub-categories.
        /// </summary>
        private async ValueTask MaterializeCategoryAsync(
            IAliasNameStore store,
            IAliasNameStoreRegistry registry,
            AliasNameCategoryDescriptor descriptor,
            AliasNameCategoryState? parent,
            IDictionary<NodeId, IList<IReference>> externalReferences,
            List<NodeState> created,
            CancellationToken cancellationToken)
        {
            AliasNameCategoryState? category =
                FindPredefinedNode<AliasNameCategoryState>(descriptor.NodeId);
            bool isNewCategory = category == null;

            if (category == null && !IsNodeIdInNamespace(descriptor.NodeId))
            {
                // The category belongs to another node manager — typically an
                // AliasNameNodeManager that registered its store with the
                // server-wide registry and builds these nodes itself.
                // Creating them here would claim its NodeIds a second time.
                return;
            }

            if (category == null)
            {
                category = SystemContext.CreateInstanceOfAliasNameCategoryType();
                category.BrowseName = descriptor.BrowseName;
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

                WireFindAlias(descriptor.NodeId, category, registry);
            }

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
                if (parent != null)
                {
                    parent.AddReference(ReferenceTypeIds.Organizes, false, category.NodeId);
                    category.AddReference(ReferenceTypeIds.Organizes, true, parent.NodeId);
                }

                await AddPredefinedNodeAsync(SystemContext, category, cancellationToken)
                    .ConfigureAwait(false);
                created.Add(category);
            }

            await MaterializeAliasesAsync(
                store, descriptor, category, externalReferences, created, cancellationToken)
                .ConfigureAwait(false);

            foreach (AliasNameCategoryDescriptor child in descriptor.SubCategories)
            {
                await MaterializeCategoryAsync(
                    store,
                    registry,
                    child,
                    category,
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

            if ((capabilities & AliasNameCapabilities.FindAliasVerbose) != 0 &&
                category.FindAliasVerbose == null)
            {
                category.AddFindAliasVerbose(SystemContext, reserved.FindAliasVerbose.Method);
                ApplyReservedArgumentIds(
                    category.FindAliasVerbose, reserved.FindAliasVerbose);
                await RegisterAddedChildAsync(
                    category.FindAliasVerbose, registerNewChildren, created, cancellationToken)
                    .ConfigureAwait(false);
            }

            if ((capabilities & AliasNameCapabilities.AddAliasesToCategory) != 0 &&
                category.AddAliasesToCategory == null)
            {
                category.AddAddAliasesToCategory(
                    SystemContext, reserved.AddAliasesToCategory.Method);
                ApplyReservedArgumentIds(
                    category.AddAliasesToCategory, reserved.AddAliasesToCategory);
                await RegisterAddedChildAsync(
                    category.AddAliasesToCategory, registerNewChildren, created, cancellationToken)
                    .ConfigureAwait(false);
            }

            if ((capabilities & AliasNameCapabilities.DeleteAliasesFromCategory) != 0 &&
                category.DeleteAliasesFromCategory == null)
            {
                category.AddDeleteAliasesFromCategory(
                    SystemContext, reserved.DeleteAliasesFromCategory.Method);
                ApplyReservedArgumentIds(
                    category.DeleteAliasesFromCategory, reserved.DeleteAliasesFromCategory);
                await RegisterAddedChildAsync(
                    category.DeleteAliasesFromCategory, registerNewChildren, created,
                    cancellationToken).ConfigureAwait(false);
            }

            if ((capabilities & AliasNameCapabilities.LastChange) != 0 &&
                category.LastChange == null)
            {
                category.AddLastChange(SystemContext, reserved.LastChange);
                await RegisterAddedChildAsync(
                    category.LastChange, registerNewChildren, created, cancellationToken)
                    .ConfigureAwait(false);
            }

            NodeId categoryId = descriptor.NodeId;

            category.FindAliasVerbose?.OnCallAsync =
                (ctx, method, objId, pattern, refType, ct) =>
                    AliasNameMethodDispatcher.FindAliasVerboseAsync(
                        registry,
                        Server.TypeTree,
                        objId.IsNull ? categoryId : objId,
                        pattern,
                        refType,
                        ct);

            category.AddAliasesToCategory?.OnCallAsync =
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

            category.DeleteAliasesFromCategory?.OnCallAsync =
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

            BindLastChange(categoryId, category, registry);
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
        /// Registers a child added to an already-loaded category. Children
        /// of a category that has not been registered yet are covered by
        /// the registration of their parent.
        /// </summary>
        private async ValueTask RegisterAddedChildAsync(
            NodeState? child,
            bool registerNewChildren,
            List<NodeState> created,
            CancellationToken cancellationToken)
        {
            if (child == null || !registerNewChildren)
            {
                return;
            }
            await AddPredefinedNodeAsync(SystemContext, child, cancellationToken)
                .ConfigureAwait(false);
            created.Add(child);
        }

        /// <summary>
        /// Creates one <c>AliasNameType</c> instance per alias whose home
        /// category is <paramref name="descriptor"/>. Aliases contributed by
        /// sub-categories are skipped here — they are materialized under
        /// their own category — even though <c>FindAliasVerbose</c> reports
        /// them recursively.
        /// </summary>
        private async ValueTask MaterializeAliasesAsync(
            IAliasNameStore store,
            AliasNameCategoryDescriptor descriptor,
            AliasNameCategoryState category,
            IDictionary<NodeId, IList<IReference>> externalReferences,
            List<NodeState> created,
            CancellationToken cancellationToken)
        {
            // Restrict the query to AliasFor and its subtypes: the reference
            // the materialized node carries is AliasFor, and
            // AliasNameVerboseDataType does not report which reference type
            // an entry was stored under. Without the filter an alias
            // registered under an unrelated reference type would be
            // published as an AliasFor association it does not have.
            IReadOnlyList<AliasNameVerboseDataType> aliases = await store
                .FindAliasVerboseAsync(
                    descriptor.NodeId,
                    AllAliasesPattern,
                    ReferenceTypeIds.AliasFor,
                    Server.TypeTree,
                    cancellationToken).ConfigureAwait(false);

            // One node per alias name; the store reports a separate record
            // per reference type, and a name may carry several targets.
            var byName = new Dictionary<string, AliasNameState>();
            var seenTargets = new HashSet<(string Name, ExpandedNodeId Target)>();

            foreach (AliasNameVerboseDataType alias in aliases)
            {
                if (alias.AliasNameCategoryId != descriptor.NodeId ||
                    string.IsNullOrEmpty(alias.AliasName.Name))
                {
                    continue;
                }

                string name = alias.AliasName.Name!;
                if (!byName.TryGetValue(name, out AliasNameState? aliasNode))
                {
                    aliasNode = CreateAliasNode(descriptor, category, alias.AliasName, name);
                    byName[name] = aliasNode;
                    created.Add(aliasNode);
                }

                for (int ii = 0; ii < alias.ReferencedNodes.Count; ii++)
                {
                    ExpandedNodeId target = alias.ReferencedNodes[ii];

                    // The same target may arrive once per AliasFor subtype
                    // the store grouped it under; the node carries one
                    // reference per target.
                    if (!seenTargets.Add((name, target)))
                    {
                        continue;
                    }

                    AddAliasForReference(
                        aliasNode,
                        target,
                        ii < alias.ServerUris.Count ? alias.ServerUris[ii] : null,
                        externalReferences);
                }
            }

            foreach (AliasNameState aliasNode in byName.Values)
            {
                await AddPredefinedNodeAsync(SystemContext, aliasNode, cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Builds the <c>AliasNameType</c> instance for one alias. Per
        /// Part 17 §6.2 the BrowseName carries the alias name and the
        /// DisplayName repeats it with an empty locale.
        /// </summary>
        private AliasNameState CreateAliasNode(
            AliasNameCategoryDescriptor descriptor,
            AliasNameCategoryState category,
            QualifiedName aliasName,
            string name)
        {
            AliasNameState aliasNode = SystemContext.CreateInstanceOfAliasNameType();

            aliasNode.NodeId = new NodeId(
                Utils.Format("{0}.{1}", descriptor.NodeId, name),
                m_namespaceIndex);
            aliasNode.BrowseName = aliasName;
            aliasNode.DisplayName = new LocalizedText(string.Empty, name);
            aliasNode.SymbolicName = name;
            aliasNode.TypeDefinitionId = ObjectTypeIds.AliasNameType;
            aliasNode.ReferenceTypeId = ReferenceTypeIds.Organizes;

            category.AddReference(ReferenceTypeIds.Organizes, false, aliasNode.NodeId);
            aliasNode.AddReference(ReferenceTypeIds.Organizes, true, category.NodeId);

            return aliasNode;
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
                // the inverse reference.
                aliasNode.AddReference(ReferenceTypeIds.AliasFor, false, target);
                return;
            }

            // Resolve the namespace-uri form a store may have been seeded
            // with, so the reference carries a plain local NodeId.
            var localTarget = ExpandedNodeId.ToNodeId(target, Server.NamespaceUris);
            if (localTarget.IsNull)
            {
                aliasNode.AddReference(ReferenceTypeIds.AliasFor, false, target);
                return;
            }

            aliasNode.AddReference(ReferenceTypeIds.AliasFor, false, localTarget);

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
                lastChange.Value = e.LastChange;
                lastChange.ClearChangeMasks(SystemContext, false);
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
                if (categoryId == ObjectIds.Aliases)
                {
                    return new ReservedChildIds(
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
                        VariableIds.Aliases_LastChange);
                }
                if (categoryId == ObjectIds.TagVariables)
                {
                    return new ReservedChildIds(
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
                        new NodeId(32854u));
                }
                if (categoryId == ObjectIds.Topics)
                {
                    return new ReservedChildIds(
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
                        new NodeId(32856u));
                }
                return default;
            }
        }

        private IAliasNameStoreRegistry? m_aliasRegistry;

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
    }
}
