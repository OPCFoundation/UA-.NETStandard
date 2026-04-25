#if OPCUA_CLIENT_V2
// ------------------------------------------------------------
//  Copyright (c) Microsoft.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

#pragma warning disable CS0672 // Member overrides obsolete member

namespace Opc.Ua.Client.Sessions.Obsolete
{
    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Logging.Abstractions;
    using Opc.Ua;
    using System;
    using System.Diagnostics;
    using System.Diagnostics.Metrics;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// This base effectively disables all synchronous and old style async
    /// operations on the session client. Calling these methods will result
    /// in exceptions, and it is not possible to override any to work around
    /// this.
    /// </summary>
    public class SessionClient : Opc.Ua.SessionClient
    {
        /// <summary>
        /// Intializes the object with a channel and default operation limits.
        /// </summary>
        /// <param name="channel"></param>
        public SessionClient(ITransportChannel? channel = null)
#pragma warning disable CA2000 // Dispose objects before losing scope
            : base(channel ?? new NullTransportChannel(), NullTelemetry.Instance)
#pragma warning restore CA2000 // Dispose objects before losing scope
            => DetachChannel();

        /// <inheritdoc/>
        public sealed override ResponseHeader? CreateSession(RequestHeader? requestHeader,
            ApplicationDescription? clientDescription, string? serverUri, string? endpointUrl,
            string? sessionName, ByteString clientNonce, ByteString clientCertificate,
            double requestedSessionTimeout, uint maxResponseMessageSize, out NodeId sessionId,
            out NodeId authenticationToken, out double revisedSessionTimeout, out ByteString serverNonce,
            out ByteString serverCertificate, out ArrayOf<EndpointDescription> serverEndpoints,
            out ArrayOf<SignedSoftwareCertificate> serverSoftwareCertificates,
            out SignatureData? serverSignature, out uint maxRequestMessageSize)
        {
            throw NotSupported(nameof(CreateSession));
        }

        /// <inheritdoc/>
        public sealed override IAsyncResult BeginCreateSession(RequestHeader? requestHeader,
            ApplicationDescription? clientDescription, string? serverUri, string? endpointUrl,
            string? sessionName, ByteString clientNonce, ByteString clientCertificate,
            double requestedSessionTimeout, uint maxResponseMessageSize,
            AsyncCallback callback, object asyncState)
        {
            throw NotSupported(nameof(BeginCreateSession));
        }

        /// <inheritdoc/>
        public sealed override ResponseHeader? EndCreateSession(IAsyncResult result,
            out NodeId sessionId, out NodeId authenticationToken,
            out double revisedSessionTimeout, out ByteString serverNonce,
            out ByteString serverCertificate, out ArrayOf<EndpointDescription> serverEndpoints,
            out ArrayOf<SignedSoftwareCertificate> serverSoftwareCertificates,
            out SignatureData? serverSignature,
            out uint maxRequestMessageSize)
        {
            throw NotSupported(nameof(EndCreateSession));
        }

        /// <inheritdoc/>
        public sealed override ResponseHeader? ActivateSession(
            RequestHeader? requestHeader, SignatureData? clientSignature,
            ArrayOf<SignedSoftwareCertificate> clientSoftwareCertificates,
            ArrayOf<string> localeIds, ExtensionObject userIdentityToken,
            SignatureData? userTokenSignature, out ByteString serverNonce,
            out ArrayOf<StatusCode> results, out ArrayOf<DiagnosticInfo> diagnosticInfos)
        {
            throw NotSupported(nameof(ActivateSession));
        }

        /// <inheritdoc/>
        public sealed override IAsyncResult BeginActivateSession(RequestHeader? requestHeader,
            SignatureData? clientSignature, ArrayOf<SignedSoftwareCertificate> clientSoftwareCertificates,
            ArrayOf<string> localeIds, ExtensionObject userIdentityToken, SignatureData? userTokenSignature,
            AsyncCallback callback, object asyncState)
        {
            throw NotSupported(nameof(BeginActivateSession));
        }

        /// <inheritdoc/>
        public sealed override ResponseHeader? EndActivateSession(IAsyncResult result,
            out ByteString serverNonce,
            out ArrayOf<StatusCode> results, out ArrayOf<DiagnosticInfo> diagnosticInfos)
        {
            throw NotSupported(nameof(EndActivateSession));
        }

        /// <inheritdoc/>
        public sealed override ResponseHeader? CloseSession(RequestHeader? requestHeader,
            bool deleteSubscriptions)
        {
            throw NotSupported(nameof(CloseSession));
        }

        /// <inheritdoc/>
        public sealed override IAsyncResult BeginCloseSession(RequestHeader? requestHeader,
            bool deleteSubscriptions, AsyncCallback callback, object asyncState)
        {
            throw NotSupported(nameof(BeginCloseSession));
        }

