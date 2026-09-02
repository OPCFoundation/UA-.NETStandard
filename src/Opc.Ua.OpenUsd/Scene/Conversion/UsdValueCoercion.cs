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
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using Opc.Ua.OpenUsd.Scene;

namespace Opc.Ua.OpenUsd.Scene.Conversion
{
    /// <summary>
    /// Converts a parsed USD attribute value into the OPC UA <see cref="Variant"/> shape
    /// required by its §6.2 binding, and back again.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The input is deliberately loosely typed: any non-string <see cref="IEnumerable"/> is
    /// treated as a sequence, so a reader may represent a USD tuple or array as an
    /// <c>object[]</c>, a <c>List&lt;object&gt;</c> or any other sequence without the
    /// materializer having to know which.
    /// </para>
    /// <para>
    /// A <see cref="Variant"/> is produced through the statically typed
    /// <c>Variant.From</c> overloads rather than the reflection-based boxing constructor, so
    /// the library stays trim- and AOT-compatible.
    /// </para>
    /// <para>
    /// Conversion fails closed: a source that cannot be represented in the declared type
    /// leaves the attribute unresolved rather than being coerced to a plausible but wrong
    /// value.
    /// </para>
    /// </remarks>
    public static class UsdValueCoercion
    {
        /// <summary>
        /// Coerces a parsed USD value into the Variant shape the mapping calls for.
        /// </summary>
        /// <param name="value">The parsed USD value.</param>
        /// <param name="mapping">The §6.2 binding chosen for the attribute's type.</param>
        /// <param name="componentCount">The fixed component count of the USD type, or 0 when
        /// the type is not a fixed-size math type.</param>
        /// <param name="result">The coerced value.</param>
        /// <returns><c>true</c> when the value could be represented.</returns>
        public static bool TryCoerce(
            UsdValue value, UsdValueTypeMapping mapping, uint componentCount, out Variant result)
        {
            result = default;
            if (mapping == null)
            {
                throw new ArgumentNullException(nameof(mapping));
            }
            if (value.IsNull)
            {
                return false;
            }
            if (mapping.IsOpaque)
            {
                // An unrecognised value type is carried opaquely rather than dropped; the
                // attribute's UsdTypeName still records the exact spelling (§8.4). The value is
                // rendered to its authored .usda text through the writer's own emitter, so a later
                // export reproduces it faithfully instead of publishing a CLR type name for a
                // structured value (for example a "(0.1, 0.2, 0.3)" tuple). A value the writer
                // cannot render is left unresolved rather than coerced to a plausible-but-wrong
                // string (fail closed).
                if (UsdaWriter.TryRenderOpaqueValue(value, out string opaque))
                {
                    result = Variant.From(opaque);
                    return true;
                }
                return false;
            }

            int width = (int)componentCount;
            if (mapping.ValueRank == ValueRanks.Scalar)
            {
                return TryScalar(value, mapping.ElementType, out result);
            }
            if (mapping.ValueRank == ValueRanks.OneDimension)
            {
                UsdValue[] items;
                if (width > 0)
                {
                    // A fixed-size math type is a flat array of components, but USD authors a
                    // matrix4d as four nested 4-tuples, so flatten to the leaves before the arity
                    // check (mirrors the reference converter's _flat). Otherwise a matrix4d would
                    // report four items against a width of sixteen and be dropped.
                    items = [.. Flatten(value)];
                    if (items.Length != width)
                    {
                        // A fixed-size math type authored with the wrong arity cannot be honoured.
                        return false;
                    }
                }
                else
                {
                    items = AsSequence(value);
                }
                return TryArray(items, mapping.ElementType, out result);
            }
            if (mapping.ValueRank == ValueRanks.TwoDimensions && width > 0)
            {
                UsdValue[] rows = AsSequence(value);
                var flat = new List<UsdValue>(rows.Length * width);
                foreach (UsdValue row in rows)
                {
                    // Flatten each element to its leaves so an array of matrix4d (each authored as
                    // four nested 4-tuples) is honoured. The outer array stays grouped one row per
                    // element — only the element shape is flattened, so a genuinely nested
                    // array-of-tuples such as color3f[] keeps its per-tuple grouping.
                    List<UsdValue> cells = Flatten(row);
                    if (cells.Count != width)
                    {
                        return false;
                    }
                    foreach (UsdValue cell in cells)
                    {
                        flat.Add(cell);
                    }
                }
                return TryMatrix(flat, mapping.ElementType, rows.Length, width, out result);
            }
            return false;
        }

