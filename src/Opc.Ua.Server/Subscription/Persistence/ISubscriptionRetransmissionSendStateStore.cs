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
