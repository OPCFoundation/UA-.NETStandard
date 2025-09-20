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

using Microsoft.Extensions.Logging;
using Opc.Ua.Server.Tests;

namespace Opc.Ua.Client.Tests
{
    /// <summary>
    /// Map test services to client session API.
    /// </summary>
    public class ClientTestServices : IServerTestServices
    {
        private readonly ISession m_session;

        public ITelemetryContext Telemetry { get; }
        public ILogger Logger { get; }

        public ClientTestServices(ISession session, ITelemetryContext telemetry)
        {
            m_session = session;
            Telemetry = telemetry;
            Logger = telemetry.CreateLogger<ClientTestServices>();
        }

        public ResponseHeader Browse(
            RequestHeader requestHeader,
            ViewDescription view,
            uint requestedMaxReferencesPerNode,
            BrowseDescriptionCollection nodesToBrowse,
            out BrowseResultCollection results,
            out DiagnosticInfoCollection diagnosticInfos)
        {
            BrowseResponse response = m_session.BrowseAsync(
                requestHeader,
                view,
                requestedMaxReferencesPerNode,
                nodesToBrowse,
                default).GetAwaiter().GetResult();
            results = response.Results;
            diagnosticInfos = response.DiagnosticInfos;
            return response.ResponseHeader;
        }

        public ResponseHeader BrowseNext(
            RequestHeader requestHeader,
            bool releaseContinuationPoints,
            ByteStringCollection continuationPoints,
            out BrowseResultCollection results,
            out DiagnosticInfoCollection diagnosticInfos)
        {
            BrowseNextResponse response = m_session.BrowseNextAsync(
                requestHeader,
                releaseContinuationPoints,
                continuationPoints,
                default).GetAwaiter().GetResult();
            results = response.Results;
            diagnosticInfos = response.DiagnosticInfos;
            return response.ResponseHeader;
        }

        public ResponseHeader CreateSubscription(
            RequestHeader requestHeader,
            double requestedPublishingInterval,
            uint requestedLifetimeCount,
            uint requestedMaxKeepAliveCount,
            uint maxNotificationsPerPublish,
            bool publishingEnabled,
            byte priority,
            out uint subscriptionId,
            out double revisedPublishingInterval,
            out uint revisedLifetimeCount,
            out uint revisedMaxKeepAliveCount)
        {
            CreateSubscriptionResponse response = m_session.CreateSubscriptionAsync(
                requestHeader,
                requestedPublishingInterval,
                requestedLifetimeCount,
                requestedMaxKeepAliveCount,
                maxNotificationsPerPublish,
                publishingEnabled,
                priority,
                default).GetAwaiter().GetResult();
            subscriptionId = response.SubscriptionId;
            revisedPublishingInterval = response.RevisedPublishingInterval;
            revisedLifetimeCount = response.RevisedLifetimeCount;
            revisedMaxKeepAliveCount = response.RevisedMaxKeepAliveCount;
            return response.ResponseHeader;
        }

        public ResponseHeader CreateMonitoredItems(
            RequestHeader requestHeader,
            uint subscriptionId,
            TimestampsToReturn timestampsToReturn,
            MonitoredItemCreateRequestCollection itemsToCreate,
            out MonitoredItemCreateResultCollection results,
            out DiagnosticInfoCollection diagnosticInfos)
        {
            CreateMonitoredItemsResponse response = m_session.CreateMonitoredItemsAsync(
                requestHeader,
                subscriptionId,
                timestampsToReturn,
                itemsToCreate,
                default).GetAwaiter().GetResult();
            results = response.Results;
            diagnosticInfos = response.DiagnosticInfos;
            return response.ResponseHeader;
        }

        public ResponseHeader ModifySubscription(
            RequestHeader requestHeader,
            uint subscriptionId,
            double requestedPublishingInterval,
            uint requestedLifetimeCount,
            uint requestedMaxKeepAliveCount,
            uint maxNotificationsPerPublish,
            byte priority,
            out double revisedPublishingInterval,
            out uint revisedLifetimeCount,
            out uint revisedMaxKeepAliveCount)
        {
            ModifySubscriptionResponse response = m_session.ModifySubscriptionAsync(
                requestHeader,
                subscriptionId,
                requestedPublishingInterval,
                requestedLifetimeCount,
                requestedMaxKeepAliveCount,
                maxNotificationsPerPublish,
                priority,
                default).GetAwaiter().GetResult();
            revisedPublishingInterval = response.RevisedPublishingInterval;
            revisedLifetimeCount = response.RevisedLifetimeCount;
            revisedMaxKeepAliveCount = response.RevisedMaxKeepAliveCount;
            return response.ResponseHeader;
        }

