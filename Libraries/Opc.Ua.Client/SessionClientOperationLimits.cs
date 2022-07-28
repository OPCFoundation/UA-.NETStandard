/* ========================================================================
 * Copyright (c) 2005-2021 The OPC Foundation, Inc. All rights reserved.
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

#define NET_STANDARD
#define NET_STANDARD_ASYNC

using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua
{
    /// <summary>
    /// The client side interface with support for operation limits.
    /// </summary>
    public class SessionClientOperationLimits : SessionClient
    {
        #region Constructors
        /// <summary>
        /// Intializes the object with a channel and a message context.
        /// </summary>
        public SessionClientOperationLimits(ITransportChannel channel)
        :
            base(channel)
        {
            m_operationLimits = new OperationLimits();
        }
        #endregion

        #region Public Properties
        /// <summary>
        /// The operation limits are used to chunk service requests.
        /// </summary>
        public OperationLimits OperationLimits
        {
            get => m_operationLimits;
            internal set
            {
                if (value == null)
                {
                    m_operationLimits = new OperationLimits();
                }
                else
                {
                    m_operationLimits = value;
                };
            }
        }
        #endregion

        #region AddNodes Methods
        /// <summary>
        /// Invokes the AddNodes service.
        /// </summary>
        public override ResponseHeader AddNodes(
            RequestHeader requestHeader,
            AddNodesItemCollection nodesToAdd,
            out AddNodesResultCollection results,
            out DiagnosticInfoCollection diagnosticInfos)
        {
            AddNodesRequest request = new AddNodesRequest();
            AddNodesResponse response = null;

            request.RequestHeader = requestHeader;
            request.NodesToAdd = nodesToAdd;

            UpdateRequestHeader(request, requestHeader == null, "AddNodes");

            try
            {
                IServiceResponse genericResponse = TransportChannel.SendRequest(request);

                if (genericResponse == null)
                {
                    throw new ServiceResultException(StatusCodes.BadUnknownResponse);
                }

                ValidateResponse(genericResponse.ResponseHeader);
                response = (AddNodesResponse)genericResponse;

                results = response.Results;
                diagnosticInfos = response.DiagnosticInfos;
            }
            finally
            {
                RequestCompleted(request, response, "AddNodes");
            }

            return response.ResponseHeader;
        }

#if (CLIENT_ASYNC)
        /// <summary>
        /// Invokes the AddNodes service using async Task based request.
        /// </summary>
        public override async Task<AddNodesResponse> AddNodesAsync(
            RequestHeader requestHeader,
            AddNodesItemCollection nodesToAdd,
            CancellationToken ct)
        {
            AddNodesRequest request = new AddNodesRequest();
            AddNodesResponse response = null;

            request.RequestHeader = requestHeader;
            request.NodesToAdd = nodesToAdd;

            UpdateRequestHeader(request, requestHeader == null, "AddNodes");

            try
            {
                IServiceResponse genericResponse = await TransportChannel.SendRequestAsync(request, ct).ConfigureAwait(false);

                if (genericResponse == null)
                {
                    throw new ServiceResultException(StatusCodes.BadUnknownResponse);
                }

                ValidateResponse(genericResponse.ResponseHeader);
                response = (AddNodesResponse)genericResponse;
            }
            finally
            {
                RequestCompleted(request, response, "AddNodes");
            }

            return response;
        }
#endif
        #endregion

        #region AddReferences Methods
        /// <summary>
        /// Invokes the AddReferences service.
        /// </summary>
        public override ResponseHeader AddReferences(
            RequestHeader requestHeader,
            AddReferencesItemCollection referencesToAdd,
            out StatusCodeCollection results,
            out DiagnosticInfoCollection diagnosticInfos)
        {
            AddReferencesRequest request = new AddReferencesRequest();
            AddReferencesResponse response = null;

            request.RequestHeader = requestHeader;
            request.ReferencesToAdd = referencesToAdd;

            UpdateRequestHeader(request, requestHeader == null, "AddReferences");

            try
            {
                IServiceResponse genericResponse = TransportChannel.SendRequest(request);

                if (genericResponse == null)
                {
                    throw new ServiceResultException(StatusCodes.BadUnknownResponse);
                }

                ValidateResponse(genericResponse.ResponseHeader);
                response = (AddReferencesResponse)genericResponse;

                results = response.Results;
                diagnosticInfos = response.DiagnosticInfos;
            }
            finally
            {
                RequestCompleted(request, response, "AddReferences");
            }

            return response.ResponseHeader;
        }

#if (CLIENT_ASYNC)
        /// <summary>
        /// Invokes the AddReferences service using async Task based request.
        /// </summary>
        public override async Task<AddReferencesResponse> AddReferencesAsync(
            RequestHeader requestHeader,
            AddReferencesItemCollection referencesToAdd,
            CancellationToken ct)
        {
            AddReferencesRequest request = new AddReferencesRequest();
            AddReferencesResponse response = null;

            request.RequestHeader = requestHeader;
            request.ReferencesToAdd = referencesToAdd;

            UpdateRequestHeader(request, requestHeader == null, "AddReferences");

            try
            {
                IServiceResponse genericResponse = await TransportChannel.SendRequestAsync(request, ct).ConfigureAwait(false);

                if (genericResponse == null)
                {
                    throw new ServiceResultException(StatusCodes.BadUnknownResponse);
                }

                ValidateResponse(genericResponse.ResponseHeader);
                response = (AddReferencesResponse)genericResponse;
            }
            finally
            {
                RequestCompleted(request, response, "AddReferences");
            }

            return response;
        }
#endif
        #endregion

        #region DeleteNodes Methods
        /// <summary>
        /// Invokes the DeleteNodes service.
        /// </summary>
        public override ResponseHeader DeleteNodes(
            RequestHeader requestHeader,
            DeleteNodesItemCollection nodesToDelete,
            out StatusCodeCollection results,
            out DiagnosticInfoCollection diagnosticInfos)
        {
            DeleteNodesRequest request = new DeleteNodesRequest();
            DeleteNodesResponse response = null;

            request.RequestHeader = requestHeader;
            request.NodesToDelete = nodesToDelete;

            UpdateRequestHeader(request, requestHeader == null, "DeleteNodes");

            try
            {
                IServiceResponse genericResponse = TransportChannel.SendRequest(request);

                if (genericResponse == null)
                {
                    throw new ServiceResultException(StatusCodes.BadUnknownResponse);
                }

                ValidateResponse(genericResponse.ResponseHeader);
                response = (DeleteNodesResponse)genericResponse;

                results = response.Results;
                diagnosticInfos = response.DiagnosticInfos;
            }
            finally
            {
                RequestCompleted(request, response, "DeleteNodes");
            }

            return response.ResponseHeader;
        }

#if (CLIENT_ASYNC)
        /// <summary>
        /// Invokes the DeleteNodes service using async Task based request.
        /// </summary>
        public override async Task<DeleteNodesResponse> DeleteNodesAsync(
            RequestHeader requestHeader,
            DeleteNodesItemCollection nodesToDelete,
            CancellationToken ct)
        {
            DeleteNodesRequest request = new DeleteNodesRequest();
            DeleteNodesResponse response = null;

            request.RequestHeader = requestHeader;
            request.NodesToDelete = nodesToDelete;

            UpdateRequestHeader(request, requestHeader == null, "DeleteNodes");

            try
            {
                IServiceResponse genericResponse = await TransportChannel.SendRequestAsync(request, ct).ConfigureAwait(false);

                if (genericResponse == null)
                {
                    throw new ServiceResultException(StatusCodes.BadUnknownResponse);
                }

                ValidateResponse(genericResponse.ResponseHeader);
                response = (DeleteNodesResponse)genericResponse;
            }
            finally
            {
                RequestCompleted(request, response, "DeleteNodes");
            }

            return response;
        }
#endif
        #endregion

        #region DeleteReferences Methods
        /// <summary>
        /// Invokes the DeleteReferences service.
        /// </summary>
        public override ResponseHeader DeleteReferences(
            RequestHeader requestHeader,
            DeleteReferencesItemCollection referencesToDelete,
            out StatusCodeCollection results,
            out DiagnosticInfoCollection diagnosticInfos)
        {
            DeleteReferencesRequest request = new DeleteReferencesRequest();
            DeleteReferencesResponse response = null;

            request.RequestHeader = requestHeader;
            request.ReferencesToDelete = referencesToDelete;

            UpdateRequestHeader(request, requestHeader == null, "DeleteReferences");

            try
            {
                IServiceResponse genericResponse = TransportChannel.SendRequest(request);

                if (genericResponse == null)
                {
                    throw new ServiceResultException(StatusCodes.BadUnknownResponse);
                }

                ValidateResponse(genericResponse.ResponseHeader);
                response = (DeleteReferencesResponse)genericResponse;

                results = response.Results;
                diagnosticInfos = response.DiagnosticInfos;
            }
            finally
            {
                RequestCompleted(request, response, "DeleteReferences");
            }

            return response.ResponseHeader;
        }

#if (CLIENT_ASYNC)
        /// <summary>
        /// Invokes the DeleteReferences service using async Task based request.
        /// </summary>
        public override async Task<DeleteReferencesResponse> DeleteReferencesAsync(
            RequestHeader requestHeader,
            DeleteReferencesItemCollection referencesToDelete,
            CancellationToken ct)
        {
            DeleteReferencesRequest request = new DeleteReferencesRequest();
            DeleteReferencesResponse response = null;

            request.RequestHeader = requestHeader;
            request.ReferencesToDelete = referencesToDelete;

            UpdateRequestHeader(request, requestHeader == null, "DeleteReferences");

            try
            {
                IServiceResponse genericResponse = await TransportChannel.SendRequestAsync(request, ct).ConfigureAwait(false);

                if (genericResponse == null)
                {
                    throw new ServiceResultException(StatusCodes.BadUnknownResponse);
                }

                ValidateResponse(genericResponse.ResponseHeader);
                response = (DeleteReferencesResponse)genericResponse;
            }
            finally
            {
                RequestCompleted(request, response, "DeleteReferences");
            }

            return response;
        }
#endif
        #endregion

        #region Browse Methods
        /// <inheritdoc/>
        public override ResponseHeader Browse(
            RequestHeader requestHeader,
            ViewDescription view,
            uint requestedMaxReferencesPerNode,
            BrowseDescriptionCollection nodesToBrowse,
            out BrowseResultCollection results,
            out DiagnosticInfoCollection diagnosticInfos)
        {
            if (OperationLimits.MaxNodesPerBrowse == 0 ||
                nodesToBrowse.Count <= OperationLimits.MaxNodesPerBrowse)
            {
                return base.Browse(requestHeader, view, requestedMaxReferencesPerNode, nodesToBrowse, out results, out diagnosticInfos);
            }

            ResponseHeader responseHeader = null;
            results = new BrowseResultCollection();
            diagnosticInfos = new DiagnosticInfoCollection();

            while (nodesToBrowse.Count > results.Count)
            {
                BrowseDescriptionCollection chunknodesToBrowse;
                if ((nodesToBrowse.Count - results.Count) > OperationLimits.MaxNodesPerBrowse)
                {
                    chunknodesToBrowse = new BrowseDescriptionCollection(nodesToBrowse.Skip(results.Count).Take((int)OperationLimits.MaxNodesPerBrowse));
                }
                else
                {
                    chunknodesToBrowse = new BrowseDescriptionCollection(nodesToBrowse.Skip(results.Count));
                }

                if (requestHeader != null)
                {
                    requestHeader.RequestHandle = 0;
                }

                responseHeader = base.Browse(
                    requestHeader,
                    view,
                    requestedMaxReferencesPerNode,
                    chunknodesToBrowse,
                    out BrowseResultCollection chunkResults,
                    out DiagnosticInfoCollection chunkDiagnosticInfos);

                ClientBase.ValidateResponse(chunkResults, chunknodesToBrowse);
                ClientBase.ValidateDiagnosticInfos(chunkDiagnosticInfos, chunknodesToBrowse);

                results.AddRange(chunkResults);
                diagnosticInfos.AddRange(chunkDiagnosticInfos);
            }

            return responseHeader;
        }

#if (CLIENT_ASYNC)
        /// <inheritdoc/>
        public override async Task<BrowseResponse> BrowseAsync(
            RequestHeader requestHeader,
            ViewDescription view,
            uint requestedMaxReferencesPerNode,
            BrowseDescriptionCollection nodesToBrowse,
            CancellationToken ct)
        {
            if (OperationLimits.MaxNodesPerBrowse == 0 ||
                nodesToBrowse.Count <= OperationLimits.MaxNodesPerBrowse)
            {
                return await base.BrowseAsync(requestHeader, view, requestedMaxReferencesPerNode, nodesToBrowse, ct);
            }

            BrowseResponse response = null;
            var results = new BrowseResultCollection();
            var diagnosticInfos = new DiagnosticInfoCollection();

            while (nodesToBrowse.Count > results.Count)
            {
                BrowseDescriptionCollection chunknodesToBrowse;
                if ((nodesToBrowse.Count - results.Count) > OperationLimits.MaxNodesPerBrowse)
                {
                    chunknodesToBrowse = new BrowseDescriptionCollection(nodesToBrowse.Skip(results.Count).Take((int)OperationLimits.MaxNodesPerBrowse));
                }
                else
                {
                    chunknodesToBrowse = new BrowseDescriptionCollection(nodesToBrowse.Skip(results.Count));
                }

                if (requestHeader != null)
                {
                    requestHeader.RequestHandle = 0;
                }

                response = await base.BrowseAsync(
                    requestHeader,
                    view,
                    requestedMaxReferencesPerNode,
                    chunknodesToBrowse, ct).ConfigureAwait(false);

                BrowseResultCollection chunkResults = response.Results;
                DiagnosticInfoCollection chunkDiagnosticInfos = response.DiagnosticInfos;

                ClientBase.ValidateResponse(chunkResults, chunknodesToBrowse);
                ClientBase.ValidateDiagnosticInfos(chunkDiagnosticInfos, chunknodesToBrowse);

                results.AddRange(chunkResults);
                diagnosticInfos.AddRange(chunkDiagnosticInfos);
            }

            response.Results = results;
            response.DiagnosticInfos = diagnosticInfos;

            return response;
        }
#endif
        #endregion


        #region TranslateBrowsePathsToNodeIds Methods
        /// <summary>
        /// Invokes the TranslateBrowsePathsToNodeIds service.
        /// </summary>
        public override ResponseHeader TranslateBrowsePathsToNodeIds(
            RequestHeader requestHeader,
            BrowsePathCollection browsePaths,
            out BrowsePathResultCollection results,
            out DiagnosticInfoCollection diagnosticInfos)
        {
            TranslateBrowsePathsToNodeIdsRequest request = new TranslateBrowsePathsToNodeIdsRequest();
            TranslateBrowsePathsToNodeIdsResponse response = null;

            request.RequestHeader = requestHeader;
            request.BrowsePaths = browsePaths;

            UpdateRequestHeader(request, requestHeader == null, "TranslateBrowsePathsToNodeIds");

            try
            {
                IServiceResponse genericResponse = TransportChannel.SendRequest(request);

                if (genericResponse == null)
                {
                    throw new ServiceResultException(StatusCodes.BadUnknownResponse);
                }

                ValidateResponse(genericResponse.ResponseHeader);
                response = (TranslateBrowsePathsToNodeIdsResponse)genericResponse;

                results = response.Results;
                diagnosticInfos = response.DiagnosticInfos;
            }
            finally
            {
                RequestCompleted(request, response, "TranslateBrowsePathsToNodeIds");
            }

            return response.ResponseHeader;
        }

#if (NET_STANDARD_ASYNC)
        /// <summary>
        /// Invokes the TranslateBrowsePathsToNodeIds service using async Task based request.
        /// </summary>
        public override async Task<TranslateBrowsePathsToNodeIdsResponse> TranslateBrowsePathsToNodeIdsAsync(
            RequestHeader requestHeader,
            BrowsePathCollection browsePaths,
            CancellationToken ct)
        {
            TranslateBrowsePathsToNodeIdsRequest request = new TranslateBrowsePathsToNodeIdsRequest();
            TranslateBrowsePathsToNodeIdsResponse response = null;

            request.RequestHeader = requestHeader;
            request.BrowsePaths = browsePaths;

            UpdateRequestHeader(request, requestHeader == null, "TranslateBrowsePathsToNodeIds");

            try
            {
                IServiceResponse genericResponse = await TransportChannel.SendRequestAsync(request, ct).ConfigureAwait(false);

                if (genericResponse == null)
                {
                    throw new ServiceResultException(StatusCodes.BadUnknownResponse);
                }

                ValidateResponse(genericResponse.ResponseHeader);
                response = (TranslateBrowsePathsToNodeIdsResponse)genericResponse;
            }
            finally
            {
                RequestCompleted(request, response, "TranslateBrowsePathsToNodeIds");
            }

            return response;
        }
#endif
        #endregion

        #region RegisterNodes Methods
        /// <summary>
        /// Invokes the RegisterNodes service.
        /// </summary>
        public override ResponseHeader RegisterNodes(
            RequestHeader requestHeader,
            NodeIdCollection nodesToRegister,
            out NodeIdCollection registeredNodeIds)
        {
            RegisterNodesRequest request = new RegisterNodesRequest();
            RegisterNodesResponse response = null;

            request.RequestHeader = requestHeader;
            request.NodesToRegister = nodesToRegister;

            UpdateRequestHeader(request, requestHeader == null, "RegisterNodes");

            try
            {
                IServiceResponse genericResponse = TransportChannel.SendRequest(request);

                if (genericResponse == null)
                {
                    throw new ServiceResultException(StatusCodes.BadUnknownResponse);
                }

                ValidateResponse(genericResponse.ResponseHeader);
                response = (RegisterNodesResponse)genericResponse;

                registeredNodeIds = response.RegisteredNodeIds;
            }
            finally
            {
                RequestCompleted(request, response, "RegisterNodes");
            }

            return response.ResponseHeader;
        }

#if (CLIENT_ASYNC)
        /// <summary>
        /// Invokes the RegisterNodes service using async Task based request.
        /// </summary>
        public override async Task<RegisterNodesResponse> RegisterNodesAsync(
            RequestHeader requestHeader,
            NodeIdCollection nodesToRegister,
            CancellationToken ct)
        {
            RegisterNodesRequest request = new RegisterNodesRequest();
            RegisterNodesResponse response = null;

            request.RequestHeader = requestHeader;
            request.NodesToRegister = nodesToRegister;

            UpdateRequestHeader(request, requestHeader == null, "RegisterNodes");

            try
            {
                IServiceResponse genericResponse = await TransportChannel.SendRequestAsync(request, ct).ConfigureAwait(false);

                if (genericResponse == null)
                {
                    throw new ServiceResultException(StatusCodes.BadUnknownResponse);
                }

                ValidateResponse(genericResponse.ResponseHeader);
                response = (RegisterNodesResponse)genericResponse;
            }
            finally
            {
                RequestCompleted(request, response, "RegisterNodes");
            }

            return response;
        }
#endif
        #endregion

        #region UnregisterNodes Methods
        /// <summary>
        /// Invokes the UnregisterNodes service.
        /// </summary>
        public override ResponseHeader UnregisterNodes(
            RequestHeader requestHeader,
            NodeIdCollection nodesToUnregister)
        {
            UnregisterNodesRequest request = new UnregisterNodesRequest();
            UnregisterNodesResponse response = null;

            request.RequestHeader = requestHeader;
            request.NodesToUnregister = nodesToUnregister;

            UpdateRequestHeader(request, requestHeader == null, "UnregisterNodes");

            try
            {
                IServiceResponse genericResponse = TransportChannel.SendRequest(request);

                if (genericResponse == null)
                {
                    throw new ServiceResultException(StatusCodes.BadUnknownResponse);
                }

                ValidateResponse(genericResponse.ResponseHeader);
                response = (UnregisterNodesResponse)genericResponse;

            }
            finally
            {
                RequestCompleted(request, response, "UnregisterNodes");
            }

            return response.ResponseHeader;
        }

#if (CLIENT_ASYNC)
        /// <summary>
        /// Invokes the UnregisterNodes service using async Task based request.
        /// </summary>
        public override async Task<UnregisterNodesResponse> UnregisterNodesAsync(
            RequestHeader requestHeader,
            NodeIdCollection nodesToUnregister,
            CancellationToken ct)
        {
            UnregisterNodesRequest request = new UnregisterNodesRequest();
            UnregisterNodesResponse response = null;

            request.RequestHeader = requestHeader;
            request.NodesToUnregister = nodesToUnregister;

            UpdateRequestHeader(request, requestHeader == null, "UnregisterNodes");

            try
            {
                IServiceResponse genericResponse = await TransportChannel.SendRequestAsync(request, ct).ConfigureAwait(false);

                if (genericResponse == null)
                {
                    throw new ServiceResultException(StatusCodes.BadUnknownResponse);
                }

                ValidateResponse(genericResponse.ResponseHeader);
                response = (UnregisterNodesResponse)genericResponse;
            }
            finally
            {
                RequestCompleted(request, response, "UnregisterNodes");
            }

            return response;
        }
#endif
        #endregion

        #region Read Methods
        /// <inheritdoc/>
        public override ResponseHeader Read(
            RequestHeader requestHeader,
            double maxAge,
            TimestampsToReturn timestampsToReturn,
            ReadValueIdCollection nodesToRead,
            out DataValueCollection results,
            out DiagnosticInfoCollection diagnosticInfos)
        {
            if (OperationLimits.MaxNodesPerRead == 0 ||
                nodesToRead.Count <= OperationLimits.MaxNodesPerRead)
            {
                return base.Read(requestHeader, maxAge, timestampsToReturn, nodesToRead, out results, out diagnosticInfos);
            }

            ResponseHeader responseHeader = null;
            results = new DataValueCollection();
            diagnosticInfos = new DiagnosticInfoCollection();

            while (nodesToRead.Count > results.Count)
            {
                ReadValueIdCollection chunkAttributesToRead;
                if ((nodesToRead.Count - results.Count) > OperationLimits.MaxNodesPerRead)
                {
                    chunkAttributesToRead = new ReadValueIdCollection(nodesToRead.Skip(results.Count).Take((int)OperationLimits.MaxNodesPerRead));
                }
                else
                {
                    chunkAttributesToRead = new ReadValueIdCollection(nodesToRead.Skip(results.Count));
                }

                if (requestHeader != null)
                {
                    requestHeader.RequestHandle = 0;
                }

                responseHeader = base.Read(
                    requestHeader,
                    maxAge,
                    timestampsToReturn,
                    chunkAttributesToRead,
                    out DataValueCollection chunkValues,
                    out DiagnosticInfoCollection chunkDiagnosticInfos);

                ClientBase.ValidateResponse(chunkValues, chunkAttributesToRead);
                ClientBase.ValidateDiagnosticInfos(chunkDiagnosticInfos, chunkAttributesToRead);

                results.AddRange(chunkValues);
                diagnosticInfos.AddRange(chunkDiagnosticInfos);
            }

            return responseHeader;
        }

#if (CLIENT_ASYNC)
        /// <inheritdoc/>
        public override async Task<ReadResponse> ReadAsync(
            RequestHeader requestHeader,
            double maxAge,
            TimestampsToReturn timestampsToReturn,
            ReadValueIdCollection nodesToRead,
            CancellationToken ct)
        {
            if (OperationLimits.MaxNodesPerRead == 0 ||
                nodesToRead.Count <= OperationLimits.MaxNodesPerRead)
            {
                return await base.ReadAsync(requestHeader, maxAge, timestampsToReturn, nodesToRead, ct);
            }

            ReadResponse response = null;
            DataValueCollection results = new DataValueCollection();
            DiagnosticInfoCollection diagnosticInfos = new DiagnosticInfoCollection();

            while (nodesToRead.Count > results.Count)
            {
                ReadValueIdCollection chunkAttributesToRead;
                if ((nodesToRead.Count - results.Count) > OperationLimits.MaxNodesPerRead)
                {
                    chunkAttributesToRead = new ReadValueIdCollection(nodesToRead.Skip(results.Count).Take((int)OperationLimits.MaxNodesPerRead));
                }
                else
                {
                    chunkAttributesToRead = new ReadValueIdCollection(nodesToRead.Skip(results.Count));
                }

                if (requestHeader != null)
                {
                    requestHeader.RequestHandle = 0;
                }

                response = await base.ReadAsync(
                    requestHeader,
                    maxAge,
                    timestampsToReturn,
                    chunkAttributesToRead, ct).ConfigureAwait(false);

                DataValueCollection chunkValues = response.Results;
                DiagnosticInfoCollection chunkDiagnosticInfos = response.DiagnosticInfos;

                ClientBase.ValidateResponse(chunkValues, chunkAttributesToRead);
                ClientBase.ValidateDiagnosticInfos(chunkDiagnosticInfos, chunkAttributesToRead);

                results.AddRange(chunkValues);
                diagnosticInfos.AddRange(chunkDiagnosticInfos);
            }

            response.Results = results;
            response.DiagnosticInfos = diagnosticInfos;

            return response;
        }
#endif
        #endregion

        #region HistoryRead Methods
        /// <summary>
        /// Invokes the HistoryRead service.
        /// </summary>
        public override ResponseHeader HistoryRead(
            RequestHeader requestHeader,
            ExtensionObject historyReadDetails,
            TimestampsToReturn timestampsToReturn,
            bool releaseContinuationPoints,
            HistoryReadValueIdCollection nodesToRead,
            out HistoryReadResultCollection results,
            out DiagnosticInfoCollection diagnosticInfos)
        {
            HistoryReadRequest request = new HistoryReadRequest();
            HistoryReadResponse response = null;

            request.RequestHeader = requestHeader;
            request.HistoryReadDetails = historyReadDetails;
            request.TimestampsToReturn = timestampsToReturn;
            request.ReleaseContinuationPoints = releaseContinuationPoints;
            request.NodesToRead = nodesToRead;

            UpdateRequestHeader(request, requestHeader == null, "HistoryRead");

            try
            {
                IServiceResponse genericResponse = TransportChannel.SendRequest(request);

                if (genericResponse == null)
                {
                    throw new ServiceResultException(StatusCodes.BadUnknownResponse);
                }

                ValidateResponse(genericResponse.ResponseHeader);
                response = (HistoryReadResponse)genericResponse;

                results = response.Results;
                diagnosticInfos = response.DiagnosticInfos;
            }
            finally
            {
                RequestCompleted(request, response, "HistoryRead");
            }

            return response.ResponseHeader;
        }

#if (CLIENT_ASYNC)
        /// <summary>
        /// Invokes the HistoryRead service using async Task based request.
        /// </summary>
        public override async Task<HistoryReadResponse> HistoryReadAsync(
            RequestHeader requestHeader,
            ExtensionObject historyReadDetails,
            TimestampsToReturn timestampsToReturn,
            bool releaseContinuationPoints,
            HistoryReadValueIdCollection nodesToRead,
            CancellationToken ct)
        {
            HistoryReadRequest request = new HistoryReadRequest();
            HistoryReadResponse response = null;

            request.RequestHeader = requestHeader;
            request.HistoryReadDetails = historyReadDetails;
            request.TimestampsToReturn = timestampsToReturn;
            request.ReleaseContinuationPoints = releaseContinuationPoints;
            request.NodesToRead = nodesToRead;

            UpdateRequestHeader(request, requestHeader == null, "HistoryRead");

            try
            {
                IServiceResponse genericResponse = await TransportChannel.SendRequestAsync(request, ct).ConfigureAwait(false);

                if (genericResponse == null)
                {
                    throw new ServiceResultException(StatusCodes.BadUnknownResponse);
                }

                ValidateResponse(genericResponse.ResponseHeader);
                response = (HistoryReadResponse)genericResponse;
            }
            finally
            {
                RequestCompleted(request, response, "HistoryRead");
            }

            return response;
        }
#endif
        #endregion

        #region Write Methods
        /// <summary>
        /// Invokes the Write service.
        /// </summary>
        public override ResponseHeader Write(
            RequestHeader requestHeader,
            WriteValueCollection nodesToWrite,
            out StatusCodeCollection results,
            out DiagnosticInfoCollection diagnosticInfos)
        {
            WriteRequest request = new WriteRequest();
            WriteResponse response = null;

            request.RequestHeader = requestHeader;
            request.NodesToWrite = nodesToWrite;

            UpdateRequestHeader(request, requestHeader == null, "Write");

            try
            {
                IServiceResponse genericResponse = TransportChannel.SendRequest(request);

                if (genericResponse == null)
                {
                    throw new ServiceResultException(StatusCodes.BadUnknownResponse);
                }

                ValidateResponse(genericResponse.ResponseHeader);
                response = (WriteResponse)genericResponse;

                results = response.Results;
                diagnosticInfos = response.DiagnosticInfos;
            }
            finally
            {
                RequestCompleted(request, response, "Write");
            }

            return response.ResponseHeader;
        }

#if (CLIENT_ASYNC)
        /// <summary>
        /// Invokes the Write service using async Task based request.
        /// </summary>
        public override async Task<WriteResponse> WriteAsync(
            RequestHeader requestHeader,
            WriteValueCollection nodesToWrite,
            CancellationToken ct)
        {
            WriteRequest request = new WriteRequest();
            WriteResponse response = null;

            request.RequestHeader = requestHeader;
            request.NodesToWrite = nodesToWrite;

            UpdateRequestHeader(request, requestHeader == null, "Write");

            try
            {
                IServiceResponse genericResponse = await TransportChannel.SendRequestAsync(request, ct).ConfigureAwait(false);

                if (genericResponse == null)
                {
                    throw new ServiceResultException(StatusCodes.BadUnknownResponse);
                }

                ValidateResponse(genericResponse.ResponseHeader);
                response = (WriteResponse)genericResponse;
            }
            finally
            {
                RequestCompleted(request, response, "Write");
            }

            return response;
        }
#endif
        #endregion

        #region HistoryUpdate Methods
        /// <summary>
        /// Invokes the HistoryUpdate service.
        /// </summary>
        public override ResponseHeader HistoryUpdate(
            RequestHeader requestHeader,
            ExtensionObjectCollection historyUpdateDetails,
            out HistoryUpdateResultCollection results,
            out DiagnosticInfoCollection diagnosticInfos)
        {
            HistoryUpdateRequest request = new HistoryUpdateRequest();
            HistoryUpdateResponse response = null;

            request.RequestHeader = requestHeader;
            request.HistoryUpdateDetails = historyUpdateDetails;

            UpdateRequestHeader(request, requestHeader == null, "HistoryUpdate");

            try
            {
                IServiceResponse genericResponse = TransportChannel.SendRequest(request);

                if (genericResponse == null)
                {
                    throw new ServiceResultException(StatusCodes.BadUnknownResponse);
                }

                ValidateResponse(genericResponse.ResponseHeader);
                response = (HistoryUpdateResponse)genericResponse;

                results = response.Results;
                diagnosticInfos = response.DiagnosticInfos;
            }
            finally
            {
                RequestCompleted(request, response, "HistoryUpdate");
            }

            return response.ResponseHeader;
        }

#if (CLIENT_ASYNC)
        /// <summary>
        /// Invokes the HistoryUpdate service using async Task based request.
        /// </summary>
        public override async Task<HistoryUpdateResponse> HistoryUpdateAsync(
            RequestHeader requestHeader,
            ExtensionObjectCollection historyUpdateDetails,
            CancellationToken ct)
        {
            HistoryUpdateRequest request = new HistoryUpdateRequest();
            HistoryUpdateResponse response = null;

            request.RequestHeader = requestHeader;
            request.HistoryUpdateDetails = historyUpdateDetails;

            UpdateRequestHeader(request, requestHeader == null, "HistoryUpdate");

            try
            {
                IServiceResponse genericResponse = await TransportChannel.SendRequestAsync(request, ct).ConfigureAwait(false);

                if (genericResponse == null)
                {
                    throw new ServiceResultException(StatusCodes.BadUnknownResponse);
                }

                ValidateResponse(genericResponse.ResponseHeader);
                response = (HistoryUpdateResponse)genericResponse;
            }
            finally
            {
                RequestCompleted(request, response, "HistoryUpdate");
            }

            return response;
        }
#endif
        #endregion

        #region Call Methods
        /// <summary>
        /// Invokes the Call service.
        /// </summary>
        public override ResponseHeader Call(
            RequestHeader requestHeader,
            CallMethodRequestCollection methodsToCall,
            out CallMethodResultCollection results,
            out DiagnosticInfoCollection diagnosticInfos)
        {
            CallRequest request = new CallRequest();
            CallResponse response = null;

            request.RequestHeader = requestHeader;
            request.MethodsToCall = methodsToCall;

            UpdateRequestHeader(request, requestHeader == null, "Call");

            try
            {
                IServiceResponse genericResponse = TransportChannel.SendRequest(request);

                if (genericResponse == null)
                {
                    throw new ServiceResultException(StatusCodes.BadUnknownResponse);
                }

                ValidateResponse(genericResponse.ResponseHeader);
                response = (CallResponse)genericResponse;

                results = response.Results;
                diagnosticInfos = response.DiagnosticInfos;
            }
            finally
            {
                RequestCompleted(request, response, "Call");
            }

            return response.ResponseHeader;
        }

#if (CLIENT_ASYNC)
        /// <summary>
        /// Invokes the Call service using async Task based request.
        /// </summary>
        public override async Task<CallResponse> CallAsync(
            RequestHeader requestHeader,
            CallMethodRequestCollection methodsToCall,
            CancellationToken ct)
        {
            CallRequest request = new CallRequest();
            CallResponse response = null;

            request.RequestHeader = requestHeader;
            request.MethodsToCall = methodsToCall;

            UpdateRequestHeader(request, requestHeader == null, "Call");

            try
            {
                IServiceResponse genericResponse = await TransportChannel.SendRequestAsync(request, ct).ConfigureAwait(false);

                if (genericResponse == null)
                {
                    throw new ServiceResultException(StatusCodes.BadUnknownResponse);
                }

                ValidateResponse(genericResponse.ResponseHeader);
                response = (CallResponse)genericResponse;
            }
            finally
            {
                RequestCompleted(request, response, "Call");
            }

            return response;
        }
#endif
        #endregion

        #region CreateMonitoredItems Methods
        /// <summary>
        /// Invokes the CreateMonitoredItems service.
        /// </summary>
        public override ResponseHeader CreateMonitoredItems(
            RequestHeader requestHeader,
            uint subscriptionId,
            TimestampsToReturn timestampsToReturn,
            MonitoredItemCreateRequestCollection itemsToCreate,
            out MonitoredItemCreateResultCollection results,
            out DiagnosticInfoCollection diagnosticInfos)
        {
            CreateMonitoredItemsRequest request = new CreateMonitoredItemsRequest();
            CreateMonitoredItemsResponse response = null;

            request.RequestHeader = requestHeader;
            request.SubscriptionId = subscriptionId;
            request.TimestampsToReturn = timestampsToReturn;
            request.ItemsToCreate = itemsToCreate;

            UpdateRequestHeader(request, requestHeader == null, "CreateMonitoredItems");

            try
            {
                IServiceResponse genericResponse = TransportChannel.SendRequest(request);

                if (genericResponse == null)
                {
                    throw new ServiceResultException(StatusCodes.BadUnknownResponse);
                }

                ValidateResponse(genericResponse.ResponseHeader);
                response = (CreateMonitoredItemsResponse)genericResponse;

                results = response.Results;
                diagnosticInfos = response.DiagnosticInfos;
            }
            finally
            {
                RequestCompleted(request, response, "CreateMonitoredItems");
            }

            return response.ResponseHeader;
        }

#if (CLIENT_ASYNC)
        /// <summary>
        /// Invokes the CreateMonitoredItems service using async Task based request.
        /// </summary>
        public override async Task<CreateMonitoredItemsResponse> CreateMonitoredItemsAsync(
            RequestHeader requestHeader,
            uint subscriptionId,
            TimestampsToReturn timestampsToReturn,
            MonitoredItemCreateRequestCollection itemsToCreate,
            CancellationToken ct)
        {
            CreateMonitoredItemsRequest request = new CreateMonitoredItemsRequest();
            CreateMonitoredItemsResponse response = null;

            request.RequestHeader = requestHeader;
            request.SubscriptionId = subscriptionId;
            request.TimestampsToReturn = timestampsToReturn;
            request.ItemsToCreate = itemsToCreate;

            UpdateRequestHeader(request, requestHeader == null, "CreateMonitoredItems");

            try
            {
                IServiceResponse genericResponse = await TransportChannel.SendRequestAsync(request, ct).ConfigureAwait(false);

                if (genericResponse == null)
                {
                    throw new ServiceResultException(StatusCodes.BadUnknownResponse);
                }

                ValidateResponse(genericResponse.ResponseHeader);
                response = (CreateMonitoredItemsResponse)genericResponse;
            }
            finally
            {
                RequestCompleted(request, response, "CreateMonitoredItems");
            }

            return response;
        }
#endif
        #endregion

        #region ModifyMonitoredItems Methods
        /// <summary>
        /// Invokes the ModifyMonitoredItems service.
        /// </summary>
        public override ResponseHeader ModifyMonitoredItems(
            RequestHeader requestHeader,
            uint subscriptionId,
            TimestampsToReturn timestampsToReturn,
            MonitoredItemModifyRequestCollection itemsToModify,
            out MonitoredItemModifyResultCollection results,
            out DiagnosticInfoCollection diagnosticInfos)
        {
            ModifyMonitoredItemsRequest request = new ModifyMonitoredItemsRequest();
            ModifyMonitoredItemsResponse response = null;

            request.RequestHeader = requestHeader;
            request.SubscriptionId = subscriptionId;
            request.TimestampsToReturn = timestampsToReturn;
            request.ItemsToModify = itemsToModify;

            UpdateRequestHeader(request, requestHeader == null, "ModifyMonitoredItems");

            try
            {
                IServiceResponse genericResponse = TransportChannel.SendRequest(request);

                if (genericResponse == null)
                {
                    throw new ServiceResultException(StatusCodes.BadUnknownResponse);
                }

                ValidateResponse(genericResponse.ResponseHeader);
                response = (ModifyMonitoredItemsResponse)genericResponse;

                results = response.Results;
                diagnosticInfos = response.DiagnosticInfos;
            }
            finally
            {
                RequestCompleted(request, response, "ModifyMonitoredItems");
            }

            return response.ResponseHeader;
        }

#if (CLIENT_ASYNC)
        /// <summary>
        /// Invokes the ModifyMonitoredItems service using async Task based request.
        /// </summary>
        public override async Task<ModifyMonitoredItemsResponse> ModifyMonitoredItemsAsync(
            RequestHeader requestHeader,
            uint subscriptionId,
            TimestampsToReturn timestampsToReturn,
            MonitoredItemModifyRequestCollection itemsToModify,
            CancellationToken ct)
        {
            ModifyMonitoredItemsRequest request = new ModifyMonitoredItemsRequest();
            ModifyMonitoredItemsResponse response = null;

            request.RequestHeader = requestHeader;
            request.SubscriptionId = subscriptionId;
            request.TimestampsToReturn = timestampsToReturn;
            request.ItemsToModify = itemsToModify;

            UpdateRequestHeader(request, requestHeader == null, "ModifyMonitoredItems");

            try
            {
                IServiceResponse genericResponse = await TransportChannel.SendRequestAsync(request, ct).ConfigureAwait(false);

                if (genericResponse == null)
                {
                    throw new ServiceResultException(StatusCodes.BadUnknownResponse);
                }

                ValidateResponse(genericResponse.ResponseHeader);
                response = (ModifyMonitoredItemsResponse)genericResponse;
            }
            finally
            {
                RequestCompleted(request, response, "ModifyMonitoredItems");
            }

            return response;
        }
#endif
        #endregion

        #region SetMonitoringMode Methods
        /// <summary>
        /// Invokes the SetMonitoringMode service.
        /// </summary>
        public override ResponseHeader SetMonitoringMode(
            RequestHeader requestHeader,
            uint subscriptionId,
            MonitoringMode monitoringMode,
            UInt32Collection monitoredItemIds,
            out StatusCodeCollection results,
            out DiagnosticInfoCollection diagnosticInfos)
        {
            SetMonitoringModeRequest request = new SetMonitoringModeRequest();
            SetMonitoringModeResponse response = null;

            request.RequestHeader = requestHeader;
            request.SubscriptionId = subscriptionId;
            request.MonitoringMode = monitoringMode;
            request.MonitoredItemIds = monitoredItemIds;

            UpdateRequestHeader(request, requestHeader == null, "SetMonitoringMode");

            try
            {
                IServiceResponse genericResponse = TransportChannel.SendRequest(request);

                if (genericResponse == null)
                {
                    throw new ServiceResultException(StatusCodes.BadUnknownResponse);
                }

                ValidateResponse(genericResponse.ResponseHeader);
                response = (SetMonitoringModeResponse)genericResponse;

                results = response.Results;
                diagnosticInfos = response.DiagnosticInfos;
            }
            finally
            {
                RequestCompleted(request, response, "SetMonitoringMode");
            }

            return response.ResponseHeader;
        }

#if (NET_STANDARD_ASYNC)
        /// <summary>
        /// Invokes the SetMonitoringMode service using async Task based request.
        /// </summary>
        public override async Task<SetMonitoringModeResponse> SetMonitoringModeAsync(
            RequestHeader requestHeader,
            uint subscriptionId,
            MonitoringMode monitoringMode,
            UInt32Collection monitoredItemIds,
            CancellationToken ct)
        {
            SetMonitoringModeRequest request = new SetMonitoringModeRequest();
            SetMonitoringModeResponse response = null;

            request.RequestHeader = requestHeader;
            request.SubscriptionId = subscriptionId;
            request.MonitoringMode = monitoringMode;
            request.MonitoredItemIds = monitoredItemIds;

            UpdateRequestHeader(request, requestHeader == null, "SetMonitoringMode");

            try
            {
                IServiceResponse genericResponse = await TransportChannel.SendRequestAsync(request, ct).ConfigureAwait(false);

                if (genericResponse == null)
                {
                    throw new ServiceResultException(StatusCodes.BadUnknownResponse);
                }

                ValidateResponse(genericResponse.ResponseHeader);
                response = (SetMonitoringModeResponse)genericResponse;
            }
            finally
            {
                RequestCompleted(request, response, "SetMonitoringMode");
            }

            return response;
        }
#endif
        #endregion

        #region SetTriggering Methods
        /// <summary>
        /// Invokes the SetTriggering service.
        /// </summary>
        public override ResponseHeader SetTriggering(
            RequestHeader requestHeader,
            uint subscriptionId,
            uint triggeringItemId,
            UInt32Collection linksToAdd,
            UInt32Collection linksToRemove,
            out StatusCodeCollection addResults,
            out DiagnosticInfoCollection addDiagnosticInfos,
            out StatusCodeCollection removeResults,
            out DiagnosticInfoCollection removeDiagnosticInfos)
        {
            SetTriggeringRequest request = new SetTriggeringRequest();
            SetTriggeringResponse response = null;

            request.RequestHeader = requestHeader;
            request.SubscriptionId = subscriptionId;
            request.TriggeringItemId = triggeringItemId;
            request.LinksToAdd = linksToAdd;
            request.LinksToRemove = linksToRemove;

            UpdateRequestHeader(request, requestHeader == null, "SetTriggering");

            try
            {
                IServiceResponse genericResponse = TransportChannel.SendRequest(request);

                if (genericResponse == null)
                {
                    throw new ServiceResultException(StatusCodes.BadUnknownResponse);
                }

                ValidateResponse(genericResponse.ResponseHeader);
                response = (SetTriggeringResponse)genericResponse;

                addResults = response.AddResults;
                addDiagnosticInfos = response.AddDiagnosticInfos;
                removeResults = response.RemoveResults;
                removeDiagnosticInfos = response.RemoveDiagnosticInfos;
            }
            finally
            {
                RequestCompleted(request, response, "SetTriggering");
            }

            return response.ResponseHeader;
        }

#if (NET_STANDARD_ASYNC)
        /// <summary>
        /// Invokes the SetTriggering service using async Task based request.
        /// </summary>
        public override async Task<SetTriggeringResponse> SetTriggeringAsync(
            RequestHeader requestHeader,
            uint subscriptionId,
            uint triggeringItemId,
            UInt32Collection linksToAdd,
            UInt32Collection linksToRemove,
            CancellationToken ct)
        {
            SetTriggeringRequest request = new SetTriggeringRequest();
            SetTriggeringResponse response = null;

            request.RequestHeader = requestHeader;
            request.SubscriptionId = subscriptionId;
            request.TriggeringItemId = triggeringItemId;
            request.LinksToAdd = linksToAdd;
            request.LinksToRemove = linksToRemove;

            UpdateRequestHeader(request, requestHeader == null, "SetTriggering");

            try
            {
                IServiceResponse genericResponse = await TransportChannel.SendRequestAsync(request, ct).ConfigureAwait(false);

                if (genericResponse == null)
                {
                    throw new ServiceResultException(StatusCodes.BadUnknownResponse);
                }

                ValidateResponse(genericResponse.ResponseHeader);
                response = (SetTriggeringResponse)genericResponse;
            }
            finally
            {
                RequestCompleted(request, response, "SetTriggering");
            }

            return response;
        }
#endif
        #endregion

        #region DeleteMonitoredItems Methods
        /// <summary>
        /// Invokes the DeleteMonitoredItems service.
        /// </summary>
        public override ResponseHeader DeleteMonitoredItems(
            RequestHeader requestHeader,
            uint subscriptionId,
            UInt32Collection monitoredItemIds,
            out StatusCodeCollection results,
            out DiagnosticInfoCollection diagnosticInfos)
        {
            DeleteMonitoredItemsRequest request = new DeleteMonitoredItemsRequest();
            DeleteMonitoredItemsResponse response = null;

            request.RequestHeader = requestHeader;
            request.SubscriptionId = subscriptionId;
            request.MonitoredItemIds = monitoredItemIds;

            UpdateRequestHeader(request, requestHeader == null, "DeleteMonitoredItems");

            try
            {
                IServiceResponse genericResponse = TransportChannel.SendRequest(request);

                if (genericResponse == null)
                {
                    throw new ServiceResultException(StatusCodes.BadUnknownResponse);
                }

                ValidateResponse(genericResponse.ResponseHeader);
                response = (DeleteMonitoredItemsResponse)genericResponse;

                results = response.Results;
                diagnosticInfos = response.DiagnosticInfos;
            }
            finally
            {
                RequestCompleted(request, response, "DeleteMonitoredItems");
            }

            return response.ResponseHeader;
        }

#if (CLIENT_ASYNC)
        /// <summary>
        /// Invokes the DeleteMonitoredItems service using async Task based request.
        /// </summary>
        public override async Task<DeleteMonitoredItemsResponse> DeleteMonitoredItemsAsync(
            RequestHeader requestHeader,
            uint subscriptionId,
            UInt32Collection monitoredItemIds,
            CancellationToken ct)
        {
            DeleteMonitoredItemsRequest request = new DeleteMonitoredItemsRequest();
            DeleteMonitoredItemsResponse response = null;

            request.RequestHeader = requestHeader;
            request.SubscriptionId = subscriptionId;
            request.MonitoredItemIds = monitoredItemIds;

            UpdateRequestHeader(request, requestHeader == null, "DeleteMonitoredItems");

            try
            {
                IServiceResponse genericResponse = await TransportChannel.SendRequestAsync(request, ct).ConfigureAwait(false);

                if (genericResponse == null)
                {
                    throw new ServiceResultException(StatusCodes.BadUnknownResponse);
                }

                ValidateResponse(genericResponse.ResponseHeader);
                response = (DeleteMonitoredItemsResponse)genericResponse;
            }
            finally
            {
                RequestCompleted(request, response, "DeleteMonitoredItems");
            }

            return response;
        }
#endif
        #endregion

        #region Private 
        private OperationLimits m_operationLimits;
        #endregion
    }
}