        /// <summary>
        /// Reads a materialized value back into the shape used by the scene document model, so an
        /// export reproduces the authored form (§7.2).
        /// </summary>
        /// <remarks>
        /// Takes the <see cref="Variant"/> straight from the Variable and reads it through its
        /// typed accessors, so no boxing accessor is needed and an <c>ArrayOf&lt;T&gt;</c> or
        /// <c>MatrixOf&lt;T&gt;</c> keeps its shape: a matrix is regrouped into the per-row tuples
        /// the document model uses for an array-of-tuples type.
        /// </remarks>
        /// <param name="value">The value read from the materialized Variable.</param>
        /// <returns>The USD-shaped value.</returns>
        public static UsdValue Decoerce(in Variant value)
        {
            TypeInfo typeInfo = value.TypeInfo;
            if (typeInfo.ValueRank == ValueRanks.TwoDimensions)
            {
                return DecoerceMatrix(value, typeInfo.BuiltInType);
            }
            if (typeInfo.ValueRank == ValueRanks.OneDimension)
            {
                return DecoerceArray(value, typeInfo.BuiltInType);
            }
            return DecoerceScalar(value, typeInfo.BuiltInType);
        }

        private static UsdValue DecoerceScalar(in Variant value, BuiltInType elementType)
        {
            switch (elementType)
            {
                case BuiltInType.Boolean:
                    return value.TryGetValue(out bool b) ? UsdValue.From(b) : UsdValue.Null;
                case BuiltInType.SByte:
                    return value.TryGetValue(out sbyte sb)
                        ? UsdValue.From(sb)
                        : UsdValue.Null;
                case BuiltInType.Int32:
                    return value.TryGetValue(out int i) ? UsdValue.From(i) : UsdValue.Null;
                case BuiltInType.Int64:
                    return value.TryGetValue(out long l) ? UsdValue.From(l) : UsdValue.Null;
                case BuiltInType.UInt32:
                    return value.TryGetValue(out uint ui) ? UsdValue.From(ui) : UsdValue.Null;
                case BuiltInType.UInt64:
                    return value.TryGetValue(out ulong ul)
                        ? FromUInt64(ul)
                        : UsdValue.Null;
                case BuiltInType.Float:
                    return value.TryGetValue(out float f) ? UsdValue.From(f) : UsdValue.Null;
                case BuiltInType.Double:
                    return value.TryGetValue(out double d) ? UsdValue.From(d) : UsdValue.Null;
                case BuiltInType.String:
                    return value.TryGetValue(out string s)
                        ? UsdValue.FromString(s)
                        : UsdValue.Null;
                default:
                    return UsdValue.Null;
            }
        }

        private static UsdValue DecoerceArray(in Variant value, BuiltInType elementType)
        {
            switch (elementType)
            {
                case BuiltInType.Boolean:
                    return value.TryGetValue(out ArrayOf<bool> b)
                        ? Wrap(b, static x => UsdValue.From(x))
                        : UsdValue.Null;
                case BuiltInType.SByte:
                    return value.TryGetValue(out ArrayOf<sbyte> sb)
                        ? Wrap(sb, static x => UsdValue.From(x))
                        : UsdValue.Null;
                case BuiltInType.Int32:
                    return value.TryGetValue(out ArrayOf<int> i)
                        ? Wrap(i, static x => UsdValue.From(x))
                        : UsdValue.Null;
                case BuiltInType.Int64:
                    return value.TryGetValue(out ArrayOf<long> l)
                        ? Wrap(l, static x => UsdValue.From(x))
                        : UsdValue.Null;
                case BuiltInType.UInt32:
                    return value.TryGetValue(out ArrayOf<uint> ui)
                        ? Wrap(ui, static x => UsdValue.From(x))
                        : UsdValue.Null;
                case BuiltInType.UInt64:
                    return value.TryGetValue(out ArrayOf<ulong> ul)
                        ? Wrap(ul, FromUInt64)
                        : UsdValue.Null;
                case BuiltInType.Float:
                    return value.TryGetValue(out ArrayOf<float> f)
                        ? Wrap(f, static x => UsdValue.From(x))
                        : UsdValue.Null;
                case BuiltInType.Double:
                    return value.TryGetValue(out ArrayOf<double> d)
                        ? Wrap(d, static x => UsdValue.From(x))
                        : UsdValue.Null;
                case BuiltInType.String:
                    return value.TryGetValue(out ArrayOf<string> s)
                        ? Wrap(s, static x => UsdValue.FromString(x))
                        : UsdValue.Null;
                default:
                    return UsdValue.Null;
            }
        }

