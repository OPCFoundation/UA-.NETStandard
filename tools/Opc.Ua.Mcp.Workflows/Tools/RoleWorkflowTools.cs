/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Opc.Ua.Client;
using Opc.Ua.Client.Roles;
using Descriptions = Opc.Ua.Mcp.WorkflowParameterDescriptions;

namespace Opc.Ua.Mcp
{
    /// <summary>
    /// Explicit Part 18 role administration, preserving remote authorization and exact method status.
    /// </summary>
    [McpServerToolType]
    public sealed class RoleWorkflowTools
    {
        /// <summary>
        /// Creates role tools over the existing host session registry.
        /// </summary>
        public RoleWorkflowTools(
            OpcUaSessionManager sessions, WorkflowOptions options, IWorkflowSessionFactory? scopes = null)
        {
            m_sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
            m_options = options ?? throw new ArgumentNullException(nameof(options));
            m_scopes = scopes ?? new WorkflowSessionFactory();
            options.Validate();
        }

        /// <summary>
        /// Enumerates the actual RoleSet with bounded browse-page cleanup.
        /// </summary>
        [McpServerTool(Name = "roles_list", ReadOnly = true)]
        [Description("List the actual server RoleSet. Returns exact role ids/type namespaces and live page metadata. " +
            "Example: maxResults=25. Does not materialize the complete RoleSet.")]
        public Task<CallToolResult> ListAsync(
            [Description(Descriptions.MaxResults)] int maxResults = 100,
            [Description(Descriptions.Offset)] int offset = 0,
            [Description(Descriptions.SessionName)] string? sessionName = null,
            CancellationToken ct = default)
        {
            return McpCompanionTools.ExecuteAsync(async token =>
            {
                WorkflowJson.Page(m_options, maxResults, offset);
                IWorkflowSessionScope scope = await m_scopes.CaptureAsync(
                    m_sessions.GetSessionInfoOrThrow(sessionName), token).ConfigureAwait(false);
                await using ConfiguredAsyncDisposable lease = scope.ConfigureAwait(false);
                ISession session = scope.Access.Session;
                return await McpCompanionTools.PageAsync(RolesAsync(session, token),
                    reference => WorkflowJson.Reference(session, reference), offset, maxResults, token)
                    .ConfigureAwait(false);
            }, ct);
        }

        /// <summary>
        /// Reads one role with quality and optional-property absence retained.
        /// </summary>
        [McpServerTool(Name = "roles_read", ReadOnly = true)]
        [Description("Read one RoleType instance. Returns exact identity, browse name, mappings and flags with " +
            "DataValue quality and optional absence. Example: roleId='i=15644'.")]
        public Task<CallToolResult> ReadAsync(
            [Description(Descriptions.RoleId)] string roleId,
            [Description(Descriptions.SessionName)] string? sessionName = null,
            CancellationToken ct = default)
        {
            return McpCompanionTools.ExecuteAsync(async token =>
            {
                IWorkflowSessionScope scope = await m_scopes.CaptureAsync(
                    m_sessions.GetSessionInfoOrThrow(sessionName), token).ConfigureAwait(false);
                await using ConfiguredAsyncDisposable lease = scope.ConfigureAwait(false);
                ISession session = scope.Access.Session;
                NodeId node = WorkflowJson.Node(session, roleId);
                _ = await McpModelAccess.RequireTypeAsync(session, node, ObjectTypeIds.RoleType, token)
                    .ConfigureAwait(false);
                RoleInfo role = await new RoleManagementClient(session, m_options.MaxItems).ReadRoleAsync(node, token)
                    .ConfigureAwait(false);
                JsonObject result = await WorkflowJson.PropertiesAsync(session, node,
                    [
                        new QualifiedName(BrowseNames.Identities), new QualifiedName(BrowseNames.Applications),
                        new QualifiedName(BrowseNames.ApplicationsExclude), new QualifiedName(BrowseNames.Endpoints),
                        new QualifiedName(BrowseNames.EndpointsExclude),
                        new QualifiedName(BrowseNames.CustomConfiguration)
                    ], token).ConfigureAwait(false);
                result["expandedNodeId"] = NodeId.ToExpandedNodeId(role.RoleId, session.NamespaceUris).ToString();
                result["browseName"] = role.BrowseName.Name;
                result["browseNamespaceUri"] = session.NamespaceUris.GetString(role.BrowseName.NamespaceIndex);
                result["consistency"] = "live";
                return result;
            }, ct);
        }

