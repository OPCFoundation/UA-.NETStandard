// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Threading;

namespace Opc.Ua
{
    /// <summary>
    /// A simple bounded, lock-free object pool.
    /// </summary>
    /// <remarks>
    /// Get and Return run once per service request on the server hot path and
    /// typically on different threads (the request is taken on a receive thread
    /// and returned on whichever thread awaits the response). A fixed slot array
    /// with interlocked exchange keeps both operations wait-free; a
    /// ConcurrentBag would steal across thread-local lists and its Count takes
    /// every per-thread lock.
    /// </remarks>
    /// <typeparam name="T">The type of object to pool.</typeparam>
    internal class ObjectPool<T> where T : class
    {
        /// <summary>
        /// The number of objects a pool may retain regardless of the processor count.
        /// </summary>
        internal const int MinRetainedCap = 256;

        private readonly Func<T> m_objectGenerator;
        private readonly T?[] m_items;
        private T? m_firstItem;

        /// <summary>
        /// Initializes a new instance of the <see cref="ObjectPool{T}"/> class.
        /// </summary>
        /// <param name="objectGenerator">The function to generate new objects.</param>
        /// <param name="maxSize">The maximum number of retained objects (capped at four per
        /// processor, but at least <see cref="MinRetainedCap"/>).</param>
        public ObjectPool(Func<T> objectGenerator, int maxSize)
        {
            m_objectGenerator = objectGenerator ?? throw new ArgumentNullException(nameof(objectGenerator));
            if (maxSize <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxSize));
            }
            // Retained objects only need to cover the ones returned before they are taken
            // again; more slots would only lengthen the scan when the pool runs dry. Requests
            // complete in bursts (a writer completes a batch of responses), so a small machine
            // with many requests in flight keeps a floor: otherwise each burst drops objects
            // that the next requests allocate again.
            int cap = Math.Max(Environment.ProcessorCount * 4, MinRetainedCap);
            m_items = new T?[Math.Max(Math.Min(maxSize, cap), 1) - 1];
        }

        /// <summary>
        /// Gets an object from the pool.
        /// </summary>
        /// <returns>An object from the pool or a new one if the pool is empty.</returns>
        public T Get()
        {
            T? item = m_firstItem;
            if (item != null && Interlocked.CompareExchange(ref m_firstItem, null, item) == item)
            {
                return item;
            }

            T?[] items = m_items;
            for (int i = 0; i < items.Length; i++)
            {
                item = items[i];
                if (item != null && Interlocked.CompareExchange(ref items[i], null, item) == item)
                {
                    return item;
                }
            }

            return m_objectGenerator();
        }

        /// <summary>
        /// Returns an object to the pool.
        /// </summary>
        /// <param name="item">The object to return.</param>
        public void Return(T item)
        {
            if (m_firstItem == null && Interlocked.CompareExchange(ref m_firstItem, item, null) == null)
            {
                return;
            }

            T?[] items = m_items;
            for (int i = 0; i < items.Length; i++)
            {
                if (Interlocked.CompareExchange(ref items[i], item, null) == null)
                {
                    return;
                }
            }
        }
    }
}