        private static UsdValue DecoerceMatrix(in Variant value, BuiltInType elementType)
        {
            switch (elementType)
            {
                case BuiltInType.Boolean:
                    return value.TryGetValue(out MatrixOf<bool> b)
                        ? Regroup(b, static x => UsdValue.From(x))
                        : UsdValue.Null;
                case BuiltInType.SByte:
                    return value.TryGetValue(out MatrixOf<sbyte> sb)
                        ? Regroup(sb, static x => UsdValue.From(x))
                        : UsdValue.Null;
                case BuiltInType.Int32:
                    return value.TryGetValue(out MatrixOf<int> i)
                        ? Regroup(i, static x => UsdValue.From(x))
                        : UsdValue.Null;
                case BuiltInType.Int64:
                    return value.TryGetValue(out MatrixOf<long> l)
                        ? Regroup(l, static x => UsdValue.From(x))
                        : UsdValue.Null;
                case BuiltInType.UInt32:
                    return value.TryGetValue(out MatrixOf<uint> ui)
                        ? Regroup(ui, static x => UsdValue.From(x))
                        : UsdValue.Null;
                case BuiltInType.UInt64:
                    return value.TryGetValue(out MatrixOf<ulong> ul)
                        ? Regroup(ul, FromUInt64)
                        : UsdValue.Null;
                case BuiltInType.Float:
                    return value.TryGetValue(out MatrixOf<float> f)
                        ? Regroup(f, static x => UsdValue.From(x))
                        : UsdValue.Null;
                case BuiltInType.Double:
                    return value.TryGetValue(out MatrixOf<double> d)
                        ? Regroup(d, static x => UsdValue.From(x))
                        : UsdValue.Null;
                case BuiltInType.String:
                    return value.TryGetValue(out MatrixOf<string> s)
                        ? Regroup(s, static x => UsdValue.FromString(x))
                        : UsdValue.Null;
                default:
                    return UsdValue.Null;
            }
        }

        /// <summary>
        /// Reads an unsigned 64 bit value into a USD value.
        /// </summary>
        /// <remarks>
        /// A value up to <see cref="long.MaxValue"/> stays integral. No USD kind can carry a
        /// larger one integrally, so it is preserved as its invariant decimal text - which
        /// <see cref="TryAsUInt64"/> reads back - rather than cast to <c>long</c>, which would
        /// silently wrap it into a negative integer and author a wrong value on export.
        /// </remarks>
        /// <param name="value">The unsigned value read from the Variable.</param>
        /// <returns>The USD value.</returns>
        private static UsdValue FromUInt64(ulong value)
        {
            return value <= long.MaxValue
                ? UsdValue.From((long)value)
                : UsdValue.FromToken(value.ToString(CultureInfo.InvariantCulture));
        }

        /// <summary>
        /// Wraps a one dimensional value as an array.
        /// </summary>
        /// <typeparam name="T">The element type.</typeparam>
        /// <param name="source">The array read from the Variable.</param>
        /// <param name="project">Projects one element onto a USD value.</param>
        /// <returns>The elements as an array.</returns>
        private static UsdValue Wrap<T>(ArrayOf<T> source, Func<T, UsdValue> project)
        {
            System.ReadOnlySpan<T> span = source.Span;
            var items = new UsdValue[span.Length];
            for (int ii = 0; ii < span.Length; ii++)
            {
                items[ii] = project(span[ii]);
            }
            return UsdValue.FromArray(items.ToArrayOf());
        }

        /// <summary>
        /// Regroups a rectangular matrix into one tuple per row, which is how the scene document
        /// model carries an array-of-tuples type such as <c>color3f[]</c>.
        /// </summary>
        /// <typeparam name="T">The matrix element type.</typeparam>
        /// <param name="source">The matrix read from the Variable.</param>
        /// <param name="project">Projects one element onto a USD value.</param>
        /// <returns>An array of per-row tuples.</returns>
        private static UsdValue Regroup<T>(MatrixOf<T> source, Func<T, UsdValue> project)
        {
            int[] dimensions = source.Dimensions;
            int rows = dimensions.Length > 0 ? dimensions[0] : 0;
            int width = dimensions.Length > 1 ? dimensions[1] : 0;
            System.ReadOnlySpan<T> flat = source.Memory.Span;
            var grouped = new UsdValue[rows];
            for (int r = 0; r < rows; r++)
            {
                var cells = new UsdValue[width];
                for (int c = 0; c < width; c++)
                {
                    int index = (r * width) + c;
                    cells[c] = index < flat.Length ? project(flat[index]) : UsdValue.Null;
                }
                grouped[r] = UsdValue.FromTuple(cells.ToArrayOf());
            }
            return UsdValue.FromArray(grouped.ToArrayOf());
        }


