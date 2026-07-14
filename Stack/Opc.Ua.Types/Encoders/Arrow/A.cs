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
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Apache.Arrow;
using Apache.Arrow.Arrays;
using Apache.Arrow.Ipc;
using Apache.Arrow.Memory;
using Apache.Arrow.Types;
using Opc.Ua.Types;

namespace Opc.Ua
{
    /// <summary>
    /// Builds and reads the Arrow arrays used by the experimental Arrow encoder and decoder.
    /// </summary>
    internal static class A
    {
        private static readonly MemoryAllocator Alloc = MemoryAllocator.Default.Value;
        private static readonly int[] DenseBinaryUnionTypeIds = [0, 1];
        private const int VariantArrayCodeBase = 40;
        private const int VariantMatrixCodeBase = 80;

        private static Field F(string n, IArrowType t, bool nullable = true)
        {
            return new(n, t, nullable, null);
        }

        private static ArrowBuffer B<T>(params T[] values)
            where T : struct
        {
            var b = new ArrowBuffer.Builder<T>(values.Length);
            b.Append(values.AsSpan());
            return b.Build(Alloc);
        }

        private static ArrowBuffer V(int length, bool valid)
        {
            var b = new ArrowBuffer.BitmapBuilder(length);
            b.AppendRange(valid, length);
            return b.Build(Alloc);
        }

        private static ArrowBuffer V(ReadOnlySpan<bool> valid)
        {
            var b = new ArrowBuffer.BitmapBuilder(valid.Length);
            foreach (bool value in valid)
            {
                b.Append(value);
            }

            return b.Build(Alloc);
        }

        /// <summary>
        /// Creates an Arrow slot containing one Boolean value.
        /// </summary>
        /// <param name = "v">The input required by this experimental codec helper.</param>
        /// <returns>The result produced by this codec helper.</returns>
        public static Slot Bool(bool v)
        {
            var b = new BooleanArray.Builder();
            b.Append(v);
            return new(F(string.Empty, BooleanType.Default, false), b.Build(Alloc));
        }

        /// <summary>
        /// Creates an Arrow slot containing one signed 8-bit integer.
        /// </summary>
        /// <param name = "v">The input required by this experimental codec helper.</param>
        /// <returns>The result produced by this codec helper.</returns>
        public static Slot I8(sbyte v)
        {
            var b = new Int8Array.Builder();
            b.Append(v);
            return new(F(string.Empty, Int8Type.Default, false), b.Build(Alloc));
        }

        /// <summary>
        /// Creates an Arrow slot containing one unsigned 8-bit integer.
        /// </summary>
        /// <param name = "v">The input required by this experimental codec helper.</param>
        /// <returns>The result produced by this codec helper.</returns>
        public static Slot U8(byte v)
        {
            var b = new UInt8Array.Builder();
            b.Append(v);
            return new(F(string.Empty, UInt8Type.Default, false), b.Build(Alloc));
        }

        /// <summary>
        /// Creates an Arrow slot containing one signed 16-bit integer.
        /// </summary>
        /// <param name = "v">The input required by this experimental codec helper.</param>
        /// <returns>The result produced by this codec helper.</returns>
        public static Slot I16(short v)
        {
            var b = new Int16Array.Builder();
            b.Append(v);
            return new(F(string.Empty, Int16Type.Default, false), b.Build(Alloc));
        }

        /// <summary>
        /// Creates an Arrow slot containing one unsigned 16-bit integer.
        /// </summary>
        /// <param name = "v">The input required by this experimental codec helper.</param>
        /// <returns>The result produced by this codec helper.</returns>
        public static Slot U16(ushort v)
        {
            var b = new UInt16Array.Builder();
            b.Append(v);
            return new(F(string.Empty, UInt16Type.Default, false), b.Build(Alloc));
        }

        /// <summary>
        /// Creates an Arrow slot containing one signed 32-bit integer.
        /// </summary>
        /// <param name = "v">The input required by this experimental codec helper.</param>
        /// <returns>The result produced by this codec helper.</returns>
        public static Slot I32(int v)
        {
            var b = new Int32Array.Builder();
            b.Append(v);
            return new(F(string.Empty, Int32Type.Default, false), b.Build(Alloc));
        }

        /// <summary>
        /// Creates an Arrow slot containing one unsigned 32-bit integer.
        /// </summary>
        /// <param name = "v">The input required by this experimental codec helper.</param>
        /// <returns>The result produced by this codec helper.</returns>
        public static Slot U32(uint v)
        {
            var b = new UInt32Array.Builder();
            b.Append(v);
            return new(F(string.Empty, UInt32Type.Default, false), b.Build(Alloc));
        }

        /// <summary>
        /// Creates an Arrow slot containing one signed 64-bit integer.
        /// </summary>
        /// <param name = "v">The input required by this experimental codec helper.</param>
        /// <returns>The result produced by this codec helper.</returns>
        public static Slot I64(long v)
        {
            var b = new Int64Array.Builder();
            b.Append(v);
            return new(F(string.Empty, Int64Type.Default, false), b.Build(Alloc));
        }

        /// <summary>
        /// Creates an Arrow slot containing one unsigned 64-bit integer.
        /// </summary>
        /// <param name = "v">The input required by this experimental codec helper.</param>
        /// <returns>The result produced by this codec helper.</returns>
        public static Slot U64(ulong v)
        {
            var b = new UInt64Array.Builder();
            b.Append(v);
            return new(F(string.Empty, UInt64Type.Default, false), b.Build(Alloc));
        }

        /// <summary>
        /// Creates an Arrow slot containing one single-precision floating-point value.
        /// </summary>
        /// <param name = "v">The input required by this experimental codec helper.</param>
        /// <returns>The result produced by this codec helper.</returns>
        public static Slot F32(float v)
        {
            var b = new FloatArray.Builder();
            b.Append(v);
            return new(F(string.Empty, FloatType.Default, false), b.Build(Alloc));
        }

        /// <summary>
        /// Creates an Arrow slot containing one double-precision floating-point value.
        /// </summary>
        /// <param name = "v">The input required by this experimental codec helper.</param>
        /// <returns>The result produced by this codec helper.</returns>
        public static Slot F64(double v)
        {
            var b = new DoubleArray.Builder();
            b.Append(v);
            return new(F(string.Empty, DoubleType.Default, false), b.Build(Alloc));
        }

        /// <summary>
        /// Creates an Arrow slot containing one nullable UTF-8 string value.
        /// </summary>
        /// <param name = "v">The input required by this experimental codec helper.</param>
        /// <returns>The result produced by this codec helper.</returns>
        public static Slot Str(string? v)
        {
            var b = new StringArray.Builder();
            if (v == null)
            {
                b.AppendNull();
            }
            else
            {
                b.Append(v);
            }

            return new(F(string.Empty, StringType.Default), b.Build(Alloc));
        }

        /// <summary>
        /// Creates an Arrow slot containing one OPC UA DateTime value as ticks.
        /// </summary>
        /// <param name = "v">The input required by this experimental codec helper.</param>
        /// <returns>The result produced by this codec helper.</returns>
        public static Slot DateTime(DateTimeUtc v)
        {
            var b = new Int64Array.Builder();
            if (v.IsNull)
            {
                b.AppendNull();
            }
            else
            {
                b.Append(v.Value);
            }

            return new(F(string.Empty, Int64Type.Default), b.Build(Alloc));
        }

        /// <summary>
        /// Creates an Arrow slot containing one OPC UA StatusCode value.
        /// </summary>
        /// <param name = "v">The input required by this experimental codec helper.</param>
        /// <returns>The result produced by this codec helper.</returns>
        public static Slot Status(StatusCode v)
        {
            var b = new UInt32Array.Builder();
            b.Append(v.Code);
            return new(F(string.Empty, UInt32Type.Default, false), b.Build(Alloc));
        }

        /// <summary>
        /// Creates an Arrow slot containing one nullable byte-string value.
        /// </summary>
        /// <param name = "v">The input required by this experimental codec helper.</param>
        /// <returns>The result produced by this codec helper.</returns>
        public static Slot Bytes(ByteString v)
        {
            var b = new BinaryArray.Builder();
            if (v.IsNull)
            {
                b.AppendNull();
            }
            else
            {
                b.Append(v.Span);
            }

            return new(F(string.Empty, BinaryType.Default), b.Build(Alloc));
        }

        /// <summary>
        /// Creates an Arrow slot containing one OPC UA Guid value.
        /// </summary>
        /// <param name = "v">The input required by this experimental codec helper.</param>
        /// <returns>The result produced by this codec helper.</returns>
        public static Slot Guid(Uuid v)
        {
            return GuidMany(new[] { v });
        }

        /// <summary>
        /// Creates an Arrow fixed-size binary slot containing OPC UA Guid values.
        /// </summary>
        /// <param name = "v">The input required by this experimental codec helper.</param>
        /// <returns>The result produced by this codec helper.</returns>
        public static Slot GuidMany(ReadOnlyMemory<Uuid> v)
        {
            var bytes = new List<byte>();
            foreach (var x in v.Span)
            {
                bytes.AddRange(x.Guid.ToByteArray());
            }

#pragma warning disable CA2000 // Justification: the Arrow array is handed off to Slot and disposed with the RecordBatch.

            var a = new FixedSizeBinaryArray(
                new ArrayData(
                    new FixedSizeBinaryType(16),
                    v.Length,
                    0,
                    0,
                    new[] { V(v.Length, true), B(bytes.ToArray()) },
                    System.Array.Empty<ArrayData>()
                )
            );
            return new(F(string.Empty, new FixedSizeBinaryType(16)), a);
#pragma warning restore CA2000
        }

