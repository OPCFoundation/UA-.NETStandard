// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

namespace Opc.Ua.Server
{
    /// <summary>
    /// Optional extension of a live retransmission mirror that also tracks which retained
    /// notifications are still queued for a Publish response.
    /// </summary>
    /// <remarks>
    /// A subscription can retain messages that were produced for a Publish response with
    /// MoreNotifications set but not yet returned. Without this information a replica that
    /// restores the mirror treats them as sent, so they are only reachable through Republish.
    /// Implementations return the stored value in
    /// <see cref="SubscriptionRetransmissionState.FirstUnsentSequenceNumber"/>.
    /// </remarks>
    public interface ISubscriptionRetransmissionSendStateStore : ISubscriptionRetransmissionStore
    {
        /// <summary>
        /// Stores the sequence number of the oldest retained notification that was not yet
        /// returned by a Publish response.
        /// </summary>
        /// <remarks>
        /// The next sequence number is passed along because the send state can change before
        /// any retransmission state was stored by this instance (for example right after a
        /// replica restored the subscription), and the stored record carries both values.
        /// </remarks>
        /// <param name="subscriptionId">The subscription id.</param>
        /// <param name="nextSequenceNumber">
        /// The sequence number the subscription assigns to its next notification message.
        /// </param>
        /// <param name="firstUnsentSequenceNumber">
        /// The sequence number of the oldest unsent notification, or 0 when every retained
        /// notification was sent.
        /// </param>
        void StoreFirstUnsentSequenceNumber(
            uint subscriptionId,
            uint nextSequenceNumber,
            uint firstUnsentSequenceNumber);
    }
}
