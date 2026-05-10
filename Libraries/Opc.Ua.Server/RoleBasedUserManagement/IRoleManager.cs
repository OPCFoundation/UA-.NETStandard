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

using System.Collections.Generic;
using System.Security.Cryptography.X509Certificates;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Extensibility surface for OPC UA Part 18 role / identity-mapping
    /// management. The default in-process implementation lives in
    /// <see cref="RoleManager"/>; integrators that want to back roles with a
    /// custom store (e.g. an LDAP directory or a database) implement this
    /// interface and inject an instance via
    /// <see cref="IServerInternal.SetRoleManager"/>.
    /// </summary>
    /// <remarks>
    /// The interface is intentionally narrow — it covers the operations the
    /// <c>RoleStateBinding</c> and the <c>RoleSet</c> service handlers need
    /// to read/mutate per-role state and resolve granted roles for a session.
    /// Implementations are expected to be thread-safe; callers may invoke
    /// these members from multiple session threads concurrently.
    /// </remarks>
    public interface IRoleManager
    {
        /// <summary>
        /// Ensures a <see cref="RoleEntry"/> exists for <paramref name="roleId"/>.
        /// Idempotent.
        /// </summary>
        RoleEntry EnsureRole(NodeId roleId);

        /// <summary>
        /// Registered role NodeIds.
        /// </summary>
        IReadOnlyList<NodeId> RoleIds { get; }

        /// <summary>
        /// Adds an identity-mapping rule to the role. Idempotent — duplicate
        /// rules are silently dropped.
        /// </summary>
        ServiceResult AddIdentity(NodeId roleId, IdentityMappingRuleType rule);

        /// <summary>
        /// Removes a previously added identity-mapping rule. Returns
        /// BadNotFound if the rule isn't present.
        /// </summary>
        ServiceResult RemoveIdentity(NodeId roleId, IdentityMappingRuleType rule);

        /// <summary>
        /// Adds an application URI to the role's Applications list.
        /// </summary>
        ServiceResult AddApplication(NodeId roleId, string applicationUri);

        /// <summary>
        /// Removes an application URI; returns BadNotFound if absent.
        /// </summary>
        ServiceResult RemoveApplication(NodeId roleId, string applicationUri);

        /// <summary>
        /// Adds an endpoint description to the role's Endpoints list.
        /// </summary>
        ServiceResult AddEndpoint(NodeId roleId, EndpointType endpoint);

        /// <summary>
        /// Removes a previously added endpoint; returns BadNotFound if absent.
        /// </summary>
        ServiceResult RemoveEndpoint(NodeId roleId, EndpointType endpoint);

        /// <summary>
        /// Read-only snapshot of the role's identities. Used by Variable read
        /// handlers.
        /// </summary>
        IList<IdentityMappingRuleType> SnapshotIdentities(NodeId roleId);

        /// <summary>
        /// Read-only snapshot of the role's application URIs.
        /// </summary>
        IList<string> SnapshotApplications(NodeId roleId, out bool exclude);

        /// <summary>
        /// Read-only snapshot of the role's endpoints.
        /// </summary>
        IList<EndpointType> SnapshotEndpoints(NodeId roleId, out bool exclude);

        /// <summary>
        /// Sets the ApplicationsExclude flag (true = role is granted to apps
        /// NOT in the list).
        /// </summary>
        void SetApplicationsExclude(NodeId roleId, bool exclude);

        /// <summary>
        /// Sets the EndpointsExclude flag (true = role is granted on endpoints
        /// NOT in the list).
        /// </summary>
        void SetEndpointsExclude(NodeId roleId, bool exclude);

        /// <summary>
        /// Computes the set of additional roles to grant a session given its
        /// identity, client cert, and endpoint per Part 18 §4.4.4.
        /// </summary>
        IList<NodeId> ResolveGrantedRoles(
            IUserIdentity identity,
            X509Certificate2 clientCertificate,
            EndpointDescription endpoint);

        /// <summary>
        /// Namespace index used to issue NodeIds for dynamically created
        /// roles. Initialized by <see cref="RoleStateBinding.Bind"/> from the
        /// diagnostics node manager's namespace.
        /// </summary>
        ushort DynamicRoleNamespaceIndex { get; set; }

        /// <summary>
        /// Adds a new dynamically-created role per the
        /// RoleSetType.AddRole method.
        /// </summary>
        ServiceResult AddRole(string roleName, string namespaceUri, out NodeId newRoleId);

        /// <summary>
        /// Removes a dynamically-created role per the
        /// RoleSetType.RemoveRole method.
        /// </summary>
        ServiceResult RemoveRole(NodeId roleId);
    }
}
