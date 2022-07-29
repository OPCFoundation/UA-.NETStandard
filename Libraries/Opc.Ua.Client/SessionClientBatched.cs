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

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua
{
    /// <summary>
    /// The client side interface with support for batching according to operation limits.
    /// </summary>
    public class SessionClientBatched : SessionClient
    {
        #region Constructors
        /// <summary>
        /// Intializes the object with a channel and a message context.
        /// </summary>
        public SessionClientBatched(ITransportChannel channel)
        :
            base(channel)
        {
            m_operationLimits = new OperationLimits();
        }
        #endregion

        #region Public Properties
        /// <summary>
        /// The operation limits are used to batch the service requests.
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
            ResponseHeader responseHeader = null;
            results = new AddNodesResultCollection();
            diagnosticInfos = new DiagnosticInfoCollection();

            foreach (var batchNodesToAdd in
                nodesToAdd.Batch<AddNodesItem, AddNodesItemCollection>(OperationLimits.MaxNodesPerNodeManagement))
            {
                if (requestHeader != null)
                {
                    requestHeader.RequestHandle = 0;
                }

                responseHeader = base.AddNodes(requestHeader,
                    batchNodesToAdd,
                    out AddNodesResultCollection batchResults,
                    out DiagnosticInfoCollection batchDiagnosticInfos);

                ClientBase.ValidateResponse(batchResults, batchNodesToAdd);
                ClientBase.ValidateDiagnosticInfos(batchDiagnosticInfos, batchNodesToAdd);

                results.AddRange(batchResults);
                diagnosticInfos.AddRange(batchDiagnosticInfos);
            }

            return responseHeader;
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
            ResponseHeader responseHeader = null;
            results = new StatusCodeCollection();
            diagnosticInfos = new DiagnosticInfoCollection();

            foreach (var batchReferencesToAdd in
                referencesToAdd.Batch<AddReferencesItem, AddReferencesItemCollection>(OperationLimits.MaxNodesPerNodeManagement))
            {
                if (requestHeader != null)
                {
                    requestHeader.RequestHandle = 0;
                }

                responseHeader = base.AddReferences(requestHeader,
                    batchReferencesToAdd,
                    out StatusCodeCollection batchResults,
                    out DiagnosticInfoCollection batchDiagnosticInfos);

                ClientBase.ValidateResponse(batchResults, batchReferencesToAdd);
                ClientBase.ValidateDiagnosticInfos(batchDiagnosticInfos, batchReferencesToAdd);

                results.AddRange(batchResults);
                diagnosticInfos.AddRange(batchDiagnosticInfos);
            }

            return responseHeader;
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
            ResponseHeader responseHeader = null;
            results = null;
            diagnosticInfos = null;

            foreach (var batchNodesToDelete in
                nodesToDelete.Batch<DeleteNodesItem, DeleteNodesItemCollection>(OperationLimits.MaxNodesPerNodeManagement))
            {
                if (requestHeader != null)
                {
                    requestHeader.RequestHandle = 0;
                }

                responseHeader = base.DeleteNodes(requestHeader,
                    batchNodesToDelete,
                    out StatusCodeCollection batchResults,
                    out DiagnosticInfoCollection batchDiagnosticInfos);

                ClientBase.ValidateResponse(batchResults, batchNodesToDelete);
                ClientBase.ValidateDiagnosticInfos(batchDiagnosticInfos, batchNodesToDelete);

                if (results == null)
                {
                    results = batchResults;
                    diagnosticInfos = batchDiagnosticInfos;
                }
                else
                {
                    results.AddRange(batchResults);
                    diagnosticInfos.AddRange(batchDiagnosticInfos);
                }
            }

            return responseHeader;
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
            ResponseHeader responseHeader = null;
            results = new StatusCodeCollection();
            diagnosticInfos = new DiagnosticInfoCollection();

            foreach (var batchReferencesToDelete in
                referencesToDelete.Batch<DeleteReferencesItem, DeleteReferencesItemCollection>(OperationLimits.MaxNodesPerNodeManagement))
            {
                if (requestHeader != null)
                {
                    requestHeader.RequestHandle = 0;
                }

                responseHeader = base.DeleteReferences(requestHeader,
                    batchReferencesToDelete,
                    out StatusCodeCollection batchResults,
                    out DiagnosticInfoCollection batchDiagnosticInfos);

                ClientBase.ValidateResponse(batchResults, batchReferencesToDelete);
                ClientBase.ValidateDiagnosticInfos(batchDiagnosticInfos, batchReferencesToDelete);

                results.AddRange(batchResults);
                diagnosticInfos.AddRange(batchDiagnosticInfos);
            }

            return responseHeader;
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
            ResponseHeader responseHeader = null;
            results = new BrowseResultCollection();
            diagnosticInfos = new DiagnosticInfoCollection();

            foreach (var nodesToBrowseBatch in
                nodesToBrowse.Batch<BrowseDescription, BrowseDescriptionCollection>(OperationLimits.MaxNodesPerBrowse))
            {
                if (requestHeader != null)
                {
                    requestHeader.RequestHandle = 0;
                }

                responseHeader = base.Browse(requestHeader,
                    view,
                    requestedMaxReferencesPerNode,
                    nodesToBrowseBatch,
                    out BrowseResultCollection batchResults,
                    out DiagnosticInfoCollection batchDiagnosticInfos);

                ClientBase.ValidateResponse(batchResults, nodesToBrowseBatch);
                ClientBase.ValidateDiagnosticInfos(batchDiagnosticInfos, nodesToBrowseBatch);

                results.AddRange(batchResults);
                diagnosticInfos.AddRange(batchDiagnosticInfos);
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
            BrowseResponse response = null;
            var results = new BrowseResultCollection();
            var diagnosticInfos = new DiagnosticInfoCollection();

            foreach (var nodesToBrowseBatch in
                nodesToBrowse.Batch<BrowseDescription, BrowseDescriptionCollection>(OperationLimits.MaxNodesPerBrowse))
            {
                if (requestHeader != null)
                {
                    requestHeader.RequestHandle = 0;
                }

                response = await base.BrowseAsync(
                    requestHeader,
                    view,
                    requestedMaxReferencesPerNode,
                    nodesToBrowseBatch, ct).ConfigureAwait(false);

                BrowseResultCollection batchResults = response.Results;
                DiagnosticInfoCollection batchDiagnosticInfos = response.DiagnosticInfos;

                ClientBase.ValidateResponse(batchResults, nodesToBrowseBatch);
                ClientBase.ValidateDiagnosticInfos(batchDiagnosticInfos, nodesToBrowseBatch);

                results.AddRange(batchResults);
                diagnosticInfos.AddRange(batchDiagnosticInfos);
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
            ResponseHeader responseHeader = null;
            results = new BrowsePathResultCollection();
            diagnosticInfos = new DiagnosticInfoCollection();

            foreach (var batchBrowsePaths in
                browsePaths.Batch<BrowsePath, BrowsePathCollection>(OperationLimits.MaxNodesPerTranslateBrowsePathsToNodeIds))
            {
                if (requestHeader != null)
                {
                    requestHeader.RequestHandle = 0;
                }

                responseHeader = base.TranslateBrowsePathsToNodeIds(requestHeader,
                    batchBrowsePaths,
                    out BrowsePathResultCollection batchResults,
                    out DiagnosticInfoCollection batchDiagnosticInfos);

                ClientBase.ValidateResponse(batchResults, batchBrowsePaths);
                ClientBase.ValidateDiagnosticInfos(batchDiagnosticInfos, batchBrowsePaths);

                results.AddRange(batchResults);
                diagnosticInfos.AddRange(batchDiagnosticInfos);
            }

            return responseHeader;
        }

#if (CLIENT_ASYNC)
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
            ResponseHeader responseHeader = null;
            registeredNodeIds = new NodeIdCollection();

            foreach (var batchNodesToRegister in
                nodesToRegister.Batch<NodeId, NodeIdCollection>(OperationLimits.MaxNodesPerRegisterNodes))
            {
                if (requestHeader != null)
                {
                    requestHeader.RequestHandle = 0;
                }

                responseHeader = base.RegisterNodes(
                    requestHeader,
                    batchNodesToRegister,
                    out NodeIdCollection batchRegisteredNodeIds);

                ClientBase.ValidateResponse(batchRegisteredNodeIds, batchNodesToRegister);

                registeredNodeIds.AddRange(batchRegisteredNodeIds);
            }

            return responseHeader;
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
            ResponseHeader responseHeader = null;

            foreach (var batchNodesToUnregister in
                nodesToUnregister.Batch<NodeId, NodeIdCollection>(OperationLimits.MaxNodesPerRegisterNodes))
            {
                if (requestHeader != null)
                {
                    requestHeader.RequestHandle = 0;
                }

                responseHeader = base.UnregisterNodes(requestHeader, batchNodesToUnregister);
            }

            return responseHeader;
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
                    out DiagnosticInfoCollection batchDiagnosticInfos);

                ClientBase.ValidateResponse(chunkValues, chunkAttributesToRead);
                ClientBase.ValidateDiagnosticInfos(batchDiagnosticInfos, chunkAttributesToRead);

                results.AddRange(chunkValues);
                diagnosticInfos.AddRange(batchDiagnosticInfos);
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
                DiagnosticInfoCollection batchDiagnosticInfos = response.DiagnosticInfos;

                ClientBase.ValidateResponse(chunkValues, chunkAttributesToRead);
                ClientBase.ValidateDiagnosticInfos(batchDiagnosticInfos, chunkAttributesToRead);

                results.AddRange(chunkValues);
                diagnosticInfos.AddRange(batchDiagnosticInfos);
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
            ResponseHeader responseHeader = null;
            results = new HistoryReadResultCollection();
            diagnosticInfos = new DiagnosticInfoCollection();

            // TODO: handle ReadData/ReadEvent
            foreach (var batchNodesToRead in
                nodesToRead.Batch<HistoryReadValueId, HistoryReadValueIdCollection>(OperationLimits.MaxNodesPerHistoryReadData))
            {
                if (requestHeader != null)
                {
                    requestHeader.RequestHandle = 0;
                }

                responseHeader = base.HistoryRead(requestHeader,
                    historyReadDetails,
                    timestampsToReturn,
                    releaseContinuationPoints,
                    batchNodesToRead,
                    out HistoryReadResultCollection batchResults,
                    out DiagnosticInfoCollection batchDiagnosticInfos);

                ClientBase.ValidateResponse(batchResults, batchNodesToRead);
                ClientBase.ValidateDiagnosticInfos(batchDiagnosticInfos, batchNodesToRead);

                results.AddRange(batchResults);
                diagnosticInfos.AddRange(batchDiagnosticInfos);
            }

            return responseHeader;
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
            ResponseHeader responseHeader = null;
            results = new StatusCodeCollection();
            diagnosticInfos = new DiagnosticInfoCollection();

            foreach (var batchNodesToWrite in
                nodesToWrite.Batch<WriteValue, WriteValueCollection>(OperationLimits.MaxNodesPerBrowse))
            {
                if (requestHeader != null)
                {
                    requestHeader.RequestHandle = 0;
                }

                responseHeader = base.Write(requestHeader,
                    batchNodesToWrite,
                    out StatusCodeCollection batchResults,
                    out DiagnosticInfoCollection batchDiagnosticInfos);

                ClientBase.ValidateResponse(batchResults, batchNodesToWrite);
                ClientBase.ValidateDiagnosticInfos(batchDiagnosticInfos, batchNodesToWrite);

                results.AddRange(batchResults);
                diagnosticInfos.AddRange(batchDiagnosticInfos);
            }

            return responseHeader;
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
            ResponseHeader responseHeader = null;
            results = new HistoryUpdateResultCollection();
            diagnosticInfos = new DiagnosticInfoCollection();

            // TODO: handle data/event
            foreach (var batchHistoryUpdateDetails in
                historyUpdateDetails.Batch<ExtensionObject, ExtensionObjectCollection>(OperationLimits.MaxNodesPerHistoryUpdateData))
            {
                if (requestHeader != null)
                {
                    requestHeader.RequestHandle = 0;
                }

                responseHeader = base.HistoryUpdate(requestHeader,
                    batchHistoryUpdateDetails,
                    out HistoryUpdateResultCollection batchResults,
                    out DiagnosticInfoCollection batchDiagnosticInfos);

                ClientBase.ValidateResponse(batchResults, batchHistoryUpdateDetails);
                ClientBase.ValidateDiagnosticInfos(batchDiagnosticInfos, batchHistoryUpdateDetails);

                results.AddRange(batchResults);
                diagnosticInfos.AddRange(batchDiagnosticInfos);
            }

            return responseHeader;
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
            ResponseHeader responseHeader = null;
            results = new CallMethodResultCollection();
            diagnosticInfos = new DiagnosticInfoCollection();

            foreach (var batchMethodsToCall in
                methodsToCall.Batch<CallMethodRequest, CallMethodRequestCollection>(OperationLimits.MaxNodesPerMethodCall))
            {
                if (requestHeader != null)
                {
                    requestHeader.RequestHandle = 0;
                }

                responseHeader = base.Call(requestHeader,
                    batchMethodsToCall,
                    out CallMethodResultCollection batchResults,
                    out DiagnosticInfoCollection batchDiagnosticInfos);

                ClientBase.ValidateResponse(batchResults, batchMethodsToCall);
                ClientBase.ValidateDiagnosticInfos(batchDiagnosticInfos, batchMethodsToCall);

                results.AddRange(batchResults);
                diagnosticInfos.AddRange(batchDiagnosticInfos);
            }

            return responseHeader;
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
            ResponseHeader responseHeader = null;
            results = new MonitoredItemCreateResultCollection();
            diagnosticInfos = new DiagnosticInfoCollection();

            foreach (var batchItemsToCreate in
                itemsToCreate.Batch<MonitoredItemCreateRequest, MonitoredItemCreateRequestCollection>(OperationLimits.MaxMonitoredItemsPerCall))
            {
                if (requestHeader != null)
                {
                    requestHeader.RequestHandle = 0;
                }

                responseHeader = base.CreateMonitoredItems(requestHeader,
                    subscriptionId,
                    timestampsToReturn,
                    batchItemsToCreate,
                    out MonitoredItemCreateResultCollection batchResults,
                    out DiagnosticInfoCollection batchDiagnosticInfos);

                ClientBase.ValidateResponse(batchResults, batchItemsToCreate);
                ClientBase.ValidateDiagnosticInfos(batchDiagnosticInfos, batchItemsToCreate);

                results.AddRange(batchResults);
                diagnosticInfos.AddRange(batchDiagnosticInfos);
            }

            return responseHeader;
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
            ResponseHeader responseHeader = null;
            results = new MonitoredItemModifyResultCollection();
            diagnosticInfos = new DiagnosticInfoCollection();

            foreach (var batchItemsToModify in
                itemsToModify.Batch<MonitoredItemModifyRequest, MonitoredItemModifyRequestCollection>(OperationLimits.MaxMonitoredItemsPerCall))
            {
                if (requestHeader != null)
                {
                    requestHeader.RequestHandle = 0;
                }

                responseHeader = base.ModifyMonitoredItems(requestHeader,
                    subscriptionId,
                    timestampsToReturn,
                    batchItemsToModify,
                    out MonitoredItemModifyResultCollection batchResults,
                    out DiagnosticInfoCollection batchDiagnosticInfos);

                ClientBase.ValidateResponse(batchResults, batchItemsToModify);
                ClientBase.ValidateDiagnosticInfos(batchDiagnosticInfos, batchItemsToModify);

                results.AddRange(batchResults);
                diagnosticInfos.AddRange(batchDiagnosticInfos);
            }

            return responseHeader;
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
            ResponseHeader responseHeader = null;
            results = new StatusCodeCollection();
            diagnosticInfos = new DiagnosticInfoCollection();

            foreach (var batchMonitoredItemIds in
                monitoredItemIds.Batch<UInt32, UInt32Collection>(OperationLimits.MaxMonitoredItemsPerCall))
            {
                if (requestHeader != null)
                {
                    requestHeader.RequestHandle = 0;
                }

                responseHeader = base.SetMonitoringMode(requestHeader,
                    subscriptionId,
                    monitoringMode,
                    batchMonitoredItemIds,
                    out StatusCodeCollection batchResults,
                    out DiagnosticInfoCollection batchDiagnosticInfos);

                ClientBase.ValidateResponse(batchResults, batchMonitoredItemIds);
                ClientBase.ValidateDiagnosticInfos(batchDiagnosticInfos, batchMonitoredItemIds);

                results.AddRange(batchResults);
                diagnosticInfos.AddRange(batchDiagnosticInfos);
            }

            return responseHeader;
        }

#if (CLIENT_ASYNC)
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
#if TODO
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
            ResponseHeader responseHeader = null;
            addResults = new StatusCodeCollection();
            addDiagnosticInfos = new DiagnosticInfoCollection();
            removeResults = new StatusCodeCollection();
            removeDiagnosticInfos = new DiagnosticInfoCollection();

            foreach (var nodesToBrowseBatch in
                linksToAdd.Batch<UInt32, UInt32Collection>(OperationLimits.MaxMonitoredItemsPerCall))
            {
                if (requestHeader != null)
                {
                    requestHeader.RequestHandle = 0;
                }

                responseHeader = base.SetTriggering(requestHeader,
                    subscriptionId,
                    triggeringItemId,
                    nodesToBrowseBatch,
                    out BrowseResultCollection batchResults,
                    out DiagnosticInfoCollection batchDiagnosticInfos);

                ClientBase.ValidateResponse(batchResults, nodesToBrowseBatch);
                ClientBase.ValidateDiagnosticInfos(batchDiagnosticInfos, nodesToBrowseBatch);

                results.AddRange(batchResults);
                diagnosticInfos.AddRange(batchDiagnosticInfos);
            }

            return responseHeader;
        }
