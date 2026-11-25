// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Runtime.CompilerServices;

namespace Opc.Ua.Server
{
    /// <summary>
    /// The pooled array that holds the data change notifications of one notification message.
    /// </summary>
    /// <remarks>
    /// See <see cref="PooledNotificationBuffer{TBuffer}"/> for why the array is not rented
    /// from <c>ArrayPool&lt;T&gt;.Shared</c>.
    /// </remarks>
    internal sealed class DataChangeNotificationBuffer
        : PooledNotificationBuffer<DataChangeNotificationBuffer>
    {
        private DataChangeNotificationBuffer(int capacity, Bucket? bucket)
            : base(bucket)
        {
            Items = new MonitoredItemNotification[capacity];
        }

        /// <summary>
        /// The data change notifications of the message in <c>[0, Count)</c>.
        /// </summary>
        public MonitoredItemNotification[] Items { get; }

        /// <summary>
        /// The number of buffers each bucket retains at most.
        /// </summary>
        internal const int MaxBuffersPerBucket = 4096;

        /// <summary>
        /// The array bytes each bucket retains at most.
        /// </summary>
        internal const long MaxRetainedBytesPerBucket = 64L * 1024 * 1024;

        /// <summary>
        /// Rents a buffer for <paramref name="count"/> data change notifications.
        /// </summary>
        /// <param name="count">The number of notifications, at least one.</param>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        public static DataChangeNotificationBuffer Rent(int count)
        {
            return s_pool.Rent(count);
        }

        /// <summary>
        /// The number of buffers the bucket for <paramref name="count"/> notifications retains
        /// at most.
        /// </summary>
        internal static int GetMaxRetainedCount(int count)
        {
            return s_pool.GetMaxRetainedCount(count);
        }

        /// <summary>
        /// The array bytes of one entry.
        /// </summary>
        internal static int EntryBytes => Unsafe.SizeOf<MonitoredItemNotification>();

        /// <inheritdoc/>
        protected override void ClearEntries(int count)
        {
            // releases the values of the recycled notifications.
            Array.Clear(Items, 0, count);
        }

        private static readonly Pool s_pool = new(
            EntryBytes,
            MaxBuffersPerBucket,
            MaxRetainedBytesPerBucket,
            static (capacity, bucket) => new DataChangeNotificationBuffer(capacity, bucket));
    }
}
