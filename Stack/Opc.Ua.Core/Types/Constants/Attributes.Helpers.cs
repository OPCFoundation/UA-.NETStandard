/* Copyright (c) 1996-2022 The OPC Foundation. All rights reserved.
   The source code in this file is covered under a dual-license scenario:
     - RCL: for OPC Foundation Corporate Members in good-standing
     - GPL V2: everybody else
   RCL license terms accompanied with this source code. See http://opcfoundation.org/License/RCL/1.00/
   GNU General Public License as published by the Free Software Foundation;
   version 2 of the License are accompanied with this source code. See http://opcfoundation.org/License/GPLv2
   This source code is distributed in the hope that it will be useful,
   but WITHOUT ANY WARRANTY; without even the implied warranty of
   MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.
*/

using System;
using System.Reflection;
using System.Collections.Generic;
#if NET8_0_OR_GREATER
using System.Collections.Frozen;
#else
using System.Collections.ObjectModel;
using System.Linq;
#endif

namespace Opc.Ua
{
    /// <summary>
    /// A class that defines constants used by UA applications.
    /// </summary>
    public static partial class Attributes
    {
        /// <summary>
        /// The maximum number of attributes defined and used by the stack.
        /// </summary>
        public const int MaxAttributes = (int)AccessLevelEx - (int)NodeId + 1;

        /// <summary>
        /// Returns the browse names for all attributes
        /// </summary>
        public static IEnumerable<string> BrowseNames => s_attributesNameToId.Value.Keys;

        /// <summary>
        /// Returns the ids for all attributes.
        /// </summary>
        public static IEnumerable<uint> Identifiers => s_attributesIdToName.Value.Keys;

        /// <summary>
        /// Returns true if the attribute id is valid.
        /// </summary>
        public static bool IsValid(uint attributeId)
        {
            return attributeId is >= NodeId and <= AccessLevelEx;
        }

        /// <summary>
        /// Returns the browse name for the attribute.
        /// </summary>
        public static string GetBrowseName(uint identifier)
        {
            return s_attributesIdToName.Value.TryGetValue(identifier, out string name)
                ? name : string.Empty;
        }

        /// <summary>
        /// Returns the browse names for all attributes.
        /// </summary>
        [Obsolete("Use BrowseNames property instead.")]
        public static string[] GetBrowseNames()
        {
            return [.. BrowseNames];
        }

        /// <summary>
        /// Returns the id for the attribute with the specified browse name.
        /// </summary>
        public static uint GetIdentifier(string browseName)
        {
            return s_attributesNameToId.Value.TryGetValue(browseName, out uint id)
                ? id : 0;
        }

        /// <summary>
        /// Returns the ids for all attributes.
        /// </summary>
        [Obsolete("Use Identifiers property instead.")]
        public static IEnumerable<uint> GetIdentifiers()
        {
            return [.. Identifiers];
        }

        /// <summary>
        /// Returns the ids for all attributes which are valid for the at least one of
        /// the node classes specified by the mask.
        /// </summary>
        public static UInt32Collection GetIdentifiers(NodeClass nodeClass)
        {
            var ids = new UInt32Collection(s_attributesIdToName.Value.Count);

            foreach (uint id in s_attributesIdToName.Value.Keys)
            {
                if (IsValid(nodeClass, id))
                {
                    ids.Add(id);
                }
            }

            return ids;
        }

