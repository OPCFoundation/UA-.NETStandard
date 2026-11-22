// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

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
