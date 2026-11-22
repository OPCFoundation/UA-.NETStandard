// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Opc.Ua.Server
{
    /// <summary>
    /// The pooled arrays that hold the events of one notification message and the admission
    /// stamps of those events, side by side.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A notification message keeps its event arrays until its last owner releases it, which
    /// is after the client acknowledged it. A server with many event subscriptions therefore
    /// has thousands of these arrays in flight, more than <c>ArrayPool&lt;T&gt;.Shared</c>
    /// keeps per bucket (32 per processor), so the shared pool misses on rent and drops on
    /// return in every publish cycle. This pool keeps up to <see cref="MaxBuffersPerBucket"/>
    /// buffers of each power-of-two capacity, bounded by <see cref="MaxRetainedBytesPerBucket"/>,
    /// and holds both arrays in one object so a message rents and returns once.
    /// </para>
    /// <para>
    /// Buffers are only retained once they were returned, so the pool grows to the peak number
    /// of messages in flight it has seen, up to that bound. A buffer larger than
    /// <see cref="MaxPooledCapacity"/> is allocated for its message and left to the collector.
    /// </para>
    /// </remarks>
    internal sealed class EventNotificationBuffer
    {
        private EventNotificationBuffer(int capacity, Bucket? bucket)
        {
            Events = new EventFieldList[capacity];
            Stamps = new object?[capacity];
            m_bucket = bucket;
        }

        /// <summary>
        /// The events of the message in <c>[0, Count)</c>.
        /// </summary>
        public EventFieldList[] Events { get; }

        /// <summary>
        /// The admission stamps of the events (see <see cref="QueuedEvent"/>), aligned with
        /// <see cref="Events"/>. They keep the EventIds of the reported occurrences reserved
        /// until the buffer is returned.
        /// </summary>
        public object?[] Stamps { get; }

        /// <summary>
        /// The number of entries in use.
        /// </summary>
        public int Count { get; private set; }

        /// <summary>
        /// The smallest pooled capacity.
        /// </summary>
        internal const int MinCapacity = 16;

        /// <summary>
        /// The largest pooled capacity; larger buffers are not retained.
        /// </summary>
        internal const int MaxPooledCapacity = 4096;

        /// <summary>
        /// The number of buffers each bucket retains at most.
        /// </summary>
        internal const int MaxBuffersPerBucket = 4096;

        /// <summary>
        /// The array bytes each bucket retains at most.
        /// </summary>
        internal const long MaxRetainedBytesPerBucket = 16L * 1024 * 1024;

        /// <summary>
        /// Rents a buffer for <paramref name="count"/> events.
        /// </summary>
        /// <param name="count">The number of events, at least one.</param>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        public static EventNotificationBuffer Rent(int count)
        {
            if (count <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(count));
            }

            EventNotificationBuffer buffer;
            if (count > MaxPooledCapacity)
            {
                buffer = new EventNotificationBuffer(count, null);
            }
            else
            {
                Bucket bucket = s_buckets[GetBucketIndex(count)];
                if (bucket.Buffers.TryDequeue(out EventNotificationBuffer? pooled))
                {
                    Interlocked.Decrement(ref bucket.Count);
                    buffer = pooled;
                }
                else
                {
                    buffer = new EventNotificationBuffer(bucket.Capacity, bucket);
                }
            }
            buffer.Count = count;
            return buffer;
        }

        /// <summary>
        /// Clears the entries in use, which releases the events and their admission stamps,
        /// and returns the buffer to its bucket if the bucket has room. The caller must not
        /// use the buffer afterwards.
        /// </summary>
        public void Return()
        {
            Array.Clear(Events, 0, Count);
            Array.Clear(Stamps, 0, Count);
            Count = 0;

            Bucket? bucket = m_bucket;
            if (bucket == null)
            {
                return;
            }

            // count first, so concurrent returns cannot overshoot the bound.
            if (Interlocked.Increment(ref bucket.Count) > bucket.MaxCount)
            {
                Interlocked.Decrement(ref bucket.Count);
                return;
            }
            bucket.Buffers.Enqueue(this);
        }

        /// <summary>
        /// The number of buffers the bucket for <paramref name="count"/> events retains at most.
        /// </summary>
        internal static int GetMaxRetainedCount(int count)
        {
            return s_buckets[GetBucketIndex(count)].MaxCount;
        }

        private static int GetBucketIndex(int count)
        {
            int index = 0;
            int capacity = MinCapacity;
            while (capacity < count)
            {
                capacity <<= 1;
                index++;
            }
            return index;
        }

        private static Bucket[] CreateBuckets()
        {
            int entryBytes = Unsafe.SizeOf<EventFieldList>() + IntPtr.Size;
            var buckets = new Bucket[GetBucketIndex(MaxPooledCapacity) + 1];
            for (int ii = 0; ii < buckets.Length; ii++)
            {
                int capacity = MinCapacity << ii;
                long maxCount = MaxRetainedBytesPerBucket / ((long)capacity * entryBytes);
                buckets[ii] = new Bucket(capacity, (int)Math.Max(1, Math.Min(MaxBuffersPerBucket, maxCount)));
            }
            return buckets;
        }

        /// <summary>
        /// The retained buffers of one power-of-two capacity.
        /// </summary>
        private sealed class Bucket(int capacity, int maxCount)
        {
            public readonly int Capacity = capacity;
            public readonly int MaxCount = maxCount;
            public readonly ConcurrentQueue<EventNotificationBuffer> Buffers = new();

            /// <summary>
            /// The retained buffers, including returns about to be enqueued.
            /// </summary>
            public int Count;
        }

        private static readonly Bucket[] s_buckets = CreateBuckets();
        private readonly Bucket? m_bucket;
    }
}
