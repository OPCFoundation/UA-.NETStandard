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
using System.Collections.Generic;
using System.Threading;

namespace Opc.Ua.Server
{
    /// <summary>
    /// An entry of a <see cref="RequestTable{T}"/>: it remembers the slot it occupies, so
    /// that it is removed without a lookup.
    /// </summary>
    internal interface IRequestTableEntry
    {
        /// <summary>
        /// The value the slot is chosen from; consecutive values spread over the table.
        /// </summary>
        uint TableKey { get; }

        /// <summary>
        /// The slot the entry occupies, <see cref="RequestTable{T}.kNoSlot"/> when it is not in
        /// a table; a field that only the table writes, with compare-and-swap.
        /// </summary>
        ref int TableSlot { get; }
    }

    /// <summary>
    /// A set of the executing requests (or open validation scopes) of a
    /// <see cref="RequestManager"/> that registers and removes entries without a lock.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every request registers and completes once and passes a validation scope, so these
    /// sets are written four times per request from every worker thread. Behind a
    /// <see cref="System.Collections.Concurrent.ConcurrentDictionary{TKey, TValue}"/>, or
    /// behind sharded locks, the writes showed as the largest waiting point after the
    /// channel gate on many cores: every lock acquisition is a chance for its holder to be
    /// preempted while other workers wait for it.
    /// </para>
    /// <para>
    /// An entry is placed with a compare-and-swap into a free slot near a scrambled hash of
    /// its key, so consecutive requests land on different cache lines, and it remembers its
    /// slot, so it is removed with one more compare-and-swap. Only when every slot near the
    /// hash is taken does it go to an overflow dictionary under a lock. Both operations are
    /// full fences. Enumeration (drains, Cancel, Session close, the timeout sweep) reads the
    /// slots one after the other: an entry it does not see was placed after it read that
    /// slot, which keeps the ordering arguments of the request manager (registration before
    /// the Session/Cancel marks, drain count published before the snapshot) intact.
    /// </para>
    /// </remarks>
    /// <typeparam name="T">The entry type.</typeparam>
    internal sealed class RequestTable<T>
        where T : class, IRequestTableEntry
    {
        /// <summary>
        /// Creates a table.
        /// </summary>
        /// <param name="capacity">The number of slots, rounded up to a power of two.</param>
        public RequestTable(int capacity = kDefaultCapacity)
        {
            int count = kProbeLimit;
            while (count < capacity)
            {
                count <<= 1;
            }

            m_slots = new T?[count];
            m_mask = count - 1;
        }

        /// <summary>
        /// The number of slots.
        /// </summary>
        public int Capacity => m_slots.Length;

        /// <summary>
        /// The number of entries in the overflow dictionary, for tests.
        /// </summary>
        internal int OverflowCount
        {
            get
            {
                lock (m_overflowLock)
                {
                    return m_overflow.Count;
                }
            }
        }

        /// <summary>
        /// Adds an entry. Adding an entry that is in the table already does nothing.
        /// </summary>
        /// <param name="entry">The entry.</param>
        public void Add(T entry)
        {
            // Claim the entry first: of concurrent or repeated adds only one places it, so an
            // entry is never in two slots.
            ref int slot = ref entry.TableSlot;
            if (Interlocked.CompareExchange(ref slot, kAddingSlot, kNoSlot) != kNoSlot)
            {
                return;
            }

            int start = GetStart(entry.TableKey);
            for (int probe = 0; probe < kProbeLimit; probe++)
            {
                int index = (start + probe) & m_mask;
                // the slot is recorded first, so whoever finds the entry in it can remove it.
                if (Volatile.Read(ref m_slots[index]) == null)
                {
                    Volatile.Write(ref slot, index);
                    if (Interlocked.CompareExchange(ref m_slots[index], entry, null) == null)
                    {
                        return;
                    }
                }
            }

            lock (m_overflowLock)
            {
                Volatile.Write(ref slot, kOverflowSlot);
                m_overflow.Add(entry);
            }

            // the lock release is no full fence; registration must be one.
            Interlocked.MemoryBarrier();
        }

        /// <summary>
        /// Removes an entry.
        /// </summary>
        /// <param name="entry">The entry.</param>
        /// <returns><c>true</c> when this call removed the entry.</returns>
        public bool Remove(T entry)
        {
            ref int slotField = ref entry.TableSlot;
            int slot = Volatile.Read(ref slotField);
            if (slot >= 0 && slot < m_slots.Length)
            {
                if (Interlocked.CompareExchange(ref m_slots[slot], null, entry) == entry)
                {
                    Volatile.Write(ref slotField, kNoSlot);
                    return true;
                }
                return false;
            }

            if (slot == kOverflowSlot)
            {
                lock (m_overflowLock)
                {
                    if (m_overflow.Remove(entry))
                    {
                        Volatile.Write(ref slotField, kNoSlot);
                        return true;
                    }
                    return false;
                }
            }

            return false;
        }

        /// <summary>
        /// Whether the entry is in the table.
        /// </summary>
        /// <param name="entry">The entry.</param>
        public bool Contains(T entry)
        {
            int slot = Volatile.Read(ref entry.TableSlot);
            if (slot >= 0 && slot < m_slots.Length)
            {
                return ReferenceEquals(Volatile.Read(ref m_slots[slot]), entry);
            }

            if (slot == kOverflowSlot)
            {
                lock (m_overflowLock)
                {
                    return m_overflow.Contains(entry);
                }
            }

            return false;
        }

        /// <summary>
        /// Returns the entries.
        /// </summary>
        public List<T> GetEntries()
        {
            var entries = new List<T>();
            for (int index = 0; index < m_slots.Length; index++)
            {
                T? entry = Volatile.Read(ref m_slots[index]);
                if (entry != null)
                {
                    entries.Add(entry);
                }
            }

            lock (m_overflowLock)
            {
                entries.AddRange(m_overflow);
            }
            return entries;
        }

        /// <summary>
        /// Removes every entry.
        /// </summary>
        /// <returns>The entries that were in the table.</returns>
        public List<T> Clear()
        {
            var entries = new List<T>();
            for (int index = 0; index < m_slots.Length; index++)
            {
                T? entry = Interlocked.Exchange(ref m_slots[index], null);
                if (entry != null)
                {
                    Volatile.Write(ref entry.TableSlot, kNoSlot);
                    entries.Add(entry);
                }
            }

            lock (m_overflowLock)
            {
                foreach (T entry in m_overflow)
                {
                    Volatile.Write(ref entry.TableSlot, kNoSlot);
                    entries.Add(entry);
                }
                m_overflow.Clear();
            }
            return entries;
        }

        private int GetStart(uint key)
        {
            // Fibonacci hashing: consecutive keys land far apart.
            return (int)((key * 2654435769u) >> 8) & m_mask;
        }

        /// <summary>
        /// The slot of an entry that is not in the table.
        /// </summary>
        internal const int kNoSlot = -1;

        /// <summary>
        /// The slot of an entry in the overflow dictionary.
        /// </summary>
        private const int kOverflowSlot = -2;

        /// <summary>
        /// The slot of an entry that an <see cref="Add"/> has claimed and is placing.
        /// </summary>
        private const int kAddingSlot = -3;
        private const int kDefaultCapacity = 4096;
        private const int kProbeLimit = 16;
        private readonly T?[] m_slots;
        private readonly int m_mask;
        private readonly Lock m_overflowLock = new();
        private readonly HashSet<T> m_overflow = new(RequestTableEntryComparer<T>.Instance);
    }

    /// <summary>
    /// Compares entries by reference.
    /// </summary>
    /// <typeparam name="T">The entry type.</typeparam>
    internal sealed class RequestTableEntryComparer<T> : IEqualityComparer<T>
        where T : class
    {
        public static RequestTableEntryComparer<T> Instance { get; } = new();

        public bool Equals(T? x, T? y)
        {
            return ReferenceEquals(x, y);
        }

        public int GetHashCode(T obj)
        {
            return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
        }
    }
}
