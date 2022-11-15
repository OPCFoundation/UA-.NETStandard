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
using System.Xml;

namespace Opc.Ua
{
	/// <summary>
	/// A class that defines constants used by UA applications.
	/// </summary>
	public static partial class DataTypes
	{
        #region Static Helper Functions
        /// <summary>
		/// Returns the browse name for the attribute.
		/// </summary>
        public static string GetBrowseName(int identifier)
		{
			FieldInfo[] fields = typeof(DataTypes).GetFields(BindingFlags.Public | BindingFlags.Static);

			foreach (FieldInfo field in fields)
			{
                if (identifier == (uint)field.GetValue(typeof(DataTypes)))
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
			FieldInfo[] fields = typeof(DataTypes).GetFields(BindingFlags.Public | BindingFlags.Static);
            
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
			FieldInfo[] fields = typeof(DataTypes).GetFields(BindingFlags.Public | BindingFlags.Static);

			foreach (FieldInfo field in fields)
			{
				if (field.Name == browseName)
				{
                    return (uint)field.GetValue(typeof(DataTypes));
				}
			}

			return 0;
        }

        /// <summary>
        /// Returns the data type id that describes a value.
        /// </summary>
        public static NodeId GetDataTypeId(object value)
        {
            return TypeInfo.GetDataTypeId(value);
        }

        /// <summary>
        /// Returns the data type id that describes a value.
        /// </summary>
        public static NodeId GetDataTypeId(Type type)
        {
            return TypeInfo.GetDataTypeId(type);
        }

        /// <summary>
        /// Returns the data type id that describes a value.
        /// </summary>
        public static NodeId GetDataTypeId(TypeInfo typeInfo)
        {
            return TypeInfo.GetDataTypeId(typeInfo);
        }

        /// <summary>
        /// Returns the array rank for a value.
        /// </summary>
        public static int GetValueRank(object value)
        {
            return TypeInfo.GetValueRank(value);
        }

        /// <summary>
        /// Returns the array rank for a type.
        /// </summary>
        public static int GetValueRank(Type type)
        {
            return TypeInfo.GetValueRank(type);
        }
        
        /// <summary>
        /// Returns the BuiltInType type for the DataTypeId.
        /// </summary>
        public static BuiltInType GetBuiltInType(NodeId datatypeId)
        {
            return TypeInfo.GetBuiltInType(datatypeId);
        }        
        
        /// <summary>
        /// Returns the BuiltInType type for the DataTypeId.
        /// </summary>
        public static BuiltInType GetBuiltInType(NodeId datatypeId, ITypeTable typeTree)
        {
            return TypeInfo.GetBuiltInType(datatypeId, typeTree);
        }

        /// <summary>
        /// Returns the system type for the datatype.
        /// </summary>
        public static Type GetSystemType(NodeId datatypeId, IEncodeableFactory factory)
        {
            return TypeInfo.GetSystemType(datatypeId, factory);
        }
        #endregion
    }
}
