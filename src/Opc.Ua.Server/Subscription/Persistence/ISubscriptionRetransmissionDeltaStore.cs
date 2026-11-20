// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

namespace Opc.Ua.Server
{
    /// <summary>
    /// Optional delta path for live retransmission mirrors.
    /// </summary>
    /// <remarks>
    /// Implementations avoid per-publish full retransmission queue snapshots by accepting only the notifications
    /// added to and removed from the queue since the previous mirror update.
    /// </remarks>
    public interface ISubscriptionRetransmissionDeltaStore : ISubscriptionRetransmissionStore
    {
        /// <summary>
        /// Stores a retransmission queue delta for a subscription.
        /// </summary>
        /// <param name="subscriptionId">The subscription id.</param>
        /// <param name="nextSequenceNumber">The next sequence number to assign.</param>
        /// <param name="addedMessages">The newly sent notifications available for republish.</param>
        /// <param name="removedSequenceNumbers">Sequence numbers no longer available for republish.</param>
        void StoreRetransmissionStateDelta(
            uint subscriptionId,
            uint nextSequenceNumber,
            ArrayOf<NotificationMessage> addedMessages,
            ArrayOf<uint> removedSequenceNumbers);
    }
}