        /// <inheritdoc/>
        public sealed override ResponseHeader? EndCloseSession(IAsyncResult result)
        {
            throw NotSupported(nameof(EndCloseSession));
        }

        /// <inheritdoc/>
        public sealed override ResponseHeader? Cancel(RequestHeader? requestHeader,
            uint requestHandle, out uint cancelCount)
        {
            throw NotSupported(nameof(Cancel));
        }

        /// <inheritdoc/>
        public sealed override IAsyncResult BeginCancel(RequestHeader? requestHeader,
            uint requestHandle, AsyncCallback callback, object asyncState)
        {
            throw NotSupported(nameof(BeginCancel));
        }

        /// <inheritdoc/>
        public sealed override ResponseHeader? EndCancel(IAsyncResult result, out uint cancelCount)
        {
            throw NotSupported(nameof(EndCancel));
        }

        /// <inheritdoc/>
        public sealed override ResponseHeader? AddNodes(RequestHeader? requestHeader,
            ArrayOf<AddNodesItem> nodesToAdd, out ArrayOf<AddNodesResult> results,
            out ArrayOf<DiagnosticInfo> diagnosticInfos)
        {
            throw NotSupported(nameof(AddNodes));
        }

        /// <inheritdoc/>
        public sealed override IAsyncResult BeginAddNodes(RequestHeader? requestHeader,
            ArrayOf<AddNodesItem> nodesToAdd, AsyncCallback callback, object asyncState)
        {
            throw NotSupported(nameof(BeginAddNodes));
        }

        /// <inheritdoc/>
        public sealed override ResponseHeader? EndAddNodes(IAsyncResult result,
            out ArrayOf<AddNodesResult> results,
            out ArrayOf<DiagnosticInfo> diagnosticInfos)
        {
            throw NotSupported(nameof(EndAddNodes));
        }

        /// <inheritdoc/>
        public sealed override ResponseHeader? AddReferences(RequestHeader? requestHeader,
            ArrayOf<AddReferencesItem> referencesToAdd, out ArrayOf<StatusCode> results,
            out ArrayOf<DiagnosticInfo> diagnosticInfos)
        {
            throw NotSupported(nameof(AddReferences));
        }

        /// <inheritdoc/>
        public sealed override IAsyncResult BeginAddReferences(RequestHeader? requestHeader,
            ArrayOf<AddReferencesItem> referencesToAdd, AsyncCallback callback, object asyncState)
        {
            throw NotSupported(nameof(BeginAddReferences));
        }

        /// <inheritdoc/>
        public sealed override ResponseHeader? EndAddReferences(IAsyncResult result,
            out ArrayOf<StatusCode> results, out ArrayOf<DiagnosticInfo> diagnosticInfos)
        {
            throw NotSupported(nameof(EndAddReferences));
        }

        /// <inheritdoc/>
        public sealed override ResponseHeader? DeleteNodes(RequestHeader? requestHeader,
            ArrayOf<DeleteNodesItem> nodesToDelete, out ArrayOf<StatusCode> results,
            out ArrayOf<DiagnosticInfo> diagnosticInfos)
        {
            throw NotSupported(nameof(DeleteNodes));
        }

        /// <inheritdoc/>
        public sealed override IAsyncResult BeginDeleteNodes(RequestHeader? requestHeader,
            ArrayOf<DeleteNodesItem> nodesToDelete, AsyncCallback callback, object asyncState)
        {
            throw NotSupported(nameof(BeginDeleteNodes));
        }

        /// <inheritdoc/>
        public sealed override ResponseHeader? EndDeleteNodes(IAsyncResult result,
            out ArrayOf<StatusCode> results, out ArrayOf<DiagnosticInfo> diagnosticInfos)
        {
            throw NotSupported(nameof(EndDeleteNodes));
        }

        /// <inheritdoc/>
        public sealed override ResponseHeader? DeleteReferences(RequestHeader? requestHeader,
            ArrayOf<DeleteReferencesItem> referencesToDelete, out ArrayOf<StatusCode> results,
            out ArrayOf<DiagnosticInfo> diagnosticInfos)
        {
            throw NotSupported(nameof(DeleteReferences));
        }

        /// <inheritdoc/>
        public sealed override IAsyncResult BeginDeleteReferences(RequestHeader? requestHeader,
            ArrayOf<DeleteReferencesItem> referencesToDelete, AsyncCallback callback,
            object asyncState)
        {
            throw NotSupported(nameof(BeginDeleteReferences));
        }

        /// <inheritdoc/>
        public sealed override ResponseHeader? EndDeleteReferences(IAsyncResult result,
            out ArrayOf<StatusCode> results, out ArrayOf<DiagnosticInfo> diagnosticInfos)
        {
            throw NotSupported(nameof(EndDeleteReferences));
        }

