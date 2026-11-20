/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Optional live retransmission mirror for subscription sequence numbers and sent notifications.
    /// </summary>
    /// <remarks>
    /// Implementations are used only when a server's <see cref="ISubscriptionStore"/> also implements this interface.
    /// Single-replica servers that do not configure a provider keep the existing in-memory retransmission behavior.
    /// </remarks>
    public interface ISubscriptionRetransmissionStore
    {
        /// <summary>
        /// Loads the latest mirrored retransmission state for a subscription.
        /// </summary>
        /// <param name="subscriptionId">The subscription id.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The restored retransmission state, or <c>null</c> when no mirrored state exists.</returns>
        ValueTask<SubscriptionRetransmissionState?> LoadRetransmissionStateAsync(
            uint subscriptionId,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Stores the current retransmission snapshot for a subscription.
        /// </summary>
        /// <param name="subscriptionId">The subscription id.</param>
        /// <param name="nextSequenceNumber">The next sequence number to assign.</param>
        /// <param name="sentMessages">The sent notifications still available for republish.</param>
        void StoreRetransmissionState(
            uint subscriptionId,
            uint nextSequenceNumber,
            ArrayOf<NotificationMessage> sentMessages);

        /// <summary>
        /// Removes an acknowledged notification from the mirrored retransmission cache.
        /// </summary>
        /// <param name="subscriptionId">The subscription id.</param>
        /// <param name="sequenceNumber">The acknowledged sequence number.</param>
        void AcknowledgeNotification(uint subscriptionId, uint sequenceNumber);
    }
}
