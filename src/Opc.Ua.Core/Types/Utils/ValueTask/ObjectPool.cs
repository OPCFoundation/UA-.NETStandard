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
        private readonly Func<T> m_objectGenerator;
        private readonly T?[] m_items;
        private T? m_firstItem;

        /// <summary>
        /// Initializes a new instance of the <see cref="ObjectPool{T}"/> class.
        /// </summary>
        /// <param name="objectGenerator">The function to generate new objects.</param>
        /// <param name="maxSize">The maximum number of retained objects (capped at four per processor).</param>
        public ObjectPool(Func<T> objectGenerator, int maxSize)
        {
            m_objectGenerator = objectGenerator ?? throw new ArgumentNullException(nameof(objectGenerator));
            if (maxSize <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxSize));
            }
            // Retained objects only need to cover the concurrently outstanding ones;
            // more slots would only lengthen the scan when the pool runs dry.
            m_items = new T?[Math.Max(Math.Min(maxSize, Environment.ProcessorCount * 4), 1) - 1];
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