        /// <summary>
        /// Creates exactly one role; no mappings or permissions are added implicitly.
        /// </summary>
        [McpServerTool(Name = "roles_create", Destructive = true)]
        [Description("Create one role in the standard RoleSet. Requires host IdentityAdministration, caller grant " +
            "and SignAndEncrypt; server SecurityAdmin remains required. Returns actual status/new role id. " +
            "Example: roleName='Maintenance', namespaceUri='urn:plant:roles'.")]
        public Task<CallToolResult> CreateAsync(
            [Description(Descriptions.RoleName)] string roleName,
            [Description(Descriptions.NamespaceUri)] string namespaceUri,
            [Description(Descriptions.SessionName)] string? sessionName = null,
            CancellationToken ct = default)
        {
            return McpCompanionTools.ExecuteAsync(async token =>
            {
                WorkflowJson.Text(roleName, 256);
                WorkflowJson.Text(namespaceUri);
                IWorkflowSessionScope scope = await WorkflowJson.IdentityScopeAsync(
                    m_sessions, m_scopes, sessionName, token).ConfigureAwait(false);
                await using ConfiguredAsyncDisposable lease = scope.ConfigureAwait(false);
                ISession session = scope.Access.Session;
                var proxy = new RoleSetTypeClient(
                    session, ObjectIds.Server_ServerCapabilities_RoleSet, session.MessageContext.Telemetry);
                CallMethodResult call = await proxy.CallWithResultAsync(
                    MethodIds.Server_ServerCapabilities_RoleSet_AddRole,
                    [Variant.From(roleName), Variant.From(namespaceUri)], token).ConfigureAwait(false);
                JsonObject result = WorkflowJson.Method(session, proxy.ObjectId, call);
                if (!StatusCode.IsBad(call.StatusCode) &&
                    call.OutputArguments.Count == 1 &&
                    call.OutputArguments[0].TryGetValue(out NodeId created))
                {
                    result["createdRole"] = WorkflowJson.Identity(session, created);
                }
                return result;
            }, ct);
        }

        /// <summary>
        /// Removes exactly one role, allowing the server to enforce reserved-role and lockout restrictions.
        /// </summary>
        [McpServerTool(Name = "roles_remove", Destructive = true)]
        [Description("Remove one role via the standard RoleSet. Returns the actual refusal or success status. " +
            "Reserved roles and self-lockout are never bypassed. Example: roleId='ns=2;s=Maintenance'.")]
        public Task<CallToolResult> RemoveAsync(
            [Description(Descriptions.RoleId)] string roleId,
            [Description(Descriptions.SessionName)] string? sessionName = null,
            CancellationToken ct = default)
        {
            return McpCompanionTools.ExecuteAsync(async token =>
            {
                IWorkflowSessionScope scope = await WorkflowJson.IdentityScopeAsync(
                    m_sessions, m_scopes, sessionName, token).ConfigureAwait(false);
                await using ConfiguredAsyncDisposable lease = scope.ConfigureAwait(false);
                ISession session = scope.Access.Session;
                NodeId role = WorkflowJson.Node(session, roleId);
                _ = await McpModelAccess.RequireTypeAsync(session, role, ObjectTypeIds.RoleType, token)
                    .ConfigureAwait(false);
                var proxy = new RoleSetTypeClient(
                    session, ObjectIds.Server_ServerCapabilities_RoleSet, session.MessageContext.Telemetry);
                return WorkflowJson.Method(session, role, await proxy.CallWithResultAsync(
                    MethodIds.Server_ServerCapabilities_RoleSet_RemoveRole,
                    [Variant.From(role)], token).ConfigureAwait(false));
            }, ct);
        }

