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

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Client.Subscriptions
{
    /// <summary>
    /// Non sdk interface that allows subscription manager to manage
    /// subcriptions. Must be implemented by subscriptions to be
    /// manageable by the subscription manager.
    /// </summary>
    internal interface IManagedSubscription : ISubscription, IMessageProcessor
    {
        /// <summary>
        /// Called after the subscription was transferred.
        /// </summary>
        /// <param name="availableSequenceNumbers">A list of sequence number
        /// ranges that identify NotificationMessages that are in the
        /// Subscription’s retransmission queue.
        /// </param>
        /// <param name="ct">The cancellation token.</param>
        ValueTask<bool> TryCompleteTransferAsync(
            IReadOnlyList<uint> availableSequenceNumbers,
            CancellationToken ct = default);

        /// <summary>
        /// Recreate the subscription on a new session
        /// </summary>
        /// <param name="ct"></param>
        /// <returns></returns>
        ValueTask RecreateAsync(CancellationToken ct = default);

        /// <summary>
        /// Notify subscription that the subscription manager has paused or
        /// resumed operations.
        /// </summary>
        /// <param name="paused"></param>
        void NotifySubscriptionManagerPaused(bool paused);
    }
}
