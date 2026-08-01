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

namespace Opc.Ua.OpenUsdScene.Conversion
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
            object? value, UsdValueTypeMapping mapping, uint componentCount, out Variant result)
        {
            result = default;
            if (mapping == null)
            {
                throw new ArgumentNullException(nameof(mapping));
            }
            if (value == null)
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
                IReadOnlyList<object?> items;
                if (width > 0)
                {
                    // A fixed-size math type is a flat array of components, but USD authors a
                    // matrix4d as four nested 4-tuples, so flatten to the leaves before the arity
                    // check (mirrors the reference converter's _flat). Otherwise a matrix4d would
                    // report four items against a width of sixteen and be dropped.
                    items = Flatten(value);
                    if (items.Count != width)
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
                IReadOnlyList<object?> rows = AsSequence(value);
                var flat = new List<object?>(rows.Count * width);
                foreach (object? row in rows)
                {
                    // Flatten each element to its leaves so an array of matrix4d (each authored as
                    // four nested 4-tuples) is honoured. The outer array stays grouped one row per
                    // element — only the element shape is flattened, so a genuinely nested
                    // array-of-tuples such as color3f[] keeps its per-tuple grouping.
                    List<object?> cells = Flatten(row);
                    if (cells.Count != width)
                    {
                        return false;
                    }
                    foreach (object? cell in cells)
                    {
                        flat.Add(cell);
                    }
                }
                return TryMatrix(flat, mapping.ElementType, rows.Count, width, out result);
            }
            return false;
        }

        /// <summary>
        /// Reads a materialized value back into the plain shape used by the scene document
        /// model, so an export reproduces the authored form (§7.2).
        /// </summary>
        /// <param name="value">The value read from the materialized Variable.</param>
        /// <returns>The USD-shaped value.</returns>
        public static object? Decoerce(object? value)
        {
            if (value is string || value == null)
            {
                return value;
            }
            if (value is Array array)
            {
                if (array.Rank == 2)
                {
                    // An array-of-tuples type comes back as a rectangular matrix; regroup it
                    // into the per-tuple rows the scene document model uses.
                    int rows = array.GetLength(0);
                    int width = array.GetLength(1);
                    var grouped = new object?[rows];
                    for (int r = 0; r < rows; r++)
                    {
                        var cells = new object?[width];
                        for (int c = 0; c < width; c++)
                        {
                            cells[c] = array.GetValue(r, c);
                        }
                        grouped[r] = cells;
                    }
                    return grouped;
                }
                var items = new object?[array.Length];
                array.CopyTo(items, 0);
                return items;
            }
            return value;
        }

        private static bool TryScalar(object value, BuiltInType elementType, out Variant result)
        {
            result = default;
            switch (elementType)
            {
                case BuiltInType.Boolean:
                    if (!TryConvert(value, Convert.ToBoolean, out bool b))
                    {
                        return false;
                    }
                    result = Variant.From(b);
                    return true;
                case BuiltInType.SByte:
                    if (!TryConvert(value, Convert.ToSByte, out sbyte sb))
                    {
                        return false;
                    }
                    result = Variant.From(sb);
                    return true;
                case BuiltInType.Int32:
                    if (!TryConvert(value, Convert.ToInt32, out int i))
                    {
                        return false;
                    }
                    result = Variant.From(i);
                    return true;
                case BuiltInType.Int64:
                    if (!TryConvert(value, Convert.ToInt64, out long l))
                    {
                        return false;
                    }
                    result = Variant.From(l);
                    return true;
                case BuiltInType.UInt32:
                    if (!TryConvert(value, Convert.ToUInt32, out uint ui))
                    {
                        return false;
                    }
                    result = Variant.From(ui);
                    return true;
                case BuiltInType.UInt64:
                    if (!TryConvert(value, Convert.ToUInt64, out ulong ul))
                    {
                        return false;
                    }
                    result = Variant.From(ul);
                    return true;
                case BuiltInType.Float:
                    if (!TryConvert(value, Convert.ToSingle, out float f))
                    {
                        return false;
                    }
                    result = Variant.From(f);
                    return true;
                case BuiltInType.Double:
                    if (!TryConvert(value, Convert.ToDouble, out double d))
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
            IReadOnlyList<object?> items, BuiltInType elementType, out Variant result)
        {
            result = default;
            switch (elementType)
            {
                case BuiltInType.Boolean:
                    if (!TryFill(items, Convert.ToBoolean, out bool[] b))
                    {
                        return false;
                    }
                    result = Variant.From((ArrayOf<bool>)b);
                    return true;
                case BuiltInType.SByte:
                    if (!TryFill(items, Convert.ToSByte, out sbyte[] sb))
                    {
                        return false;
                    }
                    result = Variant.From((ArrayOf<sbyte>)sb);
                    return true;
                case BuiltInType.Int32:
                    if (!TryFill(items, Convert.ToInt32, out int[] i))
                    {
                        return false;
                    }
                    result = Variant.From((ArrayOf<int>)i);
                    return true;
                case BuiltInType.Int64:
                    if (!TryFill(items, Convert.ToInt64, out long[] l))
                    {
                        return false;
                    }
                    result = Variant.From((ArrayOf<long>)l);
                    return true;
                case BuiltInType.UInt32:
                    if (!TryFill(items, Convert.ToUInt32, out uint[] ui))
                    {
                        return false;
                    }
                    result = Variant.From((ArrayOf<uint>)ui);
                    return true;
                case BuiltInType.UInt64:
                    if (!TryFill(items, Convert.ToUInt64, out ulong[] ul))
                    {
                        return false;
                    }
                    result = Variant.From((ArrayOf<ulong>)ul);
                    return true;
                case BuiltInType.Float:
                    if (!TryFill(items, Convert.ToSingle, out float[] f))
                    {
                        return false;
                    }
                    result = Variant.From((ArrayOf<float>)f);
                    return true;
                case BuiltInType.Double:
                    if (!TryFill(items, Convert.ToDouble, out double[] d))
                    {
                        return false;
                    }
                    result = Variant.From((ArrayOf<double>)d);
                    return true;
                case BuiltInType.String:
                    var strings = new string[items.Count];
                    for (int n = 0; n < items.Count; n++)
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
            List<object?> flat,
            BuiltInType elementType,
            int rows,
            int width,
            out Variant result)
        {
            result = default;
            switch (elementType)
            {
                case BuiltInType.Boolean:
                    if (!TryFill(flat, Convert.ToBoolean, out bool[] b))
                    {
                        return false;
                    }
                    result = Variant.From((MatrixOf<bool>)Reshape(b, rows, width));
                    return true;
                case BuiltInType.SByte:
                    if (!TryFill(flat, Convert.ToSByte, out sbyte[] sb))
                    {
                        return false;
                    }
                    result = Variant.From((MatrixOf<sbyte>)Reshape(sb, rows, width));
                    return true;
                case BuiltInType.Int32:
                    if (!TryFill(flat, Convert.ToInt32, out int[] i))
                    {
                        return false;
                    }
                    result = Variant.From((MatrixOf<int>)Reshape(i, rows, width));
                    return true;
                case BuiltInType.Int64:
                    if (!TryFill(flat, Convert.ToInt64, out long[] l))
                    {
                        return false;
                    }
                    result = Variant.From((MatrixOf<long>)Reshape(l, rows, width));
                    return true;
                case BuiltInType.UInt32:
                    if (!TryFill(flat, Convert.ToUInt32, out uint[] ui))
                    {
                        return false;
                    }
                    result = Variant.From((MatrixOf<uint>)Reshape(ui, rows, width));
                    return true;
                case BuiltInType.UInt64:
                    if (!TryFill(flat, Convert.ToUInt64, out ulong[] ul))
                    {
                        return false;
                    }
                    result = Variant.From((MatrixOf<ulong>)Reshape(ul, rows, width));
                    return true;
                case BuiltInType.Float:
                    if (!TryFill(flat, Convert.ToSingle, out float[] f))
                    {
                        return false;
                    }
                    result = Variant.From((MatrixOf<float>)Reshape(f, rows, width));
                    return true;
                case BuiltInType.Double:
                    if (!TryFill(flat, Convert.ToDouble, out double[] d))
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

        private static IReadOnlyList<object?> AsSequence(object? value)
        {
            if (value is IReadOnlyList<object?> list)
            {
                return list;
            }
            if (value is string || value == null)
            {
                return new[] { value };
            }
            if (value is IEnumerable enumerable)
            {
                var items = new List<object?>();
                foreach (object? item in enumerable)
                {
                    items.Add(item);
                }
                return items;
            }
            return new object?[] { value };
        }

        /// <summary>
        /// Recursively flattens nested tuples and arrays to their scalar leaves, mirroring the
        /// reference converter's <c>_flat</c>. A string is treated as a leaf, not a character
        /// sequence. Used to reconcile a fixed-size math type (matrix4d authored as nested tuples)
        /// with its flat component count before the arity check.
        /// </summary>
        private static List<object?> Flatten(object? value)
        {
            var sink = new List<object?>();
            FlattenInto(value, sink);
            return sink;
        }

        private static void FlattenInto(object? value, List<object?> sink)
        {
            if (value is string || value == null)
            {
                sink.Add(value);
                return;
            }
            if (value is IEnumerable enumerable)
            {
                foreach (object? item in enumerable)
                {
                    FlattenInto(item, sink);
                }
                return;
            }
            sink.Add(value);
        }

        private static bool TryConvert<T>(
            object value, Func<object, IFormatProvider, T> convert, out T result)
        {
            try
            {
                result = convert(value, CultureInfo.InvariantCulture);
                return true;
            }
            catch (Exception ex) when (ex is InvalidCastException or FormatException
                or OverflowException or ArgumentException)
            {
                result = default!;
                return false;
            }
        }

        private static bool TryFill<T>(
            IReadOnlyList<object?> items, Func<object, IFormatProvider, T> convert, out T[] result)
        {
            var target = new T[items.Count];
            for (int i = 0; i < items.Count; i++)
            {
                if (items[i] == null)
                {
                    target[i] = default!;
                    continue;
                }
                if (!TryConvert(items[i]!, convert, out T converted))
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
        /// well-defined textual form — <c>null</c>, a string, a bool (as its USD <c>true</c>/
        /// <c>false</c> spelling) or an <see cref="IFormattable"/> (numbers, timestamps). It fails
        /// closed for a structured value (a tuple/array modelled as <c>object?[]</c> or
        /// <c>List&lt;object?&gt;</c>) or any other object, because emitting <c>value.ToString()</c>
        /// there would publish a CLR type name such as <c>"System.Object[]"</c> — a plausible but
        /// wrong value. The caller returns <c>false</c> so the attribute is left unresolved instead.
        /// </summary>
        private static bool TryStringifyLeaf(object? value, out string result)
        {
            switch (value)
            {
                case null:
                    result = string.Empty;
                    return true;
                case string s:
                    result = s;
                    return true;
                case bool b:
                    result = b ? "true" : "false";
                    return true;
                case IFormattable f:
                    result = f.ToString(null, CultureInfo.InvariantCulture);
                    return true;
            }
            result = string.Empty;
            return false;
        }
    }
}
