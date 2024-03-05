/* ========================================================================
 * Copyright (c) 2005-2024 The OPC Foundation, Inc. All rights reserved.
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
        /// This Role grants rights to register, update and unregister any OPC UA Application.
        /// </summary>
        public static Role DiscoveryAdmin { get; } = new Role(ExpandedNodeId.ToNodeId(ObjectIds.WellKnownRole_DiscoveryAdmin, new NamespaceTable(new string[] { Namespaces.OpcUa, Namespaces.OpcUaGds })), BrowseNames.WellKnownRole_DiscoveryAdmin);

        public static Role CertificateAuthorityAdmin { get; } = new Role(ExpandedNodeId.ToNodeId(ObjectIds.WellKnownRole_CertificateAuthorityAdmin, new NamespaceTable(new string[] { Namespaces.OpcUa, Namespaces.OpcUaGds })), BrowseNames.WellKnownRole_CertificateAuthorityAdmin);

        public static Role RegistrationAuthorityAdmin { get; } = new Role(ExpandedNodeId.ToNodeId(ObjectIds.WellKnownRole_RegistrationAuthorityAdmin, new NamespaceTable(new string[] { Namespaces.OpcUa, Namespaces.OpcUaGds })), BrowseNames.WellKnownRole_RegistrationAuthorityAdmin);

        /// <summary>
        ///  A privilege to manage the own Certificates and pull trust list
        /// </summary>
        public static Role ApplicationSelfAdmin { get; } = new Role(NodeId.Null, "ApplicationSelfAdmin");

        public GdsRole(NodeId roleId, string name) :
            base(roleId, name)
        { }
    }
    /// <summary>
    /// RoleBasedIdentity with additional Property ApplicationId
    /// </summary>
    public class GdsRoleBasedIdentity : RoleBasedIdentity
    {
        private NodeId m_applicationId;

        public GdsRoleBasedIdentity(IUserIdentity identity, IEnumerable<Role> roles, NodeId applicationId)
     : base(identity, roles)
        {
            m_applicationId = applicationId;
        }

        public GdsRoleBasedIdentity(IUserIdentity identity, IEnumerable<Role> roles)
     : base(identity, roles)
        { }

        /// <summary>
        /// The applicationId in case the ApplicationSelfAdminPrivilege is used
        /// </summary>
        public NodeId ApplicationId
        {
            get { return m_applicationId; }
        }
    }
}
