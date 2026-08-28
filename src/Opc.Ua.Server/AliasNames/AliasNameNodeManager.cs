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
    /// address-space tree of <c>AliasNameCategoryType</c> instances — and,
    /// by default, one browsable <c>AliasNameType</c> instance per alias
    /// with its <c>AliasFor</c> references (Part 17 §6.2) — from an
    /// <see cref="IAliasNameStore"/>'s
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
    /// which it creates its category and alias instances; every
    /// <see cref="AliasNameCategoryDescriptor.NodeId"/> must lie in that
    /// namespace — a descriptor pointing anywhere else is skipped with a
    /// warning rather than claiming another manager's ids. Standard
    /// well-known categories owned by <c>DiagnosticsNodeManager</c> are
    /// not duplicated — only their methods are wired through the
    /// registry.
    /// </para>
    /// <para>
    /// The address space is built by the same shared
    /// <see cref="AliasNameNodeMaterializer"/> that
    /// <c>DiagnosticsNodeManager.MaterializeRegisteredAliasNameNodesAsync</c>
    /// uses for the standard well-known nodes, so both paths produce
    /// structurally identical Part 17 trees.
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
    public class AliasNameNodeManager : AsyncCustomNodeManager, IAliasNameMaterializerHost
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
            Store = store ?? throw new ArgumentNullException(nameof(store));
            Options = options ?? new AliasNameNodeManagerOptions();
            m_aliasLogger = server.Telemetry.CreateLogger<AliasNameNodeManager>();
            m_registry = ResolveServerRegistry(server);
            m_localCategoryDispatcher = new AliasNameStoreRegistry();
            m_localCategoryDispatcher.Register(Store);
            m_materializer = new AliasNameNodeMaterializer(
                this,
                m_localCategoryDispatcher,
                AuthorizeMutation,
                m_aliasLogger);
        }

        /// <summary>
        /// The backing <see cref="IAliasNameStore"/>.
        /// </summary>
        public IAliasNameStore Store { get; }

        /// <summary>
        /// The tunables in use.
        /// </summary>
        public AliasNameNodeManagerOptions Options { get; }

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

            var created = new List<NodeState>();
            var visited = new HashSet<NodeId>();

            await m_materializer.MaterializeStoreAsync(
                Store,
                externalReferences,
                created,
                visited,
                Options.MaterializeAliasNodes,
                cancellationToken).ConfigureAwait(false);

            m_aliasLogger.MaterializedAliasNameNodes(created.Count);

            if (Options.RegisterWithServerRegistry && m_registry != null)
            {
                try
                {
                    m_registry.Register(Store);
                    m_registeredWithServer = true;
                }
                catch (InvalidOperationException ex)
                {
                    m_aliasLogger.AliasNameStoreCouldNotBeRegisteredWithThe(ex);
                }
            }

            Store.Changed += OnStoreChanged;
        }

        /// <inheritdoc/>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Store.Changed -= OnStoreChanged;
                if (m_registeredWithServer && m_registry != null)
                {
                    m_registry.Unregister(Store);
                    m_registeredWithServer = false;
                }
                m_localCategoryDispatcher.Dispose();
            }
            base.Dispose(disposing);
        }

        private void OnStoreChanged(object? sender, AliasStoreChangedEventArgs e)
        {
            lock (m_lock)
            {
                // Every category the materializer touched is a registered
                // predefined node, so the direct lookup replaces the tree
                // walk older versions needed.
                AliasNameCategoryState? category =
                    FindPredefinedNode<AliasNameCategoryState>(e.CategoryId);
                if (category?.LastChange != null)
                {
                    category.LastChange.Value = e.LastChange;
                    category.LastChange.ClearChangeMasks(SystemContext, false);
                }
            }
        }

        private bool AuthorizeMutation(ISystemContext context)
        {
            return !Options.RequireSecurityAdminForMutations ||
                AliasNameMethodDispatcher.HasSecureAdminAccess(context);
        }

        ISystemContext IAliasNameMaterializerHost.SystemContext => SystemContext;

        ITypeTable IAliasNameMaterializerHost.TypeTree => Server.TypeTree;

        NamespaceTable IAliasNameMaterializerHost.NamespaceUris => Server.NamespaceUris;

        StringTable IAliasNameMaterializerHost.ServerUris => Server.ServerUris;

        ushort IAliasNameMaterializerHost.MaterializationNamespaceIndex => NamespaceIndex;

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
            if (!Options.LinkToStandardAliasesObject)
            {
                return;
            }

            // The standard Aliases (i=23470) object lives in another node
            // manager, so the forward half of the Organizes pair travels
            // through the external-references dictionary.
            AddExternalReference(
                ObjectIds.Aliases,
                ReferenceTypeIds.Organizes,
                isInverse: false,
                root.NodeId,
                externalReferences);
            if (!root.ReferenceExists(ReferenceTypeIds.Organizes, true, ObjectIds.Aliases))
            {
                root.AddReference(
                    ReferenceTypeIds.Organizes,
                    isInverse: true,
                    ObjectIds.Aliases);
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
            // Nothing to record: OnStoreChanged resolves the category by
            // NodeId at event time.
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

        private readonly ILogger m_aliasLogger;
        private readonly IAliasNameStoreRegistry? m_registry;
        private readonly AliasNameNodeMaterializer m_materializer;

        /// <summary>
        /// Always-available dispatcher that wraps just this manager's
        /// store so the standalone manager works even when the host
        /// server does not implement IAliasNameStoreRegistryProvider.
        /// </summary>
        private readonly AliasNameStoreRegistry m_localCategoryDispatcher;
        private bool m_registeredWithServer;
        private uint m_nextNodeId;
        private readonly Lock m_lock = new();
    }

    /// <summary>
    /// Source-generated log messages for AliasNameNodeManager.
    /// </summary>
    internal static partial class AliasNameNodeManagerLog
    {
        [LoggerMessage(EventId = ServerEventIds.AliasNameNodeManager + 0, Level = LogLevel.Warning,
            Message = "AliasNameStore could not be registered with the server-wide registry; standard " +
                "well-known Aliases methods will not dispatch through it.")]
        public static partial void AliasNameStoreCouldNotBeRegisteredWithThe(this ILogger logger, Exception ex);
    }
}
