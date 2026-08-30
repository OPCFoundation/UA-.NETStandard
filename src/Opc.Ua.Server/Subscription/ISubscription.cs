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

namespace Opc.Ua.Server
{
    /// <summary>
    /// An interface used by the monitored items to signal the subscription.
    /// </summary>
    public interface ISubscription : IDisposable
    {
        /// <summary>
        /// The session that owns the monitored item.
        /// </summary>
        ISession Session { get; }

        /// <summary>
        /// The subscriptions owner identity.
        /// </summary>
        IUserIdentity EffectiveIdentity { get; }

        /// <summary>
        /// The identifier for the item that is unique within the server.
        /// </summary>
        uint Id { get; }

        /// <summary>
        /// The identifier for the session that owns the subscription.
        /// </summary>
        NodeId SessionId { get; }

        /// <summary>
        /// The number of monitored items.
        /// </summary>
        int MonitoredItemCount { get; }

        /// <summary>
        /// The priority assigned to the subscription.
        /// </summary>
        byte Priority { get; }

        /// <summary>
        /// The publishing rate for the subscription.
        /// </summary>
        double PublishingInterval { get; }

        /// <summary>
        /// True if the subscription is set to durable and supports long lifetime and queue size
        /// </summary>
        bool IsDurable { get; }

        /// <summary>
        /// True once the subscription has been deleted via <see cref="DeleteAsync"/>. Publicly
        /// callable members throw Bad_SubscriptionIdInvalid once this is set.
        /// </summary>
        bool IsDeleted { get; }

        /// <summary>
        /// Applies an update to the subscription diagnostics while holding the
        /// subscription's diagnostics lock.
        /// </summary>
        /// <remarks>
        /// The subscription owns its lock and never exposes it, so callers cannot
        /// participate in the server's locking order. The diagnostic nodes are marked dirty
        /// inside the critical section.
        /// </remarks>
        /// <param name="update">The mutation to apply to the diagnostics.</param>
        void UpdateDiagnostics(Action<SubscriptionDiagnosticsDataType> update);

        /// <summary>
        /// Reads a value derived from the subscription diagnostics while holding the
        /// subscription's diagnostics lock.
        /// </summary>
        /// <remarks>
        /// Do not let the diagnostics object escape the callback: once the lock is
        /// released, any field read from it is unsynchronized.
        /// </remarks>
        /// <typeparam name="TResult">The type of the value produced.</typeparam>
        /// <param name="read">The projection applied to the diagnostics.</param>
        TResult ReadDiagnostics<TResult>(Func<SubscriptionDiagnosticsDataType, TResult> read);

        /// <summary>
        /// Gets the current diagnostics for the subscription.
        /// </summary>
        SubscriptionDiagnosticsDataType Diagnostics { get; }

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
        /// Refreshes the conditions.
        /// </summary>
        ValueTask ConditionRefresh2Async(uint monitoredItemId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Refreshes the conditions.
        /// </summary>
        ValueTask ConditionRefreshAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Updates the publishing parameters for the subscription.
        /// </summary>
        void Modify(
            OperationContext context,
            double publishingInterval,
            uint maxLifetimeCount,
            uint maxKeepAliveCount,
            uint maxNotificationsPerPublish,
            byte priority);

        /// <summary>
        /// Changes the monitoring mode for a set of items.
        /// </summary>
        ValueTask<(ArrayOf<StatusCode> results, ArrayOf<DiagnosticInfo> diagnosticInfos)> SetMonitoringModeAsync(
            OperationContext context,
            MonitoringMode monitoringMode,
            ArrayOf<uint> monitoredItemIds,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Enables/disables publishing for the subscription.
        /// </summary>
        void SetPublishingMode(OperationContext context, bool publishingEnabled);

        /// <summary>
        /// Deletes the monitored items in a subscription.
        /// </summary>
        ValueTask<DeleteMonitoredItemsResponse> DeleteMonitoredItemsAsync(
            OperationContext context,
            ArrayOf<uint> monitoredItemIds,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Modifies monitored items in a subscription.
        /// </summary>
        ValueTask<ModifyMonitoredItemsResponse> ModifyMonitoredItemsAsync(
            OperationContext context,
            TimestampsToReturn timestampsToReturn,
            ArrayOf<MonitoredItemModifyRequest> itemsToModify,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Adds monitored items to a subscription.
        /// </summary>
        ValueTask<CreateMonitoredItemsResponse> CreateMonitoredItemsAsync(
            OperationContext context,
            TimestampsToReturn timestampsToReturn,
            ArrayOf<MonitoredItemCreateRequest> itemsToCreate,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets the monitored items for the subscription.
        /// </summary>
        void GetMonitoredItems(
            out ArrayOf<uint> serverHandles,
            out ArrayOf<uint> clientHandles);

        /// <summary>
        /// Sets the subscription to durable mode.
        /// </summary>
        ServiceResult SetSubscriptionDurable(uint maxLifetimeCount);

        /// <summary>
        /// Initiates resending of all data monitored items in a Subscription
        /// </summary>
        void ResendData(OperationContext context);

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
        /// Deletes the subscription.
        /// </summary>
        ValueTask DeleteAsync(OperationContext context, CancellationToken cancellationToken = default);

        /// <summary>
        /// Verifies that a condition refresh operation is permitted.
        /// </summary>
        void ValidateConditionRefresh(OperationContext context);

        /// <summary>
        /// Verifies that a condition refresh operation is permitted.
        /// </summary>
        void ValidateConditionRefresh2(OperationContext context, uint monitoredItemId);

        /// <summary>
        /// Returns a cached notification message.
        /// </summary>
        NotificationMessage Republish(OperationContext context, uint retransmitSequenceNumber);

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
        /// Determines whether the authenticated owner of a target Session is compatible
        /// with the identity that owns this subscription.
        /// </summary>
        /// <param name="targetSession">The target Session for a transfer request.</param>
        /// <returns>
        /// <c>true</c> when the target Session represents the same ClientUserId; otherwise, <c>false</c>.
        /// </returns>
        bool IsTransferIdentityCompatible(ISession targetSession);

        /// <summary>
        /// Transfers the subscription to a new session.
        /// </summary>
        /// <param name="context">The session to which the subscription is transferred.</param>
        /// <param name="sendInitialValues">Whether the first Publish response shall contain current values.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        ValueTask TransferSessionAsync(OperationContext context, bool sendInitialValues, CancellationToken cancellationToken = default);

        /// <summary>
        /// Updates the triggers for the monitored item.
        /// </summary>
        void SetTriggering(
            OperationContext context,
            uint triggeringItemId,
            ArrayOf<uint> linksToAdd,
            ArrayOf<uint> linksToRemove,
            out ArrayOf<StatusCode> addResults,
            out ArrayOf<DiagnosticInfo> addDiagnosticInfos,
            out ArrayOf<StatusCode> removeResults,
            out ArrayOf<DiagnosticInfo> removeDiagnosticInfos);

        /// <summary>
        /// Return a StorableSubscription for restore after a server restart
        /// </summary>
        IStoredSubscription ToStorableSubscription();
    }
}