        /// <summary>
        /// Creates an Arrow struct slot for an OPC UA NodeId.
        /// </summary>
        /// <param name = "v">The input required by this experimental codec helper.</param>
        /// <returns>The result produced by this codec helper.</returns>
        public static Slot NodeId(NodeId v)
        {
            byte t = v.IsNull ? (byte)0 : (byte)v.IdType;
            uint n = !v.IsNull && v.TryGetValue(out uint numericIdentifier) ? numericIdentifier : 0;
            string? s = !v.IsNull && v.TryGetValue(out string stringIdentifier) ? stringIdentifier : null;
            Uuid g = !v.IsNull && v.TryGetValue(out Guid guidIdentifier) ? new Uuid(guidIdentifier) : default;
            ByteString o = !v.IsNull && v.TryGetValue(out ByteString opaqueIdentifier) ? opaqueIdentifier : default;
            return Struct(
                new() { U16(v.NamespaceIndex), U8(t), U32(n), Str(s), Guid(g), Bytes(o) },
                new() { "namespace", "id_type", "numeric", "string", "guid", "opaque" },
                !v.IsNull
            );
        }

        /// <summary>
        /// Creates an Arrow struct slot for an OPC UA ExpandedNodeId.
        /// </summary>
        /// <param name = "v">The input required by this experimental codec helper.</param>
        /// <returns>The result produced by this codec helper.</returns>
        public static Slot ExpandedNodeId(ExpandedNodeId v)
        {
            return Struct(
                new() { NodeId(v.InnerNodeId), Str(v.NamespaceUri), U32(v.ServerIndex) },
                new() { "node_id", "namespace_uri", "server_index" },
                !v.IsNull
            );
        }

        /// <summary>
        /// Creates an Arrow struct slot for an OPC UA QualifiedName.
        /// </summary>
        /// <param name = "v">The input required by this experimental codec helper.</param>
        /// <returns>The result produced by this codec helper.</returns>
        public static Slot QualifiedName(QualifiedName v)
        {
            return Struct(new() { U16(v.NamespaceIndex), Str(v.Name) }, new() { "namespace", "name" }, !v.IsNull);
        }

        /// <summary>
        /// Creates an Arrow struct slot for an OPC UA LocalizedText.
        /// </summary>
        /// <param name = "v">The input required by this experimental codec helper.</param>
        /// <returns>The result produced by this codec helper.</returns>
        public static Slot LocalizedText(LocalizedText v)
        {
            return Struct(new() { Str(v.Locale), Str(v.Text) }, new() { "locale", "text" }, !v.IsNull);
        }

        /// <summary>
        /// Creates an Arrow struct slot for an OPC UA DataValue.
        /// </summary>
        /// <param name = "v">The input required by this experimental codec helper.</param>
        /// <returns>The result produced by this codec helper.</returns>
        public static Slot DataValue(DataValue v)
        {
            return Struct(
                new()
                {
                    Variant(v.WrappedValue),
                    Status(v.StatusCode),
                    DateTime(v.SourceTimestamp),
                    U16(v.SourcePicoseconds),
                    DateTime(v.ServerTimestamp),
                    U16(v.ServerPicoseconds),
                },
                new()
                {
                    "value",
                    "status",
                    "source_timestamp",
                    "source_picoseconds",
                    "server_timestamp",
                    "server_picoseconds",
                },
                !v.IsNull
            );
        }

        /// <summary>
        /// Creates an Arrow struct slot for an OPC UA DiagnosticInfo.
        /// </summary>
        /// <param name = "v">The input required by this experimental codec helper.</param>
        /// <returns>The result produced by this codec helper.</returns>
        public static Slot Diagnostic(DiagnosticInfo? v)
        {
            return v == null
                ? Struct(
                    new() { I32(-1), I32(-1), I32(-1), I32(-1), Str(null), Status(StatusCodes.Good), Null() },
                    new()
                    {
                        "symbolic_id",
                        "namespace_uri",
                        "locale",
                        "localized_text",
                        "additional_info",
                        "inner_status_code",
                        "inner_diagnostic_info",
                    },
                    false
                )
                : Struct(
                    new()
                    {
                        I32(v.SymbolicId),
                        I32(v.NamespaceUri),
                        I32(v.Locale),
                        I32(v.LocalizedText),
                        Str(v.AdditionalInfo),
                        Status(v.InnerStatusCode),
                        Diagnostic(v.InnerDiagnosticInfo),
                    },
                    new()
                    {
                        "symbolic_id",
                        "namespace_uri",
                        "locale",
                        "localized_text",
                        "additional_info",
                        "inner_status_code",
                        "inner_diagnostic_info",
                    }
                );
        }

        /// <summary>
        /// Creates an Arrow struct slot for an OPC UA ExtensionObject.
        /// </summary>
        /// <param name = "v">The input required by this experimental codec helper.</param>
        /// <returns>The result produced by this codec helper.</returns>
        public static Slot Extension(ExtensionObject v)
        {
            ByteString body = default;
            if (v.TryGetAsBinary(out ByteString b))
            {
                body = b;
            }

            return Struct(
                new()
                {
                    ExpandedNodeId(v.TypeId),
                    Union(v.IsNull ? 0 : 1, new() { Null(), Bytes(body) }, new() { "null", "binary" }),
                },
                new() { "type_id", "body" },
                !v.IsNull
            );
        }

        /// <summary>
        /// Creates an Arrow slot containing a null value.
        /// </summary>
        /// <returns>The result produced by this codec helper.</returns>
        public static Slot Null()
        {
            var b = new NullArray.Builder();
            b.AppendNull();
            return new(F(string.Empty, NullType.Default), b.Build(Alloc));
        }

        /// <summary>
        /// Creates an Arrow struct slot from child slots and field names.
        /// </summary>
        /// <param name = "children">The child Arrow slots that make up the composite value.</param>
        /// <param name = "names">The field names for the child Arrow slots.</param>
        /// <param name = "valid">True when the composite Arrow value is not null.</param>
        /// <returns>The result produced by this codec helper.</returns>
        public static Slot Struct(List<Slot> children, List<string> names, bool valid = true)
        {
            var fields = children.Select((c, i) => c.Field(names[i])).ToList();
            var t = new StructType(fields);
#pragma warning disable CA2000 // Justification: the Arrow array is handed off to Slot and disposed with the RecordBatch.

            return new(
                F(string.Empty, t),
                new StructArray(t, 1, children.Select(c => c.Array), V(1, valid), valid ? 0 : 1, 0)
            );
#pragma warning restore CA2000
        }

        private static Slot StructMany(List<Slot> children, List<string> names, ReadOnlyMemory<bool> valid)
        {
            var fields = children.Select((c, i) => c.Field(names[i])).ToList();
            var t = new StructType(fields);
            int nullCount = valid.Span.ToArray().Count(v => !v);
#pragma warning disable CA2000 // Justification: the Arrow array is handed off to Slot and disposed with the RecordBatch.

            return new(
                F(string.Empty, t),
                new StructArray(t, valid.Length, children.Select(c => c.Array), V(valid.Span), nullCount, 0)
            );
#pragma warning restore CA2000
        }

        /// <summary>
        /// Creates a dense Arrow union slot with the selected child branch.
        /// </summary>
        /// <param name = "selected">The dense union branch selected for the Arrow value.</param>
        /// <param name = "children">The child Arrow slots that make up the composite value.</param>
        /// <param name = "names">The field names for the child Arrow slots.</param>
        /// <returns>The result produced by this codec helper.</returns>
        public static Slot Union(int selected, List<Slot> children, List<string> names)
        {
            var fields = children.Select((c, i) => c.Field(names[i])).ToList();
            // The dense-union type-ids buffer stores `selected`, so the declared union type-ids
            // must contain it or external Arrow readers (pyarrow/ADBC) cannot map a slot to its
            // child. When `selected` is a child index (e.g. the ExtensionObject body union) the
            // natural [0,1,..] mapping is correct; when it is an OPC BuiltInType code (Variant
            // union, whose value is always child 1) declare [0, selected].
            IEnumerable<int> typeIds =
                selected < fields.Count ? Enumerable.Range(0, fields.Count) : new[] { 0, selected };
            var t = new UnionType(fields, typeIds, UnionMode.Dense);
#pragma warning disable CA2000 // Justification: the Arrow array is handed off to Slot and disposed with the RecordBatch.

            return new(
                F(string.Empty, t),
                new DenseUnionArray(t, 1, children.Select(c => c.Array), B((byte)selected), B(0), 0, 0)
            );
#pragma warning restore CA2000
        }

