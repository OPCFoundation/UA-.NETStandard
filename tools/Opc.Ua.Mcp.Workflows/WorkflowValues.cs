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
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Opc.Ua.Client;

namespace Opc.Ua.Mcp
{
    /// <summary>
    /// Converts a closed, AOT-safe set of typed workflow inputs before invoking any remote mutation.
    /// </summary>
    internal static class WorkflowValues
    {
        /// <summary>
        /// Bounds complete typed batches before allocating UA update payloads or sending any operation.
        /// </summary>
        /// <typeparam name="T">A source-generated workflow input type.</typeparam>
        /// <exception cref="InvalidOperationException"></exception>
        internal static void ValidateInline<T>(T value, ClientWorkflowOptions options)
            where T : class
        {
            ArgumentNullException.ThrowIfNull(value);
            JsonTypeInfo<T> contract = WorkflowInputJsonContext.Default.GetTypeInfo(typeof(T)) as JsonTypeInfo<T> ??
                throw new InvalidOperationException("The request type has no generated JSON contract.");
            using var buffer = new McpBoundedMemoryStream(options.MaxInlineBytes);
            JsonSerializer.Serialize(buffer, value, contract);
        }

        /// <summary>
        /// Parses built-in scalar and one-dimensional values without boxed Variant access.
        /// </summary>
        /// <exception cref="ArgumentException"></exception>
        internal static Variant Parse(ISession session, WorkflowValueInput input, ClientWorkflowOptions options)
        {
            ArgumentNullException.ThrowIfNull(input);
            if (Encoding.UTF8.GetByteCount(input.Value.GetRawText()) > options.MaxInlineBytes)
            {
                throw new ArgumentException("The typed value exceeds the host's inline byte ceiling.");
            }
            JsonElement value = input.Value;
            if (value.ValueKind == JsonValueKind.Array)
            {
                ArgumentOutOfRangeException.ThrowIfGreaterThan(value.GetArrayLength(), options.MaxItems);
                return ParseArray(session, input.Type, [.. value.EnumerateArray()], options);
            }
            return input.Type switch
            {
                BuiltInType.Null when value.ValueKind == JsonValueKind.Null => Variant.Null,
                BuiltInType.Boolean => Variant.From(value.GetBoolean()),
                BuiltInType.SByte => Variant.From(value.GetSByte()),
                BuiltInType.Byte => Variant.From(value.GetByte()),
                BuiltInType.Int16 => Variant.From(value.GetInt16()),
                BuiltInType.UInt16 => Variant.From(value.GetUInt16()),
                BuiltInType.Int32 => Variant.From(value.GetInt32()),
                BuiltInType.UInt32 => Variant.From(value.GetUInt32()),
                BuiltInType.Int64 => Variant.From(value.GetInt64()),
                BuiltInType.UInt64 => Variant.From(value.GetUInt64()),
                BuiltInType.Float => Variant.From((float)Number(value)),
                BuiltInType.Double => Variant.From(Number(value)),
                BuiltInType.String => Variant.From(String(value)),
                BuiltInType.DateTime => Variant.From((DateTimeUtc)WorkflowJson.Utc(String(value))),
                BuiltInType.Guid => Variant.From(new Uuid(Guid.Parse(String(value)))),
                BuiltInType.ByteString => Variant.From(Bytes(String(value), options.MaxInlineBytes)),
                BuiltInType.NodeId => Variant.From(WorkflowJson.Node(session, String(value))),
                BuiltInType.StatusCode => Variant.From(new StatusCode(value.GetUInt32())),
                BuiltInType.LocalizedText => Variant.From(new LocalizedText(String(value))),
                _ => throw new ArgumentException("The scalar type is not supported by this typed contract.")
            };
        }

        /// <summary>
        /// Assembles explicitly indexed array values without accepting nested or JSON-encoded payloads.
        /// </summary>
        /// <exception cref="ArgumentException"></exception>
        internal static Dictionary<int, JsonElement[]> Arrays(
            int valueCount, Func<int, int?> length, ArrayOf<WorkflowArrayElementInput> elements,
            ClientWorkflowOptions options)
        {
            ArgumentOutOfRangeException.ThrowIfGreaterThan(elements.Count, options.MaxItems);
            int total = 0;
            var sizes = new Dictionary<int, int>();
            for (int index = 0; index < valueCount; index++)
            {
                if (length(index) is not { } size)
                {
                    continue;
                }
                ArgumentOutOfRangeException.ThrowIfNegative(size);
                ArgumentOutOfRangeException.ThrowIfGreaterThan(size, options.MaxItems);
                if (size > options.MaxItems - total)
                {
                    throw new ArgumentException("The total array-element count exceeds the host's item ceiling.");
                }
                sizes.Add(index, size);
                total += size;
            }
            if (total != elements.Count)
            {
                throw new ArgumentException("arrayElements must exactly fill every declared arrayLength.");
            }
            var arrays = new Dictionary<int, JsonElement[]>();
            foreach ((int index, int size) in sizes)
            {
                arrays.Add(index, new JsonElement[size]);
            }
            foreach (WorkflowArrayElementInput element in elements)
            {
                ArgumentNullException.ThrowIfNull(element);
                if (!arrays.TryGetValue(element.ValueIndex, out JsonElement[]? array) ||
                    element.ElementIndex < 0 ||
                    element.ElementIndex >= array.Length ||
                    array[element.ElementIndex].ValueKind != JsonValueKind.Undefined)
                {
                    throw new ArgumentException("An array element has an invalid or duplicate value/element index.");
                }
                Scalar(element.Value);
                array[element.ElementIndex] = element.Value;
            }
            return arrays;
        }

