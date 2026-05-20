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
    /// Standalone OPC UA Part 17 (Alias Names) node manager. Builds an
    /// address-space tree of <c>AliasNameCategoryType</c> instances from
    /// an <see cref="IAliasNameStore"/>'s
    /// <see cref="IAliasNameStore.RootCategories"/>, wires the typed
    /// <c>OnCallAsync</c> handlers of the generated
    /// <c>FindAliasMethodState</c>/<c>FindAliasVerboseMethodState</c>/<c>AddAliasesToCategoryMethodState</c>/<c>DeleteAliasesFromCategoryMethodState</c>
    /// children, and (when configured) registers the store with the
    /// server-wide <see cref="IAliasNameStoreRegistry"/> so that the
    /// standard well-known <c>Aliases (i=23470)</c> /
    /// <c>TagVariables (i=23479)</c> / <c>Topics (i=23488)</c> nodes also
    /// dispatch through it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Apps opt in by adding this manager to their server's node-manager
    /// list. The manager owns a single namespace
    /// (<see cref="AliasNameNodeManagerOptions.NamespaceUri"/>) under
    /// which it creates its category and alias instances; standard
    /// well-known categories owned by <c>DiagnosticsNodeManager</c> are
    /// not duplicated — only their methods are wired through the
    /// registry.
    /// </para>
    /// <para>
    /// All four Part 17 methods are exposed only when the corresponding
    /// <see cref="AliasNameCapabilities"/> flag is set on the
    /// <see cref="AliasNameCategoryDescriptor"/>. Mutating methods
    /// (<c>AddAliasesToCategory</c>/<c>DeleteAliasesFromCategory</c>)
    /// default to requiring the <c>SecurityAdmin</c> role on a
    /// <c>SignAndEncrypt</c> channel; override
    /// <see cref="AliasNameNodeManagerOptions.RequireSecurityAdminForMutations"/>
    /// to opt out.
    /// </para>
    /// </remarks>
    public class AliasNameNodeManager : AsyncCustomNodeManager
    {
        /// <summary>
        /// Initializes a new instance.
        /// </summary>
        /// <param name="server">The server internal interface.</param>
        /// <param name="configuration">The application configuration.</param>
        /// <param name="store">The pluggable alias-name backend; the
        /// manager builds its address space from
        /// <see cref="IAliasNameStore.RootCategories"/> and dispatches
        /// every Part 17 method through it.</param>
        /// <param name="options">Optional tunables; defaults applied
        /// when <c>null</c>.</param>
        public AliasNameNodeManager(
            IServerInternal server,
            ApplicationConfiguration configuration,
            IAliasNameStore store,
            AliasNameNodeManagerOptions? options = null)
            : base(server, configuration, ResolveNamespaceUri(options))
        {
            m_store = store ?? throw new ArgumentNullException(nameof(store));
            m_options = options ?? new AliasNameNodeManagerOptions();
            m_aliasLogger = server.Telemetry.CreateLogger<AliasNameNodeManager>();
            m_registry = ResolveServerRegistry(server);
            m_localCategoryDispatcher = new AliasNameStoreRegistry();
            m_localCategoryDispatcher.Register(m_store);
        }

        /// <summary>
        /// The backing <see cref="IAliasNameStore"/>.
        /// </summary>
        public IAliasNameStore Store => m_store;

        /// <summary>
        /// The tunables in use.
        /// </summary>
        public AliasNameNodeManagerOptions Options => m_options;

        /// <inheritdoc/>
        public override NodeId New(ISystemContext context, NodeState node)
        {
            // Preserve any caller-assigned NodeId that already lives in our
            // namespace; otherwise mint a sequential numeric id.
            if (!node.NodeId.IsNull &&
                node.NodeId.NamespaceIndex == NamespaceIndex)
            {
                return node.NodeId;
            }
            uint id = Utils.IncrementIdentifier(ref m_nextNodeId);
            return new NodeId(id, NamespaceIndex);
        }

        /// <inheritdoc/>
        public override async ValueTask CreateAddressSpaceAsync(
            IDictionary<NodeId, IList<IReference>> externalReferences,
            CancellationToken cancellationToken = default)
        {
            await base.CreateAddressSpaceAsync(externalReferences, cancellationToken)
                .ConfigureAwait(false);

            foreach (AliasNameCategoryDescriptor root in m_store.RootCategories)
            {
                AliasNameCategoryState rootState = BuildCategoryTree(root);
                m_rootCategoryStates[root.NodeId] = rootState;

                if (m_options.LinkToStandardAliasesObject)
                {
                    AddExternalReference(
                        ObjectIds.Aliases,
                        ReferenceTypeIds.Organizes,
                        isInverse: false,
                        rootState.NodeId,
                        externalReferences);
                    rootState.AddReference(
                        ReferenceTypeIds.Organizes,
                        isInverse: true,
                        ObjectIds.Aliases);
                }

                await AddPredefinedNodeAsync(
                    SystemContext, rootState, cancellationToken)
                    .ConfigureAwait(false);
            }

            if (m_options.RegisterWithServerRegistry && m_registry != null)
            {
                try
                {
                    m_registry.Register(m_store);
                    m_registeredWithServer = true;
                }
                catch (InvalidOperationException ex)
                {
                    m_aliasLogger.LogWarning(ex,
                        "AliasNameStore could not be registered with " +
                        "the server-wide registry; standard well-known " +
                        "Aliases methods will not dispatch through it.");
                }
            }

            m_store.Changed += OnStoreChanged;
        }

        /// <inheritdoc/>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                m_store.Changed -= OnStoreChanged;
                if (m_registeredWithServer && m_registry != null)
                {
                    m_registry.Unregister(m_store);
                    m_registeredWithServer = false;
                }
                m_localCategoryDispatcher.Dispose();
            }
            base.Dispose(disposing);
        }

        /// <summary>
        /// Recursively builds an <see cref="AliasNameCategoryState"/>
        /// instance for the supplied descriptor — including any optional
        /// children declared by its capabilities and any nested
        /// sub-categories — and wires every method's typed
        /// <c>OnCallAsync</c> handler to the dispatcher.
        /// </summary>
        protected virtual AliasNameCategoryState BuildCategoryTree(
            AliasNameCategoryDescriptor descriptor)
        {
            // CreateInstanceOf... returns a typed instance with the
            // mandatory FindAlias child already created. We then override
            // the identity attributes and add the optional Part 17
            // children we have been asked to expose.
            AliasNameCategoryState category =
                SystemContext.CreateInstanceOfAliasNameCategoryType();
            category.NodeId = descriptor.NodeId;
            category.BrowseName = descriptor.BrowseName;
            category.DisplayName = new LocalizedText(descriptor.BrowseName.Name);
            category.SymbolicName = descriptor.BrowseName.Name!;
            category.TypeDefinitionId = ObjectTypeIds.AliasNameCategoryType;
            category.ReferenceTypeId = ReferenceTypeIds.Organizes;

            AliasNameCapabilities cap = descriptor.Capabilities;
            if ((cap & AliasNameCapabilities.FindAliasVerbose) != 0)
            {
                category.AddFindAliasVerbose(SystemContext);
            }
            if ((cap & AliasNameCapabilities.LastChange) != 0)
            {
                category.AddLastChange(SystemContext);
            }
            if ((cap & AliasNameCapabilities.AddAliasesToCategory) != 0)
            {
                category.AddAddAliasesToCategory(SystemContext);
            }
            if ((cap & AliasNameCapabilities.DeleteAliasesFromCategory) != 0)
            {
                category.AddDeleteAliasesFromCategory(SystemContext);
            }

            foreach (AliasNameCategoryDescriptor child in descriptor.SubCategories)
            {
                AliasNameCategoryState childState = BuildCategoryTree(child);
                childState.ReferenceTypeId = ReferenceTypeIds.Organizes;
                category.AddChild(childState);
            }

            // Auto-assign NodeIds to every child not pre-pinned in our
            // namespace.
            category.AssignNodeIds(SystemContext, []);

            // Wire method handlers (after node ids are stable).
            WireCategoryHandlers(descriptor.NodeId, category);

            // Seed LastChange.
            if (category.LastChange != null)
            {
                category.LastChange.Value
                    = m_store.GetLastChange(descriptor.NodeId) ?? 0u;
            }

            return category;
        }

        private void WireCategoryHandlers(NodeId categoryId, AliasNameCategoryState category)
        {
            if (category.FindAlias != null)
            {
                category.FindAlias.OnCallAsync = (ctx, method, objId, pattern, refType, ct) =>
                    AliasNameMethodDispatcher.FindAliasAsync(
                        m_localCategoryDispatcher,
                        Server.TypeTree,
                        objId.IsNull ? categoryId : objId,
                        pattern,
                        refType,
                        ct);
            }
            if (category.FindAliasVerbose != null)
            {
                category.FindAliasVerbose.OnCallAsync = (ctx, method, objId, pattern, refType, ct) =>
                    AliasNameMethodDispatcher.FindAliasVerboseAsync(
                        m_localCategoryDispatcher,
                        Server.TypeTree,
                        objId.IsNull ? categoryId : objId,
                        pattern,
                        refType,
                        ct);
            }
            if (category.AddAliasesToCategory != null)
            {
                category.AddAliasesToCategory.OnCallAsync =
                    (ctx, method, objId, names, targets, servers, refType, ct) =>
                        DispatchAddAsync(ctx, categoryId, objId, names, targets, servers, refType, ct);
            }
            if (category.DeleteAliasesFromCategory != null)
            {
                category.DeleteAliasesFromCategory.OnCallAsync =
                    (ctx, method, objId, names, targets, ct) =>
                        DispatchDeleteAsync(ctx, categoryId, objId, names, targets, ct);
            }

            // Recurse into sub-category children (already wired through
            // BuildCategoryTree, but their state objects sit on this
            // node's children list — wire those too).
            var children = new List<BaseInstanceState>();
            category.GetChildren(SystemContext, children);
            foreach (BaseInstanceState child in children)
            {
                if (child is AliasNameCategoryState childCategory &&
                    !ReferenceEquals(childCategory, category))
                {
                    WireCategoryHandlers(childCategory.NodeId, childCategory);
                }
            }
        }

        private ValueTask<AddAliasesToCategoryMethodStateResult> DispatchAddAsync(
            ISystemContext context,
            NodeId fallbackCategoryId,
            NodeId objectId,
            ArrayOf<string> aliasNames,
            ArrayOf<ExpandedNodeId> targetNodes,
            ArrayOf<string> targetServers,
            NodeId targetReferenceType,
            CancellationToken ct)
        {
            if (m_options.RequireSecurityAdminForMutations &&
                !HasSecureAdminAccess(context))
            {
                return new ValueTask<AddAliasesToCategoryMethodStateResult>(
                    new AddAliasesToCategoryMethodStateResult
                    {
                        ServiceResult = new ServiceResult(StatusCodes.BadUserAccessDenied),
                        ErrorCodes = default
                    });
            }
            return AliasNameMethodDispatcher.AddAliasesAsync(
                m_localCategoryDispatcher,
                objectId.IsNull ? fallbackCategoryId : objectId,
                aliasNames,
                targetNodes,
                targetServers,
                targetReferenceType,
                ct);
        }

        private ValueTask<DeleteAliasesFromCategoryMethodStateResult> DispatchDeleteAsync(
            ISystemContext context,
            NodeId fallbackCategoryId,
            NodeId objectId,
            ArrayOf<string> aliasNames,
            ArrayOf<ExpandedNodeId> targetNodes,
            CancellationToken ct)
        {
            if (m_options.RequireSecurityAdminForMutations &&
                !HasSecureAdminAccess(context))
            {
                return new ValueTask<DeleteAliasesFromCategoryMethodStateResult>(
                    new DeleteAliasesFromCategoryMethodStateResult
                    {
                        ServiceResult = new ServiceResult(StatusCodes.BadUserAccessDenied),
                        ErrorCodes = default
                    });
            }
            return AliasNameMethodDispatcher.DeleteAliasesAsync(
                m_localCategoryDispatcher,
                objectId.IsNull ? fallbackCategoryId : objectId,
                aliasNames,
                targetNodes,
                ct);
        }

        private void OnStoreChanged(object? sender, AliasStoreChangedEventArgs e)
        {
            lock (m_lock)
            {
                if (!m_rootCategoryStates.TryGetValue(e.CategoryId, out AliasNameCategoryState? root))
                {
                    root = FindCategoryNode(e.CategoryId);
                }
                if (root?.LastChange != null)
                {
                    root.LastChange.Value = e.LastChange;
                    root.LastChange.ClearChangeMasks(SystemContext, false);
                }
            }
        }

        private AliasNameCategoryState? FindCategoryNode(NodeId categoryId)
        {
            foreach (AliasNameCategoryState root in m_rootCategoryStates.Values)
            {
                AliasNameCategoryState? found = FindRecursive(root, categoryId);
                if (found != null)
                {
                    return found;
                }
            }
            return null;
        }

        private AliasNameCategoryState? FindRecursive(
            AliasNameCategoryState node,
            NodeId target)
        {
            if (node.NodeId == target)
            {
                return node;
            }
            var children = new List<BaseInstanceState>();
            node.GetChildren(SystemContext, children);
            foreach (BaseInstanceState child in children)
            {
                if (child is AliasNameCategoryState sub)
                {
                    AliasNameCategoryState? hit = FindRecursive(sub, target);
                    if (hit != null)
                    {
                        return hit;
                    }
                }
            }
            return null;
        }

        private static bool HasSecureAdminAccess(ISystemContext context)
        {
            if (context is SessionSystemContext { OperationContext: OperationContext op } session)
            {
                if (op.ChannelContext?.EndpointDescription?.SecurityMode !=
                    MessageSecurityMode.SignAndEncrypt)
                {
                    return false;
                }
                return session.UserIdentity?.GrantedRoleIds
                    .Contains(ObjectIds.WellKnownRole_SecurityAdmin) == true;
            }
            return false;
        }

        private static string ResolveNamespaceUri(AliasNameNodeManagerOptions? options)
        {
            return options?.NamespaceUri
                ?? new AliasNameNodeManagerOptions().NamespaceUri;
        }

        private static IAliasNameStoreRegistry? ResolveServerRegistry(IServerInternal server)
        {
            return (server as IAliasNameStoreRegistryProvider)?.AliasNameStoreRegistry;
        }

        private readonly IAliasNameStore m_store;
        private readonly AliasNameNodeManagerOptions m_options;
        private readonly ILogger m_aliasLogger;
        private readonly IAliasNameStoreRegistry? m_registry;
        // Always-available dispatcher that wraps just this manager's store
        // so the standalone manager works even when the host server does
        // not implement IAliasNameStoreRegistryProvider.
        private readonly AliasNameStoreRegistry m_localCategoryDispatcher;
        private readonly Dictionary<NodeId, AliasNameCategoryState> m_rootCategoryStates = [];
        private bool m_registeredWithServer;
        private uint m_nextNodeId;
        private readonly object m_lock = new();
    }
}