#if (CLIENT_ASYNC)
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
            ResponseHeader responseHeader = null;
            results = new StatusCodeCollection();
            diagnosticInfos = new DiagnosticInfoCollection();

            foreach (var batchMonitoredItemIds in
                monitoredItemIds.Batch<UInt32, UInt32Collection>(OperationLimits.MaxMonitoredItemsPerCall))
            {
                if (requestHeader != null)
                {
                    requestHeader.RequestHandle = 0;
                }

                responseHeader = base.DeleteMonitoredItems(requestHeader,
                    subscriptionId,
                    batchMonitoredItemIds,
                    out StatusCodeCollection batchResults,
                    out DiagnosticInfoCollection batchDiagnosticInfos);

                ClientBase.ValidateResponse(batchResults, batchMonitoredItemIds);
                ClientBase.ValidateDiagnosticInfos(batchDiagnosticInfos, batchMonitoredItemIds);

                results.AddRange(batchResults);
                diagnosticInfos.AddRange(batchDiagnosticInfos);
            }

            return responseHeader;
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

    /// <summary>
    /// Extension helpers for client service calls.
    /// </summary>
    public static class SessionClientExtensions
    {
        /// <summary>
        /// Returns batches of a collection for processing.
        /// </summary>
        /// <remarks>
        /// Returns the original collection if batchsize is 0 or the collection count is smaller than the batch size.
        /// </remarks>
        /// <typeparam name="T">The type of the items in the collection.</typeparam>
        /// <typeparam name="C">The type of the items in the collection.</typeparam>
        /// <param name="collection">The collection from which items are batched.</param>
        /// <param name="batchSize">The size of a batch.</param>
        /// <returns>The collection.</returns>
        internal static IEnumerable<C> Batch<T, C>(this C collection, uint batchSize) where C : List<T>, new()
        {
            if (collection.Count < batchSize || batchSize == 0)
            {
                yield return collection;
            }
            else
            {
                C nextbatch = new C {
                    Capacity = (int)batchSize
                };
                foreach (T item in collection)
                {
                    nextbatch.Add(item);
                    if (nextbatch.Count == batchSize)
                    {
                        yield return nextbatch;
                        nextbatch = new C {
                            Capacity = (int)batchSize
                        };
                    }
                }
                if (nextbatch.Count > 0)
                {
                    yield return nextbatch;
                }
            }
        }
    }
}
