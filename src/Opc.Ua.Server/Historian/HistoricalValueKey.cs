/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Collections.Generic;

namespace Opc.Ua.Server.Historian
{
    /// <summary>
    /// Stable composite identity of a single entry in a historical
    /// archive.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Ordinary raw history (Part 11 §5.2.4) is unique by
    /// <c>SourceTimestamp</c>, so <see cref="UniquenessKey"/> is empty and
    /// the key degenerates to the source timestamp.
    /// </para>
    /// <para>
    /// StructuredHistoryData (Part 11 §6.8.3) may store more than one
    /// entry at the same <c>SourceTimestamp</c>. Such archives add the
    /// canonical uniqueness key produced by an
    /// <see cref="IHistorianStructuredDataKeySelector"/> so every entry
    /// keeps a stable identity across insert, replace, update and remove.
    /// </para>
    /// <para>
    /// The key defines a total order: source timestamp first, then an
    /// ordinal comparison of the uniqueness key bytes. That order is the
    /// iteration order of raw reads and therefore also the order used by
    /// exclusive paging cursors — two entries that share a timestamp can
    /// never be lost or duplicated across a page boundary.
    /// </para>
    /// </remarks>
    public readonly record struct HistoricalValueKey(
        DateTimeUtc SourceTimestamp,
        ByteString UniquenessKey) : IComparable<HistoricalValueKey>
    {
        /// <summary>
        /// Creates a key for ordinary raw history, which is unique by
        /// source timestamp alone.
        /// </summary>
        public static HistoricalValueKey FromTimestamp(DateTimeUtc sourceTimestamp)
        {
            return new HistoricalValueKey(sourceTimestamp, ByteString.Empty);
        }

        /// <summary>
        /// True when the key carries a structured uniqueness key in
        /// addition to the source timestamp.
        /// </summary>
        public bool IsStructured => !UniquenessKey.IsEmpty;

        /// <inheritdoc/>
        public int CompareTo(HistoricalValueKey other)
        {
            int result = SourceTimestamp.CompareTo(other.SourceTimestamp);
            return result != 0 ? result : UniquenessKey.CompareTo(other.UniquenessKey);
        }

        /// <summary>
        /// Compares two keys for ordering.
        /// </summary>
        public static bool operator <(HistoricalValueKey left, HistoricalValueKey right)
        {
            return left.CompareTo(right) < 0;
        }

        /// <summary>
        /// Compares two keys for ordering.
        /// </summary>
        public static bool operator <=(HistoricalValueKey left, HistoricalValueKey right)
        {
            return left.CompareTo(right) <= 0;
        }

        /// <summary>
        /// Compares two keys for ordering.
        /// </summary>
        public static bool operator >(HistoricalValueKey left, HistoricalValueKey right)
        {
            return left.CompareTo(right) > 0;
        }

        /// <summary>
        /// Compares two keys for ordering.
        /// </summary>
        public static bool operator >=(HistoricalValueKey left, HistoricalValueKey right)
        {
            return left.CompareTo(right) >= 0;
        }
    }

    /// <summary>
    /// Explicit ordering and equality for <see cref="HistoricalValueKey"/>.
    /// </summary>
    /// <remarks>
    /// Sorted archives must be created with this comparer so the storage
    /// order matches the order assumed by paging cursors.
    /// </remarks>
    public sealed class HistoricalValueKeyComparer :
        IComparer<HistoricalValueKey>,
        IEqualityComparer<HistoricalValueKey>
    {
        /// <summary>
        /// The shared comparer instance.
        /// </summary>
        public static HistoricalValueKeyComparer Instance { get; } = new();

        /// <inheritdoc/>
        public int Compare(HistoricalValueKey x, HistoricalValueKey y)
        {
            return x.CompareTo(y);
        }

        /// <inheritdoc/>
        public bool Equals(HistoricalValueKey x, HistoricalValueKey y)
        {
            return x.CompareTo(y) == 0;
        }

        /// <inheritdoc/>
        public int GetHashCode(HistoricalValueKey obj)
        {
            // A null and an empty uniqueness key are the same key: both
            // hash the empty span.
            return HashCode.Combine(obj.SourceTimestamp, obj.UniquenessKey);
        }
    }
}
