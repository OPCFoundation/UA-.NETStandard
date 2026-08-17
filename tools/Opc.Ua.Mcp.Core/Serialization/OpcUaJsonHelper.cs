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

using System;
using System.Buffers;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Opc.Ua.Mcp.Serialization
{
    /// <summary>
    /// Provides helper methods for converting OPC UA types to/from JSON-friendly representations.
    /// </summary>
    public static class OpcUaJsonHelper
    {
        /// <summary>
        /// Shared JSON serializer options for OPC UA MCP tool results.
        /// </summary>
        public static JsonSerializerOptions JsonOptions { get; } = CreateJsonOptions();

        /// <summary>
        /// Serializes an object to a JSON string using the OPC UA JSON options.
        /// </summary>
        /// <remarks>
        /// The tool helpers in this assembly reduce every OPC UA value to a closed set of
        /// JSON-friendly shapes before serializing it (see <see cref="ElementToObject"/>):
        /// <c>null</c>, the primitive scalars, <see cref="string"/>,
        /// <see cref="Dictionary{TKey, TValue}"/> of <see cref="string"/> to <see cref="object"/>
        /// and lists thereof. Because that set is closed, the JSON is written directly with a
        /// <see cref="Utf8JsonWriter"/> rather than through the reflection-based
        /// <see cref="JsonSerializer"/>, which keeps the assembly trim- and Native-AOT-safe.
        /// The output is byte-for-byte identical to the reflection-based serializer configured
        /// with <see cref="JsonOptions"/>.
        /// </remarks>
        /// <typeparam name="T"></typeparam>
        public static string Serialize<T>(T value)
        {
            var buffer = new ArrayBufferWriter<byte>();
            using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions
            {
                Indented = JsonOptions.WriteIndented,
                Encoder = JsonOptions.Encoder
            }))
            {
                WriteValue(writer, value);
            }

            return Encoding.UTF8.GetString(buffer.WrittenSpan);
        }

        /// <summary>
        /// Writes a JSON-friendly value produced by the conversion helpers in this class.
        /// </summary>
        private static void WriteValue(Utf8JsonWriter writer, object? value)
        {
            switch (value)
            {
                case null:
                    writer.WriteNullValue();
                    break;
                case string text:
                    writer.WriteStringValue(text);
                    break;
                case bool flag:
                    writer.WriteBooleanValue(flag);
                    break;
                // Utf8JsonWriter has no overloads for the narrow integer types, so widen
                // them; the rendered digits are the same either way.
                case sbyte number:
                    writer.WriteNumberValue(number);
                    break;
                case byte number:
                    writer.WriteNumberValue(number);
                    break;
                case short number:
                    writer.WriteNumberValue(number);
                    break;
                case ushort number:
                    writer.WriteNumberValue(number);
                    break;
                case int number:
                    writer.WriteNumberValue(number);
                    break;
                case uint number:
                    writer.WriteNumberValue(number);
                    break;
                case long number:
                    writer.WriteNumberValue(number);
                    break;
                case ulong number:
                    writer.WriteNumberValue(number);
                    break;
                case float number:
                    writer.WriteNumberValue(number);
                    break;
                case double number:
                    writer.WriteNumberValue(number);
                    break;
                case decimal number:
                    writer.WriteNumberValue(number);
                    break;
                case DateTime timestamp:
                    writer.WriteStringValue(timestamp);
                    break;
                case DateTimeOffset timestamp:
                    writer.WriteStringValue(timestamp);
                    break;
                case Guid guid:
                    writer.WriteStringValue(guid);
                    break;
                case byte[] bytes:
                    writer.WriteBase64StringValue(bytes);
                    break;
                case JsonElement element:
                    element.WriteTo(writer);
                    break;
                case JsonNode node:
                    node.WriteTo(writer);
                    break;
                // Must precede IEnumerable: a dictionary is also a sequence of pairs.
                case IDictionary<string, object?> map:
                    writer.WriteStartObject();
                    foreach (KeyValuePair<string, object?> entry in map)
                    {
                        writer.WritePropertyName(entry.Key);
                        WriteValue(writer, entry.Value);
                    }
                    writer.WriteEndObject();
                    break;
                case IEnumerable sequence:
                    writer.WriteStartArray();
                    foreach (object? item in sequence)
                    {
                        WriteValue(writer, item);
                    }
                    writer.WriteEndArray();
                    break;
                case char character:
                    writer.WriteStringValue(character.ToString());
                    break;
                default:
                    // The conversion helpers in this class reduce every OPC UA value to the
                    // shapes handled above, so reaching this point means a caller passed a
                    // type this trim-safe writer cannot model. Fail loudly rather than
                    // silently emitting a stringified object.
                    throw new NotSupportedException(
                        $"'{value.GetType()}' is not a JSON-friendly value. Convert it with the " +
                        $"{nameof(OpcUaJsonHelper)} helpers before serializing; arbitrary object " +
                        "graphs are not supported because that would require reflection, which is " +
                        "unavailable when trimming or publishing ahead of time.");
            }
        }

        /// <summary>
        /// Parses a NodeId from its string representation.
        /// </summary>
        /// <param name="nodeIdString">The NodeId string, e.g. "ns=2;s=MyVariable" or "i=85".</param>
        /// <exception cref="ArgumentException"></exception>
        public static NodeId ParseNodeId(string nodeIdString)
        {
            if (string.IsNullOrWhiteSpace(nodeIdString))
            {
                throw new ArgumentException("NodeId string cannot be null or empty.", nameof(nodeIdString));
            }

            return NodeId.Parse(nodeIdString);
        }

        /// <summary>
        /// Parses an ExpandedNodeId from its string representation.
        /// </summary>
        /// <exception cref="ArgumentException"></exception>
        public static ExpandedNodeId ParseExpandedNodeId(string nodeIdString)
        {
            if (string.IsNullOrWhiteSpace(nodeIdString))
            {
                throw new ArgumentException("ExpandedNodeId string cannot be null or empty.", nameof(nodeIdString));
            }

            return ExpandedNodeId.Parse(nodeIdString);
        }

        /// <summary>
        /// Parses a QualifiedName from a string like "2:MyName" or just "MyName".
        /// </summary>
        /// <exception cref="ArgumentException"></exception>
        public static QualifiedName ParseQualifiedName(string qualifiedNameString)
        {
            if (string.IsNullOrWhiteSpace(qualifiedNameString))
            {
                throw new ArgumentException(
                    "QualifiedName string cannot be null or empty.",
                    nameof(qualifiedNameString));
            }

            return QualifiedName.Parse(qualifiedNameString);
        }

        /// <summary>
        /// Converts a DataValue to a JSON-friendly dictionary.
        /// </summary>
        public static Dictionary<string, object?> DataValueToDict(DataValue dataValue)
        {
            return new Dictionary<string, object?>
            {
                ["value"] = VariantToObject(dataValue.WrappedValue),
                ["statusCode"] = StatusCodeToString(dataValue.StatusCode),
                ["sourceTimestamp"] = dataValue.SourceTimestamp != DateTime.MinValue
                    ? dataValue.SourceTimestamp.ToString("o", CultureInfo.InvariantCulture) : null,
                ["serverTimestamp"] = dataValue.ServerTimestamp != DateTime.MinValue
                    ? dataValue.ServerTimestamp.ToString("o", CultureInfo.InvariantCulture) : null
            };
        }

        /// <summary>
        /// Converts a Variant value to a JSON-friendly object.
        /// </summary>
        public static object? VariantToObject(Variant variant)
        {
            if (variant.TypeInfo.BuiltInType == BuiltInType.Null)
            {
                return null;
            }

            if (variant.TypeInfo.IsArray)
            {
                return VariantArrayToObject(variant);
            }

            if (variant.TypeInfo.IsMatrix)
            {
                return VariantMatrixToObject(variant);
            }

            switch (variant.TypeInfo.BuiltInType)
            {
                case BuiltInType.Boolean when variant.TryGetValue(out bool value):
                    return value;
                case BuiltInType.SByte when variant.TryGetValue(out sbyte value):
                    return value;
                case BuiltInType.Byte when variant.TryGetValue(out byte value):
                    return value;
                case BuiltInType.Int16 when variant.TryGetValue(out short value):
                    return value;
                case BuiltInType.UInt16 when variant.TryGetValue(out ushort value):
                    return value;
                case BuiltInType.Int32 when variant.TryGetValue(out int value):
                    return value;
                case BuiltInType.UInt32 when variant.TryGetValue(out uint value):
                    return value;
                case BuiltInType.Int64 when variant.TryGetValue(out long value):
                    return value;
                case BuiltInType.UInt64 when variant.TryGetValue(out ulong value):
                    return value;
                case BuiltInType.Float when variant.TryGetValue(out float value):
                    return value;
                case BuiltInType.Double when variant.TryGetValue(out double value):
                    return value;
                case BuiltInType.String when variant.TryGetValue(out string value):
                    return value;
                case BuiltInType.DateTime when variant.TryGetValue(out DateTimeUtc value):
                    return value.ToDateTime().ToString("o", CultureInfo.InvariantCulture);
                case BuiltInType.Guid when variant.TryGetValue(out Uuid value):
                    return value.ToString();
                case BuiltInType.ByteString when variant.TryGetValue(out ByteString value):
                    return value.IsNull ? null : Convert.ToBase64String(value.ToArray());
                case BuiltInType.NodeId when variant.TryGetValue(out NodeId value):
                    return value.ToString();
                case BuiltInType.ExpandedNodeId when variant.TryGetValue(out ExpandedNodeId value):
                    return value.ToString();
                case BuiltInType.QualifiedName when variant.TryGetValue(out QualifiedName value):
                    return value.ToString();
                case BuiltInType.LocalizedText when variant.TryGetValue(out LocalizedText value):
                    return value.Text;
                case BuiltInType.StatusCode when variant.TryGetValue(out StatusCode value):
                    return StatusCodeToString(value);
                case BuiltInType.ExtensionObject when variant.TryGetValue(out ExtensionObject value):
                    return ExtensionObjectToDict(value);
                case BuiltInType.XmlElement when variant.TryGetValue(out XmlElement value):
                    return value.ToString();
                case BuiltInType.DataValue when variant.TryGetValue(out DataValue value):
                    return value.ToString();
                default:
                    return variant.ToString();
            }
        }

        private static object? VariantArrayToObject(Variant variant)
        {
            return variant.TypeInfo.BuiltInType switch
            {
                BuiltInType.Boolean when variant.TryGetValue(out ArrayOf<bool> value) => ArrayToList(value),
                BuiltInType.SByte when variant.TryGetValue(out ArrayOf<sbyte> value) => ArrayToList(value),
                BuiltInType.Byte when variant.TryGetValue(out ArrayOf<byte> value) => ArrayToList(value),
                BuiltInType.Int16 when variant.TryGetValue(out ArrayOf<short> value) => ArrayToList(value),
                BuiltInType.UInt16 when variant.TryGetValue(out ArrayOf<ushort> value) => ArrayToList(value),
                BuiltInType.Int32 when variant.TryGetValue(out ArrayOf<int> value) => ArrayToList(value),
                BuiltInType.UInt32 when variant.TryGetValue(out ArrayOf<uint> value) => ArrayToList(value),
                BuiltInType.Int64 when variant.TryGetValue(out ArrayOf<long> value) => ArrayToList(value),
                BuiltInType.UInt64 when variant.TryGetValue(out ArrayOf<ulong> value) => ArrayToList(value),
                BuiltInType.Float when variant.TryGetValue(out ArrayOf<float> value) => ArrayToList(value),
                BuiltInType.Double when variant.TryGetValue(out ArrayOf<double> value) => ArrayToList(value),
                BuiltInType.String when variant.TryGetValue(out ArrayOf<string> value) => ArrayToList(value),
                BuiltInType.DateTime when variant.TryGetValue(out ArrayOf<DateTimeUtc> value) => ArrayToList(value),
                BuiltInType.Guid when variant.TryGetValue(out ArrayOf<Uuid> value) => ArrayToList(value),
                BuiltInType.ByteString when variant.TryGetValue(out ArrayOf<ByteString> value) => ArrayToList(value),
                BuiltInType.XmlElement when variant.TryGetValue(out ArrayOf<XmlElement> value) => ArrayToList(value),
                BuiltInType.NodeId when variant.TryGetValue(out ArrayOf<NodeId> value) => ArrayToList(value),
                BuiltInType.ExpandedNodeId when variant.TryGetValue(out ArrayOf<ExpandedNodeId> value) =>
                    ArrayToList(value),
                BuiltInType.StatusCode when variant.TryGetValue(out ArrayOf<StatusCode> value) => ArrayToList(value),
                BuiltInType.QualifiedName when variant.TryGetValue(out ArrayOf<QualifiedName> value) =>
                    ArrayToList(value),
                BuiltInType.LocalizedText when variant.TryGetValue(out ArrayOf<LocalizedText> value) =>
                    ArrayToList(value),
                BuiltInType.ExtensionObject when variant.TryGetValue(out ArrayOf<ExtensionObject> value) =>
                    ArrayToList(value),
                BuiltInType.DataValue when variant.TryGetValue(out ArrayOf<DataValue> value) => ArrayToList(value),
                BuiltInType.Variant when variant.TryGetValue(out ArrayOf<Variant> value) => ArrayToList(value),
                _ => variant.ToString()
            };
        }

        private static object? VariantMatrixToObject(Variant variant)
        {
            return variant.TypeInfo.BuiltInType switch
            {
                BuiltInType.Boolean when variant.TryGetValue(out MatrixOf<bool> value) => MatrixToList(value),
                BuiltInType.SByte when variant.TryGetValue(out MatrixOf<sbyte> value) => MatrixToList(value),
                BuiltInType.Byte when variant.TryGetValue(out MatrixOf<byte> value) => MatrixToList(value),
                BuiltInType.Int16 when variant.TryGetValue(out MatrixOf<short> value) => MatrixToList(value),
                BuiltInType.UInt16 when variant.TryGetValue(out MatrixOf<ushort> value) => MatrixToList(value),
                BuiltInType.Int32 when variant.TryGetValue(out MatrixOf<int> value) => MatrixToList(value),
                BuiltInType.UInt32 when variant.TryGetValue(out MatrixOf<uint> value) => MatrixToList(value),
                BuiltInType.Int64 when variant.TryGetValue(out MatrixOf<long> value) => MatrixToList(value),
                BuiltInType.UInt64 when variant.TryGetValue(out MatrixOf<ulong> value) => MatrixToList(value),
                BuiltInType.Float when variant.TryGetValue(out MatrixOf<float> value) => MatrixToList(value),
                BuiltInType.Double when variant.TryGetValue(out MatrixOf<double> value) => MatrixToList(value),
                BuiltInType.String when variant.TryGetValue(out MatrixOf<string> value) => MatrixToList(value),
                BuiltInType.DateTime when variant.TryGetValue(out MatrixOf<DateTimeUtc> value) => MatrixToList(value),
                BuiltInType.Guid when variant.TryGetValue(out MatrixOf<Uuid> value) => MatrixToList(value),
                BuiltInType.ByteString when variant.TryGetValue(out MatrixOf<ByteString> value) => MatrixToList(value),
                BuiltInType.XmlElement when variant.TryGetValue(out MatrixOf<XmlElement> value) => MatrixToList(value),
                BuiltInType.NodeId when variant.TryGetValue(out MatrixOf<NodeId> value) => MatrixToList(value),
                BuiltInType.ExpandedNodeId when variant.TryGetValue(out MatrixOf<ExpandedNodeId> value) =>
                    MatrixToList(value),
                BuiltInType.StatusCode when variant.TryGetValue(out MatrixOf<StatusCode> value) => MatrixToList(value),
                BuiltInType.QualifiedName when variant.TryGetValue(out MatrixOf<QualifiedName> value) =>
                    MatrixToList(value),
                BuiltInType.LocalizedText when variant.TryGetValue(out MatrixOf<LocalizedText> value) =>
                    MatrixToList(value),
                BuiltInType.ExtensionObject when variant.TryGetValue(out MatrixOf<ExtensionObject> value) =>
                    MatrixToList(value),
                BuiltInType.DataValue when variant.TryGetValue(out MatrixOf<DataValue> value) => MatrixToList(value),
                BuiltInType.Variant when variant.TryGetValue(out MatrixOf<Variant> value) => MatrixToList(value),
                _ => variant.ToString()
            };
        }

        /// <summary>
        /// Converts a StatusCode to a human-readable string.
        /// </summary>
        public static string StatusCodeToString(StatusCode statusCode)
        {
            return statusCode.SymbolicId ?? string.Empty;
        }

        /// <summary>
        /// Converts a <see cref="ReferenceDescription"/> to a JSON-friendly dictionary.
        /// </summary>
        public static Dictionary<string, object?> ReferenceDescriptionToDict(ReferenceDescription reference)
        {
            return new Dictionary<string, object?>
            {
                ["nodeId"] = reference.NodeId.ToString(),
                ["browseName"] = reference.BrowseName.ToString(),
                ["displayName"] = reference.DisplayName.Text,
                ["nodeClass"] = reference.NodeClass.ToString(),
                ["typeDefinition"] = reference.TypeDefinition.IsNull ? null : reference.TypeDefinition.ToString(),
                ["isForward"] = reference.IsForward,
                ["referenceTypeId"] = reference.ReferenceTypeId.ToString()
            };
        }

        /// <summary>
        /// Converts a <see cref="ResponseHeader"/> to a JSON-friendly dictionary.
        /// </summary>
        public static Dictionary<string, object?> ResponseHeaderToDict(ResponseHeader header)
        {
            return new Dictionary<string, object?>
            {
                ["timestamp"] = header.Timestamp.ToString("o", CultureInfo.InvariantCulture),
                ["requestHandle"] = header.RequestHandle,
                ["serviceResult"] = StatusCodeToString(header.ServiceResult)
            };
        }

        /// <summary>
        /// Converts a DiagnosticInfo to a JSON-friendly dictionary.
        /// </summary>
        public static Dictionary<string, object?>? DiagnosticInfoToDict(DiagnosticInfo? diagnosticInfo)
        {
            if (diagnosticInfo == null)
            {
                return null;
            }

            return new Dictionary<string, object?>
            {
                ["symbolicId"] = diagnosticInfo.SymbolicId,
                ["namespaceUri"] = diagnosticInfo.NamespaceUri,
                ["locale"] = diagnosticInfo.Locale,
                ["localizedText"] = diagnosticInfo.LocalizedText,
                ["additionalInfo"] = diagnosticInfo.AdditionalInfo,
                ["innerStatusCode"] = StatusCodeToString(diagnosticInfo.InnerStatusCode)
            };
        }

        /// <summary>
        /// Converts a list of StatusCodes to string representations.
        /// </summary>
        public static List<string> StatusCodesToStrings(ArrayOf<StatusCode> results)
        {
            if (results.IsNull)
            {
                return [];
            }

            return [.. results.ToArray()!.Select(StatusCodeToString)];
        }

        /// <summary>
        /// Converts a Variant value from a JSON element.
        /// </summary>
        public static Variant JsonElementToVariant(JsonElement element, string? dataType = null)
        {
            return element.ValueKind switch
            {
                JsonValueKind.True => new Variant(true),
                JsonValueKind.False => new Variant(false),
                JsonValueKind.Number when dataType?.Equals("Int32", StringComparison.OrdinalIgnoreCase) == true
                    => new Variant(element.GetInt32()),
                JsonValueKind.Number when dataType?.Equals("UInt32", StringComparison.OrdinalIgnoreCase) == true
                    => new Variant(element.GetUInt32()),
                JsonValueKind.Number when dataType?.Equals("Int16", StringComparison.OrdinalIgnoreCase) == true
                    => new Variant(element.GetInt16()),
                JsonValueKind.Number when dataType?.Equals("UInt16", StringComparison.OrdinalIgnoreCase) == true
                    => new Variant(element.GetUInt16()),
                JsonValueKind.Number when dataType?.Equals("Int64", StringComparison.OrdinalIgnoreCase) == true
                    => new Variant(element.GetInt64()),
                JsonValueKind.Number when dataType?.Equals("UInt64", StringComparison.OrdinalIgnoreCase) == true
                    => new Variant(element.GetUInt64()),
                JsonValueKind.Number when dataType?.Equals("Float", StringComparison.OrdinalIgnoreCase) == true
                    => new Variant(element.GetSingle()),
                JsonValueKind.Number when dataType?.Equals("Double", StringComparison.OrdinalIgnoreCase) == true
                    => new Variant(element.GetDouble()),
                JsonValueKind.Number when dataType?.Equals("Byte", StringComparison.OrdinalIgnoreCase) == true
                    => new Variant(element.GetByte()),
                JsonValueKind.Number when dataType?.Equals("SByte", StringComparison.OrdinalIgnoreCase) == true
                    => new Variant(element.GetSByte()),
                JsonValueKind.Number when element.TryGetInt32(out int i) => new Variant(i),
                JsonValueKind.Number when element.TryGetInt64(out long l) => new Variant(l),
                JsonValueKind.Number => new Variant(element.GetDouble()),
                JsonValueKind.String when dataType?.Equals("DateTime", StringComparison.OrdinalIgnoreCase) == true
                    => new Variant(DateTime.Parse(element.GetString()!, CultureInfo.InvariantCulture)),
                JsonValueKind.String => new Variant(element.GetString()!),
                JsonValueKind.Null or JsonValueKind.Undefined => Variant.Null,
                _ => new Variant(element.GetRawText())
            };
        }

        /// <summary>
        /// Parses an attribute ID from a string or integer.
        /// </summary>
        /// <exception cref="ArgumentException"></exception>
        public static uint ParseAttributeId(string? attributeIdStr)
        {
            if (string.IsNullOrWhiteSpace(attributeIdStr))
            {
                return Attributes.Value;
            }

            if (uint.TryParse(attributeIdStr, CultureInfo.InvariantCulture, out uint numericId))
            {
                return numericId;
            }

            return attributeIdStr.ToUpperInvariant() switch
            {
                "NODEID" => Attributes.NodeId,
                "NODECLASS" => Attributes.NodeClass,
                "BROWSENAME" => Attributes.BrowseName,
                "DISPLAYNAME" => Attributes.DisplayName,
                "DESCRIPTION" => Attributes.Description,
                "WRITEMASK" => Attributes.WriteMask,
                "USERWRITEMASK" => Attributes.UserWriteMask,
                "ISABSTRACT" => Attributes.IsAbstract,
                "SYMMETRIC" => Attributes.Symmetric,
                "INVERSENAME" => Attributes.InverseName,
                "CONTAINSNOLOOPS" => Attributes.ContainsNoLoops,
                "EVENTNOTIFIER" => Attributes.EventNotifier,
                "VALUE" => Attributes.Value,
                "DATATYPE" => Attributes.DataType,
                "VALUERANK" => Attributes.ValueRank,
                "ARRAYDIMENSIONS" => Attributes.ArrayDimensions,
                "ACCESSLEVEL" => Attributes.AccessLevel,
                "USERACCESSLEVEL" => Attributes.UserAccessLevel,
                "MINIMUMSAMPLINGINTERVAL" => Attributes.MinimumSamplingInterval,
                "HISTORIZING" => Attributes.Historizing,
                "EXECUTABLE" => Attributes.Executable,
                "USEREXECUTABLE" => Attributes.UserExecutable,
                "DATATYPEDEFINITION" => Attributes.DataTypeDefinition,
                "ROLEPERMISSIONS" => Attributes.RolePermissions,
                "USERROLEPERMISSIONS" => Attributes.UserRolePermissions,
                "ACCESSRESTRICTIONS" => Attributes.AccessRestrictions,
                "ACCESSLEVELEX" => Attributes.AccessLevelEx,
                _ => throw new ArgumentException($"Unknown attribute: {attributeIdStr}", nameof(attributeIdStr))
            };
        }

        private static Dictionary<string, object?> ExtensionObjectToDict(ExtensionObject ext)
        {
            var result = new Dictionary<string, object?>
            {
                ["typeId"] = ext.TypeId.ToString()
            };

            if (ext.TryGetValue(out IEncodeable? encodeable))
            {
                result["body"] = encodeable.ToString();
            }

            return result;
        }

        private static List<object?> ArrayToList(Array array)
        {
            var list = new List<object?>(array.Length);
            foreach (object? item in array)
            {
                list.Add(ElementToObject(item));
            }
            return list;
        }

        /// <summary>
        /// Converts a single array element to a JSON-friendly value using the
        /// same conventions as the scalar conversion, so that an array of
        /// numbers or booleans keeps its JSON type instead of being
        /// stringified.
        /// </summary>
        private static object? ElementToObject(object? item)
        {
            return item switch
            {
                null => null,
                bool value => value,
                sbyte value => value,
                byte value => value,
                short value => value,
                ushort value => value,
                int value => value,
                uint value => value,
                long value => value,
                ulong value => value,
                float value => value,
                double value => value,
                string value => value,
                DateTimeUtc value => value.ToDateTime().ToString("o", CultureInfo.InvariantCulture),
                DateTime value => value.ToString("o", CultureInfo.InvariantCulture),
                Uuid value => value.ToString(),
                Guid value => value.ToString(),
                ByteString value => value.IsNull ? null : Convert.ToBase64String(value.ToArray()),
                byte[] value => Convert.ToBase64String(value),
                NodeId value => value.ToString(),
                ExpandedNodeId value => value.ToString(),
                QualifiedName value => value.ToString(),
                LocalizedText value => value.Text,
                StatusCode value => StatusCodeToString(value),
                ExtensionObject value => ExtensionObjectToDict(value),
                Variant value => VariantToObject(value),
                DataValue value => DataValueToDict(value),
                _ => item.ToString()
            };
        }

        private static List<object?>? ArrayToList<T>(ArrayOf<T> value)
        {
            T[]? array = value.ToArray();
            return array == null ? null : ArrayToList(array);
        }

        private static List<object?>? MatrixToList<T>(MatrixOf<T> value)
        {
            Array? array = value.CreateArrayInstance();
            return array == null ? null : ArrayToList(array);
        }

        private static JsonSerializerOptions CreateJsonOptions()
        {
            return new JsonSerializerOptions
            {
                WriteIndented = true,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };
        }
    }
}
