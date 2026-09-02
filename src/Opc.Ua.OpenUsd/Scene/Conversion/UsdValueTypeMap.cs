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

namespace Opc.Ua.OpenUsd.Scene.Conversion
{
    /// <summary>
    /// The OPC UA type binding chosen for a USD <c>SdfValueTypeName</c>
    /// (draft OPC UA — OpenUSD Scene Materialization §6.2).
    /// </summary>
    public sealed class UsdValueTypeMapping
    {
        internal UsdValueTypeMapping(
            NodeId dataTypeId,
            int valueRank,
            uint[]? arrayDimensions,
            BuiltInType elementType,
            bool isOpaque)
        {
            DataTypeId = dataTypeId;
            ValueRank = valueRank;
            ArrayDimensions = arrayDimensions;
            ElementType = elementType;
            IsOpaque = isOpaque;
        }

        /// <summary>
        /// The DataType of the materialized Variable. For a role-carrying USD value type this
        /// is the semantic subtype of the built-in (for example <c>UsdColor3f : Float</c>), so
        /// the role is discoverable from the type system rather than only from the
        /// <c>UsdTypeName</c> annotation.
        /// </summary>
        public NodeId DataTypeId { get; }

        /// <summary>
        /// The ValueRank of the materialized Variable.
        /// </summary>
        public int ValueRank { get; }

        /// <summary>
        /// The ArrayDimensions of the materialized Variable, or <c>null</c> for a scalar.
        /// A fixed-size USD math type pins its extent here (for example 3 for
        /// <c>point3f</c>, 16 for <c>matrix4d</c>); a variable-length dimension is 0.
        /// </summary>
        public uint[]? ArrayDimensions { get; }

        /// <summary>
        /// The built-in type the values are encoded as. Because every role DataType subtypes
        /// a built-in, this is what a client that does not recognise the subtype will read.
        /// </summary>
        public BuiltInType ElementType { get; }

        /// <summary>
        /// Whether the USD type was not recognised and the value is carried opaquely as
        /// <c>BaseDataType</c>. The attribute's <c>UsdTypeName</c> still records the exact
        /// spelling, so an exporter reproduces it faithfully (§8.4).
        /// </summary>
        public bool IsOpaque { get; }
    }

    /// <summary>
    /// Maps a USD <c>SdfValueTypeName</c> onto an OPC UA DataType, ValueRank and
    /// ArrayDimensions per the normative table of draft OPC UA — OpenUSD Scene
    /// Materialization §6.2.
    /// </summary>
    /// <remarks>
    /// The mapping is intentionally many-to-one for generic numeric tuples: a <c>Float[3]</c>
    /// unambiguously means <c>float3</c>, while the role variants (<c>color3f</c>,
    /// <c>point3f</c>, …) are told apart by their semantic DataType. Reversibility is
    /// guaranteed because the exact USD type name is always preserved in the attribute's
    /// <c>UsdTypeName</c> property.
    /// </remarks>
    public static class UsdValueTypeMap
    {
        private sealed class Entry
        {
            public Entry(ExpandedNodeId? semantic, NodeId builtIn, BuiltInType element, uint fixedLength)
            {
                Semantic = semantic;
                BuiltIn = builtIn;
                Element = element;
                FixedLength = fixedLength;
            }

            public ExpandedNodeId? Semantic { get; }
            public NodeId BuiltIn { get; }

            public BuiltInType Element { get; }

            /// <summary>
            /// Zero for a scalar; otherwise the pinned extent of the 1-D array.
            /// </summary>
            public uint FixedLength { get; }
        }

