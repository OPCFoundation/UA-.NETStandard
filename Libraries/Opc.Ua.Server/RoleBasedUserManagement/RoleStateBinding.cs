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
            m_logger = (nodeManager.Server as IServerInternal)?.Telemetry?.CreateLogger<RoleStateBinding>()
                ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<RoleStateBinding>.Instance;
        }

        /// <summary>
        /// Resolves the <see cref="RoleSetState"/> in <paramref name="nodeManager"/>'s
        /// predefined nodes, wires every child role to <paramref name="roleManager"/>
        /// and starts listening for <see cref="IRoleManager.RoleConfigurationChanged"/>
        /// events. Returns the binding instance so the caller can dispose it
        /// on server shutdown.
        /// </summary>
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
                Opc.Ua.ObjectIds.Server_ServerCapabilities_RoleSet);
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
                if (child is RoleState roleState)
                {
                    BindRoleState(roleState);
                }
            }

            m_roleManager.RoleConfigurationChanged += OnRoleConfigurationChanged;
        }

        private ushort ResolveDynamicNamespaceIndex()
        {
            string? firstOwned = m_nodeManager.NamespaceUris?.FirstOrDefault();
            if (string.IsNullOrEmpty(firstOwned))
            {
                return 0;
            }
            int idx = m_nodeManager.SystemContext.NamespaceUris.GetIndex(firstOwned!);
            return idx is >= 0 and <= ushort.MaxValue ? (ushort)idx : (ushort)0;
        }

        private void BindRoleState(RoleState roleState)
        {
            NodeId roleId = roleState.NodeId;
            m_boundRoles[roleId] = roleState;

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

        private NodeValueEventHandler WriteApplicationsExcludeHandler(NodeId roleId)
        {
            return (ISystemContext context, NodeState node, NumericRange indexRange,
                    QualifiedName dataEncoding, ref Variant value,
                    ref StatusCode statusCode, ref DateTimeUtc timestamp) =>
                OnWriteExclude(context, roleId, ref value, isApplications: true);
        }

        private NodeValueEventHandler WriteEndpointsExcludeHandler(NodeId roleId)
        {
            return (ISystemContext context, NodeState node, NumericRange indexRange,
                    QualifiedName dataEncoding, ref Variant value,
                    ref StatusCode statusCode, ref DateTimeUtc timestamp) =>
                OnWriteExclude(context, roleId, ref value, isApplications: false);
        }

        private NodeValueEventHandler WriteCustomConfigurationHandler(NodeId roleId)
        {
            return (ISystemContext context, NodeState node, NumericRange indexRange,
                    QualifiedName dataEncoding, ref Variant value,
                    ref StatusCode statusCode, ref DateTimeUtc timestamp) =>
                OnWriteCustomConfiguration(context, roleId, ref value);
        }

        private void SyncPropertiesFromManager(NodeId roleId, RoleState roleState)
        {
            RoleEntry? entry = m_roleManager.GetRole(roleId);
            if (entry == null)
            {
                return;
            }
            if (roleState.Identities != null)
            {
                roleState.Identities.Value = ArrayOf.Wrapped(entry.Identities.ToArray());
            }
            if (roleState.Applications != null)
            {
                roleState.Applications.Value = ArrayOf.Wrapped(entry.Applications.ToArray());
            }
            if (roleState.ApplicationsExclude != null)
            {
                roleState.ApplicationsExclude.Value = entry.ApplicationsExclude;
            }
            if (roleState.Endpoints != null)
            {
                roleState.Endpoints.Value = ArrayOf.Wrapped(entry.Endpoints.ToArray());
            }
            if (roleState.EndpointsExclude != null)
            {
                roleState.EndpointsExclude.Value = entry.EndpointsExclude;
            }
            if (roleState.CustomConfiguration != null)
            {
                roleState.CustomConfiguration.Value = entry.CustomConfiguration;
            }
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
                        context,
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
        /// allocated NodeId and allocate fresh NodeIds for each optional child
        /// via <see cref="ISystemContext.NodeIdFactory"/>.
        /// </remarks>
        private async ValueTask MaterializeDynamicRoleAsync(
            ISystemContext context,
            NodeId roleNodeId,
            string roleName,
            CancellationToken cancellationToken)
        {
            if (m_roleSet == null)
            {
                return;
            }

            ushort browseNs = roleNodeId.NamespaceIndex;

            RoleState roleState = context.CreateInstanceOfRoleType(
                parent: m_roleSet,
                browseName: new QualifiedName(roleName, browseNs));
            roleState.NodeId = roleNodeId;
            roleState.SymbolicName = roleName;
            roleState.DisplayName = new LocalizedText(roleName);

            // Optional method children — wire each one in via the typed
            // factories so the OnCallAsync delegates on the source-generated
            // method state become available for binding.
            roleState.AddIdentity = context.CreateInstanceOfAddIdentityMethodType(roleState);
            AssignChildNodeId(context, roleState.AddIdentity);

            roleState.RemoveIdentity = context.CreateInstanceOfRemoveIdentityMethodType(roleState);
            AssignChildNodeId(context, roleState.RemoveIdentity);

            roleState.AddApplication = context.CreateInstanceOfAddApplicationMethodType(roleState);
            AssignChildNodeId(context, roleState.AddApplication);

            roleState.RemoveApplication = context.CreateInstanceOfRemoveApplicationMethodType(roleState);
            AssignChildNodeId(context, roleState.RemoveApplication);

            roleState.AddEndpoint = context.CreateInstanceOfAddEndpointMethodType(roleState);
            AssignChildNodeId(context, roleState.AddEndpoint);

            roleState.RemoveEndpoint = context.CreateInstanceOfRemoveEndpointMethodType(roleState);
            AssignChildNodeId(context, roleState.RemoveEndpoint);

            // Optional property children — ApplicationsExclude / EndpointsExclude
            // / CustomConfiguration are all PropertyState<bool>.
            roleState.ApplicationsExclude = BuildBoolProperty(context, roleState, BrowseNames.ApplicationsExclude);
            roleState.EndpointsExclude = BuildBoolProperty(context, roleState, BrowseNames.EndpointsExclude);
            roleState.CustomConfiguration = BuildBoolProperty(context, roleState, BrowseNames.CustomConfiguration);

            // The Identities child is created by the factory but its NodeId
            // still points at the type definition — give it a dynamic id too
            // so it is addressable as an instance.
            if (roleState.Identities != null)
            {
                AssignChildNodeId(context, roleState.Identities);
            }

            // Attach to RoleSet so browse references are established before
            // the manager indexes the subtree.
            m_roleSet.AddChild(roleState);

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

        private static void AssignChildNodeId(ISystemContext context, BaseInstanceState? child)
        {
            if (child == null || context.NodeIdFactory == null)
            {
                return;
            }
            child.NodeId = context.NodeIdFactory.New(context, child);
        }

        private static PropertyState<bool> BuildBoolProperty(
            ISystemContext context,
            NodeState parent,
            string browseName)
        {
            // Property BrowseNames in the standard nodeset live in the OPC UA
            // base namespace (always index 0). Look it up rather than relying
            // on the internal Namespaces.OpcUa constant so this code stays
            // compatible if the runtime resequences known namespaces.
            int opcUaNsIndex = context.NamespaceUris.GetIndex("http://opcfoundation.org/UA/");
            ushort ns = opcUaNsIndex >= 0 ? (ushort)opcUaNsIndex : (ushort)0;
            PropertyState<bool> property = PropertyState<bool>.With<VariantBuilder>(parent);
            property.BrowseName = new QualifiedName(browseName, ns);
            property.DisplayName = new LocalizedText(browseName);
            property.SymbolicName = browseName;
            property.DataType = DataTypeIds.Boolean;
            property.ValueRank = ValueRanks.Scalar;
            AssignChildNodeId(context, property);
            return property;
        }

        private async ValueTask<AddIdentityMethodStateResult> OnAddIdentityAsync(
            ISystemContext context,
            MethodState method,
            NodeId roleId,
            IdentityMappingRuleType rule)
        {
            await Task.Yield();

            Variant ruleVariant = Variant.FromStructure(rule);
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

            Variant ruleVariant = Variant.FromStructure(rule);
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

            Variant uriVariant = Variant.From(applicationUri);
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

            Variant uriVariant = Variant.From(applicationUri);
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

            Variant epVariant = Variant.FromStructure(endpoint);
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

            Variant epVariant = Variant.FromStructure(endpoint);
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
    }
}
