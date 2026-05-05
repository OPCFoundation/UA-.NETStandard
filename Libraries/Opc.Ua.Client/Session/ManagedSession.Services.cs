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

namespace Opc.Ua.Client
{
    /// <summary>
    /// Service method delegations for <see cref="ManagedSession"/>.
    /// All async methods are gated with a reader lock so that service
    /// calls block during reconnection. Obsolete sync and APM methods
    /// delegate directly to the inner session without gating.
    /// </summary>
    public partial class ManagedSession
    {
        /// <inheritdoc/>
        public async ValueTask<ReadResponse> ReadAsync(
            RequestHeader? requestHeader,
            double maxAge,
            TimestampsToReturn timestampsToReturn,
            ArrayOf<ReadValueId> nodesToRead,
            CancellationToken ct)
        {
            using (await m_serviceLock.ReaderLockAsync(ct).ConfigureAwait(false))
            {
                return await InnerSession.ReadAsync(
                    requestHeader, maxAge, timestampsToReturn, nodesToRead, ct).ConfigureAwait(false);
            }
        }

        /// <inheritdoc/>
        public async ValueTask<HistoryReadResponse> HistoryReadAsync(
            RequestHeader? requestHeader,
            ExtensionObject historyReadDetails,
            TimestampsToReturn timestampsToReturn,
            bool releaseContinuationPoints,
            ArrayOf<HistoryReadValueId> nodesToRead,
            CancellationToken ct)
        {
            using (await m_serviceLock.ReaderLockAsync(ct).ConfigureAwait(false))
            {
                return await InnerSession.HistoryReadAsync(
                    requestHeader, historyReadDetails, timestampsToReturn,
                    releaseContinuationPoints, nodesToRead, ct).ConfigureAwait(false);
            }
        }

        /// <inheritdoc/>
        public async ValueTask<WriteResponse> WriteAsync(
            RequestHeader? requestHeader,
            ArrayOf<WriteValue> nodesToWrite,
            CancellationToken ct)
        {
            using (await m_serviceLock.ReaderLockAsync(ct).ConfigureAwait(false))
            {
                return await InnerSession.WriteAsync(
                    requestHeader,
                    nodesToWrite,
                    ct).ConfigureAwait(false);
            }
        }

        /// <inheritdoc/>
        public async ValueTask<HistoryUpdateResponse> HistoryUpdateAsync(
            RequestHeader? requestHeader,
            ArrayOf<ExtensionObject> historyUpdateDetails,
            CancellationToken ct)
        {
            using (await m_serviceLock.ReaderLockAsync(ct).ConfigureAwait(false))
            {
                return await InnerSession.HistoryUpdateAsync(
                    requestHeader,
                    historyUpdateDetails,
                    ct).ConfigureAwait(false);
            }
        }

        /// <inheritdoc/>
        public async ValueTask<BrowseResponse> BrowseAsync(
            RequestHeader? requestHeader,
            ViewDescription? view,
            uint requestedMaxReferencesPerNode,
            ArrayOf<BrowseDescription> nodesToBrowse,
            CancellationToken ct)
        {
            using (await m_serviceLock.ReaderLockAsync(ct).ConfigureAwait(false))
            {
                return await InnerSession.BrowseAsync(
                    requestHeader,
                    view,
                    requestedMaxReferencesPerNode,
                    nodesToBrowse,
                    ct).ConfigureAwait(false);
            }
        }

        /// <inheritdoc/>
        public async ValueTask<BrowseNextResponse> BrowseNextAsync(
            RequestHeader? requestHeader,
            bool releaseContinuationPoints,
            ArrayOf<ByteString> continuationPoints,
            CancellationToken ct)
        {
            using (await m_serviceLock.ReaderLockAsync(ct).ConfigureAwait(false))
            {
                return await InnerSession.BrowseNextAsync(
                    requestHeader,
                    releaseContinuationPoints,
                    continuationPoints,
                    ct).ConfigureAwait(false);
            }
        }

