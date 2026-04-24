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
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Opc.Ua;

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
        public static string Serialize<T>(T value)
        {
            return JsonSerializer.Serialize(value, JsonOptions);
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
                throw new ArgumentException("QualifiedName string cannot be null or empty.", nameof(qualifiedNameString));
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
            if (variant == Variant.Null)
            {
                return null;
            }

            object? value = variant.AsBoxedObject();
            return value switch
            {
                null => null,
                bool b => b,
                sbyte or byte or short or ushort or int or uint or long or ulong => value,
                float f => f,
                double d => d,
                string s => s,
                DateTime dt => dt.ToString("o", CultureInfo.InvariantCulture),
                Uuid uuid => uuid.ToString(),
                byte[] bytes => Convert.ToBase64String(bytes),
                NodeId nodeId => nodeId.ToString(),
                ExpandedNodeId expNodeId => expNodeId.ToString(),
                QualifiedName qn => qn.ToString(),
                LocalizedText lt => lt.Text,
                StatusCode sc => StatusCodeToString(sc),
                ExtensionObject ext => ExtensionObjectToDict(ext),
                Array array => ArrayToList(array),
                _ => value.ToString()
            };
        }

        /// <summary>
        /// Converts a StatusCode to a human-readable string.
        /// </summary>
        public static string StatusCodeToString(StatusCode statusCode)
        {
            return statusCode.SymbolicId;
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
                JsonValueKind.String => new Variant(element.GetString()),
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

            if (ext.TryGetEncodeable(out IEncodeable? encodeable))
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
                list.Add(item switch
                {
                    Variant v => VariantToObject(v),
                    DataValue dv => DataValueToDict(dv),
                    _ => item.ToString()
                });
            }
            return list;
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
