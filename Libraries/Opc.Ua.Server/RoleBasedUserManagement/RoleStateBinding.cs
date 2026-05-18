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
                // Address-space materialization for dynamic roles is delegated
                // to integrators who subscribe to IRoleManager.RoleConfigurationChanged;
                // the well-known roles are already materialized by the
                // standard nodeset.
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

            return new RemoveRoleMethodStateResult
            {
                ServiceResult = m_roleManager.RemoveRole(roleNodeId)
            };
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