        /// <inheritdoc/>
        public async ValueTask<TranslateBrowsePathsToNodeIdsResponse> TranslateBrowsePathsToNodeIdsAsync(
            RequestHeader? requestHeader,
            ArrayOf<BrowsePath> browsePaths,
            CancellationToken ct)
        {
            using (await m_serviceLock.ReaderLockAsync(ct).ConfigureAwait(false))
            {
                return await InnerSession.TranslateBrowsePathsToNodeIdsAsync(
                    requestHeader,
                    browsePaths,
                    ct).ConfigureAwait(false);
            }
        }

        /// <inheritdoc/>
        public async ValueTask<RegisterNodesResponse> RegisterNodesAsync(
            RequestHeader? requestHeader,
            ArrayOf<NodeId> nodesToRegister,
            CancellationToken ct)
        {
            using (await m_serviceLock.ReaderLockAsync(ct).ConfigureAwait(false))
            {
                return await InnerSession.RegisterNodesAsync(
                    requestHeader,
                    nodesToRegister,
                    ct).ConfigureAwait(false);
            }
        }

        /// <inheritdoc/>
        public async ValueTask<UnregisterNodesResponse> UnregisterNodesAsync(
            RequestHeader? requestHeader,
            ArrayOf<NodeId> nodesToUnregister,
            CancellationToken ct)
        {
            using (await m_serviceLock.ReaderLockAsync(ct).ConfigureAwait(false))
            {
                return await InnerSession.UnregisterNodesAsync(
                    requestHeader,
                    nodesToUnregister,
                    ct).ConfigureAwait(false);
            }
        }

        /// <inheritdoc/>
        public async ValueTask<CallResponse> CallAsync(
            RequestHeader? requestHeader,
            ArrayOf<CallMethodRequest> methodsToCall,
            CancellationToken ct)
        {
            using (await m_serviceLock.ReaderLockAsync(ct).ConfigureAwait(false))
            {
                return await InnerSession.CallAsync(
                    requestHeader,
                    methodsToCall,
                    ct).ConfigureAwait(false);
            }
        }

        /// <inheritdoc/>
        public async ValueTask<CreateMonitoredItemsResponse> CreateMonitoredItemsAsync(
            RequestHeader? requestHeader,
            uint subscriptionId,
            TimestampsToReturn timestampsToReturn,
            ArrayOf<MonitoredItemCreateRequest> itemsToCreate,
            CancellationToken ct)
        {
            using (await m_serviceLock.ReaderLockAsync(ct).ConfigureAwait(false))
            {
                return await InnerSession.CreateMonitoredItemsAsync(
                    requestHeader,
                    subscriptionId,
                    timestampsToReturn,
                    itemsToCreate,
                    ct).ConfigureAwait(false);
            }
        }

        /// <inheritdoc/>
        public async ValueTask<ModifyMonitoredItemsResponse> ModifyMonitoredItemsAsync(
            RequestHeader? requestHeader,
            uint subscriptionId,
            TimestampsToReturn timestampsToReturn,
            ArrayOf<MonitoredItemModifyRequest> itemsToModify,
            CancellationToken ct)
        {
            using (await m_serviceLock.ReaderLockAsync(ct).ConfigureAwait(false))
            {
                return await InnerSession.ModifyMonitoredItemsAsync(
                    requestHeader,
                    subscriptionId,
                    timestampsToReturn,
                    itemsToModify,
                    ct).ConfigureAwait(false);
            }
        }

        /// <inheritdoc/>
        public async ValueTask<SetMonitoringModeResponse> SetMonitoringModeAsync(
            RequestHeader? requestHeader,
            uint subscriptionId,
            MonitoringMode monitoringMode,
            ArrayOf<uint> monitoredItemIds,
            CancellationToken ct)
        {
            using (await m_serviceLock.ReaderLockAsync(ct).ConfigureAwait(false))
            {
                return await InnerSession.SetMonitoringModeAsync(
                    requestHeader,
                    subscriptionId,
                    monitoringMode,
                    monitoredItemIds,
                    ct).ConfigureAwait(false);
            }
        }

