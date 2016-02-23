/* ========================================================================
 * Copyright (c) 2005-2013 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Reciprocal Community License ("RCL") Version 1.00
 * 
 * Unless explicitly acquired and licensed from Licensor under another 
 * license, the contents of this file are subject to the Reciprocal 
 * Community License ("RCL") Version 1.00, or subsequent versions 
 * as allowed by the RCL, and You may not copy or use this file in either 
 * source code or executable form, except in compliance with the terms and 
 * conditions of the RCL.
 * 
 * All software distributed under the RCL is provided strictly on an 
 * "AS IS" basis, WITHOUT WARRANTY OF ANY KIND, EITHER EXPRESS OR IMPLIED, 
 * AND LICENSOR HEREBY DISCLAIMS ALL SUCH WARRANTIES, INCLUDING WITHOUT 
 * LIMITATION, ANY WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR 
 * PURPOSE, QUIET ENJOYMENT, OR NON-INFRINGEMENT. See the RCL for specific 
 * language governing rights and limitations under the RCL.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/RCL/1.00/
 * ======================================================================*/

using System;
using System.Collections.Generic;

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
        /// <param name="maxPoolSize">Max size of the pool.</param>
        /// <param name="maxBufferSize">Max size of the buffer.</param>
        public BufferManager(string name, int maxPoolSize, int maxBufferSize)
        {
            m_name      = name;
            m_manager   = System.ServiceModel.Channels.BufferManager.CreateBufferManager(maxPoolSize, maxBufferSize);
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
            if (size > Int32.MaxValue - 5)
            {
                throw new ArgumentOutOfRangeException("size");
            }

            lock (m_lock)
            {
                #if TRACK_MEMORY
                byte[] buffer = m_manager.TakeBuffer(size+5);                             

                byte[] bytes = BitConverter.GetBytes(++m_id);
                Array.Copy(bytes, 0, buffer, buffer.Length-5, bytes.Length);                
                buffer[buffer.Length-1] = 0;

                m_allocated += buffer.Length;

                Allocation allocation = new Allocation();
                
                allocation.Id = m_id;
                allocation.Buffer = buffer;
                allocation.Timestamp = DateTime.UtcNow;
                allocation.Owner = owner;

                m_allocations[m_id] = allocation;
                
                return buffer;
                #else
                byte[] buffer = m_manager.TakeBuffer(size+1);               
                buffer[buffer.Length-1] = 0;
                return buffer;
                #endif
            }
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
                int id = BitConverter.ToInt32(buffer, buffer.Length-5);       
         
                Allocation allocation = null;

                if (m_allocations.TryGetValue(id, out allocation))
                {
                    allocation.Owner = Utils.Format("{0}/{1}", allocation.Owner, owner);

                    if (allocation.Reported > 0)
                    {
                        Utils.Trace("{0}: Id={1}; Owner={2}; Size={3} KB; *** TRANSFERRED ***", 
                            m_name,
                            allocation.Id, 
                            allocation.Owner, 
                            allocation.Buffer.Length/1024);
                    }
                }
            }
            #endif
        }

        /// <summary>
        /// Locks the buffer (used for debugging only).
        /// </summary>
        /// <param name="buffer">The buffer.</param>
        public static void LockBuffer(byte[] buffer)
        {
            if (buffer[buffer.Length-1] != 0)
            {
                throw new InvalidOperationException("Buffer is already locked.");
            }

            buffer[buffer.Length-1] = 1;
        }

        /// <summary>
        /// Unlocks the buffer (used for debugging only).
        /// </summary>
        /// <param name="buffer">The buffer.</param>
        public static void UnlockBuffer(byte[] buffer)
        {
            if (buffer[buffer.Length-1] == 0)
            {
                throw new InvalidOperationException("Buffer is not locked.");
            }

            buffer[buffer.Length-1] = 0;
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

            lock (m_lock)
            {
                if (buffer[buffer.Length-1] != 0)
                {
                    throw new InvalidOperationException("Buffer has been locked.");
                }

                #if TRACK_MEMORY
                m_allocated -= buffer.Length;
                
                int id = BitConverter.ToInt32(buffer, buffer.Length-5);       
         
                Allocation allocation = null;

                if (m_allocations.TryGetValue(id, out allocation))
                {
                    allocation.ReleasedBy = owner;

                    if (allocation.Reported > 0)
                    {
                        Utils.Trace("{0}: Id={1}; Owner={2}; ReleasedBy={3}; Size={4} KB; *** RETURNED ***", 
                            m_name,
                            allocation.Id, 
                            allocation.Owner, 
                            allocation.ReleasedBy,
                            allocation.Buffer.Length/1024);
                    }
                }

                m_allocations.Remove(id);
                               
                Utils.Trace("Deallocated ID {0}: {1}/{2}", id, buffer.Length, m_allocated);

                foreach (KeyValuePair<int,Allocation> current in m_allocations)
                {
                    allocation = current.Value;

                    if (allocation.ReleasedBy != null)
                    {
                        continue;
                    }

                    long ticks = allocation.Timestamp.Ticks;

                    double age = Math.Truncate(new TimeSpan(DateTime.UtcNow.Ticks - ticks).TotalSeconds);

                    if (age > 3 && Math.Truncate(age)%3 == 0)
                    {        
                        if (allocation.Reported < age)
                        {
                            Utils.Trace("{0}: Id={1}; Owner={2}; Size={3} KB; Age={4}", 
                                m_name,
                                allocation.Id, 
                                allocation.Owner, 
                                allocation.Buffer.Length/1024, 
                                age);

                            allocation.Reported = (int)age;
                        }
                    }
                }

                for (int ii = 0; ii < buffer.Length-5; ii++)
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
                #endif
               
                m_manager.ReturnBuffer(buffer);
            }
        }
        #endregion
        
        #region Private Fields
        private object m_lock = new object();
        private string m_name;
        private System.ServiceModel.Channels.BufferManager m_manager;

        #if TRACK_MEMORY
        class Allocation
        {
            public int Id;
            public byte[] Buffer;
            public DateTime Timestamp;
            public string Owner;
            public string ReleasedBy;
            public int Reported;
        }

        private int m_allocated;
        private int m_id;
        private SortedDictionary<int,Allocation> m_allocations = new SortedDictionary<int,Allocation>();
        #endif

        #endregion
    }
    #endregion 
}
