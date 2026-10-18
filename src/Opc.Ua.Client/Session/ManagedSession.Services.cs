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
using System.Runtime.ExceptionServices;
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
        public ValueTask<ReadResponse> ReadAsync(
            RequestHeader? requestHeader,
            double maxAge,
            TimestampsToReturn timestampsToReturn,
            ArrayOf<ReadValueId> nodesToRead,
            CancellationToken ct)
        {
            if (IsRecoverableApplicationFlow)
            {
                return m_recoverableExecution!.ReadAsync(new ReadRequest
                {
                    RequestHeader = requestHeader!,
                    MaxAge = maxAge,
                    TimestampsToReturn = timestampsToReturn,
                    NodesToRead = nodesToRead
                }, ct);
            }
            return CallServiceAsync(
                (requestHeader, maxAge, timestampsToReturn, nodesToRead),
                static (session, a, ct) => session.ReadAsync(
                    a.requestHeader, a.maxAge, a.timestampsToReturn, a.nodesToRead, ct),
                ct);
        }

        /// <inheritdoc/>
        public ValueTask<HistoryReadResponse> HistoryReadAsync(
            RequestHeader? requestHeader,
            ExtensionObject historyReadDetails,
            TimestampsToReturn timestampsToReturn,
            bool releaseContinuationPoints,
            ArrayOf<HistoryReadValueId> nodesToRead,
            CancellationToken ct)
        {
            return CallServiceAsync(
                (
                    requestHeader,
                    historyReadDetails,
                    timestampsToReturn,
                    releaseContinuationPoints,
                    nodesToRead),
                static (session, a, ct) => session.HistoryReadAsync(
                    a.requestHeader,
                    a.historyReadDetails,
                    a.timestampsToReturn,
                    a.releaseContinuationPoints,
                    a.nodesToRead,
                    ct),
                ct);
        }

        /// <inheritdoc/>
        public ValueTask<WriteResponse> WriteAsync(
            RequestHeader? requestHeader,
            ArrayOf<WriteValue> nodesToWrite,
            CancellationToken ct)
        {
            if (IsRecoverableApplicationFlow)
            {
                return m_recoverableExecution!.WriteAsync(new WriteRequest
                {
                    RequestHeader = requestHeader!,
                    NodesToWrite = nodesToWrite
                }, ct);
            }
            return CallServiceAsync(
                (requestHeader, nodesToWrite),
                static (session, a, ct) => session.WriteAsync(
                    a.requestHeader, a.nodesToWrite, ct),
                ct);
        }

        /// <inheritdoc/>
        public ValueTask<HistoryUpdateResponse> HistoryUpdateAsync(
            RequestHeader? requestHeader,
            ArrayOf<ExtensionObject> historyUpdateDetails,
            CancellationToken ct)
        {
            return CallServiceAsync(
                (requestHeader, historyUpdateDetails),
                static (session, a, ct) => session.HistoryUpdateAsync(
                    a.requestHeader, a.historyUpdateDetails, ct),
                ct);
        }

        /// <inheritdoc/>
        public ValueTask<BrowseResponse> BrowseAsync(
            RequestHeader? requestHeader,
            ViewDescription? view,
            uint requestedMaxReferencesPerNode,
            ArrayOf<BrowseDescription> nodesToBrowse,
            CancellationToken ct)
        {
            return CallServiceAsync(
                (requestHeader, view, requestedMaxReferencesPerNode, nodesToBrowse),
                static (session, a, ct) => session.BrowseAsync(
                    a.requestHeader, a.view, a.requestedMaxReferencesPerNode, a.nodesToBrowse, ct),
                ct);
        }

        /// <inheritdoc/>
        public ValueTask<BrowseNextResponse> BrowseNextAsync(
            RequestHeader? requestHeader,
            bool releaseContinuationPoints,
            ArrayOf<ByteString> continuationPoints,
            CancellationToken ct)
        {
            return CallServiceAsync(
                (requestHeader, releaseContinuationPoints, continuationPoints),
                static (session, a, ct) => session.BrowseNextAsync(
                    a.requestHeader, a.releaseContinuationPoints, a.continuationPoints, ct),
                ct);
        }

        /// <inheritdoc/>
        public ValueTask<TranslateBrowsePathsToNodeIdsResponse> TranslateBrowsePathsToNodeIdsAsync(
            RequestHeader? requestHeader,
            ArrayOf<BrowsePath> browsePaths,
            CancellationToken ct)
        {
            return CallServiceAsync(
                (requestHeader, browsePaths),
                static (session, a, ct) => session.TranslateBrowsePathsToNodeIdsAsync(
                    a.requestHeader, a.browsePaths, ct),
                ct);
        }

        /// <inheritdoc/>
        public ValueTask<RegisterNodesResponse> RegisterNodesAsync(
            RequestHeader? requestHeader,
            ArrayOf<NodeId> nodesToRegister,
            CancellationToken ct)
        {
            return CallServiceAsync(
                (requestHeader, nodesToRegister),
                static (session, a, ct) => session.RegisterNodesAsync(
                    a.requestHeader, a.nodesToRegister, ct),
                ct);
        }

        /// <inheritdoc/>
        public ValueTask<UnregisterNodesResponse> UnregisterNodesAsync(
            RequestHeader? requestHeader,
            ArrayOf<NodeId> nodesToUnregister,
            CancellationToken ct)
        {
            return CallServiceAsync(
                (requestHeader, nodesToUnregister),
                static (session, a, ct) => session.UnregisterNodesAsync(
                    a.requestHeader, a.nodesToUnregister, ct),
                ct);
        }

        /// <inheritdoc/>
        public ValueTask<CallResponse> CallAsync(
            RequestHeader? requestHeader,
            ArrayOf<CallMethodRequest> methodsToCall,
            CancellationToken ct)
        {
            if (IsRecoverableApplicationFlow)
            {
                return m_recoverableExecution!.CallAsync(new CallRequest
                {
                    RequestHeader = requestHeader!,
                    MethodsToCall = methodsToCall
                }, ct);
            }
            return CallServiceAsync(
                (requestHeader, methodsToCall),
                static (session, a, ct) => session.CallAsync(
                    a.requestHeader, a.methodsToCall, ct),
                ct);
        }

        /// <inheritdoc/>
        public ValueTask<CreateMonitoredItemsResponse> CreateMonitoredItemsAsync(
            RequestHeader? requestHeader,
            uint subscriptionId,
            TimestampsToReturn timestampsToReturn,
            ArrayOf<MonitoredItemCreateRequest> itemsToCreate,
            CancellationToken ct)
        {
            return CallServiceAsync(
                (requestHeader, subscriptionId, timestampsToReturn, itemsToCreate),
                static (session, a, ct) => session.CreateMonitoredItemsAsync(
                    a.requestHeader, a.subscriptionId, a.timestampsToReturn, a.itemsToCreate, ct),
                ct);
        }

        /// <inheritdoc/>
        public ValueTask<ModifyMonitoredItemsResponse> ModifyMonitoredItemsAsync(
            RequestHeader? requestHeader,
            uint subscriptionId,
            TimestampsToReturn timestampsToReturn,
            ArrayOf<MonitoredItemModifyRequest> itemsToModify,
            CancellationToken ct)
        {
            return CallServiceAsync(
                (requestHeader, subscriptionId, timestampsToReturn, itemsToModify),
                static (session, a, ct) => session.ModifyMonitoredItemsAsync(
                    a.requestHeader, a.subscriptionId, a.timestampsToReturn, a.itemsToModify, ct),
                ct);
        }

        /// <inheritdoc/>
        public ValueTask<SetMonitoringModeResponse> SetMonitoringModeAsync(
            RequestHeader? requestHeader,
            uint subscriptionId,
            MonitoringMode monitoringMode,
            ArrayOf<uint> monitoredItemIds,
            CancellationToken ct)
        {
            return CallServiceAsync(
                (requestHeader, subscriptionId, monitoringMode, monitoredItemIds),
                static (session, a, ct) => session.SetMonitoringModeAsync(
                    a.requestHeader, a.subscriptionId, a.monitoringMode, a.monitoredItemIds, ct),
                ct);
        }

        /// <inheritdoc/>
        public ValueTask<SetTriggeringResponse> SetTriggeringAsync(
            RequestHeader? requestHeader,
            uint subscriptionId,
            uint triggeringItemId,
            ArrayOf<uint> linksToAdd,
            ArrayOf<uint> linksToRemove,
            CancellationToken ct)
        {
            return CallServiceAsync(
                (requestHeader, subscriptionId, triggeringItemId, linksToAdd, linksToRemove),
                static (session, a, ct) => session.SetTriggeringAsync(
                    a.requestHeader,
                    a.subscriptionId,
                    a.triggeringItemId,
                    a.linksToAdd,
                    a.linksToRemove,
                    ct),
                ct);
        }

        /// <inheritdoc/>
        public ValueTask<DeleteMonitoredItemsResponse> DeleteMonitoredItemsAsync(
            RequestHeader? requestHeader,
            uint subscriptionId,
            ArrayOf<uint> monitoredItemIds,
            CancellationToken ct)
        {
            return CallServiceAsync(
                (requestHeader, subscriptionId, monitoredItemIds),
                static (session, a, ct) => session.DeleteMonitoredItemsAsync(
                    a.requestHeader, a.subscriptionId, a.monitoredItemIds, ct),
                ct);
        }

        /// <inheritdoc/>
        public ValueTask<CreateSubscriptionResponse> CreateSubscriptionAsync(
            RequestHeader? requestHeader,
            double requestedPublishingInterval,
            uint requestedLifetimeCount,
            uint requestedMaxKeepAliveCount,
            uint maxNotificationsPerPublish,
            bool publishingEnabled,
            byte priority,
            CancellationToken ct)
        {
            return CallServiceAsync(
                (
                    requestHeader,
                    requestedPublishingInterval,
                    requestedLifetimeCount,
                    requestedMaxKeepAliveCount,
                    maxNotificationsPerPublish,
                    publishingEnabled,
                    priority),
                static (session, a, ct) => session.CreateSubscriptionAsync(
                    a.requestHeader,
                    a.requestedPublishingInterval,
                    a.requestedLifetimeCount,
                    a.requestedMaxKeepAliveCount,
                    a.maxNotificationsPerPublish,
                    a.publishingEnabled,
                    a.priority,
                    ct),
                ct);
        }

        /// <inheritdoc/>
        public ValueTask<ModifySubscriptionResponse> ModifySubscriptionAsync(
            RequestHeader? requestHeader,
            uint subscriptionId,
            double requestedPublishingInterval,
            uint requestedLifetimeCount,
            uint requestedMaxKeepAliveCount,
            uint maxNotificationsPerPublish,
            byte priority,
            CancellationToken ct)
        {
            return CallServiceAsync(
                (
                    requestHeader,
                    subscriptionId,
                    requestedPublishingInterval,
                    requestedLifetimeCount,
                    requestedMaxKeepAliveCount,
                    maxNotificationsPerPublish,
                    priority),
                static (session, a, ct) => session.ModifySubscriptionAsync(
                    a.requestHeader,
                    a.subscriptionId,
                    a.requestedPublishingInterval,
                    a.requestedLifetimeCount,
                    a.requestedMaxKeepAliveCount,
                    a.maxNotificationsPerPublish,
                    a.priority,
                    ct),
                ct);
        }

        /// <inheritdoc/>
        public ValueTask<SetPublishingModeResponse> SetPublishingModeAsync(
            RequestHeader? requestHeader,
            bool publishingEnabled,
            ArrayOf<uint> subscriptionIds,
            CancellationToken ct)
        {
            return CallServiceAsync(
                (requestHeader, publishingEnabled, subscriptionIds),
                static (session, a, ct) => session.SetPublishingModeAsync(
                    a.requestHeader, a.publishingEnabled, a.subscriptionIds, ct),
                ct);
        }

        /// <inheritdoc/>
        public ValueTask<PublishResponse> PublishAsync(
            RequestHeader? requestHeader,
            ArrayOf<SubscriptionAcknowledgement> subscriptionAcknowledgements,
            CancellationToken ct)
        {
            return CallServiceAsync(
                (requestHeader, subscriptionAcknowledgements),
                static (session, a, ct) => session.PublishAsync(
                    a.requestHeader, a.subscriptionAcknowledgements, ct),
                ct);
        }

        /// <inheritdoc/>
        public ValueTask<RepublishResponse> RepublishAsync(
            RequestHeader? requestHeader,
            uint subscriptionId,
            uint retransmitSequenceNumber,
            CancellationToken ct)
        {
            return CallServiceAsync(
                (requestHeader, subscriptionId, retransmitSequenceNumber),
                static (session, a, ct) => session.RepublishAsync(
                    a.requestHeader, a.subscriptionId, a.retransmitSequenceNumber, ct),
                ct);
        }

        /// <inheritdoc/>
        public ValueTask<TransferSubscriptionsResponse> TransferSubscriptionsAsync(
            RequestHeader? requestHeader,
            ArrayOf<uint> subscriptionIds,
            bool sendInitialValues,
            CancellationToken ct)
        {
            return CallServiceAsync(
                (requestHeader, subscriptionIds, sendInitialValues),
                static (session, a, ct) => session.TransferSubscriptionsAsync(
                    a.requestHeader, a.subscriptionIds, a.sendInitialValues, ct),
                ct);
        }

        /// <inheritdoc/>
        public ValueTask<DeleteSubscriptionsResponse> DeleteSubscriptionsAsync(
            RequestHeader? requestHeader,
            ArrayOf<uint> subscriptionIds,
            CancellationToken ct)
        {
            return CallServiceAsync(
                (requestHeader, subscriptionIds),
                static (session, a, ct) => session.DeleteSubscriptionsAsync(
                    a.requestHeader, a.subscriptionIds, ct),
                ct);
        }

        /// <inheritdoc/>
        public ValueTask<AddNodesResponse> AddNodesAsync(
            RequestHeader? requestHeader,
            ArrayOf<AddNodesItem> nodesToAdd,
            CancellationToken ct)
        {
            return CallServiceAsync(
                (requestHeader, nodesToAdd),
                static (session, a, ct) => session.AddNodesAsync(
                    a.requestHeader, a.nodesToAdd, ct),
                ct);
        }

        /// <inheritdoc/>
        public ValueTask<AddReferencesResponse> AddReferencesAsync(
            RequestHeader? requestHeader,
            ArrayOf<AddReferencesItem> referencesToAdd,
            CancellationToken ct)
        {
            return CallServiceAsync(
                (requestHeader, referencesToAdd),
                static (session, a, ct) => session.AddReferencesAsync(
                    a.requestHeader, a.referencesToAdd, ct),
                ct);
        }

        /// <inheritdoc/>
        public ValueTask<DeleteNodesResponse> DeleteNodesAsync(
            RequestHeader? requestHeader,
            ArrayOf<DeleteNodesItem> nodesToDelete,
            CancellationToken ct)
        {
            return CallServiceAsync(
                (requestHeader, nodesToDelete),
                static (session, a, ct) => session.DeleteNodesAsync(
                    a.requestHeader, a.nodesToDelete, ct),
                ct);
        }

        /// <inheritdoc/>
        public ValueTask<DeleteReferencesResponse> DeleteReferencesAsync(
            RequestHeader? requestHeader,
            ArrayOf<DeleteReferencesItem> referencesToDelete,
            CancellationToken ct)
        {
            return CallServiceAsync(
                (requestHeader, referencesToDelete),
                static (session, a, ct) => session.DeleteReferencesAsync(
                    a.requestHeader, a.referencesToDelete, ct),
                ct);
        }

        /// <inheritdoc/>
        public ValueTask<QueryFirstResponse> QueryFirstAsync(
            RequestHeader? requestHeader,
            ViewDescription? view,
            ArrayOf<NodeTypeDescription> nodeTypes,
            ContentFilter? filter,
            uint maxDataSetsToReturn,
            uint maxReferencesToReturn,
            CancellationToken ct)
        {
            return CallServiceAsync(
                (requestHeader, view, nodeTypes, filter, maxDataSetsToReturn, maxReferencesToReturn),
                static (session, a, ct) => session.QueryFirstAsync(
                    a.requestHeader,
                    a.view,
                    a.nodeTypes,
                    a.filter,
                    a.maxDataSetsToReturn,
                    a.maxReferencesToReturn,
                    ct),
                ct);
        }

        /// <inheritdoc/>
        public ValueTask<QueryNextResponse> QueryNextAsync(
            RequestHeader? requestHeader,
            bool releaseContinuationPoint,
            ByteString continuationPoint,
            CancellationToken ct)
        {
            return CallServiceAsync(
                (requestHeader, releaseContinuationPoint, continuationPoint),
                static (session, a, ct) => session.QueryNextAsync(
                    a.requestHeader, a.releaseContinuationPoint, a.continuationPoint, ct),
                ct);
        }

        /// <inheritdoc/>
        public ValueTask<CreateSessionResponse> CreateSessionAsync(
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
            return CallServiceAsync(
                (
                    requestHeader,
                    clientDescription,
                    serverUri,
                    endpointUrl,
                    sessionName,
                    clientNonce,
                    clientCertificate,
                    requestedSessionTimeout,
                    maxResponseMessageSize),
                static (session, a, ct) => session.CreateSessionAsync(
                    a.requestHeader,
                    a.clientDescription,
                    a.serverUri,
                    a.endpointUrl,
                    a.sessionName,
                    a.clientNonce,
                    a.clientCertificate,
                    a.requestedSessionTimeout,
                    a.maxResponseMessageSize,
                    ct),
                ct);
        }

        /// <inheritdoc/>
        public ValueTask<ActivateSessionResponse> ActivateSessionAsync(
            RequestHeader? requestHeader,
            SignatureData? clientSignature,
            ArrayOf<SignedSoftwareCertificate> clientSoftwareCertificates,
            ArrayOf<string> localeIds,
            ExtensionObject userIdentityToken,
            SignatureData? userTokenSignature,
            CancellationToken ct)
        {
            return CallServiceAsync(
                (
                    requestHeader,
                    clientSignature,
                    clientSoftwareCertificates,
                    localeIds,
                    userIdentityToken,
                    userTokenSignature),
                static (session, a, ct) => session.ActivateSessionAsync(
                    a.requestHeader,
                    a.clientSignature,
                    a.clientSoftwareCertificates,
                    a.localeIds,
                    a.userIdentityToken,
                    a.userTokenSignature,
                    ct),
                ct);
        }

        /// <inheritdoc/>
        public ValueTask<CloseSessionResponse> CloseSessionAsync(
            RequestHeader? requestHeader,
            bool deleteSubscriptions,
            CancellationToken ct)
        {
            return CallServiceAsync(
                (requestHeader, deleteSubscriptions),
                static (session, a, ct) => session.CloseSessionAsync(
                    a.requestHeader, a.deleteSubscriptions, ct),
                ct);
        }

        /// <inheritdoc/>
        public ValueTask<CancelResponse> CancelAsync(
            RequestHeader? requestHeader,
            uint requestHandle,
            CancellationToken ct)
        {
            return CallServiceAsync(
                (requestHeader, requestHandle),
                static (session, a, ct) => session.CancelAsync(
                    a.requestHeader, a.requestHandle, ct),
                ct);
        }

        /// <summary>
        /// Calls a service on the inner session under the reader lock.
        /// </summary>
        /// <remarks>
        /// When services are available and no writer holds or waits for the
        /// lock, the reader lock is taken synchronously and released when the
        /// call completes, before the caller's continuation runs. That avoids
        /// a second async state machine per call; otherwise the call waits
        /// for the lock as <see cref="CallServiceLockedAsync"/> does.
        /// </remarks>
        /// <typeparam name="TArgs">The service arguments, passed to the static call.</typeparam>
        /// <typeparam name="TResponse">The service response.</typeparam>
        private ValueTask<TResponse> CallServiceAsync<TArgs, TResponse>(
            TArgs args,
            Func<Session, TArgs, CancellationToken, ValueTask<TResponse>> call,
            CancellationToken ct,
            bool recoveryControl = false)
        {
            if (!recoveryControl && !StateMachine.IsWorkerFlow)
            {
                m_recoverableExecution?.ValidateOrdinaryDispatch();
            }
            if ((StateMachine.AreServicesAvailable || StateMachine.IsWorkerFlow) &&
                m_serviceLock.TryEnterReader())
            {
                ValueTask<TResponse> operation;
                try
                {
                    operation = call(InnerSession, args, ct);
                }
                catch (Exception ex)
                {
                    m_serviceLock.ExitReader();
                    return RethrowAsync<TResponse>(ExceptionDispatchInfo.Capture(ex));
                }
                return m_serviceLock.ExitReaderOnCompletion(operation);
            }
            return CallServiceLockedAsync(args, call, ct);
        }

        private async ValueTask<TResponse> CallServiceLockedAsync<TArgs, TResponse>(
            TArgs args,
            Func<Session, TArgs, CancellationToken, ValueTask<TResponse>> call,
            CancellationToken ct)
        {
            using (await m_serviceLock.ReaderLockAsync(ct).ConfigureAwait(false))
            {
                return await call(InnerSession, args, ct).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Reports an exception thrown synchronously by a call the way the async
        /// path does: a cancellation as a canceled task, anything else as a fault.
        /// </summary>
        /// <typeparam name="TResponse">The service response.</typeparam>
        private static async ValueTask<TResponse> RethrowAsync<TResponse>(ExceptionDispatchInfo error)
        {
            await Task.CompletedTask.ConfigureAwait(false);
            error.Throw();
            return default!;
        }
    }
}
