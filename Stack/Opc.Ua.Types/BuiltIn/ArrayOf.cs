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
using System.Diagnostics.Contracts;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json.Serialization;

namespace Opc.Ua
{
    /// <summary>
    /// A read only sliceable array of T. The layout is the same as a
    /// <see cref="ReadOnlyMemory{T}"/> but you should not take a
    /// dependency on this fact. Replaces ReadOnlyList and all
    /// XXXCollection classes in past releases.
    /// </summary>
    /// <typeparam name="T">Type of the element in the array</typeparam>
    [CollectionBuilder(typeof(ArrayOf), nameof(ArrayOf.Create))]
    public readonly struct ArrayOf<T> :
        IConvertableToArray,
        IEquatable<ArrayOf<T>>,
        IEquatable<MatrixOf<T>>,
        IEquatable<IEnumerable<T>>,
        IEquatable<ReadOnlyMemory<T>>,
        IEquatable<T[]>,
        INullable
    {
        /// <summary>
        /// Empty array. Note that we initilize with array empty not
        /// ReadOnlyMemory.Empty which would result in IsNull => true
        /// </summary>
#pragma warning disable IDE0301 // Simplify collection initialization
        public static readonly ArrayOf<T> Empty = new(Array.Empty<T>());
#pragma warning restore IDE0301 // Simplify collection initialization

        /// <summary>
        /// Null array.
        /// </summary>
        public static readonly ArrayOf<T> Null;

        /// <summary>
        /// Get as memory
        /// </summary>
#pragma warning disable RCS1085 // Use auto-implemented property
        public ReadOnlyMemory<T> Memory => m_memory;
#pragma warning restore RCS1085 // Use auto-implemented property

        /// <summary>
        /// Returns the content of the array as a read only span
        /// </summary>
        [JsonIgnore]
        public ReadOnlySpan<T> Span => m_memory.Span;

        /// <summary>
        /// Returns the length of the array
        /// </summary>
        [JsonIgnore]
        public int Count => m_memory.Length;

        /// <summary>
        /// Returns true if the array is empty. This is the case
        /// for null and default arrays.
        /// </summary>
        [JsonIgnore]
        public bool IsEmpty => m_memory.IsEmpty;

        /// <summary>
        /// Returns true if the array is equivalent to null.
        /// Note that this is different from empty array state
        /// and reflects the difference between default and null
        /// for a nullable type in OPC UA.
        /// </summary>
        [JsonIgnore]
        public bool IsNull
            => ReadOnlyMemoryHelper.IsNull(in m_memory);

        /// <inheritdoc/>
        public T this[int index] => m_memory.Span[index];

        /// <inheritdoc/>
        [JsonConstructor]
        public ArrayOf(ReadOnlyMemory<T> values)
        {
            m_memory = values;
        }

        /// <inheritdoc/>
        internal ArrayOf(T[] values)
            : this(values.AsMemory())
        {
        }

        /// <inheritdoc/>
        public override bool Equals(object? obj)
        {
            return obj switch
            {
                null => IsEmpty,
                T[] other => Equals(other),
                ReadOnlyMemory<T> other => Equals(other),
                ArrayOf<T> arrayOf => Equals(arrayOf),
                IEnumerable<T> enumerable => Equals(enumerable),
                _ => false
            };
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            if (IsNull)
            {
                return "default";
            }
            if (IsEmpty)
            {
                return "[]";
            }
            StringBuilder builder = new StringBuilder()
                .Append('[')
                .Append(' ');
            for (int i = 0; i < m_memory.Length; i++)
            {
                builder = builder.Append(m_memory.Span[i]).Append(' ');
            }
            return builder.Append(']').ToString();
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            return GetHashCode(EqualityComparer<T>.Default);
        }

        /// <inheritdoc/>
        public int GetHashCode(IEqualityComparer<T> comparer)
        {
            var hashCode = new HashCode();
            for (int i = 0; i < m_memory.Length; i++)
            {
                hashCode.Add(m_memory.Span[i], comparer);
            }
            return hashCode.ToHashCode();
        }

        /// <inheritdoc/>
        public bool Equals(IEnumerable<T>? other, IEqualityComparer<T> comparer)
        {
            if (other == null)
            {
                return m_memory.IsEmpty;
            }
            using IEnumerator<T> enumerator = other.GetEnumerator();
            for (int i = 0; i < m_memory.Length; i++)
            {
                if (!enumerator.MoveNext() ||
                    !comparer.Equals(enumerator.Current, m_memory.Span[i]))
                {
                    return false;
                }
            }
            return !enumerator.MoveNext(); // enumerator should be done too
        }

        /// <inheritdoc/>
        public bool Equals(T[]? other, IEqualityComparer<T> comparer)
        {
            if (other is null)
            {
                return m_memory.IsEmpty;
            }
            if (m_memory.IsEmpty)
            {
                return other.Length == 0;
            }
#if !DEBUG && NET8_0_OR_GREATER
            return m_memory.Span.SequenceEqual(other.AsSpan(), comparer);
#else
            if (Memory.Length != other.Length)
            {
                return false;
            }
            for (int i = 0; i < m_memory.Length; i++)
            {
                if (!comparer.Equals(other[i], m_memory.Span[i]))
                {
                    return false;
                }
            }
            return true;
#endif
        }

        /// <inheritdoc/>
        public bool Equals(in MatrixOf<T> other, IEqualityComparer<T> comparer)
        {
            return other.Equals(in this, comparer);
        }

        /// <inheritdoc/>
        public bool Equals(in ArrayOf<T> other, IEqualityComparer<T> comparer)
        {
            if (IsNull || other.IsNull)
            {
                return IsNull && other.IsNull;
            }
            return IsEmpty ? other.IsEmpty : Equals(other.m_memory.Span, comparer);
        }

        /// <inheritdoc/>
        public bool Equals(in ReadOnlyMemory<T> other, IEqualityComparer<T> comparer)
        {
            return IsEmpty ? other.IsEmpty : Equals(other.Span, comparer);
        }

        /// <inheritdoc/>
        public bool Equals(ReadOnlySpan<T> other, IEqualityComparer<T> comparer)
        {
            if (IsEmpty)
            {
                return other.IsEmpty;
            }
#if !DEBUG && NET8_0_OR_GREATER
            return m_memory.Span.SequenceEqual(other, comparer);
#else
            if (m_memory.Length != other.Length)
            {
                return false;
            }
            for (int i = 0; i < m_memory.Length; i++)
            {
                if (!comparer.Equals(m_memory.Span[i], other[i]))
                {
                    return false;
                }
            }
            return true;
#endif
        }

        /// <inheritdoc/>
        public bool Equals(IEnumerable<T>? other)
        {
            return Equals(other, EqualityComparer<T>.Default);
        }

        /// <inheritdoc/>
        public bool Equals(T[]? other)
        {
            return Equals(other, EqualityComparer<T>.Default);
        }

        /// <inheritdoc/>
        public bool Equals(ReadOnlyMemory<T> other)
        {
            return Equals(in other, EqualityComparer<T>.Default);
        }

        /// <inheritdoc/>
        public bool Equals(ReadOnlySpan<T> other)
        {
            return Equals(other, EqualityComparer<T>.Default);
        }

        /// <inheritdoc/>
        public bool Equals(Span<T> other)
        {
            return Equals(other, EqualityComparer<T>.Default);
        }

        /// <inheritdoc/>
        public bool Equals(ArrayOf<T> other)
        {
            return Equals(in other, EqualityComparer<T>.Default);
        }

        /// <inheritdoc/>
        public bool Equals(MatrixOf<T> other)
        {
            return Equals(in other, EqualityComparer<T>.Default);
        }

        /// <inheritdoc/>
        public static bool operator ==(in ArrayOf<T> left, T[] right)
        {
            return left.Equals(right);
        }

        /// <inheritdoc/>
        public static bool operator !=(in ArrayOf<T> left, T[] right)
        {
            return !(left == right);
        }

        /// <inheritdoc/>
        public static bool operator ==(in ArrayOf<T> left, in ArrayOf<T> right)
        {
            return left.Equals(right);
        }

        /// <inheritdoc/>
        public static bool operator !=(in ArrayOf<T> left, in ArrayOf<T> right)
        {
            return !(left == right);
        }

        /// <inheritdoc/>
        public static bool operator ==(in ArrayOf<T> left, in MatrixOf<T> right)
        {
            return left.Equals(right);
        }

        /// <inheritdoc/>
        public static bool operator !=(in ArrayOf<T> left, in MatrixOf<T> right)
        {
            return !(left == right);
        }

        /// <inheritdoc/>
        public static bool operator ==(in ArrayOf<T> left, in ReadOnlyMemory<T> right)
        {
            return left.Equals(right);
        }

        /// <inheritdoc/>
        public static bool operator !=(in ArrayOf<T> left, in ReadOnlyMemory<T> right)
        {
            return !(left == right);
        }

        /// <inheritdoc/>
        public static bool operator ==(in ArrayOf<T> left, in ReadOnlySpan<T> right)
        {
            return left.Equals(right);
        }

        /// <inheritdoc/>
        public static bool operator !=(in ArrayOf<T> left, in ReadOnlySpan<T> right)
        {
            return !(left == right);
        }

        /// <inheritdoc/>
        public ReadOnlySpan<T>.Enumerator GetEnumerator()
        {
            return m_memory.Span.GetEnumerator();
        }

        /// <inheritdoc/>
        public static implicit operator ArrayOf<T>(T[] array)
        {
            return new(array);
        }

        /// <inheritdoc/>
        public static implicit operator ArrayOf<T>(List<T> list)
        {
            return FromList(list);
        }

#if ENABLE_WHEN_COLLECTIONS_REMOVED
        /// <inheritdoc/>
        public static explicit operator List<T>(ArrayOf<T> array)
        {
            return array.ToList();
        }
#endif

        /// <inheritdoc/>
        public static explicit operator T[]?(ArrayOf<T> array)
        {
            return array.ToArray();
        }

        /// <inheritdoc/>
        public static explicit operator MatrixOf<T>(ArrayOf<T> array)
        {
            return array.ToMatrix();
        }

        /// <inheritdoc/>
        public ArrayOf<T> Slice(int start, int length)
        {
            return new(m_memory.Slice(start, length));
        }

        /// <inheritdoc/>
        public ArrayOf<T> Slice(int start)
        {
            return new(m_memory[start..]);
        }

        /// <inheritdoc/>
        public ArrayOf<T> SafeSlice(int start, int length)
        {
            if (start >= Count)
            {
                return Empty;
            }
            if (start + length > Count)
            {
                length = Count - start;
            }
            if (length <= 0)
            {
                return Empty;
            }
            return new(m_memory.Slice(start, length));
        }

        /// <inheritdoc/>
        public ArrayOf<T> SafeSlice(int start)
        {
            if (start >= Count)
            {
                return Empty;
            }
            return new(m_memory[start..]);
        }

        /// <inheritdoc/>
        Array? IConvertableToArray.ToArray()
        {
            return ToArray();
        }

        /// <summary>
        /// Get as typed one dimensional array
        /// </summary>
        /// <returns></returns>
        public T[]? ToArray()
        {
            return IsNull ? null : m_memory.ToArray();
        }

        /// <summary>
        /// Get as typed one dimensional list for manipulation
        /// Allocates a list and fills it. Does not return null
        /// when the array is null, but an empty list.
        /// </summary>
        /// <returns></returns>
        public List<T> ToList()
        {
            if (IsEmpty)
            {
                return [];
            }
            var newList = new List<T>(m_memory.Length);
            for (int i = 0; i < m_memory.Length; i++)
            {
                newList.Add(m_memory.Span[i]);
            }
            return newList;
        }

        /// <summary>
        /// Return a array of the type of the list element with
        /// the elements of the list
        /// </summary>
        /// <returns></returns>
        internal static ArrayOf<T> FromList(List<T>? list)
        {
            return list == null ? default : list.Count == 0 ? [] : new(list.ToArray());
        }

        /// <summary>
        /// Redimensionate an array of T into a matrix
        /// </summary>
        /// <param name="dimensions"></param>
        /// <returns></returns>
        public MatrixOf<T> ToMatrix(params int[] dimensions)
        {
            return new(m_memory, dimensions);
        }

        /// <summary>
        /// Redimensionate an array of T into a matrix
        /// </summary>
        /// <param name="dimensions"></param>
        /// <returns></returns>
        public MatrixOf<T> ToMatrix(ArrayOf<int> dimensions)
        {
            return new(m_memory, dimensions.ToArray() ?? [m_memory.Length]);
        }

        /// <summary>
        /// Convert to one dimensional Matrix of T
        /// </summary>
        /// <returns></returns>
        public MatrixOf<T> ToMatrix()
        {
            return new(m_memory, [m_memory.Length]);
        }

        /// <summary>
        /// Add an item to this array to produce a new array. The item
        /// is added at the end of the array. The method is not named
        /// Add because it produces a new array and does not change the
        /// existing one.
        /// </summary>
        [Pure]
        public ArrayOf<T> AddItem(T value)
        {
            T[] buffer = new T[Count + 1];
            Span<T> dest = buffer.AsSpan();
            Span.CopyTo(dest);
            dest[Count] = value;
            return buffer.ToArrayOf();
        }

        /// <summary>
        /// Add an item to this array to produce a new array. The item
        /// is added at the specific index moving the existing items at
        /// and after the index one position. If index is equal to count,
        /// the item is added at the end of the array.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        [Pure]
        public ArrayOf<T> AddItem(T value, int index)
        {
            if (index < 0 || index > Count)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }
            if (index == Count)
            {
                return AddItem(value);
            }
            T[] buffer = new T[Count + 1];
            Span<T> target = buffer.AsSpan();
            if (index == 0)
            {
                buffer[0] = value;
                Span.CopyTo(target[1..]);
            }
            else
            {
                Span[..index].CopyTo(target[..index]);
                buffer[index] = value;
                Span[index..].CopyTo(target[(index + 1)..]);
            }
            return buffer.ToArrayOf();
        }