        private static bool TryScalar(UsdValue value, BuiltInType elementType, out Variant result)
        {
            result = default;
            switch (elementType)
            {
                case BuiltInType.Boolean:
                    if (!TryAsBoolean(value, out bool b))
                    {
                        return false;
                    }
                    result = Variant.From(b);
                    return true;
                case BuiltInType.SByte:
                    if (!TryAsSByte(value, out sbyte sb))
                    {
                        return false;
                    }
                    result = Variant.From(sb);
                    return true;
                case BuiltInType.Int32:
                    if (!TryAsInt32(value, out int i))
                    {
                        return false;
                    }
                    result = Variant.From(i);
                    return true;
                case BuiltInType.Int64:
                    if (!TryAsInt64(value, out long l))
                    {
                        return false;
                    }
                    result = Variant.From(l);
                    return true;
                case BuiltInType.UInt32:
                    if (!TryAsUInt32(value, out uint ui))
                    {
                        return false;
                    }
                    result = Variant.From(ui);
                    return true;
                case BuiltInType.UInt64:
                    if (!TryAsUInt64(value, out ulong ul))
                    {
                        return false;
                    }
                    result = Variant.From(ul);
                    return true;
                case BuiltInType.Float:
                    if (!TryAsSingle(value, out float f))
                    {
                        return false;
                    }
                    result = Variant.From(f);
                    return true;
                case BuiltInType.Double:
                    if (!TryAsDouble(value, out double d))
                    {
                        return false;
                    }
                    result = Variant.From(d);
                    return true;
                case BuiltInType.String:
                    if (!TryStringifyLeaf(value, out string scalarString))
                    {
                        return false;
                    }
                    result = Variant.From(scalarString);
                    return true;
                default:
                    return false;
            }
        }

        private static bool TryArray(
            UsdValue[] items, BuiltInType elementType, out Variant result)
        {
            result = default;
            switch (elementType)
            {
                case BuiltInType.Boolean:
                    if (!TryFill(items, TryAsBoolean, out bool[] b))
                    {
                        return false;
                    }
                    result = Variant.From((ArrayOf<bool>)b);
                    return true;
                case BuiltInType.SByte:
                    if (!TryFill(items, TryAsSByte, out sbyte[] sb))
                    {
                        return false;
                    }
                    result = Variant.From((ArrayOf<sbyte>)sb);
                    return true;
                case BuiltInType.Int32:
                    if (!TryFill(items, TryAsInt32, out int[] i))
                    {
                        return false;
                    }
                    result = Variant.From((ArrayOf<int>)i);
                    return true;
                case BuiltInType.Int64:
                    if (!TryFill(items, TryAsInt64, out long[] l))
                    {
                        return false;
                    }
                    result = Variant.From((ArrayOf<long>)l);
                    return true;
                case BuiltInType.UInt32:
                    if (!TryFill(items, TryAsUInt32, out uint[] ui))
                    {
                        return false;
                    }
                    result = Variant.From((ArrayOf<uint>)ui);
                    return true;
                case BuiltInType.UInt64:
                    if (!TryFill(items, TryAsUInt64, out ulong[] ul))
                    {
                        return false;
                    }
                    result = Variant.From((ArrayOf<ulong>)ul);
                    return true;
                case BuiltInType.Float:
                    if (!TryFill(items, TryAsSingle, out float[] f))
                    {
                        return false;
                    }
                    result = Variant.From((ArrayOf<float>)f);
                    return true;
                case BuiltInType.Double:
                    if (!TryFill(items, TryAsDouble, out double[] d))
                    {
                        return false;
                    }
                    result = Variant.From((ArrayOf<double>)d);
                    return true;
                case BuiltInType.String:
                    var strings = new string[items.Length];
                    for (int n = 0; n < items.Length; n++)
                    {
                        if (!TryStringifyLeaf(items[n], out string element))
                        {
                            return false;
                        }
                        strings[n] = element;
                    }
                    result = Variant.From((ArrayOf<string>)strings);
                    return true;
                default:
                    return false;
            }
        }