        /// <inheritdoc/>
        public sealed override ResponseHeader? Browse(RequestHeader? requestHeader,
            ViewDescription? view,
            uint requestedMaxReferencesPerNode, ArrayOf<BrowseDescription> nodesToBrowse,
            out ArrayOf<BrowseResult> results, out ArrayOf<DiagnosticInfo> diagnosticInfos)
        {
            throw NotSupported(nameof(Browse));
        }

        /// <inheritdoc/>
        public sealed override IAsyncResult BeginBrowse(RequestHeader? requestHeader,
            ViewDescription? view, uint requestedMaxReferencesPerNode,
            ArrayOf<BrowseDescription> nodesToBrowse, AsyncCallback callback, object asyncState)
        {
            throw NotSupported(nameof(BeginBrowse));
        }

        /// <inheritdoc/>
        public sealed override ResponseHeader? EndBrowse(IAsyncResult result,
            out ArrayOf<BrowseResult> results, out ArrayOf<DiagnosticInfo> diagnosticInfos)
        {
            throw NotSupported(nameof(EndBrowse));
        }

        /// <inheritdoc/>
        public sealed override ResponseHeader? BrowseNext(RequestHeader? requestHeader,
            bool releaseContinuationPoints, ArrayOf<ByteString> continuationPoints,
            out ArrayOf<BrowseResult> results, out ArrayOf<DiagnosticInfo> diagnosticInfos)
        {
            throw NotSupported(nameof(BrowseNext));
        }

        /// <inheritdoc/>
        public sealed override IAsyncResult BeginBrowseNext(RequestHeader? requestHeader,
            bool releaseContinuationPoints, ArrayOf<ByteString> continuationPoints,
            AsyncCallback callback, object asyncState)
        {
            throw NotSupported(nameof(BeginBrowseNext));
        }

        /// <inheritdoc/>
        public sealed override ResponseHeader? EndBrowseNext(IAsyncResult result,
            out ArrayOf<BrowseResult> results, out ArrayOf<DiagnosticInfo> diagnosticInfos)
        {
            throw NotSupported(nameof(EndBrowseNext));
        }

        /// <inheritdoc/>
        public sealed override ResponseHeader? TranslateBrowsePathsToNodeIds(
            RequestHeader? requestHeader,
            ArrayOf<BrowsePath> browsePaths, out ArrayOf<BrowsePathResult> results,
            out ArrayOf<DiagnosticInfo> diagnosticInfos)
        {
            throw NotSupported(nameof(TranslateBrowsePathsToNodeIds));
        }

        /// <inheritdoc/>
        public sealed override IAsyncResult BeginTranslateBrowsePathsToNodeIds(
            RequestHeader? requestHeader,
            ArrayOf<BrowsePath> browsePaths, AsyncCallback callback, object asyncState)
        {
            throw NotSupported(nameof(BeginTranslateBrowsePathsToNodeIds));
        }

        /// <inheritdoc/>
        public sealed override ResponseHeader? EndTranslateBrowsePathsToNodeIds(IAsyncResult result,
            out ArrayOf<BrowsePathResult> results, out ArrayOf<DiagnosticInfo> diagnosticInfos)
        {
            throw NotSupported(nameof(EndTranslateBrowsePathsToNodeIds));
        }

        /// <inheritdoc/>
        public sealed override ResponseHeader? RegisterNodes(RequestHeader? requestHeader,
            ArrayOf<NodeId> nodesToRegister, out ArrayOf<NodeId> registeredNodeIds)
        {
            throw NotSupported(nameof(RegisterNodes));
        }

        /// <inheritdoc/>
        public sealed override IAsyncResult BeginRegisterNodes(RequestHeader? requestHeader,
            ArrayOf<NodeId> nodesToRegister, AsyncCallback callback, object asyncState)
        {
            throw NotSupported(nameof(BeginRegisterNodes));
        }

        /// <inheritdoc/>
        public sealed override ResponseHeader? EndRegisterNodes(IAsyncResult result,
            out ArrayOf<NodeId> registeredNodeIds)
        {
            throw NotSupported(nameof(EndRegisterNodes));
        }

        /// <inheritdoc/>
        public sealed override ResponseHeader? UnregisterNodes(RequestHeader? requestHeader,
            ArrayOf<NodeId> nodesToUnregister)
        {
            throw NotSupported(nameof(UnregisterNodes));
        }

        /// <inheritdoc/>
        public sealed override IAsyncResult BeginUnregisterNodes(RequestHeader? requestHeader,
            ArrayOf<NodeId> nodesToUnregister, AsyncCallback callback, object asyncState)
        {
            throw NotSupported(nameof(BeginUnregisterNodes));
        }

        /// <inheritdoc/>
        public sealed override ResponseHeader? EndUnregisterNodes(IAsyncResult result)
        {
            throw NotSupported(nameof(EndUnregisterNodes));
        }

