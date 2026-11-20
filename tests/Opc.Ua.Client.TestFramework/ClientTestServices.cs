// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.Server.TestFramework;

namespace Opc.Ua.Client.TestFramework
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
            ArrayOf<BrowseDescription> nodesToBrowse,
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
            ArrayOf<ByteString> continuationPoints,
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
            ArrayOf<MonitoredItemCreateRequest> itemsToCreate,
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
            ArrayOf<MonitoredItemModifyRequest> itemsToModify,
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
            ArrayOf<SubscriptionAcknowledgement> subscriptionAcknowledgements,
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
            ArrayOf<uint> subscriptionIds,
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
            ArrayOf<uint> monitoredItemIds,
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
            ArrayOf<uint> subscriptionIds,
            CancellationToken ct = default)
        {
            return m_session.DeleteSubscriptionsAsync(
                requestHeader,
                subscriptionIds,
                ct);
        }

        public ValueTask<TransferSubscriptionsResponse> TransferSubscriptionsAsync(
            RequestHeader requestHeader,
            ArrayOf<uint> subscriptionIds,
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
            ArrayOf<BrowsePath> browsePaths,
            CancellationToken ct = default)
        {
            return m_session.TranslateBrowsePathsToNodeIdsAsync(
                requestHeader,
                browsePaths,
                ct);
        }
    }
}
