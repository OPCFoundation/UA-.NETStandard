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

namespace Opc.Ua.Server
{
    /// <summary>
    /// Role permission sets a server can apply to the namespaces that
    /// configure no <c>DefaultRolePermissions</c> of their own, see
    /// <see cref="StandardServer.NamespaceDefaultRolePermissions"/>.
    /// </summary>
    public static class ServerDefaultRolePermissions
    {
        /// <summary>
        /// The permissions that only observe the address space.
        /// </summary>
        public const PermissionType ReadOnly =
            PermissionType.Browse |
            PermissionType.ReadRolePermissions |
            PermissionType.Read |
            PermissionType.ReadHistory |
            PermissionType.ReceiveEvents;

        /// <summary>
        /// Every permission defined by OPC 10000-3 8.55.
        /// </summary>
        public const PermissionType All =
            ReadOnly |
            PermissionType.WriteAttribute |
            PermissionType.WriteRolePermissions |
            PermissionType.WriteHistorizing |
            PermissionType.Write |
            PermissionType.InsertHistory |
            PermissionType.ModifyHistory |
            PermissionType.DeleteHistory |
            PermissionType.Call |
            PermissionType.AddReference |
            PermissionType.RemoveReference |
            PermissionType.DeleteNode |
            PermissionType.AddNode;

        /// <summary>
        /// No server default: a node without <c>RolePermissions</c> in a
        /// namespace without <c>DefaultRolePermissions</c> is unrestricted,
        /// also for anonymous sessions.
        /// </summary>
        public static ArrayOf<RolePermissionType> Unrestricted => default;

        /// <summary>
        /// The <c>Anonymous</c> role may browse, read and receive events. The
        /// <c>AuthenticatedUser</c> role and the other well-known user roles
        /// keep every permission. A session that holds none of these roles,
        /// for example an anonymous user of a trusted application, is denied.
        /// Every access returns new entries, so changing the permissions of one server does not
        /// change another.
        /// </summary>
        public static ArrayOf<RolePermissionType> AnonymousReadOnly =>
        [
            Create(ObjectIds.WellKnownRole_Anonymous, ReadOnly),
            Create(ObjectIds.WellKnownRole_AuthenticatedUser, All),
            Create(ObjectIds.WellKnownRole_Observer, All),
            Create(ObjectIds.WellKnownRole_Operator, All),
            Create(ObjectIds.WellKnownRole_Engineer, All),
            Create(ObjectIds.WellKnownRole_Supervisor, All),
            Create(ObjectIds.WellKnownRole_ConfigureAdmin, All),
            Create(ObjectIds.WellKnownRole_SecurityAdmin, All)
        ];

        private static RolePermissionType Create(NodeId roleId, PermissionType permissions)
        {
            return new RolePermissionType
            {
                RoleId = roleId,
                Permissions = (uint)permissions
            };
        }
    }
}