        /// <inheritdoc/>
        public sealed override ResponseHeader? QueryFirst(RequestHeader? requestHeader,
            ViewDescription? view, ArrayOf<NodeTypeDescription> nodeTypes,
            ContentFilter? filter, uint maxDataSetsToReturn, uint maxReferencesToReturn,
            out ArrayOf<QueryDataSet> queryDataSets, out ByteString continuationPoint,
            out ArrayOf<ParsingResult> parsingResults, out ArrayOf<DiagnosticInfo> diagnosticInfos,
            out ContentFilterResult? filterResult)
        {
            throw NotSupported(nameof(QueryFirst));
        }

        /// <inheritdoc/>
        public sealed override IAsyncResult BeginQueryFirst(RequestHeader? requestHeader,
            ViewDescription? view, ArrayOf<NodeTypeDescription> nodeTypes, ContentFilter? filter,
            uint maxDataSetsToReturn, uint maxReferencesToReturn, AsyncCallback callback,
            object asyncState)
        {
            throw NotSupported(nameof(BeginQueryFirst));
        }

        /// <inheritdoc/>
        public sealed override ResponseHeader? EndQueryFirst(IAsyncResult result,
            out ArrayOf<QueryDataSet> queryDataSets, out ByteString continuationPoint,
            out ArrayOf<ParsingResult> parsingResults, out ArrayOf<DiagnosticInfo> diagnosticInfos,
            out ContentFilterResult? filterResult)
        {
            throw NotSupported(nameof(EndQueryFirst));
        }

        /// <inheritdoc/>
        public sealed override ResponseHeader? QueryNext(RequestHeader? requestHeader,
            bool releaseContinuationPoint, ByteString continuationPoint,
            out ArrayOf<QueryDataSet> queryDataSets, out ByteString revisedContinuationPoint)
        {
            throw NotSupported(nameof(QueryNext));
        }

        /// <inheritdoc/>
        public sealed override IAsyncResult BeginQueryNext(RequestHeader? requestHeader,
            bool releaseContinuationPoint, ByteString continuationPoint, AsyncCallback callback,
            object asyncState)
        {
            throw NotSupported(nameof(BeginQueryNext));
        }

        /// <inheritdoc/>
        public sealed override ResponseHeader? EndQueryNext(IAsyncResult result,
            out ArrayOf<QueryDataSet> queryDataSets, out ByteString revisedContinuationPoint)
        {
            throw NotSupported(nameof(EndQueryNext));
        }

        /// <inheritdoc/>
        public sealed override ResponseHeader? Read(RequestHeader? requestHeader, double maxAge,
            TimestampsToReturn timestampsToReturn, ArrayOf<ReadValueId> nodesToRead,
            out ArrayOf<DataValue> results, out ArrayOf<DiagnosticInfo> diagnosticInfos)
        {
            throw NotSupported(nameof(Read));
        }

        /// <inheritdoc/>
        public sealed override IAsyncResult BeginRead(RequestHeader? requestHeader, double maxAge,
            TimestampsToReturn timestampsToReturn, ArrayOf<ReadValueId> nodesToRead,
            AsyncCallback callback, object asyncState)
        {
            throw NotSupported(nameof(BeginRead));
        }

        /// <inheritdoc/>
        public sealed override ResponseHeader? EndRead(IAsyncResult result,
            out ArrayOf<DataValue> results, out ArrayOf<DiagnosticInfo> diagnosticInfos)
        {
            throw NotSupported(nameof(EndRead));
        }

        /// <inheritdoc/>
        public sealed override ResponseHeader? HistoryRead(RequestHeader? requestHeader,
            ExtensionObject historyReadDetails, TimestampsToReturn timestampsToReturn,
            bool releaseContinuationPoints, ArrayOf<HistoryReadValueId> nodesToRead,
            out ArrayOf<HistoryReadResult> results, out ArrayOf<DiagnosticInfo> diagnosticInfos)
        {
            throw NotSupported(nameof(HistoryRead));
        }

        /// <inheritdoc/>
        public sealed override IAsyncResult BeginHistoryRead(RequestHeader? requestHeader,
            ExtensionObject historyReadDetails, TimestampsToReturn timestampsToReturn,
            bool releaseContinuationPoints, ArrayOf<HistoryReadValueId> nodesToRead,
            AsyncCallback callback, object asyncState)
        {
            throw NotSupported(nameof(BeginHistoryRead));
        }

        /// <inheritdoc/>
        public sealed override ResponseHeader? EndHistoryRead(IAsyncResult result,
            out ArrayOf<HistoryReadResult> results, out ArrayOf<DiagnosticInfo> diagnosticInfos)
        {
            throw NotSupported(nameof(EndHistoryRead));
        }

        /// <inheritdoc/>
        public sealed override ResponseHeader? Write(RequestHeader? requestHeader,
            ArrayOf<WriteValue> nodesToWrite, out ArrayOf<StatusCode> results,
            out ArrayOf<DiagnosticInfo> diagnosticInfos)
        {
            throw NotSupported(nameof(Write));
        }