        /// <summary>
        /// Creates an Arrow union slot for an OPC UA Variant.
        /// </summary>
        /// <param name = "v">The input required by this experimental codec helper.</param>
        /// <returns>The result produced by this codec helper.</returns>
        public static Slot Variant(Variant v)
        {
            if (v.IsNull)
            {
                return Union(0, new() { Null() }, new() { "null" });
            }

            BuiltInType type = v.TypeInfo.BuiltInType;
            if (!IsVariantBodyType(type))
            {
                throw new NotSupportedException($"Arrow Variant branch '{v.TypeInfo}' is not supported yet.");
            }

            if (v.TypeInfo.IsScalar)
            {
                return VariantBranch((int)type, ScalarVariantSlot(v, type), "scalar", type);
            }

            if (v.TypeInfo.IsArray)
            {
                return VariantBranch(VariantArrayCodeBase + (int)type, ArrayVariantSlot(v, type), "array", type);
            }

            if (v.TypeInfo.IsMatrix)
            {
                return VariantBranch(VariantMatrixCodeBase + (int)type, MatrixVariantSlot(v, type), "matrix", type);
            }

            throw new NotSupportedException($"Arrow Variant branch '{v.TypeInfo}' is not supported yet.");
        }

        private static bool IsVariantBodyType(BuiltInType type)
        {
            return (type >= BuiltInType.Boolean && type <= BuiltInType.ExtensionObject)
                || type == BuiltInType.Enumeration;
        }

        private static Slot VariantBranch(int selected, Slot value, string shape, BuiltInType type)
        {
            // Dense-union type ids are int8. Variant uses: null=0; scalar=built-in id
            // (1..22); array=40+built-in id (41..62); matrix=80+built-in id (81..102).
            return Union(
                selected,
                new() { Null(), value },
                new() { "null", string.Create(CultureInfo.InvariantCulture, $"{shape}_{type}") }
            );
        }

        private static Slot ScalarVariantSlot(Variant value, BuiltInType type)
        {
            return type switch
            {
                BuiltInType.Boolean => Bool(value.GetBoolean()),
                BuiltInType.SByte => I8(value.GetSByte()),
                BuiltInType.Byte => U8(value.GetByte()),
                BuiltInType.Int16 => I16(value.GetInt16()),
                BuiltInType.UInt16 => U16(value.GetUInt16()),
                BuiltInType.Int32 => I32(value.GetInt32()),
                BuiltInType.UInt32 => U32(value.GetUInt32()),
                BuiltInType.Int64 => I64(value.GetInt64()),
                BuiltInType.UInt64 => U64(value.GetUInt64()),
                BuiltInType.Float => F32(value.GetFloat()),
                BuiltInType.Double => F64(value.GetDouble()),
                BuiltInType.String => Str(value.GetString()),
                BuiltInType.DateTime => DateTime(value.GetDateTime()),
                BuiltInType.Guid => Guid(value.GetGuid()),
                BuiltInType.ByteString => Bytes(value.GetByteString()),
                BuiltInType.XmlElement => Str(value.GetXmlElement().OuterXml),
                BuiltInType.NodeId => NodeId(value.GetNodeId()),
                BuiltInType.ExpandedNodeId => ExpandedNodeId(value.GetExpandedNodeId()),
                BuiltInType.StatusCode => Status(value.GetStatusCode()),
                BuiltInType.QualifiedName => QualifiedName(value.GetQualifiedName()),
                BuiltInType.LocalizedText => LocalizedText(value.GetLocalizedText()),
                BuiltInType.ExtensionObject => Extension(value.GetExtensionObject()),
                BuiltInType.Enumeration => I32(value.GetEnumeration().Value),
                _ => throw new NotSupportedException($"Variant scalar {type} is not supported by the Arrow encoder."),
            };
        }

        private static Slot ArrayVariantSlot(Variant value, BuiltInType type)
        {
            return type switch
            {
                BuiltInType.Boolean => List(value.GetBooleanArray(), BoolMany),
                BuiltInType.SByte => List(value.GetSByteArray(), I8Many),
                BuiltInType.Byte => List(value.GetByteArray(), U8Many),
                BuiltInType.Int16 => List(value.GetInt16Array(), I16Many),
                BuiltInType.UInt16 => List(value.GetUInt16Array(), U16Many),
                BuiltInType.Int32 => List(value.GetInt32Array(), I32Many),
                BuiltInType.UInt32 => List(value.GetUInt32Array(), U32Many),
                BuiltInType.Int64 => List(value.GetInt64Array(), I64Many),
                BuiltInType.UInt64 => List(value.GetUInt64Array(), U64Many),
                BuiltInType.Float => List(value.GetFloatArray(), F32Many),
                BuiltInType.Double => List(value.GetDoubleArray(), F64Many),
                BuiltInType.String => List(value.GetStringArray(), StrMany),
                BuiltInType.DateTime => List(value.GetDateTimeArray(), DateTimeMany),
                BuiltInType.Guid => List(value.GetGuidArray(), GuidMany),
                BuiltInType.ByteString => List(value.GetByteStringArray(), BytesMany),
                BuiltInType.XmlElement => List(value.GetXmlElementArray(), XmlMany),
                BuiltInType.NodeId => List(value.GetNodeIdArray(), NodeIdManySlot),
                BuiltInType.ExpandedNodeId => List(value.GetExpandedNodeIdArray(), ExpandedNodeIdManySlot),
                BuiltInType.StatusCode => List(value.GetStatusCodeArray(), StatusManySlot),
                BuiltInType.QualifiedName => List(value.GetQualifiedNameArray(), QualifiedNameManySlot),
                BuiltInType.LocalizedText => List(value.GetLocalizedTextArray(), LocalizedTextManySlot),
                BuiltInType.ExtensionObject => List(value.GetExtensionObjectArray(), ExtensionManySlot),
                BuiltInType.Enumeration => List(value.GetEnumerationArray().ConvertAll(e => e.Value), I32Many),
                _ => throw new NotSupportedException($"Variant array {type} is not supported by the Arrow encoder."),
            };
        }

        private static Slot MatrixVariantSlot(Variant value, BuiltInType type)
        {
            return type switch
            {
                BuiltInType.Boolean => Matrix(value.GetBooleanMatrix(), BoolMany),
                BuiltInType.SByte => Matrix(value.GetSByteMatrix(), I8Many),
                BuiltInType.Byte => Matrix(value.GetByteMatrix(), U8Many),
                BuiltInType.Int16 => Matrix(value.GetInt16Matrix(), I16Many),
                BuiltInType.UInt16 => Matrix(value.GetUInt16Matrix(), U16Many),
                BuiltInType.Int32 => Matrix(value.GetInt32Matrix(), I32Many),
                BuiltInType.UInt32 => Matrix(value.GetUInt32Matrix(), U32Many),
                BuiltInType.Int64 => Matrix(value.GetInt64Matrix(), I64Many),
                BuiltInType.UInt64 => Matrix(value.GetUInt64Matrix(), U64Many),
                BuiltInType.Float => Matrix(value.GetFloatMatrix(), F32Many),
                BuiltInType.Double => Matrix(value.GetDoubleMatrix(), F64Many),
                BuiltInType.String => Matrix(value.GetStringMatrix(), StrMany),
                BuiltInType.DateTime => Matrix(value.GetDateTimeMatrix(), DateTimeMany),
                BuiltInType.Guid => Matrix(value.GetGuidMatrix(), GuidMany),
                BuiltInType.ByteString => Matrix(value.GetByteStringMatrix(), BytesMany),
                BuiltInType.XmlElement => Matrix(value.GetXmlElementMatrix(), XmlMany),
                BuiltInType.NodeId => Matrix(value.GetNodeIdMatrix(), NodeIdManySlot),
                BuiltInType.ExpandedNodeId => Matrix(value.GetExpandedNodeIdMatrix(), ExpandedNodeIdManySlot),
                BuiltInType.StatusCode => Matrix(value.GetStatusCodeMatrix(), StatusManySlot),
                BuiltInType.QualifiedName => Matrix(value.GetQualifiedNameMatrix(), QualifiedNameManySlot),
                BuiltInType.LocalizedText => Matrix(value.GetLocalizedTextMatrix(), LocalizedTextManySlot),
                BuiltInType.ExtensionObject => Matrix(value.GetExtensionObjectMatrix(), ExtensionManySlot),
                BuiltInType.Enumeration => Matrix(value.GetEnumerationMatrix().ConvertAll(e => e.Value), I32Many),
                _ => throw new NotSupportedException($"Variant matrix {type} is not supported by the Arrow encoder."),
            };
        }

        /// <summary>
        /// Creates an Arrow struct slot containing matrix dimensions and values.
        /// </summary>
        /// <typeparam name = "T">The OPC UA encodeable or value type processed by this member.</typeparam>
        /// <param name = "values">The values to encode or decode.</param>
        /// <param name = "elem">The delegate that builds a slot for list or matrix elements.</param>
        /// <returns>The result produced by this codec helper.</returns>
        public static Slot Matrix<T>(MatrixOf<T> values, Func<ReadOnlyMemory<T>, Slot> elem)
        {
            return Struct(
                new() { List(new ArrayOf<int>(values.Dimensions), I32Many), List(new ArrayOf<T>(values.Memory), elem) },
                new() { "dimensions", "values" },
                !values.IsNull
            );
        }

