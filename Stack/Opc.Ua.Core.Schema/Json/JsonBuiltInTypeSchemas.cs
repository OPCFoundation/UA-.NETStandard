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

using System.Text.Json.Nodes;

namespace Opc.Ua.Schema.Json
{
    /// <summary>
    /// Maps OPC UA built-in types to JSON Schema fragments according to the
    /// OPC UA Part 6 JSON encoding. Integer-keyed primitives are inlined; the
    /// complex standard types (NodeId, Variant, ...) are emitted once into the
    /// document <c>$defs</c> section and referenced.
    /// </summary>
    internal static class JsonBuiltInTypeSchemas
    {
        /// <summary>
        /// Creates a JSON Schema fragment for the supplied scalar built-in type.
        /// </summary>
        /// <param name="type">The built-in type.</param>
        /// <param name="verbose">Whether the verbose flavor is requested.</param>
        /// <param name="defs">The document definitions section to populate with
        /// standard type definitions when referenced.</param>
        /// <returns>The JSON Schema fragment for the type.</returns>
        public static JsonObject Create(BuiltInType type, bool verbose, JsonObject defs)
        {
            switch (type)
            {
                case BuiltInType.Boolean:
                    return new JsonObject { ["type"] = "boolean" };
                case BuiltInType.SByte:
                    return Integer(sbyte.MinValue, sbyte.MaxValue);
                case BuiltInType.Byte:
                    return Integer(byte.MinValue, byte.MaxValue);
                case BuiltInType.Int16:
                    return Integer(short.MinValue, short.MaxValue);
                case BuiltInType.UInt16:
                    return Integer(ushort.MinValue, ushort.MaxValue);
                case BuiltInType.Int32:
                    return Integer(int.MinValue, int.MaxValue);
                case BuiltInType.UInt32:
                    return Integer(uint.MinValue, uint.MaxValue);
                case BuiltInType.Int64:
                    // Int64 is encoded as a JSON string to avoid precision loss.
                    return IntegerString(signed: true);
                case BuiltInType.UInt64:
                    return IntegerString(signed: false);
                case BuiltInType.Float:
                case BuiltInType.Double:
                case BuiltInType.Number:
                    // Special values (NaN, Infinity) are encoded as JSON strings.
                    return new JsonObject { ["type"] = new JsonArray("number", "string") };
                case BuiltInType.Integer:
                case BuiltInType.UInteger:
                    return new JsonObject { ["type"] = new JsonArray("integer", "string") };
                case BuiltInType.String:
                    return new JsonObject { ["type"] = "string" };
                case BuiltInType.DateTime:
                    return new JsonObject { ["type"] = "string", ["format"] = "date-time" };
                case BuiltInType.Guid:
                    return new JsonObject { ["type"] = "string", ["format"] = "uuid" };
                case BuiltInType.ByteString:
                    return new JsonObject { ["type"] = "string", ["contentEncoding"] = "base64" };
                case BuiltInType.XmlElement:
                    return new JsonObject { ["type"] = "string" };
                case BuiltInType.Enumeration:
                    return new JsonObject { ["type"] = "integer" };
                case BuiltInType.StatusCode:
                    return verbose
                        ? StandardRef(BuiltInType.StatusCode, defs)
                        : Integer(uint.MinValue, uint.MaxValue);
                case BuiltInType.LocalizedText:
                    return verbose
                        ? new JsonObject { ["type"] = "string" }
                        : StandardRef(BuiltInType.LocalizedText, defs);
                case BuiltInType.NodeId:
                case BuiltInType.ExpandedNodeId:
                case BuiltInType.QualifiedName:
                case BuiltInType.Variant:
                case BuiltInType.ExtensionObject:
                case BuiltInType.DataValue:
                case BuiltInType.DiagnosticInfo:
                    return StandardRef(type, defs);
                default:
                    // Unknown or abstract: allow any value.
                    return [];
            }
        }

        private static JsonObject Integer(long minimum, long maximum)
        {
            return new JsonObject
            {
                ["type"] = "integer",
                ["minimum"] = minimum,
                ["maximum"] = maximum
            };
        }

        private static JsonObject IntegerString(bool signed)
        {
            return new JsonObject
            {
                ["type"] = "string",
                ["pattern"] = signed ? "^-?\\d+$" : "^\\d+$"
            };
        }

        private static JsonObject StandardRef(BuiltInType type, JsonObject defs)
        {
            string key = JsonSchemaConstants.StandardDefPrefix + type;
            if (!defs.ContainsKey(key))
            {
                defs[key] = StandardJsonDefinitions.Create(type);
            }
            return JsonSchemaConstants.Ref(key);
        }
    }
}