        private static bool TryMatrix(
            List<UsdValue> flat,
            BuiltInType elementType,
            int rows,
            int width,
            out Variant result)
        {
            result = default;
            switch (elementType)
            {
                case BuiltInType.Boolean:
                    if (!TryFill(flat, TryAsBoolean, out bool[] b))
                    {
                        return false;
                    }
                    result = Variant.From((MatrixOf<bool>)Reshape(b, rows, width));
                    return true;
                case BuiltInType.SByte:
                    if (!TryFill(flat, TryAsSByte, out sbyte[] sb))
                    {
                        return false;
                    }
                    result = Variant.From((MatrixOf<sbyte>)Reshape(sb, rows, width));
                    return true;
                case BuiltInType.Int32:
                    if (!TryFill(flat, TryAsInt32, out int[] i))
                    {
                        return false;
                    }
                    result = Variant.From((MatrixOf<int>)Reshape(i, rows, width));
                    return true;
                case BuiltInType.Int64:
                    if (!TryFill(flat, TryAsInt64, out long[] l))
                    {
                        return false;
                    }
                    result = Variant.From((MatrixOf<long>)Reshape(l, rows, width));
                    return true;
                case BuiltInType.UInt32:
                    if (!TryFill(flat, TryAsUInt32, out uint[] ui))
                    {
                        return false;
                    }
                    result = Variant.From((MatrixOf<uint>)Reshape(ui, rows, width));
                    return true;
                case BuiltInType.UInt64:
                    if (!TryFill(flat, TryAsUInt64, out ulong[] ul))
                    {
                        return false;
                    }
                    result = Variant.From((MatrixOf<ulong>)Reshape(ul, rows, width));
                    return true;
                case BuiltInType.Float:
                    if (!TryFill(flat, TryAsSingle, out float[] f))
                    {
                        return false;
                    }
                    result = Variant.From((MatrixOf<float>)Reshape(f, rows, width));
                    return true;
                case BuiltInType.Double:
                    if (!TryFill(flat, TryAsDouble, out double[] d))
                    {
                        return false;
                    }
                    result = Variant.From((MatrixOf<double>)Reshape(d, rows, width));
                    return true;
                case BuiltInType.String:
                    var strings = new string[flat.Count];
                    for (int n = 0; n < flat.Count; n++)
                    {
                        if (!TryStringifyLeaf(flat[n], out string element))
                        {
                            return false;
                        }
                        strings[n] = element;
                    }
                    result = Variant.From((MatrixOf<string>)Reshape(strings, rows, width));
                    return true;
                default:
                    return false;
            }
        }

        private static UsdValue[] AsSequence(UsdValue value)
        {
            if (value.TryGetItems(out ArrayOf<UsdValue> items))
            {
                return items.ToArray() ?? [];
            }
            return [value];
        }

        /// <summary>
        /// Recursively flattens nested tuples and arrays to their scalar leaves, mirroring the
        /// reference converter's <c>_flat</c>. Used to reconcile a fixed-size math type (matrix4d
        /// authored as nested tuples) with its flat component count before the arity check.
        /// </summary>
        private static List<UsdValue> Flatten(UsdValue value)
        {
            var sink = new List<UsdValue>();
            FlattenInto(value, sink);
            return sink;
        }

        private static void FlattenInto(UsdValue value, List<UsdValue> sink)
        {
            if (value.TryGetItems(out ArrayOf<UsdValue> items))
            {
                System.ReadOnlySpan<UsdValue> span = items.Span;
                for (int ii = 0; ii < span.Length; ii++)
                {
                    FlattenInto(span[ii], sink);
                }
                return;
            }
            sink.Add(value);
        }

        private delegate bool UsdConverter<T>(UsdValue value, out T result);

        private static bool TryAsBoolean(UsdValue value, out bool result)
        {
            if (value.TryGetBoolean(out result))
            {
                return true;
            }
            if (value.TryGetNumber(out double number))
            {
                result = number != 0.0;
                return true;
            }
            if (value.TryGetText(out string text))
            {
                return bool.TryParse(text, out result);
            }
            result = false;
            return false;
        }

        private static bool TryAsDouble(UsdValue value, out double result)
        {
            if (value.TryGetNumber(out result))
            {
                return true;
            }
            if (value.TryGetText(out string text))
            {
                return double.TryParse(
                    text, NumberStyles.Float, CultureInfo.InvariantCulture, out result);
            }
            result = 0.0;
            return false;
        }

