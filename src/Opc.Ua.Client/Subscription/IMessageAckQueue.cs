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
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Client.Subscriptions
{
    /// <summary>
    /// Message acknoledgement queue
    /// </summary>
    internal interface IMessageAckQueue
    {
        /// <summary>
        /// Subscriptions queue acknoledgements for completed
        /// notifications as soon as they are dispatched / handled.
        /// </summary>
        /// <param name="ack"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        ValueTask QueueAsync(SubscriptionAcknowledgement ack,
            CancellationToken ct = default);

        /// <summary>
        /// Complete subscription
        /// </summary>
        /// <param name="subscriptionId"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        ValueTask CompleteAsync(uint subscriptionId,
            CancellationToken ct = default);

        /// <summary>
        /// Pauses publish ingress, drains in-flight publish requests, runs the
        /// supplied operation, and then restores the manager's requested
        /// publishing state.
        /// </summary>
        /// <param name="operation">Operation to run while publish ingress is
        /// quiesced.</param>
        /// <param name="ct">Cancellation token.</param>
        ValueTask RunWithPublishingQuiescedAsync(
            Func<CancellationToken, ValueTask> operation,
            CancellationToken ct = default);

        /// <summary>
        /// Drops every queued acknowledgement targeting the given
        /// <paramref name="subscriptionId"/>. Used by the
        /// recreate path after the old server-side subscription has
        /// been retired and while publish ingress remains quiesced.
        /// This prevents stale acknowledgements from crossing into a
        /// replacement generation when a server reuses identifiers.
        /// </summary>
        /// <param name="subscriptionId">The subscription id whose
        /// queued acknowledgements should be dropped.</param>
        /// <returns>The number of queued acknowledgements that
        /// were dropped.</returns>
        int DropPendingForSubscription(uint subscriptionId);

        /// <summary>
        /// Notify the queue/manager that the subscription's state has
        /// changed (created, modified, etc.) and the publish controller
        /// should re-evaluate worker counts and resume publishing.
        /// </summary>
        void Update();

        /// <summary>
        /// Whether the V2 subscription dispatcher should call
        /// <see cref="IPooledEncodeable.Reuse"/> on notification payload
        /// instances after the handler returns. The value reflects the
        /// current <see cref="ISubscriptionManager.PoolNotifications"/>
        /// setting; toggling it on the manager takes effect on the next
        /// dispatch.
        /// </summary>
        bool PoolNotifications { get; }
    }
}
