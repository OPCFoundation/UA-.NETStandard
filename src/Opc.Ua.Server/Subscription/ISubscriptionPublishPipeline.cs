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
    /// The publish-pipeline protocol between the subscription and the two components that
    /// drive it: <see cref="SubscriptionManager"/> and <see cref="SessionPublishQueue"/>.
    /// </summary>
    /// <remarks>
    /// Implemented only by <see cref="Subscription"/>. These members mutate the publishing
    /// state machine (timer expiry, message acknowledgement, notification consumption), so
    /// they are deliberately kept off the public <see cref="ISubscription"/> surface - a
    /// caller outside the pipeline invoking them would corrupt the publishing state.
    /// </remarks>
    internal interface ISubscriptionPublishPipeline : ISubscription
    {
        /// <summary>
        /// Called when a value of monitored item is discarded in the monitoring queue.
        /// </summary>
        void QueueOverflowHandler();

        /// <summary>
        /// Checks if the subscription is ready to publish.
        /// </summary>
        PublishingState PublishTimerExpired();

        /// <summary>
        /// Returns the available sequence numbers for retransmission
        /// For example used in Transfer Subscription
        /// </summary>
        ArrayOf<uint> AvailableSequenceNumbersForRetransmission();

        /// <summary>
        /// Tells the subscription that a session is being closed, and releases the subscription
        /// only when that session still owns it.
        /// <para>
        /// A subscription can be transferred to another session while the old one is closing, so
        /// the closing session has to be passed in: clearing the owner unconditionally would strip
        /// a subscription that has already moved on.
        /// </para>
        /// </summary>
        /// <param name="closingSession">The session that is being closed.</param>
        /// <returns><c>true</c> when the subscription was released by this call.</returns>
        bool SessionClosed(ISession closingSession);

        /// <summary>
        /// Removes a message from the message queue.
        /// </summary>
        ServiceResult? Acknowledge(OperationContext context, uint sequenceNumber);

        /// <summary>
        /// Publishes a timeout status message.
        /// </summary>
        NotificationMessage PublishTimeout();

        /// <summary>
        /// Publishes a SubscriptionTransferred status message.
        /// </summary>
        NotificationMessage SubscriptionTransferred();

        /// <summary>
        /// Returns all available notifications.
        /// </summary>
        NotificationMessage? Publish(
            OperationContext context,
            out ArrayOf<uint> availableSequenceNumbers,
            out bool moreNotifications);

        /// <summary>
        /// Reserves the subscription for transfer while it is still owned by the expected source session.
        /// </summary>
        /// <param name="sourceSession">The session that currently owns the subscription.</param>
        /// <returns><c>true</c> when the transfer reservation was acquired.</returns>
        bool TryBeginTransfer(ISession? sourceSession);

        /// <summary>
        /// Prepares monitored item state for a session transfer without making the new owner visible yet.
        /// </summary>
        /// <param name="context">The operation context for the destination session.</param>
        /// <param name="sourceSession">The session that currently owns the subscription.</param>
        /// <param name="sendInitialValues">Whether initial values should be sent after transfer commits.</param>
        /// <param name="cancellationToken">The token that aborts transfer preparation.</param>
        /// <returns>A prepared transfer that can be committed or rolled back by the caller.</returns>
        /// <exception cref="ServiceResultException">
        /// The subscription is no longer reserved by the source session.
        /// </exception>
        ValueTask<Subscription.PreparedSessionTransfer> PrepareSessionTransferAsync(
            OperationContext context,
            ISession? sourceSession,
            bool sendInitialValues,
            CancellationToken cancellationToken);

        /// <summary>
        /// Releases the transfer reservation after the destination session is already the owner.
        /// </summary>
        /// <param name="destinationSession">The session that must currently own the subscription.</param>
        /// <exception cref="ServiceResultException">Ownership changed before transfer completion.</exception>
        void CompleteTransfer(ISession destinationSession);

        /// <summary>
        /// Releases a transfer reservation without changing ownership when preparation cannot continue.
        /// </summary>
        /// <param name="sourceSession">The source session that still owns the subscription.</param>
        void AbortTransfer(ISession? sourceSession);

        /// <summary>
        /// Restores ownership if a transfer failed after assigning its destination.
        /// </summary>
        bool TryRestoreSessionAfterFailedTransfer(
            ISession destinationSession,
            ISession? sourceSession);
    }
}
