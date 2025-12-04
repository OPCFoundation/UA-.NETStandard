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
using System.Runtime.Serialization;
using System.Xml;

namespace Opc.Ua.Server
{
    /// <summary>
    /// The well known roles in a server.
    /// https://reference.opcfoundation.org/Core/Part3/v105/docs/4.9.2
    /// </summary>
    [DataContract(Namespace = Namespaces.Roles)]
    public class Role : IEquatable<Role>
    {
        /// <summary>
        /// The Role is allowed to browse and read non-security related Nodes only in the Server Object and all type Nodes.
        /// </summary>
        public static Role Anonymous { get; } =
            new Role(ObjectIds.WellKnownRole_Anonymous, BrowseNames.WellKnownRole_Anonymous);

        /// <summary>
        /// The Role is allowed to browse and read non-security related Nodes.
        /// </summary>
        public static Role AuthenticatedUser { get; } =
            new Role(
                ObjectIds.WellKnownRole_AuthenticatedUser,
                BrowseNames.WellKnownRole_AuthenticatedUser);

        /// <summary>
        /// The Role is allowed to browse, read live data, read historical data/events or subscribe to data/events.
        /// </summary>
        public static Role Observer { get; } =
            new Role(ObjectIds.WellKnownRole_Observer, BrowseNames.WellKnownRole_Observer);

        /// <summary>
        /// The Role is allowed to browse, read live data, read historical data/events or subscribe to data/events.
        /// In addition, the Session is allowed to write some live data and call some Methods.
        /// </summary>
        public static Role Operator { get; } =
            new Role(ObjectIds.WellKnownRole_Operator, BrowseNames.WellKnownRole_Operator);

        /// <summary>
        /// The Role is allowed to browse, read/write configuration data, read historical data/events, call Methods or subscribe to data/events.
        /// </summary>
        public static Role Engineer { get; } =
            new Role(ObjectIds.WellKnownRole_Engineer, BrowseNames.WellKnownRole_Engineer);

        /// <summary>
        /// The Role is allowed to browse, read live data, read historical data/events, call Methods or subscribe to data/events.
        /// </summary>
        public static Role Supervisor { get; } =
            new Role(ObjectIds.WellKnownRole_Supervisor, BrowseNames.WellKnownRole_Supervisor);

        /// <summary>
        /// The Role is allowed to change the non-security related configuration settings.
        /// </summary>
        public static Role ConfigureAdmin { get; } =
            new Role(
                ObjectIds.WellKnownRole_ConfigureAdmin,
                BrowseNames.WellKnownRole_ConfigureAdmin);

        /// <summary>
        /// The Role is allowed to change security related settings.
        /// </summary>
        public static Role SecurityAdmin { get; } =
            new Role(
                ObjectIds.WellKnownRole_SecurityAdmin,
                BrowseNames.WellKnownRole_SecurityAdmin);

        /// <summary>
        /// Constructor for new Role
        /// </summary>
        /// <param name="roleId">NodeId of the Role, used for WellKnownRoles</param>
        /// <param name="name">Name of the Role</param>
        public Role(NodeId roleId, string name)
        {
            RoleId = roleId;
            Name = name;
        }

        /// <summary>
        /// The name of the role.
        /// </summary>
        [DataMember(Name = "Name", IsRequired = true, Order = 10)]
        public string Name { get; private set; }

        /// <summary>
        /// The NodeId of the role.
        /// </summary>
        [DataMember(Name = "RoleId", IsRequired = true, Order = 20)]
        public NodeId RoleId { get; set; }

        /// <inheritdoc/>
        public bool Equals(Role other)
        {
            if (other is null)
            {
                return false;
            }
            if (ReferenceEquals(this, other))
            {
                return true;
            }
            if (GetType() != other.GetType())
            {
                return false;
            }
            return (Name == other.Name) && (RoleId == other.RoleId);
        }

        /// <inheritdoc/>
        public override bool Equals(object obj)
        {
            return Equals(obj as Role);
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            return HashCode.Combine(Name, RoleId);
        }

        /// <inheritdoc/>
        public static bool operator ==(Role lhs, Role rhs)
        {
            if (lhs is null)
            {
                if (rhs is null)
                {
                    return true;
                }

                // Only the left side is null.
                return false;
            }

            // Equals handles case of null on right side.
            return lhs.Equals(rhs);
        }

        /// <inheritdoc/>
        public static bool operator !=(Role lhs, Role rhs)
        {
            return !(lhs == rhs);
        }

        /// <summary>
        /// Returns the name of the role.
        /// </summary>
        public override string ToString()
        {
            return Name;
        }
    }

    /// <summary>
    /// The role based identity for a server.
    /// </summary>
    public class RoleBasedIdentity : IUserIdentity
    {
        private readonly IUserIdentity m_identity;

        /// <summary>
        /// Initialize the role based identity.
        /// </summary>
        public RoleBasedIdentity(IUserIdentity identity, IEnumerable<Role> roles)
        {
            m_identity = identity;
            Roles = roles;
            foreach (Role role in roles)
            {
                if (!(role.RoleId?.IsNullNodeId ?? true))
                {
                    GrantedRoleIds.Add(role.RoleId);
                }
            }
        }

        /// <inheritdoc/>
        public NodeIdCollection GrantedRoleIds => m_identity.GrantedRoleIds;

        /// <summary>
        /// The role in the context of a server.
        /// </summary>
        public IEnumerable<Role> Roles { get; }

        /// <inheritdoc/>
        public string DisplayName => m_identity.DisplayName;

        /// <inheritdoc/>
        public string PolicyId => m_identity.PolicyId;

        /// <inheritdoc/>
        public UserTokenType TokenType => m_identity.TokenType;

        /// <inheritdoc/>
        public XmlQualifiedName IssuedTokenType => m_identity.IssuedTokenType;

        /// <inheritdoc/>
        public bool SupportsSignatures => m_identity.SupportsSignatures;

        /// <inheritdoc/>
        public UserIdentityToken GetIdentityToken()
        {
            return m_identity.GetIdentityToken();
        }
    }
}