        private static bool TryAsSingle(UsdValue value, out float result)
        {
            if (!TryAsDouble(value, out double number))
            {
                result = 0.0f;
                return false;
            }
            result = (float)number;
            return !float.IsInfinity(result) || double.IsInfinity(number);
        }

        private static bool TryAsInt64(UsdValue value, out long result)
        {
            if (value.TryGetInteger(out result))
            {
                return true;
            }
            if (!TryAsDouble(value, out double number))
            {
                result = 0L;
                return false;
            }
            if (number is < long.MinValue or > long.MaxValue)
            {
                result = 0L;
                return false;
            }
            result = (long)number;
            return true;
        }

        private static bool TryAsUInt64(UsdValue value, out ulong result)
        {
            // A value above long.MaxValue has no integral USD kind to carry it, so Decoerce
            // preserves it as its invariant decimal text (see FromUInt64); read that form back
            // before falling back to the signed path.
            if (value.TryGetText(out string text) &&
                ulong.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out result))
            {
                return true;
            }
            if (!TryAsInt64(value, out long signed) || signed < 0L)
            {
                result = 0UL;
                return false;
            }
            result = (ulong)signed;
            return true;
        }

        private static bool TryAsSByte(UsdValue value, out sbyte result)
        {
            if (!TryAsInt64(value, out long signed) ||
                signed is < sbyte.MinValue or > sbyte.MaxValue)
            {
                result = 0;
                return false;
            }
            result = (sbyte)signed;
            return true;
        }

        private static bool TryAsInt32(UsdValue value, out int result)
        {
            if (!TryAsInt64(value, out long signed) ||
                signed is < int.MinValue or > int.MaxValue)
            {
                result = 0;
                return false;
            }
            result = (int)signed;
            return true;
        }

        private static bool TryAsUInt32(UsdValue value, out uint result)
        {
            if (!TryAsInt64(value, out long signed) || signed is < 0L or > uint.MaxValue)
            {
                result = 0U;
                return false;
            }
            result = (uint)signed;
            return true;
        }

        private static bool TryFill<T>(
            IReadOnlyList<UsdValue> items, UsdConverter<T> convert, out T[] result)
        {
            var target = new T[items.Count];
            for (int i = 0; i < items.Count; i++)
            {
                if (items[i].IsNull)
                {
                    target[i] = default!;
                    continue;
                }
                if (!convert(items[i], out T converted))
                {
                    result = Array.Empty<T>();
                    return false;
                }
                target[i] = converted;
            }
            result = target;
            return true;
        }

        private static T[,] Reshape<T>(T[] flat, int rows, int width)
        {
            var target = new T[rows, width];
            for (int i = 0; i < flat.Length; i++)
            {
                target[i / width, i % width] = flat[i];
            }
            return target;
        }

        /// <summary>
        /// Renders a single scalar leaf to its faithful invariant-culture string for a
        /// <c>string</c>/<c>token</c>/<c>asset</c> attribute. Succeeds only for a value that has a
        /// well-defined textual form — an absent value, text, a bool (as its USD <c>true</c>/
        /// <c>false</c> spelling) or a number. It fails closed for a structured value (a tuple,
        /// array, matrix or dictionary), because rendering one here would publish a plausible but
        /// wrong scalar. The caller returns <c>false</c> so the attribute is left unresolved.
        /// </summary>
        private static bool TryStringifyLeaf(UsdValue value, out string result)
        {
            switch (value.Kind)
            {
                case UsdValueKind.Null:
                    result = string.Empty;
                    return true;
                case UsdValueKind.String:
                case UsdValueKind.Token:
                case UsdValueKind.AssetPath:
                case UsdValueKind.PathReference:
                    value.TryGetText(out result);
                    return true;
                case UsdValueKind.Boolean:
                    value.TryGetBoolean(out bool b);
                    result = b ? "true" : "false";
                    return true;
                case UsdValueKind.Integer:
                    value.TryGetInteger(out long l);
                    result = l.ToString(CultureInfo.InvariantCulture);
                    return true;
                case UsdValueKind.Double:
                    value.TryGetDouble(out double d);
                    result = d.ToString("R", CultureInfo.InvariantCulture);
                    return true;
                default:
                    result = string.Empty;
                    return false;
            }
        }
    }
}