        /// <summary>
        /// Creates an Arrow list slot from an OPC UA array.
        /// </summary>
        /// <typeparam name = "T">The OPC UA encodeable or value type processed by this member.</typeparam>
        /// <param name = "values">The values to encode or decode.</param>
        /// <param name = "elem">The delegate that builds a slot for list or matrix elements.</param>
        /// <returns>The result produced by this codec helper.</returns>
        public static Slot List<T>(ArrayOf<T> values, Func<ReadOnlyMemory<T>, Slot> elem)
        {
            Slot child = elem(values.IsNull ? ReadOnlyMemory<T>.Empty : values.Memory);
            var t = new ListType(child.Field("item"));
#pragma warning disable CA2000 // Justification: the Arrow array is handed off to Slot and disposed with the RecordBatch.

            return new(
                F(string.Empty, t),
                new ListArray(
                    t,
                    1,
                    B(0, values.IsNull ? 0 : values.Count),
                    child.Array,
                    V(1, !values.IsNull),
                    values.IsNull ? 1 : 0,
                    0
                )
            );
#pragma warning restore CA2000
        }

        /// <summary>
        /// Creates an Arrow list slot for a single struct element.
        /// </summary>
        /// <typeparam name = "T">The OPC UA encodeable or value type processed by this member.</typeparam>
        /// <param name = "values">The values to encode or decode.</param>
        /// <param name = "elem">The delegate that builds a slot for list or matrix elements.</param>
        /// <returns>The result produced by this codec helper.</returns>
        public static Slot ListStruct<T>(ArrayOf<T> values, Func<T, Slot> elem)
        {
            return values.Count == 1
                ? List(values, s => elem(s.Span[0]))
                : throw new NotSupportedException("Struct lists currently support one element.");
        }

        /// <summary>
        /// Reads Boolean values from an Arrow array segment.
        /// </summary>
        /// <param name = "v">The input required by this experimental codec helper.</param>
        /// <returns>The result produced by this codec helper.</returns>
        public static Slot BoolMany(ReadOnlyMemory<bool> v)
        {
            var b = new BooleanArray.Builder();
            foreach (bool x in v.Span)
            {
                b.Append(x);
            }

            return new(F(string.Empty, BooleanType.Default), b.Build(Alloc));
        }

        /// <summary>
        /// Reads signed 8-bit integer values from an Arrow array segment.
        /// </summary>
        /// <param name = "v">The input required by this experimental codec helper.</param>
        /// <returns>The result produced by this codec helper.</returns>
        public static Slot I8Many(ReadOnlyMemory<sbyte> v)
        {
            var b = new Int8Array.Builder();
            foreach (sbyte x in v.Span)
            {
                b.Append(x);
            }

            return new(F(string.Empty, Int8Type.Default), b.Build(Alloc));
        }

        /// <summary>
        /// Reads unsigned 8-bit integer values from an Arrow array segment.
        /// </summary>
        /// <param name = "v">The input required by this experimental codec helper.</param>
        /// <returns>The result produced by this codec helper.</returns>
        public static Slot U8Many(ReadOnlyMemory<byte> v)
        {
            var b = new UInt8Array.Builder();
            foreach (byte x in v.Span)
            {
                b.Append(x);
            }

            return new(F(string.Empty, UInt8Type.Default), b.Build(Alloc));
        }

        /// <summary>
        /// Reads signed 16-bit integer values from an Arrow array segment.
        /// </summary>
        /// <param name = "v">The input required by this experimental codec helper.</param>
        /// <returns>The result produced by this codec helper.</returns>
        public static Slot I16Many(ReadOnlyMemory<short> v)
        {
            var b = new Int16Array.Builder();
            foreach (short x in v.Span)
            {
                b.Append(x);
            }

            return new(F(string.Empty, Int16Type.Default), b.Build(Alloc));
        }

        /// <summary>
        /// Reads unsigned 16-bit integer values from an Arrow array segment.
        /// </summary>
        /// <param name = "v">The input required by this experimental codec helper.</param>
        /// <returns>The result produced by this codec helper.</returns>
        public static Slot U16Many(ReadOnlyMemory<ushort> v)
        {
            var b = new UInt16Array.Builder();
            foreach (ushort x in v.Span)
            {
                b.Append(x);
            }

            return new(F(string.Empty, UInt16Type.Default), b.Build(Alloc));
        }

        /// <summary>
        /// Reads signed 32-bit integer values from an Arrow array segment.
        /// </summary>
        /// <param name = "v">The input required by this experimental codec helper.</param>
        /// <returns>The result produced by this codec helper.</returns>
        public static Slot I32Many(ReadOnlyMemory<int> v)
        {
            var b = new Int32Array.Builder();
            foreach (int x in v.Span)
            {
                b.Append(x);
            }

            return new(F(string.Empty, Int32Type.Default), b.Build(Alloc));
        }

        /// <summary>
        /// Reads unsigned 32-bit integer values from an Arrow array segment.
        /// </summary>
        /// <param name = "v">The input required by this experimental codec helper.</param>
        /// <returns>The result produced by this codec helper.</returns>
        public static Slot U32Many(ReadOnlyMemory<uint> v)
        {
            var b = new UInt32Array.Builder();
            foreach (uint x in v.Span)
            {
                b.Append(x);
            }

            return new(F(string.Empty, UInt32Type.Default), b.Build(Alloc));
        }

        /// <summary>
        /// Reads signed 64-bit integer values from an Arrow array segment.
        /// </summary>
        /// <param name = "v">The input required by this experimental codec helper.</param>
        /// <returns>The result produced by this codec helper.</returns>
        public static Slot I64Many(ReadOnlyMemory<long> v)
        {
            var b = new Int64Array.Builder();
            foreach (long x in v.Span)
            {
                b.Append(x);
            }

            return new(F(string.Empty, Int64Type.Default), b.Build(Alloc));
        }

        /// <summary>
        /// Reads unsigned 64-bit integer values from an Arrow array segment.
        /// </summary>
        /// <param name = "v">The input required by this experimental codec helper.</param>
        /// <returns>The result produced by this codec helper.</returns>
        public static Slot U64Many(ReadOnlyMemory<ulong> v)
        {
            var b = new UInt64Array.Builder();
            foreach (ulong x in v.Span)
            {
                b.Append(x);
            }

            return new(F(string.Empty, UInt64Type.Default), b.Build(Alloc));
        }

        /// <summary>
        /// Reads single-precision floating-point values from an Arrow array segment.
        /// </summary>
        /// <param name = "v">The input required by this experimental codec helper.</param>
        /// <returns>The result produced by this codec helper.</returns>
        public static Slot F32Many(ReadOnlyMemory<float> v)
        {
            var b = new FloatArray.Builder();
            foreach (float x in v.Span)
            {
                b.Append(x);
            }

            return new(F(string.Empty, FloatType.Default), b.Build(Alloc));
        }

        /// <summary>
        /// Reads double-precision floating-point values from an Arrow array segment.
        /// </summary>
        /// <param name = "v">The input required by this experimental codec helper.</param>
        /// <returns>The result produced by this codec helper.</returns>
        public static Slot F64Many(ReadOnlyMemory<double> v)
        {
            var b = new DoubleArray.Builder();
            foreach (double x in v.Span)
            {
                b.Append(x);
            }

            return new(F(string.Empty, DoubleType.Default), b.Build(Alloc));
        }

        /// <summary>
        /// Reads nullable UTF-8 string values from an Arrow array segment.
        /// </summary>
        /// <param name = "v">The input required by this experimental codec helper.</param>
        /// <returns>The result produced by this codec helper.</returns>
        public static Slot StrMany(ReadOnlyMemory<string> v)
        {
            var b = new StringArray.Builder();
            foreach (string x in v.Span)
            {
                if (x == null)
                {
                    b.AppendNull();
                }
                else
                {
                    b.Append(x);
                }
            }

            return new(F(string.Empty, StringType.Default), b.Build(Alloc));
        }

        /// <summary>
        /// Reads OPC UA DateTime values from an Arrow array segment.
        /// </summary>
        /// <param name = "v">The input required by this experimental codec helper.</param>
        /// <returns>The result produced by this codec helper.</returns>
        public static Slot DateTimeMany(ReadOnlyMemory<DateTimeUtc> v)
        {
            var b = new Int64Array.Builder();
            foreach (DateTimeUtc x in v.Span)
            {
                if (x.IsNull)
                {
                    b.AppendNull();
                }
                else
                {
                    b.Append(x.Value);
                }
            }

            return new(F(string.Empty, Int64Type.Default), b.Build(Alloc));
        }

        /// <summary>
        /// Reads OPC UA byte-string values from an Arrow array segment.
        /// </summary>
        /// <param name = "v">The input required by this experimental codec helper.</param>
        /// <returns>The result produced by this codec helper.</returns>
        public static Slot BytesMany(ReadOnlyMemory<ByteString> v)
        {
            var b = new BinaryArray.Builder();
            foreach (ByteString x in v.Span)
            {
                if (x.IsNull)
                {
                    b.AppendNull();
                }
                else
                {
                    b.Append(x.Span);
                }
            }

            return new(F(string.Empty, BinaryType.Default), b.Build(Alloc));
        }

        private static Slot XmlMany(ReadOnlyMemory<XmlElement> v)
        {
            string[] values = new string[v.Length];
            for (int ii = 0; ii < v.Length; ii++)
            {
                values[ii] = v.Span[ii].OuterXml!;
            }

            return StrMany(values);
        }

