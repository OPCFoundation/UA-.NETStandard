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