        /// <summary>
        /// Adds one identity rule.
        /// </summary>
        [McpServerTool(Name = "roles_add_identity", Destructive = true)]
        [Description("Add exactly one typed identity mapping; returns actual method/argument statuses. " +
            "Example: criteriaType='UserName', criteria='alice'. Requires identity administration.")]
        public Task<CallToolResult> AddIdentityAsync(
            [Description(Descriptions.RoleId)] string roleId,
            [Description(Descriptions.CriteriaType)] IdentityCriteriaType criteriaType,
            [Description(Descriptions.Criteria)] string criteria,
            [Description(Descriptions.SessionName)] string? sessionName = null,
            CancellationToken ct = default)
        {
            return IdentityAsync(roleId, criteriaType, criteria, true, sessionName, ct);
        }

        /// <summary>
        /// Removes one identity rule.
        /// </summary>
        [McpServerTool(Name = "roles_remove_identity", Destructive = true)]
        [Description("Remove exactly the selected identity mapping, without clearing other rules. " +
            "Example: criteriaType='UserName', criteria='alice'. Returns actual status.")]
        public Task<CallToolResult> RemoveIdentityAsync(
            [Description(Descriptions.RoleId)] string roleId,
            [Description(Descriptions.CriteriaType)] IdentityCriteriaType criteriaType,
            [Description(Descriptions.Criteria)] string criteria,
            [Description(Descriptions.SessionName)] string? sessionName = null,
            CancellationToken ct = default)
        {
            return IdentityAsync(roleId, criteriaType, criteria, false, sessionName, ct);
        }

        /// <summary>
        /// Adds an application-URI rule.
        /// </summary>
        [McpServerTool(Name = "roles_add_application", Destructive = true)]
        [Description("Add one exact application URI to a role mapping. Example: applicationUri='urn:plant:client'. " +
            "Returns actual status; ApplicationsExclude is unchanged.")]
        public Task<CallToolResult> AddApplicationAsync(
            [Description(Descriptions.RoleId)] string roleId,
            [Description(Descriptions.ApplicationUri)] string applicationUri,
            [Description(Descriptions.SessionName)] string? sessionName = null,
            CancellationToken ct = default)
        {
            return ApplicationAsync(roleId, applicationUri, true, sessionName, ct);
        }

        /// <summary>
        /// Removes an application-URI rule.
        /// </summary>
        [McpServerTool(Name = "roles_remove_application", Destructive = true)]
        [Description("Remove one exact application URI rule. Example: applicationUri='urn:plant:client'. " +
            "Returns actual status without modifying exclusion flags.")]
        public Task<CallToolResult> RemoveApplicationAsync(
            [Description(Descriptions.RoleId)] string roleId,
            [Description(Descriptions.ApplicationUri)] string applicationUri,
            [Description(Descriptions.SessionName)] string? sessionName = null,
            CancellationToken ct = default)
        {
            return ApplicationAsync(roleId, applicationUri, false, sessionName, ct);
        }

        /// <summary>
        /// Adds a typed endpoint mapping.
        /// </summary>
        [McpServerTool(Name = "roles_add_endpoint", Destructive = true)]
        [Description("Add one endpoint rule: URL, security mode and policy are matched exactly, not connected to. " +
            "Example: securityMode='SignAndEncrypt'. Returns actual status; exclusion flags remain unchanged.")]
        public Task<CallToolResult> AddEndpointAsync(
            [Description(Descriptions.RoleId)] string roleId,
            [Description(Descriptions.EndpointUrl)] string endpointUrl,
            [Description(Descriptions.SecurityMode)] MessageSecurityMode securityMode,
            [Description(Descriptions.SecurityPolicyUri)] string securityPolicyUri,
            [Description(Descriptions.SessionName)] string? sessionName = null,
            CancellationToken ct = default)
        {
            return EndpointAsync(roleId, endpointUrl, securityMode, securityPolicyUri, true, sessionName, ct);
        }

