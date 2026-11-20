/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Collections.Concurrent;
using System.Threading;

namespace Opc.Ua
{
    /// <summary>
    /// <para>
    /// Bounded lock-free object pool with a single "first item" fast
    /// slot and a shared queue of additional instances. Get and Return
    /// are O(1): renting from an empty pool or returning to a full one
    /// does not scan the retained instances, and threads that return
    /// whole batches (for example the notifications of a sent Publish
    /// response) do not serialize on a lock.
    /// </para>
    /// <para>
    /// The pool never blocks. A <see cref="Get"/> miss falls through to
    /// the factory delegate. A <see cref="Return"/> beyond the configured
    /// maximum drops the instance to the GC. Callers are responsible for
    /// resetting instance state before returning.
    /// </para>
    /// </summary>
    /// <typeparam name="T">The pooled type. Must be a class with a
    /// public parameterless constructor.</typeparam>
    internal sealed class BoundedObjectPool<T> where T : class, new()
    {
        private T? m_firstItem;
        private ConcurrentQueue<T>? m_items;
        private int m_count;
        private int m_maximumRetained;
        private readonly Func<T> m_factory;

        /// <summary>
        /// Create a new pool that retains up to <paramref name="maximumRetained"/>
        /// instances. One slot is reserved as a fast first-item slot
        /// accessed via <see cref="Interlocked.CompareExchange{T}(ref T, T, T)"/>;
        /// the remainder live on a shared queue that is created on the
        /// first return that needs it. Get and Return hit the fast slot
        /// first.
        /// </summary>
        /// <param name="maximumRetained">Upper bound on instances held by
        /// the pool. Must be positive.</param>
        /// <param name="factory">Optional factory delegate used to create new
        /// instances on a pool miss. When <see langword="null"/> a direct
        /// <c>new T()</c> call is compiled as a lambda, avoiding the
        /// reflection overhead of the generic <c>new T()</c> constraint.</param>
        public BoundedObjectPool(int maximumRetained, Func<T>? factory = null)
        {
            if (maximumRetained <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumRetained));
            }
            m_maximumRetained = maximumRetained;
            m_factory = factory ?? (() => new T());
        }

        /// <summary>
        /// Maximum number of instances retained by the pool, including
        /// the fast first-item slot.
        /// </summary>
        public int MaximumRetained => Volatile.Read(ref m_maximumRetained);

        /// <summary>
        /// Raises the maximum number of retained instances to at least
        /// <paramref name="maximumRetained"/>. The bound never shrinks,
        /// so concurrent callers that size the pool for their own load
        /// cannot undo each other.
        /// </summary>
        /// <param name="maximumRetained">The minimum bound.</param>
        public void EnsureMaximumRetained(int maximumRetained)
        {
            int current = Volatile.Read(ref m_maximumRetained);
            while (current < maximumRetained)
            {
                int previous = Interlocked.CompareExchange(ref m_maximumRetained, maximumRetained, current);
                if (previous == current)
                {
                    return;
                }
                current = previous;
            }
        }

        /// <summary>
        /// Rent an instance from the pool. Returns a recycled instance
        /// on a hit; otherwise constructs a fresh instance via the
        /// factory delegate.
        /// </summary>
        public T Get()
        {
            T? item = m_firstItem;
            if (item is not null &&
                Interlocked.CompareExchange(ref m_firstItem, null, item) == item)
            {
                return item;
            }

            ConcurrentQueue<T>? items = Volatile.Read(ref m_items);
            if (items != null &&
                Volatile.Read(ref m_count) > 0 &&
                items.TryDequeue(out item))
            {
                Interlocked.Decrement(ref m_count);
                return item;
            }

            return m_factory();
        }

        /// <summary>
        /// Return an instance to the pool. The pool may discard the
        /// instance if it is full. The caller is responsible for
        /// resetting any mutable state on the instance before calling
        /// this method.
        /// </summary>
        public void Return(T instance)
        {
            if (instance is null)
            {
                return;
            }

            if (m_firstItem is null &&
                Interlocked.CompareExchange(ref m_firstItem, instance, null) is null)
            {
                return;
            }

            // the fast slot is one of the retained instances.
            int maximum = MaximumRetained - 1;
            if (Volatile.Read(ref m_count) >= maximum)
            {
                // Pool is full; let the instance be collected.
                return;
            }

            if (Interlocked.Increment(ref m_count) > maximum)
            {
                Interlocked.Decrement(ref m_count);
                return;
            }

            ConcurrentQueue<T>? items = Volatile.Read(ref m_items);
            if (items == null)
            {
                Interlocked.CompareExchange(ref m_items, new ConcurrentQueue<T>(), null);
                items = m_items!;
            }
            items.Enqueue(instance);
        }
    }
}
