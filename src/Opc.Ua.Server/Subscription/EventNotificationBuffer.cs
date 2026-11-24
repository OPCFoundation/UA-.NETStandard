// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Runtime.CompilerServices;

namespace Opc.Ua.Server
{
    /// <summary>
    /// The pooled arrays that hold the events of one notification message and the admission
    /// stamps of those events, side by side.
    /// </summary>
    /// <remarks>
    /// Both arrays are held in one object so a message rents and returns once. See
    /// <see cref="PooledNotificationBuffer{TBuffer}"/> for why the arrays are not rented from
    /// <c>ArrayPool&lt;T&gt;.Shared</c>.
    /// </remarks>
    internal sealed class EventNotificationBuffer : PooledNotificationBuffer<EventNotificationBuffer>
    {
        private EventNotificationBuffer(int capacity, Bucket? bucket)
            : base(bucket)
        {
            Events = new EventFieldList[capacity];
            Stamps = new object?[capacity];
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
            return s_pool.Rent(count);
        }

        /// <summary>
        /// The number of buffers the bucket for <paramref name="count"/> events retains at most.
        /// </summary>
        internal static int GetMaxRetainedCount(int count)
        {
            return s_pool.GetMaxRetainedCount(count);
        }

        /// <inheritdoc/>
        protected override void ClearEntries(int count)
        {
            // releases the field values and the admission stamps (and so the EventIds).
            Array.Clear(Events, 0, count);
            Array.Clear(Stamps, 0, count);
        }

        private static readonly Pool s_pool = new(
            Unsafe.SizeOf<EventFieldList>() + IntPtr.Size,
            MaxBuffersPerBucket,
            MaxRetainedBytesPerBucket,
            static (capacity, bucket) => new EventNotificationBuffer(capacity, bucket));
    }
}
