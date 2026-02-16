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

#nullable enable

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;

namespace Opc.Ua
{
    /// <summary>
    /// A matrix is an <see cref="ArrayOf{T}"/> with dimensions.
    /// A matrix can also be re-interpreted as <see cref="ArrayOf{T}"/>
    /// but not vice versa due to the missing dimension
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public readonly struct MatrixOf<T> :
        IEquatable<MatrixOf<T>>,
        IEquatable<Array>,
        IEquatable<ArrayOf<T>>
    {
        /// <summary>
        /// Empty matrix
        /// </summary>
        public static readonly MatrixOf<T> Empty
            = new(ReadOnlyMemory<T>.Empty, [0]);

        /// <summary>
        /// Get values
        /// </summary>
#pragma warning disable RCS1085 // Use auto-implemented property
        public ReadOnlyMemory<T> Memory => m_memory;
#pragma warning restore RCS1085 // Use auto-implemented property

        /// <summary>
        /// Array as span
        /// </summary>
        public ReadOnlySpan<T> Span => m_memory.Span;

        /// <summary>
        /// Dimensions
        /// </summary>
#pragma warning disable RCS1085 // Use auto-implemented property
        public int[] Dimensions => m_dimensions;
#pragma warning restore RCS1085 // Use auto-implemented property

        /// <summary>
        /// Length
        /// </summary>
        public int Count => m_memory.Length;

        /// <summary>
        /// Is empty array
        /// </summary>
        public bool IsEmpty => m_memory.IsEmpty;

        /// <summary>
        /// Create array adapter
        /// </summary>
        /// <param name="array"></param>
        private MatrixOf(Array array)
        {
            // Get reverse dimensions
            // TODO: var dimensions = Dimensions.Reverse().ToArray();
            int[] dimensions = new int[array.Rank];
            for (int rank = array.Rank - 1; rank >= 0; rank--)
            {
                dimensions[rank] = array.GetLength(array.Rank - rank - 1);
            }
            var values = new T[array.Length];
            int[] indexes = new int[array.Rank];
            for (int element = 0; element < array.Length; element++)
            {
                indexes[array.Rank - 1] = element % dimensions[0];
                for (int row = 1; row < array.Rank; row++)
                {
                    int multiplier = 1;
                    for (int i = 0; i < row; i++)
                    {
                        multiplier *= dimensions[i];
                    }
                    indexes[array.Rank - row - 1] =
                        element / multiplier % dimensions[row];
                }
                values[element] = (T)
                    (array.GetValue(indexes)
                        ?? throw new ArgumentException(
                            "array contains null",
                            nameof(array)));
            }
            m_memory = values;
            m_dimensions =
            [
                .. Enumerable
                    .Range(0, array.Rank)
                    .Select(array.GetLength)
            ];
        }

        /// <summary>
        /// Create matrix
        /// </summary>
        /// <param name="values"></param>
        /// <param name="dimensions"></param>
        internal MatrixOf(ReadOnlyMemory<T> values, int[] dimensions)
        {
            m_memory = values;
            // Validate dimensions
            if (dimensions.Length == 0)
            {
                throw new ArgumentException("Rank of zero not allowed as matrix.",
                    nameof(dimensions));
            }
            int length = dimensions.Length == 1 ?
                dimensions[0] :
                dimensions.Aggregate((a, b) => a * b);
            if (length != m_memory.Length)
            {
                throw new ArgumentException("The number of elements in the array " +
                    "does not match the provided dimensions.", nameof(dimensions));
            }
            m_dimensions = [.. dimensions];
        }

        /// <inheritdoc/>
        public override bool Equals(object? obj)
        {
            return obj switch
            {
                null => IsEmpty,
                MatrixOf<T> matrixOf => Equals(matrixOf),
                ArrayOf<T> arrayOf => Equals(arrayOf),
                Array array => Equals(array),
                _ => false
            };
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            // TODO: Format as matrix
            //
            StringBuilder builder = new StringBuilder()
                .Append(typeof(T).Name)
                .Append('[');
            for (int i = 0; i < m_memory.Length; i++)
            {
                builder = builder.Append(m_memory.Span[i]);
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
            foreach (int i in Dimensions)
            {
                hashCode.Add(i);
            }
            return hashCode.ToHashCode();
        }

        /// <inheritdoc/>
        public bool Equals(Array? other, IEqualityComparer<T> comparer)
        {
            if (other == null)
            {
                return m_memory.IsEmpty;
            }
            var m = new MatrixOf<T>(other);
            return Equals(in m, comparer);
        }

        /// <inheritdoc/>
        public bool Equals(in MatrixOf<T> other, IEqualityComparer<T> comparer)
        {
            return Equals(other.m_memory.Span, m_dimensions, comparer);
        }

        /// <inheritdoc/>
        public bool Equals(in ArrayOf<T> other, IEqualityComparer<T> comparer)
        {
            if (m_dimensions.Length != 1)
            {
                return false;
            }
            return Equals(other.Span, m_dimensions, comparer);
        }

        /// <inheritdoc/>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool Equals(
            ReadOnlySpan<T> other,
            int[] dim,
            IEqualityComparer<T> comparer)
        {
            if (IsEmpty)
            {
                return other.IsEmpty;
            }
            if (!dim.SequenceEqual(m_dimensions))
            {
                return false;
            }
#if !DEBUG && NET8_0_OR_GREATER
            return m_memory.Span.SequenceEqual(other, comparer);
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
        public bool Equals(Array? other)
        {
            return Equals(other, EqualityComparer<T>.Default);
        }

        /// <inheritdoc/>
        public bool Equals(MatrixOf<T> other)
        {
            return Equals(in other, EqualityComparer<T>.Default);
        }

        /// <inheritdoc/>
        public bool Equals(ArrayOf<T> other)
        {
            return Equals(in other, EqualityComparer<T>.Default);
        }

        /// <inheritdoc/>
        public static bool operator ==(in MatrixOf<T> left, in MatrixOf<T> right)
        {
            return left.Equals(right);
        }

        /// <inheritdoc/>
        public static bool operator !=(in MatrixOf<T> left, in MatrixOf<T> right)
        {
            return !(left == right);
        }

        /// <inheritdoc/>
        public static bool operator ==(in MatrixOf<T> left, in ArrayOf<T> right)
        {
            return left.Equals(right);
        }

        /// <inheritdoc/>
        public static bool operator !=(in MatrixOf<T> left, in ArrayOf<T> right)
        {
            return !(left == right);
        }

        /// <inheritdoc/>
        public static bool operator ==(in MatrixOf<T> left, Array right)
        {
            return left.Equals(right);
        }

        /// <inheritdoc/>
        public static bool operator !=(in MatrixOf<T> left, Array right)
        {
            return !(left == right);
        }

        /// <inheritdoc/>
        public ReadOnlySpan<T>.Enumerator GetEnumerator()
        {
            return m_memory.Span.GetEnumerator();
        }

        /// <inheritdoc/>
        public static explicit operator Array(MatrixOf<T> array)
        {
            return array.CreateArrayInstance();
        }

        /// <inheritdoc/>
        public static implicit operator MatrixOf<T>(T[,] array)
        {
            return new(array);
        }

        /// <inheritdoc/>
        public static explicit operator T[,](MatrixOf<T> array)
        {
            return (T[,])array.CreateArrayInstance();
        }

        /// <inheritdoc/>
        public static implicit operator MatrixOf<T>(T[,,] array)
        {
            return new(array);
        }

        /// <inheritdoc/>
        public static explicit operator T[,,](MatrixOf<T> array)
        {
            return (T[,,])array.CreateArrayInstance();
        }

        /// <inheritdoc/>
        public static implicit operator MatrixOf<T>(T[,,,] array)
        {
            return new(array);
        }

        /// <inheritdoc/>
        public static explicit operator T[,,,](MatrixOf<T> array)
        {
            return (T[,,,])array.CreateArrayInstance();
        }

        /// <inheritdoc/>
        public static implicit operator MatrixOf<T>(T[,,,,] array)
        {
            return new(array);
        }

        /// <inheritdoc/>
        public static explicit operator T[,,,,](MatrixOf<T> array)
        {
            return (T[,,,,])array.CreateArrayInstance();
        }

        /// <inheritdoc/>
        public static implicit operator MatrixOf<T>(T[,,,,,] array)
        {
            return new(array);
        }

        /// <inheritdoc/>
        public static explicit operator T[,,,,,](MatrixOf<T> array)
        {
            return (T[,,,,,])array.CreateArrayInstance();
        }

        /// <inheritdoc/>
        public static implicit operator MatrixOf<T>(T[,,,,,,] array)
        {
            return new(array);
        }

        /// <inheritdoc/>
        public static explicit operator T[,,,,,,](MatrixOf<T> array)
        {
            return (T[,,,,,,])array.CreateArrayInstance();
        }

        /// <inheritdoc/>
        public static implicit operator MatrixOf<T>(T[,,,,,,,] array)
        {
            return new(array);
        }

        /// <inheritdoc/>
        public static explicit operator T[,,,,,,,](MatrixOf<T> array)
        {
            return (T[,,,,,,,])array.CreateArrayInstance();
        }

        /// <inheritdoc/>
        public static implicit operator MatrixOf<T>(T[,,,,,,,,] array)
        {
            return new(array);
        }

        /// <inheritdoc/>
        public static explicit operator T[,,,,,,,,](MatrixOf<T> array)
        {
            return (T[,,,,,,,,])array.CreateArrayInstance();
        }

        /// <inheritdoc/>
        public static implicit operator MatrixOf<T>(T[,,,,,,,,,] array)
        {
            return new(array);
        }

        /// <inheritdoc/>
        public static explicit operator T[,,,,,,,,,](MatrixOf<T> array)
        {
            return (T[,,,,,,,,,])array.CreateArrayInstance();
        }

        /// <summary>
        /// To array of
        /// </summary>
        /// <returns></returns>
        public ArrayOf<T> ToArrayOf()
        {
            return new ArrayOf<T>(m_memory);
        }

        /// <summary>
        /// Transform
        /// </summary>
        /// <typeparam name="TResult"></typeparam>
        /// <param name="transform"></param>
        /// <returns></returns>
        public MatrixOf<TResult> ConvertAll<TResult>(Func<T, TResult> transform)
        {
            var values = new TResult[m_memory.Length];
            for (int i = 0; i < m_memory.Length; i++)
            {
                values[i] = transform(m_memory.Span[i]);
            }
            return values.ToMatrixOf(m_dimensions);
        }

        /// <summary>
        /// Get as multidimensional array which is missing the type
        /// </summary>
        /// <returns>A multi dimensional array object</returns>
        public Array CreateArrayInstance()
        {
            int[] dim = m_dimensions;
            if (dim.Length <= 1)
            {
                return m_memory.ToArray();
            }
            System.Diagnostics.Debug.Assert(dim.Length > 1);
            var array = Array.CreateInstance(typeof(T), dim.ToArray());
            int[] indexes = new int[dim.Length];
            foreach (T? element in Memory.Span)
            {
                array.SetValue(element, indexes);
                for (int dimension = indexes.Length - 1; dimension >= 0; dimension--)
                {
                    indexes[dimension]++;
                    if (indexes[dimension] < dim[dimension])
                    {
                        break;
                    }
                    indexes[dimension] = 0;
                }
            }
            return array;
        }

        /// <summary>
        /// Create array
        /// </summary>
        /// <param name="array"></param>
        /// <returns></returns>
        public static MatrixOf<T> CreateFromArray(Array array)
        {
            return new(array);
        }

        private readonly ReadOnlyMemory<T> m_memory;
        private readonly int[] m_dimensions;
    }

    /// <summary>
    /// MatrixOf extensions
    /// </summary>
    public static class MatrixOf
    {
        /// <summary>
        /// Empty array
        /// </summary>
        /// <typeparam name="T"></typeparam>
        public static MatrixOf<T> Empty<T>()
        {
            return MatrixOf<T>.Empty;
        }

        /// <summary>
        /// Create array
        /// </summary>
        /// <typeparam name="T"></typeparam>
        public static MatrixOf<T> From<T>(Array array)
        {
            return MatrixOf<T>.CreateFromArray(array);
        }

        /// <summary>
        /// Create array of T
        /// </summary>
        /// <typeparam name="T"></typeparam>
        public static MatrixOf<T> ToMatrixOf<T>(
            this ReadOnlyMemory<T> values,
            int[] dimensions)
        {
            return new(values, dimensions);
        }

        /// <summary>
        /// Create array of T
        /// </summary>
        /// <typeparam name="T"></typeparam>
        public static MatrixOf<T> ToMatrixOf<T>(
            this IEnumerable<T> values,
            int[] dimensions)
        {
            return new(values.ToArray(), dimensions);
        }
    }
}
