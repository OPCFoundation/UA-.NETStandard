// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Threading;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Counts admitted operations in one slot per processor, so operations entered on every
    /// core at once do not share a lock or a cache line. Only the sum is meaningful: an
    /// operation may exit on another processor, as long as it exits the slot it entered.
    /// </summary>
    /// <remarks>
    /// <see cref="Enter()"/> and <see cref="Exit"/> are full fences. A gate that publishes a
    /// closing flag and then reads <see cref="Count"/> after a full fence therefore either
    /// counts an operation that entered concurrently, or the operation reads the flag after
    /// its increment and backs out. The sum is not an atomic snapshot: work extended after
    /// closure must remain in its parent's stripe via <see cref="Enter(int)"/>, so an
    /// accepted lineage cannot move behind a concurrent scan.
    /// </remarks>
    internal sealed class StripedCounter
    {
        /// <summary>
        /// Gets the number of operations entered and not yet exited.
        /// </summary>
        public long Count
        {
            get
            {
                long count = 0;
                for (int ii = kStride; ii < m_counts.Length; ii += kStride)
                {
                    count += Volatile.Read(ref m_counts[ii]);
                }
                return count;
            }
        }

        /// <summary>
        /// Gets the final processor stripe available for explicit admission.
        /// </summary>
        internal int LastSlot => m_counts.Length - kStride;

        /// <summary>
        /// Enters an operation.
        /// </summary>
        /// <returns>The slot to pass to <see cref="Exit"/>.</returns>
        public int Enter()
        {
#if NETCOREAPP3_0_OR_GREATER
            int processor = Thread.GetCurrentProcessorId();
#else
            int processor = Environment.CurrentManagedThreadId;
#endif
            // Slot 0 starts one stride in: the first line holds the array length read by every access.
            int slot = ((processor & (s_slots - 1)) + 1) * kStride;
            Enter(slot);
            return slot;
        }

        /// <summary>
        /// Extends an admitted operation in its existing stripe.
        /// </summary>
        /// <param name="slot">The stripe already protecting the parent operation.</param>
        public void Enter(int slot)
        {
            Interlocked.Increment(ref m_counts[slot]);
        }

        /// <summary>
        /// Exits an operation entered in the slot.
        /// </summary>
        /// <param name="slot">The slot <see cref="Enter()"/> returned.</param>
        public void Exit(int slot)
        {
            Interlocked.Decrement(ref m_counts[slot]);
        }

        private static int GetSlotCount()
        {
            int slots = 1;
            while (slots < Environment.ProcessorCount && slots < kMaxSlots)
            {
                slots <<= 1;
            }
            return slots;
        }

        /// <summary>
        /// One counter per 128 bytes (two 64 byte lines, the unit adjacent-line prefetch pulls in).
        /// </summary>
        private const int kStride = 16;
        private const int kMaxSlots = 64;
        private static readonly int s_slots = GetSlotCount();
        private readonly long[] m_counts = new long[(s_slots + 1) * kStride];
    }
}