        /// <summary>
        /// Replace an item in this array to produce a new array. The item
        /// is replace at the specific index without moving the existing
        /// items.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        [Pure]
        public ArrayOf<T> ReplaceItem(T value, int index)
        {
            if (index < 0 || index > Count)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }
            T[] buffer = new T[Count];
            Span.CopyTo(buffer);
            buffer[index] = value;
            return buffer.ToArrayOf();
        }

        /// <summary>
        /// Replace items in this array to produce a new array. The
        /// items are added from the provided offset
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        [Pure]
        public ArrayOf<T> ReplaceItems(ArrayOf<T> value, int index)
        {
            if (index < 0 || index + value.Count > Count)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }
            T[] buffer = new T[Count];
            Span.CopyTo(buffer);
            value.Span.CopyTo(buffer.AsSpan(index));
            return buffer.ToArrayOf();
        }

        /// <summary>
        /// Filter out the value and return an array without it.
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        public ArrayOf<T> RemoveItem(T value)
        {
            return Filter(item => !EqualityComparer<T>.Default.Equals(item, value));
        }

        /// <summary>
        /// Add all items to this array to produce a new array
        /// </summary>
        [Pure]
        public ArrayOf<T> AddItems(ArrayOf<T> values)
        {
            return ArrayOf.Combine(this, values);
        }

        /// <summary>
        /// Add all items to this array to produce a new array
        /// </summary>
        [Pure]
        public ArrayOf<T> AddItems(IEnumerable<T> values)
        {
            return ArrayOf.Combine(this, values.ToArrayOf());
        }

        /// <inheritdoc/>
        public static ArrayOf<T> operator +(in ArrayOf<T> left, T right)
        {
            return left.AddItem(right);
        }

        /// <inheritdoc/>
        public static ArrayOf<T> operator +(T left, in ArrayOf<T> right)
        {
            return right.AddItem(left, 0);
        }

        /// <inheritdoc/>
        public static ArrayOf<T> operator +(in ArrayOf<T> left, ArrayOf<T> right)
        {
            return left.AddItems(right);
        }

        /// <summary>
        /// Transform
        /// </summary>
        /// <typeparam name="TResult"></typeparam>
        public ArrayOf<TResult> ConvertAll<TResult>(Func<T, TResult> transform)
        {
            var values = new TResult[m_memory.Length];
            for (int i = 0; i < m_memory.Length; i++)
            {
                values[i] = transform(m_memory.Span[i]);
            }
            return values.ToArrayOf();
        }

        /// <summary>
        /// Filter the array and return an array with the
        /// false items removed.
        /// </summary>
        public ArrayOf<T> Filter(Func<T, bool> filter)
        {
            var values = new T[m_memory.Length];
            int j = 0;
            for (int i = 0; i < m_memory.Length; i++)
            {
                if (filter(m_memory.Span[i]))
                {
                    values[j++] = m_memory.Span[i];
                }
            }
            if (j == 0)
            {
                return Empty;
            }
            return new ArrayOf<T>(values.AsMemory()[..j]);
        }

        /// <summary>
        /// Find index of item
        /// </summary>
        public int FindIndex(Predicate<T> predicate)
        {
            if (MemoryMarshal.TryGetArray(m_memory, out ArraySegment<T> segment))
            {
                return Array.FindIndex(
                    segment.Array!,
                    segment.Offset,
                    segment.Count,
                    predicate);
            }
            for (int i = 0; i < m_memory.Length; i++)
            {
                if (predicate(m_memory.Span[i]))
                {
                    return i;
                }
            }
            return -1;
        }

        /// <summary>
        /// Find index of item
        /// </summary>
        public int IndexOf(T value)
        {
#if NET10_0_OR_GREATER
            return m_memory.Span.IndexOf(value);
#else
            if (MemoryMarshal.TryGetArray(m_memory, out ArraySegment<T> segment))
            {
                return Array.IndexOf(segment.Array!, value, segment.Offset, segment.Count);
            }
            for (int i = 0; i < m_memory.Length; i++)
            {
                if (EqualityComparer<T>.Default.Equals(value, m_memory.Span[i]))
                {
                    return i;
                }
            }
            return -1;
#endif
        }

        /// <summary>
        /// Find index of item
        /// </summary>
        public int IndexOf(T value, IEqualityComparer<T> comparer)
        {
#if NET10_0_OR_GREATER
            return m_memory.Span.IndexOf(value, comparer);
#else
            for (int i = 0; i < m_memory.Length; i++)
            {
                if (comparer.Equals(value, m_memory.Span[i]))
                {
                    return i;
                }
            }
            return -1;
#endif
        }

        /// <summary>
        /// Find item
        /// </summary>
        public T Find(Predicate<T> predicate, T defaultValue = default!)
        {
            int idx = FindIndex(predicate);
            return idx >= 0 ? m_memory.Span[idx] : defaultValue;
        }

        /// <summary>
        /// Returns true if array contains the value
        /// </summary>
        public bool Contains(T value)
        {
            return IndexOf(value) != -1;
        }

        /// <summary>
        /// Returns true if array contains the value
        /// </summary>
        public bool Contains(T value, IEqualityComparer<T> comparer)
        {
            return IndexOf(value, comparer) != -1;
        }

        /// <summary>
        /// Returns true if array contains the value
        /// </summary>
        public bool Contains(Predicate<T> predicate)
        {
            return FindIndex(predicate) != -1;
        }

        /// <summary>
        /// Call for each value
        /// </summary>
        /// <param name="value"></param>
        public void ForEach(Action<T> value)
        {
            for (int i = 0; i < m_memory.Length; i++)
            {
                value(m_memory.Span[i]);
            }
        }

