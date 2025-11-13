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

        Task<BrowseResponse> BrowseAsync(
            RequestHeader requestHeader,
            ViewDescription view,
            uint requestedMaxReferencesPerNode,
            BrowseDescriptionCollection nodesToBrowse,
            CancellationToken ct = default);

        Task<BrowseNextResponse> BrowseNextAsync(
            RequestHeader requestHeader,
            bool releaseContinuationPoints,
            ByteStringCollection continuationPoints,
            CancellationToken ct = default);

        Task<TranslateBrowsePathsToNodeIdsResponse> TranslateBrowsePathsToNodeIdsAsync(
            RequestHeader requestHeader,
            BrowsePathCollection browsePaths,
            CancellationToken ct = default);

        Task<CreateSubscriptionResponse> CreateSubscriptionAsync(
            RequestHeader requestHeader,
            double requestedPublishingInterval,
            uint requestedLifetimeCount,
            uint requestedMaxKeepAliveCount,
            uint maxNotificationsPerPublish,
            bool publishingEnabled,
            byte priority,
            CancellationToken ct = default);

        Task<CreateMonitoredItemsResponse> CreateMonitoredItemsAsync(
            RequestHeader requestHeader,
            uint subscriptionId,
            TimestampsToReturn timestampsToReturn,
            MonitoredItemCreateRequestCollection itemsToCreate,
            CancellationToken ct = default);

        Task<ModifySubscriptionResponse> ModifySubscriptionAsync(
            RequestHeader requestHeader,
            uint subscriptionId,
            double requestedPublishingInterval,
            uint requestedLifetimeCount,
            uint requestedMaxKeepAliveCount,
            uint maxNotificationsPerPublish,
            byte priority,
            CancellationToken ct = default);

        Task<ModifyMonitoredItemsResponse> ModifyMonitoredItemsAsync(
            RequestHeader requestHeader,
            uint subscriptionId,
            TimestampsToReturn timestampsToReturn,
            MonitoredItemModifyRequestCollection itemsToModify,
            CancellationToken ct = default);

        Task<PublishResponse> PublishAsync(
            RequestHeader requestHeader,
            SubscriptionAcknowledgementCollection subscriptionAcknowledgements,
            CancellationToken ct = default);

        Task<SetPublishingModeResponse> SetPublishingModeAsync(
            RequestHeader requestHeader,
            bool publishingEnabled,
            UInt32Collection subscriptionIds,
            CancellationToken ct = default);

        Task<SetMonitoringModeResponse> SetMonitoringModeAsync(
            RequestHeader requestHeader,
            uint subscriptionId,
            MonitoringMode monitoringMode,
            UInt32Collection monitoredItemIds,
            CancellationToken ct = default);

        Task<RepublishResponse> RepublishAsync(
            RequestHeader requestHeader,
            uint subscriptionId,
            uint retransmitSequenceNumber,
            CancellationToken ct = default);

        Task<DeleteSubscriptionsResponse> DeleteSubscriptionsAsync(
            RequestHeader requestHeader,
            UInt32Collection subscriptionIds,
            CancellationToken ct = default);

        Task<TransferSubscriptionsResponse> TransferSubscriptionsAsync(
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

        public ServerTestServices(ISessionServer server, ITelemetryContext telemetry)
        {
            Telemetry = telemetry;
            Logger = telemetry.CreateLogger<ServerTestServices>();
            m_server = server;
        }

        public Task<BrowseResponse> BrowseAsync(
            RequestHeader requestHeader,
            ViewDescription view,
            uint requestedMaxReferencesPerNode,
            BrowseDescriptionCollection nodesToBrowse,
            CancellationToken ct = default)
        {
            return m_server.BrowseAsync(
                requestHeader,
                view,
                requestedMaxReferencesPerNode,
                nodesToBrowse,
                ct);
        }

        public Task<BrowseNextResponse> BrowseNextAsync(
            RequestHeader requestHeader,
            bool releaseContinuationPoints,
            ByteStringCollection continuationPoints,
            CancellationToken ct = default)
        {
            return m_server.BrowseNextAsync(
                requestHeader,
                releaseContinuationPoints,
                continuationPoints,
                ct);
        }

        public Task<CreateSubscriptionResponse> CreateSubscriptionAsync(
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
            return Task.FromResult(response);
        }

        public Task<CreateMonitoredItemsResponse> CreateMonitoredItemsAsync(
            RequestHeader requestHeader,
            uint subscriptionId,
            TimestampsToReturn timestampsToReturn,
            MonitoredItemCreateRequestCollection itemsToCreate,
            CancellationToken ct = default)
        {
            ResponseHeader responseHeader = m_server.CreateMonitoredItems(
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
            return Task.FromResult(response);
        }

        public Task<ModifySubscriptionResponse> ModifySubscriptionAsync(
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
            return Task.FromResult(response);
        }

        public Task<ModifyMonitoredItemsResponse> ModifyMonitoredItemsAsync(
            RequestHeader requestHeader,
            uint subscriptionId,
            TimestampsToReturn timestampsToReturn,
            MonitoredItemModifyRequestCollection itemsToModify,
            CancellationToken ct = default)
        {
            ResponseHeader responseHeader = m_server.ModifyMonitoredItems(
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
            return Task.FromResult(response);
        }

        public Task<PublishResponse> PublishAsync(
            RequestHeader requestHeader,
            SubscriptionAcknowledgementCollection subscriptionAcknowledgements,
            CancellationToken ct = default)
        {
            return m_server.PublishAsync(
                requestHeader,
                subscriptionAcknowledgements,
                ct);
        }

        public Task<SetPublishingModeResponse> SetPublishingModeAsync(
            RequestHeader requestHeader,
            bool publishingEnabled,
            UInt32Collection subscriptionIds,
            CancellationToken ct = default)
        {
            ResponseHeader responseHeader = m_server.SetPublishingMode(
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
            return Task.FromResult(response);
        }

        public Task<SetMonitoringModeResponse> SetMonitoringModeAsync(
            RequestHeader requestHeader,
            uint subscriptionId,
            MonitoringMode monitoringMode,
            UInt32Collection monitoredItemIds,
            CancellationToken ct = default)
        {
            ResponseHeader responseHeader = m_server.SetMonitoringMode(
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
            return Task.FromResult(response);
        }

        public Task<RepublishResponse> RepublishAsync(
            RequestHeader requestHeader,
            uint subscriptionId,
            uint retransmitSequenceNumber,
            CancellationToken ct = default)
        {
            ResponseHeader responseHeader = m_server.Republish(
                requestHeader,
                subscriptionId,
                retransmitSequenceNumber,
                out NotificationMessage notificationMessage);
            var response = new RepublishResponse
            {
                ResponseHeader = responseHeader,
                NotificationMessage = notificationMessage
            };
            return Task.FromResult(response);
        }

        public Task<DeleteSubscriptionsResponse> DeleteSubscriptionsAsync(
            RequestHeader requestHeader,
            UInt32Collection subscriptionIds,
            CancellationToken ct = default)
        {
            ResponseHeader responseHeader = m_server.DeleteSubscriptions(
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
            return Task.FromResult(response);
        }

        public Task<TransferSubscriptionsResponse> TransferSubscriptionsAsync(
            RequestHeader requestHeader,
            UInt32Collection subscriptionIds,
            bool sendInitialValues,
            CancellationToken ct = default)
        {
            ResponseHeader responseHeader = m_server.TransferSubscriptions(
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
            return Task.FromResult(response);
        }

        public Task<TranslateBrowsePathsToNodeIdsResponse> TranslateBrowsePathsToNodeIdsAsync(
            RequestHeader requestHeader,
            BrowsePathCollection browsePaths,
            CancellationToken ct = default)
        {
            return m_server.TranslateBrowsePathsToNodeIdsAsync(
                requestHeader,
                browsePaths,
                ct);
        }
    }
}
