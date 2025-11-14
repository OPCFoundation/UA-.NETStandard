/* ========================================================================
 * Copyright (c) 2005-2024 The OPC Foundation, Inc. All rights reserved.
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
using Microsoft.Extensions.Logging;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// Interface for common test framework services.
    /// </summary>
    public interface IServerTestServices
    {
        ITelemetryContext Telemetry { get; }

        ILogger Logger { get; }

        ValueTask<BrowseResponse> BrowseAsync(
            RequestHeader requestHeader,
            ViewDescription view,
            uint requestedMaxReferencesPerNode,
            BrowseDescriptionCollection nodesToBrowse,
            CancellationToken ct = default);

        ValueTask<BrowseNextResponse> BrowseNextAsync(
            RequestHeader requestHeader,
            bool releaseContinuationPoints,
            ByteStringCollection continuationPoints,
            CancellationToken ct = default);

        ValueTask<TranslateBrowsePathsToNodeIdsResponse> TranslateBrowsePathsToNodeIdsAsync(
            RequestHeader requestHeader,
            BrowsePathCollection browsePaths,
            CancellationToken ct = default);

        ValueTask<CreateSubscriptionResponse> CreateSubscriptionAsync(
            RequestHeader requestHeader,
            double requestedPublishingInterval,
            uint requestedLifetimeCount,
            uint requestedMaxKeepAliveCount,
            uint maxNotificationsPerPublish,
            bool publishingEnabled,
            byte priority,
            CancellationToken ct = default);

        ValueTask<CreateMonitoredItemsResponse> CreateMonitoredItemsAsync(
            RequestHeader requestHeader,
            uint subscriptionId,
            TimestampsToReturn timestampsToReturn,
            MonitoredItemCreateRequestCollection itemsToCreate,
            CancellationToken ct = default);

        ValueTask<ModifySubscriptionResponse> ModifySubscriptionAsync(
            RequestHeader requestHeader,
            uint subscriptionId,
            double requestedPublishingInterval,
            uint requestedLifetimeCount,
            uint requestedMaxKeepAliveCount,
            uint maxNotificationsPerPublish,
            byte priority,
            CancellationToken ct = default);

        ValueTask<ModifyMonitoredItemsResponse> ModifyMonitoredItemsAsync(
            RequestHeader requestHeader,
            uint subscriptionId,
            TimestampsToReturn timestampsToReturn,
            MonitoredItemModifyRequestCollection itemsToModify,
            CancellationToken ct = default);

        ValueTask<PublishResponse> PublishAsync(
            RequestHeader requestHeader,
            SubscriptionAcknowledgementCollection subscriptionAcknowledgements,
            CancellationToken ct = default);

        ValueTask<SetPublishingModeResponse> SetPublishingModeAsync(
            RequestHeader requestHeader,
            bool publishingEnabled,
            UInt32Collection subscriptionIds,
            CancellationToken ct = default);

        ValueTask<SetMonitoringModeResponse> SetMonitoringModeAsync(
            RequestHeader requestHeader,
            uint subscriptionId,
            MonitoringMode monitoringMode,
            UInt32Collection monitoredItemIds,
            CancellationToken ct = default);

        ValueTask<RepublishResponse> RepublishAsync(
            RequestHeader requestHeader,
            uint subscriptionId,
            uint retransmitSequenceNumber,
            CancellationToken ct = default);

        ValueTask<DeleteSubscriptionsResponse> DeleteSubscriptionsAsync(
            RequestHeader requestHeader,
            UInt32Collection subscriptionIds,
            CancellationToken ct = default);

        ValueTask<TransferSubscriptionsResponse> TransferSubscriptionsAsync(
            RequestHeader requestHeader,
            UInt32Collection subscriptionIds,
            bool sendInitialValues,
            CancellationToken ct = default);
    }

    /// <summary>
    /// Implementation for a standard server.
    /// </summary>
    public class ServerTestServices : IServerTestServices
    {
        private readonly ISessionServer m_server;

        public ITelemetryContext Telemetry { get; }

        public ILogger Logger { get; }

        public SecureChannelContext SecureChannelContext { get; set; }

        public ServerTestServices(ISessionServer server, SecureChannelContext secureChannelContext, ITelemetryContext telemetry)
        {
            Telemetry = telemetry;
            Logger = telemetry.CreateLogger<ServerTestServices>();
            m_server = server;
            SecureChannelContext = secureChannelContext;
        }

        public ValueTask<BrowseResponse> BrowseAsync(
            RequestHeader requestHeader,
            ViewDescription view,
            uint requestedMaxReferencesPerNode,
            BrowseDescriptionCollection nodesToBrowse,
            CancellationToken ct = default)
        {
            return new ValueTask<BrowseResponse>(m_server.BrowseAsync(
                SecureChannelContext,
                requestHeader,
                view,
                requestedMaxReferencesPerNode,
                nodesToBrowse,
                ct));
        }

        public ValueTask<BrowseNextResponse> BrowseNextAsync(
            RequestHeader requestHeader,
            bool releaseContinuationPoints,
            ByteStringCollection continuationPoints,
            CancellationToken ct = default)
        {
            return new ValueTask<BrowseNextResponse>(m_server.BrowseNextAsync(
                SecureChannelContext,
                requestHeader,
                releaseContinuationPoints,
                continuationPoints,
                ct));
        }

        public ValueTask<CreateSubscriptionResponse> CreateSubscriptionAsync(
            RequestHeader requestHeader,
            double requestedPublishingInterval,
            uint requestedLifetimeCount,
            uint requestedMaxKeepAliveCount,
            uint maxNotificationsPerPublish,
            bool publishingEnabled,
            byte priority,
            CancellationToken ct = default)
        {
            ResponseHeader responseHeader = m_server.CreateSubscription(
                SecureChannelContext,
                requestHeader,
                requestedPublishingInterval,
                requestedLifetimeCount,
                requestedMaxKeepAliveCount,
                maxNotificationsPerPublish,
                publishingEnabled,
                priority,
                out uint subscriptionId,
                out double revisedPublishingInterval,
                out uint revisedLifetimeCount,
                out uint revisedMaxKeepAliveCount);
            var response = new CreateSubscriptionResponse
            {
                ResponseHeader = responseHeader,
                SubscriptionId = subscriptionId,
                RevisedPublishingInterval = revisedPublishingInterval,
                RevisedLifetimeCount = revisedLifetimeCount,
                RevisedMaxKeepAliveCount = revisedMaxKeepAliveCount
            };
            return new ValueTask<CreateSubscriptionResponse>(response);
        }

        public ValueTask<CreateMonitoredItemsResponse> CreateMonitoredItemsAsync(
            RequestHeader requestHeader,
            uint subscriptionId,
            TimestampsToReturn timestampsToReturn,
            MonitoredItemCreateRequestCollection itemsToCreate,
            CancellationToken ct = default)
        {
            ResponseHeader responseHeader = m_server.CreateMonitoredItems(
                SecureChannelContext,
                requestHeader,
                subscriptionId,
                timestampsToReturn,
                itemsToCreate,
                out MonitoredItemCreateResultCollection results,
                out DiagnosticInfoCollection diagnosticInfos);
            var response = new CreateMonitoredItemsResponse
            {
                ResponseHeader = responseHeader,
                Results = results,
                DiagnosticInfos = diagnosticInfos
            };
            return new ValueTask<CreateMonitoredItemsResponse>(response);
        }

        public ValueTask<ModifySubscriptionResponse> ModifySubscriptionAsync(
            RequestHeader requestHeader,
            uint subscriptionId,
            double requestedPublishingInterval,
            uint requestedLifetimeCount,
            uint requestedMaxKeepAliveCount,
            uint maxNotificationsPerPublish,
            byte priority,
            CancellationToken ct = default)
        {
            ResponseHeader responseHeader = m_server.ModifySubscription(
                SecureChannelContext,
                requestHeader,
                subscriptionId,
                requestedPublishingInterval,
                requestedLifetimeCount,
                requestedMaxKeepAliveCount,
                maxNotificationsPerPublish,
                priority,
                out double revisedPublishingInterval,
                out uint revisedLifetimeCount,
                out uint revisedMaxKeepAliveCount);
            var response = new ModifySubscriptionResponse
            {
                ResponseHeader = responseHeader,
                RevisedPublishingInterval = revisedPublishingInterval,
                RevisedLifetimeCount = revisedLifetimeCount,
                RevisedMaxKeepAliveCount = revisedMaxKeepAliveCount
            };
            return new ValueTask<ModifySubscriptionResponse>(response);
        }

        public ValueTask<ModifyMonitoredItemsResponse> ModifyMonitoredItemsAsync(
            RequestHeader requestHeader,
            uint subscriptionId,
            TimestampsToReturn timestampsToReturn,
            MonitoredItemModifyRequestCollection itemsToModify,
            CancellationToken ct = default)
        {
            ResponseHeader responseHeader = m_server.ModifyMonitoredItems(
                SecureChannelContext,
                requestHeader,
                subscriptionId,
                timestampsToReturn,
                itemsToModify,
                out MonitoredItemModifyResultCollection results,
                out DiagnosticInfoCollection diagnosticInfos);
            var response = new ModifyMonitoredItemsResponse
            {
                ResponseHeader = responseHeader,
                Results = results,
                DiagnosticInfos = diagnosticInfos
            };
            return new ValueTask<ModifyMonitoredItemsResponse>(response);
        }

        public ValueTask<PublishResponse> PublishAsync(
            RequestHeader requestHeader,
            SubscriptionAcknowledgementCollection subscriptionAcknowledgements,
            CancellationToken ct = default)
        {
            return new ValueTask<PublishResponse>(m_server.PublishAsync(
                SecureChannelContext,
                requestHeader,
                subscriptionAcknowledgements,
                ct));
        }

        public ValueTask<SetPublishingModeResponse> SetPublishingModeAsync(
            RequestHeader requestHeader,
            bool publishingEnabled,
            UInt32Collection subscriptionIds,
            CancellationToken ct = default)
        {
            ResponseHeader responseHeader = m_server.SetPublishingMode(
                SecureChannelContext,
                requestHeader,
                publishingEnabled,
                subscriptionIds,
                out StatusCodeCollection results,
                out DiagnosticInfoCollection diagnosticInfos);
            var response = new SetPublishingModeResponse
            {
                ResponseHeader = responseHeader,
                Results = results,
                DiagnosticInfos = diagnosticInfos
            };
            return new ValueTask<SetPublishingModeResponse>(response);
        }

        public ValueTask<SetMonitoringModeResponse> SetMonitoringModeAsync(
            RequestHeader requestHeader,
            uint subscriptionId,
            MonitoringMode monitoringMode,
            UInt32Collection monitoredItemIds,
            CancellationToken ct = default)
        {
            ResponseHeader responseHeader = m_server.SetMonitoringMode(
                SecureChannelContext,
                requestHeader,
                subscriptionId,
                monitoringMode,
                monitoredItemIds,
                out StatusCodeCollection results,
                out DiagnosticInfoCollection diagnosticInfos);
            var response = new SetMonitoringModeResponse
            {
                ResponseHeader = responseHeader,
                Results = results,
                DiagnosticInfos = diagnosticInfos
            };
            return new ValueTask<SetMonitoringModeResponse>(response);
        }

        public ValueTask<RepublishResponse> RepublishAsync(
            RequestHeader requestHeader,
            uint subscriptionId,
            uint retransmitSequenceNumber,
            CancellationToken ct = default)
        {
            ResponseHeader responseHeader = m_server.Republish(
                SecureChannelContext,
                requestHeader,
                subscriptionId,
                retransmitSequenceNumber,
                out NotificationMessage notificationMessage);
            var response = new RepublishResponse
            {
                ResponseHeader = responseHeader,
                NotificationMessage = notificationMessage
            };
            return new ValueTask<RepublishResponse>(response);
        }

        public ValueTask<DeleteSubscriptionsResponse> DeleteSubscriptionsAsync(
            RequestHeader requestHeader,
            UInt32Collection subscriptionIds,
            CancellationToken ct = default)
        {
            ResponseHeader responseHeader = m_server.DeleteSubscriptions(
                SecureChannelContext,
                requestHeader,
                subscriptionIds,
                out StatusCodeCollection results,
                out DiagnosticInfoCollection diagnosticInfos);
            var response = new DeleteSubscriptionsResponse
            {
                ResponseHeader = responseHeader,
                Results = results,
                DiagnosticInfos = diagnosticInfos
            };
            return new ValueTask<DeleteSubscriptionsResponse>(response);
        }

        public ValueTask<TransferSubscriptionsResponse> TransferSubscriptionsAsync(
            RequestHeader requestHeader,
            UInt32Collection subscriptionIds,
            bool sendInitialValues,
            CancellationToken ct = default)
        {
            ResponseHeader responseHeader = m_server.TransferSubscriptions(
                SecureChannelContext,
                requestHeader,
                subscriptionIds,
                sendInitialValues,
                out TransferResultCollection results,
                out DiagnosticInfoCollection diagnosticInfos);
            var response = new TransferSubscriptionsResponse
            {
                ResponseHeader = responseHeader,
                Results = results,
                DiagnosticInfos = diagnosticInfos
            };
            return new ValueTask<TransferSubscriptionsResponse>(response);
        }

        public ValueTask<TranslateBrowsePathsToNodeIdsResponse> TranslateBrowsePathsToNodeIdsAsync(
            RequestHeader requestHeader,
            BrowsePathCollection browsePaths,
            CancellationToken ct = default)
        {
            return new ValueTask<TranslateBrowsePathsToNodeIdsResponse>(m_server.TranslateBrowsePathsToNodeIdsAsync(
                SecureChannelContext,
                requestHeader,
                browsePaths,
                ct));
        }
    }
}