        private static Slot StatusManySlot(ReadOnlyMemory<StatusCode> v)
        {
            uint[] values = new uint[v.Length];
            for (int ii = 0; ii < v.Length; ii++)
            {
                values[ii] = v.Span[ii].Code;
            }

            return U32Many(values);
        }

        public static Slot NodeIdManySlot(ReadOnlyMemory<NodeId> v)
        {
            ushort[] namespaces = new ushort[v.Length];
            byte[] types = new byte[v.Length];
            uint[] numeric = new uint[v.Length];
            string[] strings = new string[v.Length];
            Uuid[] guids = new Uuid[v.Length];
            ByteString[] opaque = new ByteString[v.Length];
            bool[] valid = new bool[v.Length];
            for (int ii = 0; ii < v.Length; ii++)
            {
                NodeId x = v.Span[ii];
                valid[ii] = !x.IsNull;
                namespaces[ii] = x.NamespaceIndex;
                types[ii] = x.IsNull ? (byte)0 : (byte)x.IdType;
                numeric[ii] = !x.IsNull && x.TryGetValue(out uint numericIdentifier) ? numericIdentifier : 0;
                strings[ii] = !x.IsNull && x.TryGetValue(out string stringIdentifier) ? stringIdentifier : null!;
                guids[ii] = !x.IsNull && x.TryGetValue(out Guid guidIdentifier) ? new Uuid(guidIdentifier) : default;
                opaque[ii] = !x.IsNull && x.TryGetValue(out ByteString opaqueIdentifier) ? opaqueIdentifier : default;
            }

            return StructMany(
                new() { U16Many(namespaces), U8Many(types), U32Many(numeric), StrMany(strings), GuidMany(guids), BytesMany(opaque) },
                new() { "namespace", "id_type", "numeric", "string", "guid", "opaque" },
                valid
            );
        }

        public static Slot ExpandedNodeIdManySlot(ReadOnlyMemory<ExpandedNodeId> v)
        {
            NodeId[] nodeIds = new NodeId[v.Length];
            string[] namespaceUris = new string[v.Length];
            uint[] serverIndexes = new uint[v.Length];
            bool[] valid = new bool[v.Length];
            for (int ii = 0; ii < v.Length; ii++)
            {
                ExpandedNodeId x = v.Span[ii];
                valid[ii] = !x.IsNull;
                nodeIds[ii] = x.InnerNodeId;
                namespaceUris[ii] = x.NamespaceUri!;
                serverIndexes[ii] = x.ServerIndex;
            }

            return StructMany(
                new() { NodeIdManySlot(nodeIds), StrMany(namespaceUris), U32Many(serverIndexes) },
                new() { "node_id", "namespace_uri", "server_index" },
                valid
            );
        }

        public static Slot QualifiedNameManySlot(ReadOnlyMemory<QualifiedName> v)
        {
            ushort[] namespaces = new ushort[v.Length];
            string[] names = new string[v.Length];
            bool[] valid = new bool[v.Length];
            for (int ii = 0; ii < v.Length; ii++)
            {
                QualifiedName x = v.Span[ii];
                valid[ii] = !x.IsNull;
                namespaces[ii] = x.NamespaceIndex;
                names[ii] = x.Name!;
            }

            return StructMany(new() { U16Many(namespaces), StrMany(names) }, new() { "namespace", "name" }, valid);
        }

        public static Slot LocalizedTextManySlot(ReadOnlyMemory<LocalizedText> v)
        {
            string[] locales = new string[v.Length];
            string[] texts = new string[v.Length];
            bool[] valid = new bool[v.Length];
            for (int ii = 0; ii < v.Length; ii++)
            {
                LocalizedText x = v.Span[ii];
                valid[ii] = !x.IsNull;
                locales[ii] = x.Locale!;
                texts[ii] = x.Text!;
            }

            return StructMany(new() { StrMany(locales), StrMany(texts) }, new() { "locale", "text" }, valid);
        }

        public static Slot ExtensionManySlot(ReadOnlyMemory<ExtensionObject> v)
        {
            ExpandedNodeId[] typeIds = new ExpandedNodeId[v.Length];
            byte[] bodyTypeIds = new byte[v.Length];
            int[] bodyOffsets = new int[v.Length];
            var binaryBodies = new BinaryArray.Builder();
            int nullOffset = 0;
            int binaryOffset = 0;
            bool[] valid = new bool[v.Length];
            for (int ii = 0; ii < v.Length; ii++)
            {
                ExtensionObject x = v.Span[ii];
                valid[ii] = !x.IsNull;
                typeIds[ii] = x.TypeId;
                if (!x.IsNull && x.TryGetAsBinary(out ByteString body))
                {
                    bodyTypeIds[ii] = 1;
                    bodyOffsets[ii] = binaryOffset++;
                    binaryBodies.Append(body.Span);
                }
                else
                {
                    bodyTypeIds[ii] = 0;
                    bodyOffsets[ii] = nullOffset++;
                }
            }

            var fields = new[] { Null().Field("null"), F("binary", BinaryType.Default) };
            var unionType = new UnionType(fields, DenseBinaryUnionTypeIds, UnionMode.Dense);
#pragma warning disable CA2000 // Justification: the Arrow array is handed off to Slot and disposed with the RecordBatch.

            var bodyUnion = new Slot(
                F(string.Empty, unionType),
                new DenseUnionArray(
                    unionType,
                    v.Length,
                    new IArrowArray[] { new NullArray(nullOffset), binaryBodies.Build(Alloc) },
                    B(bodyTypeIds),
                    B(bodyOffsets),
                    0,
                    0
                )
            );
#pragma warning restore CA2000

            return StructMany(new() { ExpandedNodeIdManySlot(typeIds), bodyUnion }, new() { "type_id", "body" }, valid);
        }

        /// <inheritdoc/>
        public static Uuid ReadGuid(IArrowArray a, int i)
        {
            return new(((FixedSizeBinaryArray)a).GetBytes(i).ToArray());
        }

        /// <summary>
        /// Reads one OPC UA byte string from an Arrow binary array.
        /// </summary>
        /// <param name = "a">The Arrow array to read.</param>
        /// <param name = "i">The row or value index to read.</param>
        /// <returns>The result produced by this codec helper.</returns>
        public static ByteString ReadBytes(IArrowArray a, int i)
        {
            return ((BinaryArray)a).IsNull(i) ? default : ByteString.From(((BinaryArray)a).GetBytes(i).ToArray());
        }

        /// <inheritdoc/>
        public static Opc.Ua.NodeId ReadNodeId(IArrowArray a, int i)
        {
            var s = (StructArray)a;
            if (s.IsNull(i))
            {
                return Opc.Ua.NodeId.Null;
            }

            ushort ns = ((UInt16Array)s.Fields[0]).GetValue(i) ?? 0;
            byte t = ((UInt8Array)s.Fields[1]).GetValue(i) ?? 0;
            return (IdType)t switch
            {
                IdType.String => new Opc.Ua.NodeId(((StringArray)s.Fields[3]).GetString(i), ns),
                IdType.Guid => new Opc.Ua.NodeId(ReadGuid(s.Fields[4], i).Guid, ns),
                IdType.Opaque => new Opc.Ua.NodeId(ReadBytes(s.Fields[5], i), ns),
                _ => new Opc.Ua.NodeId(((UInt32Array)s.Fields[2]).GetValue(i) ?? 0, ns),
            };
        }

        /// <inheritdoc/>
        public static ExpandedNodeId ReadExpandedNodeId(IArrowArray a, int i)
        {
            var s = (StructArray)a;
            return new ExpandedNodeId(
                ReadNodeId(s.Fields[0], i),
                ((StringArray)s.Fields[1]).IsNull(i) ? null : ((StringArray)s.Fields[1]).GetString(i),
                ((UInt32Array)s.Fields[2]).GetValue(i) ?? 0
            );
        }

        /// <inheritdoc/>
        public static QualifiedName ReadQualifiedName(IArrowArray a, int i)
        {
            var s = (StructArray)a;
            return new QualifiedName(
                ((StringArray)s.Fields[1]).IsNull(i) ? null : ((StringArray)s.Fields[1]).GetString(i),
                ((UInt16Array)s.Fields[0]).GetValue(i) ?? 0
            );
        }

        /// <inheritdoc/>
        public static LocalizedText ReadLocalizedText(IArrowArray a, int i)
        {
            var s = (StructArray)a;
            return new LocalizedText(
                ((StringArray)s.Fields[0]).IsNull(i) ? null : ((StringArray)s.Fields[0]).GetString(i),
                ((StringArray)s.Fields[1]).IsNull(i) ? null : ((StringArray)s.Fields[1]).GetString(i)
            );
        }

        /// <inheritdoc/>
        public static DataValue ReadDataValue(IArrowArray a, int i)
        {
            var s = (StructArray)a;
            return new DataValue(
                ReadVariant(s.Fields[0], i),
                new StatusCode(((UInt32Array)s.Fields[1]).GetValue(i) ?? 0),
                new DateTimeUtc(((Int64Array)s.Fields[2]).GetValue(i) ?? 0),
                new DateTimeUtc(((Int64Array)s.Fields[4]).GetValue(i) ?? 0),
                ((UInt16Array)s.Fields[3]).GetValue(i) ?? 0,
                ((UInt16Array)s.Fields[5]).GetValue(i) ?? 0
            );
        }