        /// <inheritdoc/>
        public sealed override IAsyncResult BeginWrite(RequestHeader? requestHeader,
            ArrayOf<WriteValue> nodesToWrite, AsyncCallback callback, object asyncState)
        {
            throw NotSupported(nameof(BeginWrite));
        }

        /// <inheritdoc/>
        public sealed override ResponseHeader? EndWrite(IAsyncResult result,
            out ArrayOf<StatusCode> results, out ArrayOf<DiagnosticInfo> diagnosticInfos)
        {
            throw NotSupported(nameof(EndWrite));
        }

        /// <inheritdoc/>
        public sealed override ResponseHeader? HistoryUpdate(RequestHeader? requestHeader,
            ArrayOf<ExtensionObject> historyUpdateDetails,
            out ArrayOf<HistoryUpdateResult> results,
            out ArrayOf<DiagnosticInfo> diagnosticInfos)
        {
            throw NotSupported(nameof(HistoryUpdate));
        }

        /// <inheritdoc/>
        public sealed override IAsyncResult BeginHistoryUpdate(RequestHeader? requestHeader,
            ArrayOf<ExtensionObject> historyUpdateDetails, AsyncCallback callback,
            object asyncState)
        {
            throw NotSupported(nameof(BeginHistoryUpdate));
        }

        /// <inheritdoc/>
        public sealed override ResponseHeader? EndHistoryUpdate(IAsyncResult result,
            out ArrayOf<HistoryUpdateResult> results, out ArrayOf<DiagnosticInfo> diagnosticInfos)
        {
            throw NotSupported(nameof(EndHistoryUpdate));
        }

        /// <inheritdoc/>
        public sealed override ResponseHeader? Call(RequestHeader? requestHeader,
            ArrayOf<CallMethodRequest> methodsToCall, out ArrayOf<CallMethodResult> results,
            out ArrayOf<DiagnosticInfo> diagnosticInfos)
        {
            throw NotSupported(nameof(Call));
        }

        /// <inheritdoc/>
        public sealed override IAsyncResult BeginCall(RequestHeader? requestHeader,
            ArrayOf<CallMethodRequest> methodsToCall, AsyncCallback callback, object asyncState)
        {
            throw NotSupported(nameof(BeginCall));
        }

        /// <inheritdoc/>
        public sealed override ResponseHeader? EndCall(IAsyncResult result,
            out ArrayOf<CallMethodResult> results, out ArrayOf<DiagnosticInfo> diagnosticInfos)
        {
            throw NotSupported(nameof(EndCall));
        }

        /// <inheritdoc/>
        public sealed override ResponseHeader? CreateMonitoredItems(RequestHeader? requestHeader,
            uint subscriptionId, TimestampsToReturn timestampsToReturn,
            ArrayOf<MonitoredItemCreateRequest> itemsToCreate,
            out ArrayOf<MonitoredItemCreateResult> results,
            out ArrayOf<DiagnosticInfo> diagnosticInfos)
        {
            throw NotSupported(nameof(CreateMonitoredItems));
        }

        /// <inheritdoc/>
        public sealed override IAsyncResult BeginCreateMonitoredItems(
            RequestHeader? requestHeader,
            uint subscriptionId, TimestampsToReturn timestampsToReturn,
            ArrayOf<MonitoredItemCreateRequest> itemsToCreate, AsyncCallback callback,
            object asyncState)
        {
            throw NotSupported(nameof(BeginCreateMonitoredItems));
        }

        /// <inheritdoc/>
        public sealed override ResponseHeader? EndCreateMonitoredItems(IAsyncResult result,
            out ArrayOf<MonitoredItemCreateResult> results,
            out ArrayOf<DiagnosticInfo> diagnosticInfos)
        {
            throw NotSupported(nameof(EndCreateMonitoredItems));
        }

        /// <inheritdoc/>
        public sealed override ResponseHeader? ModifyMonitoredItems(RequestHeader? requestHeader,
            uint subscriptionId, TimestampsToReturn timestampsToReturn,
            ArrayOf<MonitoredItemModifyRequest> itemsToModify,
            out ArrayOf<MonitoredItemModifyResult> results,
            out ArrayOf<DiagnosticInfo> diagnosticInfos)
        {
            throw NotSupported(nameof(ModifyMonitoredItems));
        }

        /// <inheritdoc/>
        public sealed override IAsyncResult BeginModifyMonitoredItems(
            RequestHeader? requestHeader,
            uint subscriptionId, TimestampsToReturn timestampsToReturn,
            ArrayOf<MonitoredItemModifyRequest> itemsToModify, AsyncCallback callback,
            object asyncState)
        {
            throw NotSupported(nameof(BeginModifyMonitoredItems));
        }

