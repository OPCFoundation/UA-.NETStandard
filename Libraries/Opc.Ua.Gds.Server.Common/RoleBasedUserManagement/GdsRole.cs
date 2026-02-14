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
        /// A privilege to manage the own Certificates and pull trust list
        /// </summary>
        public static Role ApplicationSelfAdmin { get; } = new(
            ExpandedNodeId.Null,
            "ApplicationSelfAdmin");

        public GdsRole(NodeId roleId, string name)
            : base(roleId, name)
        {
        }
    }

    /// <summary>
    /// RoleBasedIdentity with additional Property ApplicationId
    /// </summary>
    public class GdsRoleBasedIdentity : RoleBasedIdentity
    {
        public GdsRoleBasedIdentity(
            IUserIdentity identity,
            IEnumerable<Role> roles,
            NodeId applicationId,
            NamespaceTable namespaces)
            : base(identity, roles, namespaces)
        {
            ApplicationId = applicationId;
        }

        public GdsRoleBasedIdentity(
            IUserIdentity identity,
            IEnumerable<Role> roles,
            NamespaceTable namespaces)
            : base(identity, roles, namespaces)
        {
        }

        /// <summary>
        /// The applicationId in case the ApplicationSelfAdminPrivilege is used
        /// </summary>
        public NodeId ApplicationId { get; }
    }
}
