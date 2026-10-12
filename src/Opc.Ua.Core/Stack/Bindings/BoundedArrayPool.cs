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
 *
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
using System.Buffers;
using System.Collections.Concurrent;
using System.Threading;

namespace Opc.Ua.Bindings
{
    /// <summary>
    /// An array pool for the transport's chunk-sized buffers that keeps enough arrays for
    /// deep request pipelines, independent of the processor count.
    /// </summary>
    /// <remarks>
    /// <see cref="ArrayPool{T}.Shared"/> keeps a few arrays per bucket and processor. A server
    /// with a thousand responses waiting to be sent exhausts it on a host with one or two
    /// processors and then allocates a new chunk-sized array for every response. This pool
    /// keeps up to <see cref="MaxArraysPerBucket"/> arrays per power-of-two bucket in a
    /// lock-free queue, so the retained memory is bounded by <c>MaxArraysPerBucket</c> times
    /// the sum of the bucket sizes, which is less than
    /// <c>2 * MaxArraysPerBucket * MaxArrayLength</c>. Arrays are only retained once they were
    /// returned, so a pool grows to the peak concurrency it has seen, up to that bound.
    /// One instance per maximum array length is shared by all buffer managers of the process,
    /// so client and server channels do not each retain their own arrays.
    /// </remarks>
    internal sealed class BoundedArrayPool : ArrayPool<byte>
    {
        /// <summary>
        /// Creates a pool for arrays up to <paramref name="maxArrayLength"/> bytes.
        /// </summary>
        /// <param name="maxArrayLength">The largest pooled array length, a power of two.</param>
        /// <param name="maxArraysPerBucket">The number of arrays retained per bucket.</param>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        internal BoundedArrayPool(int maxArrayLength, int maxArraysPerBucket)
        {
            if (maxArrayLength < kMinimumArrayLength || (maxArrayLength & (maxArrayLength - 1)) != 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxArrayLength));
            }
            if (maxArraysPerBucket <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxArraysPerBucket));
            }

            MaxArrayLength = maxArrayLength;
            MaxArraysPerBucket = maxArraysPerBucket;
            m_buckets = new Bucket[GetBucketIndex(maxArrayLength) + 1];
            for (int ii = 0; ii < m_buckets.Length; ii++)
            {
                m_buckets[ii] = new Bucket(kMinimumArrayLength << ii);
            }
        }

        /// <summary>
        /// The largest pooled array length. Larger requests are allocated and dropped on return.
        /// </summary>
        public int MaxArrayLength { get; }

        /// <summary>
        /// The number of arrays each bucket retains at most.
        /// </summary>
        public int MaxArraysPerBucket { get; }

        /// <summary>
        /// Gets the process-wide pool for arrays up to <paramref name="maxArrayLength"/> bytes,
        /// rounded up to a power of two.
        /// </summary>
        /// <param name="maxArrayLength">The largest array length the caller rents.</param>
        public static BoundedArrayPool GetShared(int maxArrayLength)
        {
            int length = kMinimumArrayLength;
            while (length < maxArrayLength)
            {
                length <<= 1;
            }
            return s_pools.GetOrAdd(
                length,
                static key => new BoundedArrayPool(key, GetDefaultMaxArraysPerBucket(key)));
        }

        /// <summary>
        /// Retains up to 64 MiB in the largest bucket, between 16 and 1024 arrays: 1024 arrays
        /// for the default 64 KiB transport buffer, enough for about a thousand responses in
        /// flight (for example 10 channels with 100 outstanding requests each).
        /// </summary>
        /// <param name="maxArrayLength">The largest pooled array length.</param>
        internal static int GetDefaultMaxArraysPerBucket(int maxArrayLength)
        {
            long arrays = kMaxRetainedBytesPerBucket / maxArrayLength;
            return (int)Math.Min(kMaxArraysPerBucket, Math.Max(kMinArraysPerBucket, arrays));
        }

        /// <summary>
        /// The number of arrays currently retained by the bucket holding
        /// <paramref name="arrayLength"/>.
        /// </summary>
        /// <param name="arrayLength">An array length up to <see cref="MaxArrayLength"/>.</param>
        internal int GetRetainedCount(int arrayLength)
        {
            return Volatile.Read(ref m_buckets[GetBucketIndex(arrayLength)].Count);
        }

        /// <inheritdoc/>
        public override byte[] Rent(int minimumLength)
        {
            if (minimumLength < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(minimumLength));
            }
            if (minimumLength == 0)
            {
                return [];
            }
            if (minimumLength > MaxArrayLength)
            {
                return new byte[minimumLength];
            }

            Bucket bucket = m_buckets[GetBucketIndex(minimumLength)];
            if (bucket.Arrays.TryDequeue(out byte[]? array))
            {
                Interlocked.Decrement(ref bucket.Count);
                return array;
            }
            return new byte[bucket.ArrayLength];
        }

        /// <inheritdoc/>
        public override void Return(byte[] array, bool clearArray = false)
        {
            if (array == null)
            {
                throw new ArgumentNullException(nameof(array));
            }
            if (array.Length == 0 || array.Length > MaxArrayLength)
            {
                return;
            }

            Bucket bucket = m_buckets[GetBucketIndex(array.Length)];
            if (array.Length != bucket.ArrayLength)
            {
                throw new ArgumentException(
                    "The buffer is not associated with this pool and may not be returned to it.",
                    nameof(array));
            }

            // count first, so concurrent returns cannot overshoot the bound
            if (Interlocked.Increment(ref bucket.Count) > MaxArraysPerBucket)
            {
                Interlocked.Decrement(ref bucket.Count);
                return;
            }
            if (clearArray)
            {
                Array.Clear(array, 0, array.Length);
            }
            bucket.Arrays.Enqueue(array);
        }

        /// <summary>
        /// Returns the index of the smallest bucket holding <paramref name="length"/> bytes.
        /// </summary>
        private static int GetBucketIndex(int length)
        {
            int index = 0;
            int bucketLength = kMinimumArrayLength;
            while (bucketLength < length)
            {
                bucketLength <<= 1;
                index++;
            }
            return index;
        }

        /// <summary>
        /// The retained arrays of one power-of-two length.
        /// </summary>
        private sealed class Bucket(int arrayLength)
        {
            /// <summary>
            /// The length of every array in this bucket.
            /// </summary>
            public readonly int ArrayLength = arrayLength;

            /// <summary>
            /// The retained arrays.
            /// </summary>
            public readonly ConcurrentQueue<byte[]> Arrays = new();

            /// <summary>
            /// The retained arrays, including returns about to be enqueued.
            /// </summary>
            public int Count;
        }

        private const int kMinimumArrayLength = 16;
        private const int kMinArraysPerBucket = 16;
        private const int kMaxArraysPerBucket = 1024;
        private const long kMaxRetainedBytesPerBucket = 64L * 1024 * 1024;
        private static readonly ConcurrentDictionary<int, BoundedArrayPool> s_pools = new();
        private readonly Bucket[] m_buckets;
    }
}
