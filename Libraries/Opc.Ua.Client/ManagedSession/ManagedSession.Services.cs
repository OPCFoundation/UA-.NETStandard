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

namespace Opc.Ua.Client
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Nito.AsyncEx;

    /// <summary>
    /// Service method delegations for <see cref="ManagedSession"/>.
    /// All async methods are gated with a reader lock so that service
    /// calls block during reconnection. Obsolete sync and APM methods
    /// delegate directly to the inner session without gating.
    /// </summary>
    public partial class ManagedSession
    {
        #region IAttributeServiceSetClientMethods

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
                return await InnerSession.ReadAsync(requestHeader, maxAge, timestampsToReturn, nodesToRead, ct).ConfigureAwait(false);
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
                return await InnerSession.HistoryReadAsync(requestHeader, historyReadDetails, timestampsToReturn, releaseContinuationPoints, nodesToRead, ct).ConfigureAwait(false);
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
                return await InnerSession.WriteAsync(requestHeader, nodesToWrite, ct).ConfigureAwait(false);
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
                return await InnerSession.HistoryUpdateAsync(requestHeader, historyUpdateDetails, ct).ConfigureAwait(false);
            }
        }

        /// <inheritdoc/>
        [Obsolete]
        public ResponseHeader? Read(
            RequestHeader? requestHeader,
            double maxAge,
            TimestampsToReturn timestampsToReturn,
            ArrayOf<ReadValueId> nodesToRead,
            out ArrayOf<DataValue> results,
            out ArrayOf<DiagnosticInfo> diagnosticInfos)
            => InnerSession.Read(requestHeader, maxAge, timestampsToReturn, nodesToRead, out results, out diagnosticInfos);

        /// <inheritdoc/>
        [Obsolete]
        public ResponseHeader? HistoryRead(
            RequestHeader? requestHeader,
            ExtensionObject historyReadDetails,
            TimestampsToReturn timestampsToReturn,
            bool releaseContinuationPoints,
            ArrayOf<HistoryReadValueId> nodesToRead,
            out ArrayOf<HistoryReadResult> results,
            out ArrayOf<DiagnosticInfo> diagnosticInfos)
            => InnerSession.HistoryRead(requestHeader, historyReadDetails, timestampsToReturn, releaseContinuationPoints, nodesToRead, out results, out diagnosticInfos);

        /// <inheritdoc/>
        [Obsolete]
        public ResponseHeader? Write(
            RequestHeader? requestHeader,
            ArrayOf<WriteValue> nodesToWrite,
            out ArrayOf<StatusCode> results,
            out ArrayOf<DiagnosticInfo> diagnosticInfos)
            => InnerSession.Write(requestHeader, nodesToWrite, out results, out diagnosticInfos);

        /// <inheritdoc/>
        [Obsolete]
        public ResponseHeader? HistoryUpdate(
            RequestHeader? requestHeader,
            ArrayOf<ExtensionObject> historyUpdateDetails,
            out ArrayOf<HistoryUpdateResult> results,
            out ArrayOf<DiagnosticInfo> diagnosticInfos)
            => InnerSession.HistoryUpdate(requestHeader, historyUpdateDetails, out results, out diagnosticInfos);

        /// <inheritdoc/>
        [Obsolete]
        public IAsyncResult BeginRead(
            RequestHeader? requestHeader,
            double maxAge,
            TimestampsToReturn timestampsToReturn,
            ArrayOf<ReadValueId> nodesToRead,
            AsyncCallback callback,
            object asyncState)
            => InnerSession.BeginRead(requestHeader, maxAge, timestampsToReturn, nodesToRead, callback, asyncState);

        /// <inheritdoc/>
        [Obsolete]
        public IAsyncResult BeginHistoryRead(
            RequestHeader? requestHeader,
            ExtensionObject historyReadDetails,
            TimestampsToReturn timestampsToReturn,
            bool releaseContinuationPoints,
            ArrayOf<HistoryReadValueId> nodesToRead,
            AsyncCallback callback,
            object asyncState)
            => InnerSession.BeginHistoryRead(requestHeader, historyReadDetails, timestampsToReturn, releaseContinuationPoints, nodesToRead, callback, asyncState);

        /// <inheritdoc/>
        [Obsolete]
        public IAsyncResult BeginWrite(
            RequestHeader? requestHeader,
            ArrayOf<WriteValue> nodesToWrite,
            AsyncCallback callback,
            object asyncState)
            => InnerSession.BeginWrite(requestHeader, nodesToWrite, callback, asyncState);

        /// <inheritdoc/>
        [Obsolete]
        public IAsyncResult BeginHistoryUpdate(
            RequestHeader? requestHeader,
            ArrayOf<ExtensionObject> historyUpdateDetails,
            AsyncCallback callback,
            object asyncState)
            => InnerSession.BeginHistoryUpdate(requestHeader, historyUpdateDetails, callback, asyncState);

        /// <inheritdoc/>
        [Obsolete]
        public ResponseHeader? EndRead(
            IAsyncResult result,
            out ArrayOf<DataValue> results,
            out ArrayOf<DiagnosticInfo> diagnosticInfos)
            => InnerSession.EndRead(result, out results, out diagnosticInfos);

        /// <inheritdoc/>
        [Obsolete]
        public ResponseHeader? EndHistoryRead(
            IAsyncResult result,
            out ArrayOf<HistoryReadResult> results,
            out ArrayOf<DiagnosticInfo> diagnosticInfos)
            => InnerSession.EndHistoryRead(result, out results, out diagnosticInfos);

        /// <inheritdoc/>
        [Obsolete]
        public ResponseHeader? EndWrite(
            IAsyncResult result,
            out ArrayOf<StatusCode> results,
            out ArrayOf<DiagnosticInfo> diagnosticInfos)
            => InnerSession.EndWrite(result, out results, out diagnosticInfos);

        /// <inheritdoc/>
        [Obsolete]
        public ResponseHeader? EndHistoryUpdate(
            IAsyncResult result,
            out ArrayOf<HistoryUpdateResult> results,
            out ArrayOf<DiagnosticInfo> diagnosticInfos)
            => InnerSession.EndHistoryUpdate(result, out results, out diagnosticInfos);

        #endregion

        #region IViewServiceSetClientMethods

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
                return await InnerSession.BrowseAsync(requestHeader, view, requestedMaxReferencesPerNode, nodesToBrowse, ct).ConfigureAwait(false);
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
                return await InnerSession.BrowseNextAsync(requestHeader, releaseContinuationPoints, continuationPoints, ct).ConfigureAwait(false);
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
                return await InnerSession.TranslateBrowsePathsToNodeIdsAsync(requestHeader, browsePaths, ct).ConfigureAwait(false);
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
                return await InnerSession.RegisterNodesAsync(requestHeader, nodesToRegister, ct).ConfigureAwait(false);
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
                return await InnerSession.UnregisterNodesAsync(requestHeader, nodesToUnregister, ct).ConfigureAwait(false);
            }
        }

        /// <inheritdoc/>
        [Obsolete]
        public ResponseHeader? Browse(
            RequestHeader? requestHeader,
            ViewDescription? view,
            uint requestedMaxReferencesPerNode,
            ArrayOf<BrowseDescription> nodesToBrowse,
            out ArrayOf<BrowseResult> results,
            out ArrayOf<DiagnosticInfo> diagnosticInfos)
            => InnerSession.Browse(requestHeader, view, requestedMaxReferencesPerNode, nodesToBrowse, out results, out diagnosticInfos);

        /// <inheritdoc/>
        [Obsolete]
        public ResponseHeader? BrowseNext(
            RequestHeader? requestHeader,
            bool releaseContinuationPoints,
            ArrayOf<ByteString> continuationPoints,
            out ArrayOf<BrowseResult> results,
            out ArrayOf<DiagnosticInfo> diagnosticInfos)
            => InnerSession.BrowseNext(requestHeader, releaseContinuationPoints, continuationPoints, out results, out diagnosticInfos);

        /// <inheritdoc/>
        [Obsolete]
        public ResponseHeader? TranslateBrowsePathsToNodeIds(
            RequestHeader? requestHeader,
            ArrayOf<BrowsePath> browsePaths,
            out ArrayOf<BrowsePathResult> results,
            out ArrayOf<DiagnosticInfo> diagnosticInfos)
            => InnerSession.TranslateBrowsePathsToNodeIds(requestHeader, browsePaths, out results, out diagnosticInfos);

        /// <inheritdoc/>
        [Obsolete]
        public ResponseHeader? RegisterNodes(
            RequestHeader? requestHeader,
            ArrayOf<NodeId> nodesToRegister,
            out ArrayOf<NodeId> registeredNodeIds)
            => InnerSession.RegisterNodes(requestHeader, nodesToRegister, out registeredNodeIds);

        /// <inheritdoc/>
        [Obsolete]
        public ResponseHeader? UnregisterNodes(
            RequestHeader? requestHeader,
            ArrayOf<NodeId> nodesToUnregister)
            => InnerSession.UnregisterNodes(requestHeader, nodesToUnregister);

        /// <inheritdoc/>
        [Obsolete]
        public IAsyncResult BeginBrowse(
            RequestHeader? requestHeader,
            ViewDescription? view,
            uint requestedMaxReferencesPerNode,
            ArrayOf<BrowseDescription> nodesToBrowse,
            AsyncCallback callback,
            object asyncState)
            => InnerSession.BeginBrowse(requestHeader, view, requestedMaxReferencesPerNode, nodesToBrowse, callback, asyncState);

        /// <inheritdoc/>
        [Obsolete]
        public IAsyncResult BeginBrowseNext(
            RequestHeader? requestHeader,
            bool releaseContinuationPoints,
            ArrayOf<ByteString> continuationPoints,
            AsyncCallback callback,
            object asyncState)
            => InnerSession.BeginBrowseNext(requestHeader, releaseContinuationPoints, continuationPoints, callback, asyncState);

        /// <inheritdoc/>
        [Obsolete]
        public IAsyncResult BeginTranslateBrowsePathsToNodeIds(
            RequestHeader? requestHeader,
            ArrayOf<BrowsePath> browsePaths,
            AsyncCallback callback,
            object asyncState)
            => InnerSession.BeginTranslateBrowsePathsToNodeIds(requestHeader, browsePaths, callback, asyncState);

        /// <inheritdoc/>
        [Obsolete]
        public IAsyncResult BeginRegisterNodes(
            RequestHeader? requestHeader,
            ArrayOf<NodeId> nodesToRegister,
            AsyncCallback callback,
            object asyncState)
            => InnerSession.BeginRegisterNodes(requestHeader, nodesToRegister, callback, asyncState);

        /// <inheritdoc/>
        [Obsolete]
        public IAsyncResult BeginUnregisterNodes(
            RequestHeader? requestHeader,
            ArrayOf<NodeId> nodesToUnregister,
            AsyncCallback callback,
            object asyncState)
            => InnerSession.BeginUnregisterNodes(requestHeader, nodesToUnregister, callback, asyncState);

        /// <inheritdoc/>
        [Obsolete]
        public ResponseHeader? EndBrowse(
            IAsyncResult result,
            out ArrayOf<BrowseResult> results,
            out ArrayOf<DiagnosticInfo> diagnosticInfos)
            => InnerSession.EndBrowse(result, out results, out diagnosticInfos);

        /// <inheritdoc/>
        [Obsolete]
        public ResponseHeader? EndBrowseNext(
            IAsyncResult result,
            out ArrayOf<BrowseResult> results,
            out ArrayOf<DiagnosticInfo> diagnosticInfos)
            => InnerSession.EndBrowseNext(result, out results, out diagnosticInfos);

        /// <inheritdoc/>
        [Obsolete]
        public ResponseHeader? EndTranslateBrowsePathsToNodeIds(
            IAsyncResult result,
            out ArrayOf<BrowsePathResult> results,
            out ArrayOf<DiagnosticInfo> diagnosticInfos)
            => InnerSession.EndTranslateBrowsePathsToNodeIds(result, out results, out diagnosticInfos);

        /// <inheritdoc/>
        [Obsolete]
        public ResponseHeader? EndRegisterNodes(
            IAsyncResult result,
            out ArrayOf<NodeId> registeredNodeIds)
            => InnerSession.EndRegisterNodes(result, out registeredNodeIds);

        /// <inheritdoc/>
        [Obsolete]
        public ResponseHeader? EndUnregisterNodes(
            IAsyncResult result)
            => InnerSession.EndUnregisterNodes(result);

        #endregion

        #region IMethodServiceSetClientMethods

        /// <inheritdoc/>
        public async ValueTask<CallResponse> CallAsync(
            RequestHeader? requestHeader,
            ArrayOf<CallMethodRequest> methodsToCall,
            CancellationToken ct)
        {
            using (await m_serviceLock.ReaderLockAsync(ct).ConfigureAwait(false))
            {
                return await InnerSession.CallAsync(requestHeader, methodsToCall, ct).ConfigureAwait(false);
            }
        }

        /// <inheritdoc/>
        [Obsolete]
        public ResponseHeader? Call(
            RequestHeader? requestHeader,
            ArrayOf<CallMethodRequest> methodsToCall,
            out ArrayOf<CallMethodResult> results,
            out ArrayOf<DiagnosticInfo> diagnosticInfos)
            => InnerSession.Call(requestHeader, methodsToCall, out results, out diagnosticInfos);

        /// <inheritdoc/>
        [Obsolete]
        public IAsyncResult BeginCall(
            RequestHeader? requestHeader,
            ArrayOf<CallMethodRequest> methodsToCall,
            AsyncCallback callback,
            object asyncState)
            => InnerSession.BeginCall(requestHeader, methodsToCall, callback, asyncState);

        /// <inheritdoc/>
        [Obsolete]
        public ResponseHeader? EndCall(
            IAsyncResult result,
            out ArrayOf<CallMethodResult> results,
            out ArrayOf<DiagnosticInfo> diagnosticInfos)
            => InnerSession.EndCall(result, out results, out diagnosticInfos);

        #endregion

        #region IMonitoredItemServiceSetClientMethods

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
                return await InnerSession.CreateMonitoredItemsAsync(requestHeader, subscriptionId, timestampsToReturn, itemsToCreate, ct).ConfigureAwait(false);
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
                return await InnerSession.ModifyMonitoredItemsAsync(requestHeader, subscriptionId, timestampsToReturn, itemsToModify, ct).ConfigureAwait(false);
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
                return await InnerSession.SetMonitoringModeAsync(requestHeader, subscriptionId, monitoringMode, monitoredItemIds, ct).ConfigureAwait(false);
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
                return await InnerSession.SetTriggeringAsync(requestHeader, subscriptionId, triggeringItemId, linksToAdd, linksToRemove, ct).ConfigureAwait(false);
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
                return await InnerSession.DeleteMonitoredItemsAsync(requestHeader, subscriptionId, monitoredItemIds, ct).ConfigureAwait(false);
            }
        }

        /// <inheritdoc/>
        [Obsolete]
        public ResponseHeader? CreateMonitoredItems(
            RequestHeader? requestHeader,
            uint subscriptionId,
            TimestampsToReturn timestampsToReturn,
            ArrayOf<MonitoredItemCreateRequest> itemsToCreate,
            out ArrayOf<MonitoredItemCreateResult> results,
            out ArrayOf<DiagnosticInfo> diagnosticInfos)
            => InnerSession.CreateMonitoredItems(requestHeader, subscriptionId, timestampsToReturn, itemsToCreate, out results, out diagnosticInfos);

        /// <inheritdoc/>
        [Obsolete]
        public ResponseHeader? ModifyMonitoredItems(
            RequestHeader? requestHeader,
            uint subscriptionId,
            TimestampsToReturn timestampsToReturn,
            ArrayOf<MonitoredItemModifyRequest> itemsToModify,
            out ArrayOf<MonitoredItemModifyResult> results,
            out ArrayOf<DiagnosticInfo> diagnosticInfos)
            => InnerSession.ModifyMonitoredItems(requestHeader, subscriptionId, timestampsToReturn, itemsToModify, out results, out diagnosticInfos);

        /// <inheritdoc/>
        [Obsolete]
        public ResponseHeader? SetMonitoringMode(
            RequestHeader? requestHeader,
            uint subscriptionId,
            MonitoringMode monitoringMode,
            ArrayOf<uint> monitoredItemIds,
            out ArrayOf<StatusCode> results,
            out ArrayOf<DiagnosticInfo> diagnosticInfos)
            => InnerSession.SetMonitoringMode(requestHeader, subscriptionId, monitoringMode, monitoredItemIds, out results, out diagnosticInfos);

        /// <inheritdoc/>
        [Obsolete]
        public ResponseHeader? SetTriggering(
            RequestHeader? requestHeader,
            uint subscriptionId,
            uint triggeringItemId,
            ArrayOf<uint> linksToAdd,
            ArrayOf<uint> linksToRemove,
            out ArrayOf<StatusCode> addResults,
            out ArrayOf<DiagnosticInfo> addDiagnosticInfos,
            out ArrayOf<StatusCode> removeResults,
            out ArrayOf<DiagnosticInfo> removeDiagnosticInfos)
            => InnerSession.SetTriggering(requestHeader, subscriptionId, triggeringItemId, linksToAdd, linksToRemove, out addResults, out addDiagnosticInfos, out removeResults, out removeDiagnosticInfos);

        /// <inheritdoc/>
        [Obsolete]
        public ResponseHeader? DeleteMonitoredItems(
            RequestHeader? requestHeader,
            uint subscriptionId,
            ArrayOf<uint> monitoredItemIds,
            out ArrayOf<StatusCode> results,
            out ArrayOf<DiagnosticInfo> diagnosticInfos)
            => InnerSession.DeleteMonitoredItems(requestHeader, subscriptionId, monitoredItemIds, out results, out diagnosticInfos);

        /// <inheritdoc/>
        [Obsolete]
        public IAsyncResult BeginCreateMonitoredItems(
            RequestHeader? requestHeader,
            uint subscriptionId,
            TimestampsToReturn timestampsToReturn,
            ArrayOf<MonitoredItemCreateRequest> itemsToCreate,
            AsyncCallback callback,
            object asyncState)
            => InnerSession.BeginCreateMonitoredItems(requestHeader, subscriptionId, timestampsToReturn, itemsToCreate, callback, asyncState);

        /// <inheritdoc/>
        [Obsolete]
        public IAsyncResult BeginModifyMonitoredItems(
            RequestHeader? requestHeader,
            uint subscriptionId,
            TimestampsToReturn timestampsToReturn,
            ArrayOf<MonitoredItemModifyRequest> itemsToModify,
            AsyncCallback callback,
            object asyncState)
            => InnerSession.BeginModifyMonitoredItems(requestHeader, subscriptionId, timestampsToReturn, itemsToModify, callback, asyncState);

        /// <inheritdoc/>
        [Obsolete]
        public IAsyncResult BeginSetMonitoringMode(
            RequestHeader? requestHeader,
            uint subscriptionId,
            MonitoringMode monitoringMode,
            ArrayOf<uint> monitoredItemIds,
            AsyncCallback callback,
            object asyncState)
            => InnerSession.BeginSetMonitoringMode(requestHeader, subscriptionId, monitoringMode, monitoredItemIds, callback, asyncState);

        /// <inheritdoc/>
        [Obsolete]
        public IAsyncResult BeginSetTriggering(
            RequestHeader? requestHeader,
            uint subscriptionId,
            uint triggeringItemId,
            ArrayOf<uint> linksToAdd,
            ArrayOf<uint> linksToRemove,
            AsyncCallback callback,
            object asyncState)
            => InnerSession.BeginSetTriggering(requestHeader, subscriptionId, triggeringItemId, linksToAdd, linksToRemove, callback, asyncState);

        /// <inheritdoc/>
        [Obsolete]
        public IAsyncResult BeginDeleteMonitoredItems(
            RequestHeader? requestHeader,
            uint subscriptionId,
            ArrayOf<uint> monitoredItemIds,
            AsyncCallback callback,
            object asyncState)
            => InnerSession.BeginDeleteMonitoredItems(requestHeader, subscriptionId, monitoredItemIds, callback, asyncState);

        /// <inheritdoc/>
        [Obsolete]
        public ResponseHeader? EndCreateMonitoredItems(
            IAsyncResult result,
            out ArrayOf<MonitoredItemCreateResult> results,
            out ArrayOf<DiagnosticInfo> diagnosticInfos)
            => InnerSession.EndCreateMonitoredItems(result, out results, out diagnosticInfos);

        /// <inheritdoc/>
        [Obsolete]
        public ResponseHeader? EndModifyMonitoredItems(
            IAsyncResult result,
            out ArrayOf<MonitoredItemModifyResult> results,
            out ArrayOf<DiagnosticInfo> diagnosticInfos)
            => InnerSession.EndModifyMonitoredItems(result, out results, out diagnosticInfos);

        /// <inheritdoc/>
        [Obsolete]
        public ResponseHeader? EndSetMonitoringMode(
            IAsyncResult result,
            out ArrayOf<StatusCode> results,
            out ArrayOf<DiagnosticInfo> diagnosticInfos)
            => InnerSession.EndSetMonitoringMode(result, out results, out diagnosticInfos);

        /// <inheritdoc/>
        [Obsolete]
        public ResponseHeader? EndSetTriggering(
            IAsyncResult result,
            out ArrayOf<StatusCode> addResults,
            out ArrayOf<DiagnosticInfo> addDiagnosticInfos,
            out ArrayOf<StatusCode> removeResults,
            out ArrayOf<DiagnosticInfo> removeDiagnosticInfos)
            => InnerSession.EndSetTriggering(result, out addResults, out addDiagnosticInfos, out removeResults, out removeDiagnosticInfos);

        /// <inheritdoc/>
        [Obsolete]
        public ResponseHeader? EndDeleteMonitoredItems(
            IAsyncResult result,
            out ArrayOf<StatusCode> results,
            out ArrayOf<DiagnosticInfo> diagnosticInfos)
            => InnerSession.EndDeleteMonitoredItems(result, out results, out diagnosticInfos);

        #endregion

        #region ISubscriptionServiceSetClientMethods

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
                return await InnerSession.CreateSubscriptionAsync(requestHeader, requestedPublishingInterval, requestedLifetimeCount, requestedMaxKeepAliveCount, maxNotificationsPerPublish, publishingEnabled, priority, ct).ConfigureAwait(false);
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
                return await InnerSession.ModifySubscriptionAsync(requestHeader, subscriptionId, requestedPublishingInterval, requestedLifetimeCount, requestedMaxKeepAliveCount, maxNotificationsPerPublish, priority, ct).ConfigureAwait(false);
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
                return await InnerSession.SetPublishingModeAsync(requestHeader, publishingEnabled, subscriptionIds, ct).ConfigureAwait(false);
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
                return await InnerSession.PublishAsync(requestHeader, subscriptionAcknowledgements, ct).ConfigureAwait(false);
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
                return await InnerSession.RepublishAsync(requestHeader, subscriptionId, retransmitSequenceNumber, ct).ConfigureAwait(false);
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
                return await InnerSession.TransferSubscriptionsAsync(requestHeader, subscriptionIds, sendInitialValues, ct).ConfigureAwait(false);
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
                return await InnerSession.DeleteSubscriptionsAsync(requestHeader, subscriptionIds, ct).ConfigureAwait(false);
            }
        }

        /// <inheritdoc/>
        [Obsolete]
        public ResponseHeader? CreateSubscription(
            RequestHeader? requestHeader,
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
            => InnerSession.CreateSubscription(requestHeader, requestedPublishingInterval, requestedLifetimeCount, requestedMaxKeepAliveCount, maxNotificationsPerPublish, publishingEnabled, priority, out subscriptionId, out revisedPublishingInterval, out revisedLifetimeCount, out revisedMaxKeepAliveCount);

        /// <inheritdoc/>
        [Obsolete]
        public ResponseHeader? ModifySubscription(
            RequestHeader? requestHeader,
            uint subscriptionId,
            double requestedPublishingInterval,
            uint requestedLifetimeCount,
            uint requestedMaxKeepAliveCount,
            uint maxNotificationsPerPublish,
            byte priority,
            out double revisedPublishingInterval,
            out uint revisedLifetimeCount,
            out uint revisedMaxKeepAliveCount)
            => InnerSession.ModifySubscription(requestHeader, subscriptionId, requestedPublishingInterval, requestedLifetimeCount, requestedMaxKeepAliveCount, maxNotificationsPerPublish, priority, out revisedPublishingInterval, out revisedLifetimeCount, out revisedMaxKeepAliveCount);

        /// <inheritdoc/>
        [Obsolete]
        public ResponseHeader? SetPublishingMode(
            RequestHeader? requestHeader,
            bool publishingEnabled,
            ArrayOf<uint> subscriptionIds,
            out ArrayOf<StatusCode> results,
            out ArrayOf<DiagnosticInfo> diagnosticInfos)
            => InnerSession.SetPublishingMode(requestHeader, publishingEnabled, subscriptionIds, out results, out diagnosticInfos);

        /// <inheritdoc/>
        [Obsolete]
        public ResponseHeader? Publish(
            RequestHeader? requestHeader,
            ArrayOf<SubscriptionAcknowledgement> subscriptionAcknowledgements,
            out uint subscriptionId,
            out ArrayOf<uint> availableSequenceNumbers,
            out bool moreNotifications,
            out NotificationMessage? notificationMessage,
            out ArrayOf<StatusCode> results,
            out ArrayOf<DiagnosticInfo> diagnosticInfos)
            => InnerSession.Publish(requestHeader, subscriptionAcknowledgements, out subscriptionId, out availableSequenceNumbers, out moreNotifications, out notificationMessage, out results, out diagnosticInfos);

        /// <inheritdoc/>
        [Obsolete]
        public ResponseHeader? Republish(
            RequestHeader? requestHeader,
            uint subscriptionId,
            uint retransmitSequenceNumber,
            out NotificationMessage? notificationMessage)
            => InnerSession.Republish(requestHeader, subscriptionId, retransmitSequenceNumber, out notificationMessage);

        /// <inheritdoc/>
        [Obsolete]
        public ResponseHeader? TransferSubscriptions(
            RequestHeader? requestHeader,
            ArrayOf<uint> subscriptionIds,
            bool sendInitialValues,
            out ArrayOf<TransferResult> results,
            out ArrayOf<DiagnosticInfo> diagnosticInfos)
            => InnerSession.TransferSubscriptions(requestHeader, subscriptionIds, sendInitialValues, out results, out diagnosticInfos);

        /// <inheritdoc/>
        [Obsolete]
        public ResponseHeader? DeleteSubscriptions(
            RequestHeader? requestHeader,
            ArrayOf<uint> subscriptionIds,
            out ArrayOf<StatusCode> results,
            out ArrayOf<DiagnosticInfo> diagnosticInfos)
            => InnerSession.DeleteSubscriptions(requestHeader, subscriptionIds, out results, out diagnosticInfos);

        /// <inheritdoc/>
        [Obsolete]
        public IAsyncResult BeginCreateSubscription(
            RequestHeader? requestHeader,
            double requestedPublishingInterval,
            uint requestedLifetimeCount,
            uint requestedMaxKeepAliveCount,
            uint maxNotificationsPerPublish,
            bool publishingEnabled,
            byte priority,
            AsyncCallback callback,
            object asyncState)
            => InnerSession.BeginCreateSubscription(requestHeader, requestedPublishingInterval, requestedLifetimeCount, requestedMaxKeepAliveCount, maxNotificationsPerPublish, publishingEnabled, priority, callback, asyncState);

        /// <inheritdoc/>
        [Obsolete]
        public IAsyncResult BeginModifySubscription(
            RequestHeader? requestHeader,
            uint subscriptionId,
            double requestedPublishingInterval,
            uint requestedLifetimeCount,
            uint requestedMaxKeepAliveCount,
            uint maxNotificationsPerPublish,
            byte priority,
            AsyncCallback callback,
            object asyncState)
            => InnerSession.BeginModifySubscription(requestHeader, subscriptionId, requestedPublishingInterval, requestedLifetimeCount, requestedMaxKeepAliveCount, maxNotificationsPerPublish, priority, callback, asyncState);

        /// <inheritdoc/>
        [Obsolete]
        public IAsyncResult BeginSetPublishingMode(
            RequestHeader? requestHeader,
            bool publishingEnabled,
            ArrayOf<uint> subscriptionIds,
            AsyncCallback callback,
            object asyncState)
            => InnerSession.BeginSetPublishingMode(requestHeader, publishingEnabled, subscriptionIds, callback, asyncState);

        /// <inheritdoc/>
        [Obsolete]
        public IAsyncResult BeginPublish(
            RequestHeader? requestHeader,
            ArrayOf<SubscriptionAcknowledgement> subscriptionAcknowledgements,
            AsyncCallback callback,
            object asyncState)
            => InnerSession.BeginPublish(requestHeader, subscriptionAcknowledgements, callback, asyncState);

        /// <inheritdoc/>
        [Obsolete]
        public IAsyncResult BeginRepublish(
            RequestHeader? requestHeader,
            uint subscriptionId,
            uint retransmitSequenceNumber,
            AsyncCallback callback,
            object asyncState)
            => InnerSession.BeginRepublish(requestHeader, subscriptionId, retransmitSequenceNumber, callback, asyncState);

        /// <inheritdoc/>
        [Obsolete]
        public IAsyncResult BeginTransferSubscriptions(
            RequestHeader? requestHeader,
            ArrayOf<uint> subscriptionIds,
            bool sendInitialValues,
            AsyncCallback callback,
            object asyncState)
            => InnerSession.BeginTransferSubscriptions(requestHeader, subscriptionIds, sendInitialValues, callback, asyncState);

        /// <inheritdoc/>
        [Obsolete]
        public IAsyncResult BeginDeleteSubscriptions(
            RequestHeader? requestHeader,
            ArrayOf<uint> subscriptionIds,
            AsyncCallback callback,
            object asyncState)
            => InnerSession.BeginDeleteSubscriptions(requestHeader, subscriptionIds, callback, asyncState);

        /// <inheritdoc/>
        [Obsolete]
        public ResponseHeader? EndCreateSubscription(
            IAsyncResult result,
            out uint subscriptionId,
            out double revisedPublishingInterval,
            out uint revisedLifetimeCount,
            out uint revisedMaxKeepAliveCount)
            => InnerSession.EndCreateSubscription(result, out subscriptionId, out revisedPublishingInterval, out revisedLifetimeCount, out revisedMaxKeepAliveCount);

        /// <inheritdoc/>
        [Obsolete]
        public ResponseHeader? EndModifySubscription(
            IAsyncResult result,
            out double revisedPublishingInterval,
            out uint revisedLifetimeCount,
            out uint revisedMaxKeepAliveCount)
            => InnerSession.EndModifySubscription(result, out revisedPublishingInterval, out revisedLifetimeCount, out revisedMaxKeepAliveCount);

        /// <inheritdoc/>
        [Obsolete]
        public ResponseHeader? EndSetPublishingMode(
            IAsyncResult result,
            out ArrayOf<StatusCode> results,
            out ArrayOf<DiagnosticInfo> diagnosticInfos)
            => InnerSession.EndSetPublishingMode(result, out results, out diagnosticInfos);

        /// <inheritdoc/>
        [Obsolete]
        public ResponseHeader? EndPublish(
            IAsyncResult result,
            out uint subscriptionId,
            out ArrayOf<uint> availableSequenceNumbers,
            out bool moreNotifications,
            out NotificationMessage? notificationMessage,
            out ArrayOf<StatusCode> results,
            out ArrayOf<DiagnosticInfo> diagnosticInfos)
            => InnerSession.EndPublish(result, out subscriptionId, out availableSequenceNumbers, out moreNotifications, out notificationMessage, out results, out diagnosticInfos);

        /// <inheritdoc/>
        [Obsolete]
        public ResponseHeader? EndRepublish(
            IAsyncResult result,
            out NotificationMessage? notificationMessage)
            => InnerSession.EndRepublish(result, out notificationMessage);

        /// <inheritdoc/>
        [Obsolete]
        public ResponseHeader? EndTransferSubscriptions(
            IAsyncResult result,
            out ArrayOf<TransferResult> results,
            out ArrayOf<DiagnosticInfo> diagnosticInfos)
            => InnerSession.EndTransferSubscriptions(result, out results, out diagnosticInfos);

        /// <inheritdoc/>
        [Obsolete]
        public ResponseHeader? EndDeleteSubscriptions(
            IAsyncResult result,
            out ArrayOf<StatusCode> results,
            out ArrayOf<DiagnosticInfo> diagnosticInfos)
            => InnerSession.EndDeleteSubscriptions(result, out results, out diagnosticInfos);

        #endregion

        #region INodeManagementServiceSetClientMethods

        /// <inheritdoc/>
        public async ValueTask<AddNodesResponse> AddNodesAsync(
            RequestHeader? requestHeader,
            ArrayOf<AddNodesItem> nodesToAdd,
            CancellationToken ct)
        {
            using (await m_serviceLock.ReaderLockAsync(ct).ConfigureAwait(false))
            {
                return await InnerSession.AddNodesAsync(requestHeader, nodesToAdd, ct).ConfigureAwait(false);
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
                return await InnerSession.AddReferencesAsync(requestHeader, referencesToAdd, ct).ConfigureAwait(false);
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
                return await InnerSession.DeleteNodesAsync(requestHeader, nodesToDelete, ct).ConfigureAwait(false);
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
                return await InnerSession.DeleteReferencesAsync(requestHeader, referencesToDelete, ct).ConfigureAwait(false);
            }
        }

        /// <inheritdoc/>
        [Obsolete]
        public ResponseHeader? AddNodes(
            RequestHeader? requestHeader,
            ArrayOf<AddNodesItem> nodesToAdd,
            out ArrayOf<AddNodesResult> results,
            out ArrayOf<DiagnosticInfo> diagnosticInfos)
            => InnerSession.AddNodes(requestHeader, nodesToAdd, out results, out diagnosticInfos);

        /// <inheritdoc/>
        [Obsolete]
        public ResponseHeader? AddReferences(
            RequestHeader? requestHeader,
            ArrayOf<AddReferencesItem> referencesToAdd,
            out ArrayOf<StatusCode> results,
            out ArrayOf<DiagnosticInfo> diagnosticInfos)
            => InnerSession.AddReferences(requestHeader, referencesToAdd, out results, out diagnosticInfos);

        /// <inheritdoc/>
        [Obsolete]
        public ResponseHeader? DeleteNodes(
            RequestHeader? requestHeader,
            ArrayOf<DeleteNodesItem> nodesToDelete,
            out ArrayOf<StatusCode> results,
            out ArrayOf<DiagnosticInfo> diagnosticInfos)
            => InnerSession.DeleteNodes(requestHeader, nodesToDelete, out results, out diagnosticInfos);

        /// <inheritdoc/>
        [Obsolete]
        public ResponseHeader? DeleteReferences(
            RequestHeader? requestHeader,
            ArrayOf<DeleteReferencesItem> referencesToDelete,
            out ArrayOf<StatusCode> results,
            out ArrayOf<DiagnosticInfo> diagnosticInfos)
            => InnerSession.DeleteReferences(requestHeader, referencesToDelete, out results, out diagnosticInfos);

        /// <inheritdoc/>
        [Obsolete]
        public IAsyncResult BeginAddNodes(
            RequestHeader? requestHeader,
            ArrayOf<AddNodesItem> nodesToAdd,
            AsyncCallback callback,
            object asyncState)
            => InnerSession.BeginAddNodes(requestHeader, nodesToAdd, callback, asyncState);

        /// <inheritdoc/>
        [Obsolete]
        public IAsyncResult BeginAddReferences(
            RequestHeader? requestHeader,
            ArrayOf<AddReferencesItem> referencesToAdd,
            AsyncCallback callback,
            object asyncState)
            => InnerSession.BeginAddReferences(requestHeader, referencesToAdd, callback, asyncState);

        /// <inheritdoc/>
        [Obsolete]
        public IAsyncResult BeginDeleteNodes(
            RequestHeader? requestHeader,
            ArrayOf<DeleteNodesItem> nodesToDelete,
            AsyncCallback callback,
            object asyncState)
            => InnerSession.BeginDeleteNodes(requestHeader, nodesToDelete, callback, asyncState);

        /// <inheritdoc/>
        [Obsolete]
        public IAsyncResult BeginDeleteReferences(
            RequestHeader? requestHeader,
            ArrayOf<DeleteReferencesItem> referencesToDelete,
            AsyncCallback callback,
            object asyncState)
            => InnerSession.BeginDeleteReferences(requestHeader, referencesToDelete, callback, asyncState);

        /// <inheritdoc/>
        [Obsolete]
        public ResponseHeader? EndAddNodes(
            IAsyncResult result,
            out ArrayOf<AddNodesResult> results,
            out ArrayOf<DiagnosticInfo> diagnosticInfos)
            => InnerSession.EndAddNodes(result, out results, out diagnosticInfos);

        /// <inheritdoc/>
        [Obsolete]
        public ResponseHeader? EndAddReferences(
            IAsyncResult result,
            out ArrayOf<StatusCode> results,
            out ArrayOf<DiagnosticInfo> diagnosticInfos)
            => InnerSession.EndAddReferences(result, out results, out diagnosticInfos);

        /// <inheritdoc/>
        [Obsolete]
        public ResponseHeader? EndDeleteNodes(
            IAsyncResult result,
            out ArrayOf<StatusCode> results,
            out ArrayOf<DiagnosticInfo> diagnosticInfos)
            => InnerSession.EndDeleteNodes(result, out results, out diagnosticInfos);

        /// <inheritdoc/>
        [Obsolete]
        public ResponseHeader? EndDeleteReferences(
            IAsyncResult result,
            out ArrayOf<StatusCode> results,
            out ArrayOf<DiagnosticInfo> diagnosticInfos)
            => InnerSession.EndDeleteReferences(result, out results, out diagnosticInfos);

        #endregion

        #region IQueryServiceSetClientMethods

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
                return await InnerSession.QueryFirstAsync(requestHeader, view, nodeTypes, filter, maxDataSetsToReturn, maxReferencesToReturn, ct).ConfigureAwait(false);
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
                return await InnerSession.QueryNextAsync(requestHeader, releaseContinuationPoint, continuationPoint, ct).ConfigureAwait(false);
            }
        }

        /// <inheritdoc/>
        [Obsolete]
        public ResponseHeader? QueryFirst(
            RequestHeader? requestHeader,
            ViewDescription? view,
            ArrayOf<NodeTypeDescription> nodeTypes,
            ContentFilter? filter,
            uint maxDataSetsToReturn,
            uint maxReferencesToReturn,
            out ArrayOf<QueryDataSet> queryDataSets,
            out ByteString continuationPoint,
            out ArrayOf<ParsingResult> parsingResults,
            out ArrayOf<DiagnosticInfo> diagnosticInfos,
            out ContentFilterResult? filterResult)
            => InnerSession.QueryFirst(requestHeader, view, nodeTypes, filter, maxDataSetsToReturn, maxReferencesToReturn, out queryDataSets, out continuationPoint, out parsingResults, out diagnosticInfos, out filterResult);

        /// <inheritdoc/>
        [Obsolete]
        public ResponseHeader? QueryNext(
            RequestHeader? requestHeader,
            bool releaseContinuationPoint,
            ByteString continuationPoint,
            out ArrayOf<QueryDataSet> queryDataSets,
            out ByteString revisedContinuationPoint)
            => InnerSession.QueryNext(requestHeader, releaseContinuationPoint, continuationPoint, out queryDataSets, out revisedContinuationPoint);

        /// <inheritdoc/>
        [Obsolete]
        public IAsyncResult BeginQueryFirst(
            RequestHeader? requestHeader,
            ViewDescription? view,
            ArrayOf<NodeTypeDescription> nodeTypes,
            ContentFilter? filter,
            uint maxDataSetsToReturn,
            uint maxReferencesToReturn,
            AsyncCallback callback,
            object asyncState)
            => InnerSession.BeginQueryFirst(requestHeader, view, nodeTypes, filter, maxDataSetsToReturn, maxReferencesToReturn, callback, asyncState);

        /// <inheritdoc/>
        [Obsolete]
        public IAsyncResult BeginQueryNext(
            RequestHeader? requestHeader,
            bool releaseContinuationPoint,
            ByteString continuationPoint,
            AsyncCallback callback,
            object asyncState)
            => InnerSession.BeginQueryNext(requestHeader, releaseContinuationPoint, continuationPoint, callback, asyncState);

        /// <inheritdoc/>
        [Obsolete]
        public ResponseHeader? EndQueryFirst(
            IAsyncResult result,
            out ArrayOf<QueryDataSet> queryDataSets,
            out ByteString continuationPoint,
            out ArrayOf<ParsingResult> parsingResults,
            out ArrayOf<DiagnosticInfo> diagnosticInfos,
            out ContentFilterResult? filterResult)
            => InnerSession.EndQueryFirst(result, out queryDataSets, out continuationPoint, out parsingResults, out diagnosticInfos, out filterResult);

        /// <inheritdoc/>
        [Obsolete]
        public ResponseHeader? EndQueryNext(
            IAsyncResult result,
            out ArrayOf<QueryDataSet> queryDataSets,
            out ByteString revisedContinuationPoint)
            => InnerSession.EndQueryNext(result, out queryDataSets, out revisedContinuationPoint);

        #endregion

        #region ISessionClientMethods

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
                return await InnerSession.CreateSessionAsync(requestHeader, clientDescription, serverUri, endpointUrl, sessionName, clientNonce, clientCertificate, requestedSessionTimeout, maxResponseMessageSize, ct).ConfigureAwait(false);
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
                return await InnerSession.ActivateSessionAsync(requestHeader, clientSignature, clientSoftwareCertificates, localeIds, userIdentityToken, userTokenSignature, ct).ConfigureAwait(false);
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
                return await InnerSession.CloseSessionAsync(requestHeader, deleteSubscriptions, ct).ConfigureAwait(false);
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
                return await InnerSession.CancelAsync(requestHeader, requestHandle, ct).ConfigureAwait(false);
            }
        }

        /// <inheritdoc/>
        [Obsolete]
        public ResponseHeader? CreateSession(
            RequestHeader? requestHeader,
            ApplicationDescription? clientDescription,
            string? serverUri,
            string? endpointUrl,
            string? sessionName,
            ByteString clientNonce,
            ByteString clientCertificate,
            double requestedSessionTimeout,
            uint maxResponseMessageSize,
            out NodeId sessionId,
            out NodeId authenticationToken,
            out double revisedSessionTimeout,
            out ByteString serverNonce,
            out ByteString serverCertificate,
            out ArrayOf<EndpointDescription> serverEndpoints,
            out ArrayOf<SignedSoftwareCertificate> serverSoftwareCertificates,
            out SignatureData? serverSignature,
            out uint maxRequestMessageSize)
            => InnerSession.CreateSession(requestHeader, clientDescription, serverUri, endpointUrl, sessionName, clientNonce, clientCertificate, requestedSessionTimeout, maxResponseMessageSize, out sessionId, out authenticationToken, out revisedSessionTimeout, out serverNonce, out serverCertificate, out serverEndpoints, out serverSoftwareCertificates, out serverSignature, out maxRequestMessageSize);

        /// <inheritdoc/>
        [Obsolete]
        public ResponseHeader? ActivateSession(
            RequestHeader? requestHeader,
            SignatureData? clientSignature,
            ArrayOf<SignedSoftwareCertificate> clientSoftwareCertificates,
            ArrayOf<string> localeIds,
            ExtensionObject userIdentityToken,
            SignatureData? userTokenSignature,
            out ByteString serverNonce,
            out ArrayOf<StatusCode> results,
            out ArrayOf<DiagnosticInfo> diagnosticInfos)
            => InnerSession.ActivateSession(requestHeader, clientSignature, clientSoftwareCertificates, localeIds, userIdentityToken, userTokenSignature, out serverNonce, out results, out diagnosticInfos);

        /// <inheritdoc/>
        [Obsolete]
        public ResponseHeader? CloseSession(
            RequestHeader? requestHeader,
            bool deleteSubscriptions)
            => InnerSession.CloseSession(requestHeader, deleteSubscriptions);

        /// <inheritdoc/>
        [Obsolete]
        public ResponseHeader? Cancel(
            RequestHeader? requestHeader,
            uint requestHandle,
            out uint cancelCount)
            => InnerSession.Cancel(requestHeader, requestHandle, out cancelCount);

        /// <inheritdoc/>
        [Obsolete]
        public IAsyncResult BeginCreateSession(
            RequestHeader? requestHeader,
            ApplicationDescription? clientDescription,
            string? serverUri,
            string? endpointUrl,
            string? sessionName,
            ByteString clientNonce,
            ByteString clientCertificate,
            double requestedSessionTimeout,
            uint maxResponseMessageSize,
            AsyncCallback callback,
            object asyncState)
            => InnerSession.BeginCreateSession(requestHeader, clientDescription, serverUri, endpointUrl, sessionName, clientNonce, clientCertificate, requestedSessionTimeout, maxResponseMessageSize, callback, asyncState);

        /// <inheritdoc/>
        [Obsolete]
        public IAsyncResult BeginActivateSession(
            RequestHeader? requestHeader,
            SignatureData? clientSignature,
            ArrayOf<SignedSoftwareCertificate> clientSoftwareCertificates,
            ArrayOf<string> localeIds,
            ExtensionObject userIdentityToken,
            SignatureData? userTokenSignature,
            AsyncCallback callback,
            object asyncState)
            => InnerSession.BeginActivateSession(requestHeader, clientSignature, clientSoftwareCertificates, localeIds, userIdentityToken, userTokenSignature, callback, asyncState);

        /// <inheritdoc/>
        [Obsolete]
        public IAsyncResult BeginCloseSession(
            RequestHeader? requestHeader,
            bool deleteSubscriptions,
            AsyncCallback callback,
            object asyncState)
            => InnerSession.BeginCloseSession(requestHeader, deleteSubscriptions, callback, asyncState);

        /// <inheritdoc/>
        [Obsolete]
        public IAsyncResult BeginCancel(
            RequestHeader? requestHeader,
            uint requestHandle,
            AsyncCallback callback,
            object asyncState)
            => InnerSession.BeginCancel(requestHeader, requestHandle, callback, asyncState);

        /// <inheritdoc/>
        [Obsolete]
        public ResponseHeader? EndCreateSession(
            IAsyncResult result,
            out NodeId sessionId,
            out NodeId authenticationToken,
            out double revisedSessionTimeout,
            out ByteString serverNonce,
            out ByteString serverCertificate,
            out ArrayOf<EndpointDescription> serverEndpoints,
            out ArrayOf<SignedSoftwareCertificate> serverSoftwareCertificates,
            out SignatureData? serverSignature,
            out uint maxRequestMessageSize)
            => InnerSession.EndCreateSession(result, out sessionId, out authenticationToken, out revisedSessionTimeout, out serverNonce, out serverCertificate, out serverEndpoints, out serverSoftwareCertificates, out serverSignature, out maxRequestMessageSize);

        /// <inheritdoc/>
        [Obsolete]
        public ResponseHeader? EndActivateSession(
            IAsyncResult result,
            out ByteString serverNonce,
            out ArrayOf<StatusCode> results,
            out ArrayOf<DiagnosticInfo> diagnosticInfos)
            => InnerSession.EndActivateSession(result, out serverNonce, out results, out diagnosticInfos);

        /// <inheritdoc/>
        [Obsolete]
        public ResponseHeader? EndCloseSession(
            IAsyncResult result)
            => InnerSession.EndCloseSession(result);

        /// <inheritdoc/>
        [Obsolete]
        public ResponseHeader? EndCancel(
            IAsyncResult result,
            out uint cancelCount)
            => InnerSession.EndCancel(result, out cancelCount);

        #endregion

    }
}