        /// <summary>
        /// Interprets one flat declaration using its discriminator and, when present, indexed array elements.
        /// </summary>
        /// <exception cref="ArgumentException"></exception>
        internal static Variant Flat(
            ISession session, BuiltInType type, JsonElement value, int index,
            Dictionary<int, JsonElement[]> arrays, ClientWorkflowOptions options)
        {
            if (arrays.TryGetValue(index, out JsonElement[]? elements))
            {
                if (value.ValueKind is not JsonValueKind.Undefined and not JsonValueKind.Null)
                {
                    throw new ArgumentException("A declared array uses arrayElements, not a scalar value.");
                }
                return ParseArray(session, type, elements, options);
            }
            Scalar(value);
            return Parse(session, new WorkflowValueInput { Type = type, Value = value }, options);
        }

        /// <summary>
        /// Decodes a bounded base64 identifier before allocating decoded storage.
        /// </summary>
        /// <exception cref="ArgumentException"></exception>
        internal static ByteString Bytes(string text, int maximum)
        {
            WorkflowJson.Text(text, checked((maximum + 2) / 3 * 4), empty: true);
            byte[] bytes = Convert.FromBase64String(text);
            if (bytes.Length > maximum)
            {
                throw new ArgumentException("The decoded byte string exceeds its host ceiling.");
            }
            return ByteString.From(bytes);
        }

        /// <summary>
        /// Converts exact event field selectors, preserving namespace URIs.
        /// </summary>
        /// <exception cref="ArgumentException"></exception>
        internal static EventFilter Filter(
            ISession session, ArrayOf<WorkflowEventFieldInput> fields, ArrayOf<WorkflowEventPathInput> paths = default)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(fields.Count, 1);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(fields.Count, 32);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(paths.Count, 256);
            var segments = new Dictionary<int, Dictionary<int, QualifiedName>>();
            foreach (WorkflowEventPathInput part in paths)
            {
                ArgumentNullException.ThrowIfNull(part);
                if (part.SelectIndex < 0 ||
                    part.SelectIndex >= fields.Count ||
                    part.SegmentIndex < 0 ||
                    part.SegmentIndex >= 8)
                {
                    throw new ArgumentException("An event path index is outside its selector or eight-segment limit.");
                }
                WorkflowJson.Text(part.Name, 256);
                WorkflowJson.Text(part.NamespaceUri);
                if (!segments.TryGetValue(part.SelectIndex, out Dictionary<int, QualifiedName>? selected))
                {
                    selected = [];
                    segments.Add(part.SelectIndex, selected);
                }
                if (!selected.TryAdd(part.SegmentIndex, McpModelAccess.Name(session, part.NamespaceUri, part.Name)))
                {
                    throw new ArgumentException("Event path segment indexes must be unique within each selector.");
                }
            }
            var clauses = new SimpleAttributeOperand[fields.Count];
            for (int index = 0; index < clauses.Length; index++)
            {
                WorkflowEventFieldInput input = fields[index];
                ArgumentNullException.ThrowIfNull(input);
                if (input.AttributeId is not Attributes.Value and not Attributes.NodeId)
                {
                    throw new ArgumentException("An event field selects only Value or NodeId.");
                }
                ArrayOf<QualifiedName> path;
                if (input.BrowseName is not null)
                {
                    if (segments.ContainsKey(index))
                    {
                        throw new ArgumentException("Use a single browseName or indexed paths, not both.");
                    }
                    WorkflowJson.Text(input.BrowseName, 256);
                    string uri = input.NamespaceUri ?? Namespaces.OpcUa;
                    WorkflowJson.Text(uri);
                    path = [McpModelAccess.Name(session, uri, input.BrowseName)];
                }
                else
                {
                    if (input.NamespaceUri is not null)
                    {
                        throw new ArgumentException("A selector namespaceUri requires its inline browseName.");
                    }
                    if (!segments.TryGetValue(index, out Dictionary<int, QualifiedName>? selected))
                    {
                        path = [];
                    }
                    else
                    {
                        var ordered = new QualifiedName[selected.Count];
                        for (int part = 0; part < ordered.Length; part++)
                        {
                            if (!selected.TryGetValue(part, out ordered[part]))
                            {
                                throw new ArgumentException("Event path segments must be contiguous from zero.");
                            }
                        }
                        path = ordered.ToArrayOf();
                    }
                }
                clauses[index] = new SimpleAttributeOperand
                {
                    TypeDefinitionId = WorkflowJson.Node(session, input.TypeDefinitionId),
                    BrowsePath = path,
                    AttributeId = input.AttributeId
                };
            }
            return new EventFilter { SelectClauses = clauses.ToArrayOf() };
        }

