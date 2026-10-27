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
        /// Queues an acknowledgement stamped with the processor that
        /// produced it and that processor's message generation. Before
        /// the acknowledgement is sent, it is discarded if the owner was
        /// removed, its server id changed, a more recently created
        /// subscription now holds the id, its generation advanced, or the
        /// server no longer holds the message.
        /// </summary>
        /// <param name="owner">The processor that produced the
        /// acknowledgement.</param>
        /// <param name="generation">The owner's message generation when
        /// the acknowledged message was processed.</param>
        /// <param name="ack">The acknowledgement.</param>
        /// <param name="ct">Cancellation token.</param>
        ValueTask QueueAsync(IMessageProcessor owner, long generation,
            SubscriptionAcknowledgement ack, CancellationToken ct = default);

        /// <summary>
        /// Starts tracking the acknowledgement of a message before the message is
        /// delivered, so that a confirmation the application makes while handling it
        /// is accepted.
        /// </summary>
        /// <param name="owner">The processor that delivers the message.</param>
        /// <param name="generation">The owner's message generation.</param>
        /// <param name="sequenceNumber">The sequence number of the message.</param>
        void TrackAcknowledgement(IMessageProcessor owner, long generation, uint sequenceNumber);

        /// <summary>
        /// Records that <paramref name="owner"/> was just assigned
        /// <paramref name="subscriptionId"/> by the server. The most
        /// recently created owner of an identifier takes precedence over
        /// any other subscription still registered under it.
        /// </summary>
        /// <param name="owner">The created subscription.</param>
        /// <param name="subscriptionId">The server-assigned id.</param>
        /// <param name="createdAt">The server time of the CreateSubscription
        /// response, or null if unknown. A message the server published
        /// earlier belongs to the id's previous subscription.</param>
        void OnSubscriptionCreated(IMessageProcessor owner, uint subscriptionId,
            DateTimeUtc createdAt = default);

        /// <summary>
        /// Complete subscription
        /// </summary>
        /// <param name="subscription">The completing subscription.</param>
        /// <param name="subscriptionId">The last server-assigned id of the
        /// subscription (0 when it was never created).</param>
        /// <param name="ct"></param>
        /// <returns></returns>
        ValueTask CompleteAsync(IMessageProcessor subscription,
            uint subscriptionId,
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
        /// Runs an automatic subscription update while the session is available.
        /// Session recovery cancels an active pass and defers its retry until
        /// explicit subscription restoration has completed.
        /// </summary>
        /// <param name="operation">The update pass to run.</param>
        /// <param name="ct">Cancellation token for the update worker.</param>
        ValueTask RunWithSessionAvailableAsync(
            Func<CancellationToken, ValueTask> operation,
            CancellationToken ct = default);

        /// <summary>
        /// Retires the acknowledgement state <paramref name="owner"/> holds
        /// under <paramref name="subscriptionId"/> and removes its deferred
        /// acknowledgements. Used when a subscription is recreated, deleted
        /// or completed. Queued and in-flight acknowledgements fail send-time
        /// validation; when an observer receives outcomes, those of the retired
        /// lifetime are reported as retired right away.
        /// </summary>
        /// <param name="owner">The subscription retiring the id.</param>
        /// <param name="subscriptionId">The retired server id.</param>
        /// <returns>The number of deferred acknowledgements that were
        /// dropped.</returns>
        int DropPendingForSubscription(IMessageProcessor owner, uint subscriptionId);

        /// <summary>
        /// Whether <paramref name="subscriptionId"/> still resolves to
        /// <paramref name="subscription"/> in the dispatch registry. A server
        /// that recycles identifiers after a session recreate can hand the id
        /// of a retired subscription to a sibling that recreated first;
        /// deleting that id would then tear down the sibling's live
        /// subscription.
        /// </summary>
        /// <param name="subscription">The subscription asking.</param>
        /// <param name="subscriptionId">The server-assigned id to check.</param>
        bool OwnsSubscriptionId(IMessageProcessor subscription, uint subscriptionId);

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
