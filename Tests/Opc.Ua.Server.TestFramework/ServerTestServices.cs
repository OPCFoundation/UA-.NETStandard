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

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// Interface for common test framework services.
    /// </summary>
    public interface IServerTestServices
    {
        IServiceMessageContext MessageContext { get; }

        ILogger Logger { get; }

        ValueTask<BrowseResponse> BrowseAsync(
            RequestHeader requestHeader,
            ViewDescription view,
            uint requestedMaxReferencesPerNode,
            ArrayOf<BrowseDescription> nodesToBrowse,
            CancellationToken ct = default);

        ValueTask<BrowseNextResponse> BrowseNextAsync(
            RequestHeader requestHeader,
            bool releaseContinuationPoints,
            ArrayOf<ByteString> continuationPoints,
            CancellationToken ct = default);

        ValueTask<TranslateBrowsePathsToNodeIdsResponse> TranslateBrowsePathsToNodeIdsAsync(
            RequestHeader requestHeader,
            ArrayOf<BrowsePath> browsePaths,
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
            ArrayOf<MonitoredItemCreateRequest> itemsToCreate,
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
            ArrayOf<MonitoredItemModifyRequest> itemsToModify,
            CancellationToken ct = default);

        ValueTask<PublishResponse> PublishAsync(
            RequestHeader requestHeader,
            ArrayOf<SubscriptionAcknowledgement> subscriptionAcknowledgements,
            CancellationToken ct = default);

        ValueTask<SetPublishingModeResponse> SetPublishingModeAsync(
            RequestHeader requestHeader,
            bool publishingEnabled,
            ArrayOf<uint> subscriptionIds,
            CancellationToken ct = default);

        ValueTask<SetMonitoringModeResponse> SetMonitoringModeAsync(
            RequestHeader requestHeader,
            uint subscriptionId,
            MonitoringMode monitoringMode,
            ArrayOf<uint> monitoredItemIds,
            CancellationToken ct = default);

        ValueTask<RepublishResponse> RepublishAsync(
            RequestHeader requestHeader,
            uint subscriptionId,
            uint retransmitSequenceNumber,
            CancellationToken ct = default);

        ValueTask<DeleteSubscriptionsResponse> DeleteSubscriptionsAsync(
            RequestHeader requestHeader,
            ArrayOf<uint> subscriptionIds,
            CancellationToken ct = default);

        ValueTask<TransferSubscriptionsResponse> TransferSubscriptionsAsync(
            RequestHeader requestHeader,
            ArrayOf<uint> subscriptionIds,
            bool sendInitialValues,
            CancellationToken ct = default);
    }

    /// <summary>
    /// Implementation for a standard server.
    /// </summary>
    public class ServerTestServices : IServerTestServices
    {
        private readonly ISessionServer m_server;

        public ILogger Logger { get; }

        public SecureChannelContext SecureChannelContext { get; set; }

        public IServiceMessageContext MessageContext => m_server.MessageContext;

        public ServerTestServices(ISessionServer server, SecureChannelContext secureChannelContext)
        {
            Logger = server.MessageContext.Telemetry.CreateLogger<ServerTestServices>();
            m_server = server;
            SecureChannelContext = secureChannelContext;
        }

        public async ValueTask<BrowseResponse> BrowseAsync(
            RequestHeader requestHeader,
            ViewDescription view,
            uint requestedMaxReferencesPerNode,
            ArrayOf<BrowseDescription> nodesToBrowse,
            CancellationToken ct = default)
        {
            using var lifetime = new RequestLifetime(ct);
            return await m_server.BrowseAsync(
                SecureChannelContext,
                requestHeader,
                view,
                requestedMaxReferencesPerNode,
                nodesToBrowse,
                lifetime).ConfigureAwait(false);
        }

        public async ValueTask<BrowseNextResponse> BrowseNextAsync(
            RequestHeader requestHeader,
            bool releaseContinuationPoints,
            ArrayOf<ByteString> continuationPoints,
            CancellationToken ct = default)
        {
            using var lifetime = new RequestLifetime(ct);
            return await m_server.BrowseNextAsync(
                SecureChannelContext,
                requestHeader,
                releaseContinuationPoints,
                continuationPoints,
                lifetime).ConfigureAwait(false);
        }

        public async ValueTask<CreateSubscriptionResponse> CreateSubscriptionAsync(
            RequestHeader requestHeader,
            double requestedPublishingInterval,
            uint requestedLifetimeCount,
            uint requestedMaxKeepAliveCount,
            uint maxNotificationsPerPublish,
            bool publishingEnabled,
            byte priority,
            CancellationToken ct = default)
        {
            using var lifetime = new RequestLifetime(ct);
            return await m_server.CreateSubscriptionAsync(
                SecureChannelContext,
                requestHeader,
                requestedPublishingInterval,
                requestedLifetimeCount,
                requestedMaxKeepAliveCount,
                maxNotificationsPerPublish,
                publishingEnabled,
                priority,
                lifetime).ConfigureAwait(false);
        }

        public async ValueTask<CreateMonitoredItemsResponse> CreateMonitoredItemsAsync(
            RequestHeader requestHeader,
            uint subscriptionId,
            TimestampsToReturn timestampsToReturn,
            ArrayOf<MonitoredItemCreateRequest> itemsToCreate,
            CancellationToken ct = default)
        {
            using var lifetime = new RequestLifetime(ct);
            return await m_server.CreateMonitoredItemsAsync(
                SecureChannelContext,
                requestHeader,
                subscriptionId,
                timestampsToReturn,
                itemsToCreate,
                lifetime).ConfigureAwait(false);
        }

        public async ValueTask<ModifySubscriptionResponse> ModifySubscriptionAsync(
            RequestHeader requestHeader,
            uint subscriptionId,
            double requestedPublishingInterval,
            uint requestedLifetimeCount,
            uint requestedMaxKeepAliveCount,
            uint maxNotificationsPerPublish,
            byte priority,
            CancellationToken ct = default)
        {
            using var lifetime = new RequestLifetime(ct);
            return await m_server.ModifySubscriptionAsync(
                SecureChannelContext,
                requestHeader,
                subscriptionId,
                requestedPublishingInterval,
                requestedLifetimeCount,
                requestedMaxKeepAliveCount,
                maxNotificationsPerPublish,
                priority,
                lifetime).ConfigureAwait(false);
        }

        public async ValueTask<ModifyMonitoredItemsResponse> ModifyMonitoredItemsAsync(
            RequestHeader requestHeader,
            uint subscriptionId,
            TimestampsToReturn timestampsToReturn,
            ArrayOf<MonitoredItemModifyRequest> itemsToModify,
            CancellationToken ct = default)
        {
            using var lifetime = new RequestLifetime(ct);
            return await m_server.ModifyMonitoredItemsAsync(
                SecureChannelContext,
                requestHeader,
                subscriptionId,
                timestampsToReturn,
                itemsToModify,
                lifetime).ConfigureAwait(false);
        }

        public async ValueTask<PublishResponse> PublishAsync(
            RequestHeader requestHeader,
            ArrayOf<SubscriptionAcknowledgement> subscriptionAcknowledgements,
            CancellationToken ct = default)
        {
            using var lifetime = new RequestLifetime(ct);
            return await m_server.PublishAsync(
                SecureChannelContext,
                requestHeader,
                subscriptionAcknowledgements,
                lifetime).ConfigureAwait(false);
        }

        public async ValueTask<SetPublishingModeResponse> SetPublishingModeAsync(
            RequestHeader requestHeader,
            bool publishingEnabled,
            ArrayOf<uint> subscriptionIds,
            CancellationToken ct = default)
        {
            using var lifetime = new RequestLifetime(ct);
            return await m_server.SetPublishingModeAsync(
                SecureChannelContext,
                requestHeader,
                publishingEnabled,
                subscriptionIds,
                lifetime).ConfigureAwait(false);
        }

        public async ValueTask<SetMonitoringModeResponse> SetMonitoringModeAsync(
            RequestHeader requestHeader,
            uint subscriptionId,
            MonitoringMode monitoringMode,
            ArrayOf<uint> monitoredItemIds,
            CancellationToken ct = default)
        {
            using var lifetime = new RequestLifetime(ct);
            return await m_server.SetMonitoringModeAsync(
                    SecureChannelContext,
                    requestHeader,
                    subscriptionId,
                    monitoringMode,
                    monitoredItemIds,
                    lifetime).ConfigureAwait(false);
        }

        public async ValueTask<RepublishResponse> RepublishAsync(
            RequestHeader requestHeader,
            uint subscriptionId,
            uint retransmitSequenceNumber,
            CancellationToken ct = default)
        {
            using var lifetime = new RequestLifetime(ct);
            return await m_server.RepublishAsync(
                SecureChannelContext,
                requestHeader,
                subscriptionId,
                retransmitSequenceNumber,
                lifetime).ConfigureAwait(false);
        }

        public async ValueTask<DeleteSubscriptionsResponse> DeleteSubscriptionsAsync(
            RequestHeader requestHeader,
            ArrayOf<uint> subscriptionIds,
            CancellationToken ct = default)
        {
            using var lifetime = new RequestLifetime(ct);
            return await m_server.DeleteSubscriptionsAsync(
                SecureChannelContext,
                requestHeader,
                subscriptionIds,
                lifetime).ConfigureAwait(false);
        }

        public async ValueTask<TransferSubscriptionsResponse> TransferSubscriptionsAsync(
            RequestHeader requestHeader,
            ArrayOf<uint> subscriptionIds,
            bool sendInitialValues,
            CancellationToken ct = default)
        {
            using var lifetime = new RequestLifetime(ct);
            return await m_server.TransferSubscriptionsAsync(
                SecureChannelContext,
                requestHeader,
                subscriptionIds,
                sendInitialValues,
                lifetime).ConfigureAwait(false);
        }

        public async ValueTask<TranslateBrowsePathsToNodeIdsResponse> TranslateBrowsePathsToNodeIdsAsync(
            RequestHeader requestHeader,
            ArrayOf<BrowsePath> browsePaths,
            CancellationToken ct = default)
        {
            using var lifetime = new RequestLifetime(ct);
            return await m_server.TranslateBrowsePathsToNodeIdsAsync(
                SecureChannelContext,
                requestHeader,
                browsePaths,
                lifetime).ConfigureAwait(false);
        }
    }
}
