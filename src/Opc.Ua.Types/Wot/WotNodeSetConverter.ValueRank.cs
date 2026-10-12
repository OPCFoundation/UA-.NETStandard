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
 *
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

using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Opc.Ua.Wot
{
    /// <summary>
    /// The ValueRank and ArrayDimensions mapping of WoT Binding Sections 7 and
    /// 9.1 for ordinary Variable affordances.
    /// </summary>
    /// <remarks>
    /// A DataSchema's <c>type</c> and <c>items</c> say whether a value is an
    /// array, but not which of the five things an OPC UA ValueRank says.
    /// <c>-3</c> admits a scalar or a one-dimensional array, <c>-2</c> admits
    /// any rank, <c>-1</c> is a scalar, <c>0</c> requires at least one
    /// dimension and a positive value fixes the number of dimensions exactly.
    /// Collapsing any of the ambiguous ones to a scalar states something the
    /// source does not, so the term is written whenever the rank is not the
    /// scalar default and read back exactly as written.
    /// </remarks>
    public static partial class WotNodeSetConverter
    {
        /// <summary>
        /// The <c>uav</c> terms carrying an OPC UA ValueRank and its
        /// ArrayDimensions.
        /// </summary>
        internal const string ValueRankTerm = "uav:valueRank";

        /// <inheritdoc cref="ValueRankTerm"/>
        internal const string ArrayDimensionsTerm = "uav:arrayDimensions";

        /// <summary>
        /// The OPC 10000-3 ValueRank of a scalar, which a NodeSet omits and
        /// which is therefore the default on both sides.
        /// </summary>
        internal const int ScalarValueRank = -1;

        /// <summary>
        /// The lowest ValueRank OPC 10000-3 defines
        /// (<c>ScalarOrOneDimension</c>).
        /// </summary>
        internal const int LowestValueRank = -3;

        /// <summary>
        /// Writes the ValueRank and ArrayDimensions of a Variable affordance.
        /// </summary>
        /// <remarks>
        /// The scalar rank is the default a NodeSet omits, so writing it would
        /// state a fact the source only implies; every other rank is written,
        /// because nothing else in the readable document carries it.
        /// ArrayDimensions is written whenever the source has one, including
        /// the zero bound OPC 10000-3 uses for a dimension whose length is not
        /// fixed.
        /// </remarks>
        private static void WriteVariableRank(
            Utf8JsonWriter writer,
            int valueRank,
            string? arrayDimensions)
        {
            if (valueRank != ScalarValueRank)
            {
                writer.WriteNumber(ValueRankTerm, valueRank);
            }
            WriteFieldArrayDimensions(writer, arrayDimensions);
        }

        private static void WriteRankedJsonType(Utf8JsonWriter writer, string? dataType, int valueRank)
        {
            if (valueRank is -3 or -2)
            {
                if (MapDataTypeToJson(dataType) is null)
                {
                    return;
                }
                writer.WritePropertyName("oneOf");
                writer.WriteStartArray();
                writer.WriteStartObject();
                WriteArgumentJsonType(writer, dataType);
                writer.WriteEndObject();
                writer.WriteStartObject();
                WriteArrayJsonType(writer, dataType, valueRank == -3 ? 1 : 0);
                writer.WriteEndObject();
                writer.WriteEndArray();
            }
            else if (valueRank >= 0)
            {
                WriteArrayJsonType(writer, dataType, valueRank);
            }
            else
            {
                WriteArgumentJsonType(writer, dataType);
            }
        }

        private static void WriteArrayJsonType(Utf8JsonWriter writer, string? dataType, int dimensions)
        {
            if (dimensions == 0)
            {
                writer.WriteString("type", "array");
                return;
            }
            for (int dimension = 0; dimension < dimensions; dimension++)
            {
                writer.WriteString("type", "array");
                writer.WritePropertyName("items");
                writer.WriteStartObject();
            }
            WriteArgumentJsonType(writer, dataType);
            for (int dimension = 0; dimension < dimensions; dimension++)
            {
                writer.WriteEndObject();
            }
        }

        internal static bool MapsRankedJsonType(JsonElement schema, string member)
        {
            if (!schema.TryGetProperty(member, out JsonElement actual))
            {
                return false;
            }
            int rank = ReadValueRank(schema);
            if (rank > 0)
            {
                int dimensions = 0;
                JsonElement nested = schema;
                while (GetElementString(nested, "type") == "array" &&
                    nested.TryGetProperty("items", out JsonElement items) &&
                    items.ValueKind == JsonValueKind.Object)
                {
                    dimensions++;
                    nested = items;
                }
                if (dimensions != rank)
                {
                    return false;
                }
            }
            using JsonDocument? generated = CreateRankedJsonType(schema);
            return generated is not null &&
                generated.RootElement.TryGetProperty(member, out JsonElement expected) &&
                JsonElement.DeepEquals(actual, expected);
        }

        /// <summary>
        /// Regenerates the ranked JSON type only when the schema states its definitive DataType.
        /// </summary>
        internal static JsonDocument? CreateRankedJsonType(JsonElement schema)
        {
            string? dataType = GetElementString(schema, "uav:mapToType");
            if (dataType is null)
            {
                return null;
            }
            using var output = new MemoryStream();
            using (var writer = new Utf8JsonWriter(output))
            {
                writer.WriteStartObject();
                WriteRankedJsonType(writer, NormalizeExpandedNodeId(dataType), ReadValueRank(schema));
                writer.WriteEndObject();
            }
            return JsonDocument.Parse(output.ToArray());
        }

        /// <summary>
        /// Reads an affordance's authored ValueRank.
        /// </summary>
        internal static int ReadValueRank(JsonElement element)
        {
            return GetElementInt32(element, ValueRankTerm) ?? ScalarValueRank;
        }

        /// <summary>
        /// Validates <c>uav:valueRank</c> and <c>uav:arrayDimensions</c>
        /// against the OPC 10000-3 semantics WoT Binding Section 7 refers to.
        /// </summary>
        /// <remarks>
        /// ArrayDimensions carries one bound per dimension, so its length is
        /// the rank by construction, and a rank that is not fixed has no
        /// dimension count to state. Both violations would otherwise
        /// materialize silently into a malformed Variable, which is why they
        /// are reported rather than repaired.
        /// </remarks>
        private static void ValidateValueRank(
            JsonElement element,
            string parentPointer,
            List<WotDiagnostic> diagnostics)
        {
            if (element.ValueKind != JsonValueKind.Object)
            {
                return;
            }
            int rank = ScalarValueRank;
            if (element.TryGetProperty(ValueRankTerm, out JsonElement declared))
            {
                if (declared.ValueKind != JsonValueKind.Number ||
                    !IsIntegerLiteral(declared) ||
                    !declared.TryGetInt32(out rank) ||
                    rank < LowestValueRank)
                {
                    diagnostics.Add(new WotDiagnostic(
                        WotDiagnosticSeverity.Error,
                        WotDiagnosticCode.InvalidValueRank,
                        $"The {ValueRankTerm} term shall be an integer no lower " +
                        $"than {LowestValueRank}, the lowest ValueRank " +
                        "OPC 10000-3 defines (WoT Binding Section 7).",
                        WotLocation.FromPointer(parentPointer + "/" + ValueRankTerm)));
                    return;
                }
            }
            if (!element.TryGetProperty(ArrayDimensionsTerm, out JsonElement dimensions))
            {
                return;
            }
            string pointer = parentPointer + "/" + ArrayDimensionsTerm;
            if (dimensions.ValueKind != JsonValueKind.Array)
            {
                diagnostics.Add(new WotDiagnostic(
                    WotDiagnosticSeverity.Error,
                    WotDiagnosticCode.InvalidValueRank,
                    $"The {ArrayDimensionsTerm} term shall be an ordered array of " +
                    "non-negative dimensions (WoT Binding Section 7).",
                    WotLocation.FromPointer(pointer)));
                return;
            }
            int count = 0;
            foreach (JsonElement dimension in dimensions.EnumerateArray())
            {
                count++;
                if (dimension.ValueKind != JsonValueKind.Number ||
                    !IsIntegerLiteral(dimension) ||
                    !dimension.TryGetUInt32(out _))
                {
                    diagnostics.Add(new WotDiagnostic(
                        WotDiagnosticSeverity.Error,
                        WotDiagnosticCode.InvalidValueRank,
                        $"A {ArrayDimensionsTerm} entry shall be a non-negative " +
                        "integer; OPC 10000-3 uses zero for a dimension whose " +
                        "length is not fixed (WoT Binding Section 7).",
                        WotLocation.FromPointer(pointer)));
                    return;
                }
            }
            if (count == 0)
            {
                return;
            }
            if (rank <= 0)
            {
                diagnostics.Add(new WotDiagnostic(
                    WotDiagnosticSeverity.Error,
                    WotDiagnosticCode.InvalidValueRank,
                    $"The affordance states {count} array dimension(s) against a " +
                    $"ValueRank of {rank}, which fixes no number of dimensions. " +
                    "OPC 10000-3 admits ArrayDimensions only for a fixed rank of " +
                    "at least one.",
                    WotLocation.FromPointer(pointer)));
                return;
            }
            if (count != rank)
            {
                diagnostics.Add(new WotDiagnostic(
                    WotDiagnosticSeverity.Error,
                    WotDiagnosticCode.InvalidValueRank,
                    $"The affordance states {count} array dimension(s) against a " +
                    $"ValueRank of {rank}. ArrayDimensions carries one bound per " +
                    "dimension, so its length is the rank.",
                    WotLocation.FromPointer(pointer)));
            }
        }
    }
}
