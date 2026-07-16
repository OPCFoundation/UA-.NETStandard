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
        /// Drops every queued acknowledgement targeting the given
        /// <paramref name="subscriptionId"/>. Used by the
        /// recovery-on-unsolicited-transfer path to prevent stale
        /// acks from generating <c>BadSubscriptionIdInvalid</c>
        /// responses after the subscription has been invalidated
        /// and is about to be recreated. Must be invoked while the
        /// old subscription id is still uniquely "dead" — i.e.
        /// before recreate assigns a new id — because servers that
        /// re-use subscription identifiers (e.g. Kepware always
        /// starting at <c>1</c>) would otherwise collide
        /// generations.
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
