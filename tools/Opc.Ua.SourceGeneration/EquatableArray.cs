// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

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
