/* Copyright (c) 1996-2022 The OPC Foundation. All rights reserved.
   The source code in this file is covered under a dual-license scenario:
     - RCL: for OPC Foundation Corporate Members in good-standing
     - GPL V2: everybody else
   RCL license terms accompanied with this source code. See http://opcfoundation.org/License/RCL/1.00/
   GNU General Public License as published by the Free Software Foundation;
   version 2 of the License are accompanied with this source code. See http://opcfoundation.org/License/GPLv2
   This source code is distributed in the hope that it will be useful,
   but WITHOUT ANY WARRANTY; without even the implied warranty of
   MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.
*/

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
