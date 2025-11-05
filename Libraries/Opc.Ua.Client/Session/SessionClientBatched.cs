/* ========================================================================
 * Copyright (c) 2005-2022 The OPC Foundation, Inc. All rights reserved.
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

#nullable disable

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
        /// <summary>
        /// Intializes the object with a channel and default operation limits.
        /// </summary>
        public SessionClientBatched(ITransportChannel channel, ITelemetryContext telemetry)
            : base(channel, telemetry)
        {
            m_operationLimits = new OperationLimits();
        }

        /// <summary>
        /// The operation limits are used to batch the service requests.
        /// </summary>
        public OperationLimits OperationLimits
        {
            get => m_operationLimits;
            protected internal set => m_operationLimits = value ?? new OperationLimits();
        }

        /// <inheritdoc/>
        public override async Task<AddNodesResponse> AddNodesAsync(
            RequestHeader requestHeader,
            AddNodesItemCollection nodesToAdd,
            CancellationToken ct)
        {
            AddNodesResponse response = null;

            uint operationLimit = OperationLimits.MaxNodesPerNodeManagement;
            InitResponseCollections<AddNodesResult, AddNodesResultCollection>(
                out AddNodesResultCollection results,
                out DiagnosticInfoCollection diagnosticInfos,
                out StringCollection stringTable,
                nodesToAdd.Count,
                operationLimit);

            foreach (
                AddNodesItemCollection batchNodesToAdd in nodesToAdd
                    .Batch<AddNodesItem, AddNodesItemCollection>(
                        operationLimit))
            {
                if (requestHeader != null)
                {
                    requestHeader.RequestHandle = 0;
                }

                response = await base.AddNodesAsync(requestHeader, batchNodesToAdd, ct)
                    .ConfigureAwait(false);

                AddNodesResultCollection batchResults = response.Results;
                DiagnosticInfoCollection batchDiagnosticInfos = response.DiagnosticInfos;

                ValidateResponse(batchResults, batchNodesToAdd);
                ValidateDiagnosticInfos(batchDiagnosticInfos, batchNodesToAdd);

                AddResponses<AddNodesResult, AddNodesResultCollection>(
                    ref results,
                    ref diagnosticInfos,
                    ref stringTable,
                    batchResults,
                    batchDiagnosticInfos,
                    response.ResponseHeader.StringTable);
            }

            response.Results = results;
            response.DiagnosticInfos = diagnosticInfos;
            response.ResponseHeader.StringTable = stringTable;

            return response;
        }

        /// <inheritdoc/>
        public override async Task<AddReferencesResponse> AddReferencesAsync(
            RequestHeader requestHeader,
            AddReferencesItemCollection referencesToAdd,
            CancellationToken ct)
        {
            AddReferencesResponse response = null;

            uint operationLimit = OperationLimits.MaxNodesPerNodeManagement;
            InitResponseCollections<StatusCode, StatusCodeCollection>(
                out StatusCodeCollection results,
                out DiagnosticInfoCollection diagnosticInfos,
                out StringCollection stringTable,
                referencesToAdd.Count,
                operationLimit);

            foreach (
                AddReferencesItemCollection batchReferencesToAdd in referencesToAdd.Batch<
                    AddReferencesItem,
                    AddReferencesItemCollection
                >(operationLimit))
            {
                if (requestHeader != null)
                {
                    requestHeader.RequestHandle = 0;
                }

                response = await base.AddReferencesAsync(requestHeader, batchReferencesToAdd, ct)
                    .ConfigureAwait(false);

                StatusCodeCollection batchResults = response.Results;
                DiagnosticInfoCollection batchDiagnosticInfos = response.DiagnosticInfos;

                ValidateResponse(batchResults, batchReferencesToAdd);
                ValidateDiagnosticInfos(batchDiagnosticInfos, batchReferencesToAdd);

                AddResponses<StatusCode, StatusCodeCollection>(
                    ref results,
                    ref diagnosticInfos,
                    ref stringTable,
                    batchResults,
                    batchDiagnosticInfos,
                    response.ResponseHeader.StringTable);
            }

            response.Results = results;
            response.DiagnosticInfos = diagnosticInfos;
            response.ResponseHeader.StringTable = stringTable;

            return response;
        }

        /// <inheritdoc/>
        public override async Task<DeleteNodesResponse> DeleteNodesAsync(
            RequestHeader requestHeader,
            DeleteNodesItemCollection nodesToDelete,
            CancellationToken ct)
        {
            DeleteNodesResponse response = null;

            uint operationLimit = OperationLimits.MaxNodesPerNodeManagement;
            InitResponseCollections<StatusCode, StatusCodeCollection>(
                out StatusCodeCollection results,
                out DiagnosticInfoCollection diagnosticInfos,
                out StringCollection stringTable,
                nodesToDelete.Count,
                operationLimit);

            foreach (
                DeleteNodesItemCollection batchNodesToDelete in nodesToDelete.Batch<
                    DeleteNodesItem,
                    DeleteNodesItemCollection
                >(operationLimit))
            {
                if (requestHeader != null)
                {
                    requestHeader.RequestHandle = 0;
                }

                response = await base.DeleteNodesAsync(requestHeader, batchNodesToDelete, ct)
                    .ConfigureAwait(false);

                StatusCodeCollection batchResults = response.Results;
                DiagnosticInfoCollection batchDiagnosticInfos = response.DiagnosticInfos;

                ValidateResponse(batchResults, batchNodesToDelete);
                ValidateDiagnosticInfos(batchDiagnosticInfos, batchNodesToDelete);

                AddResponses<StatusCode, StatusCodeCollection>(
                    ref results,
                    ref diagnosticInfos,
                    ref stringTable,
                    batchResults,
                    batchDiagnosticInfos,
                    response.ResponseHeader.StringTable);
            }

            response.Results = results;
            response.DiagnosticInfos = diagnosticInfos;
            response.ResponseHeader.StringTable = stringTable;

            return response;
        }

        /// <inheritdoc/>
        public override async Task<DeleteReferencesResponse> DeleteReferencesAsync(
            RequestHeader requestHeader,
            DeleteReferencesItemCollection referencesToDelete,
            CancellationToken ct)
        {
            DeleteReferencesResponse response = null;

            uint operationLimit = OperationLimits.MaxNodesPerNodeManagement;
            InitResponseCollections<StatusCode, StatusCodeCollection>(
                out StatusCodeCollection results,
                out DiagnosticInfoCollection diagnosticInfos,
                out StringCollection stringTable,
                referencesToDelete.Count,
                operationLimit);

            foreach (
                DeleteReferencesItemCollection batchReferencesToDelete in referencesToDelete.Batch<
                    DeleteReferencesItem,
                    DeleteReferencesItemCollection
                >(operationLimit))
            {
                if (requestHeader != null)
                {
                    requestHeader.RequestHandle = 0;
                }

                response = await base.DeleteReferencesAsync(
                    requestHeader,
                    batchReferencesToDelete,
                    ct)
                    .ConfigureAwait(false);

                StatusCodeCollection batchResults = response.Results;
                DiagnosticInfoCollection batchDiagnosticInfos = response.DiagnosticInfos;

                ValidateResponse(batchResults, batchReferencesToDelete);
                ValidateDiagnosticInfos(batchDiagnosticInfos, batchReferencesToDelete);

                AddResponses<StatusCode, StatusCodeCollection>(
                    ref results,
                    ref diagnosticInfos,
                    ref stringTable,
                    batchResults,
                    batchDiagnosticInfos,
                    response.ResponseHeader.StringTable);
            }

            response.Results = results;
            response.DiagnosticInfos = diagnosticInfos;
            response.ResponseHeader.StringTable = stringTable;

            return response;
        }

        /// <inheritdoc/>
        public override async Task<BrowseResponse> BrowseAsync(
            RequestHeader requestHeader,
            ViewDescription view,
            uint requestedMaxReferencesPerNode,
            BrowseDescriptionCollection nodesToBrowse,
            CancellationToken ct)
        {
            BrowseResponse response = null;

            uint operationLimit = OperationLimits.MaxNodesPerBrowse;
            InitResponseCollections<BrowseResult, BrowseResultCollection>(
                out BrowseResultCollection results,
                out DiagnosticInfoCollection diagnosticInfos,
                out StringCollection stringTable,
                nodesToBrowse.Count,
                operationLimit);

            foreach (
                BrowseDescriptionCollection nodesToBrowseBatch in nodesToBrowse.Batch<
                    BrowseDescription,
                    BrowseDescriptionCollection
                >(operationLimit))
            {
                if (requestHeader != null)
                {
                    requestHeader.RequestHandle = 0;
                }

                response = await base.BrowseAsync(
                        requestHeader,
                        view,
                        requestedMaxReferencesPerNode,
                        nodesToBrowseBatch,
                        ct)
                    .ConfigureAwait(false);

                BrowseResultCollection batchResults = response.Results;
                DiagnosticInfoCollection batchDiagnosticInfos = response.DiagnosticInfos;

                ValidateResponse(batchResults, nodesToBrowseBatch);
                ValidateDiagnosticInfos(batchDiagnosticInfos, nodesToBrowseBatch);

                AddResponses<BrowseResult, BrowseResultCollection>(
                    ref results,
                    ref diagnosticInfos,
                    ref stringTable,
                    batchResults,
                    batchDiagnosticInfos,
                    response.ResponseHeader.StringTable);
            }

            response.Results = results;
            response.DiagnosticInfos = diagnosticInfos;
            response.ResponseHeader.StringTable = stringTable;

            return response;
        }

        /// <inheritdoc/>
        public override async Task<TranslateBrowsePathsToNodeIdsResponse> TranslateBrowsePathsToNodeIdsAsync(
            RequestHeader requestHeader,
            BrowsePathCollection browsePaths,
            CancellationToken ct)
        {
            TranslateBrowsePathsToNodeIdsResponse response = null;

            uint operationLimit = OperationLimits.MaxNodesPerTranslateBrowsePathsToNodeIds;
            InitResponseCollections<BrowsePathResult, BrowsePathResultCollection>(
                out BrowsePathResultCollection results,
                out DiagnosticInfoCollection diagnosticInfos,
                out StringCollection stringTable,
                browsePaths.Count,
                operationLimit);

            foreach (
                BrowsePathCollection batchBrowsePaths in browsePaths
                    .Batch<BrowsePath, BrowsePathCollection>(
                        operationLimit))
            {
                if (requestHeader != null)
                {
                    requestHeader.RequestHandle = 0;
                }

                response = await base.TranslateBrowsePathsToNodeIdsAsync(
                    requestHeader,
                    batchBrowsePaths,
                    ct)
                    .ConfigureAwait(false);

                BrowsePathResultCollection batchResults = response.Results;
                DiagnosticInfoCollection batchDiagnosticInfos = response.DiagnosticInfos;

                ValidateResponse(batchResults, batchBrowsePaths);
                ValidateDiagnosticInfos(batchDiagnosticInfos, batchBrowsePaths);

                AddResponses<BrowsePathResult, BrowsePathResultCollection>(
                    ref results,
                    ref diagnosticInfos,
                    ref stringTable,
                    batchResults,
                    batchDiagnosticInfos,
                    response.ResponseHeader.StringTable);
            }

            response.Results = results;
            response.DiagnosticInfos = diagnosticInfos;
            response.ResponseHeader.StringTable = stringTable;

            return response;
        }

        /// <inheritdoc/>
        public override async Task<RegisterNodesResponse> RegisterNodesAsync(
            RequestHeader requestHeader,
            NodeIdCollection nodesToRegister,
            CancellationToken ct)
        {
            RegisterNodesResponse response = null;
            var registeredNodeIds = new NodeIdCollection();

            foreach (
                NodeIdCollection batchNodesToRegister in nodesToRegister
                    .Batch<NodeId, NodeIdCollection>(
                        OperationLimits.MaxNodesPerRegisterNodes))
            {
                if (requestHeader != null)
                {
                    requestHeader.RequestHandle = 0;
                }

                response = await base.RegisterNodesAsync(requestHeader, batchNodesToRegister, ct)
                    .ConfigureAwait(false);

                NodeIdCollection batchRegisteredNodeIds = response.RegisteredNodeIds;

                ValidateResponse(batchRegisteredNodeIds, batchNodesToRegister);

                registeredNodeIds.AddRange(batchRegisteredNodeIds);
            }

            response.RegisteredNodeIds = registeredNodeIds;

            return response;
        }

        /// <inheritdoc/>
        public override async Task<UnregisterNodesResponse> UnregisterNodesAsync(
            RequestHeader requestHeader,
            NodeIdCollection nodesToUnregister,
            CancellationToken ct)
        {
            UnregisterNodesResponse response = null;

            foreach (
                NodeIdCollection batchNodesToUnregister in nodesToUnregister
                    .Batch<NodeId, NodeIdCollection>(
                        OperationLimits.MaxNodesPerRegisterNodes))
            {
                if (requestHeader != null)
                {
                    requestHeader.RequestHandle = 0;
                }

                response = await base.UnregisterNodesAsync(
                    requestHeader,
                    batchNodesToUnregister,
                    ct)
                    .ConfigureAwait(false);
            }

            return response;
        }

        /// <inheritdoc/>
        public override async Task<ReadResponse> ReadAsync(
            RequestHeader requestHeader,
            double maxAge,
            TimestampsToReturn timestampsToReturn,
            ReadValueIdCollection nodesToRead,
            CancellationToken ct)
        {
            ReadResponse response = null;

            uint operationLimit = OperationLimits.MaxNodesPerRead;
            InitResponseCollections<DataValue, DataValueCollection>(
                out DataValueCollection results,
                out DiagnosticInfoCollection diagnosticInfos,
                out StringCollection stringTable,
                nodesToRead.Count,
                operationLimit);

            foreach (
                ReadValueIdCollection batchAttributesToRead in nodesToRead
                    .Batch<ReadValueId, ReadValueIdCollection>(
                        operationLimit))
            {
                if (requestHeader != null)
                {
                    requestHeader.RequestHandle = 0;
                }

                response = await base.ReadAsync(
                    requestHeader,
                    maxAge,
                    timestampsToReturn,
                    batchAttributesToRead,
                    ct)
                    .ConfigureAwait(false);

                DataValueCollection batchResults = response.Results;
                DiagnosticInfoCollection batchDiagnosticInfos = response.DiagnosticInfos;

                ValidateResponse(batchResults, batchAttributesToRead);
                ValidateDiagnosticInfos(batchDiagnosticInfos, batchAttributesToRead);

                AddResponses<DataValue, DataValueCollection>(
                    ref results,
                    ref diagnosticInfos,
                    ref stringTable,
                    batchResults,
                    batchDiagnosticInfos,
                    response.ResponseHeader.StringTable);
            }

            response.Results = results;
            response.DiagnosticInfos = diagnosticInfos;
            response.ResponseHeader.StringTable = stringTable;

            return response;
        }

        /// <inheritdoc/>
        public override async Task<HistoryReadResponse> HistoryReadAsync(
            RequestHeader requestHeader,
            ExtensionObject historyReadDetails,
            TimestampsToReturn timestampsToReturn,
            bool releaseContinuationPoints,
            HistoryReadValueIdCollection nodesToRead,
            CancellationToken ct)
        {
            HistoryReadResponse response = null;

            uint operationLimit = OperationLimits.MaxNodesPerHistoryReadData;
            if (historyReadDetails?.Body is ReadEventDetails)
            {
                operationLimit = OperationLimits.MaxNodesPerHistoryReadEvents;
            }

            InitResponseCollections<HistoryReadResult, HistoryReadResultCollection>(
                out HistoryReadResultCollection results,
                out DiagnosticInfoCollection diagnosticInfos,
                out StringCollection stringTable,
                nodesToRead.Count,
                operationLimit);

            foreach (
                HistoryReadValueIdCollection batchNodesToRead in nodesToRead.Batch<
                    HistoryReadValueId,
                    HistoryReadValueIdCollection
                >(operationLimit))
            {
                if (requestHeader != null)
                {
                    requestHeader.RequestHandle = 0;
                }

                response = await base.HistoryReadAsync(
                        requestHeader,
                        historyReadDetails,
                        timestampsToReturn,
                        releaseContinuationPoints,
                        batchNodesToRead,
                        ct)
                    .ConfigureAwait(false);

                HistoryReadResultCollection batchResults = response.Results;
                DiagnosticInfoCollection batchDiagnosticInfos = response.DiagnosticInfos;

                ValidateResponse(batchResults, batchNodesToRead);
                ValidateDiagnosticInfos(batchDiagnosticInfos, batchNodesToRead);

                AddResponses<HistoryReadResult, HistoryReadResultCollection>(
                    ref results,
                    ref diagnosticInfos,
                    ref stringTable,
                    batchResults,
                    batchDiagnosticInfos,
                    response.ResponseHeader.StringTable);
            }

            response.Results = results;
            response.DiagnosticInfos = diagnosticInfos;
            response.ResponseHeader.StringTable = stringTable;

            return response;
        }

        /// <inheritdoc/>
        public override async Task<WriteResponse> WriteAsync(
            RequestHeader requestHeader,
            WriteValueCollection nodesToWrite,
            CancellationToken ct)
        {
            WriteResponse response = null;

            uint operationLimit = OperationLimits.MaxNodesPerWrite;
            InitResponseCollections<StatusCode, StatusCodeCollection>(
                out StatusCodeCollection results,
                out DiagnosticInfoCollection diagnosticInfos,
                out StringCollection stringTable,
                nodesToWrite.Count,
                operationLimit);

            foreach (
                WriteValueCollection batchNodesToWrite in nodesToWrite
                    .Batch<WriteValue, WriteValueCollection>(
                        operationLimit))
            {
                if (requestHeader != null)
                {
                    requestHeader.RequestHandle = 0;
                }

                response = await base.WriteAsync(requestHeader, batchNodesToWrite, ct)
                    .ConfigureAwait(false);

                StatusCodeCollection batchResults = response.Results;
                DiagnosticInfoCollection batchDiagnosticInfos = response.DiagnosticInfos;

                ValidateResponse(batchResults, batchNodesToWrite);
                ValidateDiagnosticInfos(batchDiagnosticInfos, batchNodesToWrite);

                AddResponses<StatusCode, StatusCodeCollection>(
                    ref results,
                    ref diagnosticInfos,
                    ref stringTable,
                    batchResults,
                    batchDiagnosticInfos,
                    response.ResponseHeader.StringTable);
            }

            response.Results = results;
            response.DiagnosticInfos = diagnosticInfos;
            response.ResponseHeader.StringTable = stringTable;

            return response;
        }

        /// <inheritdoc/>
        public override async Task<HistoryUpdateResponse> HistoryUpdateAsync(
            RequestHeader requestHeader,
            ExtensionObjectCollection historyUpdateDetails,
            CancellationToken ct)
        {
            HistoryUpdateResponse response = null;

            uint operationLimit = OperationLimits.MaxNodesPerHistoryUpdateData;
            if (historyUpdateDetails.Count > 0 &&
                historyUpdateDetails[0].TypeId == DataTypeIds.UpdateEventDetails)
            {
                operationLimit = OperationLimits.MaxNodesPerHistoryUpdateEvents;
            }

            InitResponseCollections<HistoryUpdateResult, HistoryUpdateResultCollection>(
                out HistoryUpdateResultCollection results,
                out DiagnosticInfoCollection diagnosticInfos,
                out StringCollection stringTable,
                historyUpdateDetails.Count,
                operationLimit);

            foreach (
                ExtensionObjectCollection batchHistoryUpdateDetails in historyUpdateDetails.Batch<
                    ExtensionObject,
                    ExtensionObjectCollection
                >(operationLimit))
            {
                if (requestHeader != null)
                {
                    requestHeader.RequestHandle = 0;
                }

                response = await base.HistoryUpdateAsync(
                    requestHeader,
                    batchHistoryUpdateDetails,
                    ct)
                    .ConfigureAwait(false);
                HistoryUpdateResultCollection batchResults = response.Results;
                DiagnosticInfoCollection batchDiagnosticInfos = response.DiagnosticInfos;

                ValidateResponse(batchResults, batchHistoryUpdateDetails);
                ValidateDiagnosticInfos(batchDiagnosticInfos, batchHistoryUpdateDetails);

                AddResponses<HistoryUpdateResult, HistoryUpdateResultCollection>(
                    ref results,
                    ref diagnosticInfos,
                    ref stringTable,
                    batchResults,
                    batchDiagnosticInfos,
                    response.ResponseHeader.StringTable);
            }

            response.Results = results;
            response.DiagnosticInfos = diagnosticInfos;
            response.ResponseHeader.StringTable = stringTable;

            return response;
        }

        /// <inheritdoc/>
        public override async Task<CallResponse> CallAsync(
            RequestHeader requestHeader,
            CallMethodRequestCollection methodsToCall,
            CancellationToken ct)
        {
            CallResponse response = null;

            uint operationLimit = OperationLimits.MaxNodesPerMethodCall;
            InitResponseCollections<CallMethodResult, CallMethodResultCollection>(
                out CallMethodResultCollection results,
                out DiagnosticInfoCollection diagnosticInfos,
                out StringCollection stringTable,
                methodsToCall.Count,
                operationLimit);

            foreach (
                CallMethodRequestCollection batchMethodsToCall in methodsToCall.Batch<
                    CallMethodRequest,
                    CallMethodRequestCollection
                >(operationLimit))
            {
                if (requestHeader != null)
                {
                    requestHeader.RequestHandle = 0;
                }

                response = await base.CallAsync(requestHeader, batchMethodsToCall, ct)
                    .ConfigureAwait(false);

                CallMethodResultCollection batchResults = response.Results;
                DiagnosticInfoCollection batchDiagnosticInfos = response.DiagnosticInfos;

                ValidateResponse(batchResults, batchMethodsToCall);
                ValidateDiagnosticInfos(batchDiagnosticInfos, batchMethodsToCall);

                AddResponses<CallMethodResult, CallMethodResultCollection>(
                    ref results,
                    ref diagnosticInfos,
                    ref stringTable,
                    batchResults,
                    batchDiagnosticInfos,
                    response.ResponseHeader.StringTable);
            }

            response.Results = results;
            response.DiagnosticInfos = diagnosticInfos;
            response.ResponseHeader.StringTable = stringTable;

            return response;
        }

        /// <inheritdoc/>
        public override async Task<CreateMonitoredItemsResponse> CreateMonitoredItemsAsync(
            RequestHeader requestHeader,
            uint subscriptionId,
            TimestampsToReturn timestampsToReturn,
            MonitoredItemCreateRequestCollection itemsToCreate,
            CancellationToken ct)
        {
            CreateMonitoredItemsResponse response = null;

            uint operationLimit = OperationLimits.MaxMonitoredItemsPerCall;
            InitResponseCollections<MonitoredItemCreateResult, MonitoredItemCreateResultCollection>(
                out MonitoredItemCreateResultCollection results,
                out DiagnosticInfoCollection diagnosticInfos,
                out StringCollection stringTable,
                itemsToCreate.Count,
                operationLimit);

            foreach (
                MonitoredItemCreateRequestCollection batchItemsToCreate in itemsToCreate.Batch<
                    MonitoredItemCreateRequest,
                    MonitoredItemCreateRequestCollection
                >(operationLimit))
            {
                if (requestHeader != null)
                {
                    requestHeader.RequestHandle = 0;
                }

                response = await base.CreateMonitoredItemsAsync(
                        requestHeader,
                        subscriptionId,
                        timestampsToReturn,
                        batchItemsToCreate,
                        ct)
                    .ConfigureAwait(false);

                MonitoredItemCreateResultCollection batchResults = response.Results;
                DiagnosticInfoCollection batchDiagnosticInfos = response.DiagnosticInfos;

                ValidateResponse(batchResults, batchItemsToCreate);
                ValidateDiagnosticInfos(batchDiagnosticInfos, batchItemsToCreate);

                AddResponses<MonitoredItemCreateResult, MonitoredItemCreateResultCollection>(
                    ref results,
                    ref diagnosticInfos,
                    ref stringTable,
                    batchResults,
                    batchDiagnosticInfos,
                    response.ResponseHeader.StringTable);
            }

            response.Results = results;
            response.DiagnosticInfos = diagnosticInfos;
            response.ResponseHeader.StringTable = stringTable;

            return response;
        }

        /// <inheritdoc/>
        public override async Task<ModifyMonitoredItemsResponse> ModifyMonitoredItemsAsync(
            RequestHeader requestHeader,
            uint subscriptionId,
            TimestampsToReturn timestampsToReturn,
            MonitoredItemModifyRequestCollection itemsToModify,
            CancellationToken ct)
        {
            ModifyMonitoredItemsResponse response = null;

            uint operationLimit = OperationLimits.MaxMonitoredItemsPerCall;
            InitResponseCollections<MonitoredItemModifyResult, MonitoredItemModifyResultCollection>(
                out MonitoredItemModifyResultCollection results,
                out DiagnosticInfoCollection diagnosticInfos,
                out StringCollection stringTable,
                itemsToModify.Count,
                operationLimit);

            foreach (
                MonitoredItemModifyRequestCollection batchItemsToModify in itemsToModify.Batch<
                    MonitoredItemModifyRequest,
                    MonitoredItemModifyRequestCollection
                >(operationLimit))
            {
                if (requestHeader != null)
                {
                    requestHeader.RequestHandle = 0;
                }

                response = await base.ModifyMonitoredItemsAsync(
                        requestHeader,
                        subscriptionId,
                        timestampsToReturn,
                        batchItemsToModify,
                        ct)
                    .ConfigureAwait(false);

                MonitoredItemModifyResultCollection batchResults = response.Results;
                DiagnosticInfoCollection batchDiagnosticInfos = response.DiagnosticInfos;

                ValidateResponse(batchResults, batchItemsToModify);
                ValidateDiagnosticInfos(batchDiagnosticInfos, batchItemsToModify);

                AddResponses<MonitoredItemModifyResult, MonitoredItemModifyResultCollection>(
                    ref results,
                    ref diagnosticInfos,
                    ref stringTable,
                    batchResults,
                    batchDiagnosticInfos,
                    response.ResponseHeader.StringTable);
            }

            response.Results = results;
            response.DiagnosticInfos = diagnosticInfos;
            response.ResponseHeader.StringTable = stringTable;

            return response;
        }

        /// <inheritdoc/>
        public override async Task<SetMonitoringModeResponse> SetMonitoringModeAsync(
            RequestHeader requestHeader,
            uint subscriptionId,
            MonitoringMode monitoringMode,
            UInt32Collection monitoredItemIds,
            CancellationToken ct)
        {
            SetMonitoringModeResponse response = null;

            uint operationLimit = OperationLimits.MaxMonitoredItemsPerCall;
            InitResponseCollections<StatusCode, StatusCodeCollection>(
                out StatusCodeCollection results,
                out DiagnosticInfoCollection diagnosticInfos,
                out StringCollection stringTable,
                monitoredItemIds.Count,
                operationLimit);

            foreach (
                UInt32Collection batchMonitoredItemIds in monitoredItemIds
                    .Batch<uint, UInt32Collection>(operationLimit))
            {
                if (requestHeader != null)
                {
                    requestHeader.RequestHandle = 0;
                }

                response = await base.SetMonitoringModeAsync(
                        requestHeader,
                        subscriptionId,
                        monitoringMode,
                        batchMonitoredItemIds,
                        ct)
                    .ConfigureAwait(false);

                StatusCodeCollection batchResults = response.Results;
                DiagnosticInfoCollection batchDiagnosticInfos = response.DiagnosticInfos;

                ValidateResponse(batchResults, batchMonitoredItemIds);
                ValidateDiagnosticInfos(batchDiagnosticInfos, batchMonitoredItemIds);

                AddResponses<StatusCode, StatusCodeCollection>(
                    ref results,
                    ref diagnosticInfos,
                    ref stringTable,
                    batchResults,
                    batchDiagnosticInfos,
                    response.ResponseHeader.StringTable);
            }

            response.Results = results;
            response.DiagnosticInfos = diagnosticInfos;
            response.ResponseHeader.StringTable = stringTable;

            return response;
        }

        /// <inheritdoc/>
        public override async Task<SetTriggeringResponse> SetTriggeringAsync(
            RequestHeader requestHeader,
            uint subscriptionId,
            uint triggeringItemId,
            UInt32Collection linksToAdd,
            UInt32Collection linksToRemove,
            CancellationToken ct)
        {
            SetTriggeringResponse response = null;

            uint operationLimit = OperationLimits.MaxMonitoredItemsPerCall;
            InitResponseCollections<StatusCode, StatusCodeCollection>(
                out StatusCodeCollection addResults,
                out DiagnosticInfoCollection addDiagnosticInfos,
                out StringCollection stringTable,
                linksToAdd.Count,
                operationLimit);

            InitResponseCollections<StatusCode, StatusCodeCollection>(
                out StatusCodeCollection removeResults,
                out DiagnosticInfoCollection removeDiagnosticInfos,
                out _,
                linksToRemove.Count,
                operationLimit);

            foreach (UInt32Collection batchLinksToAdd in linksToAdd.Batch<uint, UInt32Collection>(
                operationLimit))
            {
                UInt32Collection batchLinksToRemove;
                if (operationLimit == 0)
                {
                    batchLinksToRemove = linksToRemove;
                    linksToRemove = [];
                }
                else if (batchLinksToAdd.Count < operationLimit)
                {
                    batchLinksToRemove = [.. linksToRemove.Take(
                        (int)operationLimit - batchLinksToAdd.Count)];
                    linksToRemove = [.. linksToRemove.Skip(batchLinksToRemove.Count)];
                }
                else
                {
                    batchLinksToRemove = [];
                }

                if (requestHeader != null)
                {
                    requestHeader.RequestHandle = 0;
                }

                response = await base.SetTriggeringAsync(
                        requestHeader,
                        subscriptionId,
                        triggeringItemId,
                        batchLinksToAdd,
                        batchLinksToRemove,
                        ct)
                    .ConfigureAwait(false);

                StatusCodeCollection batchAddResults = response.AddResults;
                DiagnosticInfoCollection batchAddDiagnosticInfos = response.AddDiagnosticInfos;
                StatusCodeCollection batchRemoveResults = response.RemoveResults;
                DiagnosticInfoCollection batchRemoveDiagnosticInfos = response
                    .RemoveDiagnosticInfos;

                ValidateResponse(batchAddResults, batchLinksToAdd);
                ValidateDiagnosticInfos(batchAddDiagnosticInfos, batchLinksToAdd);
                ValidateResponse(batchRemoveResults, batchLinksToRemove);
                ValidateDiagnosticInfos(batchRemoveDiagnosticInfos, batchLinksToRemove);

                AddResponses<StatusCode, StatusCodeCollection>(
                    ref addResults,
                    ref addDiagnosticInfos,
                    ref stringTable,
                    batchAddResults,
                    batchAddDiagnosticInfos,
                    response.ResponseHeader.StringTable);

                AddResponses<StatusCode, StatusCodeCollection>(
                    ref removeResults,
                    ref removeDiagnosticInfos,
                    ref stringTable,
                    batchRemoveResults,
                    batchRemoveDiagnosticInfos,
                    response.ResponseHeader.StringTable);
            }

            if (linksToRemove.Count > 0)
            {
                foreach (
                    UInt32Collection batchLinksToRemove in linksToRemove
                        .Batch<uint, UInt32Collection>(operationLimit))
                {
                    if (requestHeader != null)
                    {
                        requestHeader.RequestHandle = 0;
                    }

                    var batchLinksToAdd = new UInt32Collection();
                    response = await base.SetTriggeringAsync(
                            requestHeader,
                            subscriptionId,
                            triggeringItemId,
                            batchLinksToAdd,
                            batchLinksToRemove,
                            ct)
                        .ConfigureAwait(false);

                    StatusCodeCollection batchAddResults = response.AddResults;
                    DiagnosticInfoCollection batchAddDiagnosticInfos = response.AddDiagnosticInfos;
                    StatusCodeCollection batchRemoveResults = response.RemoveResults;
                    DiagnosticInfoCollection batchRemoveDiagnosticInfos = response
                        .RemoveDiagnosticInfos;

                    ValidateResponse(batchAddResults, batchLinksToAdd);
                    ValidateDiagnosticInfos(batchAddDiagnosticInfos, batchLinksToAdd);
                    ValidateResponse(batchRemoveResults, batchLinksToRemove);
                    ValidateDiagnosticInfos(batchRemoveDiagnosticInfos, batchLinksToRemove);

                    AddResponses<StatusCode, StatusCodeCollection>(
                        ref addResults,
                        ref addDiagnosticInfos,
                        ref stringTable,
                        batchAddResults,
                        batchAddDiagnosticInfos,
                        response.ResponseHeader.StringTable);

                    AddResponses<StatusCode, StatusCodeCollection>(
                        ref removeResults,
                        ref removeDiagnosticInfos,
                        ref stringTable,
                        batchRemoveResults,
                        batchRemoveDiagnosticInfos,
                        response.ResponseHeader.StringTable);
                }
            }

            response.AddResults = addResults;
            response.AddDiagnosticInfos = addDiagnosticInfos;
            response.RemoveResults = removeResults;
            response.RemoveDiagnosticInfos = removeDiagnosticInfos;
            response.ResponseHeader.StringTable = stringTable;

            return response;
        }

        /// <inheritdoc/>
        public override async Task<DeleteMonitoredItemsResponse> DeleteMonitoredItemsAsync(
            RequestHeader requestHeader,
            uint subscriptionId,
            UInt32Collection monitoredItemIds,
            CancellationToken ct)
        {
            DeleteMonitoredItemsResponse response = null;

            uint operationLimit = OperationLimits.MaxMonitoredItemsPerCall;
            InitResponseCollections<StatusCode, StatusCodeCollection>(
                out StatusCodeCollection results,
                out DiagnosticInfoCollection diagnosticInfos,
                out StringCollection stringTable,
                monitoredItemIds.Count,
                operationLimit);

            foreach (
                UInt32Collection batchMonitoredItemIds in monitoredItemIds
                    .Batch<uint, UInt32Collection>(operationLimit))
            {
                if (requestHeader != null)
                {
                    requestHeader.RequestHandle = 0;
                }

                response = await base.DeleteMonitoredItemsAsync(
                        requestHeader,
                        subscriptionId,
                        batchMonitoredItemIds,
                        ct)
                    .ConfigureAwait(false);
                StatusCodeCollection batchResults = response.Results;
                DiagnosticInfoCollection batchDiagnosticInfos = response.DiagnosticInfos;

                ValidateResponse(batchResults, batchMonitoredItemIds);
                ValidateDiagnosticInfos(batchDiagnosticInfos, batchMonitoredItemIds);

                AddResponses<StatusCode, StatusCodeCollection>(
                    ref results,
                    ref diagnosticInfos,
                    ref stringTable,
                    batchResults,
                    batchDiagnosticInfos,
                    response.ResponseHeader.StringTable);
            }

            response.Results = results;
            response.DiagnosticInfos = diagnosticInfos;
            response.ResponseHeader.StringTable = stringTable;

            return response;
        }

        /// <summary>
        /// Initialize the collections for a service call.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <typeparam name="C"></typeparam>
        /// <remarks>
        /// Preset the result collections with null if the operation limit
        /// is sufficient or with the final size if batching is necessary.
        /// </remarks>
        private static void InitResponseCollections<T, C>(
            out C results,
            out DiagnosticInfoCollection diagnosticInfos,
            out StringCollection stringTable,
            int count,
            uint operationLimit)
            where C : List<T>, new()
        {
            if (count <= operationLimit)
            {
                results = null;
                diagnosticInfos = null;
                stringTable = null;
            }
            else
            {
                results = new C { Capacity = count };
                diagnosticInfos = new DiagnosticInfoCollection(count);
                stringTable = [];
            }
        }

        /// <summary>
        /// Add the result of a batched service call to the results.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <typeparam name="C"></typeparam>
        /// <remarks>
        /// Assigns the batched collection result to the result if the result
        /// collection is not initialized, adds the range to the result
        /// collections otherwise.
        /// The string table indexes are updated in the diagnostic infos if necessary.
        /// </remarks>
        private static void AddResponses<T, C>(
            ref C results,
            ref DiagnosticInfoCollection diagnosticInfos,
            ref StringCollection stringTable,
            C batchedResults,
            DiagnosticInfoCollection batchedDiagnosticInfos,
            StringCollection batchedStringTable)
            where C : List<T>
        {
            if (results == null)
            {
                results = batchedResults;
                diagnosticInfos = batchedDiagnosticInfos;
                stringTable = batchedStringTable;
            }
            else
            {
                bool hasDiagnosticInfos = diagnosticInfos.Count > 0;
                bool hasEmptyDiagnosticInfos = diagnosticInfos.Count == 0 && results.Count > 0;
                bool hasBatchDiagnosticInfos = batchedDiagnosticInfos.Count > 0;
                int correctionCount = 0;
                if (hasBatchDiagnosticInfos && hasEmptyDiagnosticInfos)
                {
                    correctionCount = results.Count;
                }
                else if (!hasBatchDiagnosticInfos && hasDiagnosticInfos)
                {
                    correctionCount = batchedResults.Count;
                }
                if (correctionCount > 0)
                {
                    // fill missing diagnostics infos with null entries
                    for (int i = 0; i < correctionCount; i++)
                    {
                        diagnosticInfos.Add(null);
                    }
                }
                else if (batchedStringTable.Count > 0)
                {
                    // correct indexes in the string table
                    int stringTableOffset = stringTable.Count;
                    foreach (DiagnosticInfo diagnosticInfo in batchedDiagnosticInfos)
                    {
                        UpdateDiagnosticInfoIndexes(diagnosticInfo, stringTableOffset);
                    }
                }
                results.AddRange(batchedResults);
                diagnosticInfos.AddRange(batchedDiagnosticInfos);
                stringTable.AddRange(batchedStringTable);
            }
        }

        private static void UpdateDiagnosticInfoIndexes(
            DiagnosticInfo diagnosticInfo,
            int stringTableOffset)
        {
            int depth = 0;
            while (diagnosticInfo != null && depth++ < DiagnosticInfo.MaxInnerDepth)
            {
                if (diagnosticInfo.LocalizedText >= 0)
                {
                    diagnosticInfo.LocalizedText += stringTableOffset;
                }
                if (diagnosticInfo.Locale >= 0)
                {
                    diagnosticInfo.Locale += stringTableOffset;
                }
                if (diagnosticInfo.NamespaceUri >= 0)
                {
                    diagnosticInfo.NamespaceUri += stringTableOffset;
                }
                if (diagnosticInfo.SymbolicId >= 0)
                {
                    diagnosticInfo.SymbolicId += stringTableOffset;
                }
                diagnosticInfo = diagnosticInfo.InnerDiagnosticInfo;
            }
        }

        private OperationLimits m_operationLimits;
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
        internal static IEnumerable<C> Batch<T, C>(this C collection, uint batchSize)
            where C : List<T>, new()
        {
            if (collection.Count < batchSize || batchSize == 0)
            {
                yield return collection;
            }
            else
            {
                var nextbatch = new C { Capacity = (int)batchSize };
                foreach (T item in collection)
                {
                    nextbatch.Add(item);
                    if (nextbatch.Count == batchSize)
                    {
                        yield return nextbatch;
                        nextbatch = new C { Capacity = (int)batchSize };
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
