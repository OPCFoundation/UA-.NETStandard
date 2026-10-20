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
using System.Text.Json;
using Opc.Ua.GMS.Contracts;
using Opc.Ua.MachineTool;
using Opc.Ua.Mcp.Serialization;

namespace Opc.Ua.Mcp.Tools
{
    /// <summary>
    /// Validates bounded wire inputs before opening a typed facade or allocating server resources.
    /// </summary>
    internal static class GmsParameters
    {
        /// <summary>
        /// Parses a non-null standard NodeId without guessing namespace indices.
        /// </summary>
        internal static NodeId Node(string value)
        {
            Text(value);
            NodeId node = OpcUaJsonHelper.ParseNodeId(value);
            if (node.IsNull)
            {
                throw new ArgumentException("A non-null GMS instance NodeId is required.", nameof(value));
            }
            return node;
        }

        /// <summary>
        /// Rejects empty or unbounded identifiers before they reach result methods.
        /// </summary>
        internal static void Text(string value)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(value);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value.Length, 1024);
        }

        /// <summary>
        /// Selects exact documented names rather than accepting numeric or case-insensitive enum conversions.
        /// </summary>
        internal static void Select(string value, params string[] allowed)
        {
            if (System.Array.IndexOf(allowed, value) < 0)
            {
                throw new ArgumentException("Select an exact documented operation name.", nameof(value));
            }
        }

        /// <summary>
        /// Applies a bounded inclusive integer interval independently of MCP schema validation.
        /// </summary>
        internal static void Bound(int value, int minimum, int maximum)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, minimum);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, maximum);
        }

        /// <summary>
        /// Bounds the entire encoded JSON response, including base64 and envelope overhead.
        /// </summary>
        internal static void ResponseLimit(int value) => Bound(value, 1024, MaximumResponseBytes);

        /// <summary>
        /// Reuses the native collector's notification and time limits.
        /// </summary>
        internal static MachineToolObservationOptions Observation(int seconds, int notifications, int bytes)
        {
            Bound(seconds, 1, 60);
            Bound(notifications, 1, 100);
            ResponseLimit(bytes);
            return new MachineToolObservationOptions
            {
                Window = TimeSpan.FromSeconds(seconds),
                MaximumNotifications = notifications
            };
        }

        /// <summary>
        /// Supplies finite native metadata, inventory and transfer bounds on every result operation.
        /// </summary>
        internal static GmsResultLimits Results(int maximumResults, int bytes, int fileBytes = 65536)
        {
            Bound(maximumResults, 1, 101);
            ResponseLimit(bytes);
            Bound(fileBytes, 1, 524288);
            return new GmsResultLimits
            {
                MaximumResults = maximumResults,
                MaximumMetadataBytes = bytes,
                MaximumFileBytes = fileBytes,
                MaximumCharacteristics = 32,
                MaximumCorrections = maximumResults,
                MaximumUsedTools = 100
            };
        }

        /// <summary>
        /// Converts a native JSON array to distinct bounded identifiers without exposing ArrayOf internals.
        /// </summary>
        internal static ArrayOf<string> Identifiers(JsonElement input)
        {
            if (input.ValueKind != JsonValueKind.Array || input.GetArrayLength() is < 1 or > 100)
            {
                throw new ArgumentException("resultIds must be an array of 1-100 distinct nonempty strings.",
                    nameof(input));
            }
            var result = new List<string>(input.GetArrayLength());
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonElement element in input.EnumerateArray())
            {
                if (element.ValueKind != JsonValueKind.String)
                {
                    throw new ArgumentException("Each result identifier must be a JSON string.", nameof(input));
                }
                string value = element.GetString()!;
                Text(value);
                if (!seen.Add(value))
                {
                    throw new ArgumentException("Result identifiers must be distinct.", nameof(input));
                }
                result.Add(value);
            }
            return result.ToArrayOf();
        }

        /// <summary>
        /// Parses explicit instance-qualified characteristic placeholders without rewriting their namespace.
        /// </summary>
        internal static ArrayOf<QualifiedName> Characteristics(JsonElement input)
        {
            if (input.ValueKind == JsonValueKind.Undefined)
            {
                return [];
            }
            if (input.ValueKind != JsonValueKind.Array || input.GetArrayLength() > 32)
            {
                throw new ArgumentException("characteristics must be an array of at most 32 qualified names.",
                    nameof(input));
            }
            var result = new List<QualifiedName>(input.GetArrayLength());
            var seen = new HashSet<QualifiedName>();
            foreach (JsonElement element in input.EnumerateArray())
            {
                if (element.ValueKind != JsonValueKind.Object ||
                    !element.TryGetProperty("name", out JsonElement name) || name.ValueKind != JsonValueKind.String ||
                    !element.TryGetProperty("namespaceIndex", out JsonElement index) ||
                    index.ValueKind != JsonValueKind.Number || !index.TryGetUInt16(out ushort ns))
                {
                    throw new ArgumentException("Each characteristic needs name and UInt16 namespaceIndex.",
                        nameof(input));
                }
                int properties = 0;
                foreach (JsonProperty property in element.EnumerateObject())
                {
                    properties++;
                    if (property.Name is not ("name" or "namespaceIndex"))
                    {
                        throw new ArgumentException("Unknown characteristic property.", nameof(input));
                    }
                }
                if (properties != 2)
                {
                    throw new ArgumentException("Characteristic properties must not be duplicated.", nameof(input));
                }
                string text = name.GetString()!;
                ArgumentException.ThrowIfNullOrWhiteSpace(text);
                ArgumentOutOfRangeException.ThrowIfGreaterThan(text.Length, 256);
                var qualified = new QualifiedName(text, ns);
                if (!seen.Add(qualified))
                {
                    throw new ArgumentException("Characteristic qualified names must be distinct.", nameof(input));
                }
                result.Add(qualified);
            }
            return result.ToArrayOf();
        }

        /// <summary>
        /// Parses a small native query-selection object without changing standard filter or ordering semantics.
        /// </summary>
        internal static (string Field, JsonElement Value, string Order) Query(JsonElement input)
        {
            string field = "None";
            JsonElement value = default;
            string order = "ResultId";
            if (input.ValueKind == JsonValueKind.Undefined)
            {
                return (field, value, order);
            }
            if (input.ValueKind != JsonValueKind.Object)
            {
                throw new ArgumentException("query must be a native JSON object.", nameof(input));
            }
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonProperty property in input.EnumerateObject())
            {
                if (!seen.Add(property.Name))
                {
                    throw new ArgumentException("Query selection properties must not be duplicated.", nameof(input));
                }
                switch (property.Name)
                {
                    case "filterField":
                    case "orderBy":
                        if (property.Value.ValueKind != JsonValueKind.String)
                        {
                            throw new ArgumentException("Query selectors require JSON strings.", nameof(input));
                        }
                        if (property.Name == "filterField")
                        {
                            field = property.Value.GetString()!;
                        }
                        else
                        {
                            order = property.Value.GetString()!;
                        }
                        break;
                    case "filterValue":
                        value = property.Value;
                        break;
                    default:
                        throw new ArgumentException("Unknown query selection property.", nameof(input));
                }
            }
            ValidateFilter(field, value, order);
            return (field, value, order);
        }

        /// <summary>
        /// Validates the small metadata filter selector and its native JSON value before session access.
        /// </summary>
        internal static void ValidateFilter(string field, JsonElement value, string orderBy)
        {
            Select(field, "None", "ResultId", "JobId", "IsPartial");
            Select(orderBy, "None", "ResultId");
            switch (field)
            {
                case "None":
                    if (value.ValueKind != JsonValueKind.Undefined)
                    {
                        throw new ArgumentException("filterValue must be omitted when filterField is None.",
                            nameof(value));
                    }
                    break;
                case "IsPartial":
                    if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                    {
                        throw new ArgumentException("IsPartial requires a JSON Boolean.", nameof(value));
                    }
                    break;
                default:
                    if (value.ValueKind != JsonValueKind.String)
                    {
                        throw new ArgumentException("ResultId and JobId require a JSON string.", nameof(value));
                    }
                    Text(value.GetString()!);
                    break;
            }
        }

        /// <summary>
        /// Builds a standard Machinery content filter, leaving evaluation and metadata-handle ownership to clients.
        /// </summary>
        internal static (ContentFilter Filter, ArrayOf<RelativePath> Order) Filter(
            NamespaceTable namespaces, string field, JsonElement value, string orderBy)
        {
            ValidateFilter(field, value, orderBy);
            int index = namespaces.GetIndex(Machinery.Result.Namespaces.MachineryResult);
            if (index < 0)
            {
                throw new ServiceResultException(StatusCodes.BadNotSupported,
                    "The endpoint has no Machinery Result namespace.");
            }
            var filter = new ContentFilter();
            if (field != "None")
            {
                var operand = new SimpleAttributeOperand(
                    NodeId.Create(Machinery.Result.VariableTypes.ResultType,
                        Machinery.Result.Namespaces.MachineryResult, namespaces),
                    [new QualifiedName("ResultMetaData", (ushort)index), new QualifiedName(field, (ushort)index)]);
                filter.Push(FilterOperator.Equals, Variant.FromStructure(operand),
                    field == "IsPartial" ? Variant.From(value.GetBoolean()) : Variant.From(value.GetString()!));
            }
            return (filter, orderBy == "None" ? [] :
            [
                new RelativePath
                {
                    Elements =
                    [
                        new RelativePathElement
                        {
                            ReferenceTypeId = Opc.Ua.ReferenceTypeIds.HierarchicalReferences,
                            IncludeSubtypes = true,
                            TargetName = new QualifiedName("ResultMetaData", (ushort)index)
                        },
                        new RelativePathElement
                        {
                            ReferenceTypeId = Opc.Ua.ReferenceTypeIds.HierarchicalReferences,
                            IncludeSubtypes = true,
                            TargetName = new QualifiedName(orderBy, (ushort)index)
                        }
                    ]
                }
            ]);
        }

        /// <summary>The absolute complete MCP JSON reply limit.</summary>
        internal const int MaximumResponseBytes = 1048576;
    }
}
