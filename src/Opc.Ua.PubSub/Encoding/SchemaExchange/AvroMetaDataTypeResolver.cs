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

namespace Opc.Ua.PubSub.Encoding
{
    /// <summary>
    /// Resolves the custom DataTypes declared inside a <see cref="DataSetMetaDataType"/> so a
    /// schema can be generated without an AddressSpace (§6.7).
    /// </summary>
    /// <remarks>
    /// A DataSetMetaData is self-contained for schema generation because it carries the field list
    /// together with the definitions of the DataTypes those fields use: `StructureDataTypes`
    /// (§5.6, §5.7), `EnumDataTypes` (§5.3) and `SimpleDataTypes` (§5.2).
    /// </remarks>
    internal sealed class AvroMetaDataTypeResolver
    {
        private readonly Dictionary<string, StructureDescription> _structures =
            new(StringComparer.Ordinal);
        private readonly Dictionary<string, EnumDescription> _enums =
            new(StringComparer.Ordinal);
        private readonly Dictionary<string, SimpleTypeDescription> _simpleTypes =
            new(StringComparer.Ordinal);

        /// <summary>
        /// Initializes a new instance of the <see cref="AvroMetaDataTypeResolver"/> class.
        /// </summary>
        /// <param name="metaData">The DataSetMetaData carrying the type descriptions.</param>
        public AvroMetaDataTypeResolver(DataSetMetaDataType? metaData)
        {
            if (metaData is null)
            {
                return;
            }
            if (!metaData.StructureDataTypes.IsNull)
            {
                foreach (StructureDescription description in metaData.StructureDataTypes)
                {
                    if (!description.DataTypeId.IsNull)
                    {
                        _structures[Key(description.DataTypeId)] = description;
                    }
                }
            }
            if (!metaData.EnumDataTypes.IsNull)
            {
                foreach (EnumDescription description in metaData.EnumDataTypes)
                {
                    if (!description.DataTypeId.IsNull)
                    {
                        _enums[Key(description.DataTypeId)] = description;
                    }
                }
            }
            if (!metaData.SimpleDataTypes.IsNull)
            {
                foreach (SimpleTypeDescription description in metaData.SimpleDataTypes)
                {
                    if (!description.DataTypeId.IsNull)
                    {
                        _simpleTypes[Key(description.DataTypeId)] = description;
                    }
                }
            }
        }

        /// <summary>
        /// Gets a value indicating whether the resolver carries no type descriptions.
        /// </summary>
        public bool IsEmpty =>
            _structures.Count == 0 && _enums.Count == 0 && _simpleTypes.Count == 0;

        /// <summary>
        /// Attempts to resolve a DataType NodeId to a structured type description.
        /// </summary>
        /// <param name="dataType">The DataType NodeId.</param>
        /// <param name="description">The resolved description when found.</param>
        /// <returns><see langword="true"/> when the DataType is a declared structure.</returns>
        public bool TryGetStructure(NodeId dataType, out StructureDescription? description)
        {
            description = null;
            return !dataType.IsNull && _structures.TryGetValue(Key(dataType), out description);
        }

        /// <summary>
        /// Attempts to resolve a DataType NodeId to an enumeration description.
        /// </summary>
        /// <param name="dataType">The DataType NodeId.</param>
        /// <param name="description">The resolved description when found.</param>
        /// <returns><see langword="true"/> when the DataType is a declared enumeration.</returns>
        public bool TryGetEnum(NodeId dataType, out EnumDescription? description)
        {
            description = null;
            return !dataType.IsNull && _enums.TryGetValue(Key(dataType), out description);
        }

        /// <summary>
        /// Attempts to resolve a DataType NodeId to a simple type description.
        /// </summary>
        /// <param name="dataType">The DataType NodeId.</param>
        /// <param name="description">The resolved description when found.</param>
        /// <returns><see langword="true"/> when the DataType is a declared simple type.</returns>
        public bool TryGetSimpleType(NodeId dataType, out SimpleTypeDescription? description)
        {
            description = null;
            return !dataType.IsNull && _simpleTypes.TryGetValue(Key(dataType), out description);
        }

        /// <summary>
        /// Gets a value indicating whether the DataType is declared by any description.
        /// </summary>
        /// <param name="dataType">The DataType NodeId.</param>
        /// <returns><see langword="true"/> when the DataType is declared.</returns>
        public bool IsDeclared(NodeId dataType)
        {
            if (dataType.IsNull)
            {
                return false;
            }
            string key = Key(dataType);
            return _structures.ContainsKey(key)
                || _enums.ContainsKey(key)
                || _simpleTypes.ContainsKey(key);
        }

        private static string Key(NodeId nodeId)
        {
            return nodeId.ToString() ?? string.Empty;
        }
    }
}
