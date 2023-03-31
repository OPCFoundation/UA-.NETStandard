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

//#define TRACE_MEMORY
//#define TRACK_MEMORY 

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Threading;

namespace Opc.Ua.Bindings
{
    #region BufferCollection Class
    /// <summary>
    /// A collection of buffers.
    /// </summary>
    public class BufferCollection : List<ArraySegment<byte>>
    {
        #region Constructors
        /// <summary>
        /// Creates an empty collection.
        /// </summary>
        public BufferCollection()
        {
        }

        /// <summary>
        /// Creates an empty collection with the specified capacity.
        /// </summary>
        /// <param name="capacity">The capacity.</param>
        public BufferCollection(int capacity) : base(capacity)
        {
        }

        /// <summary>
        /// Creates a collection with a single element.
        /// </summary>
        /// <param name="segment">The segment.</param>
        public BufferCollection(ArraySegment<byte> segment)
        {
            Add(segment);
        }

        /// <summary>
        /// Creates a collection with a single element.
        /// </summary>
        /// <param name="array">The array.</param>
        /// <param name="offset">The offset.</param>
        /// <param name="count">The count.</param>
        public BufferCollection(byte[] array, int offset, int count)
        {
            Add(new ArraySegment<byte>(array, offset, count));
        }
        #endregion

        #region Public Methods
        /// <summary>
        /// Returns the buffers to the manager before clearing the collection.
        /// </summary>
        /// <param name="bufferManager">The buffer manager.</param>
        /// <param name="owner">The owner.</param>
        /// <returns>Length of all buffers in this collection</returns>
        public int Release(BufferManager bufferManager, string owner)
        {
            int count = 0;

            foreach (ArraySegment<byte> buffer in this)
            {
                count += buffer.Count;
                bufferManager.ReturnBuffer(buffer.Array, owner);
            }

            Clear();

            return count;
        }

        /// <summary>
        /// Returns the total amount of data in the buffers.
        /// </summary>
        /// <value>The total size.</value>
        public int TotalSize
        {
            get
            {
                int count = 0;

                for (int ii = 0; ii < this.Count; ii++)
                {
                    count += this[ii].Count;
                }

                return count;
            }
        }
        #endregion
    }
    #endregion

    #region BufferManager Class
    /// <summary>
    /// A thread safe wrapper for the buffer manager class.
    /// </summary>
    public class BufferManager
    {
        #region Constructors
        /// <summary>
        /// Constructs the buffer manager.
        /// </summary>
        /// <param name="name">The name.</param>
        /// <param name="maxBufferSize">Max size of the buffer.</param>
        public BufferManager(string name, int maxBufferSize)
        {
            int maxArrayLength = maxBufferSize + m_cookieLength;
            m_name = name;
            m_arrayPool = ArrayPool<byte>.Create(maxBufferSize, 32);
            m_maxBufferSize = maxBufferSize;
        }
        #endregion

        #region Public Methods
        /// <summary>
        /// Returns a buffer with at least the specified size.
        /// </summary>
        /// <param name="size">The size.</param>
        /// <param name="owner">The owner.</param>
        /// <returns>The buffer content</returns>
        public byte[] TakeBuffer(int size, string owner)
        {
            if (size > m_maxBufferSize)
            {
                throw new ArgumentOutOfRangeException(nameof(size));
            }

            byte[] buffer = m_arrayPool.Rent(size + m_cookieLength);
#if TRACK_MEMORY
            lock (m_lock)
            {
                byte[] bytes = BitConverter.GetBytes(++m_id);
                Array.Copy(bytes, 0, buffer, buffer.Length - 5, bytes.Length);

                m_allocated += buffer.Length;

                Allocation allocation = new Allocation();

                allocation.Id = m_id;
                allocation.Buffer = buffer;
                allocation.Timestamp = DateTime.UtcNow;
                allocation.Owner = owner;

                m_allocations[m_id] = allocation;
            }
#endif
#if TRACE_MEMORY
            Utils.LogTrace("{0:X}:TakeBuffer({1:X},{2:X},{3},{4})", this.GetHashCode(), buffer.GetHashCode(), buffer.Length, owner, ++m_buffersTaken);
#endif
            buffer[buffer.Length - 1] = m_cookieUnlocked;

            return buffer;
        }

        /// <summary>
        /// Changes the owner of a buffer (used for debugging only).
        /// </summary>
        /// <param name="buffer">The buffer.</param>
        /// <param name="owner">The owner.</param>
        public void TransferBuffer(byte[] buffer, string owner)
        {
#if TRACK_MEMORY
            if (buffer == null)
            {
                return;
            }

            lock (m_lock)
            {
                int id = BitConverter.ToInt32(buffer, buffer.Length - 5);

                Allocation allocation = null;

                if (m_allocations.TryGetValue(id, out allocation))
                {
                    allocation.Owner = Utils.Format("{0}/{1}", allocation.Owner, owner);

                    if (allocation.Reported > 0)
                    {
                        Utils.LogTrace("{0}: Id={1}; Owner={2}; Size={3} KB; *** TRANSFERRED ***",
                            m_name,
                            allocation.Id,
                            allocation.Owner,
                            allocation.Buffer.Length / 1024);
                    }
                }
            }
#endif
#if TRACE_MEMORY
            Utils.LogTrace("{0:X}:TransferBuffer({1:X},{2:X},{3})", this.GetHashCode(), buffer.GetHashCode(), buffer.Length, owner);
#endif
        }