        /// <summary>
        /// Removes a typed endpoint mapping.
        /// </summary>
        [McpServerTool(Name = "roles_remove_endpoint", Destructive = true)]
        [Description("Remove exactly one endpoint rule with matching URL/mode/policy. Returns actual status. " +
            "Example: securityMode='SignAndEncrypt'; does not connect or downgrade security.")]
        public Task<CallToolResult> RemoveEndpointAsync(
            [Description(Descriptions.RoleId)] string roleId,
            [Description(Descriptions.EndpointUrl)] string endpointUrl,
            [Description(Descriptions.SecurityMode)] MessageSecurityMode securityMode,
            [Description(Descriptions.SecurityPolicyUri)] string securityPolicyUri,
            [Description(Descriptions.SessionName)] string? sessionName = null,
            CancellationToken ct = default)
        {
            return EndpointAsync(roleId, endpointUrl, securityMode, securityPolicyUri, false, sessionName, ct);
        }

        /// <summary>
        /// Updates only explicitly supplied role flags in one non-atomic Write.
        /// </summary>
        [McpServerTool(Name = "roles_set_flags", Destructive = true)]
        [Description("Set explicit ApplicationsExclude, EndpointsExclude or CustomConfiguration values. Omission " +
            "leaves each field unchanged; false is an explicit write. Returns ordered per-field statuses, not CAS. " +
            "Example: applicationsExclude=false.")]
        public Task<CallToolResult> SetFlagsAsync(
            [Description(Descriptions.RoleId)] string roleId,
            [Description(Descriptions.ApplicationsExclude)] bool? applicationsExclude = null,
            [Description(Descriptions.EndpointsExclude)] bool? endpointsExclude = null,
            [Description(Descriptions.CustomConfiguration)] bool? customConfiguration = null,
            [Description(Descriptions.SessionName)] string? sessionName = null,
            CancellationToken ct = default)
        {
            return McpCompanionTools.ExecuteAsync(async token =>
            {
                IWorkflowSessionScope scope = await WorkflowJson.IdentityScopeAsync(
                    m_sessions, m_scopes, sessionName, token).ConfigureAwait(false);
                await using ConfiguredAsyncDisposable lease = scope.ConfigureAwait(false);
                ISession session = scope.Access.Session;
                NodeId role = WorkflowJson.Node(session, roleId);
                _ = await McpModelAccess.RequireTypeAsync(session, role, ObjectTypeIds.RoleType, token)
                    .ConfigureAwait(false);
                var writes = new List<WriteValue>();
                var names = new JsonArray();
                (string Name, bool? Value)[] flags =
                [
                    (BrowseNames.ApplicationsExclude, applicationsExclude),
                    (BrowseNames.EndpointsExclude, endpointsExclude),
                    (BrowseNames.CustomConfiguration, customConfiguration)
                ];
                foreach ((string name, bool? value) in flags)
                {
                    if (value.HasValue)
                    {
                        NodeId child = await WorkflowJson.ChildAsync(session, role, name, token).ConfigureAwait(false);
                        writes.Add(new WriteValue
                        {
                            NodeId = child,
                            AttributeId = Attributes.Value,
                            Value = new DataValue(Variant.From(value.Value))
                        });
                        names.Add((JsonNode)name);
                    }
                }
                if (writes.Count == 0)
                {
                    throw new ArgumentException("At least one explicit role flag is required.");
                }
                var requests = writes.ToArrayOf();
                WriteResponse write = await session.WriteAsync(null, requests, token).ConfigureAwait(false);
                ClientBase.ValidateResponse(write.Results, requests);
                JsonObject result = WorkflowJson.Update(session, role, write.Results);
                result["fields"] = names;
                return result;
            }, ct);
        }

        /// <summary>
        /// Reads declared and effective permissions as separate, quality-bearing attributes.
        /// </summary>
        [McpServerTool(Name = "roles_read_permissions", ReadOnly = true)]
        [Description("Read RolePermissions, UserRolePermissions, AccessRestrictions and write masks on one node. " +
            "Effective permissions are server-derived, never guessed. Unsupported attributes retain Bad status. " +
            "Returns quality-bearing attributes. Example: nodeId='i=2253'.")]
        public Task<CallToolResult> ReadPermissionsAsync(
            [Description(Descriptions.NodeId)] string nodeId,
            [Description(Descriptions.SessionName)] string? sessionName = null,
            CancellationToken ct = default)
        {
            return McpCompanionTools.ExecuteAsync(async token =>
            {
                IWorkflowSessionScope scope = await m_scopes.CaptureAsync(
                    m_sessions.GetSessionInfoOrThrow(sessionName), token).ConfigureAwait(false);
                await using ConfiguredAsyncDisposable lease = scope.ConfigureAwait(false);
                ISession session = scope.Access.Session;
                return await WorkflowJson.AttributesAsync(session, WorkflowJson.Node(session, nodeId),
                    [
                        Attributes.RolePermissions, Attributes.UserRolePermissions, Attributes.AccessRestrictions,
                        Attributes.WriteMask, Attributes.UserWriteMask
                    ], token).ConfigureAwait(false);
            }, ct);
        }