        /// <summary>
        /// Reads one OPC UA DiagnosticInfo from an Arrow struct array.
        /// </summary>
        /// <param name = "a">The Arrow array to read.</param>
        /// <param name = "i">The row or value index to read.</param>
        /// <returns>The result produced by this codec helper.</returns>
        public static DiagnosticInfo? ReadDiagnostic(IArrowArray a, int i)
        {
            var s = (StructArray)a;
            if (s.IsNull(i))
            {
                return null;
            }

            return new DiagnosticInfo
            {
                SymbolicId = ((Int32Array)s.Fields[0]).GetValue(i) ?? -1,
                NamespaceUri = ((Int32Array)s.Fields[1]).GetValue(i) ?? -1,
                Locale = ((Int32Array)s.Fields[2]).GetValue(i) ?? -1,
                LocalizedText = ((Int32Array)s.Fields[3]).GetValue(i) ?? -1,
                AdditionalInfo = ((StringArray)s.Fields[4]).IsNull(i) ? null : ((StringArray)s.Fields[4]).GetString(i),
                InnerStatusCode = new StatusCode(((UInt32Array)s.Fields[5]).GetValue(i) ?? 0),
                InnerDiagnosticInfo = ReadDiagnostic(s.Fields[6], i),
            };
        }

        /// <summary>
        /// Reads one OPC UA ExtensionObject from an Arrow struct array.
        /// </summary>
        /// <param name = "a">The Arrow array to read.</param>
        /// <param name = "i">The row or value index to read.</param>
        /// <returns>The result produced by this codec helper.</returns>
        public static ExtensionObject ReadExtension(IArrowArray a, int i)
        {
            var s = (StructArray)a;
            if (s.IsNull(i))
            {
                return ExtensionObject.Null;
            }

            var u = (DenseUnionArray)s.Fields[1];
            return u.TypeIds[i] == 1
                ? new ExtensionObject(ReadExpandedNodeId(s.Fields[0], i), ReadBytes(u.Fields[1], u.ValueOffsets[i]))
                : new ExtensionObject(ReadExpandedNodeId(s.Fields[0], i));
        }

        /// <inheritdoc/>
        public static Opc.Ua.Variant ReadVariant(IArrowArray a, int i)
        {
            var u = (DenseUnionArray)a;
            int code = u.TypeIds[i];
            int off = u.ValueOffsets[i];
            if (code == 0)
            {
                return Opc.Ua.Variant.Null;
            }

            if ((code >= (int)BuiltInType.Boolean && code <= (int)BuiltInType.ExtensionObject)
                || code == (int)BuiltInType.Enumeration)
            {
                return ReadScalarVariant((BuiltInType)code, u.Fields[1], off);
            }

            if ((code > VariantArrayCodeBase && code <= VariantArrayCodeBase + (int)BuiltInType.ExtensionObject)
                || code == VariantArrayCodeBase + (int)BuiltInType.Enumeration)
            {
                return ReadArrayVariant((BuiltInType)(code - VariantArrayCodeBase), u.Fields[1]);
            }

            if ((code > VariantMatrixCodeBase && code <= VariantMatrixCodeBase + (int)BuiltInType.ExtensionObject)
                || code == VariantMatrixCodeBase + (int)BuiltInType.Enumeration)
            {
                return ReadMatrixVariant((BuiltInType)(code - VariantMatrixCodeBase), u.Fields[1], off);
            }

            throw new NotSupportedException($"Unknown Variant union code {code}.");
        }

        private static Opc.Ua.Variant ReadScalarVariant(BuiltInType type, IArrowArray a, int i)
        {
            return type switch
            {
                BuiltInType.Boolean => new Opc.Ua.Variant(((BooleanArray)a).GetValue(i) ?? false),
                BuiltInType.SByte => new Opc.Ua.Variant(((Int8Array)a).GetValue(i) ?? 0),
                BuiltInType.Byte => new Opc.Ua.Variant(((UInt8Array)a).GetValue(i) ?? 0),
                BuiltInType.Int16 => new Opc.Ua.Variant(((Int16Array)a).GetValue(i) ?? 0),
                BuiltInType.UInt16 => new Opc.Ua.Variant(((UInt16Array)a).GetValue(i) ?? 0),
                BuiltInType.Int32 => new Opc.Ua.Variant(((Int32Array)a).GetValue(i) ?? 0),
                BuiltInType.UInt32 => new Opc.Ua.Variant(((UInt32Array)a).GetValue(i) ?? 0),
                BuiltInType.Int64 => new Opc.Ua.Variant(((Int64Array)a).GetValue(i) ?? 0),
                BuiltInType.UInt64 => new Opc.Ua.Variant(((UInt64Array)a).GetValue(i) ?? 0),
                BuiltInType.Float => new Opc.Ua.Variant(((FloatArray)a).GetValue(i) ?? 0),
                BuiltInType.Double => new Opc.Ua.Variant(((DoubleArray)a).GetValue(i) ?? 0),
                BuiltInType.String => new Opc.Ua.Variant(((StringArray)a).GetString(i)),
                BuiltInType.DateTime => new Opc.Ua.Variant(((Int64Array)a).IsNull(i) ? default : new DateTimeUtc(((Int64Array)a).GetValue(i) ?? 0)),
                BuiltInType.Guid => new Opc.Ua.Variant(ReadGuid(a, i)),
                BuiltInType.ByteString => new Opc.Ua.Variant(ReadBytes(a, i)),
                BuiltInType.XmlElement => new Opc.Ua.Variant(((StringArray)a).IsNull(i) ? default : (XmlElement)((StringArray)a).GetString(i)),
                BuiltInType.NodeId => new Opc.Ua.Variant(ReadNodeId(a, i)),
                BuiltInType.ExpandedNodeId => new Opc.Ua.Variant(ReadExpandedNodeId(a, i)),
                BuiltInType.StatusCode => new Opc.Ua.Variant(new StatusCode(((UInt32Array)a).GetValue(i) ?? 0)),
                BuiltInType.QualifiedName => new Opc.Ua.Variant(ReadQualifiedName(a, i)),
                BuiltInType.LocalizedText => new Opc.Ua.Variant(ReadLocalizedText(a, i)),
                BuiltInType.ExtensionObject => new Opc.Ua.Variant(ReadExtension(a, i)),
                BuiltInType.Enumeration => Opc.Ua.Variant.From(new EnumValue(((Int32Array)a).GetValue(i) ?? 0)),
                _ => throw new NotSupportedException($"Variant scalar {type} is not supported by the Arrow decoder."),
            };
        }

        private static Opc.Ua.Variant ReadArrayVariant(BuiltInType type, IArrowArray a)
        {
            return type switch
            {
                BuiltInType.Boolean => new Opc.Ua.Variant(ReadList((null!, a), ReadBoolMany)),
                BuiltInType.SByte => new Opc.Ua.Variant(ReadList((null!, a), ReadI8Many)),
                BuiltInType.Byte => new Opc.Ua.Variant(ReadList((null!, a), ReadU8Many)),
                BuiltInType.Int16 => new Opc.Ua.Variant(ReadList((null!, a), ReadI16Many)),
                BuiltInType.UInt16 => new Opc.Ua.Variant(ReadList((null!, a), ReadU16Many)),
                BuiltInType.Int32 => new Opc.Ua.Variant(ReadList((null!, a), ReadI32Many)),
                BuiltInType.UInt32 => new Opc.Ua.Variant(ReadList((null!, a), ReadU32Many)),
                BuiltInType.Int64 => new Opc.Ua.Variant(ReadList((null!, a), ReadI64Many)),
                BuiltInType.UInt64 => new Opc.Ua.Variant(ReadList((null!, a), ReadU64Many)),
                BuiltInType.Float => new Opc.Ua.Variant(ReadList((null!, a), ReadF32Many)),
                BuiltInType.Double => new Opc.Ua.Variant(ReadList((null!, a), ReadF64Many)),
                BuiltInType.String => new Opc.Ua.Variant(ReadList((null!, a), ReadStrMany).ConvertAll(value => value!)),
                BuiltInType.DateTime => new Opc.Ua.Variant(ReadList((null!, a), ReadDateTimeMany)),
                BuiltInType.Guid => new Opc.Ua.Variant(ReadList((null!, a), ReadGuidMany)),
                BuiltInType.ByteString => new Opc.Ua.Variant(ReadList((null!, a), ReadBytesMany)),
                BuiltInType.XmlElement => new Opc.Ua.Variant(
                    ReadList((null!, a), ReadStrMany).ConvertAll(value => value == null ? default : (XmlElement)value)
                ),
                BuiltInType.NodeId => new Opc.Ua.Variant(ReadList((null!, a), ReadNodeIdMany)),
                BuiltInType.ExpandedNodeId => new Opc.Ua.Variant(ReadList((null!, a), ReadExpandedNodeIdMany)),
                BuiltInType.StatusCode => new Opc.Ua.Variant(ReadList((null!, a), ReadStatusMany)),
                BuiltInType.QualifiedName => new Opc.Ua.Variant(ReadList((null!, a), ReadQualifiedNameMany)),
                BuiltInType.LocalizedText => new Opc.Ua.Variant(ReadList((null!, a), ReadLocalizedTextMany)),
                BuiltInType.ExtensionObject => new Opc.Ua.Variant(ReadList((null!, a), ReadExtensionMany)),
                BuiltInType.Enumeration => Opc.Ua.Variant.From(
                    ReadList((null!, a), ReadI32Many).ConvertAll(x => new EnumValue(x))),
                _ => throw new NotSupportedException($"Variant array {type} is not supported by the Arrow decoder."),
            };
        }