        public ResponseHeader ModifyMonitoredItems(
            RequestHeader requestHeader,
            uint subscriptionId,
            TimestampsToReturn timestampsToReturn,
            MonitoredItemModifyRequestCollection itemsToModify,
            out MonitoredItemModifyResultCollection results,
            out DiagnosticInfoCollection diagnosticInfos)
        {
            ModifyMonitoredItemsResponse response = m_session.ModifyMonitoredItemsAsync(
                requestHeader,
                subscriptionId,
                timestampsToReturn,
                itemsToModify,
                default).GetAwaiter().GetResult();
            results = response.Results;
            diagnosticInfos = response.DiagnosticInfos;
            return response.ResponseHeader;
        }

        public ResponseHeader Publish(
            RequestHeader requestHeader,
            SubscriptionAcknowledgementCollection subscriptionAcknowledgements,
            out uint subscriptionId,
            out UInt32Collection availableSequenceNumbers,
            out bool moreNotifications,
            out NotificationMessage notificationMessage,
            out StatusCodeCollection results,
            out DiagnosticInfoCollection diagnosticInfos)
        {
            PublishResponse response = m_session.PublishAsync(
                requestHeader,
                subscriptionAcknowledgements,
                default).GetAwaiter().GetResult();
            subscriptionId = response.SubscriptionId;
            availableSequenceNumbers = response.AvailableSequenceNumbers;
            moreNotifications = response.MoreNotifications;
            notificationMessage = response.NotificationMessage;
            results = response.Results;
            diagnosticInfos = response.DiagnosticInfos;
            return response.ResponseHeader;
        }

        public ResponseHeader SetPublishingMode(
            RequestHeader requestHeader,
            bool publishingEnabled,
            UInt32Collection subscriptionIds,
            out StatusCodeCollection results,
            out DiagnosticInfoCollection diagnosticInfos)
        {
            SetPublishingModeResponse response = m_session.SetPublishingModeAsync(
                requestHeader,
                publishingEnabled,
                subscriptionIds,
                default).GetAwaiter().GetResult();
            results = response.Results;
            diagnosticInfos = response.DiagnosticInfos;
            return response.ResponseHeader;
        }

        public ResponseHeader SetMonitoringMode(
            RequestHeader requestHeader,
            uint subscriptionId,
            MonitoringMode monitoringMode,
            UInt32Collection monitoredItemIds,
            out StatusCodeCollection results,
            out DiagnosticInfoCollection diagnosticInfos)
        {
            SetMonitoringModeResponse response = m_session.SetMonitoringModeAsync(
                requestHeader,
                subscriptionId,
                monitoringMode,
                monitoredItemIds,
                default).GetAwaiter().GetResult();
            results = response.Results;
            diagnosticInfos = response.DiagnosticInfos;
            return response.ResponseHeader;
        }

        public ResponseHeader Republish(
            RequestHeader requestHeader,
            uint subscriptionId,
            uint retransmitSequenceNumber,
            out NotificationMessage notificationMessage)
        {
            RepublishResponse response = m_session.RepublishAsync(
                requestHeader,
                subscriptionId,
                retransmitSequenceNumber,
                default).GetAwaiter().GetResult();
            notificationMessage = response.NotificationMessage;
            return response.ResponseHeader;
        }

        public ResponseHeader DeleteSubscriptions(
            RequestHeader requestHeader,
            UInt32Collection subscriptionIds,
            out StatusCodeCollection results,
            out DiagnosticInfoCollection diagnosticInfos)
        {
            DeleteSubscriptionsResponse response = m_session.DeleteSubscriptionsAsync(
                requestHeader,
                subscriptionIds,
                default).GetAwaiter().GetResult();
            results = response.Results;
            diagnosticInfos = response.DiagnosticInfos;
            return response.ResponseHeader;
        }

        public ResponseHeader TransferSubscriptions(
            RequestHeader requestHeader,
            UInt32Collection subscriptionIds,
            bool sendInitialValues,
            out TransferResultCollection results,
            out DiagnosticInfoCollection diagnosticInfos)
        {
            TransferSubscriptionsResponse response = m_session.TransferSubscriptionsAsync(
                requestHeader,
                subscriptionIds,
                sendInitialValues,
                default).GetAwaiter().GetResult();
            results = response.Results;
            diagnosticInfos = response.DiagnosticInfos;
            return response.ResponseHeader;
        }

        public ResponseHeader TranslateBrowsePathsToNodeIds(
            RequestHeader requestHeader,
            BrowsePathCollection browsePaths,
            out BrowsePathResultCollection results,
            out DiagnosticInfoCollection diagnosticInfos)
        {
            TranslateBrowsePathsToNodeIdsResponse response =
                m_session.TranslateBrowsePathsToNodeIdsAsync(
                    requestHeader,
                    browsePaths,
                    default).GetAwaiter().GetResult();
            results = response.Results;
            diagnosticInfos = response.DiagnosticInfos;
            return response.ResponseHeader;
        }
    }
}
