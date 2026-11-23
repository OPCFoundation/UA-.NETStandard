// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

namespace Opc.Ua.Server
{
    /// <summary>
    /// Mirrored retransmission state for a subscription.
    /// </summary>
    public sealed class SubscriptionRetransmissionState
    {
        /// <summary>
        /// The next sequence number the subscription should assign.
        /// </summary>
        public uint NextSequenceNumber { get; set; }

        /// <summary>
        /// Sent notifications that remain available for republish.
        /// </summary>
        public ArrayOf<NotificationMessage> SentMessages { get; set; } = [];

        /// <summary>
        /// The sequence number of the oldest message in <see cref="SentMessages"/> that is
        /// queued for a Publish response but was not yet returned, or 0 when every message
        /// was sent (or the mirror does not track it).
        /// </summary>
        /// <remarks>
        /// Populated by stores that implement <see cref="ISubscriptionRetransmissionSendStateStore"/>.
        /// </remarks>
        public uint FirstUnsentSequenceNumber { get; set; }
    }
}