        /// <summary>
        /// Refuses hidden objects and nested arrays where the interface promises one actual JSON scalar.
        /// </summary>
        /// <exception cref="ArgumentException"></exception>
        private static void Scalar(JsonElement value)
        {
            if (value.ValueKind is JsonValueKind.Undefined or JsonValueKind.Object or JsonValueKind.Array)
            {
                throw new ArgumentException("A field value must be an actual JSON scalar, not an object or array.");
            }
        }

        /// <summary>
        /// Converts a bounded scalar array using the declared UA element type.
        /// </summary>
        /// <exception cref="ArgumentException"></exception>
        private static Variant ParseArray(
            ISession session, BuiltInType type, JsonElement[] values, ClientWorkflowOptions options)
        {
            return type switch
            {
                BuiltInType.Boolean => Variant.From(values.Select(element => element.GetBoolean()).ToArrayOf()),
                BuiltInType.SByte => Variant.From(values.Select(element => element.GetSByte()).ToArrayOf()),
                BuiltInType.Byte => Variant.From(values.Select(element => element.GetByte()).ToArrayOf()),
                BuiltInType.Int16 => Variant.From(values.Select(element => element.GetInt16()).ToArrayOf()),
                BuiltInType.UInt16 => Variant.From(values.Select(element => element.GetUInt16()).ToArrayOf()),
                BuiltInType.Int32 => Variant.From(values.Select(element => element.GetInt32()).ToArrayOf()),
                BuiltInType.UInt32 => Variant.From(values.Select(element => element.GetUInt32()).ToArrayOf()),
                BuiltInType.Int64 => Variant.From(values.Select(element => element.GetInt64()).ToArrayOf()),
                BuiltInType.UInt64 => Variant.From(values.Select(element => element.GetUInt64()).ToArrayOf()),
                BuiltInType.Float => Variant.From(values.Select(element => (float)Number(element)).ToArrayOf()),
                BuiltInType.Double => Variant.From(values.Select(Number).ToArrayOf()),
                BuiltInType.String => Variant.From(values.Select(String).ToArrayOf()),
                BuiltInType.DateTime => Variant.From(values.Select(element =>
                    (DateTimeUtc)WorkflowJson.Utc(String(element))).ToArrayOf()),
                BuiltInType.NodeId => Variant.From(values.Select(element =>
                    WorkflowJson.Node(session, String(element))).ToArrayOf()),
                BuiltInType.ByteString => Variant.From(values.Select(element =>
                    Bytes(String(element), options.MaxInlineBytes)).ToArrayOf()),
                BuiltInType.Guid => Variant.From(values.Select(element =>
                    new Uuid(Guid.Parse(String(element)))).ToArrayOf()),
                BuiltInType.StatusCode => Variant.From(values.Select(element =>
                    new StatusCode(element.GetUInt32())).ToArrayOf()),
                BuiltInType.LocalizedText => Variant.From(values.Select(element =>
                    new LocalizedText(String(element))).ToArrayOf()),
                _ => throw new ArgumentException("The array element type is not supported by this typed contract.")
            };
        }

        /// <summary>
        /// Requires a JSON string rather than silently coercing another value kind.
        /// </summary>
        /// <exception cref="ArgumentException"></exception>
        private static string String(JsonElement value)
        {
            return value.GetString() ?? throw new ArgumentException("A non-null string value is required.");
        }

        /// <summary>
        /// Accepts the standard UA JSON tokens for non-finite IEEE values, but not general string coercion.
        /// </summary>
        /// <exception cref="ArgumentException"></exception>
        private static double Number(JsonElement value)
        {
            return value.ValueKind == JsonValueKind.String ? value.GetString() switch
            {
                "NaN" => double.NaN,
                "Infinity" => double.PositiveInfinity,
                "-Infinity" => double.NegativeInfinity,
                _ => throw new ArgumentException("A floating value is a JSON number or NaN/Infinity/-Infinity.")
            } : value.GetDouble();
        }
    }
}