        private static readonly Dictionary<string, Entry> s_map =
            new Dictionary<string, Entry>(StringComparer.Ordinal)
            {
                // Scalars that map straight onto a built-in.
                ["bool"] = new Entry(null, Opc.Ua.DataTypeIds.Boolean, BuiltInType.Boolean, 0),
                ["uchar"] = new Entry(null, Opc.Ua.DataTypeIds.SByte, BuiltInType.SByte, 0),
                ["int"] = new Entry(null, Opc.Ua.DataTypeIds.Int32, BuiltInType.Int32, 0),
                ["int64"] = new Entry(null, Opc.Ua.DataTypeIds.Int64, BuiltInType.Int64, 0),
                ["uint"] = new Entry(null, Opc.Ua.DataTypeIds.UInt32, BuiltInType.UInt32, 0),
                ["uint64"] = new Entry(null, Opc.Ua.DataTypeIds.UInt64, BuiltInType.UInt64, 0),
                ["half"] = new Entry(null, Opc.Ua.DataTypeIds.Float, BuiltInType.Float, 0),
                ["float"] = new Entry(null, Opc.Ua.DataTypeIds.Float, BuiltInType.Float, 0),
                ["double"] = new Entry(null, Opc.Ua.DataTypeIds.Double, BuiltInType.Double, 0),
                ["string"] = new Entry(null, Opc.Ua.DataTypeIds.String, BuiltInType.String, 0),

                // Scalars that carry a USD role, expressed as a subtype of the built-in (§5.7).
                ["token"] = new Entry(
                    DataTypeIds.UsdToken, Opc.Ua.DataTypeIds.String, BuiltInType.String, 0),
                ["asset"] = new Entry(
                    DataTypeIds.UsdAssetPath, Opc.Ua.DataTypeIds.String, BuiltInType.String, 0),
                ["timecode"] = new Entry(
                    DataTypeIds.UsdTimeCode, Opc.Ua.DataTypeIds.Double, BuiltInType.Double, 0),

                // Generic fixed-size numeric tuples — the shape alone is the meaning, so these
                // stay plain built-in arrays and ArrayDimensions conveys the extent.
                ["float2"] = new Entry(null, Opc.Ua.DataTypeIds.Float, BuiltInType.Float, 2),
                ["float3"] = new Entry(null, Opc.Ua.DataTypeIds.Float, BuiltInType.Float, 3),
                ["float4"] = new Entry(null, Opc.Ua.DataTypeIds.Float, BuiltInType.Float, 4),
                ["double2"] = new Entry(null, Opc.Ua.DataTypeIds.Double, BuiltInType.Double, 2),
                ["double3"] = new Entry(null, Opc.Ua.DataTypeIds.Double, BuiltInType.Double, 3),
                ["double4"] = new Entry(null, Opc.Ua.DataTypeIds.Double, BuiltInType.Double, 4),
                ["int2"] = new Entry(null, Opc.Ua.DataTypeIds.Int32, BuiltInType.Int32, 2),
                ["int3"] = new Entry(null, Opc.Ua.DataTypeIds.Int32, BuiltInType.Int32, 3),
                ["int4"] = new Entry(null, Opc.Ua.DataTypeIds.Int32, BuiltInType.Int32, 4),

                // Role-carrying vectors: same encoding, distinct DataType so a renderer can
                // tell a colour from a point without parsing a string.
                ["color3f"] = new Entry(
                    DataTypeIds.UsdColor3f, Opc.Ua.DataTypeIds.Float, BuiltInType.Float, 3),
                ["normal3f"] = new Entry(
                    DataTypeIds.UsdNormal3f, Opc.Ua.DataTypeIds.Float, BuiltInType.Float, 3),
                ["point3f"] = new Entry(
                    DataTypeIds.UsdPoint3f, Opc.Ua.DataTypeIds.Float, BuiltInType.Float, 3),
                ["vector3f"] = new Entry(
                    DataTypeIds.UsdVector3f, Opc.Ua.DataTypeIds.Float, BuiltInType.Float, 3),
                ["texCoord2f"] = new Entry(
                    DataTypeIds.UsdTexCoord2f, Opc.Ua.DataTypeIds.Float, BuiltInType.Float, 2),
                ["quatf"] = new Entry(
                    DataTypeIds.UsdQuatf, Opc.Ua.DataTypeIds.Float, BuiltInType.Float, 4),
                ["quatd"] = new Entry(
                    DataTypeIds.UsdQuatd, Opc.Ua.DataTypeIds.Double, BuiltInType.Double, 4),
                ["matrix4d"] = new Entry(
                    DataTypeIds.UsdMatrix4d, Opc.Ua.DataTypeIds.Double, BuiltInType.Double, 16),
            };

