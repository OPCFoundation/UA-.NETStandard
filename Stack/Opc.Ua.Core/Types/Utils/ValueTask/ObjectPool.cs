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
using System.Collections.Concurrent;

namespace Opc.Ua
{
    /// <summary>
    /// A simple object pool implementation.
    /// </summary>
    /// <typeparam name="T">The type of object to pool.</typeparam>
    internal class ObjectPool<T> where T : class
    {
        private readonly ConcurrentBag<T> m_objects;
        private readonly Func<T> m_objectGenerator;
        private readonly int m_maxSize;

        /// <summary>
        /// Initializes a new instance of the <see cref="ObjectPool{T}"/> class.
        /// </summary>
        /// <param name="objectGenerator">The function to generate new objects.</param>
        /// <param name="maxSize">The maximum size of the pool.</param>
        public ObjectPool(Func<T> objectGenerator, int maxSize)
        {
            m_objectGenerator = objectGenerator ?? throw new ArgumentNullException(nameof(objectGenerator));
            m_maxSize = maxSize > 0 ? maxSize : throw new ArgumentOutOfRangeException(nameof(maxSize));
            m_objects = new ConcurrentBag<T>();
        }

        /// <summary>
        /// Gets an object from the pool.
        /// </summary>
        /// <returns>An object from the pool or a new one if the pool is empty.</returns>
        public T Get()
        {
            if (m_objects.TryTake(out T item))
            {
                return item;
            }

            return m_objectGenerator();
        }

        /// <summary>
        /// Returns an object to the pool.
        /// </summary>
        /// <param name="item">The object to return.</param>
        public void Return(T item)
        {
            if (m_objects.Count < m_maxSize)
            {
                m_objects.Add(item);
            }
        }
    }
}
