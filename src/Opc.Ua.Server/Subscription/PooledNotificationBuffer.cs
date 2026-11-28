// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Concurrent;
using System.Threading;

namespace Opc.Ua.Server
{
    /// <summary>
    /// A pooled holder of the arrays of one notification message, returned with the payload
    /// of the message when its last owner releases it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A notification message keeps its arrays until its last owner releases it, which is
    /// after the client acknowledged it. A server with many subscriptions therefore has
    /// thousands of these arrays in flight, more than <c>ArrayPool&lt;T&gt;.Shared</c> keeps
    /// per bucket (32 per processor), so the shared pool misses on rent and drops on return
    /// in every publish cycle. A <see cref="Pool"/> keeps buffers of each power-of-two
    /// capacity from <see cref="MinCapacity"/> to <see cref="MaxPooledCapacity"/>, bounded by
    /// a count and by the bytes of the arrays per bucket.
    /// </para>
    /// <para>
    /// Buffers are only retained once they were returned, so a pool grows to the peak number
    /// of messages in flight it has seen, up to that bound. A buffer larger than
    /// <see cref="MaxPooledCapacity"/> is allocated for its message and left to the collector.
    /// </para>
    /// </remarks>
    /// <typeparam name="TBuffer">The buffer type.</typeparam>
    internal abstract class PooledNotificationBuffer<TBuffer>
        where TBuffer : PooledNotificationBuffer<TBuffer>
    {
        /// <summary>
        /// Creates a buffer that returns to <paramref name="bucket"/>, or is not retained
        /// when it is <c>null</c>.
        /// </summary>
        protected PooledNotificationBuffer(Bucket? bucket)
        {
            m_bucket = bucket;
        }

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
        /// Clears the entries in use, which releases what they reference, and returns the
        /// buffer to its bucket if the bucket has room. The caller must not use the buffer
        /// afterwards.
        /// </summary>
        public void Return()
        {
            ClearEntries(Count);
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
            bucket.Buffers.Enqueue((TBuffer)this);
        }

        /// <summary>
        /// Clears the first <paramref name="count"/> entries of the arrays.
        /// </summary>
        protected abstract void ClearEntries(int count);

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

        /// <summary>
        /// The power-of-two buckets of one buffer type.
        /// </summary>
        internal sealed class Pool
        {
            /// <summary>
            /// Creates the buckets.
            /// </summary>
            /// <param name="entryBytes">The array bytes of one entry of a buffer.</param>
            /// <param name="maxBuffersPerBucket">The buffers a bucket retains at most.</param>
            /// <param name="maxRetainedBytesPerBucket">The array bytes a bucket retains at most.</param>
            /// <param name="create">Creates a buffer of a capacity for a bucket.</param>
            public Pool(
                int entryBytes,
                int maxBuffersPerBucket,
                long maxRetainedBytesPerBucket,
                Func<int, Bucket?, TBuffer> create)
            {
                m_create = create;
                m_buckets = new Bucket[GetBucketIndex(MaxPooledCapacity) + 1];
                for (int ii = 0; ii < m_buckets.Length; ii++)
                {
                    int capacity = MinCapacity << ii;
                    long maxCount = maxRetainedBytesPerBucket / ((long)capacity * entryBytes);
                    m_buckets[ii] = new Bucket(
                        capacity,
                        (int)Math.Max(1, Math.Min(maxBuffersPerBucket, maxCount)));
                }
            }

            /// <summary>
            /// Rents a buffer for <paramref name="count"/> entries.
            /// </summary>
            /// <param name="count">The number of entries, at least one.</param>
            /// <exception cref="ArgumentOutOfRangeException"></exception>
            public TBuffer Rent(int count)
            {
                if (count <= 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(count));
                }

                TBuffer buffer;
                if (count > MaxPooledCapacity)
                {
                    buffer = m_create(count, null);
                }
                else
                {
                    Bucket bucket = m_buckets[GetBucketIndex(count)];
                    if (bucket.Buffers.TryDequeue(out TBuffer? pooled))
                    {
                        Interlocked.Decrement(ref bucket.Count);
                        buffer = pooled;
                    }
                    else
                    {
                        buffer = m_create(bucket.Capacity, bucket);
                    }
                }
                ((PooledNotificationBuffer<TBuffer>)buffer).Count = count;
                return buffer;
            }

            /// <summary>
            /// Creates a buffer for <paramref name="count"/> entries that is not retained
            /// when it is returned.
            /// </summary>
            /// <param name="count">The number of entries, at least one.</param>
            /// <exception cref="ArgumentOutOfRangeException"></exception>
            public TBuffer CreateUnpooled(int count)
            {
                if (count <= 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(count));
                }

                TBuffer buffer = m_create(count, null);
                ((PooledNotificationBuffer<TBuffer>)buffer).Count = count;
                return buffer;
            }

            /// <summary>
            /// The number of buffers the bucket for <paramref name="count"/> entries retains
            /// at most.
            /// </summary>
            public int GetMaxRetainedCount(int count)
            {
                return m_buckets[GetBucketIndex(count)].MaxCount;
            }

            private readonly Bucket[] m_buckets;
            private readonly Func<int, Bucket?, TBuffer> m_create;
        }

        /// <summary>
        /// The retained buffers of one power-of-two capacity.
        /// </summary>
        internal sealed class Bucket(int capacity, int maxCount)
        {
            public readonly int Capacity = capacity;
            public readonly int MaxCount = maxCount;
            public readonly ConcurrentQueue<TBuffer> Buffers = new();

            /// <summary>
            /// The retained buffers, including returns about to be enqueued.
            /// </summary>
            public int Count;
        }

        private readonly Bucket? m_bucket;
    }
}
