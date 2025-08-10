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

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua
{
    #region ISessionServer Interface
    /// <summary>
    /// An interface to a UA server implementation.
    /// </summary>
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public interface ISessionServer : IServerBase
    {
        #if (!OPCUA_EXCLUDE_FindServers)
        /// <summary>
        /// Invokes the FindServers service.
        /// </summary>
        ResponseHeader FindServers(
            RequestHeader                        requestHeader,
            string                               endpointUrl,
            StringCollection                     localeIds,
            StringCollection                     serverUris,
            out ApplicationDescriptionCollection servers);

        #if (!OPCUA_EXCLUDE_FindServers_ASYNC)
        /// <summary>
        /// Invokes the FindServers service using async Task based request.
        /// </summary>
        Task<FindServersResponse> FindServersAsync(
            RequestHeader     requestHeader,
            string            endpointUrl,
            StringCollection  localeIds,
            StringCollection  serverUris,
            CancellationToken ct);
        #endif
        #endif

        #if (!OPCUA_EXCLUDE_FindServersOnNetwork)
        /// <summary>
        /// Invokes the FindServersOnNetwork service.
        /// </summary>
        ResponseHeader FindServersOnNetwork(
            RequestHeader                 requestHeader,
            uint                          startingRecordId,
            uint                          maxRecordsToReturn,
            StringCollection              serverCapabilityFilter,
            out DateTime                  lastCounterResetTime,
            out ServerOnNetworkCollection servers);

        #if (!OPCUA_EXCLUDE_FindServersOnNetwork_ASYNC)
        /// <summary>
        /// Invokes the FindServersOnNetwork service using async Task based request.
        /// </summary>
        Task<FindServersOnNetworkResponse> FindServersOnNetworkAsync(
            RequestHeader     requestHeader,
            uint              startingRecordId,
            uint              maxRecordsToReturn,
            StringCollection  serverCapabilityFilter,
            CancellationToken ct);
        #endif
        #endif

        #if (!OPCUA_EXCLUDE_GetEndpoints)
        /// <summary>
        /// Invokes the GetEndpoints service.
        /// </summary>
        ResponseHeader GetEndpoints(
            RequestHeader                     requestHeader,
            string                            endpointUrl,
            StringCollection                  localeIds,
            StringCollection                  profileUris,
            out EndpointDescriptionCollection endpoints);

        #if (!OPCUA_EXCLUDE_GetEndpoints_ASYNC)
        /// <summary>
        /// Invokes the GetEndpoints service using async Task based request.
        /// </summary>
        Task<GetEndpointsResponse> GetEndpointsAsync(
            RequestHeader     requestHeader,
            string            endpointUrl,
            StringCollection  localeIds,
            StringCollection  profileUris,
            CancellationToken ct);
        #endif
        #endif

        #if (!OPCUA_EXCLUDE_CreateSession)
        /// <summary>
        /// Invokes the CreateSession service.
        /// </summary>
        ResponseHeader CreateSession(
            RequestHeader                           requestHeader,
            ApplicationDescription                  clientDescription,
            string                                  serverUri,
            string                                  endpointUrl,
            string                                  sessionName,
            byte[]                                  clientNonce,
            byte[]                                  clientCertificate,
            double                                  requestedSessionTimeout,
            uint                                    maxResponseMessageSize,
            out NodeId                              sessionId,
            out NodeId                              authenticationToken,
            out double                              revisedSessionTimeout,
            out byte[]                              serverNonce,
            out byte[]                              serverCertificate,
            out EndpointDescriptionCollection       serverEndpoints,
            out SignedSoftwareCertificateCollection serverSoftwareCertificates,
            out SignatureData                       serverSignature,
            out uint                                maxRequestMessageSize);

        #if (!OPCUA_EXCLUDE_CreateSession_ASYNC)
        /// <summary>
        /// Invokes the CreateSession service using async Task based request.
        /// </summary>
        Task<CreateSessionResponse> CreateSessionAsync(
            RequestHeader          requestHeader,
            ApplicationDescription clientDescription,
            string                 serverUri,
            string                 endpointUrl,
            string                 sessionName,
            byte[]                 clientNonce,
            byte[]                 clientCertificate,
            double                 requestedSessionTimeout,
            uint                   maxResponseMessageSize,
            CancellationToken      ct);
        #endif
        #endif

        #if (!OPCUA_EXCLUDE_ActivateSession)
        /// <summary>
        /// Invokes the ActivateSession service.
        /// </summary>
        ResponseHeader ActivateSession(
            RequestHeader                       requestHeader,
            SignatureData                       clientSignature,
            SignedSoftwareCertificateCollection clientSoftwareCertificates,
            StringCollection                    localeIds,
            ExtensionObject                     userIdentityToken,
            SignatureData                       userTokenSignature,
            out byte[]                          serverNonce,
            out StatusCodeCollection            results,
            out DiagnosticInfoCollection        diagnosticInfos);

        #if (!OPCUA_EXCLUDE_ActivateSession_ASYNC)
        /// <summary>
        /// Invokes the ActivateSession service using async Task based request.
        /// </summary>
        Task<ActivateSessionResponse> ActivateSessionAsync(
            RequestHeader                       requestHeader,
            SignatureData                       clientSignature,
            SignedSoftwareCertificateCollection clientSoftwareCertificates,
            StringCollection                    localeIds,
            ExtensionObject                     userIdentityToken,
            SignatureData                       userTokenSignature,
            CancellationToken                   ct);
        #endif
        #endif

        #if (!OPCUA_EXCLUDE_CloseSession)
        /// <summary>
        /// Invokes the CloseSession service.
        /// </summary>
        ResponseHeader CloseSession(
            RequestHeader requestHeader,
            bool          deleteSubscriptions);

        #if (!OPCUA_EXCLUDE_CloseSession_ASYNC)
        /// <summary>
        /// Invokes the CloseSession service using async Task based request.
        /// </summary>
        Task<CloseSessionResponse> CloseSessionAsync(
            RequestHeader     requestHeader,
            bool              deleteSubscriptions,
            CancellationToken ct);
        #endif
        #endif

        #if (!OPCUA_EXCLUDE_Cancel)
        /// <summary>
        /// Invokes the Cancel service.
        /// </summary>
        ResponseHeader Cancel(
            RequestHeader requestHeader,
            uint          requestHandle,
            out uint      cancelCount);

        #if (!OPCUA_EXCLUDE_Cancel_ASYNC)
        /// <summary>
        /// Invokes the Cancel service using async Task based request.
        /// </summary>
        Task<CancelResponse> CancelAsync(
            RequestHeader     requestHeader,
            uint              requestHandle,
            CancellationToken ct);
        #endif
        #endif

        #if (!OPCUA_EXCLUDE_AddNodes)
        /// <summary>
        /// Invokes the AddNodes service.
        /// </summary>
        ResponseHeader AddNodes(
            RequestHeader                requestHeader,
            AddNodesItemCollection       nodesToAdd,
            out AddNodesResultCollection results,
            out DiagnosticInfoCollection diagnosticInfos);

        #if (!OPCUA_EXCLUDE_AddNodes_ASYNC)
        /// <summary>
        /// Invokes the AddNodes service using async Task based request.
        /// </summary>
        Task<AddNodesResponse> AddNodesAsync(
            RequestHeader          requestHeader,
            AddNodesItemCollection nodesToAdd,
            CancellationToken      ct);
        #endif
        #endif

        #if (!OPCUA_EXCLUDE_AddReferences)
        /// <summary>
        /// Invokes the AddReferences service.
        /// </summary>
        ResponseHeader AddReferences(
            RequestHeader                requestHeader,
            AddReferencesItemCollection  referencesToAdd,
            out StatusCodeCollection     results,
            out DiagnosticInfoCollection diagnosticInfos);

        #if (!OPCUA_EXCLUDE_AddReferences_ASYNC)
        /// <summary>
        /// Invokes the AddReferences service using async Task based request.
        /// </summary>
        Task<AddReferencesResponse> AddReferencesAsync(
            RequestHeader               requestHeader,
            AddReferencesItemCollection referencesToAdd,
            CancellationToken           ct);
        #endif
        #endif

        #if (!OPCUA_EXCLUDE_DeleteNodes)
        /// <summary>
        /// Invokes the DeleteNodes service.
        /// </summary>
        ResponseHeader DeleteNodes(
            RequestHeader                requestHeader,
            DeleteNodesItemCollection    nodesToDelete,
            out StatusCodeCollection     results,
            out DiagnosticInfoCollection diagnosticInfos);

        #if (!OPCUA_EXCLUDE_DeleteNodes_ASYNC)
        /// <summary>
        /// Invokes the DeleteNodes service using async Task based request.
        /// </summary>
        Task<DeleteNodesResponse> DeleteNodesAsync(
            RequestHeader             requestHeader,
            DeleteNodesItemCollection nodesToDelete,
            CancellationToken         ct);
        #endif
        #endif

        #if (!OPCUA_EXCLUDE_DeleteReferences)
        /// <summary>
        /// Invokes the DeleteReferences service.
        /// </summary>
        ResponseHeader DeleteReferences(
            RequestHeader                  requestHeader,
            DeleteReferencesItemCollection referencesToDelete,
            out StatusCodeCollection       results,
            out DiagnosticInfoCollection   diagnosticInfos);

        #if (!OPCUA_EXCLUDE_DeleteReferences_ASYNC)
        /// <summary>
        /// Invokes the DeleteReferences service using async Task based request.
        /// </summary>
        Task<DeleteReferencesResponse> DeleteReferencesAsync(
            RequestHeader                  requestHeader,
            DeleteReferencesItemCollection referencesToDelete,
            CancellationToken              ct);
        #endif
        #endif

        #if (!OPCUA_EXCLUDE_Browse)
        /// <summary>
        /// Invokes the Browse service.
        /// </summary>
        ResponseHeader Browse(
            RequestHeader                requestHeader,
            ViewDescription              view,
            uint                         requestedMaxReferencesPerNode,
            BrowseDescriptionCollection  nodesToBrowse,
            out BrowseResultCollection   results,
            out DiagnosticInfoCollection diagnosticInfos);

        #if (!OPCUA_EXCLUDE_Browse_ASYNC)
        /// <summary>
        /// Invokes the Browse service using async Task based request.
        /// </summary>
        Task<BrowseResponse> BrowseAsync(
            RequestHeader               requestHeader,
            ViewDescription             view,
            uint                        requestedMaxReferencesPerNode,
            BrowseDescriptionCollection nodesToBrowse,
            CancellationToken           ct);
        #endif
        #endif

        #if (!OPCUA_EXCLUDE_BrowseNext)
        /// <summary>
        /// Invokes the BrowseNext service.
        /// </summary>
        ResponseHeader BrowseNext(
            RequestHeader                requestHeader,
            bool                         releaseContinuationPoints,
            ByteStringCollection         continuationPoints,
            out BrowseResultCollection   results,
            out DiagnosticInfoCollection diagnosticInfos);

        #if (!OPCUA_EXCLUDE_BrowseNext_ASYNC)
        /// <summary>
        /// Invokes the BrowseNext service using async Task based request.
        /// </summary>
        Task<BrowseNextResponse> BrowseNextAsync(
            RequestHeader        requestHeader,
            bool                 releaseContinuationPoints,
            ByteStringCollection continuationPoints,
            CancellationToken    ct);
        #endif
        #endif

        #if (!OPCUA_EXCLUDE_TranslateBrowsePathsToNodeIds)
        /// <summary>
        /// Invokes the TranslateBrowsePathsToNodeIds service.
        /// </summary>
        ResponseHeader TranslateBrowsePathsToNodeIds(
            RequestHeader                  requestHeader,
            BrowsePathCollection           browsePaths,
            out BrowsePathResultCollection results,
            out DiagnosticInfoCollection   diagnosticInfos);

        #if (!OPCUA_EXCLUDE_TranslateBrowsePathsToNodeIds_ASYNC)
        /// <summary>
        /// Invokes the TranslateBrowsePathsToNodeIds service using async Task based request.
        /// </summary>
        Task<TranslateBrowsePathsToNodeIdsResponse> TranslateBrowsePathsToNodeIdsAsync(
            RequestHeader        requestHeader,
            BrowsePathCollection browsePaths,
            CancellationToken    ct);
        #endif
        #endif

        #if (!OPCUA_EXCLUDE_RegisterNodes)
        /// <summary>
        /// Invokes the RegisterNodes service.
        /// </summary>
        ResponseHeader RegisterNodes(
            RequestHeader        requestHeader,
            NodeIdCollection     nodesToRegister,
            out NodeIdCollection registeredNodeIds);

        #if (!OPCUA_EXCLUDE_RegisterNodes_ASYNC)
        /// <summary>
        /// Invokes the RegisterNodes service using async Task based request.
        /// </summary>
        Task<RegisterNodesResponse> RegisterNodesAsync(
            RequestHeader     requestHeader,
            NodeIdCollection  nodesToRegister,
            CancellationToken ct);
        #endif
        #endif

        #if (!OPCUA_EXCLUDE_UnregisterNodes)
        /// <summary>
        /// Invokes the UnregisterNodes service.
        /// </summary>
        ResponseHeader UnregisterNodes(
            RequestHeader    requestHeader,
            NodeIdCollection nodesToUnregister);

        #if (!OPCUA_EXCLUDE_UnregisterNodes_ASYNC)
        /// <summary>
        /// Invokes the UnregisterNodes service using async Task based request.
        /// </summary>
        Task<UnregisterNodesResponse> UnregisterNodesAsync(
            RequestHeader     requestHeader,
            NodeIdCollection  nodesToUnregister,
            CancellationToken ct);
        #endif
        #endif

        #if (!OPCUA_EXCLUDE_QueryFirst)
        /// <summary>
        /// Invokes the QueryFirst service.
        /// </summary>
        ResponseHeader QueryFirst(
            RequestHeader                 requestHeader,
            ViewDescription               view,
            NodeTypeDescriptionCollection nodeTypes,
            ContentFilter                 filter,
            uint                          maxDataSetsToReturn,
            uint                          maxReferencesToReturn,
            out QueryDataSetCollection    queryDataSets,
            out byte[]                    continuationPoint,
            out ParsingResultCollection   parsingResults,
            out DiagnosticInfoCollection  diagnosticInfos,
            out ContentFilterResult       filterResult);

        #if (!OPCUA_EXCLUDE_QueryFirst_ASYNC)
        /// <summary>
        /// Invokes the QueryFirst service using async Task based request.
        /// </summary>
        Task<QueryFirstResponse> QueryFirstAsync(
            RequestHeader                 requestHeader,
            ViewDescription               view,
            NodeTypeDescriptionCollection nodeTypes,
            ContentFilter                 filter,
            uint                          maxDataSetsToReturn,
            uint                          maxReferencesToReturn,
            CancellationToken             ct);
        #endif
        #endif

        #if (!OPCUA_EXCLUDE_QueryNext)
        /// <summary>
        /// Invokes the QueryNext service.
        /// </summary>
        ResponseHeader QueryNext(
            RequestHeader              requestHeader,
            bool                       releaseContinuationPoint,
            byte[]                     continuationPoint,
            out QueryDataSetCollection queryDataSets,
            out byte[]                 revisedContinuationPoint);

        #if (!OPCUA_EXCLUDE_QueryNext_ASYNC)
        /// <summary>
        /// Invokes the QueryNext service using async Task based request.
        /// </summary>
        Task<QueryNextResponse> QueryNextAsync(
            RequestHeader     requestHeader,
            bool              releaseContinuationPoint,
            byte[]            continuationPoint,
            CancellationToken ct);
        #endif
        #endif

        #if (!OPCUA_EXCLUDE_Read)
        /// <summary>
        /// Invokes the Read service.
        /// </summary>
        ResponseHeader Read(
            RequestHeader                requestHeader,
            double                       maxAge,
            TimestampsToReturn           timestampsToReturn,
            ReadValueIdCollection        nodesToRead,
            out DataValueCollection      results,
            out DiagnosticInfoCollection diagnosticInfos);

        #if (!OPCUA_EXCLUDE_Read_ASYNC)
        /// <summary>
        /// Invokes the Read service using async Task based request.
        /// </summary>
        Task<ReadResponse> ReadAsync(
            RequestHeader         requestHeader,
            double                maxAge,
            TimestampsToReturn    timestampsToReturn,
            ReadValueIdCollection nodesToRead,
            CancellationToken     ct);
        #endif
        #endif

        #if (!OPCUA_EXCLUDE_HistoryRead)
        /// <summary>
        /// Invokes the HistoryRead service.
        /// </summary>
        ResponseHeader HistoryRead(
            RequestHeader                   requestHeader,
            ExtensionObject                 historyReadDetails,
            TimestampsToReturn              timestampsToReturn,
            bool                            releaseContinuationPoints,
            HistoryReadValueIdCollection    nodesToRead,
            out HistoryReadResultCollection results,
            out DiagnosticInfoCollection    diagnosticInfos);

        #if (!OPCUA_EXCLUDE_HistoryRead_ASYNC)
        /// <summary>
        /// Invokes the HistoryRead service using async Task based request.
        /// </summary>
        Task<HistoryReadResponse> HistoryReadAsync(
            RequestHeader                requestHeader,
            ExtensionObject              historyReadDetails,
            TimestampsToReturn           timestampsToReturn,
            bool                         releaseContinuationPoints,
            HistoryReadValueIdCollection nodesToRead,
            CancellationToken            ct);
        #endif
        #endif

        #if (!OPCUA_EXCLUDE_Write)
        /// <summary>
        /// Invokes the Write service.
        /// </summary>
        ResponseHeader Write(
            RequestHeader                requestHeader,
            WriteValueCollection         nodesToWrite,
            out StatusCodeCollection     results,
            out DiagnosticInfoCollection diagnosticInfos);

        #if (!OPCUA_EXCLUDE_Write_ASYNC)
        /// <summary>
        /// Invokes the Write service using async Task based request.
        /// </summary>
        Task<WriteResponse> WriteAsync(
            RequestHeader        requestHeader,
            WriteValueCollection nodesToWrite,
            CancellationToken    ct);
        #endif
        #endif

        #if (!OPCUA_EXCLUDE_HistoryUpdate)
        /// <summary>
        /// Invokes the HistoryUpdate service.
        /// </summary>
        ResponseHeader HistoryUpdate(
            RequestHeader                     requestHeader,
            ExtensionObjectCollection         historyUpdateDetails,
            out HistoryUpdateResultCollection results,
            out DiagnosticInfoCollection      diagnosticInfos);

        #if (!OPCUA_EXCLUDE_HistoryUpdate_ASYNC)
        /// <summary>
        /// Invokes the HistoryUpdate service using async Task based request.
        /// </summary>
        Task<HistoryUpdateResponse> HistoryUpdateAsync(
            RequestHeader             requestHeader,
            ExtensionObjectCollection historyUpdateDetails,
            CancellationToken         ct);
        #endif
        #endif

        #if (!OPCUA_EXCLUDE_Call)
        /// <summary>
        /// Invokes the Call service.
        /// </summary>
        ResponseHeader Call(
            RequestHeader                  requestHeader,
            CallMethodRequestCollection    methodsToCall,
            out CallMethodResultCollection results,
            out DiagnosticInfoCollection   diagnosticInfos);

        #if (!OPCUA_EXCLUDE_Call_ASYNC)
        /// <summary>
        /// Invokes the Call service using async Task based request.
        /// </summary>
        Task<CallResponse> CallAsync(
            RequestHeader               requestHeader,
            CallMethodRequestCollection methodsToCall,
            CancellationToken           ct);
        #endif
        #endif

        #if (!OPCUA_EXCLUDE_CreateMonitoredItems)
        /// <summary>
        /// Invokes the CreateMonitoredItems service.
        /// </summary>
        ResponseHeader CreateMonitoredItems(
            RequestHeader                           requestHeader,
            uint                                    subscriptionId,
            TimestampsToReturn                      timestampsToReturn,
            MonitoredItemCreateRequestCollection    itemsToCreate,
            out MonitoredItemCreateResultCollection results,
            out DiagnosticInfoCollection            diagnosticInfos);

        #if (!OPCUA_EXCLUDE_CreateMonitoredItems_ASYNC)
        /// <summary>
        /// Invokes the CreateMonitoredItems service using async Task based request.
        /// </summary>
        Task<CreateMonitoredItemsResponse> CreateMonitoredItemsAsync(
            RequestHeader                        requestHeader,
            uint                                 subscriptionId,
            TimestampsToReturn                   timestampsToReturn,
            MonitoredItemCreateRequestCollection itemsToCreate,
            CancellationToken                    ct);
        #endif
        #endif

        #if (!OPCUA_EXCLUDE_ModifyMonitoredItems)
        /// <summary>
        /// Invokes the ModifyMonitoredItems service.
        /// </summary>
        ResponseHeader ModifyMonitoredItems(
            RequestHeader                           requestHeader,
            uint                                    subscriptionId,
            TimestampsToReturn                      timestampsToReturn,
            MonitoredItemModifyRequestCollection    itemsToModify,
            out MonitoredItemModifyResultCollection results,
            out DiagnosticInfoCollection            diagnosticInfos);

        #if (!OPCUA_EXCLUDE_ModifyMonitoredItems_ASYNC)
        /// <summary>
        /// Invokes the ModifyMonitoredItems service using async Task based request.
        /// </summary>
        Task<ModifyMonitoredItemsResponse> ModifyMonitoredItemsAsync(
            RequestHeader                        requestHeader,
            uint                                 subscriptionId,
            TimestampsToReturn                   timestampsToReturn,
            MonitoredItemModifyRequestCollection itemsToModify,
            CancellationToken                    ct);
        #endif
        #endif

        #if (!OPCUA_EXCLUDE_SetMonitoringMode)
        /// <summary>
        /// Invokes the SetMonitoringMode service.
        /// </summary>
        ResponseHeader SetMonitoringMode(
            RequestHeader                requestHeader,
            uint                         subscriptionId,
            MonitoringMode               monitoringMode,
            UInt32Collection             monitoredItemIds,
            out StatusCodeCollection     results,
            out DiagnosticInfoCollection diagnosticInfos);

        #if (!OPCUA_EXCLUDE_SetMonitoringMode_ASYNC)
        /// <summary>
        /// Invokes the SetMonitoringMode service using async Task based request.
        /// </summary>
        Task<SetMonitoringModeResponse> SetMonitoringModeAsync(
            RequestHeader     requestHeader,
            uint              subscriptionId,
            MonitoringMode    monitoringMode,
            UInt32Collection  monitoredItemIds,
            CancellationToken ct);
        #endif
        #endif

        #if (!OPCUA_EXCLUDE_SetTriggering)
        /// <summary>
        /// Invokes the SetTriggering service.
        /// </summary>
        ResponseHeader SetTriggering(
            RequestHeader                requestHeader,
            uint                         subscriptionId,
            uint                         triggeringItemId,
            UInt32Collection             linksToAdd,
            UInt32Collection             linksToRemove,
            out StatusCodeCollection     addResults,
            out DiagnosticInfoCollection addDiagnosticInfos,
            out StatusCodeCollection     removeResults,
            out DiagnosticInfoCollection removeDiagnosticInfos);

        #if (!OPCUA_EXCLUDE_SetTriggering_ASYNC)
        /// <summary>
        /// Invokes the SetTriggering service using async Task based request.
        /// </summary>
        Task<SetTriggeringResponse> SetTriggeringAsync(
            RequestHeader     requestHeader,
            uint              subscriptionId,
            uint              triggeringItemId,
            UInt32Collection  linksToAdd,
            UInt32Collection  linksToRemove,
            CancellationToken ct);
        #endif
        #endif

        #if (!OPCUA_EXCLUDE_DeleteMonitoredItems)
        /// <summary>
        /// Invokes the DeleteMonitoredItems service.
        /// </summary>
        ResponseHeader DeleteMonitoredItems(
            RequestHeader                requestHeader,
            uint                         subscriptionId,
            UInt32Collection             monitoredItemIds,
            out StatusCodeCollection     results,
            out DiagnosticInfoCollection diagnosticInfos);

        #if (!OPCUA_EXCLUDE_DeleteMonitoredItems_ASYNC)
        /// <summary>
        /// Invokes the DeleteMonitoredItems service using async Task based request.
        /// </summary>
        Task<DeleteMonitoredItemsResponse> DeleteMonitoredItemsAsync(
            RequestHeader     requestHeader,
            uint              subscriptionId,
            UInt32Collection  monitoredItemIds,
            CancellationToken ct);
        #endif
        #endif

        #if (!OPCUA_EXCLUDE_CreateSubscription)
        /// <summary>
        /// Invokes the CreateSubscription service.
        /// </summary>
        ResponseHeader CreateSubscription(
            RequestHeader requestHeader,
            double        requestedPublishingInterval,
            uint          requestedLifetimeCount,
            uint          requestedMaxKeepAliveCount,
            uint          maxNotificationsPerPublish,
            bool          publishingEnabled,
            byte          priority,
            out uint      subscriptionId,
            out double    revisedPublishingInterval,
            out uint      revisedLifetimeCount,
            out uint      revisedMaxKeepAliveCount);

        #if (!OPCUA_EXCLUDE_CreateSubscription_ASYNC)
        /// <summary>
        /// Invokes the CreateSubscription service using async Task based request.
        /// </summary>
        Task<CreateSubscriptionResponse> CreateSubscriptionAsync(
            RequestHeader     requestHeader,
            double            requestedPublishingInterval,
            uint              requestedLifetimeCount,
            uint              requestedMaxKeepAliveCount,
            uint              maxNotificationsPerPublish,
            bool              publishingEnabled,
            byte              priority,
            CancellationToken ct);
        #endif
        #endif

        #if (!OPCUA_EXCLUDE_ModifySubscription)
        /// <summary>
        /// Invokes the ModifySubscription service.
        /// </summary>
        ResponseHeader ModifySubscription(
            RequestHeader requestHeader,
            uint          subscriptionId,
            double        requestedPublishingInterval,
            uint          requestedLifetimeCount,
            uint          requestedMaxKeepAliveCount,
            uint          maxNotificationsPerPublish,
            byte          priority,
            out double    revisedPublishingInterval,
            out uint      revisedLifetimeCount,
            out uint      revisedMaxKeepAliveCount);

        #if (!OPCUA_EXCLUDE_ModifySubscription_ASYNC)
        /// <summary>
        /// Invokes the ModifySubscription service using async Task based request.
        /// </summary>
        Task<ModifySubscriptionResponse> ModifySubscriptionAsync(
            RequestHeader     requestHeader,
            uint              subscriptionId,
            double            requestedPublishingInterval,
            uint              requestedLifetimeCount,
            uint              requestedMaxKeepAliveCount,
            uint              maxNotificationsPerPublish,
            byte              priority,
            CancellationToken ct);
        #endif
        #endif

        #if (!OPCUA_EXCLUDE_SetPublishingMode)
        /// <summary>
        /// Invokes the SetPublishingMode service.
        /// </summary>
        ResponseHeader SetPublishingMode(
            RequestHeader                requestHeader,
            bool                         publishingEnabled,
            UInt32Collection             subscriptionIds,
            out StatusCodeCollection     results,
            out DiagnosticInfoCollection diagnosticInfos);

        #if (!OPCUA_EXCLUDE_SetPublishingMode_ASYNC)
        /// <summary>
        /// Invokes the SetPublishingMode service using async Task based request.
        /// </summary>
        Task<SetPublishingModeResponse> SetPublishingModeAsync(
            RequestHeader     requestHeader,
            bool              publishingEnabled,
            UInt32Collection  subscriptionIds,
            CancellationToken ct);
        #endif
        #endif

        #if (!OPCUA_EXCLUDE_Publish)
        /// <summary>
        /// Invokes the Publish service.
        /// </summary>
        ResponseHeader Publish(
            RequestHeader                         requestHeader,
            SubscriptionAcknowledgementCollection subscriptionAcknowledgements,
            out uint                              subscriptionId,
            out UInt32Collection                  availableSequenceNumbers,
            out bool                              moreNotifications,
            out NotificationMessage               notificationMessage,
            out StatusCodeCollection              results,
            out DiagnosticInfoCollection          diagnosticInfos);

        #if (!OPCUA_EXCLUDE_Publish_ASYNC)
        /// <summary>
        /// Invokes the Publish service using async Task based request.
        /// </summary>
        Task<PublishResponse> PublishAsync(
            RequestHeader                         requestHeader,
            SubscriptionAcknowledgementCollection subscriptionAcknowledgements,
            CancellationToken                     ct);
        #endif
        #endif

        #if (!OPCUA_EXCLUDE_Republish)
        /// <summary>
        /// Invokes the Republish service.
        /// </summary>
        ResponseHeader Republish(
            RequestHeader           requestHeader,
            uint                    subscriptionId,
            uint                    retransmitSequenceNumber,
            out NotificationMessage notificationMessage);

        #if (!OPCUA_EXCLUDE_Republish_ASYNC)
        /// <summary>
        /// Invokes the Republish service using async Task based request.
        /// </summary>
        Task<RepublishResponse> RepublishAsync(
            RequestHeader     requestHeader,
            uint              subscriptionId,
            uint              retransmitSequenceNumber,
            CancellationToken ct);
        #endif
        #endif

        #if (!OPCUA_EXCLUDE_TransferSubscriptions)
        /// <summary>
        /// Invokes the TransferSubscriptions service.
        /// </summary>
        ResponseHeader TransferSubscriptions(
            RequestHeader                requestHeader,
            UInt32Collection             subscriptionIds,
            bool                         sendInitialValues,
            out TransferResultCollection results,
            out DiagnosticInfoCollection diagnosticInfos);

        #if (!OPCUA_EXCLUDE_TransferSubscriptions_ASYNC)
        /// <summary>
        /// Invokes the TransferSubscriptions service using async Task based request.
        /// </summary>
        Task<TransferSubscriptionsResponse> TransferSubscriptionsAsync(
            RequestHeader     requestHeader,
            UInt32Collection  subscriptionIds,
            bool              sendInitialValues,
            CancellationToken ct);
        #endif
        #endif

        #if (!OPCUA_EXCLUDE_DeleteSubscriptions)
        /// <summary>
        /// Invokes the DeleteSubscriptions service.
        /// </summary>
        ResponseHeader DeleteSubscriptions(
            RequestHeader                requestHeader,
            UInt32Collection             subscriptionIds,
            out StatusCodeCollection     results,
            out DiagnosticInfoCollection diagnosticInfos);

        #if (!OPCUA_EXCLUDE_DeleteSubscriptions_ASYNC)
        /// <summary>
        /// Invokes the DeleteSubscriptions service using async Task based request.
        /// </summary>
        Task<DeleteSubscriptionsResponse> DeleteSubscriptionsAsync(
            RequestHeader     requestHeader,
            UInt32Collection  subscriptionIds,
            CancellationToken ct);
        #endif
        #endif
    }
    #endregion

    #region SessionServerBase Class
    /// <summary>
    /// A basic implementation of the UA server.
    /// </summary>
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public partial class SessionServerBase : ServerBase, ISessionServer
    {
        #if (!OPCUA_EXCLUDE_FindServers)
        /// <summary>
        /// Invokes the FindServers service.
        /// </summary>
        public virtual ResponseHeader FindServers(
            RequestHeader                        requestHeader,
            string                               endpointUrl,
            StringCollection                     localeIds,
            StringCollection                     serverUris,
            out ApplicationDescriptionCollection servers)
        {
            servers = null;

            ValidateRequest(requestHeader);

            // Insert implementation.

            return CreateResponse(requestHeader, StatusCodes.BadServiceUnsupported);
        }

        #if (!OPCUA_EXCLUDE_FindServers_ASYNC)
            /// <summary>
            /// Invokes the FindServers service using async Task based request.
            /// </summary>
            public virtual async Task<FindServersResponse> FindServersAsync(
                RequestHeader     requestHeader,
                string            endpointUrl,
                StringCollection  localeIds,
                StringCollection  serverUris,
                CancellationToken ct)
            {
                ValidateRequest(requestHeader);

                // Insert implementation.
                await Task.CompletedTask;

                throw new ServiceResultException(StatusCodes.BadServiceUnsupported);
            }
        #endif
        #endif

        #if (!OPCUA_EXCLUDE_FindServersOnNetwork)
        /// <summary>
        /// Invokes the FindServersOnNetwork service.
        /// </summary>
        public virtual ResponseHeader FindServersOnNetwork(
            RequestHeader                 requestHeader,
            uint                          startingRecordId,
            uint                          maxRecordsToReturn,
            StringCollection              serverCapabilityFilter,
            out DateTime                  lastCounterResetTime,
            out ServerOnNetworkCollection servers)
        {
            lastCounterResetTime = DateTime.MinValue;
            servers = null;

            ValidateRequest(requestHeader);

            // Insert implementation.

            return CreateResponse(requestHeader, StatusCodes.BadServiceUnsupported);
        }

        #if (!OPCUA_EXCLUDE_FindServersOnNetwork_ASYNC)
            /// <summary>
            /// Invokes the FindServersOnNetwork service using async Task based request.
            /// </summary>
            public virtual async Task<FindServersOnNetworkResponse> FindServersOnNetworkAsync(
                RequestHeader     requestHeader,
                uint              startingRecordId,
                uint              maxRecordsToReturn,
                StringCollection  serverCapabilityFilter,
                CancellationToken ct)
            {
                ValidateRequest(requestHeader);

                // Insert implementation.
                await Task.CompletedTask;

                throw new ServiceResultException(StatusCodes.BadServiceUnsupported);
            }
        #endif
        #endif

        #if (!OPCUA_EXCLUDE_GetEndpoints)
        /// <summary>
        /// Invokes the GetEndpoints service.
        /// </summary>
        public virtual ResponseHeader GetEndpoints(
            RequestHeader                     requestHeader,
            string                            endpointUrl,
            StringCollection                  localeIds,
            StringCollection                  profileUris,
            out EndpointDescriptionCollection endpoints)
        {
            endpoints = null;

            ValidateRequest(requestHeader);

            // Insert implementation.

            return CreateResponse(requestHeader, StatusCodes.BadServiceUnsupported);
        }

        #if (!OPCUA_EXCLUDE_GetEndpoints_ASYNC)
            /// <summary>
            /// Invokes the GetEndpoints service using async Task based request.
            /// </summary>
            public virtual async Task<GetEndpointsResponse> GetEndpointsAsync(
                RequestHeader     requestHeader,
                string            endpointUrl,
                StringCollection  localeIds,
                StringCollection  profileUris,
                CancellationToken ct)
            {
                ValidateRequest(requestHeader);

                // Insert implementation.
                await Task.CompletedTask;

                throw new ServiceResultException(StatusCodes.BadServiceUnsupported);
            }
        #endif
        #endif

        #if (!OPCUA_EXCLUDE_CreateSession)
        /// <summary>
        /// Invokes the CreateSession service.
        /// </summary>
        public virtual ResponseHeader CreateSession(
            RequestHeader                           requestHeader,
            ApplicationDescription                  clientDescription,
            string                                  serverUri,
            string                                  endpointUrl,
            string                                  sessionName,
            byte[]                                  clientNonce,
            byte[]                                  clientCertificate,
            double                                  requestedSessionTimeout,
            uint                                    maxResponseMessageSize,
            out NodeId                              sessionId,
            out NodeId                              authenticationToken,
            out double                              revisedSessionTimeout,
            out byte[]                              serverNonce,
            out byte[]                              serverCertificate,
            out EndpointDescriptionCollection       serverEndpoints,
            out SignedSoftwareCertificateCollection serverSoftwareCertificates,
            out SignatureData                       serverSignature,
            out uint                                maxRequestMessageSize)
        {
            sessionId = null;
            authenticationToken = null;
            revisedSessionTimeout = 0;
            serverNonce = null;
            serverCertificate = null;
            serverEndpoints = null;
            serverSoftwareCertificates = null;
            serverSignature = null;
            maxRequestMessageSize = 0;

            ValidateRequest(requestHeader);

            // Insert implementation.

            return CreateResponse(requestHeader, StatusCodes.BadServiceUnsupported);
        }

        #if (!OPCUA_EXCLUDE_CreateSession_ASYNC)
            /// <summary>
            /// Invokes the CreateSession service using async Task based request.
            /// </summary>
            public virtual async Task<CreateSessionResponse> CreateSessionAsync(
                RequestHeader          requestHeader,
                ApplicationDescription clientDescription,
                string                 serverUri,
                string                 endpointUrl,
                string                 sessionName,
                byte[]                 clientNonce,
                byte[]                 clientCertificate,
                double                 requestedSessionTimeout,
                uint                   maxResponseMessageSize,
                CancellationToken      ct)
            {
                ValidateRequest(requestHeader);

                // Insert implementation.
                await Task.CompletedTask;

                throw new ServiceResultException(StatusCodes.BadServiceUnsupported);
            }
        #endif
        #endif

        #if (!OPCUA_EXCLUDE_ActivateSession)
        /// <summary>
        /// Invokes the ActivateSession service.
        /// </summary>
        public virtual ResponseHeader ActivateSession(
            RequestHeader                       requestHeader,
            SignatureData                       clientSignature,
            SignedSoftwareCertificateCollection clientSoftwareCertificates,
            StringCollection                    localeIds,
            ExtensionObject                     userIdentityToken,
            SignatureData                       userTokenSignature,
            out byte[]                          serverNonce,
            out StatusCodeCollection            results,
            out DiagnosticInfoCollection        diagnosticInfos)
        {
            serverNonce = null;
            results = null;
            diagnosticInfos = null;

            ValidateRequest(requestHeader);

            // Insert implementation.

            return CreateResponse(requestHeader, StatusCodes.BadServiceUnsupported);
        }

        #if (!OPCUA_EXCLUDE_ActivateSession_ASYNC)
            /// <summary>
            /// Invokes the ActivateSession service using async Task based request.
            /// </summary>
            public virtual async Task<ActivateSessionResponse> ActivateSessionAsync(
                RequestHeader                       requestHeader,
                SignatureData                       clientSignature,
                SignedSoftwareCertificateCollection clientSoftwareCertificates,
                StringCollection                    localeIds,
                ExtensionObject                     userIdentityToken,
                SignatureData                       userTokenSignature,
                CancellationToken                   ct)
            {
                ValidateRequest(requestHeader);

                // Insert implementation.
                await Task.CompletedTask;

                throw new ServiceResultException(StatusCodes.BadServiceUnsupported);
            }
        #endif
        #endif

        #if (!OPCUA_EXCLUDE_CloseSession)
        /// <summary>
        /// Invokes the CloseSession service.
        /// </summary>
        public virtual ResponseHeader CloseSession(
            RequestHeader requestHeader,
            bool          deleteSubscriptions)
        {

            ValidateRequest(requestHeader);

            // Insert implementation.

            return CreateResponse(requestHeader, StatusCodes.BadServiceUnsupported);
        }

        #if (!OPCUA_EXCLUDE_CloseSession_ASYNC)
            /// <summary>
            /// Invokes the CloseSession service using async Task based request.
            /// </summary>
            public virtual async Task<CloseSessionResponse> CloseSessionAsync(
                RequestHeader     requestHeader,
                bool              deleteSubscriptions,
                CancellationToken ct)
            {
                ValidateRequest(requestHeader);

                // Insert implementation.
                await Task.CompletedTask;

                throw new ServiceResultException(StatusCodes.BadServiceUnsupported);
            }
        #endif
        #endif

        #if (!OPCUA_EXCLUDE_Cancel)
        /// <summary>
        /// Invokes the Cancel service.
        /// </summary>
        public virtual ResponseHeader Cancel(
            RequestHeader requestHeader,
            uint          requestHandle,
            out uint      cancelCount)
        {
            cancelCount = 0;

            ValidateRequest(requestHeader);

            // Insert implementation.

            return CreateResponse(requestHeader, StatusCodes.BadServiceUnsupported);
        }

        #if (!OPCUA_EXCLUDE_Cancel_ASYNC)
            /// <summary>
            /// Invokes the Cancel service using async Task based request.
            /// </summary>
            public virtual async Task<CancelResponse> CancelAsync(
                RequestHeader     requestHeader,
                uint              requestHandle,
                CancellationToken ct)
            {
                ValidateRequest(requestHeader);

                // Insert implementation.
                await Task.CompletedTask;

                throw new ServiceResultException(StatusCodes.BadServiceUnsupported);
            }
        #endif
        #endif

        #if (!OPCUA_EXCLUDE_AddNodes)
        /// <summary>
        /// Invokes the AddNodes service.
        /// </summary>
        public virtual ResponseHeader AddNodes(
            RequestHeader                requestHeader,
            AddNodesItemCollection       nodesToAdd,
            out AddNodesResultCollection results,
            out DiagnosticInfoCollection diagnosticInfos)
        {
            results = null;
            diagnosticInfos = null;

            ValidateRequest(requestHeader);

            // Insert implementation.

            return CreateResponse(requestHeader, StatusCodes.BadServiceUnsupported);
        }

        #if (!OPCUA_EXCLUDE_AddNodes_ASYNC)
            /// <summary>
            /// Invokes the AddNodes service using async Task based request.
            /// </summary>
            public virtual async Task<AddNodesResponse> AddNodesAsync(
                RequestHeader          requestHeader,
                AddNodesItemCollection nodesToAdd,
                CancellationToken      ct)
            {
                ValidateRequest(requestHeader);

                // Insert implementation.
                await Task.CompletedTask;

                throw new ServiceResultException(StatusCodes.BadServiceUnsupported);
            }
        #endif
        #endif

        #if (!OPCUA_EXCLUDE_AddReferences)
        /// <summary>
        /// Invokes the AddReferences service.
        /// </summary>
        public virtual ResponseHeader AddReferences(
            RequestHeader                requestHeader,
            AddReferencesItemCollection  referencesToAdd,
            out StatusCodeCollection     results,
            out DiagnosticInfoCollection diagnosticInfos)
        {
            results = null;
            diagnosticInfos = null;

            ValidateRequest(requestHeader);

            // Insert implementation.

            return CreateResponse(requestHeader, StatusCodes.BadServiceUnsupported);
        }

        #if (!OPCUA_EXCLUDE_AddReferences_ASYNC)
            /// <summary>
            /// Invokes the AddReferences service using async Task based request.
            /// </summary>
            public virtual async Task<AddReferencesResponse> AddReferencesAsync(
                RequestHeader               requestHeader,
                AddReferencesItemCollection referencesToAdd,
                CancellationToken           ct)
            {
                ValidateRequest(requestHeader);

                // Insert implementation.
                await Task.CompletedTask;

                throw new ServiceResultException(StatusCodes.BadServiceUnsupported);
            }
        #endif
        #endif

        #if (!OPCUA_EXCLUDE_DeleteNodes)
        /// <summary>
        /// Invokes the DeleteNodes service.
        /// </summary>
        public virtual ResponseHeader DeleteNodes(
            RequestHeader                requestHeader,
            DeleteNodesItemCollection    nodesToDelete,
            out StatusCodeCollection     results,
            out DiagnosticInfoCollection diagnosticInfos)
        {
            results = null;
            diagnosticInfos = null;

            ValidateRequest(requestHeader);

            // Insert implementation.

            return CreateResponse(requestHeader, StatusCodes.BadServiceUnsupported);
        }

        #if (!OPCUA_EXCLUDE_DeleteNodes_ASYNC)
            /// <summary>
            /// Invokes the DeleteNodes service using async Task based request.
            /// </summary>
            public virtual async Task<DeleteNodesResponse> DeleteNodesAsync(
                RequestHeader             requestHeader,
                DeleteNodesItemCollection nodesToDelete,
                CancellationToken         ct)
            {
                ValidateRequest(requestHeader);

                // Insert implementation.
                await Task.CompletedTask;

                throw new ServiceResultException(StatusCodes.BadServiceUnsupported);
            }
        #endif
        #endif

        #if (!OPCUA_EXCLUDE_DeleteReferences)
        /// <summary>
        /// Invokes the DeleteReferences service.
        /// </summary>
        public virtual ResponseHeader DeleteReferences(
            RequestHeader                  requestHeader,
            DeleteReferencesItemCollection referencesToDelete,
            out StatusCodeCollection       results,
            out DiagnosticInfoCollection   diagnosticInfos)
        {
            results = null;
            diagnosticInfos = null;

            ValidateRequest(requestHeader);

            // Insert implementation.

            return CreateResponse(requestHeader, StatusCodes.BadServiceUnsupported);
        }

        #if (!OPCUA_EXCLUDE_DeleteReferences_ASYNC)
            /// <summary>
            /// Invokes the DeleteReferences service using async Task based request.
            /// </summary>
            public virtual async Task<DeleteReferencesResponse> DeleteReferencesAsync(
                RequestHeader                  requestHeader,
                DeleteReferencesItemCollection referencesToDelete,
                CancellationToken              ct)
            {
                ValidateRequest(requestHeader);

                // Insert implementation.
                await Task.CompletedTask;

                throw new ServiceResultException(StatusCodes.BadServiceUnsupported);
            }
        #endif
        #endif

        #if (!OPCUA_EXCLUDE_Browse)
        /// <summary>
        /// Invokes the Browse service.
        /// </summary>
        public virtual ResponseHeader Browse(
            RequestHeader                requestHeader,
            ViewDescription              view,
            uint                         requestedMaxReferencesPerNode,
            BrowseDescriptionCollection  nodesToBrowse,
            out BrowseResultCollection   results,
            out DiagnosticInfoCollection diagnosticInfos)
        {
            results = null;
            diagnosticInfos = null;

            ValidateRequest(requestHeader);

            // Insert implementation.

            return CreateResponse(requestHeader, StatusCodes.BadServiceUnsupported);
        }

        #if (!OPCUA_EXCLUDE_Browse_ASYNC)
            /// <summary>
            /// Invokes the Browse service using async Task based request.
            /// </summary>
            public virtual async Task<BrowseResponse> BrowseAsync(
                RequestHeader               requestHeader,
                ViewDescription             view,
                uint                        requestedMaxReferencesPerNode,
                BrowseDescriptionCollection nodesToBrowse,
                CancellationToken           ct)
            {
                ValidateRequest(requestHeader);

                // Insert implementation.
                await Task.CompletedTask;

                throw new ServiceResultException(StatusCodes.BadServiceUnsupported);
            }
        #endif
        #endif

        #if (!OPCUA_EXCLUDE_BrowseNext)
        /// <summary>
        /// Invokes the BrowseNext service.
        /// </summary>
        public virtual ResponseHeader BrowseNext(
            RequestHeader                requestHeader,
            bool                         releaseContinuationPoints,
            ByteStringCollection         continuationPoints,
            out BrowseResultCollection   results,
            out DiagnosticInfoCollection diagnosticInfos)
        {
            results = null;
            diagnosticInfos = null;

            ValidateRequest(requestHeader);

            // Insert implementation.

            return CreateResponse(requestHeader, StatusCodes.BadServiceUnsupported);
        }

        #if (!OPCUA_EXCLUDE_BrowseNext_ASYNC)
            /// <summary>
            /// Invokes the BrowseNext service using async Task based request.
            /// </summary>
            public virtual async Task<BrowseNextResponse> BrowseNextAsync(
                RequestHeader        requestHeader,
                bool                 releaseContinuationPoints,
                ByteStringCollection continuationPoints,
                CancellationToken    ct)
            {
                ValidateRequest(requestHeader);

                // Insert implementation.
                await Task.CompletedTask;

                throw new ServiceResultException(StatusCodes.BadServiceUnsupported);
            }
        #endif
        #endif

        #if (!OPCUA_EXCLUDE_TranslateBrowsePathsToNodeIds)
        /// <summary>
        /// Invokes the TranslateBrowsePathsToNodeIds service.
        /// </summary>
        public virtual ResponseHeader TranslateBrowsePathsToNodeIds(
            RequestHeader                  requestHeader,
            BrowsePathCollection           browsePaths,
            out BrowsePathResultCollection results,
            out DiagnosticInfoCollection   diagnosticInfos)
        {
            results = null;
            diagnosticInfos = null;

            ValidateRequest(requestHeader);

            // Insert implementation.

            return CreateResponse(requestHeader, StatusCodes.BadServiceUnsupported);
        }

        #if (!OPCUA_EXCLUDE_TranslateBrowsePathsToNodeIds_ASYNC)
            /// <summary>
            /// Invokes the TranslateBrowsePathsToNodeIds service using async Task based request.
            /// </summary>
            public virtual async Task<TranslateBrowsePathsToNodeIdsResponse> TranslateBrowsePathsToNodeIdsAsync(
                RequestHeader        requestHeader,
                BrowsePathCollection browsePaths,
                CancellationToken    ct)
            {
                ValidateRequest(requestHeader);

                // Insert implementation.
                await Task.CompletedTask;

                throw new ServiceResultException(StatusCodes.BadServiceUnsupported);
            }
        #endif
        #endif

        #if (!OPCUA_EXCLUDE_RegisterNodes)
        /// <summary>
        /// Invokes the RegisterNodes service.
        /// </summary>
        public virtual ResponseHeader RegisterNodes(
            RequestHeader        requestHeader,
            NodeIdCollection     nodesToRegister,
            out NodeIdCollection registeredNodeIds)
        {
            registeredNodeIds = null;

            ValidateRequest(requestHeader);

            // Insert implementation.

            return CreateResponse(requestHeader, StatusCodes.BadServiceUnsupported);
        }

        #if (!OPCUA_EXCLUDE_RegisterNodes_ASYNC)
            /// <summary>
            /// Invokes the RegisterNodes service using async Task based request.
            /// </summary>
            public virtual async Task<RegisterNodesResponse> RegisterNodesAsync(
                RequestHeader     requestHeader,
                NodeIdCollection  nodesToRegister,
                CancellationToken ct)
            {
                ValidateRequest(requestHeader);

                // Insert implementation.
                await Task.CompletedTask;

                throw new ServiceResultException(StatusCodes.BadServiceUnsupported);
            }
        #endif
        #endif

        #if (!OPCUA_EXCLUDE_UnregisterNodes)
        /// <summary>
        /// Invokes the UnregisterNodes service.
        /// </summary>
        public virtual ResponseHeader UnregisterNodes(
            RequestHeader    requestHeader,
            NodeIdCollection nodesToUnregister)
        {

            ValidateRequest(requestHeader);

            // Insert implementation.

            return CreateResponse(requestHeader, StatusCodes.BadServiceUnsupported);
        }

        #if (!OPCUA_EXCLUDE_UnregisterNodes_ASYNC)
            /// <summary>
            /// Invokes the UnregisterNodes service using async Task based request.
            /// </summary>
            public virtual async Task<UnregisterNodesResponse> UnregisterNodesAsync(
                RequestHeader     requestHeader,
                NodeIdCollection  nodesToUnregister,
                CancellationToken ct)
            {
                ValidateRequest(requestHeader);

                // Insert implementation.
                await Task.CompletedTask;

                throw new ServiceResultException(StatusCodes.BadServiceUnsupported);
            }
        #endif
        #endif

        #if (!OPCUA_EXCLUDE_QueryFirst)
        /// <summary>
        /// Invokes the QueryFirst service.
        /// </summary>
        public virtual ResponseHeader QueryFirst(
            RequestHeader                 requestHeader,
            ViewDescription               view,
            NodeTypeDescriptionCollection nodeTypes,
            ContentFilter                 filter,
            uint                          maxDataSetsToReturn,
            uint                          maxReferencesToReturn,
            out QueryDataSetCollection    queryDataSets,
            out byte[]                    continuationPoint,
            out ParsingResultCollection   parsingResults,
            out DiagnosticInfoCollection  diagnosticInfos,
            out ContentFilterResult       filterResult)
        {
            queryDataSets = null;
            continuationPoint = null;
            parsingResults = null;
            diagnosticInfos = null;
            filterResult = null;

            ValidateRequest(requestHeader);

            // Insert implementation.

            return CreateResponse(requestHeader, StatusCodes.BadServiceUnsupported);
        }

        #if (!OPCUA_EXCLUDE_QueryFirst_ASYNC)
            /// <summary>
            /// Invokes the QueryFirst service using async Task based request.
            /// </summary>
            public virtual async Task<QueryFirstResponse> QueryFirstAsync(
                RequestHeader                 requestHeader,
                ViewDescription               view,
                NodeTypeDescriptionCollection nodeTypes,
                ContentFilter                 filter,
                uint                          maxDataSetsToReturn,
                uint                          maxReferencesToReturn,
                CancellationToken             ct)
            {
                ValidateRequest(requestHeader);

                // Insert implementation.
                await Task.CompletedTask;

                throw new ServiceResultException(StatusCodes.BadServiceUnsupported);
            }
        #endif
        #endif

        #if (!OPCUA_EXCLUDE_QueryNext)
        /// <summary>
        /// Invokes the QueryNext service.
        /// </summary>
        public virtual ResponseHeader QueryNext(
            RequestHeader              requestHeader,
            bool                       releaseContinuationPoint,
            byte[]                     continuationPoint,
            out QueryDataSetCollection queryDataSets,
            out byte[]                 revisedContinuationPoint)
        {
            queryDataSets = null;
            revisedContinuationPoint = null;

            ValidateRequest(requestHeader);

            // Insert implementation.

            return CreateResponse(requestHeader, StatusCodes.BadServiceUnsupported);
        }

        #if (!OPCUA_EXCLUDE_QueryNext_ASYNC)
            /// <summary>
            /// Invokes the QueryNext service using async Task based request.
            /// </summary>
            public virtual async Task<QueryNextResponse> QueryNextAsync(
                RequestHeader     requestHeader,
                bool              releaseContinuationPoint,
                byte[]            continuationPoint,
                CancellationToken ct)
            {
                ValidateRequest(requestHeader);

                // Insert implementation.
                await Task.CompletedTask;

                throw new ServiceResultException(StatusCodes.BadServiceUnsupported);
            }
        #endif
        #endif

        #if (!OPCUA_EXCLUDE_Read)
        /// <summary>
        /// Invokes the Read service.
        /// </summary>
        public virtual ResponseHeader Read(
            RequestHeader                requestHeader,
            double                       maxAge,
            TimestampsToReturn           timestampsToReturn,
            ReadValueIdCollection        nodesToRead,
            out DataValueCollection      results,
            out DiagnosticInfoCollection diagnosticInfos)
        {
            results = null;
            diagnosticInfos = null;

            ValidateRequest(requestHeader);

            // Insert implementation.

            return CreateResponse(requestHeader, StatusCodes.BadServiceUnsupported);
        }

        #if (!OPCUA_EXCLUDE_Read_ASYNC)
            /// <summary>
            /// Invokes the Read service using async Task based request.
            /// </summary>
            public virtual async Task<ReadResponse> ReadAsync(
                RequestHeader         requestHeader,
                double                maxAge,
                TimestampsToReturn    timestampsToReturn,
                ReadValueIdCollection nodesToRead,
                CancellationToken     ct)
            {
                ValidateRequest(requestHeader);

                // Insert implementation.
                await Task.CompletedTask;

                throw new ServiceResultException(StatusCodes.BadServiceUnsupported);
            }
        #endif
        #endif

        #if (!OPCUA_EXCLUDE_HistoryRead)
        /// <summary>
        /// Invokes the HistoryRead service.
        /// </summary>
        public virtual ResponseHeader HistoryRead(
            RequestHeader                   requestHeader,
            ExtensionObject                 historyReadDetails,
            TimestampsToReturn              timestampsToReturn,
            bool                            releaseContinuationPoints,
            HistoryReadValueIdCollection    nodesToRead,
            out HistoryReadResultCollection results,
            out DiagnosticInfoCollection    diagnosticInfos)
        {
            results = null;
            diagnosticInfos = null;

            ValidateRequest(requestHeader);

            // Insert implementation.

            return CreateResponse(requestHeader, StatusCodes.BadServiceUnsupported);
        }

        #if (!OPCUA_EXCLUDE_HistoryRead_ASYNC)
            /// <summary>
            /// Invokes the HistoryRead service using async Task based request.
            /// </summary>
            public virtual async Task<HistoryReadResponse> HistoryReadAsync(
                RequestHeader                requestHeader,
                ExtensionObject              historyReadDetails,
                TimestampsToReturn           timestampsToReturn,
                bool                         releaseContinuationPoints,
                HistoryReadValueIdCollection nodesToRead,
                CancellationToken            ct)
            {
                ValidateRequest(requestHeader);

                // Insert implementation.
                await Task.CompletedTask;

                throw new ServiceResultException(StatusCodes.BadServiceUnsupported);
            }
        #endif
        #endif

        #if (!OPCUA_EXCLUDE_Write)
        /// <summary>
        /// Invokes the Write service.
        /// </summary>
        public virtual ResponseHeader Write(
            RequestHeader                requestHeader,
            WriteValueCollection         nodesToWrite,
            out StatusCodeCollection     results,
            out DiagnosticInfoCollection diagnosticInfos)
        {
            results = null;
            diagnosticInfos = null;

            ValidateRequest(requestHeader);

            // Insert implementation.

            return CreateResponse(requestHeader, StatusCodes.BadServiceUnsupported);
        }

        #if (!OPCUA_EXCLUDE_Write_ASYNC)
            /// <summary>
            /// Invokes the Write service using async Task based request.
            /// </summary>
            public virtual async Task<WriteResponse> WriteAsync(
                RequestHeader        requestHeader,
                WriteValueCollection nodesToWrite,
                CancellationToken    ct)
            {
                ValidateRequest(requestHeader);

                // Insert implementation.
                await Task.CompletedTask;

                throw new ServiceResultException(StatusCodes.BadServiceUnsupported);
            }
        #endif
        #endif

        #if (!OPCUA_EXCLUDE_HistoryUpdate)
        /// <summary>
        /// Invokes the HistoryUpdate service.
        /// </summary>
        public virtual ResponseHeader HistoryUpdate(
            RequestHeader                     requestHeader,
            ExtensionObjectCollection         historyUpdateDetails,
            out HistoryUpdateResultCollection results,
            out DiagnosticInfoCollection      diagnosticInfos)
        {
            results = null;
            diagnosticInfos = null;

            ValidateRequest(requestHeader);

            // Insert implementation.

            return CreateResponse(requestHeader, StatusCodes.BadServiceUnsupported);
        }

        #if (!OPCUA_EXCLUDE_HistoryUpdate_ASYNC)
            /// <summary>
            /// Invokes the HistoryUpdate service using async Task based request.
            /// </summary>
            public virtual async Task<HistoryUpdateResponse> HistoryUpdateAsync(
                RequestHeader             requestHeader,
                ExtensionObjectCollection historyUpdateDetails,
                CancellationToken         ct)
            {
                ValidateRequest(requestHeader);

                // Insert implementation.
                await Task.CompletedTask;

                throw new ServiceResultException(StatusCodes.BadServiceUnsupported);
            }
        #endif
        #endif

        #if (!OPCUA_EXCLUDE_Call)
        /// <summary>
        /// Invokes the Call service.
        /// </summary>
        public virtual ResponseHeader Call(
            RequestHeader                  requestHeader,
            CallMethodRequestCollection    methodsToCall,
            out CallMethodResultCollection results,
            out DiagnosticInfoCollection   diagnosticInfos)
        {
            results = null;
            diagnosticInfos = null;

            ValidateRequest(requestHeader);

            // Insert implementation.

            return CreateResponse(requestHeader, StatusCodes.BadServiceUnsupported);
        }

        #if (!OPCUA_EXCLUDE_Call_ASYNC)
            /// <summary>
            /// Invokes the Call service using async Task based request.
            /// </summary>
            public virtual async Task<CallResponse> CallAsync(
                RequestHeader               requestHeader,
                CallMethodRequestCollection methodsToCall,
                CancellationToken           ct)
            {
                ValidateRequest(requestHeader);

                // Insert implementation.
                await Task.CompletedTask;

                throw new ServiceResultException(StatusCodes.BadServiceUnsupported);
            }
        #endif
        #endif

        #if (!OPCUA_EXCLUDE_CreateMonitoredItems)
        /// <summary>
        /// Invokes the CreateMonitoredItems service.
        /// </summary>
        public virtual ResponseHeader CreateMonitoredItems(
            RequestHeader                           requestHeader,
            uint                                    subscriptionId,
            TimestampsToReturn                      timestampsToReturn,
            MonitoredItemCreateRequestCollection    itemsToCreate,
            out MonitoredItemCreateResultCollection results,
            out DiagnosticInfoCollection            diagnosticInfos)
        {
            results = null;
            diagnosticInfos = null;

            ValidateRequest(requestHeader);

            // Insert implementation.

            return CreateResponse(requestHeader, StatusCodes.BadServiceUnsupported);
        }

        #if (!OPCUA_EXCLUDE_CreateMonitoredItems_ASYNC)
            /// <summary>
            /// Invokes the CreateMonitoredItems service using async Task based request.
            /// </summary>
            public virtual async Task<CreateMonitoredItemsResponse> CreateMonitoredItemsAsync(
                RequestHeader                        requestHeader,
                uint                                 subscriptionId,
                TimestampsToReturn                   timestampsToReturn,
                MonitoredItemCreateRequestCollection itemsToCreate,
                CancellationToken                    ct)
            {
                ValidateRequest(requestHeader);

                // Insert implementation.
                await Task.CompletedTask;

                throw new ServiceResultException(StatusCodes.BadServiceUnsupported);
            }
        #endif
        #endif

        #if (!OPCUA_EXCLUDE_ModifyMonitoredItems)
        /// <summary>
        /// Invokes the ModifyMonitoredItems service.
        /// </summary>
        public virtual ResponseHeader ModifyMonitoredItems(
            RequestHeader                           requestHeader,
            uint                                    subscriptionId,
            TimestampsToReturn                      timestampsToReturn,
            MonitoredItemModifyRequestCollection    itemsToModify,
            out MonitoredItemModifyResultCollection results,
            out DiagnosticInfoCollection            diagnosticInfos)
        {
            results = null;
            diagnosticInfos = null;

            ValidateRequest(requestHeader);

            // Insert implementation.

            return CreateResponse(requestHeader, StatusCodes.BadServiceUnsupported);
        }

        #if (!OPCUA_EXCLUDE_ModifyMonitoredItems_ASYNC)
            /// <summary>
            /// Invokes the ModifyMonitoredItems service using async Task based request.
            /// </summary>
            public virtual async Task<ModifyMonitoredItemsResponse> ModifyMonitoredItemsAsync(
                RequestHeader                        requestHeader,
                uint                                 subscriptionId,
                TimestampsToReturn                   timestampsToReturn,
                MonitoredItemModifyRequestCollection itemsToModify,
                CancellationToken                    ct)
            {
                ValidateRequest(requestHeader);

                // Insert implementation.
                await Task.CompletedTask;

                throw new ServiceResultException(StatusCodes.BadServiceUnsupported);
            }
        #endif
        #endif

        #if (!OPCUA_EXCLUDE_SetMonitoringMode)
        /// <summary>
        /// Invokes the SetMonitoringMode service.
        /// </summary>
        public virtual ResponseHeader SetMonitoringMode(
            RequestHeader                requestHeader,
            uint                         subscriptionId,
            MonitoringMode               monitoringMode,
            UInt32Collection             monitoredItemIds,
            out StatusCodeCollection     results,
            out DiagnosticInfoCollection diagnosticInfos)
        {
            results = null;
            diagnosticInfos = null;

            ValidateRequest(requestHeader);

            // Insert implementation.

            return CreateResponse(requestHeader, StatusCodes.BadServiceUnsupported);
        }

        #if (!OPCUA_EXCLUDE_SetMonitoringMode_ASYNC)
            /// <summary>
            /// Invokes the SetMonitoringMode service using async Task based request.
            /// </summary>
            public virtual async Task<SetMonitoringModeResponse> SetMonitoringModeAsync(
                RequestHeader     requestHeader,
                uint              subscriptionId,
                MonitoringMode    monitoringMode,
                UInt32Collection  monitoredItemIds,
                CancellationToken ct)
            {
                ValidateRequest(requestHeader);

                // Insert implementation.
                await Task.CompletedTask;

                throw new ServiceResultException(StatusCodes.BadServiceUnsupported);
            }
        #endif
        #endif

        #if (!OPCUA_EXCLUDE_SetTriggering)
        /// <summary>
        /// Invokes the SetTriggering service.
        /// </summary>
        public virtual ResponseHeader SetTriggering(
            RequestHeader                requestHeader,
            uint                         subscriptionId,
            uint                         triggeringItemId,
            UInt32Collection             linksToAdd,
            UInt32Collection             linksToRemove,
            out StatusCodeCollection     addResults,
            out DiagnosticInfoCollection addDiagnosticInfos,
            out StatusCodeCollection     removeResults,
            out DiagnosticInfoCollection removeDiagnosticInfos)
        {
            addResults = null;
            addDiagnosticInfos = null;
            removeResults = null;
            removeDiagnosticInfos = null;

            ValidateRequest(requestHeader);

            // Insert implementation.

            return CreateResponse(requestHeader, StatusCodes.BadServiceUnsupported);
        }

        #if (!OPCUA_EXCLUDE_SetTriggering_ASYNC)
            /// <summary>
            /// Invokes the SetTriggering service using async Task based request.
            /// </summary>
            public virtual async Task<SetTriggeringResponse> SetTriggeringAsync(
                RequestHeader     requestHeader,
                uint              subscriptionId,
                uint              triggeringItemId,
                UInt32Collection  linksToAdd,
                UInt32Collection  linksToRemove,
                CancellationToken ct)
            {
                ValidateRequest(requestHeader);

                // Insert implementation.
                await Task.CompletedTask;

                throw new ServiceResultException(StatusCodes.BadServiceUnsupported);
            }
        #endif
        #endif

        #if (!OPCUA_EXCLUDE_DeleteMonitoredItems)
        /// <summary>
        /// Invokes the DeleteMonitoredItems service.
        /// </summary>
        public virtual ResponseHeader DeleteMonitoredItems(
            RequestHeader                requestHeader,
            uint                         subscriptionId,
            UInt32Collection             monitoredItemIds,
            out StatusCodeCollection     results,
            out DiagnosticInfoCollection diagnosticInfos)
        {
            results = null;
            diagnosticInfos = null;

            ValidateRequest(requestHeader);

            // Insert implementation.

            return CreateResponse(requestHeader, StatusCodes.BadServiceUnsupported);
        }

        #if (!OPCUA_EXCLUDE_DeleteMonitoredItems_ASYNC)
            /// <summary>
            /// Invokes the DeleteMonitoredItems service using async Task based request.
            /// </summary>
            public virtual async Task<DeleteMonitoredItemsResponse> DeleteMonitoredItemsAsync(
                RequestHeader     requestHeader,
                uint              subscriptionId,
                UInt32Collection  monitoredItemIds,
                CancellationToken ct)
            {
                ValidateRequest(requestHeader);

                // Insert implementation.
                await Task.CompletedTask;

                throw new ServiceResultException(StatusCodes.BadServiceUnsupported);
            }
        #endif
        #endif

        #if (!OPCUA_EXCLUDE_CreateSubscription)
        /// <summary>
        /// Invokes the CreateSubscription service.
        /// </summary>
        public virtual ResponseHeader CreateSubscription(
            RequestHeader requestHeader,
            double        requestedPublishingInterval,
            uint          requestedLifetimeCount,
            uint          requestedMaxKeepAliveCount,
            uint          maxNotificationsPerPublish,
            bool          publishingEnabled,
            byte          priority,
            out uint      subscriptionId,
            out double    revisedPublishingInterval,
            out uint      revisedLifetimeCount,
            out uint      revisedMaxKeepAliveCount)
        {
            subscriptionId = 0;
            revisedPublishingInterval = 0;
            revisedLifetimeCount = 0;
            revisedMaxKeepAliveCount = 0;

            ValidateRequest(requestHeader);

            // Insert implementation.

            return CreateResponse(requestHeader, StatusCodes.BadServiceUnsupported);
        }

        #if (!OPCUA_EXCLUDE_CreateSubscription_ASYNC)
            /// <summary>
            /// Invokes the CreateSubscription service using async Task based request.
            /// </summary>
            public virtual async Task<CreateSubscriptionResponse> CreateSubscriptionAsync(
                RequestHeader     requestHeader,
                double            requestedPublishingInterval,
                uint              requestedLifetimeCount,
                uint              requestedMaxKeepAliveCount,
                uint              maxNotificationsPerPublish,
                bool              publishingEnabled,
                byte              priority,
                CancellationToken ct)
            {
                ValidateRequest(requestHeader);

                // Insert implementation.
                await Task.CompletedTask;

                throw new ServiceResultException(StatusCodes.BadServiceUnsupported);
            }
        #endif
        #endif

        #if (!OPCUA_EXCLUDE_ModifySubscription)
        /// <summary>
        /// Invokes the ModifySubscription service.
        /// </summary>
        public virtual ResponseHeader ModifySubscription(
            RequestHeader requestHeader,
            uint          subscriptionId,
            double        requestedPublishingInterval,
            uint          requestedLifetimeCount,
            uint          requestedMaxKeepAliveCount,
            uint          maxNotificationsPerPublish,
            byte          priority,
            out double    revisedPublishingInterval,
            out uint      revisedLifetimeCount,
            out uint      revisedMaxKeepAliveCount)
        {
            revisedPublishingInterval = 0;
            revisedLifetimeCount = 0;
            revisedMaxKeepAliveCount = 0;

            ValidateRequest(requestHeader);

            // Insert implementation.

            return CreateResponse(requestHeader, StatusCodes.BadServiceUnsupported);
        }

        #if (!OPCUA_EXCLUDE_ModifySubscription_ASYNC)
            /// <summary>
            /// Invokes the ModifySubscription service using async Task based request.
            /// </summary>
            public virtual async Task<ModifySubscriptionResponse> ModifySubscriptionAsync(
                RequestHeader     requestHeader,
                uint              subscriptionId,
                double            requestedPublishingInterval,
                uint              requestedLifetimeCount,
                uint              requestedMaxKeepAliveCount,
                uint              maxNotificationsPerPublish,
                byte              priority,
                CancellationToken ct)
            {
                ValidateRequest(requestHeader);

                // Insert implementation.
                await Task.CompletedTask;

                throw new ServiceResultException(StatusCodes.BadServiceUnsupported);
            }
        #endif
        #endif

        #if (!OPCUA_EXCLUDE_SetPublishingMode)
        /// <summary>
        /// Invokes the SetPublishingMode service.
        /// </summary>
        public virtual ResponseHeader SetPublishingMode(
            RequestHeader                requestHeader,
            bool                         publishingEnabled,
            UInt32Collection             subscriptionIds,
            out StatusCodeCollection     results,
            out DiagnosticInfoCollection diagnosticInfos)
        {
            results = null;
            diagnosticInfos = null;

            ValidateRequest(requestHeader);

            // Insert implementation.

            return CreateResponse(requestHeader, StatusCodes.BadServiceUnsupported);
        }

        #if (!OPCUA_EXCLUDE_SetPublishingMode_ASYNC)
            /// <summary>
            /// Invokes the SetPublishingMode service using async Task based request.
            /// </summary>
            public virtual async Task<SetPublishingModeResponse> SetPublishingModeAsync(
                RequestHeader     requestHeader,
                bool              publishingEnabled,
                UInt32Collection  subscriptionIds,
                CancellationToken ct)
            {
                ValidateRequest(requestHeader);

                // Insert implementation.
                await Task.CompletedTask;

                throw new ServiceResultException(StatusCodes.BadServiceUnsupported);
            }
        #endif
        #endif

        #if (!OPCUA_EXCLUDE_Publish)
        /// <summary>
        /// Invokes the Publish service.
        /// </summary>
        public virtual ResponseHeader Publish(
            RequestHeader                         requestHeader,
            SubscriptionAcknowledgementCollection subscriptionAcknowledgements,
            out uint                              subscriptionId,
            out UInt32Collection                  availableSequenceNumbers,
            out bool                              moreNotifications,
            out NotificationMessage               notificationMessage,
            out StatusCodeCollection              results,
            out DiagnosticInfoCollection          diagnosticInfos)
        {
            subscriptionId = 0;
            availableSequenceNumbers = null;
            moreNotifications = false;
            notificationMessage = null;
            results = null;
            diagnosticInfos = null;

            ValidateRequest(requestHeader);

            // Insert implementation.

            return CreateResponse(requestHeader, StatusCodes.BadServiceUnsupported);
        }

        #if (!OPCUA_EXCLUDE_Publish_ASYNC)
            /// <summary>
            /// Invokes the Publish service using async Task based request.
            /// </summary>
            public virtual async Task<PublishResponse> PublishAsync(
                RequestHeader                         requestHeader,
                SubscriptionAcknowledgementCollection subscriptionAcknowledgements,
                CancellationToken                     ct)
            {
                ValidateRequest(requestHeader);

                // Insert implementation.
                await Task.CompletedTask;

                throw new ServiceResultException(StatusCodes.BadServiceUnsupported);
            }
        #endif
        #endif

        #if (!OPCUA_EXCLUDE_Republish)
        /// <summary>
        /// Invokes the Republish service.
        /// </summary>
        public virtual ResponseHeader Republish(
            RequestHeader           requestHeader,
            uint                    subscriptionId,
            uint                    retransmitSequenceNumber,
            out NotificationMessage notificationMessage)
        {
            notificationMessage = null;

            ValidateRequest(requestHeader);

            // Insert implementation.

            return CreateResponse(requestHeader, StatusCodes.BadServiceUnsupported);
        }

        #if (!OPCUA_EXCLUDE_Republish_ASYNC)
            /// <summary>
            /// Invokes the Republish service using async Task based request.
            /// </summary>
            public virtual async Task<RepublishResponse> RepublishAsync(
                RequestHeader     requestHeader,
                uint              subscriptionId,
                uint              retransmitSequenceNumber,
                CancellationToken ct)
            {
                ValidateRequest(requestHeader);

                // Insert implementation.
                await Task.CompletedTask;

                throw new ServiceResultException(StatusCodes.BadServiceUnsupported);
            }
        #endif
        #endif

        #if (!OPCUA_EXCLUDE_TransferSubscriptions)
        /// <summary>
        /// Invokes the TransferSubscriptions service.
        /// </summary>
        public virtual ResponseHeader TransferSubscriptions(
            RequestHeader                requestHeader,
            UInt32Collection             subscriptionIds,
            bool                         sendInitialValues,
            out TransferResultCollection results,
            out DiagnosticInfoCollection diagnosticInfos)
        {
            results = null;
            diagnosticInfos = null;

            ValidateRequest(requestHeader);

            // Insert implementation.

            return CreateResponse(requestHeader, StatusCodes.BadServiceUnsupported);
        }

        #if (!OPCUA_EXCLUDE_TransferSubscriptions_ASYNC)
            /// <summary>
            /// Invokes the TransferSubscriptions service using async Task based request.
            /// </summary>
            public virtual async Task<TransferSubscriptionsResponse> TransferSubscriptionsAsync(
                RequestHeader     requestHeader,
                UInt32Collection  subscriptionIds,
                bool              sendInitialValues,
                CancellationToken ct)
            {
                ValidateRequest(requestHeader);

                // Insert implementation.
                await Task.CompletedTask;

                throw new ServiceResultException(StatusCodes.BadServiceUnsupported);
            }
        #endif
        #endif

        #if (!OPCUA_EXCLUDE_DeleteSubscriptions)
        /// <summary>
        /// Invokes the DeleteSubscriptions service.
        /// </summary>
        public virtual ResponseHeader DeleteSubscriptions(
            RequestHeader                requestHeader,
            UInt32Collection             subscriptionIds,
            out StatusCodeCollection     results,
            out DiagnosticInfoCollection diagnosticInfos)
        {
            results = null;
            diagnosticInfos = null;

            ValidateRequest(requestHeader);

            // Insert implementation.

            return CreateResponse(requestHeader, StatusCodes.BadServiceUnsupported);
        }

        #if (!OPCUA_EXCLUDE_DeleteSubscriptions_ASYNC)
            /// <summary>
            /// Invokes the DeleteSubscriptions service using async Task based request.
            /// </summary>
            public virtual async Task<DeleteSubscriptionsResponse> DeleteSubscriptionsAsync(
                RequestHeader     requestHeader,
                UInt32Collection  subscriptionIds,
                CancellationToken ct)
            {
                ValidateRequest(requestHeader);

                // Insert implementation.
                await Task.CompletedTask;

                throw new ServiceResultException(StatusCodes.BadServiceUnsupported);
            }
        #endif
        #endif
    }
    #endregion

    #region IDiscoveryServer Interface
    /// <summary>
    /// An interface to a UA server implementation.
    /// </summary>
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public interface IDiscoveryServer : IServerBase
    {
        #if (!OPCUA_EXCLUDE_FindServers)
        /// <summary>
        /// Invokes the FindServers service.
        /// </summary>
        ResponseHeader FindServers(
            RequestHeader                        requestHeader,
            string                               endpointUrl,
            StringCollection                     localeIds,
            StringCollection                     serverUris,
            out ApplicationDescriptionCollection servers);

        #if (!OPCUA_EXCLUDE_FindServers_ASYNC)
        /// <summary>
        /// Invokes the FindServers service using async Task based request.
        /// </summary>
        Task<FindServersResponse> FindServersAsync(
            RequestHeader     requestHeader,
            string            endpointUrl,
            StringCollection  localeIds,
            StringCollection  serverUris,
            CancellationToken ct);
        #endif
        #endif

        #if (!OPCUA_EXCLUDE_FindServersOnNetwork)
        /// <summary>
        /// Invokes the FindServersOnNetwork service.
        /// </summary>
        ResponseHeader FindServersOnNetwork(
            RequestHeader                 requestHeader,
            uint                          startingRecordId,
            uint                          maxRecordsToReturn,
            StringCollection              serverCapabilityFilter,
            out DateTime                  lastCounterResetTime,
            out ServerOnNetworkCollection servers);

        #if (!OPCUA_EXCLUDE_FindServersOnNetwork_ASYNC)
        /// <summary>
        /// Invokes the FindServersOnNetwork service using async Task based request.
        /// </summary>
        Task<FindServersOnNetworkResponse> FindServersOnNetworkAsync(
            RequestHeader     requestHeader,
            uint              startingRecordId,
            uint              maxRecordsToReturn,
            StringCollection  serverCapabilityFilter,
            CancellationToken ct);
        #endif
        #endif

        #if (!OPCUA_EXCLUDE_GetEndpoints)
        /// <summary>
        /// Invokes the GetEndpoints service.
        /// </summary>
        ResponseHeader GetEndpoints(
            RequestHeader                     requestHeader,
            string                            endpointUrl,
            StringCollection                  localeIds,
            StringCollection                  profileUris,
            out EndpointDescriptionCollection endpoints);

        #if (!OPCUA_EXCLUDE_GetEndpoints_ASYNC)
        /// <summary>
        /// Invokes the GetEndpoints service using async Task based request.
        /// </summary>
        Task<GetEndpointsResponse> GetEndpointsAsync(
            RequestHeader     requestHeader,
            string            endpointUrl,
            StringCollection  localeIds,
            StringCollection  profileUris,
            CancellationToken ct);
        #endif
        #endif

        #if (!OPCUA_EXCLUDE_RegisterServer)
        /// <summary>
        /// Invokes the RegisterServer service.
        /// </summary>
        ResponseHeader RegisterServer(
            RequestHeader    requestHeader,
            RegisteredServer server);

        #if (!OPCUA_EXCLUDE_RegisterServer_ASYNC)
        /// <summary>
        /// Invokes the RegisterServer service using async Task based request.
        /// </summary>
        Task<RegisterServerResponse> RegisterServerAsync(
            RequestHeader     requestHeader,
            RegisteredServer  server,
            CancellationToken ct);
        #endif
        #endif

        #if (!OPCUA_EXCLUDE_RegisterServer2)
        /// <summary>
        /// Invokes the RegisterServer2 service.
        /// </summary>
        ResponseHeader RegisterServer2(
            RequestHeader                requestHeader,
            RegisteredServer             server,
            ExtensionObjectCollection    discoveryConfiguration,
            out StatusCodeCollection     configurationResults,
            out DiagnosticInfoCollection diagnosticInfos);

        #if (!OPCUA_EXCLUDE_RegisterServer2_ASYNC)
        /// <summary>
        /// Invokes the RegisterServer2 service using async Task based request.
        /// </summary>
        Task<RegisterServer2Response> RegisterServer2Async(
            RequestHeader             requestHeader,
            RegisteredServer          server,
            ExtensionObjectCollection discoveryConfiguration,
            CancellationToken         ct);
        #endif
        #endif
    }
    #endregion

    #region DiscoveryServerBase Class
    /// <summary>
    /// A basic implementation of the UA server.
    /// </summary>
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public partial class DiscoveryServerBase : ServerBase, IDiscoveryServer
    {
        #if (!OPCUA_EXCLUDE_FindServers)
        /// <summary>
        /// Invokes the FindServers service.
        /// </summary>
        public virtual ResponseHeader FindServers(
            RequestHeader                        requestHeader,
            string                               endpointUrl,
            StringCollection                     localeIds,
            StringCollection                     serverUris,
            out ApplicationDescriptionCollection servers)
        {
            servers = null;

            ValidateRequest(requestHeader);

            // Insert implementation.

            return CreateResponse(requestHeader, StatusCodes.BadServiceUnsupported);
        }

        #if (!OPCUA_EXCLUDE_FindServers_ASYNC)
            /// <summary>
            /// Invokes the FindServers service using async Task based request.
            /// </summary>
            public virtual async Task<FindServersResponse> FindServersAsync(
                RequestHeader     requestHeader,
                string            endpointUrl,
                StringCollection  localeIds,
                StringCollection  serverUris,
                CancellationToken ct)
            {
                ValidateRequest(requestHeader);

                // Insert implementation.
                await Task.CompletedTask;

                throw new ServiceResultException(StatusCodes.BadServiceUnsupported);
            }
        #endif
        #endif

        #if (!OPCUA_EXCLUDE_FindServersOnNetwork)
        /// <summary>
        /// Invokes the FindServersOnNetwork service.
        /// </summary>
        public virtual ResponseHeader FindServersOnNetwork(
            RequestHeader                 requestHeader,
            uint                          startingRecordId,
            uint                          maxRecordsToReturn,
            StringCollection              serverCapabilityFilter,
            out DateTime                  lastCounterResetTime,
            out ServerOnNetworkCollection servers)
        {
            lastCounterResetTime = DateTime.MinValue;
            servers = null;

            ValidateRequest(requestHeader);

            // Insert implementation.

            return CreateResponse(requestHeader, StatusCodes.BadServiceUnsupported);
        }

        #if (!OPCUA_EXCLUDE_FindServersOnNetwork_ASYNC)
            /// <summary>
            /// Invokes the FindServersOnNetwork service using async Task based request.
            /// </summary>
            public virtual async Task<FindServersOnNetworkResponse> FindServersOnNetworkAsync(
                RequestHeader     requestHeader,
                uint              startingRecordId,
                uint              maxRecordsToReturn,
                StringCollection  serverCapabilityFilter,
                CancellationToken ct)
            {
                ValidateRequest(requestHeader);

                // Insert implementation.
                await Task.CompletedTask;

                throw new ServiceResultException(StatusCodes.BadServiceUnsupported);
            }
        #endif
        #endif

        #if (!OPCUA_EXCLUDE_GetEndpoints)
        /// <summary>
        /// Invokes the GetEndpoints service.
        /// </summary>
        public virtual ResponseHeader GetEndpoints(
            RequestHeader                     requestHeader,
            string                            endpointUrl,
            StringCollection                  localeIds,
            StringCollection                  profileUris,
            out EndpointDescriptionCollection endpoints)
        {
            endpoints = null;

            ValidateRequest(requestHeader);

            // Insert implementation.

            return CreateResponse(requestHeader, StatusCodes.BadServiceUnsupported);
        }

        #if (!OPCUA_EXCLUDE_GetEndpoints_ASYNC)
            /// <summary>
            /// Invokes the GetEndpoints service using async Task based request.
            /// </summary>
            public virtual async Task<GetEndpointsResponse> GetEndpointsAsync(
                RequestHeader     requestHeader,
                string            endpointUrl,
                StringCollection  localeIds,
                StringCollection  profileUris,
                CancellationToken ct)
            {
                ValidateRequest(requestHeader);

                // Insert implementation.
                await Task.CompletedTask;

                throw new ServiceResultException(StatusCodes.BadServiceUnsupported);
            }
        #endif
        #endif

        #if (!OPCUA_EXCLUDE_RegisterServer)
        /// <summary>
        /// Invokes the RegisterServer service.
        /// </summary>
        public virtual ResponseHeader RegisterServer(
            RequestHeader    requestHeader,
            RegisteredServer server)
        {

            ValidateRequest(requestHeader);

            // Insert implementation.

            return CreateResponse(requestHeader, StatusCodes.BadServiceUnsupported);
        }

        #if (!OPCUA_EXCLUDE_RegisterServer_ASYNC)
            /// <summary>
            /// Invokes the RegisterServer service using async Task based request.
            /// </summary>
            public virtual async Task<RegisterServerResponse> RegisterServerAsync(
                RequestHeader     requestHeader,
                RegisteredServer  server,
                CancellationToken ct)
            {
                ValidateRequest(requestHeader);

                // Insert implementation.
                await Task.CompletedTask;

                throw new ServiceResultException(StatusCodes.BadServiceUnsupported);
            }
        #endif
        #endif

        #if (!OPCUA_EXCLUDE_RegisterServer2)
        /// <summary>
        /// Invokes the RegisterServer2 service.
        /// </summary>
        public virtual ResponseHeader RegisterServer2(
            RequestHeader                requestHeader,
            RegisteredServer             server,
            ExtensionObjectCollection    discoveryConfiguration,
            out StatusCodeCollection     configurationResults,
            out DiagnosticInfoCollection diagnosticInfos)
        {
            configurationResults = null;
            diagnosticInfos = null;

            ValidateRequest(requestHeader);

            // Insert implementation.

            return CreateResponse(requestHeader, StatusCodes.BadServiceUnsupported);
        }

        #if (!OPCUA_EXCLUDE_RegisterServer2_ASYNC)
            /// <summary>
            /// Invokes the RegisterServer2 service using async Task based request.
            /// </summary>
            public virtual async Task<RegisterServer2Response> RegisterServer2Async(
                RequestHeader             requestHeader,
                RegisteredServer          server,
                ExtensionObjectCollection discoveryConfiguration,
                CancellationToken         ct)
            {
                ValidateRequest(requestHeader);

                // Insert implementation.
                await Task.CompletedTask;

                throw new ServiceResultException(StatusCodes.BadServiceUnsupported);
            }
        #endif
        #endif
    }
    #endregion
}
