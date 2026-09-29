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
using System.Collections;
using System.Collections.Generic;

namespace Opc.Ua.SourceGeneration
{
    /// <summary>
    /// A read-only list that compares by content.
    /// </summary>
    /// <remarks>
    /// Roslyn caches an incremental step by comparing its output with the
    /// previous run's output. A record carrying a freshly allocated array or
    /// list compares that member by reference, so the record is never equal
    /// to its previous value and every downstream step runs again. Carrying
    /// the list as this type instead keeps the record equatable by value,
    /// including when it is typed as <see cref="IReadOnlyList{T}"/>: a
    /// record's generated equality dispatches to <see cref="Equals(object)"/>.
    /// </remarks>
    internal sealed class EquatableArray<T> : IReadOnlyList<T>, IEquatable<EquatableArray<T>>
    {
        /// <summary>
        /// The empty list.
        /// </summary>
        public static readonly EquatableArray<T> Empty = new([]);

        /// <summary>
        /// Wrap the items. The array is owned by the new instance and must
        /// not be modified afterwards.
        /// </summary>
        public EquatableArray(T[] items)
        {
            m_items = items ?? [];
        }

        /// <summary>
        /// Copy the items into a new list.
        /// </summary>
        public static EquatableArray<T> From(IEnumerable<T> items)
        {
            return items == null ? Empty : new EquatableArray<T>([.. items]);
        }

        /// <inheritdoc/>
        public T this[int index] => m_items[index];

        /// <inheritdoc/>
        public int Count => m_items.Length;

        /// <inheritdoc/>
        public bool Equals(EquatableArray<T> other)
        {
            if (other is null)
            {
                return false;
            }
            if (ReferenceEquals(this, other))
            {
                return true;
            }
            if (m_items.Length != other.m_items.Length)
            {
                return false;
            }
            EqualityComparer<T> comparer = EqualityComparer<T>.Default;
            for (int ii = 0; ii < m_items.Length; ii++)
            {
                if (!comparer.Equals(m_items[ii], other.m_items[ii]))
                {
                    return false;
                }
            }
            return true;
        }

        /// <inheritdoc/>
        public override bool Equals(object obj)
        {
            return Equals(obj as EquatableArray<T>);
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            int hash = 17;
            foreach (T item in m_items)
            {
                hash = (hash * 31) + (item?.GetHashCode() ?? 0);
            }
            return hash;
        }

        /// <inheritdoc/>
        public IEnumerator<T> GetEnumerator()
        {
            return ((IEnumerable<T>)m_items).GetEnumerator();
        }

        /// <inheritdoc/>
        IEnumerator IEnumerable.GetEnumerator()
        {
            return m_items.GetEnumerator();
        }

        private readonly T[] m_items;
    }
}