        /// <summary>
        /// Maps a USD value type name onto its OPC UA binding.
        /// </summary>
        /// <param name="sdfValueTypeName">The USD type name, for example <c>float3</c>,
        /// <c>token</c> or <c>color3f[]</c>. A trailing <c>[]</c> adds one rank.</param>
        /// <param name="namespaceUris">The server namespace table, used to resolve the
        /// semantic DataTypes of this companion model. When <c>null</c>, a recognised role
        /// type degrades to its built-in supertype, which is still a correct — if less
        /// specific — binding.</param>
        /// <returns>The binding. An unrecognised type never throws: it degrades to an opaque
        /// <c>BaseDataType</c> binding per §8.4.</returns>
        public static UsdValueTypeMapping Map(string sdfValueTypeName, NamespaceTable? namespaceUris)
        {
            string name = Normalize(sdfValueTypeName, out bool isArray);

            if (!s_map.TryGetValue(name, out Entry? entry))
            {
                // Unknown value type: carry it opaquely rather than dropping it (§8.4).
                return new UsdValueTypeMapping(
                    Opc.Ua.DataTypeIds.BaseDataType,
                    isArray ? ValueRanks.OneDimension : ValueRanks.Scalar,
                    isArray ? new uint[] { 0 } : null,
                    BuiltInType.Variant,
                    isOpaque: true);
            }

            NodeId dataTypeId = ResolveDataType(entry, namespaceUris);
            bool isTuple = entry.FixedLength > 0;

            // A fixed-size math type is already a 1-D array; an explicit USD array adds one
            // further rank, with the outer (variable) dimension reported as 0.
            int rank;
            uint[]? dimensions;
            if (isTuple)
            {
                rank = isArray ? ValueRanks.TwoDimensions : ValueRanks.OneDimension;
                dimensions = isArray
                    ? new uint[] { 0, entry.FixedLength }
                    : new uint[] { entry.FixedLength };
            }
            else
            {
                rank = isArray ? ValueRanks.OneDimension : ValueRanks.Scalar;
                dimensions = isArray ? new uint[] { 0 } : null;
            }

            return new UsdValueTypeMapping(dataTypeId, rank, dimensions, entry.Element, isOpaque: false);
        }

        /// <summary>
        /// Whether the value type name is one this model maps to a concrete DataType.
        /// </summary>
        /// <param name="sdfValueTypeName">The USD type name.</param>
        /// <returns><c>true</c> when the type is recognised.</returns>
        public static bool IsKnown(string sdfValueTypeName)
        {
            return s_map.ContainsKey(Normalize(sdfValueTypeName, out _));
        }

        /// <summary>
        /// The number of components a fixed-size USD math type decomposes into, or 0 when the
        /// type is a scalar or unknown. <c>matrix4d</c> reports 16 (row-major flattened).
        /// </summary>
        /// <param name="sdfValueTypeName">The USD type name.</param>
        /// <returns>The component count.</returns>
        public static uint ComponentCount(string sdfValueTypeName)
        {
            return s_map.TryGetValue(Normalize(sdfValueTypeName, out _), out Entry? entry)
                ? entry.FixedLength
                : 0;
        }

        private static string Normalize(string? sdfValueTypeName, out bool isArray)
        {
            string name = (sdfValueTypeName ?? string.Empty).Trim();
            isArray = name.EndsWith("[]", StringComparison.Ordinal);
            return isArray ? name.Substring(0, name.Length - 2) : name;
        }

        private static NodeId ResolveDataType(Entry entry, NamespaceTable? namespaceUris)
        {
            ExpandedNodeId? semantic = entry.Semantic;
            if (!semantic.HasValue || namespaceUris == null)
            {
                // Without a namespace table the semantic subtype cannot be addressed; the
                // built-in supertype carries exactly the same value encoding.
                return entry.BuiltIn;
            }
            NodeId resolved = ExpandedNodeId.ToNodeId(semantic.Value, namespaceUris);
            return resolved.IsNull ? entry.BuiltIn : resolved;
        }
    }
}
