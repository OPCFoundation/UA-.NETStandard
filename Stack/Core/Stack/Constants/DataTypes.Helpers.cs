/* ========================================================================
 * Copyright (c) 2005-2013 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Reciprocal Community License ("RCL") Version 1.00
 * 
 * Unless explicitly acquired and licensed from Licensor under another 
 * license, the contents of this file are subject to the Reciprocal 
 * Community License ("RCL") Version 1.00, or subsequent versions 
 * as allowed by the RCL, and You may not copy or use this file in either 
 * source code or executable form, except in compliance with the terms and 
 * conditions of the RCL.
 * 
 * All software distributed under the RCL is provided strictly on an 
 * "AS IS" basis, WITHOUT WARRANTY OF ANY KIND, EITHER EXPRESS OR IMPLIED, 
 * AND LICENSOR HEREBY DISCLAIMS ALL SUCH WARRANTIES, INCLUDING WITHOUT 
 * LIMITATION, ANY WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR 
 * PURPOSE, QUIET ENJOYMENT, OR NON-INFRINGEMENT. See the RCL for specific 
 * language governing rights and limitations under the RCL.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/RCL/1.00/
 * ======================================================================*/

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
                if (identifier == (int)field.GetValue(typeof(DataTypes)))
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
        public static Type GetSystemType(NodeId datatypeId, EncodeableFactory factory)
        {
            return TypeInfo.GetSystemType(datatypeId, factory);
        }
        #endregion
    }
}
