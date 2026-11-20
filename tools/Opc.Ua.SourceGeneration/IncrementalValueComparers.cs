/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System.Collections.Generic;
using System.Collections.Immutable;

namespace Opc.Ua.SourceGeneration
{
    /// <summary>
    /// Value comparers for the collection types the incremental pipeline carries
    /// between stages.
    /// </summary>
    /// <remarks>
    /// Roslyn caches an incremental step's output by comparing it with the
    /// previous run's output. <see cref="ImmutableArray{T}"/> and
    /// <see cref="ImmutableHashSet{T}"/> compare by reference, so a stage
    /// derived from the compilation produces an "unequal" result on every
    /// keystroke even when nothing it reads has changed, and the whole model
    /// generation runs again. Supplying these comparers lets the cache hit.
    /// </remarks>
    internal static class IncrementalValueComparers
    {
        /// <summary>
        /// Compares two immutable arrays element by element.
        /// </summary>
        public static IEqualityComparer<ImmutableArray<T>> ForArray<T>()
        {
            return ImmutableArrayComparer<T>.Instance;
        }

        /// <summary>
        /// Compares two immutable sets by content.
        /// </summary>
        public static IEqualityComparer<ImmutableHashSet<T>> ForSet<T>()
        {
            return ImmutableHashSetComparer<T>.Instance;
        }

        private sealed class ImmutableArrayComparer<T> : IEqualityComparer<ImmutableArray<T>>
        {
            public static readonly ImmutableArrayComparer<T> Instance = new();

            public bool Equals(ImmutableArray<T> x, ImmutableArray<T> y)
            {
                if (x.IsDefault || y.IsDefault)
                {
                    return x.IsDefault && y.IsDefault;
                }
                if (x.Length != y.Length)
                {
                    return false;
                }
                for (int ii = 0; ii < x.Length; ii++)
                {
                    if (!EqualityComparer<T>.Default.Equals(x[ii], y[ii]))
                    {
                        return false;
                    }
                }
                return true;
            }

            public int GetHashCode(ImmutableArray<T> obj)
            {
                if (obj.IsDefaultOrEmpty)
                {
                    return 0;
                }
                int hash = 17;
                foreach (T item in obj)
                {
                    hash = (hash * 31) + (item?.GetHashCode() ?? 0);
                }
                return hash;
            }
        }

        private sealed class ImmutableHashSetComparer<T> : IEqualityComparer<ImmutableHashSet<T>>
        {
            public static readonly ImmutableHashSetComparer<T> Instance = new();

            public bool Equals(ImmutableHashSet<T> x, ImmutableHashSet<T> y)
            {
                if (x is null || y is null)
                {
                    return ReferenceEquals(x, y);
                }
                if (x.Count != y.Count)
                {
                    return false;
                }
                // Not SetEquals: it copies the whole other side into a fresh
                // HashSet before comparing, and this runs on every compilation
                // change over an index of every state type in the reference
                // closure. Both sides are sets of the same size, so containment
                // one way is equality.
                foreach (T item in y)
                {
                    if (!x.Contains(item))
                    {
                        return false;
                    }
                }
                return true;
            }

            public int GetHashCode(ImmutableHashSet<T> obj)
            {
                // Order independent so two sets with the same content agree.
                int hash = 0;
                if (obj == null)
                {
                    return hash;
                }
                foreach (T item in obj)
                {
                    hash ^= item?.GetHashCode() ?? 0;
                }
                return hash;
            }
        }
    }
}
