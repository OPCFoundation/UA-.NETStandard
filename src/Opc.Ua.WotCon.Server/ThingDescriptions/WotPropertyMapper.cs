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
                    dataType = DataTypeIds.BaseDataType;
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
                    dataType = DataTypeIds.Boolean;
                    return true;
                case "number":
                    dataType = DataTypeIds.Double;
                    return true;
                case "integer":
                    dataType = DataTypeIds.Int64;
                    return true;
                case "string":
                    dataType = DataTypeIds.String;
                    return true;
                case null:
                case "":
                case "object":
                case "null":
                    dataType = NodeId.Null;
                    return false;
                default:
                    dataType = DataTypeIds.BaseDataType;
                    return true;
            }
        }
    }
}
