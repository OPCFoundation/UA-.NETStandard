// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

namespace Opc.Ua.Types
{
    /// <summary>
    /// Attributes of opc ua nodes
    /// </summary>
    /// <exclude />
#if !INTERNAL
    public
#else
    internal
#endif
        static class Attributes
    {
        /// <summary>
        /// The canonical identifier for the node.
        /// </summary>
        public const uint NodeId = 1;

        /// <summary>
        /// The class of the node.
        /// </summary>
        public const uint NodeClass = 2;

        /// <summary>
        /// A non-localized, human readable name for the node.
        /// </summary>
        public const uint BrowseName = 3;

        /// <summary>
        /// A localized, human readable name for the node.
        /// </summary>
        public const uint DisplayName = 4;

        /// <summary>
        /// A localized description for the node.
        /// </summary>
        public const uint Description = 5;

        /// <summary>
        /// Indicates which attributes are writable.
        /// </summary>
        public const uint WriteMask = 6;

        /// <summary>
        /// Indicates which attributes are writable by the current user.
        /// </summary>
        public const uint UserWriteMask = 7;

        /// <summary>
        /// Indicates that a type node may not be instantiated.
        /// </summary>
        public const uint IsAbstract = 8;

        /// <summary>
        /// Indicates that forward and inverse references have the same meaning.
        /// </summary>
        public const uint Symmetric = 9;

        /// <summary>
        /// The browse name for an inverse reference.
        /// </summary>
        public const uint InverseName = 10;

        /// <summary>
        /// Indicates that following forward references within a view will not cause a loop.
        /// </summary>
        public const uint ContainsNoLoops = 11;

        /// <summary>
        /// Indicates that the node can be used to subscribe to events.
        /// </summary>
        public const uint EventNotifier = 12;

        /// <summary>
        /// The value of a variable.
        /// </summary>
        public const uint Value = 13;

        /// <summary>
        /// The node id of the data type for the variable value.
        /// </summary>
        public const uint DataType = 14;

        /// <summary>
        /// The number of dimensions in the value.
        /// </summary>
        public const uint ValueRank = 15;

        /// <summary>
        /// The length for each dimension of an array value.
        /// </summary>
        public const uint ArrayDimensions = 16;

        /// <summary>
        /// How a variable may be accessed.
        /// </summary>
        public const uint AccessLevel = 17;

        /// <summary>
        /// How a variable may be accessed after taking the user's access rights into account.
        /// </summary>
        public const uint UserAccessLevel = 18;

        /// <summary>
        /// Specifies (in milliseconds) how fast the server can reasonably sample the value for changes.
        /// </summary>
        public const uint MinimumSamplingInterval = 19;

        /// <summary>
        /// Specifies whether the server is actively collecting historical data for the variable.
        /// </summary>
        public const uint Historizing = 20;

        /// <summary>
        /// Whether the method can be called.
        /// </summary>
        public const uint Executable = 21;

        /// <summary>
        /// Whether the method can be called by the current user.
        /// </summary>
        public const uint UserExecutable = 22;

        /// <summary>
        /// Provides the metadata and encoding information for custom DataTypes.
        /// </summary>
        public const uint DataTypeDefinition = 23;

        /// <summary>
        /// The permissions for the node granted to roles.
        /// </summary>
        public const uint RolePermissions = 24;

        /// <summary>
        /// The subset of permissions available for the roles available to the current session.
        /// </summary>
        public const uint UserRolePermissions = 25;

        /// <summary>
        /// The access restrictions assigned to the node.
        /// </summary>
        public const uint AccessRestrictions = 26;

        /// <summary>
        /// How a variable may be accessed.
        /// </summary>
        public const uint AccessLevelEx = 27;

        /// <summary>
        /// Returns the data type id for the attribute.
        /// </summary>
        public static NodeId GetDataTypeId(uint attributeId)
        {
            switch (attributeId)
            {
                case Value:
                    return DataTypeIds.BaseDataType;
                case DisplayName:
                case Description:
                    return DataTypeIds.LocalizedText;
                case WriteMask:
                case UserWriteMask:
                    return DataTypeIds.UInt32;
                case NodeId:
                    return DataTypeIds.NodeId;
                case NodeClass:
                    return DataTypeIds.Enumeration;
                case BrowseName:
                    return DataTypeIds.QualifiedName;
                case IsAbstract:
                case Symmetric:
                    return DataTypeIds.Boolean;
                case InverseName:
                    return DataTypeIds.LocalizedText;
                case ContainsNoLoops:
                    return DataTypeIds.Boolean;
                case EventNotifier:
                    return DataTypeIds.Byte;
                case DataType:
                    return DataTypeIds.NodeId;
                case ValueRank:
                    return DataTypeIds.Int32;
                case AccessLevel:
                case UserAccessLevel:
                    return DataTypeIds.Byte;
                case MinimumSamplingInterval:
                    return DataTypeIds.Duration;
                case Historizing:
                case Executable:
                case UserExecutable:
                    return DataTypeIds.Boolean;
                case ArrayDimensions:
                    return DataTypeIds.UInt32;
                case DataTypeDefinition:
                    return DataTypeIds.Structure;
                case RolePermissions:
                case UserRolePermissions:
                    return DataTypeIds.RolePermissionType;
                case AccessRestrictions:
                    return DataTypeIds.UInt16;
                case AccessLevelEx:
                    return DataTypeIds.UInt32;
                default:
                    ThrowIfOutOfRange(attributeId);
                    return default;
            }
        }

        /// <summary>
        /// Throw if out of range
        /// </summary>
        /// <param name="attributeId"></param>
        /// <exception cref="ServiceResultException"></exception>
        public static void ThrowIfOutOfRange(uint attributeId)
        {
            if (attributeId is < NodeId or > AccessLevelEx)
            {
                throw ServiceResultException.Unexpected(
                    $"Invalid attribute id {attributeId}. This attribute is not defined.");
            }
        }
    }
}
