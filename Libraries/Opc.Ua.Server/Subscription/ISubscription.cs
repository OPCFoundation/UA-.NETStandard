/* ========================================================================
 * Copyright (c) 2005-2020 The OPC Foundation, Inc. All rights reserved.
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
        /// Gets the lock that must be acquired before accessing the contents of the Diagnostics property.
        /// </summary>
        object DiagnosticsLock { get; }

        /// <summary>
        /// Gets the lock that must be acquired before updating the contents of the Diagnostics property.
        /// </summary>
        object DiagnosticsWriteLock { get; }

        /// <summary>
        /// Gets the current diagnostics for the subscription.
        /// </summary>
        public SubscriptionDiagnosticsDataType Diagnostics { get; }

        /// <summary>
        /// Called when a monitored item is ready to publish.
        /// </summary>
        void ItemReadyToPublish(IMonitoredItem monitoredItem);

        /// <summary>
        /// Called when a monitored item is ready to publish.
        /// </summary>
        void ItemNotificationsAvailable(IMonitoredItem monitoredItem);

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
        public UInt32Collection AvailableSequenceNumbersForRetransmission();

        /// <summary>
        /// Refreshes the conditions.
        /// </summary>
        void ConditionRefresh2(uint monitoredItemId);

        /// <summary>
        /// Refreshes the conditions.
        /// </summary>
        void ConditionRefresh();

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
        void SetMonitoringMode(
            OperationContext context,
            MonitoringMode monitoringMode,
            UInt32Collection monitoredItemIds,
            out StatusCodeCollection results,
            out DiagnosticInfoCollection diagnosticInfos);

        /// <summary>
        /// Enables/disables publishing for the subscription.
        /// </summary>
        void SetPublishingMode(
            OperationContext context,
            bool publishingEnabled);

        /// <summary>
        /// Deletes the monitored items in a subscription.
        /// </summary>
        void DeleteMonitoredItems(
            OperationContext context,
            UInt32Collection monitoredItemIds,
            out StatusCodeCollection results,
            out DiagnosticInfoCollection diagnosticInfos);

        /// <summary>
        /// Modifies monitored items in a subscription.
        /// </summary>
        void ModifyMonitoredItems(
            OperationContext context,
            TimestampsToReturn timestampsToReturn,
            MonitoredItemModifyRequestCollection itemsToModify,
            out MonitoredItemModifyResultCollection results,
            out DiagnosticInfoCollection diagnosticInfos);

        /// <summary>
        /// Adds monitored items to a subscription.
        /// </summary>
        void CreateMonitoredItems(
            OperationContext context,
            TimestampsToReturn timestampsToReturn,
            MonitoredItemCreateRequestCollection itemsToCreate,
            out MonitoredItemCreateResultCollection results,
            out DiagnosticInfoCollection diagnosticInfos);

        /// <summary>
        /// Gets the monitored items for the subscription.
        /// </summary>
        void GetMonitoredItems(out uint[] serverHandles, out uint[] clientHandles);

        /// <summary>
        /// Sets the subscription to durable mode.
        /// </summary>
        public ServiceResult SetSubscriptionDurable(uint maxLifetimeCount);

        /// <summary>
        /// Initiates resending of all data monitored items in a Subscription
        /// </summary>
        void ResendData(OperationContext context);

        /// <summary>
        /// Tells the subscription that the owning session is being closed.
        /// </summary>
        void SessionClosed();

        /// <summary>
        /// Removes a message from the message queue.
        /// </summary>
        ServiceResult Acknowledge(OperationContext context, uint sequenceNumber);

        /// <summary>
        /// Deletes the subscription.
        /// </summary>
        void Delete(OperationContext context);

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
        NotificationMessage Republish(
            OperationContext context,
            uint retransmitSequenceNumber);

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
        NotificationMessage Publish(
            OperationContext context,
            out UInt32Collection availableSequenceNumbers,
            out bool moreNotifications);

        /// <summary>
        /// Transfers the subscription to a new session.
        /// </summary>
        /// <param name="context">The session to which the subscription is transferred.</param>
        /// <param name="sendInitialValues">Whether the first Publish response shall contain current values.</param>
        void TransferSession(OperationContext context, bool sendInitialValues);

        /// <summary>
        /// Updates the triggers for the monitored item.
        /// </summary>
        public void SetTriggering(
            OperationContext context,
            uint triggeringItemId,
            UInt32Collection linksToAdd,
            UInt32Collection linksToRemove,
            out StatusCodeCollection addResults,
            out DiagnosticInfoCollection addDiagnosticInfos,
            out StatusCodeCollection removeResults,
            out DiagnosticInfoCollection removeDiagnosticInfos);

        /// <summary>
        /// Return a StorableSubscription for restore after a server restart
        /// </summary>
        public IStoredSubscription ToStorableSubscription();
    }
}
