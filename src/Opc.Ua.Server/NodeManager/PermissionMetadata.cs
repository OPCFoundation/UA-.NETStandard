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
    /// The attributes the role-permission and access-restriction checks read:
    /// the subset of <see cref="NodeMetadata"/> a permission check needs.
    /// </summary>
    /// <remarks>
    /// Read, Write, Browse and Call validate every node they touch. Filling this
    /// value on the stack spares the per-node <see cref="NodeMetadata"/> object when
    /// the caller only needs the verdict.
    /// </remarks>
    internal readonly struct PermissionMetadata
    {
        public PermissionMetadata(
            NodeId nodeId,
            bool isPartOfTypeHierarchy,
            AccessRestrictionType accessRestrictions,
            AccessRestrictionType defaultAccessRestrictions,
            ArrayOf<RolePermissionType> rolePermissions,
            ArrayOf<RolePermissionType> defaultRolePermissions,
            ArrayOf<RolePermissionType> userRolePermissions,
            ArrayOf<RolePermissionType> defaultUserRolePermissions)
        {
            NodeId = nodeId;
            IsPartOfTypeHierarchy = isPartOfTypeHierarchy;
            AccessRestrictions = accessRestrictions;
            DefaultAccessRestrictions = defaultAccessRestrictions;
            RolePermissions = rolePermissions;
            DefaultRolePermissions = defaultRolePermissions;
            UserRolePermissions = userRolePermissions;
            DefaultUserRolePermissions = defaultUserRolePermissions;
        }

        /// <summary>
        /// Takes the permission attributes of node metadata.
        /// </summary>
        public static PermissionMetadata From(NodeMetadata metadata)
        {
            return new PermissionMetadata(
                metadata.NodeId,
                metadata.IsPartOfTypeHierarchy,
                metadata.AccessRestrictions,
                metadata.DefaultAccessRestrictions,
                metadata.RolePermissions,
                metadata.DefaultRolePermissions,
                metadata.UserRolePermissions,
                metadata.DefaultUserRolePermissions);
        }

        /// <inheritdoc cref="NodeMetadata.NodeId"/>
        public NodeId NodeId { get; }

        /// <inheritdoc cref="NodeMetadata.IsPartOfTypeHierarchy"/>
        public bool IsPartOfTypeHierarchy { get; }

        /// <inheritdoc cref="NodeMetadata.AccessRestrictions"/>
        public AccessRestrictionType AccessRestrictions { get; }

        /// <inheritdoc cref="NodeMetadata.DefaultAccessRestrictions"/>
        public AccessRestrictionType DefaultAccessRestrictions { get; }

        /// <inheritdoc cref="NodeMetadata.RolePermissions"/>
        public ArrayOf<RolePermissionType> RolePermissions { get; }

        /// <inheritdoc cref="NodeMetadata.DefaultRolePermissions"/>
        public ArrayOf<RolePermissionType> DefaultRolePermissions { get; }

        /// <inheritdoc cref="NodeMetadata.UserRolePermissions"/>
        public ArrayOf<RolePermissionType> UserRolePermissions { get; }

        /// <inheritdoc cref="NodeMetadata.DefaultUserRolePermissions"/>
        public ArrayOf<RolePermissionType> DefaultUserRolePermissions { get; }
    }
}