        /// <inheritdoc/>
        public async ValueTask<SetTriggeringResponse> SetTriggeringAsync(
            RequestHeader? requestHeader,
            uint subscriptionId,
            uint triggeringItemId,
            ArrayOf<uint> linksToAdd,
            ArrayOf<uint> linksToRemove,
            CancellationToken ct)
        {
            using (await m_serviceLock.ReaderLockAsync(ct).ConfigureAwait(false))
            {
                return await InnerSession.SetTriggeringAsync(
                    requestHeader,
                    subscriptionId,
                    triggeringItemId,
                    linksToAdd,
                    linksToRemove,
                    ct).ConfigureAwait(false);
            }
        }

        /// <inheritdoc/>
        public async ValueTask<DeleteMonitoredItemsResponse> DeleteMonitoredItemsAsync(
            RequestHeader? requestHeader,
            uint subscriptionId,
            ArrayOf<uint> monitoredItemIds,
            CancellationToken ct)
        {
            using (await m_serviceLock.ReaderLockAsync(ct).ConfigureAwait(false))
            {
                return await InnerSession.DeleteMonitoredItemsAsync(
                    requestHeader,
                    subscriptionId,
                    monitoredItemIds,
                    ct).ConfigureAwait(false);
            }
        }

        /// <inheritdoc/>
        public async ValueTask<CreateSubscriptionResponse> CreateSubscriptionAsync(
            RequestHeader? requestHeader,
            double requestedPublishingInterval,
            uint requestedLifetimeCount,
            uint requestedMaxKeepAliveCount,
            uint maxNotificationsPerPublish,
            bool publishingEnabled,
            byte priority,
            CancellationToken ct)
        {
            using (await m_serviceLock.ReaderLockAsync(ct).ConfigureAwait(false))
            {
                return await InnerSession.CreateSubscriptionAsync(
                    requestHeader,
                    requestedPublishingInterval,
                    requestedLifetimeCount,
                    requestedMaxKeepAliveCount,
                    maxNotificationsPerPublish,
                    publishingEnabled,
                    priority,
                    ct).ConfigureAwait(false);
            }
        }

        /// <inheritdoc/>
        public async ValueTask<ModifySubscriptionResponse> ModifySubscriptionAsync(
            RequestHeader? requestHeader,
            uint subscriptionId,
            double requestedPublishingInterval,
            uint requestedLifetimeCount,
            uint requestedMaxKeepAliveCount,
            uint maxNotificationsPerPublish,
            byte priority,
            CancellationToken ct)
        {
            using (await m_serviceLock.ReaderLockAsync(ct).ConfigureAwait(false))
            {
                return await InnerSession.ModifySubscriptionAsync(
                    requestHeader,
                    subscriptionId,
                    requestedPublishingInterval,
                    requestedLifetimeCount,
                    requestedMaxKeepAliveCount,
                    maxNotificationsPerPublish,
                    priority,
                    ct).ConfigureAwait(false);
            }
        }

        /// <inheritdoc/>
        public async ValueTask<SetPublishingModeResponse> SetPublishingModeAsync(
            RequestHeader? requestHeader,
            bool publishingEnabled,
            ArrayOf<uint> subscriptionIds,
            CancellationToken ct)
        {
            using (await m_serviceLock.ReaderLockAsync(ct).ConfigureAwait(false))
            {
                return await InnerSession.SetPublishingModeAsync(
                    requestHeader,
                    publishingEnabled,
                    subscriptionIds,
                    ct).ConfigureAwait(false);
            }
        }

