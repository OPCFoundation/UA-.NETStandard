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

        public ILogger Logger { get; }

        public IServiceMessageContext MessageContext => m_session.MessageContext;

        public ClientTestServices(ISession session, ITelemetryContext telemetry)
        {
            m_session = session;
            Logger = session.MessageContext.Telemetry.CreateLogger<ClientTestServices>();
        }

        public ValueTask<BrowseResponse> BrowseAsync(
            RequestHeader requestHeader,
            ViewDescription view,
            uint requestedMaxReferencesPerNode,
            BrowseDescriptionCollection nodesToBrowse,
            CancellationToken ct = default)
        {
            return m_session.BrowseAsync(
                requestHeader,
                view,
                requestedMaxReferencesPerNode,
                nodesToBrowse,
                ct);
        }

        public ValueTask<BrowseNextResponse> BrowseNextAsync(
            RequestHeader requestHeader,
            bool releaseContinuationPoints,
            ByteStringCollection continuationPoints,
            CancellationToken ct = default)
        {
            return m_session.BrowseNextAsync(
                requestHeader,
                releaseContinuationPoints,
                continuationPoints,
                ct);
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
            return m_session.CreateSubscriptionAsync(
                requestHeader,
                requestedPublishingInterval,
                requestedLifetimeCount,
                requestedMaxKeepAliveCount,
                maxNotificationsPerPublish,
                publishingEnabled,
                priority,
                ct);
        }

        public ValueTask<CreateMonitoredItemsResponse> CreateMonitoredItemsAsync(
            RequestHeader requestHeader,
            uint subscriptionId,
            TimestampsToReturn timestampsToReturn,
            MonitoredItemCreateRequestCollection itemsToCreate,
            CancellationToken ct = default)
        {
            return m_session.CreateMonitoredItemsAsync(
                requestHeader,
                subscriptionId,
                timestampsToReturn,
                itemsToCreate,
                ct);
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
            return m_session.ModifySubscriptionAsync(
                requestHeader,
                subscriptionId,
                requestedPublishingInterval,
                requestedLifetimeCount,
                requestedMaxKeepAliveCount,
                maxNotificationsPerPublish,
                priority,
                ct);
        }

        public ValueTask<ModifyMonitoredItemsResponse> ModifyMonitoredItemsAsync(
            RequestHeader requestHeader,
            uint subscriptionId,
            TimestampsToReturn timestampsToReturn,
            MonitoredItemModifyRequestCollection itemsToModify,
            CancellationToken ct = default)
        {
            return m_session.ModifyMonitoredItemsAsync(
                requestHeader,
                subscriptionId,
                timestampsToReturn,
                itemsToModify,
                ct);
        }

        public ValueTask<PublishResponse> PublishAsync(
            RequestHeader requestHeader,
            SubscriptionAcknowledgementCollection subscriptionAcknowledgements,
            CancellationToken ct = default)
        {
            return m_session.PublishAsync(
                requestHeader,
                subscriptionAcknowledgements,
                ct);
        }

        public ValueTask<SetPublishingModeResponse> SetPublishingModeAsync(
            RequestHeader requestHeader,
            bool publishingEnabled,
            UInt32Collection subscriptionIds,
            CancellationToken ct = default)
        {
            return m_session.SetPublishingModeAsync(
                requestHeader,
                publishingEnabled,
                subscriptionIds,
                ct);
        }

        public ValueTask<SetMonitoringModeResponse> SetMonitoringModeAsync(
            RequestHeader requestHeader,
            uint subscriptionId,
            MonitoringMode monitoringMode,
            UInt32Collection monitoredItemIds,
            CancellationToken ct = default)
        {
            return m_session.SetMonitoringModeAsync(
                requestHeader,
                subscriptionId,
                monitoringMode,
                monitoredItemIds,
                ct);
        }

        public ValueTask<RepublishResponse> RepublishAsync(
            RequestHeader requestHeader,
            uint subscriptionId,
            uint retransmitSequenceNumber,
            CancellationToken ct = default)
        {
            return m_session.RepublishAsync(
                requestHeader,
                subscriptionId,
                retransmitSequenceNumber,
                ct);
        }

        public ValueTask<DeleteSubscriptionsResponse> DeleteSubscriptionsAsync(
            RequestHeader requestHeader,
            UInt32Collection subscriptionIds,
            CancellationToken ct = default)
        {
            return m_session.DeleteSubscriptionsAsync(
                requestHeader,
                subscriptionIds,
                ct);
        }

        public ValueTask<TransferSubscriptionsResponse> TransferSubscriptionsAsync(
            RequestHeader requestHeader,
            UInt32Collection subscriptionIds,
            bool sendInitialValues,
            CancellationToken ct = default)
        {
            return m_session.TransferSubscriptionsAsync(
                requestHeader,
                subscriptionIds,
                sendInitialValues,
                ct);
        }

        public ValueTask<TranslateBrowsePathsToNodeIdsResponse> TranslateBrowsePathsToNodeIdsAsync(
            RequestHeader requestHeader,
            BrowsePathCollection browsePaths,
            CancellationToken ct = default)
        {
            return m_session.TranslateBrowsePathsToNodeIdsAsync(
                requestHeader,
                browsePaths,
                ct);
        }
    }
}
