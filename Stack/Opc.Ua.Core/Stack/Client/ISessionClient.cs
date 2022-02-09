/* Copyright (c) 1996-2020 The OPC Foundation. All rights reserved.
   The source code in this file is covered under a dual-license scenario:
     - RCL: for OPC Foundation members in good-standing
     - GPL V2: everybody else
   RCL license terms accompanied with this source code. See http://opcfoundation.org/License/RCL/1.00/
   GNU General Public License as published by the Free Software Foundation;
   version 2 of the License are accompanied with this source code. See http://opcfoundation.org/License/GPLv2
   This source code is distributed in the hope that it will be useful,
   but WITHOUT ANY WARRANTY; without even the implied warranty of
   MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.
*/

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua
{
    /// <summary>
    /// The client side interface for a UA server.
    /// </summary>
    public interface ISessionClient: IClientBase
    {
        #region Public Properties
        /// <summary>
        /// The server assigned identifier for the current session.
        /// </summary>
        /// <value>The session id.</value>
        NodeId SessionId { get; }

        /// <summary>
        /// Whether a session has beed created with the server.
        /// </summary>
        /// <value><c>true</c> if connected; otherwise, <c>false</c>.</value>
        bool Connected { get; }
        #endregion

        #region Client API
        #region CreateSession Methods
#if (!OPCUA_EXCLUDE_CreateSession)
#if (!NET_STANDARD)
        /// <summary>
        /// Invokes the CreateSession service.
        /// </summary>
        ResponseHeader CreateSession(
            RequestHeader requestHeader,
            ApplicationDescription clientDescription,
            string serverUri,
            string endpointUrl,
            string sessionName,
            byte[] clientNonce,
            byte[] clientCertificate,
            double requestedSessionTimeout,
            uint maxResponseMessageSize,
            out NodeId sessionId,
            out NodeId authenticationToken,
            out double revisedSessionTimeout,
            out byte[] serverNonce,
            out byte[] serverCertificate,
            out EndpointDescriptionCollection serverEndpoints,
            out SignedSoftwareCertificateCollection serverSoftwareCertificates,
            out SignatureData serverSignature,
            out uint maxRequestMessageSize);

        /// <summary>
        /// Begins an asynchronous invocation of the CreateSession service.
        /// </summary>
        IAsyncResult BeginCreateSession(
            RequestHeader requestHeader,
            ApplicationDescription clientDescription,
            string serverUri,
            string endpointUrl,
            string sessionName,
            byte[] clientNonce,
            byte[] clientCertificate,
            double requestedSessionTimeout,
            uint maxResponseMessageSize,
            AsyncCallback callback,
            object asyncState);

        /// <summary>
        /// Finishes an asynchronous invocation of the CreateSession service.
        /// </summary>
        ResponseHeader EndCreateSession(
            IAsyncResult result,
            out NodeId sessionId,
            out NodeId authenticationToken,
            out double revisedSessionTimeout,
            out byte[] serverNonce,
            out byte[] serverCertificate,
            out EndpointDescriptionCollection serverEndpoints,
            out SignedSoftwareCertificateCollection serverSoftwareCertificates,
            out SignatureData serverSignature,
            out uint maxRequestMessageSize);
#else  // NET_STANDARD
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

        /// <summary>
        /// Begins an asynchronous invocation of the CreateSession service.
        /// </summary>
        IAsyncResult BeginCreateSession(
            RequestHeader          requestHeader,
            ApplicationDescription clientDescription,
            string                 serverUri,
            string                 endpointUrl,
            string                 sessionName,
            byte[]                 clientNonce,
            byte[]                 clientCertificate,
            double                 requestedSessionTimeout,
            uint                   maxResponseMessageSize,
            AsyncCallback          callback,
            object                 asyncState);

        /// <summary>
        /// Finishes an asynchronous invocation of the CreateSession service.
        /// </summary>
        ResponseHeader EndCreateSession(
            IAsyncResult                            result,
            out NodeId                              sessionId,
            out NodeId                              authenticationToken,
            out double                              revisedSessionTimeout,
            out byte[]                              serverNonce,
            out byte[]                              serverCertificate,
            out EndpointDescriptionCollection       serverEndpoints,
            out SignedSoftwareCertificateCollection serverSoftwareCertificates,
            out SignatureData                       serverSignature,
            out uint                                maxRequestMessageSize);
#endif

#if (NET_STANDARD_ASYNC)
        /// <summary>
        /// Invokes the CreateSession service using Task based request.
        /// </summary>
        Task<CreateSessionResponse> CreateSessionAsync(
            RequestHeader requestHeader,
            ApplicationDescription clientDescription,
            string serverUri,
            string endpointUrl,
            string sessionName,
            byte[] clientNonce,
            byte[] clientCertificate,
            double requestedSessionTimeout,
            uint maxResponseMessageSize,
            CancellationToken ct);
#endif
#endif
        #endregion

        #region ActivateSession Methods
#if (!OPCUA_EXCLUDE_ActivateSession)
#if (!NET_STANDARD)
        /// <summary>
        /// Invokes the ActivateSession service.
        /// </summary>
        ResponseHeader ActivateSession(
            RequestHeader requestHeader,
            SignatureData clientSignature,
            SignedSoftwareCertificateCollection clientSoftwareCertificates,
            StringCollection localeIds,
            ExtensionObject userIdentityToken,
            SignatureData userTokenSignature,
            out byte[] serverNonce,
            out StatusCodeCollection results,
            out DiagnosticInfoCollection diagnosticInfos);

        /// <summary>
        /// Begins an asynchronous invocation of the ActivateSession service.
        /// </summary>
        IAsyncResult BeginActivateSession(
            RequestHeader requestHeader,
            SignatureData clientSignature,
            SignedSoftwareCertificateCollection clientSoftwareCertificates,
            StringCollection localeIds,
            ExtensionObject userIdentityToken,
            SignatureData userTokenSignature,
            AsyncCallback callback,
            object asyncState);

        /// <summary>
        /// Finishes an asynchronous invocation of the ActivateSession service.
        /// </summary>
        ResponseHeader EndActivateSession(
            IAsyncResult result,
            out byte[] serverNonce,
            out StatusCodeCollection results,
            out DiagnosticInfoCollection diagnosticInfos);

#else  // NET_STANDARD
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

        /// <summary>
        /// Begins an asynchronous invocation of the ActivateSession service.
        /// </summary>
        IAsyncResult BeginActivateSession(
            RequestHeader                       requestHeader,
            SignatureData                       clientSignature,
            SignedSoftwareCertificateCollection clientSoftwareCertificates,
            StringCollection                    localeIds,
            ExtensionObject                     userIdentityToken,
            SignatureData                       userTokenSignature,
            AsyncCallback                       callback,
            object                              asyncState);

        /// <summary>
        /// Finishes an asynchronous invocation of the ActivateSession service.
        /// </summary>
        ResponseHeader EndActivateSession(
            IAsyncResult                 result,
            out byte[]                   serverNonce,
            out StatusCodeCollection     results,
            out DiagnosticInfoCollection diagnosticInfos);
#endif

#if (NET_STANDARD_ASYNC)
        /// <summary>
        /// Invokes the ActivateSession service using Task based request.
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
        #endregion

        #region CloseSession Methods
#if (!OPCUA_EXCLUDE_CloseSession)
#if (!NET_STANDARD)
        /// <summary>
        /// Invokes the CloseSession service.
        /// </summary>
        ResponseHeader CloseSession(
            RequestHeader requestHeader,
            bool deleteSubscriptions);

        /// <summary>
        /// Begins an asynchronous invocation of the CloseSession service.
        /// </summary>
        IAsyncResult BeginCloseSession(
            RequestHeader requestHeader,
            bool deleteSubscriptions,
            AsyncCallback callback,
            object asyncState);

        /// <summary>
        /// Finishes an asynchronous invocation of the CloseSession service.
        /// </summary>
        ResponseHeader EndCloseSession(IAsyncResult result);

#else  // NET_STANDARD
        /// <summary>
        /// Invokes the CloseSession service.
        /// </summary>
        ResponseHeader CloseSession(
            RequestHeader requestHeader,
            bool          deleteSubscriptions);

        /// <summary>
        /// Begins an asynchronous invocation of the CloseSession service.
        /// </summary>
        IAsyncResult BeginCloseSession(
            RequestHeader requestHeader,
            bool          deleteSubscriptions,
            AsyncCallback callback,
            object        asyncState);

        /// <summary>
        /// Finishes an asynchronous invocation of the CloseSession service.
        /// </summary>
        ResponseHeader EndCloseSession(
            IAsyncResult result);
#endif

#if (NET_STANDARD_ASYNC)
        /// <summary>
        /// Invokes the CloseSession service using Task based request.
        /// </summary>
        Task<CloseSessionResponse> CloseSessionAsync(
            RequestHeader     requestHeader,
            bool              deleteSubscriptions,
            CancellationToken ct);
#endif
#endif
        #endregion

        #region Cancel Methods
#if (!OPCUA_EXCLUDE_Cancel)
#if (!NET_STANDARD)
        /// <summary>
        /// Invokes the Cancel service.
        /// </summary>
        ResponseHeader Cancel(
            RequestHeader requestHeader,
            uint requestHandle,
            out uint cancelCount);

        /// <summary>
        /// Begins an asynchronous invocation of the Cancel service.
        /// </summary>
        IAsyncResult BeginCancel(
            RequestHeader requestHeader,
            uint requestHandle,
            AsyncCallback callback,
            object asyncState);

        /// <summary>
        /// Finishes an asynchronous invocation of the Cancel service.
        /// </summary>
        ResponseHeader EndCancel(
            IAsyncResult result,
            out uint cancelCount);

#else  // NET_STANDARD
        /// <summary>
        /// Invokes the Cancel service.
        /// </summary>
        ResponseHeader Cancel(
            RequestHeader requestHeader,
            uint          requestHandle,
            out uint      cancelCount);

        /// <summary>
        /// Begins an asynchronous invocation of the Cancel service.
        /// </summary>
        IAsyncResult BeginCancel(
            RequestHeader requestHeader,
            uint          requestHandle,
            AsyncCallback callback,
            object        asyncState);

        /// <summary>
        /// Finishes an asynchronous invocation of the Cancel service.
        /// </summary>
        ResponseHeader EndCancel(
            IAsyncResult result,
            out uint cancelCount);
#endif

#if (NET_STANDARD_ASYNC)
        /// <summary>
        /// Invokes the Cancel service using Task based request.
        /// </summary>
        Task<CancelResponse> CancelAsync(
            RequestHeader     requestHeader,
            uint              requestHandle,
            CancellationToken ct);
#endif
#endif
        #endregion

        #region AddNodes Methods
#if (!OPCUA_EXCLUDE_AddNodes)
#if (!NET_STANDARD)
        /// <summary>
        /// Invokes the AddNodes service.
        /// </summary>
        ResponseHeader AddNodes(
            RequestHeader requestHeader,
            AddNodesItemCollection nodesToAdd,
            out AddNodesResultCollection results,
            out DiagnosticInfoCollection diagnosticInfos);

        /// <summary>
        /// Begins an asynchronous invocation of the AddNodes service.
        /// </summary>
        IAsyncResult BeginAddNodes(
            RequestHeader requestHeader,
            AddNodesItemCollection nodesToAdd,
            AsyncCallback callback,
            object asyncState);

        /// <summary>
        /// Finishes an asynchronous invocation of the AddNodes service.
        /// </summary>
        ResponseHeader EndAddNodes(
            IAsyncResult result,
            out AddNodesResultCollection results,
            out DiagnosticInfoCollection diagnosticInfos);

#else  // NET_STANDARD
        /// <summary>
        /// Invokes the AddNodes service.
        /// </summary>
        ResponseHeader AddNodes(
            RequestHeader                requestHeader,
            AddNodesItemCollection       nodesToAdd,
            out AddNodesResultCollection results,
            out DiagnosticInfoCollection diagnosticInfos);

        /// <summary>
        /// Begins an asynchronous invocation of the AddNodes service.
        /// </summary>
        IAsyncResult BeginAddNodes(
            RequestHeader          requestHeader,
            AddNodesItemCollection nodesToAdd,
            AsyncCallback          callback,
            object                 asyncState);

        /// <summary>
        /// Finishes an asynchronous invocation of the AddNodes service.
        /// </summary>
        ResponseHeader EndAddNodes(
            IAsyncResult                 result,
            out AddNodesResultCollection results,
            out DiagnosticInfoCollection diagnosticInfos);
#endif

#if (NET_STANDARD_ASYNC)
        /// <summary>
        /// Invokes the AddNodes service using Task based request.
        /// </summary>
        Task<AddNodesResponse> AddNodesAsync(
            RequestHeader          requestHeader,
            AddNodesItemCollection nodesToAdd,
            CancellationToken      ct);
#endif
#endif
        #endregion

        #region AddReferences Methods
#if (!OPCUA_EXCLUDE_AddReferences)
#if (!NET_STANDARD)
        /// <summary>
        /// Invokes the AddReferences service.
        /// </summary>
        ResponseHeader AddReferences(
            RequestHeader requestHeader,
            AddReferencesItemCollection referencesToAdd,
            out StatusCodeCollection results,
            out DiagnosticInfoCollection diagnosticInfos);

        /// <summary>
        /// Begins an asynchronous invocation of the AddReferences service.
        /// </summary>
        IAsyncResult BeginAddReferences(
            RequestHeader requestHeader,
            AddReferencesItemCollection referencesToAdd,
            AsyncCallback callback,
            object asyncState);

        /// <summary>
        /// Finishes an asynchronous invocation of the AddReferences service.
        /// </summary>
        ResponseHeader EndAddReferences(
            IAsyncResult result,
            out StatusCodeCollection results,
            out DiagnosticInfoCollection diagnosticInfos);

#else  // NET_STANDARD
        /// <summary>
        /// Invokes the AddReferences service.
        /// </summary>
        ResponseHeader AddReferences(
            RequestHeader                requestHeader,
            AddReferencesItemCollection  referencesToAdd,
            out StatusCodeCollection     results,
            out DiagnosticInfoCollection diagnosticInfos);

        /// <summary>
        /// Begins an asynchronous invocation of the AddReferences service.
        /// </summary>
        IAsyncResult BeginAddReferences(
            RequestHeader               requestHeader,
            AddReferencesItemCollection referencesToAdd,
            AsyncCallback               callback,
            object                      asyncState);

        /// <summary>
        /// Finishes an asynchronous invocation of the AddReferences service.
        /// </summary>
        ResponseHeader EndAddReferences(
            IAsyncResult                 result,
            out StatusCodeCollection     results,
            out DiagnosticInfoCollection diagnosticInfos);
#endif

#if (NET_STANDARD_ASYNC)
        /// <summary>
        /// Invokes the AddReferences service using Task based request.
        /// </summary>
        Task<AddReferencesResponse> AddReferencesAsync(
            RequestHeader               requestHeader,
            AddReferencesItemCollection referencesToAdd,
            CancellationToken           ct);
#endif
#endif
        #endregion

        #region DeleteNodes Methods
#if (!OPCUA_EXCLUDE_DeleteNodes)
#if (!NET_STANDARD)
        /// <summary>
        /// Invokes the DeleteNodes service.
        /// </summary>
        ResponseHeader DeleteNodes(
            RequestHeader requestHeader,
            DeleteNodesItemCollection nodesToDelete,
            out StatusCodeCollection results,
            out DiagnosticInfoCollection diagnosticInfos);

        /// <summary>
        /// Begins an asynchronous invocation of the DeleteNodes service.
        /// </summary>
        IAsyncResult BeginDeleteNodes(
            RequestHeader requestHeader,
            DeleteNodesItemCollection nodesToDelete,
            AsyncCallback callback,
            object asyncState);

        /// <summary>
        /// Finishes an asynchronous invocation of the DeleteNodes service.
        /// </summary>
        ResponseHeader EndDeleteNodes(
            IAsyncResult result,
            out StatusCodeCollection results,
            out DiagnosticInfoCollection diagnosticInfos);

#else  // NET_STANDARD
        /// <summary>
        /// Invokes the DeleteNodes service.
        /// </summary>
        ResponseHeader DeleteNodes(
            RequestHeader                requestHeader,
            DeleteNodesItemCollection    nodesToDelete,
            out StatusCodeCollection     results,
            out DiagnosticInfoCollection diagnosticInfos);

        /// <summary>
        /// Begins an asynchronous invocation of the DeleteNodes service.
        /// </summary>
        IAsyncResult BeginDeleteNodes(
            RequestHeader             requestHeader,
            DeleteNodesItemCollection nodesToDelete,
            AsyncCallback             callback,
            object                    asyncState);

        /// <summary>
        /// Finishes an asynchronous invocation of the DeleteNodes service.
        /// </summary>
        ResponseHeader EndDeleteNodes(
            IAsyncResult                 result,
            out StatusCodeCollection     results,
            out DiagnosticInfoCollection diagnosticInfos);
#endif

#if (NET_STANDARD_ASYNC)
        /// <summary>
        /// Invokes the DeleteNodes service using Task based request.
        /// </summary>
        Task<DeleteNodesResponse> DeleteNodesAsync(
            RequestHeader             requestHeader,
            DeleteNodesItemCollection nodesToDelete,
            CancellationToken         ct);
#endif
#endif
        #endregion

        #region DeleteReferences Methods
#if (!OPCUA_EXCLUDE_DeleteReferences)
#if (!NET_STANDARD)
        /// <summary>
        /// Invokes the DeleteReferences service.
        /// </summary>
        ResponseHeader DeleteReferences(
            RequestHeader requestHeader,
            DeleteReferencesItemCollection referencesToDelete,
            out StatusCodeCollection results,
            out DiagnosticInfoCollection diagnosticInfos);

        /// <summary>
        /// Begins an asynchronous invocation of the DeleteReferences service.
        /// </summary>
        IAsyncResult BeginDeleteReferences(
            RequestHeader requestHeader,
            DeleteReferencesItemCollection referencesToDelete,
            AsyncCallback callback,
            object asyncState);

        /// <summary>
        /// Finishes an asynchronous invocation of the DeleteReferences service.
        /// </summary>
        ResponseHeader EndDeleteReferences(
            IAsyncResult result,
            out StatusCodeCollection results,
            out DiagnosticInfoCollection diagnosticInfos);

#else  // NET_STANDARD
        /// <summary>
        /// Invokes the DeleteReferences service.
        /// </summary>
        ResponseHeader DeleteReferences(
            RequestHeader                  requestHeader,
            DeleteReferencesItemCollection referencesToDelete,
            out StatusCodeCollection       results,
            out DiagnosticInfoCollection   diagnosticInfos);

        /// <summary>
        /// Begins an asynchronous invocation of the DeleteReferences service.
        /// </summary>
        IAsyncResult BeginDeleteReferences(
            RequestHeader                  requestHeader,
            DeleteReferencesItemCollection referencesToDelete,
            AsyncCallback                  callback,
            object                         asyncState);

        /// <summary>
        /// Finishes an asynchronous invocation of the DeleteReferences service.
        /// </summary>
        ResponseHeader EndDeleteReferences(
            IAsyncResult                 result,
            out StatusCodeCollection     results,
            out DiagnosticInfoCollection diagnosticInfos);
#endif

#if (NET_STANDARD_ASYNC)
        /// <summary>
        /// Invokes the DeleteReferences service using Task based request.
        /// </summary>
        Task<DeleteReferencesResponse> DeleteReferencesAsync(
            RequestHeader                  requestHeader,
            DeleteReferencesItemCollection referencesToDelete,
            CancellationToken              ct);
#endif
#endif
        #endregion

        #region Browse Methods
#if (!OPCUA_EXCLUDE_Browse)
#if (!NET_STANDARD)
        /// <summary>
        /// Invokes the Browse service.
        /// </summary>
        ResponseHeader Browse(
            RequestHeader requestHeader,
            ViewDescription view,
            uint requestedMaxReferencesPerNode,
            BrowseDescriptionCollection nodesToBrowse,
            out BrowseResultCollection results,
            out DiagnosticInfoCollection diagnosticInfos);

        /// <summary>
        /// Begins an asynchronous invocation of the Browse service.
        /// </summary>
        IAsyncResult BeginBrowse(
            RequestHeader requestHeader,
            ViewDescription view,
            uint requestedMaxReferencesPerNode,
            BrowseDescriptionCollection nodesToBrowse,
            AsyncCallback callback,
            object asyncState);

        /// <summary>
        /// Finishes an asynchronous invocation of the Browse service.
        /// </summary>
        ResponseHeader EndBrowse(
            IAsyncResult result,
            out BrowseResultCollection results,
            out DiagnosticInfoCollection diagnosticInfos);

#else  // NET_STANDARD
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

        /// <summary>
        /// Begins an asynchronous invocation of the Browse service.
        /// </summary>
        IAsyncResult BeginBrowse(
            RequestHeader               requestHeader,
            ViewDescription             view,
            uint                        requestedMaxReferencesPerNode,
            BrowseDescriptionCollection nodesToBrowse,
            AsyncCallback               callback,
            object                      asyncState);

        /// <summary>
        /// Finishes an asynchronous invocation of the Browse service.
        /// </summary>
        ResponseHeader EndBrowse(
            IAsyncResult                 result,
            out BrowseResultCollection   results,
            out DiagnosticInfoCollection diagnosticInfos);
#endif

#if (NET_STANDARD_ASYNC)
        /// <summary>
        /// Invokes the Browse service using Task based request.
        /// </summary>
        Task<BrowseResponse> BrowseAsync(
            RequestHeader               requestHeader,
            ViewDescription             view,
            uint                        requestedMaxReferencesPerNode,
            BrowseDescriptionCollection nodesToBrowse,
            CancellationToken           ct);
#endif
#endif
        #endregion

        #region BrowseNext Methods
#if (!OPCUA_EXCLUDE_BrowseNext)
#if (!NET_STANDARD)
        /// <summary>
        /// Invokes the BrowseNext service.
        /// </summary>
        ResponseHeader BrowseNext(
            RequestHeader requestHeader,
            bool releaseContinuationPoints,
            ByteStringCollection continuationPoints,
            out BrowseResultCollection results,
            out DiagnosticInfoCollection diagnosticInfos);

        /// <summary>
        /// Begins an asynchronous invocation of the BrowseNext service.
        /// </summary>
        IAsyncResult BeginBrowseNext(
            RequestHeader requestHeader,
            bool releaseContinuationPoints,
            ByteStringCollection continuationPoints,
            AsyncCallback callback,
            object asyncState);

        /// <summary>
        /// Finishes an asynchronous invocation of the BrowseNext service.
        /// </summary>
        ResponseHeader EndBrowseNext(
            IAsyncResult result,
            out BrowseResultCollection results,
            out DiagnosticInfoCollection diagnosticInfos);

#else  // NET_STANDARD
        /// <summary>
        /// Invokes the BrowseNext service.
        /// </summary>
        ResponseHeader BrowseNext(
            RequestHeader                requestHeader,
            bool                         releaseContinuationPoints,
            ByteStringCollection         continuationPoints,
            out BrowseResultCollection   results,
            out DiagnosticInfoCollection diagnosticInfos);

        /// <summary>
        /// Begins an asynchronous invocation of the BrowseNext service.
        /// </summary>
        IAsyncResult BeginBrowseNext(
            RequestHeader        requestHeader,
            bool                 releaseContinuationPoints,
            ByteStringCollection continuationPoints,
            AsyncCallback        callback,
            object               asyncState);

        /// <summary>
        /// Finishes an asynchronous invocation of the BrowseNext service.
        /// </summary>
        ResponseHeader EndBrowseNext(
            IAsyncResult                 result,
            out BrowseResultCollection   results,
            out DiagnosticInfoCollection diagnosticInfos);
#endif

#if (NET_STANDARD_ASYNC)
        /// <summary>
        /// Invokes the BrowseNext service using Task based request.
        /// </summary>
        Task<BrowseNextResponse> BrowseNextAsync(
            RequestHeader        requestHeader,
            bool                 releaseContinuationPoints,
            ByteStringCollection continuationPoints,
            CancellationToken    ct);
#endif
#endif
        #endregion

        #region TranslateBrowsePathsToNodeIds Methods
#if (!OPCUA_EXCLUDE_TranslateBrowsePathsToNodeIds)
#if (!NET_STANDARD)
        /// <summary>
        /// Invokes the TranslateBrowsePathsToNodeIds service.
        /// </summary>
        ResponseHeader TranslateBrowsePathsToNodeIds(
            RequestHeader requestHeader,
            BrowsePathCollection browsePaths,
            out BrowsePathResultCollection results,
            out DiagnosticInfoCollection diagnosticInfos);

        /// <summary>
        /// Begins an asynchronous invocation of the TranslateBrowsePathsToNodeIds service.
        /// </summary>
        IAsyncResult BeginTranslateBrowsePathsToNodeIds(
            RequestHeader requestHeader,
            BrowsePathCollection browsePaths,
            AsyncCallback callback,
            object asyncState);

        /// <summary>
        /// Finishes an asynchronous invocation of the TranslateBrowsePathsToNodeIds service.
        /// </summary>
        ResponseHeader EndTranslateBrowsePathsToNodeIds(
            IAsyncResult result,
            out BrowsePathResultCollection results,
            out DiagnosticInfoCollection diagnosticInfos);

#else  // NET_STANDARD
        /// <summary>
        /// Invokes the TranslateBrowsePathsToNodeIds service.
        /// </summary>
        ResponseHeader TranslateBrowsePathsToNodeIds(
            RequestHeader                  requestHeader,
            BrowsePathCollection           browsePaths,
            out BrowsePathResultCollection results,
            out DiagnosticInfoCollection   diagnosticInfos);

        /// <summary>
        /// Begins an asynchronous invocation of the TranslateBrowsePathsToNodeIds service.
        /// </summary>
        IAsyncResult BeginTranslateBrowsePathsToNodeIds(
            RequestHeader requestHeader,
            BrowsePathCollection browsePaths,
            AsyncCallback callback,
            object asyncState);

        /// <summary>
        /// Finishes an asynchronous invocation of the TranslateBrowsePathsToNodeIds service.
        /// </summary>
        ResponseHeader EndTranslateBrowsePathsToNodeIds(
            IAsyncResult                   result,
            out BrowsePathResultCollection results,
            out DiagnosticInfoCollection   diagnosticInfos);
#endif

#if (NET_STANDARD_ASYNC)
        /// <summary>
        /// Invokes the TranslateBrowsePathsToNodeIds service using Task based request.
        /// </summary>
        Task<TranslateBrowsePathsToNodeIdsResponse> TranslateBrowsePathsToNodeIdsAsync(
            RequestHeader        requestHeader,
            BrowsePathCollection browsePaths,
            CancellationToken    ct);
#endif
#endif
        #endregion

        #region RegisterNodes Methods
#if (!OPCUA_EXCLUDE_RegisterNodes)
#if (!NET_STANDARD)
        /// <summary>
        /// Invokes the RegisterNodes service.
        /// </summary>
        ResponseHeader RegisterNodes(
            RequestHeader requestHeader,
            NodeIdCollection nodesToRegister,
            out NodeIdCollection registeredNodeIds);

        /// <summary>
        /// Begins an asynchronous invocation of the RegisterNodes service.
        /// </summary>
        IAsyncResult BeginRegisterNodes(
            RequestHeader requestHeader,
            NodeIdCollection nodesToRegister,
            AsyncCallback callback,
            object asyncState);

        /// <summary>
        /// Finishes an asynchronous invocation of the RegisterNodes service.
        /// </summary>
        ResponseHeader EndRegisterNodes(
            IAsyncResult result,
            out NodeIdCollection registeredNodeIds);

#else  // NET_STANDARD
        /// <summary>
        /// Invokes the RegisterNodes service.
        /// </summary>
        ResponseHeader RegisterNodes(
            RequestHeader        requestHeader,
            NodeIdCollection     nodesToRegister,
            out NodeIdCollection registeredNodeIds);

        /// <summary>
        /// Begins an asynchronous invocation of the RegisterNodes service.
        /// </summary>
        IAsyncResult BeginRegisterNodes(
            RequestHeader    requestHeader,
            NodeIdCollection nodesToRegister,
            AsyncCallback    callback,
            object           asyncState);

        /// <summary>
        /// Finishes an asynchronous invocation of the RegisterNodes service.
        /// </summary>
        ResponseHeader EndRegisterNodes(
            IAsyncResult         result,
            out NodeIdCollection registeredNodeIds);
#endif

#if (NET_STANDARD_ASYNC)
        /// <summary>
        /// Invokes the RegisterNodes service using Task based request.
        /// </summary>
        Task<RegisterNodesResponse> RegisterNodesAsync(
            RequestHeader     requestHeader,
            NodeIdCollection  nodesToRegister,
            CancellationToken ct);
#endif
#endif
        #endregion

        #region UnregisterNodes Methods
#if (!OPCUA_EXCLUDE_UnregisterNodes)
#if (!NET_STANDARD)
        /// <summary>
        /// Invokes the UnregisterNodes service.
        /// </summary>
        ResponseHeader UnregisterNodes(
            RequestHeader requestHeader,
            NodeIdCollection nodesToUnregister);

        /// <summary>
        /// Begins an asynchronous invocation of the UnregisterNodes service.
        /// </summary>
        IAsyncResult BeginUnregisterNodes(
            RequestHeader requestHeader,
            NodeIdCollection nodesToUnregister,
            AsyncCallback callback,
            object asyncState);

        /// <summary>
        /// Finishes an asynchronous invocation of the UnregisterNodes service.
        /// </summary>
        ResponseHeader EndUnregisterNodes(
            IAsyncResult result);

#else  // NET_STANDARD
        /// <summary>
        /// Invokes the UnregisterNodes service.
        /// </summary>
        ResponseHeader UnregisterNodes(
            RequestHeader    requestHeader,
            NodeIdCollection nodesToUnregister);

        /// <summary>
        /// Begins an asynchronous invocation of the UnregisterNodes service.
        /// </summary>
        IAsyncResult BeginUnregisterNodes(
            RequestHeader    requestHeader,
            NodeIdCollection nodesToUnregister,
            AsyncCallback    callback,
            object           asyncState);

        /// <summary>
        /// Finishes an asynchronous invocation of the UnregisterNodes service.
        /// </summary>
        ResponseHeader EndUnregisterNodes(
            IAsyncResult result);
#endif

#if (NET_STANDARD_ASYNC)
        /// <summary>
        /// Invokes the UnregisterNodes service using Task based request.
        /// </summary>
        Task<UnregisterNodesResponse> UnregisterNodesAsync(
            RequestHeader     requestHeader,
            NodeIdCollection  nodesToUnregister,
            CancellationToken ct);
#endif
#endif
        #endregion

        #region QueryFirst Methods
#if (!OPCUA_EXCLUDE_QueryFirst)
#if (!NET_STANDARD)
        /// <summary>
        /// Invokes the QueryFirst service.
        /// </summary>
        ResponseHeader QueryFirst(
            RequestHeader requestHeader,
            ViewDescription view,
            NodeTypeDescriptionCollection nodeTypes,
            ContentFilter filter,
            uint maxDataSetsToReturn,
            uint maxReferencesToReturn,
            out QueryDataSetCollection queryDataSets,
            out byte[] continuationPoint,
            out ParsingResultCollection parsingResults,
            out DiagnosticInfoCollection diagnosticInfos,
            out ContentFilterResult filterResult);

        /// <summary>
        /// Begins an asynchronous invocation of the QueryFirst service.
        /// </summary>
        IAsyncResult BeginQueryFirst(
            RequestHeader requestHeader,
            ViewDescription view,
            NodeTypeDescriptionCollection nodeTypes,
            ContentFilter filter,
            uint maxDataSetsToReturn,
            uint maxReferencesToReturn,
            AsyncCallback callback,
            object asyncState);

        /// <summary>
        /// Finishes an asynchronous invocation of the QueryFirst service.
        /// </summary>
        ResponseHeader EndQueryFirst(
            IAsyncResult result,
            out QueryDataSetCollection queryDataSets,
            out byte[] continuationPoint,
            out ParsingResultCollection parsingResults,
            out DiagnosticInfoCollection diagnosticInfos,
            out ContentFilterResult filterResult);

#else  // NET_STANDARD
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

        /// <summary>
        /// Begins an asynchronous invocation of the QueryFirst service.
        /// </summary>
        IAsyncResult BeginQueryFirst(
            RequestHeader                 requestHeader,
            ViewDescription               view,
            NodeTypeDescriptionCollection nodeTypes,
            ContentFilter                 filter,
            uint                          maxDataSetsToReturn,
            uint                          maxReferencesToReturn,
            AsyncCallback                 callback,
            object                        asyncState);

        /// <summary>
        /// Finishes an asynchronous invocation of the QueryFirst service.
        /// </summary>
        ResponseHeader EndQueryFirst(
            IAsyncResult                 result,
            out QueryDataSetCollection   queryDataSets,
            out byte[]                   continuationPoint,
            out ParsingResultCollection  parsingResults,
            out DiagnosticInfoCollection diagnosticInfos,
            out ContentFilterResult      filterResult);
#endif

#if (NET_STANDARD_ASYNC)
        /// <summary>
        /// Invokes the QueryFirst service using Task based request.
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
        #endregion

        #region QueryNext Methods
#if (!OPCUA_EXCLUDE_QueryNext)
#if (!NET_STANDARD)
        /// <summary>
        /// Invokes the QueryNext service.
        /// </summary>
        ResponseHeader QueryNext(
            RequestHeader requestHeader,
            bool releaseContinuationPoint,
            byte[] continuationPoint,
            out QueryDataSetCollection queryDataSets,
            out byte[] revisedContinuationPoint);

        /// <summary>
        /// Begins an asynchronous invocation of the QueryNext service.
        /// </summary>
        IAsyncResult BeginQueryNext(
            RequestHeader requestHeader,
            bool releaseContinuationPoint,
            byte[] continuationPoint,
            AsyncCallback callback,
            object asyncState);

        /// <summary>
        /// Finishes an asynchronous invocation of the QueryNext service.
        /// </summary>
        ResponseHeader EndQueryNext(
            IAsyncResult result,
            out QueryDataSetCollection queryDataSets,
            out byte[] revisedContinuationPoint);

#else  // NET_STANDARD
        /// <summary>
        /// Invokes the QueryNext service.
        /// </summary>
        ResponseHeader QueryNext(
            RequestHeader              requestHeader,
            bool                       releaseContinuationPoint,
            byte[]                     continuationPoint,
            out QueryDataSetCollection queryDataSets,
            out byte[]                 revisedContinuationPoint);

        /// <summary>
        /// Begins an asynchronous invocation of the QueryNext service.
        /// </summary>
        IAsyncResult BeginQueryNext(
            RequestHeader requestHeader,
            bool          releaseContinuationPoint,
            byte[]        continuationPoint,
            AsyncCallback callback,
            object        asyncState);

        /// <summary>
        /// Finishes an asynchronous invocation of the QueryNext service.
        /// </summary>
        ResponseHeader EndQueryNext(
            IAsyncResult               result,
            out QueryDataSetCollection queryDataSets,
            out byte[]                 revisedContinuationPoint);
#endif

#if (NET_STANDARD_ASYNC)
        /// <summary>
        /// Invokes the QueryNext service using Task based request.
        /// </summary>
        Task<QueryNextResponse> QueryNextAsync(
            RequestHeader     requestHeader,
            bool              releaseContinuationPoint,
            byte[]            continuationPoint,
            CancellationToken ct);
#endif
#endif
        #endregion

        #region Read Methods
#if (!OPCUA_EXCLUDE_Read)
#if (!NET_STANDARD)
        /// <summary>
        /// Invokes the Read service.
        /// </summary>
        ResponseHeader Read(
            RequestHeader requestHeader,
            double maxAge,
            TimestampsToReturn timestampsToReturn,
            ReadValueIdCollection nodesToRead,
            out DataValueCollection results,
            out DiagnosticInfoCollection diagnosticInfos);

        /// <summary>
        /// Begins an asynchronous invocation of the Read service.
        /// </summary>
        IAsyncResult BeginRead(
            RequestHeader requestHeader,
            double maxAge,
            TimestampsToReturn timestampsToReturn,
            ReadValueIdCollection nodesToRead,
            AsyncCallback callback,
            object asyncState);

        /// <summary>
        /// Finishes an asynchronous invocation of the Read service.
        /// </summary>
        ResponseHeader EndRead(
            IAsyncResult result,
            out DataValueCollection results,
            out DiagnosticInfoCollection diagnosticInfos);

#else  // NET_STANDARD
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

        /// <summary>
        /// Begins an asynchronous invocation of the Read service.
        /// </summary>
        IAsyncResult BeginRead(
            RequestHeader         requestHeader,
            double                maxAge,
            TimestampsToReturn    timestampsToReturn,
            ReadValueIdCollection nodesToRead,
            AsyncCallback         callback,
            object                asyncState);

        /// <summary>
        /// Finishes an asynchronous invocation of the Read service.
        /// </summary>
        ResponseHeader EndRead(
            IAsyncResult                 result,
            out DataValueCollection      results,
            out DiagnosticInfoCollection diagnosticInfos);
#endif

#if (NET_STANDARD_ASYNC)
        /// <summary>
        /// Invokes the Read service using Task based request.
        /// </summary>
        Task<ReadResponse> ReadAsync(
            RequestHeader         requestHeader,
            double                maxAge,
            TimestampsToReturn    timestampsToReturn,
            ReadValueIdCollection nodesToRead,
            CancellationToken     ct);
#endif
#endif
        #endregion

        #region HistoryRead Methods
#if (!OPCUA_EXCLUDE_HistoryRead)
#if (!NET_STANDARD)
        /// <summary>
        /// Invokes the HistoryRead service.
        /// </summary>
        ResponseHeader HistoryRead(
            RequestHeader requestHeader,
            ExtensionObject historyReadDetails,
            TimestampsToReturn timestampsToReturn,
            bool releaseContinuationPoints,
            HistoryReadValueIdCollection nodesToRead,
            out HistoryReadResultCollection results,
            out DiagnosticInfoCollection diagnosticInfos);

        /// <summary>
        /// Begins an asynchronous invocation of the HistoryRead service.
        /// </summary>
        IAsyncResult BeginHistoryRead(
            RequestHeader requestHeader,
            ExtensionObject historyReadDetails,
            TimestampsToReturn timestampsToReturn,
            bool releaseContinuationPoints,
            HistoryReadValueIdCollection nodesToRead,
            AsyncCallback callback,
            object asyncState);

        /// <summary>
        /// Finishes an asynchronous invocation of the HistoryRead service.
        /// </summary>
        ResponseHeader EndHistoryRead(
            IAsyncResult result,
            out HistoryReadResultCollection results,
            out DiagnosticInfoCollection diagnosticInfos);

#else  // NET_STANDARD
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

        /// <summary>
        /// Begins an asynchronous invocation of the HistoryRead service.
        /// </summary>
        IAsyncResult BeginHistoryRead(
            RequestHeader                requestHeader,
            ExtensionObject              historyReadDetails,
            TimestampsToReturn           timestampsToReturn,
            bool                         releaseContinuationPoints,
            HistoryReadValueIdCollection nodesToRead,
            AsyncCallback                callback,
            object                       asyncState);

        /// <summary>
        /// Finishes an asynchronous invocation of the HistoryRead service.
        /// </summary>
        ResponseHeader EndHistoryRead(
            IAsyncResult                    result,
            out HistoryReadResultCollection results,
            out DiagnosticInfoCollection    diagnosticInfos);
#endif

#if (NET_STANDARD_ASYNC)
        /// <summary>
        /// Invokes the HistoryRead service using Task based request.
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
        #endregion

        #region Write Methods
#if (!OPCUA_EXCLUDE_Write)
#if (!NET_STANDARD)
        /// <summary>
        /// Invokes the Write service.
        /// </summary>
        ResponseHeader Write(
            RequestHeader requestHeader,
            WriteValueCollection nodesToWrite,
            out StatusCodeCollection results,
            out DiagnosticInfoCollection diagnosticInfos);

        /// <summary>
        /// Begins an asynchronous invocation of the Write service.
        /// </summary>
        IAsyncResult BeginWrite(
            RequestHeader requestHeader,
            WriteValueCollection nodesToWrite,
            AsyncCallback callback,
            object asyncState);

        /// <summary>
        /// Finishes an asynchronous invocation of the Write service.
        /// </summary>
        ResponseHeader EndWrite(
            IAsyncResult result,
            out StatusCodeCollection results,
            out DiagnosticInfoCollection diagnosticInfos);

#else  // NET_STANDARD
        /// <summary>
        /// Invokes the Write service.
        /// </summary>
        ResponseHeader Write(
            RequestHeader                requestHeader,
            WriteValueCollection         nodesToWrite,
            out StatusCodeCollection     results,
            out DiagnosticInfoCollection diagnosticInfos);

        /// <summary>
        /// Begins an asynchronous invocation of the Write service.
        /// </summary>
        IAsyncResult BeginWrite(
            RequestHeader        requestHeader,
            WriteValueCollection nodesToWrite,
            AsyncCallback        callback,
            object               asyncState);

        /// <summary>
        /// Finishes an asynchronous invocation of the Write service.
        /// </summary>
        ResponseHeader EndWrite(
            IAsyncResult                 result,
            out StatusCodeCollection     results,
            out DiagnosticInfoCollection diagnosticInfos);
#endif

#if (NET_STANDARD_ASYNC)
        /// <summary>
        /// Invokes the Write service using Task based request.
        /// </summary>
        Task<WriteResponse> WriteAsync(
            RequestHeader        requestHeader,
            WriteValueCollection nodesToWrite,
            CancellationToken    ct);
#endif
#endif
        #endregion

        #region HistoryUpdate Methods
#if (!OPCUA_EXCLUDE_HistoryUpdate)
#if (!NET_STANDARD)
        /// <summary>
        /// Invokes the HistoryUpdate service.
        /// </summary>
        ResponseHeader HistoryUpdate(
            RequestHeader requestHeader,
            ExtensionObjectCollection historyUpdateDetails,
            out HistoryUpdateResultCollection results,
            out DiagnosticInfoCollection diagnosticInfos);

        /// <summary>
        /// Begins an asynchronous invocation of the HistoryUpdate service.
        /// </summary>
        IAsyncResult BeginHistoryUpdate(
            RequestHeader requestHeader,
            ExtensionObjectCollection historyUpdateDetails,
            AsyncCallback callback,
            object asyncState);

        /// <summary>
        /// Finishes an asynchronous invocation of the HistoryUpdate service.
        /// </summary>
        ResponseHeader EndHistoryUpdate(
            IAsyncResult result,
            out HistoryUpdateResultCollection results,
            out DiagnosticInfoCollection diagnosticInfos);

#else  // NET_STANDARD
        /// <summary>
        /// Invokes the HistoryUpdate service.
        /// </summary>
        ResponseHeader HistoryUpdate(
            RequestHeader                     requestHeader,
            ExtensionObjectCollection         historyUpdateDetails,
            out HistoryUpdateResultCollection results,
            out DiagnosticInfoCollection      diagnosticInfos);

        /// <summary>
        /// Begins an asynchronous invocation of the HistoryUpdate service.
        /// </summary>
        IAsyncResult BeginHistoryUpdate(
            RequestHeader             requestHeader,
            ExtensionObjectCollection historyUpdateDetails,
            AsyncCallback             callback,
            object                    asyncState);

        /// <summary>
        /// Finishes an asynchronous invocation of the HistoryUpdate service.
        /// </summary>
        ResponseHeader EndHistoryUpdate(
            IAsyncResult                      result,
            out HistoryUpdateResultCollection results,
            out DiagnosticInfoCollection      diagnosticInfos);
#endif

#if (NET_STANDARD_ASYNC)
        /// <summary>
        /// Invokes the HistoryUpdate service using Task based request.
        /// </summary>
        Task<HistoryUpdateResponse> HistoryUpdateAsync(
            RequestHeader             requestHeader,
            ExtensionObjectCollection historyUpdateDetails,
            CancellationToken         ct);
#endif
#endif
        #endregion

        #region Call Methods
#if (!OPCUA_EXCLUDE_Call)
#if (!NET_STANDARD)
        /// <summary>
        /// Invokes the Call service.
        /// </summary>
        ResponseHeader Call(
            RequestHeader requestHeader,
            CallMethodRequestCollection methodsToCall,
            out CallMethodResultCollection results,
            out DiagnosticInfoCollection diagnosticInfos);

        /// <summary>
        /// Begins an asynchronous invocation of the Call service.
        /// </summary>
        IAsyncResult BeginCall(
            RequestHeader requestHeader,
            CallMethodRequestCollection methodsToCall,
            AsyncCallback callback,
            object asyncState);

        /// <summary>
        /// Finishes an asynchronous invocation of the Call service.
        /// </summary>
        ResponseHeader EndCall(
            IAsyncResult result,
            out CallMethodResultCollection results,
            out DiagnosticInfoCollection diagnosticInfos);

#else  // NET_STANDARD
        /// <summary>
        /// Invokes the Call service.
        /// </summary>
        ResponseHeader Call(
            RequestHeader                  requestHeader,
            CallMethodRequestCollection    methodsToCall,
            out CallMethodResultCollection results,
            out DiagnosticInfoCollection   diagnosticInfos);

        /// <summary>
        /// Begins an asynchronous invocation of the Call service.
        /// </summary>
        IAsyncResult BeginCall(
            RequestHeader               requestHeader,
            CallMethodRequestCollection methodsToCall,
            AsyncCallback               callback,
            object                      asyncState);

        /// <summary>
        /// Finishes an asynchronous invocation of the Call service.
        /// </summary>
        ResponseHeader EndCall(
            IAsyncResult                   result,
            out CallMethodResultCollection results,
            out DiagnosticInfoCollection   diagnosticInfos);
#endif

#if (NET_STANDARD_ASYNC)
        /// <summary>
        /// Invokes the Call service using Task based request.
        /// </summary>
        Task<CallResponse> CallAsync(
            RequestHeader               requestHeader,
            CallMethodRequestCollection methodsToCall,
            CancellationToken           ct);

#endif
#endif
        #endregion

        #region CreateMonitoredItems Methods
#if (!OPCUA_EXCLUDE_CreateMonitoredItems)
#if (!NET_STANDARD)
        /// <summary>
        /// Invokes the CreateMonitoredItems service.
        /// </summary>
        ResponseHeader CreateMonitoredItems(
            RequestHeader requestHeader,
            uint subscriptionId,
            TimestampsToReturn timestampsToReturn,
            MonitoredItemCreateRequestCollection itemsToCreate,
            out MonitoredItemCreateResultCollection results,
            out DiagnosticInfoCollection diagnosticInfos);

        /// <summary>
        /// Begins an asynchronous invocation of the CreateMonitoredItems service.
        /// </summary>
        IAsyncResult BeginCreateMonitoredItems(
            RequestHeader requestHeader,
            uint subscriptionId,
            TimestampsToReturn timestampsToReturn,
            MonitoredItemCreateRequestCollection itemsToCreate,
            AsyncCallback callback,
            object asyncState);

        /// <summary>
        /// Finishes an asynchronous invocation of the CreateMonitoredItems service.
        /// </summary>
        ResponseHeader EndCreateMonitoredItems(
            IAsyncResult result,
            out MonitoredItemCreateResultCollection results,
            out DiagnosticInfoCollection diagnosticInfos);

#else  // NET_STANDARD
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

        /// <summary>
        /// Begins an asynchronous invocation of the CreateMonitoredItems service.
        /// </summary>
        IAsyncResult BeginCreateMonitoredItems(
            RequestHeader                        requestHeader,
            uint                                 subscriptionId,
            TimestampsToReturn                   timestampsToReturn,
            MonitoredItemCreateRequestCollection itemsToCreate,
            AsyncCallback                        callback,
            object                               asyncState);

        /// <summary>
        /// Finishes an asynchronous invocation of the CreateMonitoredItems service.
        /// </summary>
        ResponseHeader EndCreateMonitoredItems(
            IAsyncResult                            result,
            out MonitoredItemCreateResultCollection results,
            out DiagnosticInfoCollection            diagnosticInfos);
#endif

#if (NET_STANDARD_ASYNC)
        /// <summary>
        /// Invokes the CreateMonitoredItems service using Task based request.
        /// </summary>
        Task<CreateMonitoredItemsResponse> CreateMonitoredItemsAsync(
            RequestHeader                        requestHeader,
            uint                                 subscriptionId,
            TimestampsToReturn                   timestampsToReturn,
            MonitoredItemCreateRequestCollection itemsToCreate,
            CancellationToken                    ct);
#endif
#endif
        #endregion

        #region ModifyMonitoredItems Methods
#if (!OPCUA_EXCLUDE_ModifyMonitoredItems)
#if (!NET_STANDARD)
        /// <summary>
        /// Invokes the ModifyMonitoredItems service.
        /// </summary>
        ResponseHeader ModifyMonitoredItems(
            RequestHeader requestHeader,
            uint subscriptionId,
            TimestampsToReturn timestampsToReturn,
            MonitoredItemModifyRequestCollection itemsToModify,
            out MonitoredItemModifyResultCollection results,
            out DiagnosticInfoCollection diagnosticInfos);

        /// <summary>
        /// Begins an asynchronous invocation of the ModifyMonitoredItems service.
        /// </summary>
        IAsyncResult BeginModifyMonitoredItems(
            RequestHeader requestHeader,
            uint subscriptionId,
            TimestampsToReturn timestampsToReturn,
            MonitoredItemModifyRequestCollection itemsToModify,
            AsyncCallback callback,
            object asyncState);

        /// <summary>
        /// Finishes an asynchronous invocation of the ModifyMonitoredItems service.
        /// </summary>
        ResponseHeader EndModifyMonitoredItems(
            IAsyncResult result,
            out MonitoredItemModifyResultCollection results,
            out DiagnosticInfoCollection diagnosticInfos);

#else  // NET_STANDARD
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

        /// <summary>
        /// Begins an asynchronous invocation of the ModifyMonitoredItems service.
        /// </summary>
        IAsyncResult BeginModifyMonitoredItems(
            RequestHeader                        requestHeader,
            uint                                 subscriptionId,
            TimestampsToReturn                   timestampsToReturn,
            MonitoredItemModifyRequestCollection itemsToModify,
            AsyncCallback                        callback,
            object                               asyncState);

        /// <summary>
        /// Finishes an asynchronous invocation of the ModifyMonitoredItems service.
        /// </summary>
        ResponseHeader EndModifyMonitoredItems(
            IAsyncResult                            result,
            out MonitoredItemModifyResultCollection results,
            out DiagnosticInfoCollection            diagnosticInfos);

#endif

#if (NET_STANDARD_ASYNC)
        /// <summary>
        /// Invokes the ModifyMonitoredItems service using Task based request.
        /// </summary>
        Task<ModifyMonitoredItemsResponse> ModifyMonitoredItemsAsync(
            RequestHeader                        requestHeader,
            uint                                 subscriptionId,
            TimestampsToReturn                   timestampsToReturn,
            MonitoredItemModifyRequestCollection itemsToModify,
            CancellationToken                    ct);
#endif
#endif
        #endregion

        #region SetMonitoringMode Methods
#if (!OPCUA_EXCLUDE_SetMonitoringMode)
#if (!NET_STANDARD)
        /// <summary>
        /// Invokes the SetMonitoringMode service.
        /// </summary>
        ResponseHeader SetMonitoringMode(
            RequestHeader requestHeader,
            uint subscriptionId,
            MonitoringMode monitoringMode,
            UInt32Collection monitoredItemIds,
            out StatusCodeCollection results,
            out DiagnosticInfoCollection diagnosticInfos);

        /// <summary>
        /// Begins an asynchronous invocation of the SetMonitoringMode service.
        /// </summary>
        IAsyncResult BeginSetMonitoringMode(
            RequestHeader requestHeader,
            uint subscriptionId,
            MonitoringMode monitoringMode,
            UInt32Collection monitoredItemIds,
            AsyncCallback callback,
            object asyncState);

        /// <summary>
        /// Finishes an asynchronous invocation of the SetMonitoringMode service.
        /// </summary>
        ResponseHeader EndSetMonitoringMode(
            IAsyncResult result,
            out StatusCodeCollection results,
            out DiagnosticInfoCollection diagnosticInfos);

#else  // NET_STANDARD
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

        /// <summary>
        /// Begins an asynchronous invocation of the SetMonitoringMode service.
        /// </summary>
        IAsyncResult BeginSetMonitoringMode(
            RequestHeader    requestHeader,
            uint             subscriptionId,
            MonitoringMode   monitoringMode,
            UInt32Collection monitoredItemIds,
            AsyncCallback    callback,
            object           asyncState);

        /// <summary>
        /// Finishes an asynchronous invocation of the SetMonitoringMode service.
        /// </summary>
        ResponseHeader EndSetMonitoringMode(
            IAsyncResult                 result,
            out StatusCodeCollection     results,
            out DiagnosticInfoCollection diagnosticInfos);
#endif

#if (NET_STANDARD_ASYNC)
        /// <summary>
        /// Invokes the SetMonitoringMode service using Task based request.
        /// </summary>
        Task<SetMonitoringModeResponse> SetMonitoringModeAsync(
            RequestHeader     requestHeader,
            uint              subscriptionId,
            MonitoringMode    monitoringMode,
            UInt32Collection  monitoredItemIds,
            CancellationToken ct);
#endif
#endif
        #endregion

        #region SetTriggering Methods
#if (!OPCUA_EXCLUDE_SetTriggering)
#if (!NET_STANDARD)
        /// <summary>
        /// Invokes the SetTriggering service.
        /// </summary>
        ResponseHeader SetTriggering(
            RequestHeader requestHeader,
            uint subscriptionId,
            uint triggeringItemId,
            UInt32Collection linksToAdd,
            UInt32Collection linksToRemove,
            out StatusCodeCollection addResults,
            out DiagnosticInfoCollection addDiagnosticInfos,
            out StatusCodeCollection removeResults,
            out DiagnosticInfoCollection removeDiagnosticInfos);

        /// <summary>
        /// Begins an asynchronous invocation of the SetTriggering service.
        /// </summary>
        IAsyncResult BeginSetTriggering(
            RequestHeader requestHeader,
            uint subscriptionId,
            uint triggeringItemId,
            UInt32Collection linksToAdd,
            UInt32Collection linksToRemove,
            AsyncCallback callback,
            object asyncState);

        /// <summary>
        /// Finishes an asynchronous invocation of the SetTriggering service.
        /// </summary>
        ResponseHeader EndSetTriggering(
            IAsyncResult result,
            out StatusCodeCollection addResults,
            out DiagnosticInfoCollection addDiagnosticInfos,
            out StatusCodeCollection removeResults,
            out DiagnosticInfoCollection removeDiagnosticInfos);

#else  // NET_STANDARD
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

        /// <summary>
        /// Begins an asynchronous invocation of the SetTriggering service.
        /// </summary>
        IAsyncResult BeginSetTriggering(
            RequestHeader    requestHeader,
            uint             subscriptionId,
            uint             triggeringItemId,
            UInt32Collection linksToAdd,
            UInt32Collection linksToRemove,
            AsyncCallback    callback,
            object           asyncState);

        /// <summary>
        /// Finishes an asynchronous invocation of the SetTriggering service.
        /// </summary>
        ResponseHeader EndSetTriggering(
            IAsyncResult                 result,
            out StatusCodeCollection     addResults,
            out DiagnosticInfoCollection addDiagnosticInfos,
            out StatusCodeCollection     removeResults,
            out DiagnosticInfoCollection removeDiagnosticInfos);
#endif

#if (NET_STANDARD_ASYNC)
        /// <summary>
        /// Invokes the SetTriggering service using Task based request.
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
        #endregion

        #region DeleteMonitoredItems Methods
#if (!OPCUA_EXCLUDE_DeleteMonitoredItems)
#if (!NET_STANDARD)
        /// <summary>
        /// Invokes the DeleteMonitoredItems service.
        /// </summary>
        ResponseHeader DeleteMonitoredItems(
            RequestHeader requestHeader,
            uint subscriptionId,
            UInt32Collection monitoredItemIds,
            out StatusCodeCollection results,
            out DiagnosticInfoCollection diagnosticInfos);

        /// <summary>
        /// Begins an asynchronous invocation of the DeleteMonitoredItems service.
        /// </summary>
        IAsyncResult BeginDeleteMonitoredItems(
            RequestHeader requestHeader,
            uint subscriptionId,
            UInt32Collection monitoredItemIds,
            AsyncCallback callback,
            object asyncState);

        /// <summary>
        /// Finishes an asynchronous invocation of the DeleteMonitoredItems service.
        /// </summary>
        ResponseHeader EndDeleteMonitoredItems(
            IAsyncResult result,
            out StatusCodeCollection results,
            out DiagnosticInfoCollection diagnosticInfos);

#else  // NET_STANDARD
        /// <summary>
        /// Invokes the DeleteMonitoredItems service.
        /// </summary>
        ResponseHeader DeleteMonitoredItems(
            RequestHeader                requestHeader,
            uint                         subscriptionId,
            UInt32Collection             monitoredItemIds,
            out StatusCodeCollection     results,
            out DiagnosticInfoCollection diagnosticInfos);

        /// <summary>
        /// Begins an asynchronous invocation of the DeleteMonitoredItems service.
        /// </summary>
        IAsyncResult BeginDeleteMonitoredItems(
            RequestHeader    requestHeader,
            uint             subscriptionId,
            UInt32Collection monitoredItemIds,
            AsyncCallback    callback,
            object           asyncState);

        /// <summary>
        /// Finishes an asynchronous invocation of the DeleteMonitoredItems service.
        /// </summary>
        ResponseHeader EndDeleteMonitoredItems(
            IAsyncResult                 result,
            out StatusCodeCollection     results,
            out DiagnosticInfoCollection diagnosticInfos);
#endif

#if (NET_STANDARD_ASYNC)
        /// <summary>
        /// Invokes the DeleteMonitoredItems service using Task based request.
        /// </summary>
        Task<DeleteMonitoredItemsResponse> DeleteMonitoredItemsAsync(
            RequestHeader     requestHeader,
            uint              subscriptionId,
            UInt32Collection  monitoredItemIds,
            CancellationToken ct);
#endif
#endif
        #endregion

        #region CreateSubscription Methods
#if (!OPCUA_EXCLUDE_CreateSubscription)
#if (!NET_STANDARD)
        /// <summary>
        /// Invokes the CreateSubscription service.
        /// </summary>
        ResponseHeader CreateSubscription(
            RequestHeader requestHeader,
            double requestedPublishingInterval,
            uint requestedLifetimeCount,
            uint requestedMaxKeepAliveCount,
            uint maxNotificationsPerPublish,
            bool publishingEnabled,
            byte priority,
            out uint subscriptionId,
            out double revisedPublishingInterval,
            out uint revisedLifetimeCount,
            out uint revisedMaxKeepAliveCount);

        /// <summary>
        /// Begins an asynchronous invocation of the CreateSubscription service.
        /// </summary>
        IAsyncResult BeginCreateSubscription(
            RequestHeader requestHeader,
            double requestedPublishingInterval,
            uint requestedLifetimeCount,
            uint requestedMaxKeepAliveCount,
            uint maxNotificationsPerPublish,
            bool publishingEnabled,
            byte priority,
            AsyncCallback callback,
            object asyncState);

        /// <summary>
        /// Finishes an asynchronous invocation of the CreateSubscription service.
        /// </summary>
        ResponseHeader EndCreateSubscription(
            IAsyncResult result,
            out uint subscriptionId,
            out double revisedPublishingInterval,
            out uint revisedLifetimeCount,
            out uint revisedMaxKeepAliveCount);

#else  // NET_STANDARD
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

        /// <summary>
        /// Begins an asynchronous invocation of the CreateSubscription service.
        /// </summary>
        IAsyncResult BeginCreateSubscription(
            RequestHeader requestHeader,
            double        requestedPublishingInterval,
            uint          requestedLifetimeCount,
            uint          requestedMaxKeepAliveCount,
            uint          maxNotificationsPerPublish,
            bool          publishingEnabled,
            byte          priority,
            AsyncCallback callback,
            object        asyncState);

        /// <summary>
        /// Finishes an asynchronous invocation of the CreateSubscription service.
        /// </summary>
        ResponseHeader EndCreateSubscription(
            IAsyncResult result,
            out uint   subscriptionId,
            out double revisedPublishingInterval,
            out uint   revisedLifetimeCount,
            out uint   revisedMaxKeepAliveCount);
#endif

#if (NET_STANDARD_ASYNC)
        /// <summary>
        /// Invokes the CreateSubscription service using Task based request.
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
        #endregion

        #region ModifySubscription Methods
#if (!OPCUA_EXCLUDE_ModifySubscription)
#if (!NET_STANDARD)
        /// <summary>
        /// Invokes the ModifySubscription service.
        /// </summary>
        ResponseHeader ModifySubscription(
            RequestHeader requestHeader,
            uint subscriptionId,
            double requestedPublishingInterval,
            uint requestedLifetimeCount,
            uint requestedMaxKeepAliveCount,
            uint maxNotificationsPerPublish,
            byte priority,
            out double revisedPublishingInterval,
            out uint revisedLifetimeCount,
            out uint revisedMaxKeepAliveCount);

        /// <summary>
        /// Begins an asynchronous invocation of the ModifySubscription service.
        /// </summary>
        IAsyncResult BeginModifySubscription(
            RequestHeader requestHeader,
            uint subscriptionId,
            double requestedPublishingInterval,
            uint requestedLifetimeCount,
            uint requestedMaxKeepAliveCount,
            uint maxNotificationsPerPublish,
            byte priority,
            AsyncCallback callback,
            object asyncState);

        /// <summary>
        /// Finishes an asynchronous invocation of the ModifySubscription service.
        /// </summary>
        ResponseHeader EndModifySubscription(
            IAsyncResult result,
            out double revisedPublishingInterval,
            out uint revisedLifetimeCount,
            out uint revisedMaxKeepAliveCount);

#else  // NET_STANDARD
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

        /// <summary>
        /// Begins an asynchronous invocation of the ModifySubscription service.
        /// </summary>
        IAsyncResult BeginModifySubscription(
            RequestHeader requestHeader,
            uint          subscriptionId,
            double        requestedPublishingInterval,
            uint          requestedLifetimeCount,
            uint          requestedMaxKeepAliveCount,
            uint          maxNotificationsPerPublish,
            byte          priority,
            AsyncCallback callback,
            object        asyncState);

        /// <summary>
        /// Finishes an asynchronous invocation of the ModifySubscription service.
        /// </summary>
        ResponseHeader EndModifySubscription(
            IAsyncResult result,
            out double revisedPublishingInterval,
            out uint   revisedLifetimeCount,
            out uint   revisedMaxKeepAliveCount);
#endif

#if (NET_STANDARD_ASYNC)
        /// <summary>
        /// Invokes the ModifySubscription service using Task based request.
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
        #endregion

        #region SetPublishingMode Methods
#if (!OPCUA_EXCLUDE_SetPublishingMode)
#if (!NET_STANDARD)
        /// <summary>
        /// Invokes the SetPublishingMode service.
        /// </summary>
        ResponseHeader SetPublishingMode(
            RequestHeader requestHeader,
            bool publishingEnabled,
            UInt32Collection subscriptionIds,
            out StatusCodeCollection results,
            out DiagnosticInfoCollection diagnosticInfos);

        /// <summary>
        /// Begins an asynchronous invocation of the SetPublishingMode service.
        /// </summary>
        IAsyncResult BeginSetPublishingMode(
            RequestHeader requestHeader,
            bool publishingEnabled,
            UInt32Collection subscriptionIds,
            AsyncCallback callback,
            object asyncState);

        /// <summary>
        /// Finishes an asynchronous invocation of the SetPublishingMode service.
        /// </summary>
        ResponseHeader EndSetPublishingMode(
            IAsyncResult result,
            out StatusCodeCollection results,
            out DiagnosticInfoCollection diagnosticInfos);

#else  // NET_STANDARD
        /// <summary>
        /// Invokes the SetPublishingMode service.
        /// </summary>
        ResponseHeader SetPublishingMode(
            RequestHeader                requestHeader,
            bool                         publishingEnabled,
            UInt32Collection             subscriptionIds,
            out StatusCodeCollection     results,
            out DiagnosticInfoCollection diagnosticInfos);

        /// <summary>
        /// Begins an asynchronous invocation of the SetPublishingMode service.
        /// </summary>
        IAsyncResult BeginSetPublishingMode(
            RequestHeader    requestHeader,
            bool             publishingEnabled,
            UInt32Collection subscriptionIds,
            AsyncCallback    callback,
            object           asyncState);

        /// <summary>
        /// Finishes an asynchronous invocation of the SetPublishingMode service.
        /// </summary>
        ResponseHeader EndSetPublishingMode(
            IAsyncResult                 result,
            out StatusCodeCollection     results,
            out DiagnosticInfoCollection diagnosticInfos);
#endif

#if (NET_STANDARD_ASYNC)
        /// <summary>
        /// Invokes the SetPublishingMode service using Task based request.
        /// </summary>
        Task<SetPublishingModeResponse> SetPublishingModeAsync(
            RequestHeader     requestHeader,
            bool              publishingEnabled,
            UInt32Collection  subscriptionIds,
            CancellationToken ct);
#endif
#endif
        #endregion

        #region Publish Methods
#if (!OPCUA_EXCLUDE_Publish)
#if (!NET_STANDARD)
        /// <summary>
        /// Invokes the Publish service.
        /// </summary>
        ResponseHeader Publish(
            RequestHeader requestHeader,
            SubscriptionAcknowledgementCollection subscriptionAcknowledgements,
            out uint subscriptionId,
            out UInt32Collection availableSequenceNumbers,
            out bool moreNotifications,
            out NotificationMessage notificationMessage,
            out StatusCodeCollection results,
            out DiagnosticInfoCollection diagnosticInfos);

        /// <summary>
        /// Begins an asynchronous invocation of the Publish service.
        /// </summary>
        IAsyncResult BeginPublish(
            RequestHeader requestHeader,
            SubscriptionAcknowledgementCollection subscriptionAcknowledgements,
            AsyncCallback callback,
            object asyncState);

        /// <summary>
        /// Finishes an asynchronous invocation of the Publish service.
        /// </summary>
        ResponseHeader EndPublish(
            IAsyncResult result,
            out uint subscriptionId,
            out UInt32Collection availableSequenceNumbers,
            out bool moreNotifications,
            out NotificationMessage notificationMessage,
            out StatusCodeCollection results,
            out DiagnosticInfoCollection diagnosticInfos);

#else  // NET_STANDARD
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

        /// <summary>
        /// Begins an asynchronous invocation of the Publish service.
        /// </summary>
        IAsyncResult BeginPublish(
            RequestHeader                         requestHeader,
            SubscriptionAcknowledgementCollection subscriptionAcknowledgements,
            AsyncCallback                         callback,
            object                                asyncState);

        /// <summary>
        /// Finishes an asynchronous invocation of the Publish service.
        /// </summary>
        ResponseHeader EndPublish(
            IAsyncResult                 result,
            out uint                     subscriptionId,
            out UInt32Collection         availableSequenceNumbers,
            out bool                     moreNotifications,
            out NotificationMessage      notificationMessage,
            out StatusCodeCollection     results,
            out DiagnosticInfoCollection diagnosticInfos);
#endif

#if (NET_STANDARD_ASYNC)
        /// <summary>
        /// Invokes the Publish service using Task based request.
        /// </summary>
        Task<PublishResponse> PublishAsync(
            RequestHeader                         requestHeader,
            SubscriptionAcknowledgementCollection subscriptionAcknowledgements,
            CancellationToken                     ct);

#endif
#endif
        #endregion

        #region Republish Methods
#if (!OPCUA_EXCLUDE_Republish)
#if (!NET_STANDARD)
        /// <summary>
        /// Invokes the Republish service.
        /// </summary>
        ResponseHeader Republish(
            RequestHeader requestHeader,
            uint subscriptionId,
            uint retransmitSequenceNumber,
            out NotificationMessage notificationMessage);

        /// <summary>
        /// Begins an asynchronous invocation of the Republish service.
        /// </summary>
        IAsyncResult BeginRepublish(
            RequestHeader requestHeader,
            uint subscriptionId,
            uint retransmitSequenceNumber,
            AsyncCallback callback,
            object asyncState);

        /// <summary>
        /// Finishes an asynchronous invocation of the Republish service.
        /// </summary>
        ResponseHeader EndRepublish(
            IAsyncResult result,
            out NotificationMessage notificationMessage);

#else  // NET_STANDARD
        /// <summary>
        /// Invokes the Republish service.
        /// </summary>
        ResponseHeader Republish(
            RequestHeader           requestHeader,
            uint                    subscriptionId,
            uint                    retransmitSequenceNumber,
            out NotificationMessage notificationMessage);

        /// <summary>
        /// Begins an asynchronous invocation of the Republish service.
        /// </summary>
        IAsyncResult BeginRepublish(
            RequestHeader requestHeader,
            uint          subscriptionId,
            uint          retransmitSequenceNumber,
            AsyncCallback callback,
            object        asyncState);

        /// <summary>
        /// Finishes an asynchronous invocation of the Republish service.
        /// </summary>
        ResponseHeader EndRepublish(
            IAsyncResult            result,
            out NotificationMessage notificationMessage);
#endif

#if (NET_STANDARD_ASYNC)
        /// <summary>
        /// Invokes the Republish service using Task based request.
        /// </summary>
        Task<RepublishResponse> RepublishAsync(
            RequestHeader     requestHeader,
            uint              subscriptionId,
            uint              retransmitSequenceNumber,
            CancellationToken ct);
#endif
#endif
        #endregion

        #region TransferSubscriptions Methods
#if (!OPCUA_EXCLUDE_TransferSubscriptions)
#if (!NET_STANDARD)
        /// <summary>
        /// Invokes the TransferSubscriptions service.
        /// </summary>
        ResponseHeader TransferSubscriptions(
            RequestHeader requestHeader,
            UInt32Collection subscriptionIds,
            bool sendInitialValues,
            out TransferResultCollection results,
            out DiagnosticInfoCollection diagnosticInfos);

        /// <summary>
        /// Begins an asynchronous invocation of the TransferSubscriptions service.
        /// </summary>
        IAsyncResult BeginTransferSubscriptions(
            RequestHeader requestHeader,
            UInt32Collection subscriptionIds,
            bool sendInitialValues,
            AsyncCallback callback,
            object asyncState);

        /// <summary>
        /// Finishes an asynchronous invocation of the TransferSubscriptions service.
        /// </summary>
        ResponseHeader EndTransferSubscriptions(
            IAsyncResult result,
            out TransferResultCollection results,
            out DiagnosticInfoCollection diagnosticInfos);

#else  // NET_STANDARD
        /// <summary>
        /// Invokes the TransferSubscriptions service.
        /// </summary>
        ResponseHeader TransferSubscriptions(
            RequestHeader                requestHeader,
            UInt32Collection             subscriptionIds,
            bool                         sendInitialValues,
            out TransferResultCollection results,
            out DiagnosticInfoCollection diagnosticInfos);

        /// <summary>
        /// Begins an asynchronous invocation of the TransferSubscriptions service.
        /// </summary>
        IAsyncResult BeginTransferSubscriptions(
            RequestHeader    requestHeader,
            UInt32Collection subscriptionIds,
            bool             sendInitialValues,
            AsyncCallback    callback,
            object           asyncState);

        /// <summary>
        /// Finishes an asynchronous invocation of the TransferSubscriptions service.
        /// </summary>
        ResponseHeader EndTransferSubscriptions(
            IAsyncResult                 result,
            out TransferResultCollection results,
            out DiagnosticInfoCollection diagnosticInfos);
#endif

#if (NET_STANDARD_ASYNC)
        /// <summary>
        /// Invokes the TransferSubscriptions service using Task based request.
        /// </summary>
        Task<TransferSubscriptionsResponse> TransferSubscriptionsAsync(
            RequestHeader     requestHeader,
            UInt32Collection  subscriptionIds,
            bool              sendInitialValues,
            CancellationToken ct);
#endif
#endif
        #endregion

        #region DeleteSubscriptions Methods
#if (!OPCUA_EXCLUDE_DeleteSubscriptions)
#if (!NET_STANDARD)
        /// <summary>
        /// Invokes the DeleteSubscriptions service.
        /// </summary>
        ResponseHeader DeleteSubscriptions(
            RequestHeader requestHeader,
            UInt32Collection subscriptionIds,
            out StatusCodeCollection results,
            out DiagnosticInfoCollection diagnosticInfos);

        /// <summary>
        /// Begins an asynchronous invocation of the DeleteSubscriptions service.
        /// </summary>
        IAsyncResult BeginDeleteSubscriptions(
            RequestHeader requestHeader,
            UInt32Collection subscriptionIds,
            AsyncCallback callback,
            object asyncState);

        /// <summary>
        /// Finishes an asynchronous invocation of the DeleteSubscriptions service.
        /// </summary>
        ResponseHeader EndDeleteSubscriptions(
            IAsyncResult result,
            out StatusCodeCollection results,
            out DiagnosticInfoCollection diagnosticInfos);
#else  // NET_STANDARD
        /// <summary>
        /// Invokes the DeleteSubscriptions service.
        /// </summary>
        ResponseHeader DeleteSubscriptions(
            RequestHeader                requestHeader,
            UInt32Collection             subscriptionIds,
            out StatusCodeCollection     results,
            out DiagnosticInfoCollection diagnosticInfos);

        /// <summary>
        /// Begins an asynchronous invocation of the DeleteSubscriptions service.
        /// </summary>
        IAsyncResult BeginDeleteSubscriptions(
            RequestHeader    requestHeader,
            UInt32Collection subscriptionIds,
            AsyncCallback    callback,
            object           asyncState);

        /// <summary>
        /// Finishes an asynchronous invocation of the DeleteSubscriptions service.
        /// </summary>
        ResponseHeader EndDeleteSubscriptions(
            IAsyncResult                 result,
            out StatusCodeCollection     results,
            out DiagnosticInfoCollection diagnosticInfos);
#endif

#if (NET_STANDARD_ASYNC)
        /// <summary>
        /// Invokes the DeleteSubscriptions service using Task based request.
        /// </summary>
        Task<DeleteSubscriptionsResponse> DeleteSubscriptionsAsync(
            RequestHeader     requestHeader,
            UInt32Collection  subscriptionIds,
            CancellationToken ct);
#endif
#endif
        #endregion
        #endregion
    }
}