        /// <inheritdoc/>
        public async ValueTask<PublishResponse> PublishAsync(
            RequestHeader? requestHeader,
            ArrayOf<SubscriptionAcknowledgement> subscriptionAcknowledgements,
            CancellationToken ct)
        {
            using (await m_serviceLock.ReaderLockAsync(ct).ConfigureAwait(false))
            {
                return await InnerSession.PublishAsync(
                    requestHeader,
                    subscriptionAcknowledgements,
                    ct).ConfigureAwait(false);
            }
        }

        /// <inheritdoc/>
        public async ValueTask<RepublishResponse> RepublishAsync(
            RequestHeader? requestHeader,
            uint subscriptionId,
            uint retransmitSequenceNumber,
            CancellationToken ct)
        {
            using (await m_serviceLock.ReaderLockAsync(ct).ConfigureAwait(false))
            {
                return await InnerSession.RepublishAsync(
                    requestHeader,
                    subscriptionId,
                    retransmitSequenceNumber,
                    ct).ConfigureAwait(false);
            }
        }

        /// <inheritdoc/>
        public async ValueTask<TransferSubscriptionsResponse> TransferSubscriptionsAsync(
            RequestHeader? requestHeader,
            ArrayOf<uint> subscriptionIds,
            bool sendInitialValues,
            CancellationToken ct)
        {
            using (await m_serviceLock.ReaderLockAsync(ct).ConfigureAwait(false))
            {
                return await InnerSession.TransferSubscriptionsAsync(
                    requestHeader,
                    subscriptionIds,
                    sendInitialValues,
                    ct).ConfigureAwait(false);
            }
        }

        /// <inheritdoc/>
        public async ValueTask<DeleteSubscriptionsResponse> DeleteSubscriptionsAsync(
            RequestHeader? requestHeader,
            ArrayOf<uint> subscriptionIds,
            CancellationToken ct)
        {
            using (await m_serviceLock.ReaderLockAsync(ct).ConfigureAwait(false))
            {
                return await InnerSession.DeleteSubscriptionsAsync(
                    requestHeader,
                    subscriptionIds,
                    ct).ConfigureAwait(false);
            }
        }

        /// <inheritdoc/>
        public async ValueTask<AddNodesResponse> AddNodesAsync(
            RequestHeader? requestHeader,
            ArrayOf<AddNodesItem> nodesToAdd,
            CancellationToken ct)
        {
            using (await m_serviceLock.ReaderLockAsync(ct).ConfigureAwait(false))
            {
                return await InnerSession.AddNodesAsync(
                    requestHeader,
                    nodesToAdd,
                    ct).ConfigureAwait(false);
            }
        }

        /// <inheritdoc/>
        public async ValueTask<AddReferencesResponse> AddReferencesAsync(
            RequestHeader? requestHeader,
            ArrayOf<AddReferencesItem> referencesToAdd,
            CancellationToken ct)
        {
            using (await m_serviceLock.ReaderLockAsync(ct).ConfigureAwait(false))
            {
                return await InnerSession.AddReferencesAsync(
                    requestHeader,
                    referencesToAdd,
                    ct).ConfigureAwait(false);
            }
        }

        /// <inheritdoc/>
        public async ValueTask<DeleteNodesResponse> DeleteNodesAsync(
            RequestHeader? requestHeader,
            ArrayOf<DeleteNodesItem> nodesToDelete,
            CancellationToken ct)
        {
            using (await m_serviceLock.ReaderLockAsync(ct).ConfigureAwait(false))
            {
                return await InnerSession.DeleteNodesAsync(
                    requestHeader,
                    nodesToDelete,
                    ct).ConfigureAwait(false);
            }
        }

        /// <inheritdoc/>
        public async ValueTask<DeleteReferencesResponse> DeleteReferencesAsync(
            RequestHeader? requestHeader,
            ArrayOf<DeleteReferencesItem> referencesToDelete,
            CancellationToken ct)
        {
            using (await m_serviceLock.ReaderLockAsync(ct).ConfigureAwait(false))
            {
                return await InnerSession.DeleteReferencesAsync(
                    requestHeader,
                    referencesToDelete,
                    ct).ConfigureAwait(false);
            }
        }