#pragma warning disable IDE0032 // Use auto property
        private readonly ReadOnlyMemory<T> m_memory;
#pragma warning restore IDE0032 // Use auto property
    }

    /// <summary>
    /// Marks types as convertable to <see cref="Array"/>
    /// </summary>
    public interface IConvertableToArray
    {
        /// <summary>
        /// Convert to array
        /// </summary>
        /// <returns></returns>
        Array? ToArray();
    }

    /// <summary>
    /// Collection builder for array of and accessor for dimensions
    /// </summary>
    public static class ArrayOf
    {
        /// <summary>
        /// Empty array
        /// </summary>
        /// <typeparam name="T"></typeparam>
        public static ArrayOf<T> Empty<T>()
        {
            return [];
        }

        /// <summary>
        /// Null (default) array
        /// </summary>
        /// <typeparam name="T"></typeparam>
        public static ArrayOf<T> Null<T>()
        {
            return default;
        }

        /// <summary>
        /// Create array
        /// </summary>
        /// <param name="memory"></param>
        /// <returns></returns>
        /// <typeparam name="T"></typeparam>
        public static ArrayOf<T> Create<T>(ReadOnlySpan<T> memory)
        {
            return Wrapped(memory.ToArray());
        }

        /// <summary>
        /// Create array - do not remove - it is used in the test
        /// code to create mocked array of instances using reflection.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        public static ArrayOf<T> Wrapped<T>(params T[] memory)
        {
            return new(memory.AsMemory());
        }

        /// <summary>
        /// Create an array of from the array type
        /// </summary>
        /// <typeparam name="T"></typeparam>
        public static ArrayOf<T> From<T>(Array array)
        {
            return MatrixOf<T>.CreateFromArray(array).ToArrayOf();
        }

        /// <summary>
        /// Create array of T
        /// </summary>
        /// <typeparam name="T"></typeparam>
        public static ArrayOf<T> ToArrayOf<T>(this ReadOnlyMemory<T> values)
        {
            return values.IsEmpty ? [] : new(values);
        }

        /// <summary>
        /// Create array of T
        /// </summary>
        /// <typeparam name="T"></typeparam>
        public static ArrayOf<T> ToArrayOf<T>(
            this System.Collections.IEnumerable? values)
        {
            return (values?.Cast<T>()).ToArrayOf();
        }

        /// <summary>
        /// Create array of chars
        /// </summary>
        public static ArrayOf<char> ToArrayOf(this string? values)
        {
            return (values?.ToCharArray().ToArrayOf()) ?? default;
        }

        /// <summary>
        /// Create array of chars
        /// </summary>
        public static string? ToString(this ArrayOf<char> values)
        {
            return values.IsNull ? null : new string(
#if NET8_0_OR_GREATER
                values.Span
#else
                values.ToArray()
#endif
                );
        }

        /// <summary>
        /// Create array of T
        /// </summary>
        /// <typeparam name="T"></typeparam>
        public static ArrayOf<T> ToArrayOf<T>(this IEnumerable<T>? values)
        {
            if (values == null)
            {
                return default;
            }
#if NET8_0_OR_GREATER
            if (values.TryGetNonEnumeratedCount(out int count))
            {
                if (count == 0)
                {
                    return ArrayOf<T>.Empty;
                }
                var copy = new T[count];
                int index = 0;
                foreach (T item in values)
                {
                    copy[index++] = item;
                }
                return new(copy);
            }
#endif
#pragma warning disable RCS1151 // Cast explicit to avoid covariant conversion
            return new([.. values.Cast<T>()]);
#pragma warning restore RCS1151 // Cast explicit to avoid covariant conversion
        }

        /// <summary>
        /// Create array of T
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <typeparam name="TResult"></typeparam>
        public static ArrayOf<TResult> ToArrayOf<T, TResult>(
            this IEnumerable<T>? values,
            Func<T, TResult> predicate)
        {
            return ToArrayOf(values?.Select(predicate));
        }

        /// <summary>
        /// Create array of T
        /// </summary>
        /// <typeparam name="T"></typeparam>
        public static ArrayOf<T> ToArrayOf<T>(this T[]? values)
        {
            return values == null ? default : values.Length == 0 ? [] : new(values);
        }

        /// <summary>
        /// Add array as range to list
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <exception cref="ArgumentNullException"><paramref name="list"/> is <c>null</c>.</exception>
        public static void AddRange<T>(this IList<T> list, ArrayOf<T> values)
        {
            if (list == null)
            {
                throw new ArgumentNullException(nameof(list));
            }
            for (int i = 0; i < values.Count; i++)
            {
                list.Add(values.Span[i]);
            }
        }

        /// <summary>
        /// Returns batches of a arrays
        /// </summary>
        /// <typeparam name="T">The type of the items in the arrays.</typeparam>
        /// <param name="collection">The arrays from which items are batched.</param>
        /// <param name="batchSize">The size of a batch.</param>
        /// <returns>The arrays.</returns>
        public static IEnumerable<ArrayOf<T>> Batch<T>(
            this ArrayOf<T> collection,
            int batchSize)
        {
            if (collection.Count <= batchSize || batchSize == 0)
            {
                yield return collection;
            }
            else // Slice the array
            {
                for (int ii = 0; ii < collection.Count; ii += batchSize)
                {
                    if (batchSize >= collection.Count - ii)
                    {
                        // Return remaining slice
                        yield return collection[ii..];
                    }
                    else
                    {
                        yield return collection.Slice(ii, batchSize);
                    }
                }
            }
        }

        /// <summary>
        /// Transform
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <typeparam name="TResult"></typeparam>
        public static ArrayOf<TResult> ToArrayOf<T, TResult>(
            this ArrayOf<T> value,
            Func<T, TResult> transform)
        {
            var values = new TResult[value.Memory.Length];
            for (int i = 0; i < value.Memory.Length; i++)
            {
                values[i] = transform(value.Memory.Span[i]);
            }
            return values.ToArrayOf();
        }

        /// <summary>
        /// Check whether count exceeds limit. 0 means unlimitted.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        public static bool Exceeds<T>(this ArrayOf<T> array, uint limit)
        {
            return limit != 0 && array.Count > limit;
        }

        /// <summary>
        /// Combine to new array
        /// </summary>
        /// <typeparam name="T"></typeparam>
        public static ArrayOf<T> Combine<T>(params ArrayOf<T>[] arrays)
        {
            if (arrays.Length == 0)
            {
                return [];
            }
            int length = 0;
            foreach (ArrayOf<T> item in arrays)
            {
                length += item.Count;
            }
            if (length == 0)
            {
                return [];
            }
            T[] buffer = new T[length];
            Span<T> dest = buffer.AsSpan();
            foreach (ArrayOf<T> item in arrays)
            {
                item.Span.CopyTo(dest[..item.Count]);
                if (dest.Length == item.Count)
                {
                    break;
                }
                dest = dest[item.Count..];
            }
            return buffer.ToArrayOf();
        }
    }
}
