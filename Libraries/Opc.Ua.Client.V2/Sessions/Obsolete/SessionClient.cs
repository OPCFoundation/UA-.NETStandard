// ------------------------------------------------------------
//  Copyright (c) Microsoft.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace Opc.Ua.Client.Sessions.Obsolete
{
    using Opc.Ua;
    using Opc.Ua.Client.Obsolete;
    using System;

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
            : base(channel ?? new NullChannel()) => DetachChannel();

        /// <inheritdoc/>
        public sealed override StatusCode Close()
        {
            throw NotSupported(nameof(Close));
        }

        /// <inheritdoc/>
        public sealed override ResponseHeader CreateSession(RequestHeader? requestHeader,
            ApplicationDescription clientDescription, string serverUri, string endpointUrl,
            string sessionName, byte[] clientNonce, byte[] clientCertificate,
            double requestedSessionTimeout, uint maxResponseMessageSize, out NodeId sessionId,
            out NodeId authenticationToken, out double revisedSessionTimeout, out byte[] serverNonce,
            out byte[] serverCertificate, out EndpointDescriptionCollection serverEndpoints,
            out SignedSoftwareCertificateCollection serverSoftwareCertificates,
            out SignatureData serverSignature, out uint maxRequestMessageSize)
        {
            throw NotSupported(nameof(CreateSession));
        }

        /// <inheritdoc/>
        public sealed override IAsyncResult BeginCreateSession(RequestHeader? requestHeader,
            ApplicationDescription clientDescription, string serverUri, string endpointUrl,
            string sessionName, byte[] clientNonce, byte[] clientCertificate, double requestedSessionTimeout,
            uint maxResponseMessageSize, AsyncCallback callback, object asyncState)
        {
            throw NotSupported(nameof(BeginCreateSession));
        }

        /// <inheritdoc/>
        public sealed override ResponseHeader EndCreateSession(IAsyncResult result,
            out NodeId sessionId, out NodeId authenticationToken,
            out double revisedSessionTimeout, out byte[] serverNonce,
            out byte[] serverCertificate, out EndpointDescriptionCollection serverEndpoints,
            out SignedSoftwareCertificateCollection serverSoftwareCertificates,
            out SignatureData serverSignature,
            out uint maxRequestMessageSize)
        {
            throw NotSupported(nameof(EndCreateSession));
        }

        /// <inheritdoc/>
        public sealed override ResponseHeader ActivateSession(
            RequestHeader? requestHeader, SignatureData clientSignature,
            SignedSoftwareCertificateCollection clientSoftwareCertificates,
            StringCollection localeIds, ExtensionObject userIdentityToken,
            SignatureData userTokenSignature, out byte[] serverNonce,
            out StatusCodeCollection results, out DiagnosticInfoCollection diagnosticInfos)
        {
            throw NotSupported(nameof(ActivateSession));
        }

        /// <inheritdoc/>
        public sealed override IAsyncResult BeginActivateSession(RequestHeader? requestHeader,
            SignatureData clientSignature, SignedSoftwareCertificateCollection? clientSoftwareCertificates,
            StringCollection localeIds, ExtensionObject userIdentityToken, SignatureData userTokenSignature,
            AsyncCallback? callback, object? asyncState)
        {
            throw NotSupported(nameof(BeginActivateSession));
        }

        /// <inheritdoc/>
        public sealed override ResponseHeader EndActivateSession(IAsyncResult result, out byte[] serverNonce,
            out StatusCodeCollection results, out DiagnosticInfoCollection diagnosticInfos)
        {
            throw NotSupported(nameof(EndActivateSession));
        }

        /// <inheritdoc/>
        public sealed override ResponseHeader CloseSession(RequestHeader? requestHeader, bool deleteSubscriptions)
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
        public sealed override ResponseHeader EndCloseSession(IAsyncResult result)
        {
            throw NotSupported(nameof(EndCloseSession));
        }

        /// <inheritdoc/>
        public sealed override ResponseHeader Cancel(RequestHeader? requestHeader,
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
        public sealed override ResponseHeader EndCancel(IAsyncResult result, out uint cancelCount)
        {
            throw NotSupported(nameof(EndCancel));
        }

        /// <inheritdoc/>
        public sealed override ResponseHeader AddNodes(RequestHeader? requestHeader,
            AddNodesItemCollection nodesToAdd, out AddNodesResultCollection results,
            out DiagnosticInfoCollection diagnosticInfos)
        {
            throw NotSupported(nameof(AddNodes));
        }

        /// <inheritdoc/>
        public sealed override IAsyncResult BeginAddNodes(RequestHeader? requestHeader,
            AddNodesItemCollection nodesToAdd, AsyncCallback callback, object asyncState)
        {
            throw NotSupported(nameof(BeginAddNodes));
        }

        /// <inheritdoc/>
        public sealed override ResponseHeader EndAddNodes(IAsyncResult result, out AddNodesResultCollection results,
            out DiagnosticInfoCollection diagnosticInfos)
        {
            throw NotSupported(nameof(EndAddNodes));
        }

        /// <inheritdoc/>
        public sealed override ResponseHeader AddReferences(RequestHeader? requestHeader,
            AddReferencesItemCollection referencesToAdd, out StatusCodeCollection results,
            out DiagnosticInfoCollection diagnosticInfos)
        {
            throw NotSupported(nameof(AddReferences));
        }

        /// <inheritdoc/>
        public sealed override IAsyncResult BeginAddReferences(RequestHeader? requestHeader,
            AddReferencesItemCollection referencesToAdd, AsyncCallback callback, object asyncState)
        {
            throw NotSupported(nameof(BeginAddReferences));
        }

        /// <inheritdoc/>
        public sealed override ResponseHeader EndAddReferences(IAsyncResult result,
            out StatusCodeCollection results, out DiagnosticInfoCollection diagnosticInfos)
        {
            throw NotSupported(nameof(EndAddReferences));
        }

        /// <inheritdoc/>
        public sealed override ResponseHeader DeleteNodes(RequestHeader? requestHeader,
            DeleteNodesItemCollection nodesToDelete, out StatusCodeCollection results,
            out DiagnosticInfoCollection diagnosticInfos)
        {
            throw NotSupported(nameof(DeleteNodes));
        }

        /// <inheritdoc/>
        public sealed override IAsyncResult BeginDeleteNodes(RequestHeader? requestHeader,
            DeleteNodesItemCollection nodesToDelete, AsyncCallback callback, object asyncState)
        {
            throw NotSupported(nameof(BeginDeleteNodes));
        }

        /// <inheritdoc/>
        public sealed override ResponseHeader EndDeleteNodes(IAsyncResult result,
            out StatusCodeCollection results, out DiagnosticInfoCollection diagnosticInfos)
        {
            throw NotSupported(nameof(EndDeleteNodes));
        }

        /// <inheritdoc/>
        public sealed override ResponseHeader DeleteReferences(RequestHeader? requestHeader,
            DeleteReferencesItemCollection referencesToDelete, out StatusCodeCollection results,
            out DiagnosticInfoCollection diagnosticInfos)
        {
            throw NotSupported(nameof(DeleteReferences));
        }

        /// <inheritdoc/>
        public sealed override IAsyncResult BeginDeleteReferences(RequestHeader? requestHeader,
            DeleteReferencesItemCollection referencesToDelete, AsyncCallback callback,
            object asyncState)
        {
            throw NotSupported(nameof(BeginDeleteReferences));
        }

        /// <inheritdoc/>
        public sealed override ResponseHeader EndDeleteReferences(IAsyncResult result,
            out StatusCodeCollection results, out DiagnosticInfoCollection diagnosticInfos)
        {
            throw NotSupported(nameof(EndDeleteReferences));
        }

        /// <inheritdoc/>
        public sealed override ResponseHeader Browse(RequestHeader? requestHeader, ViewDescription view,
            uint requestedMaxReferencesPerNode, BrowseDescriptionCollection nodesToBrowse,
            out BrowseResultCollection results, out DiagnosticInfoCollection diagnosticInfos)
        {
            throw NotSupported(nameof(Browse));
        }

        /// <inheritdoc/>
        public sealed override IAsyncResult BeginBrowse(RequestHeader? requestHeader,
            ViewDescription view, uint requestedMaxReferencesPerNode,
            BrowseDescriptionCollection nodesToBrowse, AsyncCallback callback, object asyncState)
        {
            throw NotSupported(nameof(BeginBrowse));
        }

        /// <inheritdoc/>
        public sealed override ResponseHeader EndBrowse(IAsyncResult result,
            out BrowseResultCollection results, out DiagnosticInfoCollection diagnosticInfos)
        {
            throw NotSupported(nameof(EndBrowse));
        }

        /// <inheritdoc/>
        public sealed override ResponseHeader BrowseNext(RequestHeader? requestHeader,
            bool releaseContinuationPoints, ByteStringCollection continuationPoints,
            out BrowseResultCollection results, out DiagnosticInfoCollection diagnosticInfos)
        {
            throw NotSupported(nameof(BrowseNext));
        }

        /// <inheritdoc/>
        public sealed override IAsyncResult BeginBrowseNext(RequestHeader? requestHeader,
            bool releaseContinuationPoints, ByteStringCollection continuationPoints,
            AsyncCallback callback, object asyncState)
        {
            throw NotSupported(nameof(BeginBrowseNext));
        }

        /// <inheritdoc/>
        public sealed override ResponseHeader EndBrowseNext(IAsyncResult result,
            out BrowseResultCollection results, out DiagnosticInfoCollection diagnosticInfos)
        {
            throw NotSupported(nameof(EndBrowseNext));
        }

        /// <inheritdoc/>
        public sealed override ResponseHeader TranslateBrowsePathsToNodeIds(RequestHeader? requestHeader,
            BrowsePathCollection browsePaths, out BrowsePathResultCollection results,
            out DiagnosticInfoCollection diagnosticInfos)
        {
            throw NotSupported(nameof(TranslateBrowsePathsToNodeIds));
        }

        /// <inheritdoc/>
        public sealed override IAsyncResult BeginTranslateBrowsePathsToNodeIds(RequestHeader? requestHeader,
            BrowsePathCollection browsePaths, AsyncCallback callback, object asyncState)
        {
            throw NotSupported(nameof(BeginTranslateBrowsePathsToNodeIds));
        }

        /// <inheritdoc/>
        public sealed override ResponseHeader EndTranslateBrowsePathsToNodeIds(IAsyncResult result,
            out BrowsePathResultCollection results, out DiagnosticInfoCollection diagnosticInfos)
        {
            throw NotSupported(nameof(EndTranslateBrowsePathsToNodeIds));
        }

        /// <inheritdoc/>
        public sealed override ResponseHeader RegisterNodes(RequestHeader? requestHeader,
            NodeIdCollection nodesToRegister, out NodeIdCollection registeredNodeIds)
        {
            throw NotSupported(nameof(RegisterNodes));
        }

        /// <inheritdoc/>
        public sealed override IAsyncResult BeginRegisterNodes(RequestHeader? requestHeader,
            NodeIdCollection nodesToRegister, AsyncCallback callback, object asyncState)
        {
            throw NotSupported(nameof(BeginRegisterNodes));
        }

        /// <inheritdoc/>
        public sealed override ResponseHeader EndRegisterNodes(IAsyncResult result,
            out NodeIdCollection registeredNodeIds)
        {
            throw NotSupported(nameof(EndRegisterNodes));
        }

        /// <inheritdoc/>
        public sealed override ResponseHeader UnregisterNodes(RequestHeader? requestHeader,
            NodeIdCollection nodesToUnregister)
        {
            throw NotSupported(nameof(UnregisterNodes));
        }

        /// <inheritdoc/>
        public sealed override IAsyncResult BeginUnregisterNodes(RequestHeader? requestHeader,
            NodeIdCollection nodesToUnregister, AsyncCallback callback, object asyncState)
        {
            throw NotSupported(nameof(BeginUnregisterNodes));
        }

        /// <inheritdoc/>
        public sealed override ResponseHeader EndUnregisterNodes(IAsyncResult result)
        {
            throw NotSupported(nameof(EndUnregisterNodes));
        }

        /// <inheritdoc/>
        public sealed override ResponseHeader QueryFirst(RequestHeader? requestHeader,
            ViewDescription view, NodeTypeDescriptionCollection nodeTypes,
            ContentFilter filter, uint maxDataSetsToReturn, uint maxReferencesToReturn,
            out QueryDataSetCollection queryDataSets, out byte[] continuationPoint,
            out ParsingResultCollection parsingResults, out DiagnosticInfoCollection diagnosticInfos,
            out ContentFilterResult filterResult)
        {
            throw NotSupported(nameof(QueryFirst));
        }

        /// <inheritdoc/>
        public sealed override IAsyncResult BeginQueryFirst(RequestHeader? requestHeader,
            ViewDescription view, NodeTypeDescriptionCollection nodeTypes, ContentFilter filter,
            uint maxDataSetsToReturn, uint maxReferencesToReturn, AsyncCallback callback,
            object asyncState)
        {
            throw NotSupported(nameof(BeginQueryFirst));
        }

        /// <inheritdoc/>
        public sealed override ResponseHeader EndQueryFirst(IAsyncResult result,
            out QueryDataSetCollection queryDataSets, out byte[] continuationPoint,
            out ParsingResultCollection parsingResults, out DiagnosticInfoCollection diagnosticInfos,
            out ContentFilterResult filterResult)
        {
            throw NotSupported(nameof(EndQueryFirst));
        }

        /// <inheritdoc/>
        public sealed override ResponseHeader QueryNext(RequestHeader? requestHeader,
            bool releaseContinuationPoint, byte[] continuationPoint,
            out QueryDataSetCollection queryDataSets, out byte[] revisedContinuationPoint)
        {
            throw NotSupported(nameof(QueryNext));
        }

        /// <inheritdoc/>
        public sealed override IAsyncResult BeginQueryNext(RequestHeader? requestHeader,
            bool releaseContinuationPoint, byte[] continuationPoint, AsyncCallback callback,
            object asyncState)
        {
            throw NotSupported(nameof(BeginQueryNext));
        }

        /// <inheritdoc/>
        public sealed override ResponseHeader EndQueryNext(IAsyncResult result,
            out QueryDataSetCollection queryDataSets, out byte[] revisedContinuationPoint)
        {
            throw NotSupported(nameof(EndQueryNext));
        }

        /// <inheritdoc/>
        public sealed override ResponseHeader HistoryRead(RequestHeader? requestHeader,
            ExtensionObject historyReadDetails, TimestampsToReturn timestampsToReturn,
            bool releaseContinuationPoints, HistoryReadValueIdCollection nodesToRead,
            out HistoryReadResultCollection results, out DiagnosticInfoCollection diagnosticInfos)
        {
            throw NotSupported(nameof(HistoryRead));
        }

        /// <inheritdoc/>
        public sealed override IAsyncResult BeginHistoryRead(RequestHeader? requestHeader,
            ExtensionObject historyReadDetails, TimestampsToReturn timestampsToReturn,
            bool releaseContinuationPoints, HistoryReadValueIdCollection nodesToRead,
            AsyncCallback callback, object asyncState)
        {
            throw NotSupported(nameof(BeginHistoryRead));
        }

        /// <inheritdoc/>
        public sealed override ResponseHeader EndHistoryRead(IAsyncResult result,
            out HistoryReadResultCollection results, out DiagnosticInfoCollection diagnosticInfos)
        {
            throw NotSupported(nameof(EndHistoryRead));
        }

        /// <inheritdoc/>
        public sealed override ResponseHeader Write(RequestHeader? requestHeader,
            WriteValueCollection nodesToWrite, out StatusCodeCollection results,
            out DiagnosticInfoCollection diagnosticInfos)
        {
            throw NotSupported(nameof(Write));
        }

        /// <inheritdoc/>
        public sealed override IAsyncResult BeginWrite(RequestHeader? requestHeader,
            WriteValueCollection nodesToWrite, AsyncCallback callback, object asyncState)
        {
            throw NotSupported(nameof(BeginWrite));
        }

        /// <inheritdoc/>
        public sealed override ResponseHeader EndWrite(IAsyncResult result,
            out StatusCodeCollection results, out DiagnosticInfoCollection diagnosticInfos)
        {
            throw NotSupported(nameof(EndWrite));
        }

        /// <inheritdoc/>
        public sealed override ResponseHeader HistoryUpdate(RequestHeader? requestHeader,
            ExtensionObjectCollection historyUpdateDetails, out HistoryUpdateResultCollection results,
            out DiagnosticInfoCollection diagnosticInfos)
        {
            throw NotSupported(nameof(HistoryUpdate));
        }

        /// <inheritdoc/>
        public sealed override IAsyncResult BeginHistoryUpdate(RequestHeader? requestHeader,
            ExtensionObjectCollection historyUpdateDetails, AsyncCallback callback, object asyncState)
        {
            throw NotSupported(nameof(BeginHistoryUpdate));
        }

        /// <inheritdoc/>
        public sealed override ResponseHeader EndHistoryUpdate(IAsyncResult result,
            out HistoryUpdateResultCollection results, out DiagnosticInfoCollection diagnosticInfos)
        {
            throw NotSupported(nameof(EndHistoryUpdate));
        }

        /// <inheritdoc/>
        public sealed override ResponseHeader Call(RequestHeader? requestHeader,
            CallMethodRequestCollection methodsToCall, out CallMethodResultCollection results,
            out DiagnosticInfoCollection diagnosticInfos)
        {
            throw NotSupported(nameof(Call));
        }

        /// <inheritdoc/>
        public sealed override IAsyncResult BeginCall(RequestHeader? requestHeader,
            CallMethodRequestCollection methodsToCall, AsyncCallback callback, object asyncState)
        {
            throw NotSupported(nameof(BeginCall));
        }

        /// <inheritdoc/>
        public sealed override ResponseHeader EndCall(IAsyncResult result,
            out CallMethodResultCollection results, out DiagnosticInfoCollection diagnosticInfos)
        {
            throw NotSupported(nameof(EndCall));
        }

        /// <inheritdoc/>
        public sealed override ResponseHeader CreateMonitoredItems(RequestHeader? requestHeader,
            uint subscriptionId, TimestampsToReturn timestampsToReturn,
            MonitoredItemCreateRequestCollection itemsToCreate,
            out MonitoredItemCreateResultCollection results,
            out DiagnosticInfoCollection diagnosticInfos)
        {
            throw NotSupported(nameof(CreateMonitoredItems));
        }

        /// <inheritdoc/>
        public sealed override IAsyncResult BeginCreateMonitoredItems(RequestHeader? requestHeader,
            uint subscriptionId, TimestampsToReturn timestampsToReturn,
            MonitoredItemCreateRequestCollection itemsToCreate, AsyncCallback callback,
            object asyncState)
        {
            throw NotSupported(nameof(BeginCreateMonitoredItems));
        }

        /// <inheritdoc/>
        public sealed override ResponseHeader EndCreateMonitoredItems(IAsyncResult result,
            out MonitoredItemCreateResultCollection results, out DiagnosticInfoCollection diagnosticInfos)
        {
            throw NotSupported(nameof(EndCreateMonitoredItems));
        }

        /// <inheritdoc/>
        public sealed override ResponseHeader ModifyMonitoredItems(RequestHeader? requestHeader,
            uint subscriptionId, TimestampsToReturn timestampsToReturn,
            MonitoredItemModifyRequestCollection itemsToModify,
            out MonitoredItemModifyResultCollection results, out DiagnosticInfoCollection diagnosticInfos)
        {
            throw NotSupported(nameof(ModifyMonitoredItems));
        }

        /// <inheritdoc/>
        public sealed override IAsyncResult BeginModifyMonitoredItems(RequestHeader? requestHeader,
            uint subscriptionId, TimestampsToReturn timestampsToReturn,
            MonitoredItemModifyRequestCollection itemsToModify, AsyncCallback callback,
            object asyncState)
        {
            throw NotSupported(nameof(BeginModifyMonitoredItems));
        }

        /// <inheritdoc/>
        public sealed override ResponseHeader EndModifyMonitoredItems(IAsyncResult result,
            out MonitoredItemModifyResultCollection results,
            out DiagnosticInfoCollection diagnosticInfos)
        {
            throw NotSupported(nameof(EndModifyMonitoredItems));
        }

        /// <inheritdoc/>
        public sealed override ResponseHeader SetMonitoringMode(RequestHeader? requestHeader,
            uint subscriptionId, MonitoringMode monitoringMode, UInt32Collection monitoredItemIds,
            out StatusCodeCollection results, out DiagnosticInfoCollection diagnosticInfos)
        {
            throw NotSupported(nameof(SetMonitoringMode));
        }

        /// <inheritdoc/>
        public sealed override IAsyncResult BeginSetMonitoringMode(RequestHeader? requestHeader,
            uint subscriptionId, MonitoringMode monitoringMode, UInt32Collection monitoredItemIds,
            AsyncCallback callback, object asyncState)
        {
            throw NotSupported(nameof(BeginSetMonitoringMode));
        }

        /// <inheritdoc/>
        public sealed override ResponseHeader EndSetMonitoringMode(IAsyncResult result,
            out StatusCodeCollection results, out DiagnosticInfoCollection diagnosticInfos)
        {
            throw NotSupported(nameof(EndSetMonitoringMode));
        }

        /// <inheritdoc/>
        public sealed override ResponseHeader SetTriggering(RequestHeader? requestHeader,
            uint subscriptionId, uint triggeringItemId, UInt32Collection linksToAdd,
            UInt32Collection linksToRemove, out StatusCodeCollection addResults,
            out DiagnosticInfoCollection addDiagnosticInfos, out StatusCodeCollection removeResults,
            out DiagnosticInfoCollection removeDiagnosticInfos)
        {
            throw NotSupported(nameof(SetTriggering));
        }

        /// <inheritdoc/>
        public sealed override IAsyncResult BeginSetTriggering(RequestHeader? requestHeader,
            uint subscriptionId, uint triggeringItemId, UInt32Collection linksToAdd,
            UInt32Collection linksToRemove, AsyncCallback callback, object asyncState)
        {
            throw NotSupported(nameof(BeginSetTriggering));
        }

        /// <inheritdoc/>
        public sealed override ResponseHeader EndSetTriggering(IAsyncResult result,
            out StatusCodeCollection addResults, out DiagnosticInfoCollection addDiagnosticInfos,
            out StatusCodeCollection removeResults, out DiagnosticInfoCollection removeDiagnosticInfos)
        {
            throw NotSupported(nameof(EndSetTriggering));
        }

        /// <inheritdoc/>
        public sealed override ResponseHeader DeleteMonitoredItems(RequestHeader? requestHeader,
            uint subscriptionId, UInt32Collection monitoredItemIds, out StatusCodeCollection results,
            out DiagnosticInfoCollection diagnosticInfos)
        {
            throw NotSupported(nameof(DeleteMonitoredItems));
        }

        /// <inheritdoc/>
        public sealed override IAsyncResult BeginDeleteMonitoredItems(RequestHeader? requestHeader,
            uint subscriptionId, UInt32Collection monitoredItemIds, AsyncCallback callback,
            object asyncState)
        {
            throw NotSupported(nameof(BeginDeleteMonitoredItems));
        }

        /// <inheritdoc/>
        public sealed override ResponseHeader EndDeleteMonitoredItems(IAsyncResult result, out
            StatusCodeCollection results, out DiagnosticInfoCollection diagnosticInfos)
        {
            throw NotSupported(nameof(EndDeleteMonitoredItems));
        }

        /// <inheritdoc/>
        public sealed override ResponseHeader CreateSubscription(RequestHeader? requestHeader,
            double requestedPublishingInterval, uint requestedLifetimeCount,
            uint requestedMaxKeepAliveCount, uint maxNotificationsPerPublish,
            bool publishingEnabled, byte priority, out uint subscriptionId,
            out double revisedPublishingInterval, out uint revisedLifetimeCount,
            out uint revisedMaxKeepAliveCount)
        {
            throw NotSupported(nameof(CreateSubscription));
        }

        /// <inheritdoc/>
        public sealed override IAsyncResult BeginCreateSubscription(RequestHeader? requestHeader,
            double requestedPublishingInterval, uint requestedLifetimeCount,
            uint requestedMaxKeepAliveCount, uint maxNotificationsPerPublish,
            bool publishingEnabled, byte priority, AsyncCallback callback, object asyncState)
        {
            throw NotSupported(nameof(BeginCreateSubscription));
        }

        /// <inheritdoc/>
        public sealed override ResponseHeader EndCreateSubscription(IAsyncResult result,
            out uint subscriptionId, out double revisedPublishingInterval,
            out uint revisedLifetimeCount, out uint revisedMaxKeepAliveCount)
        {
            throw NotSupported(nameof(EndCreateSubscription));
        }

        /// <inheritdoc/>
        public sealed override ResponseHeader ModifySubscription(RequestHeader? requestHeader,
            uint subscriptionId, double requestedPublishingInterval, uint requestedLifetimeCount,
            uint requestedMaxKeepAliveCount, uint maxNotificationsPerPublish, byte priority,
            out double revisedPublishingInterval, out uint revisedLifetimeCount,
            out uint revisedMaxKeepAliveCount)
        {
            throw NotSupported(nameof(ModifySubscription));
        }

        /// <inheritdoc/>
        public sealed override IAsyncResult BeginModifySubscription(RequestHeader? requestHeader,
            uint subscriptionId, double requestedPublishingInterval, uint requestedLifetimeCount,
            uint requestedMaxKeepAliveCount, uint maxNotificationsPerPublish, byte priority,
            AsyncCallback callback, object asyncState)
        {
            throw NotSupported(nameof(BeginModifySubscription));
        }

        /// <inheritdoc/>
        public sealed override ResponseHeader EndModifySubscription(IAsyncResult result,
            out double revisedPublishingInterval, out uint revisedLifetimeCount,
            out uint revisedMaxKeepAliveCount)
        {
            throw NotSupported(nameof(EndModifySubscription));
        }

        /// <inheritdoc/>
        public sealed override ResponseHeader SetPublishingMode(RequestHeader? requestHeader,
            bool publishingEnabled, UInt32Collection subscriptionIds, out StatusCodeCollection results,
            out DiagnosticInfoCollection diagnosticInfos)
        {
            throw NotSupported(nameof(SetPublishingMode));
        }

        /// <inheritdoc/>
        public sealed override IAsyncResult BeginSetPublishingMode(RequestHeader? requestHeader,
            bool publishingEnabled, UInt32Collection subscriptionIds, AsyncCallback callback,
            object asyncState)
        {
            throw NotSupported(nameof(BeginSetPublishingMode));
        }

        /// <inheritdoc/>
        public sealed override ResponseHeader EndSetPublishingMode(IAsyncResult result,
            out StatusCodeCollection results, out DiagnosticInfoCollection diagnosticInfos)
        {
            throw NotSupported(nameof(EndSetPublishingMode));
        }

        /// <inheritdoc/>
        public sealed override ResponseHeader Publish(RequestHeader? requestHeader,
            SubscriptionAcknowledgementCollection subscriptionAcknowledgements,
            out uint subscriptionId, out UInt32Collection availableSequenceNumbers,
            out bool moreNotifications, out NotificationMessage notificationMessage,
            out StatusCodeCollection results, out DiagnosticInfoCollection diagnosticInfos)
        {
            throw NotSupported(nameof(Publish));
        }

        /// <inheritdoc/>
        public sealed override IAsyncResult BeginPublish(RequestHeader? requestHeader,
            SubscriptionAcknowledgementCollection subscriptionAcknowledgements,
            AsyncCallback callback, object asyncState)
        {
            throw NotSupported(nameof(BeginPublish));
        }

        /// <inheritdoc/>
        public sealed override ResponseHeader EndPublish(IAsyncResult result, out uint subscriptionId,
            out UInt32Collection availableSequenceNumbers, out bool moreNotifications,
            out NotificationMessage notificationMessage, out StatusCodeCollection results,
            out DiagnosticInfoCollection diagnosticInfos)
        {
            throw NotSupported(nameof(EndPublish));
        }

        /// <inheritdoc/>
        public sealed override IAsyncResult BeginRead(RequestHeader? requestHeader, double maxAge,
            TimestampsToReturn timestampsToReturn, ReadValueIdCollection nodesToRead,
            AsyncCallback callback, object asyncState)
        {
            throw NotSupported(nameof(BeginRead));
        }

        /// <inheritdoc/>
        public sealed override ResponseHeader EndRead(IAsyncResult result,
            out DataValueCollection results, out DiagnosticInfoCollection diagnosticInfos)
        {
            throw NotSupported(nameof(EndRead));
        }

        /// <inheritdoc/>
        public sealed override ResponseHeader Read(RequestHeader? requestHeader, double maxAge,
            TimestampsToReturn timestampsToReturn, ReadValueIdCollection nodesToRead,
            out DataValueCollection results, out DiagnosticInfoCollection diagnosticInfos)
        {
            throw NotSupported(nameof(Read));
        }

        /// <inheritdoc/>
        public sealed override ResponseHeader Republish(RequestHeader? requestHeader,
            uint subscriptionId, uint retransmitSequenceNumber,
            out NotificationMessage notificationMessage)
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
        public sealed override ResponseHeader EndRepublish(IAsyncResult result,
            out NotificationMessage notificationMessage)
        {
            throw NotSupported(nameof(EndRepublish));
        }

        /// <inheritdoc/>
        public sealed override ResponseHeader TransferSubscriptions(RequestHeader? requestHeader,
            UInt32Collection subscriptionIds, bool sendInitialValues,
            out TransferResultCollection results, out DiagnosticInfoCollection diagnosticInfos)
        {
            throw NotSupported(nameof(TransferSubscriptions));
        }

        /// <inheritdoc/>
        public sealed override IAsyncResult BeginTransferSubscriptions(RequestHeader? requestHeader,
            UInt32Collection subscriptionIds, bool sendInitialValues, AsyncCallback callback,
            object asyncState)
        {
            throw NotSupported(nameof(BeginTransferSubscriptions));
        }

        /// <inheritdoc/>
        public sealed override ResponseHeader EndTransferSubscriptions(IAsyncResult result,
            out TransferResultCollection results, out DiagnosticInfoCollection diagnosticInfos)
        {
            throw NotSupported(nameof(EndTransferSubscriptions));
        }

        /// <inheritdoc/>
        public sealed override ResponseHeader DeleteSubscriptions(RequestHeader? requestHeader,
            UInt32Collection subscriptionIds, out StatusCodeCollection results,
            out DiagnosticInfoCollection diagnosticInfos)
        {
            throw NotSupported(nameof(DeleteSubscriptions));
        }

        /// <inheritdoc/>
        public sealed override IAsyncResult BeginDeleteSubscriptions(RequestHeader? requestHeader,
            UInt32Collection subscriptionIds, AsyncCallback callback, object asyncState)
        {
            throw NotSupported(nameof(BeginDeleteSubscriptions));
        }

        /// <inheritdoc/>
        public sealed override ResponseHeader EndDeleteSubscriptions(IAsyncResult result,
            out StatusCodeCollection results, out DiagnosticInfoCollection diagnosticInfos)
        {
            throw NotSupported(nameof(EndDeleteSubscriptions));
        }

        /// <inheritdoc/>
        protected sealed override void UpdateRequestHeader(IServiceRequest request, bool useDefaults)
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
    }
}