        /// <inheritdoc/>
        public async ValueTask<QueryFirstResponse> QueryFirstAsync(
            RequestHeader? requestHeader,
            ViewDescription? view,
            ArrayOf<NodeTypeDescription> nodeTypes,
            ContentFilter? filter,
            uint maxDataSetsToReturn,
            uint maxReferencesToReturn,
            CancellationToken ct)
        {
            using (await m_serviceLock.ReaderLockAsync(ct).ConfigureAwait(false))
            {
                return await InnerSession.QueryFirstAsync(
                    requestHeader,
                    view,
                    nodeTypes,
                    filter,
                    maxDataSetsToReturn,
                    maxReferencesToReturn,
                    ct).ConfigureAwait(false);
            }
        }

        /// <inheritdoc/>
        public async ValueTask<QueryNextResponse> QueryNextAsync(
            RequestHeader? requestHeader,
            bool releaseContinuationPoint,
            ByteString continuationPoint,
            CancellationToken ct)
        {
            using (await m_serviceLock.ReaderLockAsync(ct).ConfigureAwait(false))
            {
                return await InnerSession.QueryNextAsync(
                    requestHeader,
                    releaseContinuationPoint,
                    continuationPoint,
                    ct).ConfigureAwait(false);
            }
        }

        /// <inheritdoc/>
        public async ValueTask<CreateSessionResponse> CreateSessionAsync(
            RequestHeader? requestHeader,
            ApplicationDescription? clientDescription,
            string? serverUri,
            string? endpointUrl,
            string? sessionName,
            ByteString clientNonce,
            ByteString clientCertificate,
            double requestedSessionTimeout,
            uint maxResponseMessageSize,
            CancellationToken ct)
        {
            using (await m_serviceLock.ReaderLockAsync(ct).ConfigureAwait(false))
            {
                return await InnerSession.CreateSessionAsync(
                    requestHeader,
                    clientDescription,
                    serverUri,
                    endpointUrl,
                    sessionName,
                    clientNonce,
                    clientCertificate,
                    requestedSessionTimeout,
                    maxResponseMessageSize,
                    ct).ConfigureAwait(false);
            }
        }

        /// <inheritdoc/>
        public async ValueTask<ActivateSessionResponse> ActivateSessionAsync(
            RequestHeader? requestHeader,
            SignatureData? clientSignature,
            ArrayOf<SignedSoftwareCertificate> clientSoftwareCertificates,
            ArrayOf<string> localeIds,
            ExtensionObject userIdentityToken,
            SignatureData? userTokenSignature,
            CancellationToken ct)
        {
            using (await m_serviceLock.ReaderLockAsync(ct).ConfigureAwait(false))
            {
                return await InnerSession.ActivateSessionAsync(
                    requestHeader,
                    clientSignature,
                    clientSoftwareCertificates,
                    localeIds,
                    userIdentityToken,
                    userTokenSignature,
                    ct).ConfigureAwait(false);
            }
        }

        /// <inheritdoc/>
        public async ValueTask<CloseSessionResponse> CloseSessionAsync(
            RequestHeader? requestHeader,
            bool deleteSubscriptions,
            CancellationToken ct)
        {
            using (await m_serviceLock.ReaderLockAsync(ct).ConfigureAwait(false))
            {
                return await InnerSession.CloseSessionAsync(
                    requestHeader,
                    deleteSubscriptions,
                    ct).ConfigureAwait(false);
            }
        }

        /// <inheritdoc/>
        public async ValueTask<CancelResponse> CancelAsync(
            RequestHeader? requestHeader,
            uint requestHandle,
            CancellationToken ct)
        {
            using (await m_serviceLock.ReaderLockAsync(ct).ConfigureAwait(false))
            {
                return await InnerSession.CancelAsync(
                    requestHeader,
                    requestHandle,
                    ct).ConfigureAwait(false);
            }
        }
    }
}
