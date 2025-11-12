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
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Provides access to the subscription manager within the server.
    /// </summary>
    /// <remarks>
    /// Sinks that receive these events must not block the thread.
    /// </remarks>
    public interface ISubscriptionManager : IDisposable
    {
        /// <summary>
        /// Raised after a new subscription is created.
        /// </summary>
        event SubscriptionEventHandler SubscriptionCreated;

        /// <summary>
        /// Raised before a subscription is deleted.
        /// </summary>
        event SubscriptionEventHandler SubscriptionDeleted;

        /// <summary>
        /// Returns all of the subscriptions known to the subscription manager.
        /// </summary>
        /// <returns>A list of the subscriptions.</returns>
        IList<ISubscription> GetSubscriptions();

        /// <summary>
        /// Set a subscription into durable mode
        /// </summary>
        ServiceResult SetSubscriptionDurable(
            ISystemContext context,
            uint subscriptionId,
            uint lifetimeInHours,
            out uint revisedLifetimeInHours);

        /// <summary>
        /// Creates a new subscription.
        /// </summary>
        void CreateSubscription(
            OperationContext context,
            double requestedPublishingInterval,
            uint requestedLifetimeCount,
            uint requestedMaxKeepAliveCount,
            uint maxNotificationsPerPublish,
            bool publishingEnabled,
            byte priority,
            out uint subscriptionId,
            out double revisedPublishingInterval,
            out uint revisedLifetimeCount,
            out uint revisedMaxKeepAliveCount);

        /// <summary>
        /// Starts up the manager makes it ready to create subscriptions.
        /// </summary>
        void Startup();

        /// <summary>
        /// Closes all subscriptions and rejects any new requests.
        /// </summary>
        void Shutdown();

        /// <summary>
        /// Stores durable subscriptions to  be able to restore them after a restart
        /// </summary>
        void StoreSubscriptions();

        /// <summary>
        /// Restore durable subscriptions after a server restart
        /// </summary>
        void RestoreSubscriptions();

        /// <summary>
        /// Deletes group of subscriptions.
        /// </summary>
        void DeleteSubscriptions(
            OperationContext context,
            UInt32Collection subscriptionIds,
            out StatusCodeCollection results,
            out DiagnosticInfoCollection diagnosticInfos);

        /// <summary>
        /// Publishes a subscription.
        /// </summary>
        Task<PublishResponse> PublishAsync(
            OperationContext context,
            SubscriptionAcknowledgementCollection subscriptionAcknowledgements,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Modifies an existing subscription.
        /// </summary>
        void ModifySubscription(
            OperationContext context,
            uint subscriptionId,
            double requestedPublishingInterval,
            uint requestedLifetimeCount,
            uint requestedMaxKeepAliveCount,
            uint maxNotificationsPerPublish,
            byte priority,
            out double revisedPublishingInterval,
            out uint revisedLifetimeCount,
            out uint revisedMaxKeepAliveCount);

        /// <summary>
        /// Sets the publishing mode for a set of subscriptions.
        /// </summary>
        void SetPublishingMode(
            OperationContext context,
            bool publishingEnabled,
            UInt32Collection subscriptionIds,
            out StatusCodeCollection results,
            out DiagnosticInfoCollection diagnosticInfos);

        /// <summary>
        /// Attaches a groups of subscriptions to a different session.
        /// </summary>
        void TransferSubscriptions(
            OperationContext context,
            UInt32Collection subscriptionIds,
            bool sendInitialValues,
            out TransferResultCollection results,
            out DiagnosticInfoCollection diagnosticInfos);

        /// <summary>
        /// Republishes a previously published notification message.
        /// </summary>
        NotificationMessage Republish(
            OperationContext context,
            uint subscriptionId,
            uint retransmitSequenceNumber);

        /// <summary>
        /// Updates the triggers for the monitored item.
        /// </summary>
        void SetTriggering(
            OperationContext context,
            uint subscriptionId,
            uint triggeringItemId,
            UInt32Collection linksToAdd,
            UInt32Collection linksToRemove,
            out StatusCodeCollection addResults,
            out DiagnosticInfoCollection addDiagnosticInfos,
            out StatusCodeCollection removeResults,
            out DiagnosticInfoCollection removeDiagnosticInfos);

        /// <summary>
        /// Adds monitored items to a subscription.
        /// </summary>
        void CreateMonitoredItems(
            OperationContext context,
            uint subscriptionId,
            TimestampsToReturn timestampsToReturn,
            MonitoredItemCreateRequestCollection itemsToCreate,
            out MonitoredItemCreateResultCollection results,
            out DiagnosticInfoCollection diagnosticInfos);

        /// <summary>
        /// Modifies monitored items in a subscription.
        /// </summary>
        void ModifyMonitoredItems(
            OperationContext context,
            uint subscriptionId,
            TimestampsToReturn timestampsToReturn,
            MonitoredItemModifyRequestCollection itemsToModify,
            out MonitoredItemModifyResultCollection results,
            out DiagnosticInfoCollection diagnosticInfos);

        /// <summary>
        /// Deletes the monitored items in a subscription.
        /// </summary>
        void DeleteMonitoredItems(
            OperationContext context,
            uint subscriptionId,
            UInt32Collection monitoredItemIds,
            out StatusCodeCollection results,
            out DiagnosticInfoCollection diagnosticInfos);

        /// <summary>
        /// Changes the monitoring mode for a set of items.
        /// </summary>
        void SetMonitoringMode(
            OperationContext context,
            uint subscriptionId,
            MonitoringMode monitoringMode,
            UInt32Collection monitoredItemIds,
            out StatusCodeCollection results,
            out DiagnosticInfoCollection diagnosticInfos);

        /// <summary>
        /// Signals that a session is closing.
        /// </summary>
        void SessionClosing(OperationContext context, NodeId sessionId, bool deleteSubscriptions);

        /// <summary>
        /// Deletes the specified subscription.
        /// </summary>
        StatusCode DeleteSubscription(OperationContext context, uint subscriptionId);

        /// <summary>
        /// Refreshes the conditions for the specified subscription.
        /// </summary>
        void ConditionRefresh(OperationContext context, uint subscriptionId);

        /// <summary>
        /// Refreshes the conditions for the specified subscription and monitored item.
        /// </summary>
        void ConditionRefresh2(OperationContext context, uint subscriptionId, uint monitoredItemId);
    }

    /// <summary>
    /// The delegate for functions used to receive subscription related events.
    /// </summary>
    /// <param name="subscription">The subscription that was affected.</param>
    /// <param name="deleted">True if the subscription was deleted.</param>
    public delegate void SubscriptionEventHandler(ISubscription subscription, bool deleted);
}
