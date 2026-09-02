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

namespace Opc.Ua.OpenUsd.Scene
{
    /// <summary>
    /// A value authored on a USD attribute, scoped to the shapes a <c>.usda</c>
    /// document can express.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The USD value model is recursive and ragged: besides scalars it carries
    /// tuples (<c>float3</c>), arrays, arrays of tuples (<c>color3f[]</c>),
    /// matrices authored as a tuple of row tuples, and asset paths and prim path
    /// references that must round-trip as their own syntax. A
    /// <see cref="Variant"/> cannot express the nested "array of tuple" shape, so
    /// the scene document model uses this union instead of an untyped
    /// <c>object</c>.
    /// </para>
    /// <para>
    /// The attribute's <c>TypeName</c> stays authoritative for how a value is
    /// rendered back out - the kind adds type safety, it does not replace the
    /// declared USD type.
    /// </para>
    /// <para>
    /// Absence is <see cref="Null"/> with <see cref="IsNull"/> set, never
    /// <see cref="Nullable{T}"/>.
    /// </para>
    /// </remarks>
    public readonly struct UsdValue : INullable, IEquatable<UsdValue>
    {
        /// <summary>
        /// A value that carries nothing, for an attribute that is declared but
        /// has no authored value.
        /// </summary>
        public static UsdValue Null => default;

        /// <summary>
        /// Whether this value carries nothing.
        /// </summary>
        public bool IsNull => m_kind == UsdValueKind.Null;

        /// <summary>
        /// The shape this value carries.
        /// </summary>
        public UsdValueKind Kind => m_kind;

        /// <summary>
        /// Creates a boolean value.
        /// </summary>
        /// <param name="value">The value.</param>
        /// <returns>The USD value.</returns>
        public static UsdValue From(bool value)
        {
            return new UsdValue(UsdValueKind.Boolean, value ? 1L : 0L, 0.0, null, null, null);
        }

        /// <summary>
        /// Creates an integral value.
        /// </summary>
        /// <param name="value">The value.</param>
        /// <returns>The USD value.</returns>
        public static UsdValue From(long value)
        {
            return new UsdValue(UsdValueKind.Integer, value, 0.0, null, null, null);
        }

        /// <summary>
        /// Creates a floating point value.
        /// </summary>
        /// <param name="value">The value.</param>
        /// <returns>The USD value.</returns>
        public static UsdValue From(double value)
        {
            return new UsdValue(UsdValueKind.Double, 0L, value, null, null, null);
        }

        /// <summary>
        /// Creates a quoted string value.
        /// </summary>
        /// <param name="value">The text, or <c>null</c> for a null value.</param>
        /// <returns>The USD value.</returns>
        public static UsdValue FromString(string? value)
        {
            return value == null
                ? Null
                : new UsdValue(UsdValueKind.String, 0L, 0.0, value, null, null);
        }

        /// <summary>
        /// Creates a bare token value.
        /// </summary>
        /// <param name="value">The token, or <c>null</c> for a null value.</param>
        /// <returns>The USD value.</returns>
        public static UsdValue FromToken(string? value)
        {
            return value == null
                ? Null
                : new UsdValue(UsdValueKind.Token, 0L, 0.0, value, null, null);
        }

        /// <summary>
        /// Creates an asset path value, authored as <c>@path@</c>.
        /// </summary>
        /// <param name="value">The asset path, or <c>null</c> for a null value.</param>
        /// <returns>The USD value.</returns>
        public static UsdValue FromAssetPath(string? value)
        {
            return value == null
                ? Null
                : new UsdValue(UsdValueKind.AssetPath, 0L, 0.0, value, null, null);
        }

        /// <summary>
        /// Creates a prim path reference, authored as <c>&lt;/Path&gt;</c>.
        /// </summary>
        /// <param name="value">The prim path, or <c>null</c> for a null value.</param>
        /// <returns>The USD value.</returns>
        public static UsdValue FromPathReference(string? value)
        {
            return value == null
                ? Null
                : new UsdValue(UsdValueKind.PathReference, 0L, 0.0, value, null, null);
        }

        /// <summary>
        /// Creates a fixed arity group, authored as <c>(a, b, c)</c>.
        /// </summary>
        /// <param name="items">The components.</param>
        /// <returns>The USD value.</returns>
        public static UsdValue FromTuple(ArrayOf<UsdValue> items)
        {
            return new UsdValue(UsdValueKind.Tuple, 0L, 0.0, null, items.ToArray(), null);
        }

        /// <summary>
        /// Creates a sequence, authored as <c>[a, b, c]</c>.
        /// </summary>
        /// <param name="items">The elements.</param>
        /// <returns>The USD value.</returns>
        public static UsdValue FromArray(ArrayOf<UsdValue> items)
        {
            return new UsdValue(UsdValueKind.Array, 0L, 0.0, null, items.ToArray(), null);
        }

        /// <summary>
        /// Creates a matrix from its rows, each of which is a tuple.
        /// </summary>
        /// <param name="rows">The rows.</param>
        /// <returns>The USD value.</returns>
        public static UsdValue FromMatrix(ArrayOf<UsdValue> rows)
        {
            return new UsdValue(UsdValueKind.Matrix, 0L, 0.0, null, rows.ToArray(), null);
        }

        /// <summary>
        /// Creates a nested metadata dictionary.
        /// </summary>
        /// <param name="entries">The entries, or <c>null</c> for a null value.</param>
        /// <returns>The USD value.</returns>
        public static UsdValue FromDictionary(IReadOnlyDictionary<string, UsdValue>? entries)
        {
            return entries == null
                ? Null
                : new UsdValue(UsdValueKind.Dictionary, 0L, 0.0, null, null, entries);
        }

        /// <summary>
        /// Reads a boolean value.
        /// </summary>
        /// <param name="value">The value when this is a boolean.</param>
        /// <returns><c>true</c> when this is a boolean.</returns>
        public bool TryGetBoolean(out bool value)
        {
            value = m_integer != 0L;
            return m_kind == UsdValueKind.Boolean;
        }

        /// <summary>
        /// Reads an integral value.
        /// </summary>
        /// <param name="value">The value when this is an integer.</param>
        /// <returns><c>true</c> when this is an integer.</returns>
        public bool TryGetInteger(out long value)
        {
            value = m_integer;
            return m_kind == UsdValueKind.Integer;
        }

        /// <summary>
        /// Reads a floating point value.
        /// </summary>
        /// <param name="value">The value when this is a double.</param>
        /// <returns><c>true</c> when this is a double.</returns>
        public bool TryGetDouble(out double value)
        {
            value = m_double;
            return m_kind == UsdValueKind.Double;
        }

        /// <summary>
        /// Reads any numeric value as a double, widening an integer.
        /// </summary>
        /// <param name="value">The value when this is numeric.</param>
        /// <returns><c>true</c> when this is an integer or a double.</returns>
        public bool TryGetNumber(out double value)
        {
            switch (m_kind)
            {
                case UsdValueKind.Integer:
                    value = m_integer;
                    return true;
                case UsdValueKind.Double:
                    value = m_double;
                    return true;
                case UsdValueKind.Boolean:
                    value = m_integer;
                    return true;
                default:
                    value = 0.0;
                    return false;
            }
        }

        /// <summary>
        /// Reads a quoted string value.
        /// </summary>
        /// <param name="value">The text when this is a string.</param>
        /// <returns><c>true</c> when this is a string.</returns>
        public bool TryGetString(out string value)
        {
            value = m_text ?? string.Empty;
            return m_kind == UsdValueKind.String;
        }

        /// <summary>
        /// Reads a bare token value.
        /// </summary>
        /// <param name="value">The token when this is a token.</param>
        /// <returns><c>true</c> when this is a token.</returns>
        public bool TryGetToken(out string value)
        {
            value = m_text ?? string.Empty;
            return m_kind == UsdValueKind.Token;
        }

        /// <summary>
        /// Reads an asset path value.
        /// </summary>
        /// <param name="value">The asset path when this is one.</param>
        /// <returns><c>true</c> when this is an asset path.</returns>
        public bool TryGetAssetPath(out string value)
        {
            value = m_text ?? string.Empty;
            return m_kind == UsdValueKind.AssetPath;
        }

        /// <summary>
        /// Reads a prim path reference.
        /// </summary>
        /// <param name="value">The prim path when this is one.</param>
        /// <returns><c>true</c> when this is a path reference.</returns>
        public bool TryGetPathReference(out string value)
        {
            value = m_text ?? string.Empty;
            return m_kind == UsdValueKind.PathReference;
        }

        /// <summary>
        /// Reads any textual value, whatever syntax it was authored with.
        /// </summary>
        /// <param name="value">The text when this value carries text.</param>
        /// <returns><c>true</c> when this value carries text.</returns>
        public bool TryGetText(out string value)
        {
            value = m_text ?? string.Empty;
            return m_kind is UsdValueKind.String
                or UsdValueKind.Token
                or UsdValueKind.AssetPath
                or UsdValueKind.PathReference;
        }

        /// <summary>
        /// Reads the components of a tuple.
        /// </summary>
        /// <param name="value">The components when this is a tuple.</param>
        /// <returns><c>true</c> when this is a tuple.</returns>
        public bool TryGetTuple(out ArrayOf<UsdValue> value)
        {
            value = Items;
            return m_kind == UsdValueKind.Tuple;
        }

        /// <summary>
        /// Reads the elements of an array.
        /// </summary>
        /// <param name="value">The elements when this is an array.</param>
        /// <returns><c>true</c> when this is an array.</returns>
        public bool TryGetArray(out ArrayOf<UsdValue> value)
        {
            value = Items;
            return m_kind == UsdValueKind.Array;
        }

        /// <summary>
        /// Reads the rows of a matrix.
        /// </summary>
        /// <param name="value">The rows when this is a matrix.</param>
        /// <returns><c>true</c> when this is a matrix.</returns>
        public bool TryGetMatrix(out ArrayOf<UsdValue> value)
        {
            value = Items;
            return m_kind == UsdValueKind.Matrix;
        }

        /// <summary>
        /// Reads the elements of any composite value - a tuple, an array or the
        /// rows of a matrix - without caring which of the three it is.
        /// </summary>
        /// <param name="value">The elements when this value is composite.</param>
        /// <returns><c>true</c> when this value is composite.</returns>
        public bool TryGetItems(out ArrayOf<UsdValue> value)
        {
            value = Items;
            return m_kind is UsdValueKind.Tuple
                or UsdValueKind.Array
                or UsdValueKind.Matrix;
        }

        /// <summary>
        /// Reads the entries of a nested metadata dictionary.
        /// </summary>
        /// <param name="value">The entries when this is a dictionary.</param>
        /// <returns><c>true</c> when this is a dictionary.</returns>
        public bool TryGetDictionary(out IReadOnlyDictionary<string, UsdValue> value)
        {
            value = m_entries ?? s_emptyEntries;
            return m_kind == UsdValueKind.Dictionary;
        }

        /// <inheritdoc/>
        public bool Equals(UsdValue other)
        {
            if (m_kind != other.m_kind)
            {
                return false;
            }
            switch (m_kind)
            {
                case UsdValueKind.Null:
                    return true;
                case UsdValueKind.Boolean:
                case UsdValueKind.Integer:
                    return m_integer == other.m_integer;
                case UsdValueKind.Double:
                    return m_double.Equals(other.m_double);
                case UsdValueKind.String:
                case UsdValueKind.Token:
                case UsdValueKind.AssetPath:
                case UsdValueKind.PathReference:
                    return string.Equals(m_text, other.m_text, StringComparison.Ordinal);
                case UsdValueKind.Dictionary:
                    return EntriesEqual(m_entries, other.m_entries);
                default:
                    return ItemsEqual(m_items, other.m_items);
            }
        }

        /// <inheritdoc/>
        public override bool Equals(object? obj)
        {
            return obj is UsdValue other && Equals(other);
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            var hash = new HashCode();
            hash.Add(m_kind);
            switch (m_kind)
            {
                case UsdValueKind.Boolean:
                case UsdValueKind.Integer:
                    hash.Add(m_integer);
                    break;
                case UsdValueKind.Double:
                    hash.Add(m_double);
                    break;
                case UsdValueKind.String:
                case UsdValueKind.Token:
                case UsdValueKind.AssetPath:
                case UsdValueKind.PathReference:
                    hash.Add(m_text, StringComparer.Ordinal);
                    break;
                case UsdValueKind.Tuple:
                case UsdValueKind.Array:
                case UsdValueKind.Matrix:
                    System.ReadOnlySpan<UsdValue> items = Span;
                    hash.Add(items.Length);
                    for (int ii = 0; ii < items.Length; ii++)
                    {
                        hash.Add(items[ii]);
                    }
                    break;
                case UsdValueKind.Dictionary:
                    // The entry hashes are summed rather than sequenced, so two equal dictionaries
                    // hash alike whatever order they enumerate in while dictionaries that differ
                    // only in their entries - not in their size - still separate.
                    hash.Add(m_entries?.Count ?? 0);
                    hash.Add(EntriesHashCode(m_entries));
                    break;
                default:
                    break;
            }
            return hash.ToHashCode();
        }

        /// <summary>
        /// Compares two values.
        /// </summary>
        /// <param name="left">The first value.</param>
        /// <param name="right">The second value.</param>
        /// <returns><c>true</c> when the values are equal.</returns>
        public static bool operator ==(UsdValue left, UsdValue right)
        {
            return left.Equals(right);
        }

        /// <summary>
        /// Compares two values.
        /// </summary>
        /// <param name="left">The first value.</param>
        /// <param name="right">The second value.</param>
        /// <returns><c>true</c> when the values differ.</returns>
        public static bool operator !=(UsdValue left, UsdValue right)
        {
            return !left.Equals(right);
        }

        /// <summary>
        /// Renders this value to its invariant textual form, which is what a caller that cannot
        /// carry the value in a typed shape falls back to. A composite renders its items and a
        /// dictionary its entries ordered by key, so the text is deterministic and no value is
        /// silently rendered as the empty string.
        /// </summary>
        /// <returns>The textual form.</returns>
        public override string ToString()
        {
            switch (m_kind)
            {
                case UsdValueKind.Null:
                    return string.Empty;
                case UsdValueKind.Boolean:
                    return m_integer != 0L ? "true" : "false";
                case UsdValueKind.Integer:
                    return m_integer.ToString(CultureInfo.InvariantCulture);
                case UsdValueKind.Double:
                    return m_double.ToString("R", CultureInfo.InvariantCulture);
                case UsdValueKind.Tuple:
                case UsdValueKind.Matrix:
                    return "(" + JoinItems() + ")";
                case UsdValueKind.Array:
                    return "[" + JoinItems() + "]";
                case UsdValueKind.Dictionary:
                    return "{" + JoinEntries() + "}";
                default:
                    return m_text ?? string.Empty;
            }
        }

        private UsdValue(
            UsdValueKind kind,
            long integer,
            double number,
            string? text,
            UsdValue[]? items,
            IReadOnlyDictionary<string, UsdValue>? entries)
        {
            m_kind = kind;
            m_integer = integer;
            m_double = number;
            m_text = text;
            m_items = items;
            m_entries = entries;
        }

        private System.ReadOnlySpan<UsdValue> Span => m_items ?? [];

        private ArrayOf<UsdValue> Items => (m_items ?? []).ToArrayOf();

        private string JoinItems()
        {
            System.ReadOnlySpan<UsdValue> items = Span;
            var builder = new System.Text.StringBuilder();
            for (int ii = 0; ii < items.Length; ii++)
            {
                if (ii > 0)
                {
                    builder.Append(", ");
                }
                builder.Append(items[ii].ToString());
            }
            return builder.ToString();
        }

        private string JoinEntries()
        {
            if (m_entries == null || m_entries.Count == 0)
            {
                return string.Empty;
            }
            var keys = new List<string>(m_entries.Count);
            foreach (KeyValuePair<string, UsdValue> entry in m_entries)
            {
                keys.Add(entry.Key);
            }
            // Ordered so the rendering of a dictionary does not depend on its enumeration order.
            keys.Sort(StringComparer.Ordinal);
            var builder = new System.Text.StringBuilder();
            for (int ii = 0; ii < keys.Count; ii++)
            {
                if (ii > 0)
                {
                    builder.Append(", ");
                }
                builder.Append(keys[ii])
                    .Append(": ")
                    .Append(m_entries[keys[ii]].ToString());
            }
            return builder.ToString();
        }

        private static bool ItemsEqual(UsdValue[]? left, UsdValue[]? right)
        {
            System.ReadOnlySpan<UsdValue> a = left ?? [];
            System.ReadOnlySpan<UsdValue> b = right ?? [];
            if (a.Length != b.Length)
            {
                return false;
            }
            for (int ii = 0; ii < a.Length; ii++)
            {
                if (!a[ii].Equals(b[ii]))
                {
                    return false;
                }
            }
            return true;
        }

        private static bool EntriesEqual(
            IReadOnlyDictionary<string, UsdValue>? left,
            IReadOnlyDictionary<string, UsdValue>? right)
        {
            int leftCount = left?.Count ?? 0;
            int rightCount = right?.Count ?? 0;
            if (leftCount != rightCount)
            {
                return false;
            }
            if (leftCount == 0)
            {
                return true;
            }
            foreach (KeyValuePair<string, UsdValue> entry in left!)
            {
                if (!right!.TryGetValue(entry.Key, out UsdValue other) ||
                    !entry.Value.Equals(other))
                {
                    return false;
                }
            }
            return true;
        }

        private static int EntriesHashCode(IReadOnlyDictionary<string, UsdValue>? entries)
        {
            int combined = 0;
            if (entries != null)
            {
                foreach (KeyValuePair<string, UsdValue> entry in entries)
                {
                    // Addition is commutative, so the result does not depend on the order the
                    // entries enumerate in, which is what keeps this consistent with Equals.
                    combined = unchecked(combined + HashCode.Combine(
                        StringComparer.Ordinal.GetHashCode(entry.Key),
                        entry.Value.GetHashCode()));
                }
            }
            return combined;
        }

        private static readonly IReadOnlyDictionary<string, UsdValue> s_emptyEntries =
            new Dictionary<string, UsdValue>(StringComparer.Ordinal);

        private readonly UsdValueKind m_kind;
        private readonly long m_integer;
        private readonly double m_double;
        private readonly string? m_text;
        private readonly UsdValue[]? m_items;
        private readonly IReadOnlyDictionary<string, UsdValue>? m_entries;
    }
}
