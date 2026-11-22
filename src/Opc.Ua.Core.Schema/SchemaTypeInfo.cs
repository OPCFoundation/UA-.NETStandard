// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

namespace Opc.Ua.Schema
{
    /// <summary>
    /// Identifies standard wire encodings used by structure-field schemas.
    /// </summary>
    public static class SchemaTypeInfo
    {
        /// <summary>
        /// Returns the built-in wire encoding of a standard structure field.
        /// Number, Integer and UInteger fields use Variant encoding as specified
        /// by OPC 10000-6, 5.1.6.
        /// </summary>
        /// <param name="dataType">The field's declared data type.</param>
        /// <returns>
        /// The built-in encoding, or <see cref="BuiltInType.Null"/> when the
        /// data type requires definition resolution.
        /// </returns>
        public static BuiltInType GetFieldEncodingType(NodeId dataType)
        {
            if (dataType == DataTypeIds.Number ||
                dataType == DataTypeIds.Integer ||
                dataType == DataTypeIds.UInteger)
            {
                return BuiltInType.Variant;
            }
            return TypeInfo.GetBuiltInType(dataType);
        }
    }
}