        private static Opc.Ua.Variant ReadMatrixVariant(BuiltInType type, IArrowArray a, int i)
        {
            return type switch
            {
                BuiltInType.Boolean => new Opc.Ua.Variant(ReadMatrix(a, i, ReadBoolMany)),
                BuiltInType.SByte => new Opc.Ua.Variant(ReadMatrix(a, i, ReadI8Many)),
                BuiltInType.Byte => new Opc.Ua.Variant(ReadMatrix(a, i, ReadU8Many)),
                BuiltInType.Int16 => new Opc.Ua.Variant(ReadMatrix(a, i, ReadI16Many)),
                BuiltInType.UInt16 => new Opc.Ua.Variant(ReadMatrix(a, i, ReadU16Many)),
                BuiltInType.Int32 => new Opc.Ua.Variant(ReadMatrix(a, i, ReadI32Many)),
                BuiltInType.UInt32 => new Opc.Ua.Variant(ReadMatrix(a, i, ReadU32Many)),
                BuiltInType.Int64 => new Opc.Ua.Variant(ReadMatrix(a, i, ReadI64Many)),
                BuiltInType.UInt64 => new Opc.Ua.Variant(ReadMatrix(a, i, ReadU64Many)),
                BuiltInType.Float => new Opc.Ua.Variant(ReadMatrix(a, i, ReadF32Many)),
                BuiltInType.Double => new Opc.Ua.Variant(ReadMatrix(a, i, ReadF64Many)),
                BuiltInType.String => new Opc.Ua.Variant(ReadMatrix(a, i, ReadStrMany).ConvertAll(value => value!)),
                BuiltInType.DateTime => new Opc.Ua.Variant(ReadMatrix(a, i, ReadDateTimeMany)),
                BuiltInType.Guid => new Opc.Ua.Variant(ReadMatrix(a, i, ReadGuidMany)),
                BuiltInType.ByteString => new Opc.Ua.Variant(ReadMatrix(a, i, ReadBytesMany)),
                BuiltInType.XmlElement => new Opc.Ua.Variant(
                    ReadMatrix(a, i, ReadStrMany).ConvertAll(value => value == null ? default : (XmlElement)value)
                ),
                BuiltInType.NodeId => new Opc.Ua.Variant(ReadMatrix(a, i, ReadNodeIdMany)),
                BuiltInType.ExpandedNodeId => new Opc.Ua.Variant(ReadMatrix(a, i, ReadExpandedNodeIdMany)),
                BuiltInType.StatusCode => new Opc.Ua.Variant(ReadMatrix(a, i, ReadStatusMany)),
                BuiltInType.QualifiedName => new Opc.Ua.Variant(ReadMatrix(a, i, ReadQualifiedNameMany)),
                BuiltInType.LocalizedText => new Opc.Ua.Variant(ReadMatrix(a, i, ReadLocalizedTextMany)),
                BuiltInType.ExtensionObject => new Opc.Ua.Variant(ReadMatrix(a, i, ReadExtensionMany)),
                BuiltInType.Enumeration => Opc.Ua.Variant.From(
                    ReadMatrix(a, i, ReadI32Many).ConvertAll(x => new EnumValue(x))),
                _ => throw new NotSupportedException($"Variant matrix {type} is not supported by the Arrow decoder."),
            };
        }

        public static MatrixOf<T> ReadMatrix<T>(IArrowArray a, int i, Func<IArrowArray, int, int, T[]> read)
        {
            var s = (StructArray)a;
            if (s.IsNull(i))
            {
                return default;
            }

            var dims = ReadListAt((null!, s.Fields[0]), i, ReadI32Many);
            var vals = ReadListAt((null!, s.Fields[1]), i, read);
            return vals.ToMatrix(dims);
        }

        /// <summary>
        /// Reads an OPC UA array from an Arrow list array.
        /// </summary>
        /// <typeparam name = "T">The OPC UA encodeable or value type processed by this member.</typeparam>
        /// <param name = "c">The Arrow field and array pair to read.</param>
        /// <param name = "read">The delegate that reads the list value range.</param>
        /// <returns>The decoded OPC UA array.</returns>
        public static ArrayOf<T> ReadList<T>((Field Field, IArrowArray Array) c, Func<IArrowArray, int, int, T[]> read)
        {
            return ReadListAt(c, 0, read);
        }

        private static ArrayOf<T> ReadListAt<T>((Field Field, IArrowArray Array) c, int index, Func<IArrowArray, int, int, T[]> read)
        {
            var l = (ListArray)c.Array;
            if (l.IsNull(index))
            {
                return ArrayOf<T>.Null;
            }

            return new ArrayOf<T>(read(l.Values, l.ValueOffsets[index], l.ValueOffsets[index + 1] - l.ValueOffsets[index]));
        }

        /// <summary>
        /// Reads BoolMany from the experimental encoded representation.
        /// </summary>
        /// <param name = "a">The Arrow array to read.</param>
        /// <param name = "s">The first value index in the Arrow segment.</param>
        /// <param name = "n">The number of values in the Arrow segment.</param>
        /// <returns>The result produced by this codec helper.</returns>
        public static bool[] ReadBoolMany(IArrowArray a, int s, int n)
        {
            return Enumerable.Range(s, n).Select(i => ((BooleanArray)a).GetValue(i) ?? false).ToArray();
        }

        /// <summary>
        /// Reads I8Many from the experimental encoded representation.
        /// </summary>
        /// <param name = "a">The Arrow array to read.</param>
        /// <param name = "s">The first value index in the Arrow segment.</param>
        /// <param name = "n">The number of values in the Arrow segment.</param>
        /// <returns>The result produced by this codec helper.</returns>
        public static sbyte[] ReadI8Many(IArrowArray a, int s, int n)
        {
            return Enumerable.Range(s, n).Select(i => ((Int8Array)a).GetValue(i) ?? 0).ToArray();
        }

        /// <summary>
        /// Reads U8Many from the experimental encoded representation.
        /// </summary>
        /// <param name = "a">The Arrow array to read.</param>
        /// <param name = "s">The first value index in the Arrow segment.</param>
        /// <param name = "n">The number of values in the Arrow segment.</param>
        /// <returns>The result produced by this codec helper.</returns>
        public static byte[] ReadU8Many(IArrowArray a, int s, int n)
        {
            return Enumerable.Range(s, n).Select(i => ((UInt8Array)a).GetValue(i) ?? 0).ToArray();
        }

        /// <summary>
        /// Reads I16Many from the experimental encoded representation.
        /// </summary>
        /// <param name = "a">The Arrow array to read.</param>
        /// <param name = "s">The first value index in the Arrow segment.</param>
        /// <param name = "n">The number of values in the Arrow segment.</param>
        /// <returns>The result produced by this codec helper.</returns>
        public static short[] ReadI16Many(IArrowArray a, int s, int n)
        {
            return Enumerable.Range(s, n).Select(i => ((Int16Array)a).GetValue(i) ?? 0).ToArray();
        }

        /// <summary>
        /// Reads U16Many from the experimental encoded representation.
        /// </summary>
        /// <param name = "a">The Arrow array to read.</param>
        /// <param name = "s">The first value index in the Arrow segment.</param>
        /// <param name = "n">The number of values in the Arrow segment.</param>
        /// <returns>The result produced by this codec helper.</returns>
        public static ushort[] ReadU16Many(IArrowArray a, int s, int n)
        {
            return Enumerable.Range(s, n).Select(i => ((UInt16Array)a).GetValue(i) ?? 0).ToArray();
        }

        /// <summary>
        /// Reads I32Many from the experimental encoded representation.
        /// </summary>
        /// <param name = "a">The Arrow array to read.</param>
        /// <param name = "s">The first value index in the Arrow segment.</param>
        /// <param name = "n">The number of values in the Arrow segment.</param>
        /// <returns>The result produced by this codec helper.</returns>
        public static int[] ReadI32Many(IArrowArray a, int s, int n)
        {
            return Enumerable.Range(s, n).Select(i => ((Int32Array)a).GetValue(i) ?? 0).ToArray();
        }

        /// <summary>
        /// Reads U32Many from the experimental encoded representation.
        /// </summary>
        /// <param name = "a">The Arrow array to read.</param>
        /// <param name = "s">The first value index in the Arrow segment.</param>
        /// <param name = "n">The number of values in the Arrow segment.</param>
        /// <returns>The result produced by this codec helper.</returns>
        public static uint[] ReadU32Many(IArrowArray a, int s, int n)
        {
            return Enumerable.Range(s, n).Select(i => ((UInt32Array)a).GetValue(i) ?? 0).ToArray();
        }

        /// <summary>
        /// Reads I64Many from the experimental encoded representation.
        /// </summary>
        /// <param name = "a">The Arrow array to read.</param>
        /// <param name = "s">The first value index in the Arrow segment.</param>
        /// <param name = "n">The number of values in the Arrow segment.</param>
        /// <returns>The result produced by this codec helper.</returns>
        public static long[] ReadI64Many(IArrowArray a, int s, int n)
        {
            return Enumerable.Range(s, n).Select(i => ((Int64Array)a).GetValue(i) ?? 0).ToArray();
        }

