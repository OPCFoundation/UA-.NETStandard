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
        /// Returns true if the attribute id is valid.
        /// </summary>
        public static bool IsValid(uint attributeId)
        {
            return attributeId is >= NodeId and <= AccessLevelEx;
        }

        /// <summary>
        /// Returns the ids for all attributes which are valid for the at least one of
        /// the node classes specified by the mask.
        /// </summary>
        public static UInt32Collection GetIdentifiers(NodeClass nodeClass)
        {
            var ids = new UInt32Collection(s_idToName.Value.Count);

            foreach (uint id in s_idToName.Value.Keys)
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
                    return default;
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
    }
}
