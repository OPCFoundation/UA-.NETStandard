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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Binds the standard <c>RoleSet</c> object and its child <c>RoleType</c>
    /// instances to an <see cref="IRoleManager"/> using the source-generated
    /// typed proxies (<see cref="RoleSetState"/>, <see cref="RoleState"/>,
    /// <see cref="AddRoleMethodState"/>, etc.).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every typed method state's <c>OnCallAsync</c> delegate is wired through
    /// <see cref="RoleAuthorizationGate.CheckAdmin"/>, mutates the
    /// <see cref="IRoleManager"/>, reports a
    /// <c>RoleMappingRuleChangedAuditEventType</c> via
    /// <see cref="AuditEvents.ReportAuditRoleMappingRuleChangedEvent"/> and
    /// keeps the property values exposed on the address-space role node in
    /// sync with the manager state (so reads observe live values).
    /// </para>
    /// <para>
    /// The binding relies on <see cref="DiagnosticsNodeManager"/>'s
    /// <c>AddBehaviourToPredefinedNodeAsync</c> override having upgraded the
    /// passive <c>BaseObjectState</c> nodes representing <c>RoleSet</c> and
    /// each well-known role to the typed proxies before <see cref="Bind"/>
    /// is invoked.
    /// </para>
    /// </remarks>
    public sealed class RoleStateBinding : IDisposable
    {
        private readonly AsyncCustomNodeManager m_nodeManager;
        private readonly IRoleManager m_roleManager;
        private readonly IAuditEventServer? m_auditServer;
        private readonly ILogger m_logger;
        private readonly Dictionary<NodeId, RoleState> m_boundRoles = [];
        private RoleSetState? m_roleSet;
        private bool m_disposed;

        private RoleStateBinding(
            AsyncCustomNodeManager nodeManager,
            IRoleManager roleManager,
            IAuditEventServer? auditServer)
        {
            m_nodeManager = nodeManager;
            m_roleManager = roleManager;
            m_auditServer = auditServer;
            m_logger = nodeManager.Server?.Telemetry?.CreateLogger<RoleStateBinding>()
                ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<RoleStateBinding>.Instance;
        }

        /// <summary>
        /// Resolves the <see cref="RoleSetState"/> in <paramref name="nodeManager"/>'s
        /// predefined nodes, wires every child role to <paramref name="roleManager"/>
        /// and starts listening for <see cref="IRoleManager.RoleConfigurationChanged"/>
        /// events. Returns the binding instance so the caller can dispose it
        /// on server shutdown.
        /// </summary>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="nodeManager"/> is null.
        /// </exception>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="roleManager"/> is null.
        /// </exception>
        public static RoleStateBinding? Bind(
            AsyncCustomNodeManager nodeManager,
            IRoleManager roleManager,
            IAuditEventServer? auditServer)
        {
            if (nodeManager == null)
            {
                throw new ArgumentNullException(nameof(nodeManager));
            }
            if (roleManager == null)
            {
                throw new ArgumentNullException(nameof(roleManager));
            }

            RoleSetState? roleSet = nodeManager.FindPredefinedNode<RoleSetState>(
                ObjectIds.Server_ServerCapabilities_RoleSet);
            if (roleSet == null)
            {
                return null;
            }

            var binding = new RoleStateBinding(nodeManager, roleManager, auditServer);
            binding.Initialize(roleSet);
            return binding;
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            if (m_disposed)
            {
                return;
            }
            m_disposed = true;
            m_roleManager.RoleConfigurationChanged -= OnRoleConfigurationChanged;
        }

        private void Initialize(RoleSetState roleSet)
        {
            m_roleSet = roleSet;

            ushort dynamicNamespaceIndex = ResolveDynamicNamespaceIndex();

            if (roleSet.AddRole != null)
            {
                roleSet.AddRole.OnCall = null;
                roleSet.AddRole.OnCallAsync = (context, method, objectId, roleName, namespaceUri, ct) =>
                    OnAddRoleAsync(context, method, roleName, namespaceUri, dynamicNamespaceIndex, ct);
            }
            if (roleSet.RemoveRole != null)
            {
                roleSet.RemoveRole.OnCall = null;
                roleSet.RemoveRole.OnCallAsync = (context, method, objectId, roleNodeId, ct) =>
                    OnRemoveRoleAsync(context, method, roleNodeId, ct);
            }

            var children = new List<BaseInstanceState>();
            roleSet.GetChildren(m_nodeManager.SystemContext, children);
            foreach (BaseInstanceState child in children)
            {
                if (child is RoleState roleStateChild)
                {
                    BindRoleState(roleStateChild);
                }
            }

            // The standard nodeset wires the well-known role nodes to the
            // RoleSet via a HasComponent reference but GetChildren on the
            // typed RoleSetState proxy only enumerates the typed AddRole /
            // RemoveRole method children. Walk the raw references and look
            // each role up in the manager's PredefinedNodes index.
            var refs = new List<IReference>();
            roleSet.GetReferences(m_nodeManager.SystemContext, refs);
            foreach (IReference reference in refs)
            {
                if (reference.IsInverse ||
                    reference.ReferenceTypeId != ReferenceTypeIds.HasComponent)
                {
                    continue;
                }
                var targetId = ExpandedNodeId.ToNodeId(reference.TargetId,
                    m_nodeManager.SystemContext.NamespaceUris);
                if (targetId.IsNull)
                {
                    continue;
                }
                NodeState? target = m_nodeManager.FindPredefinedNode<NodeState>(targetId);
                if (target is RoleState roleStateRef && !m_boundRoles.ContainsKey(targetId))
                {
                    BindRoleState(roleStateRef);
                }
            }

            m_roleManager.RoleConfigurationChanged += OnRoleConfigurationChanged;
        }

        private ushort ResolveDynamicNamespaceIndex()
        {
            if (m_nodeManager.NamespaceUris == null)
            {
                return 0;
            }
            // The diagnostics manager owns both the OPC UA core namespace
            // and a dedicated "Diagnostics" namespace. Pick the first one
            // that is NOT the OPC UA core URI so dynamically allocated role
            // NodeIds don't collide with reserved Part 5 identifiers
            // (e.g. i=1 = Boolean in ns=0).
            foreach (string uri in m_nodeManager.NamespaceUris)
            {
                if (string.IsNullOrEmpty(uri))
                {
                    continue;
                }
                if (string.Equals(uri, "http://opcfoundation.org/UA/", StringComparison.Ordinal))
                {
                    continue;
                }
                int idx = m_nodeManager.SystemContext.NamespaceUris.GetIndex(uri);
                if (idx is > 0 and <= ushort.MaxValue)
                {
                    return (ushort)idx;
                }
            }
            return 0;
        }

        private void BindRoleState(RoleState roleState)
        {
            NodeId roleId = roleState.NodeId;
            m_boundRoles[roleId] = roleState;

            // When the role node was loaded from the standard nodeset XML,
            // its method/property children are plain MethodState /
            // PropertyState instances rather than the typed proxies the
            // source-gen RoleState exposes. Promote them so the typed
            // OnCallAsync / OnWriteValue plumbing applies uniformly.
            EnsureTypedChildrenAttached(roleState);

            if (roleState.AddIdentity != null)
            {
                roleState.AddIdentity.OnCall = null;
                roleState.AddIdentity.OnCallAsync = (context, method, objectId, rule, ct) =>
                    OnAddIdentityAsync(context, method, roleId, rule);
            }
            if (roleState.RemoveIdentity != null)
            {
                roleState.RemoveIdentity.OnCall = null;
                roleState.RemoveIdentity.OnCallAsync = (context, method, objectId, rule, ct) =>
                    OnRemoveIdentityAsync(context, method, roleId, rule);
            }
            if (roleState.AddApplication != null)
            {
                roleState.AddApplication.OnCall = null;
                roleState.AddApplication.OnCallAsync = (context, method, objectId, applicationUri, ct) =>
                    OnAddApplicationAsync(context, method, roleId, applicationUri);
            }
            if (roleState.RemoveApplication != null)
            {
                roleState.RemoveApplication.OnCall = null;
                roleState.RemoveApplication.OnCallAsync = (context, method, objectId, applicationUri, ct) =>
                    OnRemoveApplicationAsync(context, method, roleId, applicationUri);
            }
            if (roleState.AddEndpoint != null)
            {
                roleState.AddEndpoint.OnCall = null;
                roleState.AddEndpoint.OnCallAsync = (context, method, objectId, endpoint, ct) =>
                    OnAddEndpointAsync(context, method, roleId, endpoint);
            }
            if (roleState.RemoveEndpoint != null)
            {
                roleState.RemoveEndpoint.OnCall = null;
                roleState.RemoveEndpoint.OnCallAsync = (context, method, objectId, endpoint, ct) =>
                    OnRemoveEndpointAsync(context, method, roleId, endpoint);
            }

            if (roleState.ApplicationsExclude != null)
            {
                roleState.ApplicationsExclude.UserAccessLevel = AccessLevels.CurrentReadOrWrite;
                roleState.ApplicationsExclude.AccessLevel = AccessLevels.CurrentReadOrWrite;
                roleState.ApplicationsExclude.OnWriteValue = WriteApplicationsExcludeHandler(roleId);
            }
            if (roleState.EndpointsExclude != null)
            {
                roleState.EndpointsExclude.UserAccessLevel = AccessLevels.CurrentReadOrWrite;
                roleState.EndpointsExclude.AccessLevel = AccessLevels.CurrentReadOrWrite;
                roleState.EndpointsExclude.OnWriteValue = WriteEndpointsExcludeHandler(roleId);
            }
            if (roleState.CustomConfiguration != null)
            {
                roleState.CustomConfiguration.UserAccessLevel = AccessLevels.CurrentReadOrWrite;
                roleState.CustomConfiguration.AccessLevel = AccessLevels.CurrentReadOrWrite;
                roleState.CustomConfiguration.OnWriteValue = WriteCustomConfigurationHandler(roleId);
            }

            SyncPropertiesFromManager(roleId, roleState);
        }

        /// <summary>
        /// Promotes plain <see cref="MethodState"/> / <see cref="PropertyState"/>
        /// children of <paramref name="roleState"/> to the typed proxies
        /// exposed by the source-generated <see cref="RoleState"/> shape.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The standard nodeset XML loader instantiates each child as the
        /// generic base type. The <see cref="RoleState.AddIdentity"/> and
        /// related typed properties remain <see langword="null"/> unless we
        /// explicitly construct an <c>AddIdentityMethodState</c> (etc.)
        /// from the existing passive child and assign it back. Without this
        /// promotion the typed <c>OnCallAsync</c> delegates that
        /// <see cref="BindRoleState"/> wires up are never reached and the
        /// server returns <c>BadNotImplemented</c>.
        /// </para>
        /// <para>
        /// Children that are already typed (e.g. ones materialized via
        /// <see cref="MaterializeDynamicRoleAsync"/> for dynamically added
        /// roles) are left untouched.
        /// </para>
        /// </remarks>
        private void EnsureTypedChildrenAttached(RoleState roleState)
        {
            ISystemContext context = m_nodeManager.SystemContext;

            roleState.AddIdentity ??= PromoteMethodChild(
                    m_nodeManager, context, roleState, BrowseNames.AddIdentity,
                    parent => new AddIdentityMethodState(parent));
            roleState.RemoveIdentity ??= PromoteMethodChild(
                    m_nodeManager, context, roleState, BrowseNames.RemoveIdentity,
                    parent => new RemoveIdentityMethodState(parent));
            roleState.AddApplication ??= PromoteMethodChild(
                    m_nodeManager, context, roleState, BrowseNames.AddApplication,
                    parent => new AddApplicationMethodState(parent));
            roleState.RemoveApplication ??= PromoteMethodChild(
                    m_nodeManager, context, roleState, BrowseNames.RemoveApplication,
                    parent => new RemoveApplicationMethodState(parent));
            roleState.AddEndpoint ??= PromoteMethodChild(
                    m_nodeManager, context, roleState, BrowseNames.AddEndpoint,
                    parent => new AddEndpointMethodState(parent));
            roleState.RemoveEndpoint ??= PromoteMethodChild(
                    m_nodeManager, context, roleState, BrowseNames.RemoveEndpoint,
                    parent => new RemoveEndpointMethodState(parent));

            roleState.ApplicationsExclude ??= PromoteBoolPropertyChild(
                    m_nodeManager, context, roleState, BrowseNames.ApplicationsExclude);
            roleState.EndpointsExclude ??= PromoteBoolPropertyChild(
                    m_nodeManager, context, roleState, BrowseNames.EndpointsExclude);
            roleState.CustomConfiguration ??= PromoteBoolPropertyChild(
                    m_nodeManager, context, roleState, BrowseNames.CustomConfiguration);
        }

        private static TTyped? PromoteMethodChild<TTyped>(
            AsyncCustomNodeManager manager,
            ISystemContext context,
            NodeState parent,
            string browseName,
            Func<NodeState, TTyped> factory)
            where TTyped : MethodState
        {
            BaseInstanceState? passive = FindChildByBrowseName(context, parent, browseName);
            if (passive is not MethodState passiveMethod)
            {
                return null;
            }
            if (passiveMethod is TTyped typedAlready)
            {
                return typedAlready;
            }

            // Construct the typed proxy and re-bind it in place of the
            // passive method. Create(context, source) copies the NodeId,
            // BrowseName, references, etc. so the existing method
            // dispatch on the original NodeId continues to work.
            TTyped active = factory(parent);
            active.Create(context, passiveMethod);
            parent.ReplaceChild(context, active);
            // The manager's PredefinedNodes index still points at the
            // passive instance. Refresh it so server-side method dispatch
            // (which looks up by NodeId) lands on the typed proxy.
            manager.ReplacePredefinedNode(active.NodeId, active);
            return active;
        }

        private static PropertyState<bool>? PromoteBoolPropertyChild(
            AsyncCustomNodeManager manager,
            ISystemContext context,
            NodeState parent,
            string browseName)
        {
            BaseInstanceState? passive = FindChildByBrowseName(context, parent, browseName);
            if (passive == null)
            {
                return null;
            }
            if (passive is PropertyState<bool> typedAlready)
            {
                return typedAlready;
            }
            if (passive is not BaseVariableState passiveVar)
            {
                return null;
            }

            var active = PropertyState<bool>.With<VariantBuilder>(parent);
            active.Create(context, passiveVar);
            parent.ReplaceChild(context, active);
            manager.ReplacePredefinedNode(active.NodeId, active);
            return active;
        }

        private static BaseInstanceState? FindChildByBrowseName(
            ISystemContext context, NodeState parent, string browseName)
        {
            var children = new List<BaseInstanceState>();
            parent.GetChildren(context, children);
            for (int ii = 0; ii < children.Count; ii++)
            {
                BaseInstanceState child = children[ii];
                if (child.BrowseName.Name == browseName)
                {
                    return child;
                }
            }
            return null;
        }

        private NodeValueEventHandler WriteApplicationsExcludeHandler(NodeId roleId)
        {
            return (context, node, indexRange,
                    dataEncoding, ref value,
                    ref statusCode, ref timestamp) =>
                OnWriteExclude(context, roleId, ref value, isApplications: true);
        }

        private NodeValueEventHandler WriteEndpointsExcludeHandler(NodeId roleId)
        {
            return (context, node, indexRange,
                    dataEncoding, ref value,
                    ref statusCode, ref timestamp) =>
                OnWriteExclude(context, roleId, ref value, isApplications: false);
        }

        private NodeValueEventHandler WriteCustomConfigurationHandler(NodeId roleId)
        {
            return (context, node, indexRange,
                    dataEncoding, ref value,
                    ref statusCode, ref timestamp) =>
                OnWriteCustomConfiguration(context, roleId, ref value);
        }

        private void SyncPropertiesFromManager(NodeId roleId, RoleState roleState)
        {
            RoleEntry? entry = m_roleManager.GetRole(roleId);
            if (entry == null)
            {
                return;
            }
            roleState.Identities?.Value = ArrayOf.Wrapped(entry.Identities.ToArray());
            roleState.Applications?.Value = ArrayOf.Wrapped(entry.Applications.ToArray());
            roleState.ApplicationsExclude?.Value = entry.ApplicationsExclude;
            roleState.Endpoints?.Value = ArrayOf.Wrapped(entry.Endpoints.ToArray());
            roleState.EndpointsExclude?.Value = entry.EndpointsExclude;
            roleState.CustomConfiguration?.Value = entry.CustomConfiguration;
            roleState.ClearChangeMasks(m_nodeManager.SystemContext, includeChildren: true);
        }

        private void OnRoleConfigurationChanged(object? sender, RoleConfigurationChangedEventArgs e)
        {
            if (m_disposed)
            {
                return;
            }
            if (e.Kind == RoleConfigurationChangeKind.RoleRemoved)
            {
                m_boundRoles.Remove(e.RoleId);
                return;
            }
            if (m_boundRoles.TryGetValue(e.RoleId, out RoleState? roleState))
            {
                SyncPropertiesFromManager(e.RoleId, roleState);
            }
        }

        private async ValueTask<AddRoleMethodStateResult> OnAddRoleAsync(
            ISystemContext context,
            MethodState method,
            string roleName,
            string namespaceUri,
            ushort dynamicNamespaceIndex,
            CancellationToken cancellationToken)
        {
            await Task.Yield();

            var result = new AddRoleMethodStateResult { RoleNodeId = NodeId.Null };

            ServiceResult auth = RoleAuthorizationGate.CheckAdmin(context);
            if (ServiceResult.IsBad(auth))
            {
                result.ServiceResult = auth;
                return result;
            }

            ServiceResult add = m_roleManager.AddRole(
                roleName,
                namespaceUri,
                context.NamespaceUris,
                dynamicNamespaceIndex,
                out NodeId newRoleId);

            if (ServiceResult.IsGood(add))
            {
                result.RoleNodeId = newRoleId;

                // Materialize the RoleType subtree into the address space so
                // browsers and method callers see the dynamic role straight
                // away. Failures here are logged but not surfaced as a status
                // code so the RoleManager state stays consistent with what
                // AddRole reported.
                try
                {
                    await MaterializeDynamicRoleAsync(
                        newRoleId,
                        roleName,
                        cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    m_logger.LogWarning(ex,
                        "AddRole({RoleName}) succeeded in the RoleManager but address-space materialization failed.",
                        roleName);
                }
            }
            result.ServiceResult = add;
            return result;
        }

        private async ValueTask<RemoveRoleMethodStateResult> OnRemoveRoleAsync(
            ISystemContext context,
            MethodState method,
            NodeId roleNodeId,
            CancellationToken cancellationToken)
        {
            await Task.Yield();

            ServiceResult auth = RoleAuthorizationGate.CheckAdmin(context);
            if (ServiceResult.IsBad(auth))
            {
                return new RemoveRoleMethodStateResult { ServiceResult = auth };
            }

            ServiceResult remove = m_roleManager.RemoveRole(roleNodeId);

            if (ServiceResult.IsGood(remove))
            {
                // Drop the address-space subtree so subsequent browses don't
                // see the deleted role. Mirror the AddRole behaviour: failures
                // are logged and swallowed so the RoleManager state stays
                // authoritative.
                try
                {
                    await DematerializeDynamicRoleAsync(roleNodeId, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    m_logger.LogWarning(ex,
                        "RemoveRole({RoleId}) succeeded in the RoleManager but address-space removal failed.",
                        roleNodeId);
                }
            }

            return new RemoveRoleMethodStateResult { ServiceResult = remove };
        }

        /// <summary>
        /// Materializes a dynamically added <c>RoleType</c> instance
        /// underneath the bound <see cref="RoleSetState"/> using the
        /// source-generated typed proxies.
        /// </summary>
        /// <remarks>
        /// The well-known roles ship pre-built in the standard nodeset; this
        /// method only fires for roles created at runtime via
        /// <see cref="IRoleManager.AddRole"/>. The factory
        /// <c>CreateInstanceOfRoleType</c> sets the well-known RoleType NodeId
        /// (15620) as the default — we overwrite it with the dynamic role's
        /// allocated NodeId and rebase its mandatory and optional descendants
        /// through the owning NodeManager's <see cref="ISystemContext.NodeIdFactory"/>.
        /// </remarks>
        private async ValueTask MaterializeDynamicRoleAsync(
            NodeId roleNodeId,
            string roleName,
            CancellationToken cancellationToken)
        {
            if (m_roleSet == null)
            {
                return;
            }

            ushort browseNs = roleNodeId.NamespaceIndex;
            // The request context is not guaranteed to carry a NodeIdFactory.
            ServerSystemContext nodeContext = m_nodeManager.SystemContext.Copy();
            nodeContext.NodeIdFactory = new DynamicRoleNodeIdFactory();

            RoleState roleState = nodeContext.CreateInstanceOfRoleType(
                parent: m_roleSet,
                browseName: new QualifiedName(roleName, browseNs));
            roleState.NodeId = roleNodeId;
            roleState.SymbolicName = roleName;
            roleState.DisplayName = new LocalizedText(roleName);

            // Optional method children — wire each one in via the typed
            // factories so the OnCallAsync delegates on the source-generated
            // method state become available for binding. Each child must
            // be added BOTH to the typed property (for binding lookups)
            // AND to the base m_children collection (so Browse traversal
            // sees the HasComponent reference). Browse walks GetReferences
            // (not m_children), so we also need to add explicit forward
            // and inverse references for every materialized child.
            // Pass the spec-defined BrowseName so the standard browse-path
            // lookups (e.g. "/AddIdentity") match the instance.
            const ushort opcUaNs = 0;
            roleState.AddIdentity = nodeContext.CreateInstanceOfAddIdentityMethodType(
                roleState, new QualifiedName(BrowseNames.AddIdentity, opcUaNs));
            AssignChildNodeId(nodeContext, roleState.AddIdentity);
            LinkChild(roleState, roleState.AddIdentity);

            roleState.RemoveIdentity = nodeContext.CreateInstanceOfRemoveIdentityMethodType(
                roleState, new QualifiedName(BrowseNames.RemoveIdentity, opcUaNs));
            AssignChildNodeId(nodeContext, roleState.RemoveIdentity);
            LinkChild(roleState, roleState.RemoveIdentity);

            roleState.AddApplication = nodeContext.CreateInstanceOfAddApplicationMethodType(
                roleState, new QualifiedName(BrowseNames.AddApplication, opcUaNs));
            AssignChildNodeId(nodeContext, roleState.AddApplication);
            LinkChild(roleState, roleState.AddApplication);

            roleState.RemoveApplication = nodeContext.CreateInstanceOfRemoveApplicationMethodType(
                roleState, new QualifiedName(BrowseNames.RemoveApplication, opcUaNs));
            AssignChildNodeId(nodeContext, roleState.RemoveApplication);
            LinkChild(roleState, roleState.RemoveApplication);

            roleState.AddEndpoint = nodeContext.CreateInstanceOfAddEndpointMethodType(
                roleState, new QualifiedName(BrowseNames.AddEndpoint, opcUaNs));
            AssignChildNodeId(nodeContext, roleState.AddEndpoint);
            LinkChild(roleState, roleState.AddEndpoint);

            roleState.RemoveEndpoint = nodeContext.CreateInstanceOfRemoveEndpointMethodType(
                roleState, new QualifiedName(BrowseNames.RemoveEndpoint, opcUaNs));
            AssignChildNodeId(nodeContext, roleState.RemoveEndpoint);
            LinkChild(roleState, roleState.RemoveEndpoint);

            // Optional property children — ApplicationsExclude / EndpointsExclude
            // / CustomConfiguration are all PropertyState<bool>. Use the
            // HasProperty reference type rather than HasComponent so the
            // browse classification matches the standard nodeset.
            roleState.AddApplicationsExclude(nodeContext);
            LinkChild(roleState, roleState.ApplicationsExclude, ReferenceTypeIds.HasProperty);
            roleState.AddEndpointsExclude(nodeContext);
            LinkChild(roleState, roleState.EndpointsExclude, ReferenceTypeIds.HasProperty);
            roleState.AddCustomConfiguration(nodeContext);
            LinkChild(roleState, roleState.CustomConfiguration, ReferenceTypeIds.HasProperty);

            // The Identities child is created by the factory but its NodeId
            // still points at the type definition — give it a dynamic id too
            // so it is addressable as an instance.
            if (roleState.Identities != null)
            {
                AssignChildNodeId(nodeContext, roleState.Identities);
            }

            // Attach to RoleSet so browse references are established before
            // the manager indexes the subtree. AddChild only updates the
            // typed children collection; the actual HasComponent reference
            // pair that Browse traverses is added explicitly so the new
            // role shows up when clients walk the RoleSet's children.
            m_roleSet.AddChild(roleState);
            m_roleSet.AddReference(ReferenceTypeIds.HasComponent, isInverse: false, roleState.NodeId);
            roleState.AddReference(ReferenceTypeIds.HasComponent, isInverse: true, m_roleSet.NodeId);

            await m_nodeManager.AddPredefinedNodeAsync(roleState, cancellationToken)
                .ConfigureAwait(false);

            // Now that the state is in PredefinedNodes, wire the OnCallAsync
            // delegates and OnWriteValue handlers via the existing binding
            // path. This keeps materialization and wiring identical to the
            // well-known role path.
            BindRoleState(roleState);

            m_logger.LogDebug(
                "Materialized dynamic role {RoleName} ({RoleId}) under RoleSet.",
                roleName, roleNodeId);
        }

        private async ValueTask DematerializeDynamicRoleAsync(
            NodeId roleNodeId,
            CancellationToken cancellationToken)
        {
            // Drop the binding bookkeeping first so any concurrent
            // RoleConfigurationChanged listener walks the new state.
            m_boundRoles.Remove(roleNodeId);

            await m_nodeManager.DeleteNodeAsync(
                    m_nodeManager.SystemContext,
                    roleNodeId,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        private static void AssignChildNodeId(
            ServerSystemContext context,
            BaseInstanceState? child)
        {
            if (child == null)
            {
                return;
            }
            if (context.NodeIdFactory == null)
            {
                throw new InvalidOperationException(
                    "Dynamic role materialization requires a NodeIdFactory.");
            }
            NodeId previousNodeId = context.AssignInstanceNodeId(child);
            context.AssignInstanceChildNodeIds(
                child,
                previousNodeId,
                child.Parent ?? child);
        }

        /// <summary>
        /// Attaches <paramref name="child"/> to <paramref name="parent"/> by
        /// registering the typed child link (<c>AddChild</c>) and adding
        /// explicit forward + inverse references so Browse traversal sees
        /// the new HasComponent / HasProperty relationship.
        /// </summary>
        private static void LinkChild(NodeState parent, BaseInstanceState? child, NodeId? referenceTypeId = null)
        {
            if (child == null || child.NodeId.IsNull)
            {
                return;
            }
            NodeId refTypeId = referenceTypeId ?? ReferenceTypeIds.HasComponent;
            parent.AddChild(child);
            parent.AddReference(refTypeId, isInverse: false, child.NodeId);
            child.AddReference(refTypeId, isInverse: true, parent.NodeId);
        }

        private async ValueTask<AddIdentityMethodStateResult> OnAddIdentityAsync(
            ISystemContext context,
            MethodState method,
            NodeId roleId,
            IdentityMappingRuleType rule)
        {
            await Task.Yield();

            var ruleVariant = Variant.FromStructure(rule);
            var result = new AddIdentityMethodStateResult();
            ServiceResult auth = RoleAuthorizationGate.CheckAdmin(context);
            if (ServiceResult.IsBad(auth))
            {
                result.ServiceResult = auth;
                ReportAudit(context, roleId, method, [ruleVariant], success: false);
                return result;
            }

            ServiceResult sr = m_roleManager.AddIdentity(roleId, rule);
            result.ServiceResult = sr;
            ReportAudit(context, roleId, method, [ruleVariant], ServiceResult.IsGood(sr));
            return result;
        }

        private async ValueTask<RemoveIdentityMethodStateResult> OnRemoveIdentityAsync(
            ISystemContext context,
            MethodState method,
            NodeId roleId,
            IdentityMappingRuleType rule)
        {
            await Task.Yield();

            var ruleVariant = Variant.FromStructure(rule);
            var result = new RemoveIdentityMethodStateResult();
            ServiceResult auth = RoleAuthorizationGate.CheckAdmin(context);
            if (ServiceResult.IsBad(auth))
            {
                result.ServiceResult = auth;
                ReportAudit(context, roleId, method, [ruleVariant], success: false);
                return result;
            }

            ServiceResult sr = m_roleManager.RemoveIdentity(roleId, rule);
            result.ServiceResult = sr;
            ReportAudit(context, roleId, method, [ruleVariant], ServiceResult.IsGood(sr));
            return result;
        }

        private async ValueTask<AddApplicationMethodStateResult> OnAddApplicationAsync(
            ISystemContext context,
            MethodState method,
            NodeId roleId,
            string applicationUri)
        {
            await Task.Yield();

            var uriVariant = Variant.From(applicationUri);
            var result = new AddApplicationMethodStateResult();
            ServiceResult auth = RoleAuthorizationGate.CheckAdmin(context);
            if (ServiceResult.IsBad(auth))
            {
                result.ServiceResult = auth;
                ReportAudit(context, roleId, method, [uriVariant], success: false);
                return result;
            }

            ServiceResult sr = m_roleManager.AddApplication(roleId, applicationUri);
            result.ServiceResult = sr;
            ReportAudit(context, roleId, method, [uriVariant], ServiceResult.IsGood(sr));
            return result;
        }

        private async ValueTask<RemoveApplicationMethodStateResult> OnRemoveApplicationAsync(
            ISystemContext context,
            MethodState method,
            NodeId roleId,
            string applicationUri)
        {
            await Task.Yield();

            var uriVariant = Variant.From(applicationUri);
            var result = new RemoveApplicationMethodStateResult();
            ServiceResult auth = RoleAuthorizationGate.CheckAdmin(context);
            if (ServiceResult.IsBad(auth))
            {
                result.ServiceResult = auth;
                ReportAudit(context, roleId, method, [uriVariant], success: false);
                return result;
            }

            ServiceResult sr = m_roleManager.RemoveApplication(roleId, applicationUri);
            result.ServiceResult = sr;
            ReportAudit(context, roleId, method, [uriVariant], ServiceResult.IsGood(sr));
            return result;
        }

        private async ValueTask<AddEndpointMethodStateResult> OnAddEndpointAsync(
            ISystemContext context,
            MethodState method,
            NodeId roleId,
            EndpointType endpoint)
        {
            await Task.Yield();

            var epVariant = Variant.FromStructure(endpoint);
            var result = new AddEndpointMethodStateResult();
            ServiceResult auth = RoleAuthorizationGate.CheckAdmin(context);
            if (ServiceResult.IsBad(auth))
            {
                result.ServiceResult = auth;
                ReportAudit(context, roleId, method, [epVariant], success: false);
                return result;
            }

            ServiceResult sr = m_roleManager.AddEndpoint(roleId, endpoint);
            result.ServiceResult = sr;
            ReportAudit(context, roleId, method, [epVariant], ServiceResult.IsGood(sr));
            return result;
        }

        private async ValueTask<RemoveEndpointMethodStateResult> OnRemoveEndpointAsync(
            ISystemContext context,
            MethodState method,
            NodeId roleId,
            EndpointType endpoint)
        {
            await Task.Yield();

            var epVariant = Variant.FromStructure(endpoint);
            var result = new RemoveEndpointMethodStateResult();
            ServiceResult auth = RoleAuthorizationGate.CheckAdmin(context);
            if (ServiceResult.IsBad(auth))
            {
                result.ServiceResult = auth;
                ReportAudit(context, roleId, method, [epVariant], success: false);
                return result;
            }

            ServiceResult sr = m_roleManager.RemoveEndpoint(roleId, endpoint);
            result.ServiceResult = sr;
            ReportAudit(context, roleId, method, [epVariant], ServiceResult.IsGood(sr));
            return result;
        }

        private ServiceResult OnWriteExclude(
            ISystemContext context, NodeId roleId, ref Variant value, bool isApplications)
        {
            ServiceResult auth = RoleAuthorizationGate.CheckAdmin(context);
            if (ServiceResult.IsBad(auth))
            {
                return auth;
            }
            if (!value.TryGetValue(out bool b))
            {
                return new ServiceResult(StatusCodes.BadTypeMismatch);
            }
            return isApplications
                ? m_roleManager.SetApplicationsExclude(roleId, b)
                : m_roleManager.SetEndpointsExclude(roleId, b);
        }

        private ServiceResult OnWriteCustomConfiguration(
            ISystemContext context, NodeId roleId, ref Variant value)
        {
            ServiceResult auth = RoleAuthorizationGate.CheckAdmin(context);
            if (ServiceResult.IsBad(auth))
            {
                return auth;
            }
            if (!value.TryGetValue(out bool b))
            {
                return new ServiceResult(StatusCodes.BadTypeMismatch);
            }
            return m_roleManager.SetCustomConfiguration(roleId, b);
        }

        private void ReportAudit(
            ISystemContext context,
            NodeId roleId,
            MethodState method,
            Variant[] inputArguments,
            bool success)
        {
            if (m_auditServer == null)
            {
                return;
            }
            m_auditServer.ReportAuditRoleMappingRuleChangedEvent(
                context,
                roleId,
                method,
                ArrayOf.Wrapped(inputArguments),
                success,
                m_logger);
        }

        private sealed class DynamicRoleNodeIdFactory : INodeIdFactory
        {
            public NodeId New(ISystemContext context, NodeState node)
            {
                if (node is BaseInstanceState instance &&
                    instance.Parent != null)
                {
                    string parentId = instance.Parent.NodeId.IdentifierAsString;
                    string childName = string.IsNullOrEmpty(instance.SymbolicName)
                        ? instance.BrowseName.Name ?? instance.GetType().Name
                        : instance.SymbolicName;
                    return new NodeId(
                        $"{parentId}_{childName}",
                        instance.Parent.NodeId.NamespaceIndex);
                }

                return node.NodeId;
            }
        }
    }
}
