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
using System.Collections.Generic;
using System.Reflection;

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
    public static partial class DataTypes
    {
        /// <summary>
        /// Returns the browse names for all data types.
        /// </summary>
        public static IEnumerable<string> BrowseNames => s_dataTypeNameToId.Value.Keys;

        /// <summary>
        /// Returns the browse name for the data type id.
        /// </summary>
        public static string GetBrowseName(int identifier)
        {
            return s_dataTypeIdToName.Value.TryGetValue((uint)identifier, out string name)
                ? name : string.Empty;
        }

        /// <summary>
        /// Returns the browse names for all data types.
        /// </summary>
        [Obsolete("Use BrowseNames property instead.")]
        public static string[] GetBrowseNames()
        {
            return [.. BrowseNames];
        }

        /// <summary>
        /// Returns the id for the data type with the specified browse name.
        /// </summary>
        public static uint GetIdentifier(string browseName)
        {
            return s_dataTypeNameToId.Value.TryGetValue(browseName, out uint id)
                ? id : 0;
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

        /// <summary>
        /// Creates a dictionary of data type names to identifiers
        /// </summary>
        private static readonly Lazy<IReadOnlyDictionary<string, uint>> s_dataTypeNameToId =
            new(() =>
            {
#if NET8_0_OR_GREATER
                return s_dataTypeIdToName.Value.ToFrozenDictionary(k => k.Value, k => k.Key);
#else
                return new ReadOnlyDictionary<string, uint>(
                    s_dataTypeIdToName.Value.ToDictionary(k => k.Value, k => k.Key));
#endif
            });

        /// <summary>
        /// Creates a dictionary of data type identifers to browse names.
        /// </summary>
        private static readonly Lazy<IReadOnlyDictionary<uint, string>> s_dataTypeIdToName =
            new(() =>
            {
                FieldInfo[] fields = typeof(DataTypes).GetFields(
                    BindingFlags.Public | BindingFlags.Static);

                var keyValuePairs = new Dictionary<uint, string>();
                foreach (FieldInfo field in fields)
                {
                    keyValuePairs.Add((uint)field.GetValue(typeof(DataTypes)), field.Name);
                }
#if NET8_0_OR_GREATER
                return keyValuePairs.ToFrozenDictionary();
#else
                return new ReadOnlyDictionary<uint, string>(keyValuePairs);
#endif
            });
    }
}