        /// <summary>
        /// Returns the built type required for an attribute.
        /// </summary>
        public static BuiltInType GetBuiltInType(uint attributeId)
        {
            switch (attributeId)
            {
                case Value:
                    return BuiltInType.Variant;
                case DisplayName:
                case Description:
                    return BuiltInType.LocalizedText;
                case WriteMask:
                case UserWriteMask:
                    return BuiltInType.UInt32;
                case NodeId:
                    return BuiltInType.NodeId;
                case NodeClass:
                    return BuiltInType.Int32;
                case BrowseName:
                    return BuiltInType.QualifiedName;
                case IsAbstract:
                case Symmetric:
                    return BuiltInType.Boolean;
                case InverseName:
                    return BuiltInType.LocalizedText;
                case ContainsNoLoops:
                    return BuiltInType.Boolean;
                case EventNotifier:
                    return BuiltInType.Byte;
                case DataType:
                    return BuiltInType.NodeId;
                case ValueRank:
                    return BuiltInType.Int32;
                case AccessLevel:
                case UserAccessLevel:
                    return BuiltInType.Byte;
                case MinimumSamplingInterval:
                    return BuiltInType.Double;
                case Historizing:
                case Executable:
                case UserExecutable:
                    return BuiltInType.Boolean;
                case ArrayDimensions:
                    return BuiltInType.UInt32;
                case DataTypeDefinition:
                    return BuiltInType.ExtensionObject;
                case RolePermissions:
                case UserRolePermissions:
                    return BuiltInType.Variant;
                case AccessRestrictions:
                    return BuiltInType.UInt16;
                case AccessLevelEx:
                    return BuiltInType.UInt32;
                default:
                    ThrowIfOutOfRange(attributeId);
                    return BuiltInType.Null;
            }
        }

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
                    return null;
            }
        }

        /// <summary>
        /// Returns true if the corresponding bit is set in the attribute write mask.
        /// </summary>
        public static bool IsWriteable(uint attributeId, uint writeMask)
        {
            switch (attributeId)
            {
                case Value:
                    return (writeMask & (uint)AttributeWriteMask.ValueForVariableType) != 0;
                case DisplayName:
                    return (writeMask & (uint)AttributeWriteMask.DisplayName) != 0;
                case Description:
                    return (writeMask & (uint)AttributeWriteMask.Description) != 0;
                case WriteMask:
                    return (writeMask & (uint)AttributeWriteMask.WriteMask) != 0;
                case UserWriteMask:
                    return (writeMask & (uint)AttributeWriteMask.UserWriteMask) != 0;
                case NodeId:
                    return (writeMask & (uint)AttributeWriteMask.NodeId) != 0;
                case NodeClass:
                    return (writeMask & (uint)AttributeWriteMask.NodeClass) != 0;
                case BrowseName:
                    return (writeMask & (uint)AttributeWriteMask.BrowseName) != 0;
                case IsAbstract:
                    return (writeMask & (uint)AttributeWriteMask.IsAbstract) != 0;
                case Symmetric:
                    return (writeMask & (uint)AttributeWriteMask.Symmetric) != 0;
                case InverseName:
                    return (writeMask & (uint)AttributeWriteMask.InverseName) != 0;
                case ContainsNoLoops:
                    return (writeMask & (uint)AttributeWriteMask.ContainsNoLoops) != 0;
                case EventNotifier:
                    return (writeMask & (uint)AttributeWriteMask.EventNotifier) != 0;
                case DataType:
                    return (writeMask & (uint)AttributeWriteMask.DataType) != 0;
                case ValueRank:
                    return (writeMask & (uint)AttributeWriteMask.ValueRank) != 0;
                case AccessLevel:
                    return (writeMask & (uint)AttributeWriteMask.AccessLevel) != 0;
                case UserAccessLevel:
                    return (writeMask & (uint)AttributeWriteMask.UserAccessLevel) != 0;
                case MinimumSamplingInterval:
                    return (writeMask & (uint)AttributeWriteMask.MinimumSamplingInterval) != 0;
                case Historizing:
                    return (writeMask & (uint)AttributeWriteMask.Historizing) != 0;
                case Executable:
                    return (writeMask & (uint)AttributeWriteMask.Executable) != 0;
                case UserExecutable:
                    return (writeMask & (uint)AttributeWriteMask.UserExecutable) != 0;
                case ArrayDimensions:
                    return (writeMask & (uint)AttributeWriteMask.ArrayDimensions) != 0;
                case DataTypeDefinition:
                    return (writeMask & (uint)AttributeWriteMask.DataTypeDefinition) != 0;
                case RolePermissions:
                    return (writeMask & (uint)AttributeWriteMask.RolePermissions) != 0;
                case AccessRestrictions:
                    return (writeMask & (uint)AttributeWriteMask.AccessRestrictions) != 0;
                case AccessLevelEx:
                    return (writeMask & (uint)AttributeWriteMask.AccessLevelEx) != 0;
                default:
                    ThrowIfOutOfRange(attributeId);
                    return false;
            }
        }

        /// <summary>
        /// Sets the corresponding bit in the attribute write mask and returns the result.
        /// </summary>
        public static uint SetWriteable(uint attributeId, uint writeMask)
        {
            switch (attributeId)
            {
                case Value:
                    return writeMask | (uint)AttributeWriteMask.ValueForVariableType;
                case DisplayName:
                    return writeMask | (uint)AttributeWriteMask.DisplayName;
                case Description:
                    return writeMask | (uint)AttributeWriteMask.Description;
                case WriteMask:
                    return writeMask | (uint)AttributeWriteMask.WriteMask;
                case UserWriteMask:
                    return writeMask | (uint)AttributeWriteMask.UserWriteMask;
                case NodeId:
                    return writeMask | (uint)AttributeWriteMask.NodeId;
                case NodeClass:
                    return writeMask | (uint)AttributeWriteMask.NodeClass;
                case BrowseName:
                    return writeMask | (uint)AttributeWriteMask.BrowseName;
                case IsAbstract:
                    return writeMask | (uint)AttributeWriteMask.IsAbstract;
                case Symmetric:
                    return writeMask | (uint)AttributeWriteMask.Symmetric;
                case InverseName:
                    return writeMask | (uint)AttributeWriteMask.InverseName;
                case ContainsNoLoops:
                    return writeMask | (uint)AttributeWriteMask.ContainsNoLoops;
                case EventNotifier:
                    return writeMask | (uint)AttributeWriteMask.EventNotifier;
                case DataType:
                    return writeMask | (uint)AttributeWriteMask.DataType;
                case ValueRank:
                    return writeMask | (uint)AttributeWriteMask.ValueRank;
                case AccessLevel:
                    return writeMask | (uint)AttributeWriteMask.AccessLevel;
                case UserAccessLevel:
                    return writeMask | (uint)AttributeWriteMask.UserAccessLevel;
                case MinimumSamplingInterval:
                    return writeMask | (uint)AttributeWriteMask.MinimumSamplingInterval;
                case Historizing:
                    return writeMask | (uint)AttributeWriteMask.Historizing;
                case Executable:
                    return writeMask | (uint)AttributeWriteMask.Executable;
                case UserExecutable:
                    return writeMask | (uint)AttributeWriteMask.UserExecutable;
                case ArrayDimensions:
                    return writeMask | (uint)AttributeWriteMask.ArrayDimensions;
                case DataTypeDefinition:
                    return writeMask | (uint)AttributeWriteMask.DataTypeDefinition;
                case RolePermissions:
                    return writeMask | (uint)AttributeWriteMask.RolePermissions;
                case AccessRestrictions:
                    return writeMask | (uint)AttributeWriteMask.AccessRestrictions;
                case AccessLevelEx:
                    return writeMask | (uint)AttributeWriteMask.AccessLevelEx;
                default:
                    ThrowIfOutOfRange(attributeId);
                    return writeMask;
            }
        }

        /// <summary>
        /// Returns the value rank for the attribute.
        /// </summary>
        public static int GetValueRank(uint attributeId)
        {
            if (attributeId == Value)
            {
                return ValueRanks.Any;
            }

            if (attributeId == ArrayDimensions)
            {
                return ValueRanks.OneDimension;
            }

            return ValueRanks.Scalar;
        }

        /// <summary>
        /// Checks if the attribute is valid for at least one of node classes specified in the mask.
        /// </summary>
        public static bool IsValid(NodeClass nodeClassMask, uint attributeId)
        {
            int nodeClass = (int)nodeClassMask;
            switch (attributeId)
            {
                case NodeId:
                case NodeClass:
                case BrowseName:
                case DisplayName:
                case Description:
                case WriteMask:
                case UserWriteMask:
                case RolePermissions:
                case UserRolePermissions:
                case AccessRestrictions:
                    return true;
                case Value:
                case DataType:
                case ValueRank:
                case ArrayDimensions:
                    return (nodeClass &
                        (
                            (int)Ua.NodeClass.VariableType |
                            (int)Ua.NodeClass.Variable)
                        ) != 0;
                case IsAbstract:
                    return (nodeClass &
                        (
                            (int)Ua.NodeClass.VariableType |
                            (int)Ua.NodeClass.ObjectType |
                            (int)Ua.NodeClass.ReferenceType |
                            (int)Ua.NodeClass.DataType)
                        ) != 0;
                case Symmetric:
                case InverseName:
                    return (nodeClass & (int)Ua.NodeClass.ReferenceType) != 0;
                case ContainsNoLoops:
                    return (nodeClass & (int)Ua.NodeClass.View) != 0;
                case EventNotifier:
                    return (nodeClass &
                        (
                            (int)Ua.NodeClass.Object |
                            (int)Ua.NodeClass.View)
                        ) != 0;
                case AccessLevel:
                case UserAccessLevel:
                case MinimumSamplingInterval:
                case Historizing:
                case AccessLevelEx:
                    return (nodeClass & (int)Ua.NodeClass.Variable) != 0;
                case Executable:
                case UserExecutable:
                    return (nodeClass & (int)Ua.NodeClass.Method) != 0;
                case DataTypeDefinition:
                    return (nodeClass & (int)Ua.NodeClass.DataType) != 0;
                default:
                    ThrowIfOutOfRange(attributeId);
                    return false;
            }
        }

        /// <summary>
        /// Returns the AttributeWriteMask for the attribute.
        /// </summary>
        public static AttributeWriteMask GetMask(uint attributeId)
        {
            switch (attributeId)
            {
                case NodeId:
                    return AttributeWriteMask.NodeId;
                case NodeClass:
                    return AttributeWriteMask.NodeClass;
                case BrowseName:
                    return AttributeWriteMask.BrowseName;
                case DisplayName:
                    return AttributeWriteMask.DisplayName;
                case Description:
                    return AttributeWriteMask.Description;
                case WriteMask:
                    return AttributeWriteMask.WriteMask;
                case UserWriteMask:
                    return AttributeWriteMask.UserWriteMask;
                case DataType:
                    return AttributeWriteMask.DataType;
                case ValueRank:
                    return AttributeWriteMask.ValueRank;
                case ArrayDimensions:
                    return AttributeWriteMask.ArrayDimensions;
                case IsAbstract:
                    return AttributeWriteMask.IsAbstract;
                case Symmetric:
                    return AttributeWriteMask.Symmetric;
                case InverseName:
                    return AttributeWriteMask.InverseName;
                case ContainsNoLoops:
                    return AttributeWriteMask.ContainsNoLoops;
                case EventNotifier:
                    return AttributeWriteMask.EventNotifier;
                case AccessLevel:
                    return AttributeWriteMask.AccessLevel;
                case UserAccessLevel:
                    return AttributeWriteMask.UserAccessLevel;
                case MinimumSamplingInterval:
                    return AttributeWriteMask.MinimumSamplingInterval;
                case Historizing:
                    return AttributeWriteMask.Historizing;
                case Executable:
                    return AttributeWriteMask.Executable;
                case UserExecutable:
                    return AttributeWriteMask.UserExecutable;
                case DataTypeDefinition:
                    return AttributeWriteMask.DataTypeDefinition;
                case RolePermissions:
                    return AttributeWriteMask.RolePermissions;
                //case UserRolePermissions:
                //  return AttributeWriteMask.UserRolePermissions;
                case AccessRestrictions:
                    return AttributeWriteMask.AccessRestrictions;
                case AccessLevelEx:
                    return AttributeWriteMask.AccessLevelEx;
                default:
                    ThrowIfOutOfRange(attributeId);
                    return 0;
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

        /// <summary>
        /// Creates a dictionary of names to attribute identifiers
        /// </summary>
        private static readonly Lazy<IReadOnlyDictionary<string, uint>> s_attributesNameToId =
            new(() =>
            {
#if NET8_0_OR_GREATER
                return s_attributesIdToName.Value.ToFrozenDictionary(k => k.Value, k => k.Key);
#else
                return new ReadOnlyDictionary<string, uint>(
                    s_attributesIdToName.Value.ToDictionary(k => k.Value, k => k.Key));
#endif
            });

        /// <summary>
        /// Creates a dictionary of identifiers to browse names for the attributes.
        /// </summary>
        private static readonly Lazy<IReadOnlyDictionary<uint, string>> s_attributesIdToName =
            new(() =>
            {
                FieldInfo[] fields = typeof(Attributes).GetFields(
                    BindingFlags.Public | BindingFlags.Static);

                var keyValuePairs = new Dictionary<uint, string>();
                foreach (FieldInfo field in fields)
                {
                    if (field.FieldType == typeof(uint))
                    {
                        uint value = Convert.ToUInt32(
                            field.GetValue(typeof(Attributes)),
                            System.Globalization.CultureInfo.InvariantCulture);
                        keyValuePairs.Add(value, field.Name);
                    }
                }
#if NET8_0_OR_GREATER
                return keyValuePairs.ToFrozenDictionary();
#else
                return new ReadOnlyDictionary<uint, string>(keyValuePairs);
#endif
            });
    }
}
