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
using System.Buffers;
using System.Threading;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Counts the owners of a notification message whose payload objects (the
    /// <see cref="NotificationMessage"/>, its notification lists and their
    /// <see cref="MonitoredItemNotification"/>/<see cref="EventFieldList"/> entries) came
    /// from their activator pools. The payload is returned to the pools only when the last
    /// owner releases it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The retransmission queue owns one reference for as long as it retains the message.
    /// Every Publish response that carries the message owns another one, released by the
    /// transport once the response has been encoded or abandoned (see
    /// <see cref="LeasedPublishResponse"/>). A client may acknowledge a message before the
    /// server has finished sending it, so neither event alone may recycle the payload.
    /// </para>
    /// <para>
    /// The message is shared by its owners and must be treated as immutable while it is
    /// leased. A reference that is never released only means that the payload is left to
    /// the garbage collector instead of being pooled; a missing release is therefore safe,
    /// an extra release is not, which is why every owner releases at most once.
    /// </para>
    /// </remarks>
    internal sealed class NotificationMessageLease
    {
        /// <summary>
        /// Creates a lease with one reference owned by the creator.
        /// </summary>
        public NotificationMessageLease(NotificationMessage message)
            : this(message, null)
        {
        }

        /// <summary>
        /// Creates a lease with one reference owned by the creator for a message whose data
        /// change notifications are a slice of <paramref name="rentedItems"/>, an array rented
        /// from <see cref="ArrayPool{T}.Shared"/> that is returned with the payload.
        /// </summary>
        public NotificationMessageLease(NotificationMessage message, MonitoredItemNotification[]? rentedItems)
        {
            Message = message ?? throw new ArgumentNullException(nameof(message));
            m_rentedItems = rentedItems;
            m_references = 1;
        }

        /// <summary>
        /// The leased message.
        /// </summary>
        public NotificationMessage Message { get; }

        /// <summary>
        /// The number of owners that have not released the message yet.
        /// </summary>
        internal int References => Volatile.Read(ref m_references);

        /// <summary>
        /// Adds a reference for a new owner. Only an existing owner may add one, so the
        /// count is never raised from zero.
        /// </summary>
        public void AddReference()
        {
            Interlocked.Increment(ref m_references);
        }

        /// <summary>
        /// Releases one reference and recycles the payload when it was the last one.
        /// </summary>
        public void Release()
        {
            int references = Interlocked.Decrement(ref m_references);
            if (references == 0)
            {
                RecyclePayload(Message, m_rentedItems);
            }
            else if (references < 0)
            {
                // an owner released twice; never recycle twice.
                Interlocked.Increment(ref m_references);
            }
        }

        /// <summary>
        /// Returns the pooled objects of a notification message to their activator pools.
        /// The message must not be referenced by anyone afterwards.
        /// </summary>
        internal static void RecyclePayload(NotificationMessage message)
        {
            RecyclePayload(message, null);
        }

        /// <summary>
        /// Returns the pooled objects of a notification message to their activator pools and
        /// the rented array that holds its data change notifications to its array pool. The
        /// message must not be referenced by anyone afterwards.
        /// </summary>
        internal static void RecyclePayload(NotificationMessage message, MonitoredItemNotification[]? rentedItems)
        {
            ReadOnlySpan<ExtensionObject> data = message.NotificationData.Span;
            for (int i = 0; i < data.Length; i++)
            {
                if (data[i].TryGetValue(out DataChangeNotification? dcn))
                {
                    ReadOnlySpan<MonitoredItemNotification> items =
                        dcn.MonitoredItems.Span;
                    for (int j = 0; j < items.Length; j++)
                    {
                        (items[j] as IPooledEncodeable)?.Reuse();
                    }
                    (dcn as IPooledEncodeable)?.Reuse();
                }
                else if (data[i].TryGetValue(out EventNotificationList? enl))
                {
                    ReadOnlySpan<EventFieldList> events = enl.Events.Span;
                    for (int j = 0; j < events.Length; j++)
                    {
                        (events[j] as IPooledEncodeable)?.Reuse();
                    }
                    (enl as IPooledEncodeable)?.Reuse();
                }
            }
            (message as IPooledEncodeable)?.Reuse();

            // cleared, so the array does not keep the recycled notifications reachable.
            if (rentedItems != null)
            {
                ArrayPool<MonitoredItemNotification>.Shared.Return(rentedItems, clearArray: true);
            }
        }

        private readonly MonitoredItemNotification[]? m_rentedItems;
        private int m_references;
    }

    /// <summary>
    /// A Publish response that owns a reference to the notification message it carries
    /// and releases it when the transport has finished with the response.
    /// </summary>
    /// <remarks>
    /// The transports call <see cref="IPooledEncodeable.Reuse"/> on a response once it has
    /// been encoded for sending, or when it is dropped because sending failed. That call is
    /// the send-completion signal of the lease. The response itself is not returned to the
    /// <see cref="PublishResponse"/> pool, because a pooled instance would be handed out as
    /// a plain <see cref="PublishResponse"/> later.
    /// </remarks>
    internal sealed class LeasedPublishResponse : PublishResponse
    {
        /// <summary>
        /// Creates a response that takes over the caller's reference on the lease.
        /// </summary>
        public LeasedPublishResponse(NotificationMessageLease? lease)
        {
            m_lease = lease;
        }

        /// <inheritdoc/>
        protected override void ReuseCore()
        {
            Interlocked.Exchange(ref m_lease, null)?.Release();
        }

        /// <inheritdoc/>
        public override object Clone()
        {
            // the clone deep-copies the message, so it owns no reference.
            var clone = (LeasedPublishResponse)base.Clone();
            clone.m_lease = null;
            return clone;
        }

        private NotificationMessageLease? m_lease;
    }
}