        /// <summary>
        /// Replaces declared RolePermissions; effective permissions remain read-only.
        /// </summary>
        [McpServerTool(Name = "roles_write_permissions", Destructive = true)]
        [Description("Explicitly replace the declared RolePermissions attribute. An empty permissions array clears " +
            "it; omission is invalid. Server write support and self-lockout checks remain authoritative. " +
            "Example: permissions=[{roleId:'i=15644',permissions:1}]. Returns Write status, not atomic CAS.")]
        public Task<CallToolResult> WritePermissionsAsync(
            [Description(Descriptions.NodeId)] string nodeId,
            [Description(Descriptions.Permissions)] ArrayOf<WorkflowRolePermissionInput> permissions,
            [Description(Descriptions.SessionName)] string? sessionName = null,
            CancellationToken ct = default)
        {
            return McpCompanionTools.ExecuteAsync(async token =>
            {
                var input = new WorkflowPermissionsInput { Permissions = permissions };
                if (input.Permissions.IsNull || input.Permissions.Count > m_options.MaxItems)
                {
                    throw new ArgumentException("An explicit bounded permission array is required.");
                }
                WorkflowValues.ValidateInline(input, m_options);
                IWorkflowSessionScope scope = await WorkflowJson.IdentityScopeAsync(
                    m_sessions, m_scopes, sessionName, token).ConfigureAwait(false);
                await using ConfiguredAsyncDisposable lease = scope.ConfigureAwait(false);
                OpcUaSessionManager.SessionInfo info = scope.Access;
                ISession session = info.Session;
                NodeId node = WorkflowJson.Node(session, nodeId);
                var updates = new RolePermissionType[input.Permissions.Count];
                var roles = new HashSet<NodeId>();
                for (int index = 0; index < updates.Length; index++)
                {
                    WorkflowRolePermissionInput permission = input.Permissions[index];
                    ArgumentNullException.ThrowIfNull(permission);
                    NodeId role = WorkflowJson.Node(session, permission.RoleId);
                    if (!roles.Add(role))
                    {
                        throw new ArgumentException("The declared permission array contains a duplicate role.");
                    }
                    updates[index] = new RolePermissionType { RoleId = role, Permissions = permission.Permissions };
                }
                ArrayOf<WriteValue> requests =
                [
                    new WriteValue
                    {
                        NodeId = node, AttributeId = Attributes.RolePermissions,
                        Value = new DataValue(Variant.FromStructure(updates.ToArrayOf()))
                    }
                ];
                await m_sessions.UaOperationGuard.AuthorizeWritesAsync(info, requests, token).ConfigureAwait(false);
                WriteResponse write = await session.WriteAsync(null, requests, token).ConfigureAwait(false);
                ClientBase.ValidateResponse(write.Results, requests);
                return WorkflowJson.Update(session, node, write.Results);
            }, ct);
        }

        /// <summary>
        /// Streams only role instances and releases any unconsumed browse continuation point.
        /// </summary>
        private static async IAsyncEnumerable<ReferenceDescription> RolesAsync(
            ISession session, [EnumeratorCancellation] CancellationToken ct)
        {
            await foreach (ReferenceDescription reference in session.EnumerateReferencesAsync(
                ObjectIds.Server_ServerCapabilities_RoleSet, ReferenceTypeIds.HierarchicalReferences,
                nodeClassMask: (uint)NodeClass.Object, ct: ct).ConfigureAwait(false))
            {
                NodeId type = McpModelAccess.ModelNode(session, reference.TypeDefinition);
                if (type == ObjectTypeIds.RoleType ||
                    await session.NodeCache.IsTypeOfAsync(type, ObjectTypeIds.RoleType, ct).ConfigureAwait(false))
                {
                    yield return reference;
                }
            }
        }