        /// <inheritdoc/>
        public sealed override ResponseHeader? EndModifyMonitoredItems(IAsyncResult result,
            out ArrayOf<MonitoredItemModifyResult> results,
            out ArrayOf<DiagnosticInfo> diagnosticInfos)
        {
            throw NotSupported(nameof(EndModifyMonitoredItems));
        }

        /// <inheritdoc/>
        public sealed override ResponseHeader? SetMonitoringMode(RequestHeader? requestHeader,
            uint subscriptionId, MonitoringMode monitoringMode, ArrayOf<uint> monitoredItemIds,
            out ArrayOf<StatusCode> results, out ArrayOf<DiagnosticInfo> diagnosticInfos)
        {
            throw NotSupported(nameof(SetMonitoringMode));
        }

        /// <inheritdoc/>
        public sealed override IAsyncResult BeginSetMonitoringMode(RequestHeader? requestHeader,
            uint subscriptionId, MonitoringMode monitoringMode, ArrayOf<uint> monitoredItemIds,
            AsyncCallback callback, object asyncState)
        {
            throw NotSupported(nameof(BeginSetMonitoringMode));
        }

        /// <inheritdoc/>
        public sealed override ResponseHeader? EndSetMonitoringMode(IAsyncResult result,
            out ArrayOf<StatusCode> results, out ArrayOf<DiagnosticInfo> diagnosticInfos)
        {
            throw NotSupported(nameof(EndSetMonitoringMode));
        }

        /// <inheritdoc/>
        public sealed override ResponseHeader? SetTriggering(RequestHeader? requestHeader,
            uint subscriptionId, uint triggeringItemId, ArrayOf<uint> linksToAdd,
            ArrayOf<uint> linksToRemove, out ArrayOf<StatusCode> addResults,
            out ArrayOf<DiagnosticInfo> addDiagnosticInfos, out ArrayOf<StatusCode> removeResults,
            out ArrayOf<DiagnosticInfo> removeDiagnosticInfos)
        {
            throw NotSupported(nameof(SetTriggering));
        }

        /// <inheritdoc/>
        public sealed override IAsyncResult BeginSetTriggering(RequestHeader? requestHeader,
            uint subscriptionId, uint triggeringItemId, ArrayOf<uint> linksToAdd,
            ArrayOf<uint> linksToRemove, AsyncCallback callback, object asyncState)
        {
            throw NotSupported(nameof(BeginSetTriggering));
        }

        /// <inheritdoc/>
        public sealed override ResponseHeader? EndSetTriggering(IAsyncResult result,
            out ArrayOf<StatusCode> addResults, out ArrayOf<DiagnosticInfo> addDiagnosticInfos,
            out ArrayOf<StatusCode> removeResults,
            out ArrayOf<DiagnosticInfo> removeDiagnosticInfos)
        {
            throw NotSupported(nameof(EndSetTriggering));
        }

        /// <inheritdoc/>
        public sealed override ResponseHeader? DeleteMonitoredItems(RequestHeader? requestHeader,
            uint subscriptionId, ArrayOf<uint> monitoredItemIds,
            out ArrayOf<StatusCode> results,
            out ArrayOf<DiagnosticInfo> diagnosticInfos)
        {
            throw NotSupported(nameof(DeleteMonitoredItems));
        }

        /// <inheritdoc/>
        public sealed override IAsyncResult BeginDeleteMonitoredItems(
            RequestHeader? requestHeader,
            uint subscriptionId, ArrayOf<uint> monitoredItemIds, AsyncCallback callback,
            object asyncState)
        {
            throw NotSupported(nameof(BeginDeleteMonitoredItems));
        }

        /// <inheritdoc/>
        public sealed override ResponseHeader? EndDeleteMonitoredItems(IAsyncResult result,
            out ArrayOf<StatusCode> results, out ArrayOf<DiagnosticInfo> diagnosticInfos)
        {
            throw NotSupported(nameof(EndDeleteMonitoredItems));
        }

        /// <inheritdoc/>
        public sealed override ResponseHeader? CreateSubscription(RequestHeader? requestHeader,
            double requestedPublishingInterval, uint requestedLifetimeCount,
            uint requestedMaxKeepAliveCount, uint maxNotificationsPerPublish,
            bool publishingEnabled, byte priority, out uint subscriptionId,
            out double revisedPublishingInterval, out uint revisedLifetimeCount,
            out uint revisedMaxKeepAliveCount)
        {
            throw NotSupported(nameof(CreateSubscription));
        }

        /// <inheritdoc/>
        public sealed override IAsyncResult BeginCreateSubscription(
            RequestHeader? requestHeader,
            double requestedPublishingInterval, uint requestedLifetimeCount,
            uint requestedMaxKeepAliveCount, uint maxNotificationsPerPublish,
            bool publishingEnabled, byte priority, AsyncCallback callback, object asyncState)
        {
            throw NotSupported(nameof(BeginCreateSubscription));
        }

