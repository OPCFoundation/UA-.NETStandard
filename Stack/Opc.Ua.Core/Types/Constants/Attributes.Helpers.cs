/* Copyright (c) 1996-2020 The OPC Foundation. All rights reserved.
   The source code in this file is covered under a dual-license scenario:
     - RCL: for OPC Foundation members in good-standing
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
using System.Xml;

namespace Opc.Ua
{            
	/// <summary>
	/// A class that defines constants used by UA applications.
	/// </summary>
    public static partial class Attributes
    {        
        #region Static Helper Functions
        /// <summary>
		/// Returns true is the attribute id is valid.
		/// </summary>
        public static bool IsValid(uint attributeId)
		{
            return (attributeId >= Attributes.NodeId && attributeId <= Attributes.AccessLevelEx);
        }

        /// <summary>
		/// Returns the browse name for the attribute.
		/// </summary>
        public static string GetBrowseName(uint identifier)
		{
			FieldInfo[] fields = typeof(Attributes).GetFields(BindingFlags.Public | BindingFlags.Static);

			foreach (FieldInfo field in fields)
			{
                if (identifier == (uint)field.GetValue(typeof(Attributes)))
				{
					return field.Name;
				}
			}

			return System.String.Empty;
		}

		/// <summary>
		/// Returns the browse names for all attributes.
		/// </summary>
		public static string[] GetBrowseNames()
		{
			FieldInfo[] fields = typeof(Attributes).GetFields(BindingFlags.Public | BindingFlags.Static);
            
            int ii = 0;

            string[] names = new string[fields.Length];
            
			foreach (FieldInfo field in fields)
			{
				names[ii++] = field.Name;
			}

			return names;
		}

		/// <summary>
		/// Returns the id for the attribute with the specified browse name.
		/// </summary>
        public static uint GetIdentifier(string browseName)
		{
			FieldInfo[] fields = typeof(Attributes).GetFields(BindingFlags.Public | BindingFlags.Static);

			foreach (FieldInfo field in fields)
			{
				if (field.Name == browseName)
				{
                    return (uint)field.GetValue(typeof(Attributes));
				}
			}

			return 0;
        }

		/// <summary>
		/// Returns the ids for all attributes.
		/// </summary>
		public static uint[] GetIdentifiers()
		{
			FieldInfo[] fields = typeof(Attributes).GetFields(BindingFlags.Public | BindingFlags.Static);
            
            int ii = 0;
            uint[] ids = new uint[fields.Length];
            
			foreach (FieldInfo field in fields)
			{
                ids[ii++] = (uint)field.GetValue(typeof(Attributes));
			}

			return ids;
		}
        
		/// <summary>
		/// Returns the ids for all attributes which are valid for the at least one of the node classes specified by the mask.
		/// </summary>
        public static UInt32Collection GetIdentifiers(NodeClass nodeClass)
        {
			FieldInfo[] fields = typeof(Attributes).GetFields(BindingFlags.Public | BindingFlags.Static);
            
            UInt32Collection ids = new UInt32Collection(fields.Length);
            
			foreach (FieldInfo field in fields)
			{
                uint id = (uint)field.GetValue(typeof(Attributes));

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
                case Value:                   return BuiltInType.Variant;
                case DisplayName:             return BuiltInType.LocalizedText;
                case Description:             return BuiltInType.LocalizedText;
                case WriteMask:               return BuiltInType.UInt32;
                case UserWriteMask:           return BuiltInType.UInt32;
                case NodeId:                  return BuiltInType.NodeId;
                case NodeClass:               return BuiltInType.Int32;
                case BrowseName:              return BuiltInType.QualifiedName;
                case IsAbstract:              return BuiltInType.Boolean;
                case Symmetric:               return BuiltInType.Boolean;
                case InverseName:             return BuiltInType.LocalizedText;
                case ContainsNoLoops:         return BuiltInType.Boolean;
                case EventNotifier:           return BuiltInType.Byte;
                case DataType:                return BuiltInType.NodeId;
                case ValueRank:               return BuiltInType.Int32;
                case AccessLevel:             return BuiltInType.Byte;
                case UserAccessLevel:         return BuiltInType.Byte;
                case MinimumSamplingInterval: return BuiltInType.Double;
                case Historizing:             return BuiltInType.Boolean;
                case Executable:              return BuiltInType.Boolean;
                case UserExecutable:          return BuiltInType.Boolean;
                case ArrayDimensions:         return BuiltInType.UInt32;
                case DataTypeDefinition:      return BuiltInType.ExtensionObject;
                case RolePermissions:         return BuiltInType.Variant;
                case UserRolePermissions:     return BuiltInType.Variant;
                case AccessRestrictions:      return BuiltInType.UInt16;
                case AccessLevelEx:           return BuiltInType.UInt32;
            }
                    
            return BuiltInType.Null;
        }
        
        /// <summary>
        /// Returns the data type id for the attribute.
        /// </summary>
        public static NodeId GetDataTypeId(uint attributeId)
        {
            switch (attributeId)
            {
                case Value:                   return DataTypes.BaseDataType;
                case DisplayName:             return DataTypes.LocalizedText;
                case Description:             return DataTypes.LocalizedText;
                case WriteMask:               return DataTypes.UInt32;
                case UserWriteMask:           return DataTypes.UInt32;
                case NodeId:                  return DataTypes.NodeId;
                case NodeClass:               return DataTypes.Enumeration;
                case BrowseName:              return DataTypes.QualifiedName;
                case IsAbstract:              return DataTypes.Boolean;
                case Symmetric:               return DataTypes.Boolean;
                case InverseName:             return DataTypes.LocalizedText;
                case ContainsNoLoops:         return DataTypes.Boolean;
                case EventNotifier:           return DataTypes.Byte;
                case DataType:                return DataTypes.NodeId;
                case ValueRank:               return DataTypes.Int32;
                case AccessLevel:             return DataTypes.Byte;
                case UserAccessLevel:         return DataTypes.Byte;
                case MinimumSamplingInterval: return DataTypes.Duration;
                case Historizing:             return DataTypes.Boolean;
                case Executable:              return DataTypes.Boolean;
                case UserExecutable:          return DataTypes.Boolean;
                case ArrayDimensions:         return DataTypes.UInt32;
                case DataTypeDefinition:      return DataTypes.Structure;
                case RolePermissions:         return DataTypes.RolePermissionType;
                case UserRolePermissions:     return DataTypes.RolePermissionType;
                case AccessRestrictions:      return DataTypes.UInt16;
                case AccessLevelEx:           return DataTypes.UInt32;
            }
                    
            return null;
        }
        
        /// <summary>
        /// Returns true if the corresponding bit is set in the attribute write mask.
        /// </summary>
        public static bool IsWriteable(uint attributeId, uint writeMask)
        {
            switch (attributeId)
            {
                case Value:                   return (writeMask & (uint)AttributeWriteMask.ValueForVariableType) != 0;
                case DisplayName:             return (writeMask & (uint)AttributeWriteMask.DisplayName) != 0;
                case Description:             return (writeMask & (uint)AttributeWriteMask.Description) != 0;
                case WriteMask:               return (writeMask & (uint)AttributeWriteMask.WriteMask) != 0;
                case UserWriteMask:           return (writeMask & (uint)AttributeWriteMask.UserWriteMask) != 0;
                case NodeId:                  return (writeMask & (uint)AttributeWriteMask.NodeId) != 0;
                case NodeClass:               return (writeMask & (uint)AttributeWriteMask.NodeClass) != 0;
                case BrowseName:              return (writeMask & (uint)AttributeWriteMask.BrowseName) != 0;
                case IsAbstract:              return (writeMask & (uint)AttributeWriteMask.IsAbstract) != 0;
                case Symmetric:               return (writeMask & (uint)AttributeWriteMask.Symmetric) != 0;
                case InverseName:             return (writeMask & (uint)AttributeWriteMask.InverseName) != 0;
                case ContainsNoLoops:         return (writeMask & (uint)AttributeWriteMask.ContainsNoLoops) != 0;
                case EventNotifier:           return (writeMask & (uint)AttributeWriteMask.EventNotifier) != 0;
                case DataType:                return (writeMask & (uint)AttributeWriteMask.DataType) != 0;
                case ValueRank:               return (writeMask & (uint)AttributeWriteMask.ValueRank) != 0;
                case AccessLevel:             return (writeMask & (uint)AttributeWriteMask.AccessLevel) != 0;
                case UserAccessLevel:         return (writeMask & (uint)AttributeWriteMask.UserAccessLevel) != 0;
                case MinimumSamplingInterval: return (writeMask & (uint)AttributeWriteMask.MinimumSamplingInterval) != 0;
                case Historizing:             return (writeMask & (uint)AttributeWriteMask.Historizing) != 0;
                case Executable:              return (writeMask & (uint)AttributeWriteMask.Executable) != 0;
                case UserExecutable:          return (writeMask & (uint)AttributeWriteMask.UserExecutable) != 0;
                case ArrayDimensions:         return (writeMask & (uint)AttributeWriteMask.ArrayDimensions) != 0;
                case DataTypeDefinition:      return (writeMask & (uint)AttributeWriteMask.DataTypeDefinition) != 0;
                case RolePermissions:         return (writeMask & (uint)AttributeWriteMask.RolePermissions) != 0;
                case AccessRestrictions:      return (writeMask & (uint)AttributeWriteMask.AccessRestrictions) != 0;
                case AccessLevelEx:           return (writeMask & (uint)AttributeWriteMask.AccessLevelEx) != 0;
            }
                    
            return false;
        }
        
        /// <summary>
        /// Sets the corresponding bit in the attribute write mask and returns the result.
        /// </summary>
        public static uint SetWriteable(uint attributeId, uint writeMask)
        {
            switch (attributeId)
            {
                case Value:                   return writeMask | (uint)AttributeWriteMask.ValueForVariableType;
                case DisplayName:             return writeMask | (uint)AttributeWriteMask.DisplayName;
                case Description:             return writeMask | (uint)AttributeWriteMask.Description;
                case WriteMask:               return writeMask | (uint)AttributeWriteMask.WriteMask;
                case UserWriteMask:           return writeMask | (uint)AttributeWriteMask.UserWriteMask;
                case NodeId:                  return writeMask | (uint)AttributeWriteMask.NodeId;
                case NodeClass:               return writeMask | (uint)AttributeWriteMask.NodeClass;
                case BrowseName:              return writeMask | (uint)AttributeWriteMask.BrowseName;
                case IsAbstract:              return writeMask | (uint)AttributeWriteMask.IsAbstract;
                case Symmetric:               return writeMask | (uint)AttributeWriteMask.Symmetric;
                case InverseName:             return writeMask | (uint)AttributeWriteMask.InverseName;
                case ContainsNoLoops:         return writeMask | (uint)AttributeWriteMask.ContainsNoLoops;
                case EventNotifier:           return writeMask | (uint)AttributeWriteMask.EventNotifier;
                case DataType:                return writeMask | (uint)AttributeWriteMask.DataType;
                case ValueRank:               return writeMask | (uint)AttributeWriteMask.ValueRank;
                case AccessLevel:             return writeMask | (uint)AttributeWriteMask.AccessLevel;
                case UserAccessLevel:         return writeMask | (uint)AttributeWriteMask.UserAccessLevel;
                case MinimumSamplingInterval: return writeMask | (uint)AttributeWriteMask.MinimumSamplingInterval;
                case Historizing:             return writeMask | (uint)AttributeWriteMask.Historizing;
                case Executable:              return writeMask | (uint)AttributeWriteMask.Executable;
                case UserExecutable:          return writeMask | (uint)AttributeWriteMask.UserExecutable;
                case ArrayDimensions:         return writeMask | (uint)AttributeWriteMask.ArrayDimensions;
                case DataTypeDefinition:      return writeMask | (uint)AttributeWriteMask.DataTypeDefinition;
                case RolePermissions:         return writeMask | (uint)AttributeWriteMask.RolePermissions;
                case AccessRestrictions:      return writeMask | (uint)AttributeWriteMask.AccessRestrictions;
                case AccessLevelEx:           return writeMask | (uint)AttributeWriteMask.AccessLevelEx;
            }

            return writeMask;
        }
        
        /// <summary>
        /// Returns the value rank for the attribute.
        /// </summary>
        public static int GetValueRank(uint attributeId)
        {
            if (attributeId == Attributes.Value)
            {
                return ValueRanks.Any;
            }
            
            if (attributeId == Attributes.ArrayDimensions)
            {
                return ValueRanks.OneDimension;
            }

            return ValueRanks.Scalar;
        }

        /// <summary>
        /// Checks if the attribute is valid for at least one of node classes specified in the mask.
        /// </summary>
        public static bool IsValid(NodeClass nodeClass, uint attributeId)
        {
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
                {
                    return true;
                }

                case Value:
                case DataType:
                case ValueRank:
                case ArrayDimensions:
                {
                    return (nodeClass & (Opc.Ua.NodeClass.VariableType | Opc.Ua.NodeClass.Variable)) != 0;
                }

                case IsAbstract:
                {
                    return (nodeClass & (Opc.Ua.NodeClass.VariableType | Opc.Ua.NodeClass.ObjectType | Opc.Ua.NodeClass.ReferenceType | Opc.Ua.NodeClass.DataType)) != 0;
                }

                case Symmetric:
                case InverseName:
                {
                    return (nodeClass & Opc.Ua.NodeClass.ReferenceType) != 0;
                }

                case ContainsNoLoops:
                {
                    return (nodeClass & Opc.Ua.NodeClass.View) != 0;
                }

                case EventNotifier:
                {
                    return (nodeClass & (Opc.Ua.NodeClass.Object | Opc.Ua.NodeClass.View)) != 0;
                } 
                    
                case AccessLevel:
                case UserAccessLevel:
                case MinimumSamplingInterval:
                case Historizing:
                case AccessLevelEx:
                {
                    return (nodeClass & Opc.Ua.NodeClass.Variable) != 0;
                } 

                case Executable:
                case UserExecutable:
                {
                    return (nodeClass & Opc.Ua.NodeClass.Method) != 0;
                }

                case DataTypeDefinition:
                {
                    return (nodeClass & Opc.Ua.NodeClass.DataType) != 0;
                }
            }

            return false;
        }

        /// <summary>
        /// Returns the AttributeWriteMask for the attribute.
        /// </summary>
        public static AttributeWriteMask GetMask(uint attributeId)
        {
            switch (attributeId)
            {
                case NodeId:                  return AttributeWriteMask.NodeId;
                case NodeClass:               return AttributeWriteMask.NodeClass;
                case BrowseName:              return AttributeWriteMask.BrowseName;
                case DisplayName:             return AttributeWriteMask.DisplayName;
                case Description:             return AttributeWriteMask.Description;
                case WriteMask:               return AttributeWriteMask.WriteMask;
                case UserWriteMask:           return AttributeWriteMask.UserWriteMask;
                case DataType:                return AttributeWriteMask.DataType;
                case ValueRank:               return AttributeWriteMask.ValueRank;
                case ArrayDimensions:         return AttributeWriteMask.ArrayDimensions;
                case IsAbstract:              return AttributeWriteMask.IsAbstract;
                case Symmetric:               return AttributeWriteMask.Symmetric;
                case InverseName:             return AttributeWriteMask.InverseName;
                case ContainsNoLoops:         return AttributeWriteMask.ContainsNoLoops;
                case EventNotifier:           return AttributeWriteMask.EventNotifier;
                case AccessLevel:             return AttributeWriteMask.AccessLevel;
                case UserAccessLevel:         return AttributeWriteMask.UserAccessLevel;
                case MinimumSamplingInterval: return AttributeWriteMask.MinimumSamplingInterval;
                case Historizing:             return AttributeWriteMask.Historizing;
                case Executable:              return AttributeWriteMask.Executable;
                case UserExecutable:          return AttributeWriteMask.UserExecutable;
                case DataTypeDefinition:      return AttributeWriteMask.DataTypeDefinition;
                case RolePermissions:         return AttributeWriteMask.RolePermissions;
                //case UserRolePermissions:     return AttributeWriteMask.UserRolePermissions;
                case AccessRestrictions:      return AttributeWriteMask.AccessRestrictions;
                case AccessLevelEx:           return AttributeWriteMask.AccessLevelEx;
            }

            return 0;
        }
        #endregion
    }
}
