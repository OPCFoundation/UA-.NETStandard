// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;

namespace Opc.Ua.WotCon.Server.ThingDescriptions
{
    /// <summary>
    /// Maps WoT JSON-schema primitive types to OPC UA built-in data types
    /// per OPC 10100-1 §6.3.8 Table 14.
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    ///   <item><c>boolean</c> → <c>Boolean</c></item>
    ///   <item><c>number</c>  → <c>Double</c></item>
    ///   <item><c>integer</c> → <c>Int64</c></item>
    ///   <item><c>string</c>  → <c>String</c></item>
    ///   <item><c>object</c>, <c>null</c> → no mapping (returns <c>false</c>)</item>
    ///   <item><c>array</c> → <see cref="ValueRanks.OneDimension"/> of the element type</item>
    /// </list>
    /// </remarks>
    public static class WotPropertyMapper
    {
        /// <summary>
        /// Resolves the OPC UA <c>DataType</c> and <c>ValueRank</c> for a
        /// WoT property descriptor.
        /// </summary>
        /// <returns>
        /// <c>true</c> when a mapping exists. <c>false</c> for unmappable
        /// shapes (caller should publish <see cref="StatusCodes.BadConfigurationError"/>
        /// on read per OPC 10100-1 §6.3.8 last paragraph).
        /// </returns>
        /// <exception cref="ArgumentNullException"><paramref name="property"/> is null.</exception>
        public static bool TryMap(
            WotProperty property,
            out NodeId dataType,
            out int valueRank)
        {
            if (property is null)
            {
                throw new ArgumentNullException(nameof(property));
            }
            if (string.Equals(property.Type, "array", StringComparison.OrdinalIgnoreCase))
            {
                valueRank = ValueRanks.OneDimension;
                if (property.Items?.Type == null)
                {
                    dataType = Ua.DataTypeIds.BaseDataType;
                    return true;
                }
                return TryMapPrimitive(property.Items.Type, out dataType);
            }

            valueRank = ValueRanks.Scalar;
            return TryMapPrimitive(property.Type, out dataType);
        }

        /// <summary>
        /// Resolves the OPC UA <c>DataType</c> for a JSON-schema primitive
        /// type string.
        /// </summary>
        public static bool TryMapPrimitive(string? jsonType, out NodeId dataType)
        {
            switch (jsonType?.ToLowerInvariant())
            {
                case "boolean":
                    dataType = Ua.DataTypeIds.Boolean;
                    return true;
                case "number":
                    dataType = Ua.DataTypeIds.Double;
                    return true;
                case "integer":
                    dataType = Ua.DataTypeIds.Int64;
                    return true;
                case "string":
                    dataType = Ua.DataTypeIds.String;
                    return true;
                case null:
                case "":
                case "object":
                case "null":
                    dataType = NodeId.Null;
                    return false;
                default:
                    dataType = Ua.DataTypeIds.BaseDataType;
                    return true;
            }
        }
    }
}