        /// <inheritdoc/>
        public sealed override ResponseHeader? EndCreateSubscription(IAsyncResult result,
            out uint subscriptionId, out double revisedPublishingInterval,
            out uint revisedLifetimeCount, out uint revisedMaxKeepAliveCount)
        {
            throw NotSupported(nameof(EndCreateSubscription));
        }

        /// <inheritdoc/>
        public sealed override ResponseHeader? ModifySubscription(RequestHeader? requestHeader,
            uint subscriptionId, double requestedPublishingInterval,
            uint requestedLifetimeCount,
            uint requestedMaxKeepAliveCount, uint maxNotificationsPerPublish, byte priority,
            out double revisedPublishingInterval, out uint revisedLifetimeCount,
            out uint revisedMaxKeepAliveCount)
        {
            throw NotSupported(nameof(ModifySubscription));
        }

        /// <inheritdoc/>
        public sealed override IAsyncResult BeginModifySubscription(
            RequestHeader? requestHeader,
            uint subscriptionId, double requestedPublishingInterval,
            uint requestedLifetimeCount,
            uint requestedMaxKeepAliveCount, uint maxNotificationsPerPublish, byte priority,
            AsyncCallback callback, object asyncState)
        {
            throw NotSupported(nameof(BeginModifySubscription));
        }

        /// <inheritdoc/>
        public sealed override ResponseHeader? EndModifySubscription(IAsyncResult result,
            out double revisedPublishingInterval, out uint revisedLifetimeCount,
            out uint revisedMaxKeepAliveCount)
        {
            throw NotSupported(nameof(EndModifySubscription));
        }

        /// <inheritdoc/>
        public sealed override ResponseHeader? SetPublishingMode(RequestHeader? requestHeader,
            bool publishingEnabled, ArrayOf<uint> subscriptionIds,
            out ArrayOf<StatusCode> results,
            out ArrayOf<DiagnosticInfo> diagnosticInfos)
        {
            throw NotSupported(nameof(SetPublishingMode));
        }

        /// <inheritdoc/>
        public sealed override IAsyncResult BeginSetPublishingMode(RequestHeader? requestHeader,
            bool publishingEnabled, ArrayOf<uint> subscriptionIds, AsyncCallback callback,
            object asyncState)
        {
            throw NotSupported(nameof(BeginSetPublishingMode));
        }

        /// <inheritdoc/>
        public sealed override ResponseHeader? EndSetPublishingMode(IAsyncResult result,
            out ArrayOf<StatusCode> results, out ArrayOf<DiagnosticInfo> diagnosticInfos)
        {
            throw NotSupported(nameof(EndSetPublishingMode));
        }

        /// <inheritdoc/>
        public sealed override ResponseHeader? Publish(RequestHeader? requestHeader,
            ArrayOf<SubscriptionAcknowledgement> subscriptionAcknowledgements,
            out uint subscriptionId, out ArrayOf<uint> availableSequenceNumbers,
            out bool moreNotifications, out NotificationMessage? notificationMessage,
            out ArrayOf<StatusCode> results, out ArrayOf<DiagnosticInfo> diagnosticInfos)
        {
            throw NotSupported(nameof(Publish));
        }

        /// <inheritdoc/>
        public sealed override IAsyncResult BeginPublish(RequestHeader? requestHeader,
            ArrayOf<SubscriptionAcknowledgement> subscriptionAcknowledgements,
            AsyncCallback callback, object asyncState)
        {
            throw NotSupported(nameof(BeginPublish));
        }

        /// <inheritdoc/>
        public sealed override ResponseHeader? EndPublish(IAsyncResult result,
            out uint subscriptionId,
            out ArrayOf<uint> availableSequenceNumbers, out bool moreNotifications,
            out NotificationMessage? notificationMessage, out ArrayOf<StatusCode> results,
            out ArrayOf<DiagnosticInfo> diagnosticInfos)
        {
            throw NotSupported(nameof(EndPublish));
        }

        /// <inheritdoc/>
        public sealed override ResponseHeader? Republish(RequestHeader? requestHeader,
            uint subscriptionId, uint retransmitSequenceNumber,
            out NotificationMessage? notificationMessage)
        {
            throw NotSupported(nameof(Republish));
        }

        /// <inheritdoc/>
        public sealed override IAsyncResult BeginRepublish(RequestHeader? requestHeader,
            uint subscriptionId, uint retransmitSequenceNumber, AsyncCallback callback,
            object asyncState)
        {
            throw NotSupported(nameof(BeginRepublish));
        }

        /// <inheritdoc/>
        public sealed override ResponseHeader? EndRepublish(IAsyncResult result,
            out NotificationMessage? notificationMessage)
        {
            throw NotSupported(nameof(EndRepublish));
        }

