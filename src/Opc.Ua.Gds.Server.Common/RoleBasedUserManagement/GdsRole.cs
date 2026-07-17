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
using System.Linq;
using Opc.Ua.Server;

namespace Opc.Ua.Gds.Server
{
    /// <summary>
    /// The supported roles in a GDS server.
    /// </summary>
    public class GdsRole : Role
    {
        /// <summary>
        /// This Role grants rights to register, update and unregister any
        /// OPC UA Application.
        /// </summary>
        public static Role DiscoveryAdmin { get; } = new(
            ObjectIds.WellKnownRole_DiscoveryAdmin,
            BrowseNames.WellKnownRole_DiscoveryAdmin);

        /// <summary>
        /// Certificate authority administration role. This Role grants rights
        /// to manage the Certificate Authority, e.g. to create and revoke
        /// certificates, manage trust lists, and configure certificate policies.
        /// </summary>
        public static Role CertificateAuthorityAdmin { get; } = new(
            ObjectIds.WellKnownRole_CertificateAuthorityAdmin,
            BrowseNames.WellKnownRole_CertificateAuthorityAdmin);

        /// <summary>
        /// This role grants rights to manage the Registration Authority, e.g.
        /// to approve or reject application registration requests, and to manage the
        /// Registration Authority.
        /// </summary>
        public static Role RegistrationAuthorityAdmin { get; } = new(
            ObjectIds.WellKnownRole_RegistrationAuthorityAdmin,
            BrowseNames.WellKnownRole_RegistrationAuthorityAdmin);

        /// <summary>
        /// A privilege to manage the own Certificates and pull trust list.
        /// </summary>
        /// <remarks>
        /// Per OPC 10000-12 §7.2 the ApplicationSelfAdmin privilege grants a
        /// Client access only to the Application whose
        /// <c>ApplicationInstance</c> Certificate was used to establish the
        /// SecureChannel.
        /// </remarks>
        public static Role ApplicationSelfAdmin { get; } = new(
            ExpandedNodeId.Null,
            "ApplicationSelfAdmin");

        /// <summary>
        /// A privilege to manage one or more Applications administered by a
        /// configuration tool or agent.
        /// </summary>
        /// <remarks>
        /// Per OPC 10000-12 §7.2 the ApplicationAdmin privilege grants a
        /// Client access to a configurable set of Applications. The set of
        /// administered <c>ApplicationId</c>s is carried by the
        /// <see cref="GdsRoleBasedIdentity.AdministeredApplicationIds"/>
        /// property assigned during impersonation.
        /// </remarks>
        public static Role ApplicationAdmin { get; } = new(
            ExpandedNodeId.Null,
            "ApplicationAdmin");

        public GdsRole(NodeId roleId, string name)
            : base(roleId, name)
        {
        }
    }

    /// <summary>
    /// RoleBasedIdentity with additional Properties ApplicationId and
    /// AdministeredApplicationIds.
    /// </summary>
    public class GdsRoleBasedIdentity : RoleBasedIdentity
    {
        public GdsRoleBasedIdentity(
            IUserIdentity identity,
            IEnumerable<Role> roles,
            NodeId applicationId,
            NamespaceTable namespaces)
            : this(identity, roles, applicationId, null, namespaces)
        {
        }

        public GdsRoleBasedIdentity(
            IUserIdentity identity,
            IEnumerable<Role> roles,
            NamespaceTable namespaces)
            : this(identity, roles, default, null, namespaces)
        {
        }

        /// <summary>
        /// Creates a new identity that carries an
        /// <see cref="GdsRole.ApplicationSelfAdmin"/> binding and/or an
        /// <see cref="GdsRole.ApplicationAdmin"/> binding.
        /// </summary>
        /// <param name="identity">The wrapped user identity.</param>
        /// <param name="roles">The roles granted to the identity.</param>
        /// <param name="applicationId">
        /// The <c>ApplicationId</c> associated with the
        /// <see cref="GdsRole.ApplicationSelfAdmin"/> privilege, or
        /// <see cref="NodeId.Null"/> if not applicable.
        /// </param>
        /// <param name="administeredApplicationIds">
        /// The set of <c>ApplicationId</c>s administered via the
        /// <see cref="GdsRole.ApplicationAdmin"/> privilege, or <c>null</c> if
        /// not applicable.
        /// </param>
        /// <param name="namespaces">The namespace table.</param>
        public GdsRoleBasedIdentity(
            IUserIdentity identity,
            IEnumerable<Role> roles,
            NodeId applicationId,
            IEnumerable<NodeId>? administeredApplicationIds,
            NamespaceTable namespaces)
            : base(identity, roles, namespaces)
        {
            ApplicationId = applicationId;
            AdministeredApplicationIds = administeredApplicationIds == null
                ? []
                : [.. administeredApplicationIds];
        }

        /// <summary>
        /// The applicationId in case the ApplicationSelfAdminPrivilege is used.
        /// </summary>
        public NodeId ApplicationId { get; }

        /// <summary>
        /// The set of ApplicationIds administered by an
        /// <see cref="GdsRole.ApplicationAdmin"/> bearer.
        /// </summary>
        public IReadOnlyList<NodeId> AdministeredApplicationIds { get; }

        /// <inheritdoc/>
        /// <remarks>
        /// Overridden to return a <see cref="GdsRoleBasedIdentity"/> so that
        /// <see cref="ApplicationId"/>,
        /// <see cref="AdministeredApplicationIds"/> and the concrete type are
        /// preserved when additional roles (e.g.
        /// <see cref="Role.TrustedApplication"/>) are layered on.
        /// </remarks>
        public override RoleBasedIdentity WithAdditionalRoles(
            IEnumerable<Role> additionalRoles,
            NamespaceTable namespaces)
        {
            return new GdsRoleBasedIdentity(
                InnerIdentity,
                Roles.Concat(additionalRoles),
                ApplicationId,
                AdministeredApplicationIds,
                namespaces);
        }
    }
}
