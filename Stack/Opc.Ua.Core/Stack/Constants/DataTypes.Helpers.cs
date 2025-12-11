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
                    if (field.FieldType == typeof(uint))
                    {
                        uint value = Convert.ToUInt32(
                            field.GetValue(typeof(DataTypes)),
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
