// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Buffers;
using System.Threading;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Counts the owners of a notification message whose payload objects (the
    /// <see cref="NotificationMessage"/> and its data change list) came from their activator
    /// pools, and whose notification arrays were rented from <see cref="ArrayPool{T}.Shared"/> or
    /// <see cref="EventNotificationBuffer"/>. The payload is returned to the pools only when the last
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
            : this(message, default)
        {
        }

        /// <summary>
        /// Creates a lease with one reference owned by the creator for a message whose data
        /// change and event notifications are slices of the arrays in <paramref name="rented"/>,
        /// rented from <see cref="ArrayPool{T}.Shared"/> and returned with the payload.
        /// </summary>
        /// <param name="message">The leased message.</param>
        /// <param name="rented">The rented arrays that hold the notifications, if any.</param>
        public NotificationMessageLease(
            NotificationMessage message,
            RentedNotificationArrays rented)
        {
            Message = message ?? throw new ArgumentNullException(nameof(message));
            m_rented = rented;
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
                RecyclePayload(Message, m_rented);
            }
            else if (references < 0)
            {
                // an owner released twice; never recycle twice.
                Interlocked.Increment(ref m_references);
            }
        }


        /// <summary>
        /// Returns the pooled objects of a notification message to their activator pools and
        /// the rented arrays that hold its notifications to their array pools. The message
        /// must not be referenced by anyone afterwards.
        /// </summary>
        /// <param name="message">The message to recycle.</param>
        /// <param name="rented">The rented notification arrays to return, if any.</param>
        internal static void RecyclePayload(
            NotificationMessage message,
            RentedNotificationArrays rented)
        {
            ReadOnlySpan<ExtensionObject> data = message.NotificationData.Span;
            for (int i = 0; i < data.Length; i++)
            {
                if (data[i].TryGetValue(out DataChangeNotification? dcn))
                {
                    // the MonitoredItemNotification entries are values in the rented array.
                    (dcn as IPooledEncodeable)?.Reuse();
                }
            }
            (message as IPooledEncodeable)?.Reuse();

            // cleared, so the arrays do not keep the recycled notifications reachable.
            if (rented.Items != null)
            {
                ArrayPool<MonitoredItemNotification>.Shared.Return(rented.Items, clearArray: true);
            }
            // also releases the occurrence references (admission stamps) of the event fields.
            rented.Events?.Return();
        }

        private readonly RentedNotificationArrays m_rented;
        private int m_references;
    }

    /// <summary>
    /// The pooled arrays that hold the notifications of one notification message; the
    /// message sees exact-length slices of them.
    /// </summary>
    /// <param name="Items">
    /// The data change array rented from <see cref="ArrayPool{T}.Shared"/>, if any.
    /// </param>
    /// <param name="Events">
    /// The pooled events and their admission stamps (see <see cref="QueuedEvent"/>), if any.
    /// The stamps keep the EventIds of the reported occurrences reserved until the message
    /// is released.
    /// </param>
    internal readonly record struct RentedNotificationArrays(
        MonitoredItemNotification[]? Items,
        EventNotificationBuffer? Events = null);

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
