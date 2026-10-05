/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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

namespace Opc.Ua.Schema.OpenApi
{
    /// <summary>
    /// The OpenAPI 3.0 schemas of the OPC UA built-in types as the REST
    /// binding writes them (OPC 10000-6, 5.4 and G.3, Compact encoding):
    /// scalar types inline, the structured built-in types as component
    /// schemas. The <c>format</c> values <c>UaNodeId</c>,
    /// <c>UaExpandedNodeId</c> and <c>UaQualifiedName</c> are the ones the
    /// OPC Foundation publication uses for the string encodings.
    /// </summary>
    internal static class OpenApiBuiltInSchemas
    {
        public const string StatusCode = "StatusCode";
        public const string LocalizedText = "LocalizedText";
        public const string Variant = "Variant";
        public const string DataValue = "DataValue";
        public const string DiagnosticInfo = "DiagnosticInfo";
        public const string ExtensionObject = "ExtensionObject";

        /// <summary>
        /// Returns the component schema name of a structured built-in type,
        /// or <c>null</c> when <paramref name="type"/> is written as a
        /// scalar.
        /// </summary>
        /// <param name="type">The built-in type.</param>
        /// <returns>The component schema name or <c>null</c>.</returns>
        public static string? GetComponentName(BuiltInType type)
        {
            return type switch
            {
                BuiltInType.StatusCode => StatusCode,
                BuiltInType.LocalizedText => LocalizedText,
                BuiltInType.ExtensionObject => ExtensionObject,
                BuiltInType.DataValue => DataValue,
                BuiltInType.DiagnosticInfo => DiagnosticInfo,
                BuiltInType.Variant => Variant,
                _ => null
            };
        }

        /// <summary>
        /// Returns whether the encoder writes the JSON literal <c>null</c>
        /// for a null value of <paramref name="type"/> where it cannot omit
        /// the value, which is an element of an array (OPC 10000-6, 5.4.5).
        /// </summary>
        /// <param name="type">The built-in type.</param>
        /// <returns>
        /// <c>true</c> for the types that have a null value: strings, Guid,
        /// ByteString, NodeId, ExpandedNodeId, QualifiedName, XmlElement,
        /// LocalizedText, ExtensionObject, DataValue, DiagnosticInfo and
        /// Variant. The numbers, Boolean, DateTime, StatusCode and the
        /// enumerations are always written.
        /// </returns>
        public static bool CanBeNull(BuiltInType type)
        {
            return type is
                BuiltInType.String or
                BuiltInType.XmlElement or
                BuiltInType.Guid or
                BuiltInType.ByteString or
                BuiltInType.NodeId or
                BuiltInType.ExpandedNodeId or
                BuiltInType.QualifiedName or
                BuiltInType.LocalizedText or
                BuiltInType.ExtensionObject or
                BuiltInType.DataValue or
                BuiltInType.DiagnosticInfo or
                BuiltInType.Variant;
        }

        /// <summary>
        /// Makes <paramref name="schema"/> accept <c>null</c> in the form of
        /// OpenAPI 3.0: <c>nullable</c> next to the schema, and next to an
        /// <c>allOf</c> that holds the schema when it is a reference, which
        /// cannot carry keywords of its own.
        /// </summary>
        /// <param name="schema">
        /// The schema of a value; it is changed unless it is a reference.
        /// </param>
        /// <returns>The schema that also accepts <c>null</c>.</returns>
        public static JsonObject MakeNullable(JsonObject schema)
        {
            if (schema.ContainsKey("$ref"))
            {
                return new JsonObject
                {
                    ["nullable"] = true,
                    ["allOf"] = new JsonArray(schema)
                };
            }
            schema["nullable"] = true;
            return schema;
        }

        /// <summary>
        /// Creates the schema of a one dimensional array
        /// (OPC 10000-6, 5.4.5).
        /// </summary>
        /// <param name="items">The schema of an element.</param>
        /// <returns>The array schema.</returns>
        public static JsonObject CreateArray(JsonObject items)
        {
            return new JsonObject { ["type"] = "array", ["items"] = items };
        }