        /// <inheritdoc/>
        public sealed override ResponseHeader? TransferSubscriptions(
            RequestHeader? requestHeader,
            ArrayOf<uint> subscriptionIds, bool sendInitialValues,
            out ArrayOf<TransferResult> results, out ArrayOf<DiagnosticInfo> diagnosticInfos)
        {
            throw NotSupported(nameof(TransferSubscriptions));
        }

        /// <inheritdoc/>
        public sealed override IAsyncResult BeginTransferSubscriptions(
            RequestHeader? requestHeader,
            ArrayOf<uint> subscriptionIds, bool sendInitialValues, AsyncCallback callback,
            object asyncState)
        {
            throw NotSupported(nameof(BeginTransferSubscriptions));
        }

        /// <inheritdoc/>
        public sealed override ResponseHeader? EndTransferSubscriptions(IAsyncResult result,
            out ArrayOf<TransferResult> results, out ArrayOf<DiagnosticInfo> diagnosticInfos)
        {
            throw NotSupported(nameof(EndTransferSubscriptions));
        }

        /// <inheritdoc/>
        public sealed override ResponseHeader? DeleteSubscriptions(RequestHeader? requestHeader,
            ArrayOf<uint> subscriptionIds, out ArrayOf<StatusCode> results,
            out ArrayOf<DiagnosticInfo> diagnosticInfos)
        {
            throw NotSupported(nameof(DeleteSubscriptions));
        }

        /// <inheritdoc/>
        public sealed override IAsyncResult BeginDeleteSubscriptions(
            RequestHeader? requestHeader,
            ArrayOf<uint> subscriptionIds, AsyncCallback callback, object asyncState)
        {
            throw NotSupported(nameof(BeginDeleteSubscriptions));
        }

        /// <inheritdoc/>
        public sealed override ResponseHeader? EndDeleteSubscriptions(IAsyncResult result,
            out ArrayOf<StatusCode> results, out ArrayOf<DiagnosticInfo> diagnosticInfos)
        {
            throw NotSupported(nameof(EndDeleteSubscriptions));
        }

        /// <inheritdoc/>
        protected sealed override void UpdateRequestHeader(IServiceRequest request,
            bool useDefaults)
        {
            throw NotSupported(nameof(UpdateRequestHeader));
        }

        /// <summary>
        /// Throw not supported exception
        /// </summary>
        /// <param name="name"></param>
        /// <returns></returns>
        private static ServiceResultException NotSupported(string name)
        {
#if DEBUG_OBSOLETE
            System.Diagnostics.Debug.Fail(name + " not supported");
#endif
            return ServiceResultException.Create(StatusCodes.BadNotSupported,
                name + " deprecated");
        }

        /// <summary>
        /// Null telemetry context used as placeholder in the obsolete base.
        /// </summary>
        private sealed class NullTelemetry : Opc.Ua.ITelemetryContext
        {
            public static readonly NullTelemetry Instance = new();
            public ILoggerFactory LoggerFactory { get; } = NullLoggerFactory.Instance;
            public ActivitySource ActivitySource { get; } = new("Opc.Ua.Client.Obsolete");
            public Meter CreateMeter() => new("Opc.Ua.Client.Obsolete");
        }

        /// <summary>
        /// Null transport channel used as placeholder that is immediately detached.
        /// </summary>
        private sealed class NullTransportChannel : ITransportChannel
        {
            public TransportChannelFeatures SupportedFeatures
                => TransportChannelFeatures.None;

            public EndpointDescription EndpointDescription
                => throw new ServiceResultException(
                    StatusCodes.BadNotSupported, "NullTransportChannel");

            public EndpointConfiguration EndpointConfiguration
                => throw new ServiceResultException(
                    StatusCodes.BadNotSupported, "NullTransportChannel");

            public byte[] ChannelThumbprint
                => throw new ServiceResultException(
                    StatusCodes.BadNotSupported, "NullTransportChannel");

            public byte[] ClientChannelCertificate
                => throw new ServiceResultException(
                    StatusCodes.BadNotSupported, "NullTransportChannel");

            public byte[] ServerChannelCertificate
                => throw new ServiceResultException(
                    StatusCodes.BadNotSupported, "NullTransportChannel");

            public IServiceMessageContext MessageContext
                => throw new ServiceResultException(
                    StatusCodes.BadNotSupported, "NullTransportChannel");

            public int OperationTimeout { get; set; }

            public ValueTask ReconnectAsync(
                ITransportWaitingConnection? connection = null,
                CancellationToken ct = default)
                => throw new ServiceResultException(
                    StatusCodes.BadNotSupported, "NullTransportChannel");

            public ValueTask<IServiceResponse> SendRequestAsync(
                IServiceRequest request, CancellationToken ct = default)
                => throw new ServiceResultException(
                    StatusCodes.BadNotSupported, "NullTransportChannel");

            public ValueTask CloseAsync(CancellationToken ct = default)
                => default;

            public void Dispose() { }
        }
    }
}
#endif