        /// <summary>
        /// Locks the buffer (used for debugging only).
        /// </summary>
        /// <param name="buffer">The buffer.</param>
        public static void LockBuffer(byte[] buffer)
        {
            if (buffer[buffer.Length - 1] != m_cookieUnlocked)
            {
                throw new InvalidOperationException("Buffer is already locked.");
            }
#if TRACE_MEMORY
            Utils.LogTrace("LockBuffer({0:X},{1:X})", buffer.GetHashCode(), buffer.Length);
#endif
            buffer[buffer.Length - 1] = m_cookieLocked;
        }

        /// <summary>
        /// Unlocks the buffer (used for debugging only).
        /// </summary>
        /// <param name="buffer">The buffer.</param>
        public static void UnlockBuffer(byte[] buffer)
        {
            if (buffer[buffer.Length - 1] != m_cookieLocked)
            {
                throw new InvalidOperationException("Buffer is not locked.");
            }
#if TRACE_MEMORY
            Utils.LogTrace("UnlockBuffer({0:X},{1:X})", buffer.GetHashCode(), buffer.Length);
#endif
            buffer[buffer.Length - 1] = m_cookieUnlocked;
        }

        /// <summary>
        /// Release the buffer.
        /// </summary>
        /// <param name="buffer">The buffer.</param>
        /// <param name="owner">The owner.</param>
        public void ReturnBuffer(byte[] buffer, string owner)
        {
            if (buffer == null)
            {
                return;
            }

#if TRACE_MEMORY
            Utils.LogTrace("{0:X}:ReturnBuffer({1:X},{2:X},{3},{4})", this.GetHashCode(), buffer.GetHashCode(), buffer.Length, owner, --m_buffersTaken);
#endif
            if (buffer[buffer.Length - 1] != m_cookieUnlocked)
            {
                throw new InvalidOperationException("Buffer has been locked.");
            }

            // destroy cookie
            buffer[buffer.Length - 1] = m_cookieUnlocked ^ m_cookieLocked;

#if TRACK_MEMORY
            lock (m_lock)
            {

                m_allocated -= buffer.Length;

                int id = BitConverter.ToInt32(buffer, buffer.Length - 5);

                Allocation allocation = null;

                if (m_allocations.TryGetValue(id, out allocation))
                {
                    allocation.ReleasedBy = owner;

                    if (allocation.Reported > 0)
                    {
                        Utils.LogTrace("{0}: Id={1}; Owner={2}; ReleasedBy={3}; Size={4} KB; *** RETURNED ***",
                            m_name,
                            allocation.Id,
                            allocation.Owner,
                            allocation.ReleasedBy,
                            allocation.Buffer.Length / 1024);
                    }
                }

                m_allocations.Remove(id);

                Utils.LogTrace("Deallocated ID {0}: {1}/{2}", id, buffer.Length, m_allocated);

                foreach (KeyValuePair<int, Allocation> current in m_allocations)
                {
                    allocation = current.Value;

                    if (allocation.ReleasedBy != null)
                    {
                        continue;
                    }

                    long ticks = allocation.Timestamp.Ticks;

                    double age = Math.Truncate(new TimeSpan(DateTime.UtcNow.Ticks - ticks).TotalSeconds);

                    if (age > 3 && Math.Truncate(age) % 3 == 0)
                    {
                        if (allocation.Reported < age)
                        {
                            Utils.LogTrace("{0}: Id={1}; Owner={2}; Size={3} KB; Age={4}",
                                m_name,
                                allocation.Id,
                                allocation.Owner,
                                allocation.Buffer.Length / 1024,
                                age);

                            allocation.Reported = (int)age;
                        }
                    }
                }

                for (int ii = 0; ii < buffer.Length - 5; ii++)
                {
                    if (m_name == "Server")
                    {
                        buffer[ii] = 0xFA;
                    }
                    else
                    {
                        buffer[ii] = 0xFC;
                    }
                }
            }
#endif
            m_arrayPool.Return(buffer);
        }
        #endregion

        #region Private Fields
        private readonly string m_name;
        private readonly int m_maxBufferSize;
#if TRACE_MEMORY
        private int m_buffersTaken = 0;
#endif
        private ArrayPool<byte> m_arrayPool;
        const byte m_cookieLocked = 0xa5;
        const byte m_cookieUnlocked = 0x5a;
#if TRACK_MEMORY
        const byte m_cookieLength = 5;
        class Allocation
        {
            public int Id;
            public byte[] Buffer;
            public DateTime Timestamp;
            public string Owner;
            public string ReleasedBy;
            public int Reported;
        }
        private readonly object m_lock = new object();
        private int m_allocated;
        private int m_id;
        private SortedDictionary<int, Allocation> m_allocations = new SortedDictionary<int, Allocation>();
#else
        const byte m_cookieLength = 1;
#endif
        #endregion
    }
    #endregion
}