        /// <summary>
        /// Creates the schema of a multi dimensional array that is the
        /// value of a structure field: an object with the elements in
        /// <c>Array</c>, flattened, and the length of each dimension in
        /// <c>Dimensions</c> (OPC 10000-6, 5.4.5), the way the encoder
        /// writes a field of a fixed value rank of two or more.
        /// </summary>
        /// <param name="items">The schema of an element.</param>
        /// <returns>The matrix schema.</returns>
        public static JsonObject CreateMatrix(JsonObject items)
        {
            return new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["Array"] = CreateArray(items),
                    ["Dimensions"] = CreateDimensions()
                }
            };
        }

        /// <summary>
        /// Creates a reference to a component schema.
        /// </summary>
        /// <param name="componentName">The component schema name.</param>
        /// <returns>The <c>$ref</c> schema.</returns>
        public static JsonObject Reference(string componentName)
        {
            return new JsonObject { ["$ref"] = "#/components/schemas/" + componentName };
        }

        /// <summary>
        /// Creates the inline schema of a scalar built-in type.
        /// </summary>
        /// <param name="type">
        /// The built-in type; one for which <see cref="GetComponentName"/>
        /// returns <c>null</c>.
        /// </param>
        /// <returns>The schema of a scalar value of the type.</returns>
        public static JsonObject CreateScalar(BuiltInType type)
        {
            switch (type)
            {
                case BuiltInType.Boolean:
                    return new JsonObject { ["type"] = "boolean" };
                case BuiltInType.SByte:
                    return Integer("int32", sbyte.MinValue, sbyte.MaxValue);
                case BuiltInType.Byte:
                    return Integer("int32", byte.MinValue, byte.MaxValue);
                case BuiltInType.Int16:
                    return Integer("int32", short.MinValue, short.MaxValue);
                case BuiltInType.UInt16:
                    return Integer("int32", ushort.MinValue, ushort.MaxValue);
                case BuiltInType.Int32:
                case BuiltInType.Enumeration:
                    return new JsonObject { ["type"] = "integer", ["format"] = "int32" };
                case BuiltInType.UInt32:
                    return UInt32();
                case BuiltInType.Int64:
                    // Part 6, 5.4.2.4: Int64 and UInt64 are written as JSON strings.
                    return new JsonObject { ["type"] = "string", ["format"] = "int64" };
                case BuiltInType.UInt64:
                    return new JsonObject { ["type"] = "string", ["format"] = "uint64" };
                case BuiltInType.Float:
                    return new JsonObject { ["type"] = "number", ["format"] = "float" };
                case BuiltInType.Double:
                    return new JsonObject { ["type"] = "number", ["format"] = "double" };
                case BuiltInType.String:
                case BuiltInType.XmlElement:
                    return new JsonObject { ["type"] = "string" };
                case BuiltInType.DateTime:
                    return new JsonObject { ["type"] = "string", ["format"] = "date-time" };
                case BuiltInType.Guid:
                    return new JsonObject { ["type"] = "string", ["format"] = "uuid" };
                case BuiltInType.ByteString:
                    return new JsonObject { ["type"] = "string", ["format"] = "byte" };
                case BuiltInType.NodeId:
                    return new JsonObject { ["type"] = "string", ["format"] = "UaNodeId" };
                case BuiltInType.ExpandedNodeId:
                    return new JsonObject { ["type"] = "string", ["format"] = "UaExpandedNodeId" };
                case BuiltInType.QualifiedName:
                    return new JsonObject { ["type"] = "string", ["format"] = "UaQualifiedName" };
                default:
                    return [];
            }
        }

        /// <summary>
        /// Creates the component schema of a structured built-in type.
        /// </summary>
        /// <param name="name">
        /// One of the component schema names of this class.
        /// </param>
        /// <returns>The component schema.</returns>
        public static JsonObject CreateComponent(string name)
        {
            return name switch
            {
                StatusCode => CreateStatusCode(),
                LocalizedText => CreateLocalizedText(),
                Variant => CreateVariant(),
                DataValue => CreateDataValue(),
                DiagnosticInfo => CreateDiagnosticInfo(),
                _ => CreateExtensionObject()
            };
        }

        private static JsonObject CreateStatusCode()
        {
            // Part 6, 5.4.2.16: Code, and Symbol in the Verbose encoding.
            return Object(new JsonObject
            {
                ["Code"] = UInt32(),
                ["Symbol"] = new JsonObject { ["type"] = "string" }
            });
        }

        private static JsonObject CreateLocalizedText()
        {
            // Part 6, 5.4.2.15
            return Object(new JsonObject
            {
                ["Locale"] = new JsonObject { ["type"] = "string", ["format"] = "rfc3066" },
                ["Text"] = new JsonObject { ["type"] = "string" }
            });
        }

        private static JsonObject CreateVariant()
        {
            // Part 6, 5.4.2.17
            return Object(VariantProperties());
        }

        private static JsonObject CreateDataValue()
        {
            // Part 6, 5.4.2.18: the fields of a Variant and the DataValue fields.
            // Table 42 names the status field "Status".
            JsonObject properties = VariantProperties();
            properties["Status"] = Reference(StatusCode);
            properties["SourceTimestamp"] = new JsonObject { ["type"] = "string", ["format"] = "date-time" };
            properties["SourcePicoseconds"] = Integer("int32", 0, ushort.MaxValue);
            properties["ServerTimestamp"] = new JsonObject { ["type"] = "string", ["format"] = "date-time" };
            properties["ServerPicoseconds"] = Integer("int32", 0, ushort.MaxValue);
            return Object(properties);
        }

        private static JsonObject CreateDiagnosticInfo()
        {
            // Part 6, 5.4.2.19
            return Object(new JsonObject
            {
                ["SymbolicId"] = new JsonObject { ["type"] = "integer", ["format"] = "int32" },
                ["NamespaceUri"] = new JsonObject { ["type"] = "integer", ["format"] = "int32" },
                ["Locale"] = new JsonObject { ["type"] = "integer", ["format"] = "int32" },
                ["LocalizedText"] = new JsonObject { ["type"] = "integer", ["format"] = "int32" },
                ["AdditionalInfo"] = new JsonObject { ["type"] = "string" },
                ["InnerStatusCode"] = Reference(StatusCode),
                ["InnerDiagnosticInfo"] = Reference(DiagnosticInfo)
            });
        }

        private static JsonObject CreateExtensionObject()
        {
            // Part 6, 5.4.2.16: a body that is JSON encoded is written inline,
            // next to UaTypeId, so more properties than the three below can
            // appear: additionalProperties stays at its default of true.
            return new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["UaTypeId"] = new JsonObject { ["type"] = "string", ["format"] = "UaNodeId" },
                    ["UaEncoding"] = Integer("int32", 0, byte.MaxValue),
                    ["UaBody"] = new JsonObject { ["type"] = "string", ["format"] = "byte" }
                }
            };
        }

        private static JsonObject VariantProperties()
        {
            return new JsonObject
            {
                ["UaType"] = Integer("int32", 0, byte.MaxValue),
                ["Value"] = new JsonObject(),
                ["Dimensions"] = CreateDimensions()
            };
        }

        private static JsonObject CreateDimensions()
        {
            return CreateArray(new JsonObject
            {
                ["type"] = "integer",
                ["format"] = "int32",
                ["minimum"] = 0
            });
        }

        private static JsonObject UInt32()
        {
            return Integer("int64", uint.MinValue, uint.MaxValue);
        }

        private static JsonObject Integer(string format, long minimum, long maximum)
        {
            return new JsonObject
            {
                ["type"] = "integer",
                ["format"] = format,
                ["minimum"] = minimum,
                ["maximum"] = maximum
            };
        }

        private static JsonObject Object(JsonObject properties)
        {
            return new JsonObject
            {
                ["type"] = "object",
                ["properties"] = properties,
                ["additionalProperties"] = false
            };
        }
    }
}