        /// <summary>
        /// Reads U64Many from the experimental encoded representation.
        /// </summary>
        /// <param name = "a">The Arrow array to read.</param>
        /// <param name = "s">The first value index in the Arrow segment.</param>
        /// <param name = "n">The number of values in the Arrow segment.</param>
        /// <returns>The result produced by this codec helper.</returns>
        public static ulong[] ReadU64Many(IArrowArray a, int s, int n)
        {
            return Enumerable.Range(s, n).Select(i => ((UInt64Array)a).GetValue(i) ?? 0).ToArray();
        }

        /// <summary>
        /// Reads F32Many from the experimental encoded representation.
        /// </summary>
        /// <param name = "a">The Arrow array to read.</param>
        /// <param name = "s">The first value index in the Arrow segment.</param>
        /// <param name = "n">The number of values in the Arrow segment.</param>
        /// <returns>The result produced by this codec helper.</returns>
        public static float[] ReadF32Many(IArrowArray a, int s, int n)
        {
            return Enumerable.Range(s, n).Select(i => ((FloatArray)a).GetValue(i) ?? 0).ToArray();
        }

        /// <summary>
        /// Reads F64Many from the experimental encoded representation.
        /// </summary>
        /// <param name = "a">The Arrow array to read.</param>
        /// <param name = "s">The first value index in the Arrow segment.</param>
        /// <param name = "n">The number of values in the Arrow segment.</param>
        /// <returns>The result produced by this codec helper.</returns>
        public static double[] ReadF64Many(IArrowArray a, int s, int n)
        {
            return Enumerable.Range(s, n).Select(i => ((DoubleArray)a).GetValue(i) ?? 0).ToArray();
        }

        /// <summary>
        /// Reads StrMany from the experimental encoded representation.
        /// </summary>
        /// <param name = "a">The Arrow array to read.</param>
        /// <param name = "s">The first value index in the Arrow segment.</param>
        /// <param name = "n">The number of values in the Arrow segment.</param>
        /// <returns>The result produced by this codec helper.</returns>
        public static string?[] ReadStrMany(IArrowArray a, int s, int n)
        {
            return Enumerable
                .Range(s, n)
                .Select(i => ((StringArray)a).IsNull(i) ? null : ((StringArray)a).GetString(i))
                .ToArray();
        }

        /// <summary>
        /// Reads DateTimeMany from the experimental encoded representation.
        /// </summary>
        /// <param name = "a">The Arrow array to read.</param>
        /// <param name = "s">The first value index in the Arrow segment.</param>
        /// <param name = "n">The number of values in the Arrow segment.</param>
        /// <returns>The result produced by this codec helper.</returns>
        public static DateTimeUtc[] ReadDateTimeMany(IArrowArray a, int s, int n)
        {
            return Enumerable
                .Range(s, n)
                .Select(i => ((Int64Array)a).IsNull(i) ? default : new DateTimeUtc(((Int64Array)a).GetValue(i) ?? 0))
                .ToArray();
        }

        /// <summary>
        /// Reads GuidMany from the experimental encoded representation.
        /// </summary>
        /// <param name = "a">The Arrow array to read.</param>
        /// <param name = "s">The first value index in the Arrow segment.</param>
        /// <param name = "n">The number of values in the Arrow segment.</param>
        /// <returns>The result produced by this codec helper.</returns>
        public static Uuid[] ReadGuidMany(IArrowArray a, int s, int n)
        {
            return Enumerable.Range(s, n).Select(i => ReadGuid(a, i)).ToArray();
        }

        /// <summary>
        /// Reads BytesMany from the experimental encoded representation.
        /// </summary>
        /// <param name = "a">The Arrow array to read.</param>
        /// <param name = "s">The first value index in the Arrow segment.</param>
        /// <param name = "n">The number of values in the Arrow segment.</param>
        /// <returns>The result produced by this codec helper.</returns>
        public static ByteString[] ReadBytesMany(IArrowArray a, int s, int n)
        {
            return Enumerable.Range(s, n).Select(i => ReadBytes(a, i)).ToArray();
        }

        /// <summary>
        /// Reads NodeIdMany from the experimental encoded representation.
        /// </summary>
        /// <param name = "a">The Arrow array to read.</param>
        /// <param name = "s">The first value index in the Arrow segment.</param>
        /// <param name = "n">The number of values in the Arrow segment.</param>
        /// <returns>The result produced by this codec helper.</returns>
        public static NodeId[] ReadNodeIdMany(IArrowArray a, int s, int n)
        {
            return Enumerable.Range(s, n).Select(i => ReadNodeId(a, i)).ToArray();
        }

        /// <summary>
        /// Reads ExpandedNodeIdMany from the experimental encoded representation.
        /// </summary>
        /// <param name = "a">The Arrow array to read.</param>
        /// <param name = "s">The first value index in the Arrow segment.</param>
        /// <param name = "n">The number of values in the Arrow segment.</param>
        /// <returns>The result produced by this codec helper.</returns>
        public static ExpandedNodeId[] ReadExpandedNodeIdMany(IArrowArray a, int s, int n)
        {
            return Enumerable.Range(s, n).Select(i => ReadExpandedNodeId(a, i)).ToArray();
        }

        /// <summary>
        /// Reads OPC UA StatusCode values from an Arrow array segment.
        /// </summary>
        /// <param name = "a">The Arrow array to read.</param>
        /// <param name = "s">The first value index in the Arrow segment.</param>
        /// <param name = "n">The number of values in the Arrow segment.</param>
        /// <returns>The result produced by this codec helper.</returns>
        public static StatusCode[] ReadStatusMany(IArrowArray a, int s, int n)
        {
            return Enumerable.Range(s, n).Select(i => new StatusCode(((UInt32Array)a).GetValue(i) ?? 0)).ToArray();
        }

        /// <summary>
        /// Reads OPC UA QualifiedName values from an Arrow array segment.
        /// </summary>
        /// <param name = "a">The Arrow array to read.</param>
        /// <param name = "s">The first value index in the Arrow segment.</param>
        /// <param name = "n">The number of values in the Arrow segment.</param>
        /// <returns>The result produced by this codec helper.</returns>
        public static QualifiedName[] ReadQualifiedNameMany(IArrowArray a, int s, int n)
        {
            return Enumerable.Range(s, n).Select(i => ReadQualifiedName(a, i)).ToArray();
        }

        /// <summary>
        /// Reads OPC UA LocalizedText values from an Arrow array segment.
        /// </summary>
        /// <param name = "a">The Arrow array to read.</param>
        /// <param name = "s">The first value index in the Arrow segment.</param>
        /// <param name = "n">The number of values in the Arrow segment.</param>
        /// <returns>The result produced by this codec helper.</returns>
        public static LocalizedText[] ReadLocalizedTextMany(IArrowArray a, int s, int n)
        {
            return Enumerable.Range(s, n).Select(i => ReadLocalizedText(a, i)).ToArray();
        }

        /// <summary>
        /// Reads OPC UA DiagnosticInfo values from an Arrow array segment.
        /// </summary>
        /// <param name = "a">The Arrow array to read.</param>
        /// <param name = "s">The first value index in the Arrow segment.</param>
        /// <param name = "n">The number of values in the Arrow segment.</param>
        /// <returns>The result produced by this codec helper.</returns>
        public static DiagnosticInfo?[] ReadDiagnosticMany(IArrowArray a, int s, int n)
        {
            return Enumerable.Range(s, n).Select(i => ReadDiagnostic(a, i)).ToArray();
        }

        /// <summary>
        /// Reads OPC UA Variant values from an Arrow array segment.
        /// </summary>
        /// <param name = "a">The Arrow array to read.</param>
        /// <param name = "s">The first value index in the Arrow segment.</param>
        /// <param name = "n">The number of values in the Arrow segment.</param>
        /// <returns>The result produced by this codec helper.</returns>
        public static Variant[] ReadVariantMany(IArrowArray a, int s, int n)
        {
            return Enumerable.Range(s, n).Select(i => ReadVariant(a, i)).ToArray();
        }

        /// <summary>
        /// Reads OPC UA DataValue values from an Arrow array segment.
        /// </summary>
        /// <param name = "a">The Arrow array to read.</param>
        /// <param name = "s">The first value index in the Arrow segment.</param>
        /// <param name = "n">The number of values in the Arrow segment.</param>
        /// <returns>The result produced by this codec helper.</returns>
        public static DataValue[] ReadDataValueMany(IArrowArray a, int s, int n)
        {
            return Enumerable.Range(s, n).Select(i => ReadDataValue(a, i)).ToArray();
        }

        /// <summary>
        /// Reads OPC UA ExtensionObject values from an Arrow array segment.
        /// </summary>
        /// <param name = "a">The Arrow array to read.</param>
        /// <param name = "s">The first value index in the Arrow segment.</param>
        /// <param name = "n">The number of values in the Arrow segment.</param>
        /// <returns>The result produced by this codec helper.</returns>
        public static ExtensionObject[] ReadExtensionMany(IArrowArray a, int s, int n)
        {
            return Enumerable.Range(s, n).Select(i => ReadExtension(a, i)).ToArray();
        }
    }
}