        /// <summary>
        /// Constructs one concrete identity rule before dispatch.
        /// </summary>
        private Task<CallToolResult> IdentityAsync(
            string roleId, IdentityCriteriaType criteriaType, string criteria, bool add,
            string? sessionName, CancellationToken ct)
        {
            return McpCompanionTools.ExecuteAsync(async token =>
            {
                if (!Enum.IsDefined(criteriaType))
                {
                    throw new ArgumentOutOfRangeException(nameof(criteriaType));
                }
                WorkflowJson.Text(criteria);
                return await RoleCallAsync(roleId, add ? BrowseNames.AddIdentity : BrowseNames.RemoveIdentity,
                    [Variant.FromStructure(new IdentityMappingRuleType
                    {
                        CriteriaType = criteriaType, Criteria = criteria
                    })], sessionName, token).ConfigureAwait(false);
            }, ct);
        }

        /// <summary>
        /// Dispatches a fixed application mapping method.
        /// </summary>
        private Task<CallToolResult> ApplicationAsync(
            string roleId, string applicationUri, bool add, string? sessionName, CancellationToken ct)
        {
            return McpCompanionTools.ExecuteAsync(async token =>
            {
                WorkflowJson.Text(applicationUri);
                return await RoleCallAsync(roleId, add ? BrowseNames.AddApplication : BrowseNames.RemoveApplication,
                    [Variant.From(applicationUri)], sessionName, token).ConfigureAwait(false);
            }, ct);
        }

        /// <summary>
        /// Constructs a typed endpoint rule with explicit security policy.
        /// </summary>
        private Task<CallToolResult> EndpointAsync(
            string roleId, string endpointUrl, MessageSecurityMode mode, string policy, bool add,
            string? sessionName, CancellationToken ct)
        {
            return McpCompanionTools.ExecuteAsync(async token =>
            {
                WorkflowJson.Text(endpointUrl);
                WorkflowJson.Text(policy);
                if (mode is not MessageSecurityMode.None and not MessageSecurityMode.Sign and
                    not MessageSecurityMode.SignAndEncrypt)
                {
                    throw new ArgumentOutOfRangeException(nameof(mode));
                }
                return await RoleCallAsync(roleId, add ? BrowseNames.AddEndpoint : BrowseNames.RemoveEndpoint,
                    [Variant.FromStructure(new EndpointType
                    {
                        EndpointUrl = endpointUrl, SecurityMode = mode, SecurityPolicyUri = policy
                    })], sessionName, token).ConfigureAwait(false);
            }, ct);
        }

        /// <summary>
        /// Resolves one known namespace-zero method on a verified RoleType instance.
        /// </summary>
        private async ValueTask<JsonObject> RoleCallAsync(
            string roleId, string method, ArrayOf<Variant> arguments, string? sessionName, CancellationToken ct)
        {
            IWorkflowSessionScope scope = await WorkflowJson.IdentityScopeAsync(
                m_sessions, m_scopes, sessionName, ct).ConfigureAwait(false);
            await using ConfiguredAsyncDisposable lease = scope.ConfigureAwait(false);
            ISession session = scope.Access.Session;
            NodeId role = WorkflowJson.Node(session, roleId);
            _ = await McpModelAccess.RequireTypeAsync(session, role, ObjectTypeIds.RoleType, ct).ConfigureAwait(false);
            NodeId methodId = await WorkflowJson.ChildAsync(session, role, method, ct).ConfigureAwait(false);
            var proxy = new RoleTypeClient(session, role, session.MessageContext.Telemetry);
            return WorkflowJson.Method(session, role,
                await proxy.CallWithResultAsync(methodId, arguments, ct).ConfigureAwait(false));
        }

        private readonly OpcUaSessionManager m_sessions;
        private readonly WorkflowOptions m_options;
        private readonly IWorkflowSessionFactory m_scopes;
    }
}
