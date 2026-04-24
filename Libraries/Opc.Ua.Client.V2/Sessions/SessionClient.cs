// ------------------------------------------------------------
//  Copyright (c) 2005-2020 The OPC Foundation, Inc. All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace Opc.Ua.Client.Sessions
{
    using Microsoft.Extensions.Logging;
    using Opc.Ua.Client.Services;
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// The client side interface with support for batching according
    /// to operation limits.
    /// </summary>
    internal abstract class SessionClient : Obsolete.SessionClient,
        IViewServiceSet, IAttributeServiceSet, ISubscriptionServiceSet,
        IMethodServiceSet, IMonitoredItemServiceSet, INodeManagementServiceSet
    {
        /// <summary>
        /// The operation limits are used to batch the service requests.
        /// </summary>
        public Limits OperationLimits { get; } = new();

        /// <summary>
        /// Whether to log all activity to the logger
        /// </summary>
        public bool TraceActivityUsingLogger { get; set; }

        /// <summary>
        /// Observability context
        /// </summary>
        protected ITelemetryContext Observability { get; }

        /// <summary>
        /// Intializes the object with a channel and logger factory.
        /// </summary>
        /// <param name="telemetry"></param>
        /// <param name="channel"></param>
        protected SessionClient(ITelemetryContext telemetry,
            ITransportChannel? channel = null) : base(channel)
        {
            Observability = telemetry;
            _logger = telemetry.LoggerFactory.CreateLogger<SessionClient>();
        }

        /// <inheritdoc/>
        public override async Task<ActivateSessionResponse> ActivateSessionAsync(
            RequestHeader? requestHeader, SignatureData clientSignature,
            SignedSoftwareCertificateCollection clientSoftwareCertificates,
            StringCollection localeIds, ExtensionObject userIdentityToken,
            SignatureData userTokenSignature, CancellationToken ct)
        {
            using var activity = StartActivity(nameof(ActivateSessionAsync));
            return await base.ActivateSessionAsync(requestHeader, clientSignature,
                clientSoftwareCertificates, localeIds, userIdentityToken,
                userTokenSignature, ct).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public override async Task<CloseSessionResponse> CloseSessionAsync(
            RequestHeader? requestHeader, bool deleteSubscriptions, CancellationToken ct)
        {
            using var activity = StartActivity(nameof(CloseSessionAsync));
            return await base.CloseSessionAsync(requestHeader, deleteSubscriptions,
                ct).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public override async Task<CancelResponse> CancelAsync(RequestHeader? requestHeader,
            uint requestHandle, CancellationToken ct)
        {
            using var activity = StartActivity(nameof(CancelAsync));
            return await base.CancelAsync(requestHeader, requestHandle, ct).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public override async Task<CreateSubscriptionResponse> CreateSubscriptionAsync(
            RequestHeader? requestHeader, double requestedPublishingInterval,
            uint requestedLifetimeCount, uint requestedMaxKeepAliveCount,
            uint maxNotificationsPerPublish, bool publishingEnabled, byte priority,
            CancellationToken ct)
        {
            using var activity = StartActivity(nameof(CreateSubscriptionAsync));
            return await base.CreateSubscriptionAsync(requestHeader, requestedPublishingInterval,
                requestedLifetimeCount, requestedMaxKeepAliveCount, maxNotificationsPerPublish,
                publishingEnabled, priority, ct).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public override async Task<ModifySubscriptionResponse> ModifySubscriptionAsync(
            RequestHeader? requestHeader, uint subscriptionId, double requestedPublishingInterval,
            uint requestedLifetimeCount, uint requestedMaxKeepAliveCount,
            uint maxNotificationsPerPublish, byte priority, CancellationToken ct)
        {
            using var activity = StartActivity(nameof(ModifySubscriptionAsync));
            return await base.ModifySubscriptionAsync(requestHeader, subscriptionId,
                requestedPublishingInterval, requestedLifetimeCount, requestedMaxKeepAliveCount,
                maxNotificationsPerPublish, priority, ct).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public override async Task<SetPublishingModeResponse> SetPublishingModeAsync(
            RequestHeader? requestHeader, bool publishingEnabled,
            UInt32Collection subscriptionIds, CancellationToken ct)
        {
            using var activity = StartActivity(nameof(SetPublishingModeAsync));
            return await base.SetPublishingModeAsync(requestHeader, publishingEnabled,
                subscriptionIds, ct).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public override async Task<PublishResponse> PublishAsync(RequestHeader? requestHeader,
            SubscriptionAcknowledgementCollection subscriptionAcknowledgements, CancellationToken ct)
        {
            using var activity = StartActivity(nameof(PublishAsync));
            return await base.PublishAsync(requestHeader, subscriptionAcknowledgements,
                ct).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public override async Task<RepublishResponse> RepublishAsync(RequestHeader? requestHeader,
            uint subscriptionId, uint retransmitSequenceNumber, CancellationToken ct)
        {
            using var activity = StartActivity(nameof(RepublishAsync));
            return await base.RepublishAsync(requestHeader, subscriptionId,
                retransmitSequenceNumber, ct).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public override async Task<TransferSubscriptionsResponse> TransferSubscriptionsAsync(
            RequestHeader? requestHeader, UInt32Collection subscriptionIds, bool sendInitialValues,
            CancellationToken ct)
        {
            using var activity = StartActivity(nameof(TransferSubscriptionsAsync));
            return await base.TransferSubscriptionsAsync(requestHeader, subscriptionIds,
                sendInitialValues, ct).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public override async Task<DeleteSubscriptionsResponse> DeleteSubscriptionsAsync(
            RequestHeader? requestHeader, UInt32Collection subscriptionIds, CancellationToken ct)
        {
            using var activity = StartActivity(nameof(DeleteSubscriptionsAsync));
            return await base.DeleteSubscriptionsAsync(requestHeader, subscriptionIds,
                ct).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public override async Task<QueryFirstResponse> QueryFirstAsync(RequestHeader? requestHeader,
            ViewDescription view, NodeTypeDescriptionCollection nodeTypes, ContentFilter filter,
            uint maxDataSetsToReturn, uint maxReferencesToReturn, CancellationToken ct)
        {
            using var activity = StartActivity(nameof(QueryFirstAsync));
            return await base.QueryFirstAsync(requestHeader, view, nodeTypes, filter,
                maxDataSetsToReturn, maxReferencesToReturn, ct).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public override async Task<QueryNextResponse> QueryNextAsync(RequestHeader? requestHeader,
            bool releaseContinuationPoint, byte[] continuationPoint, CancellationToken ct)
        {
            using var activity = StartActivity(nameof(QueryNextAsync));
            return await base.QueryNextAsync(requestHeader, releaseContinuationPoint,
                continuationPoint, ct).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public override async Task<BrowseResponse> BrowseAsync(RequestHeader? requestHeader,
            ViewDescription? view, uint requestedMaxReferencesPerNode,
            BrowseDescriptionCollection nodesToBrowse, CancellationToken ct)
        {
            using var activity = StartActivity(nameof(BrowseAsync));
            var operationLimit = OperationLimits.MaxNodesPerBrowse;
            if (operationLimit == 0 || operationLimit >= nodesToBrowse.Count)
            {
                return await base.BrowseAsync(requestHeader, view,
                    requestedMaxReferencesPerNode, nodesToBrowse, ct).ConfigureAwait(false);
            }

            requestHeader ??= new RequestHeader();
            BrowseResponse? response = null;
            InitResponseCollections<BrowseResult, BrowseResultCollection>(out var results,
                out var diagnosticInfos, out var stringTable, nodesToBrowse.Count, operationLimit);
            foreach (var nodesToBrowseBatch in nodesToBrowse
                .Batch<BrowseDescription, BrowseDescriptionCollection>(operationLimit))
            {
                requestHeader.RequestHandle = 0;
                response = await base.BrowseAsync(requestHeader, view,
                    requestedMaxReferencesPerNode,
                    nodesToBrowseBatch, ct).ConfigureAwait(false);

                var batchResults = response.Results;
                var batchDiagnosticInfos = response.DiagnosticInfos;

                Ua.ClientBase.ValidateResponse(batchResults, nodesToBrowseBatch);
                Ua.ClientBase.ValidateDiagnosticInfos(batchDiagnosticInfos, nodesToBrowseBatch);

                AddResponses<BrowseResult, BrowseResultCollection>(
                    ref results, ref diagnosticInfos, ref stringTable, batchResults,
                    batchDiagnosticInfos, response.ResponseHeader.StringTable);
            }

            Debug.Assert(response != null, "Empty ops should have been covered in fast path");
            response.Results = results;
            response.DiagnosticInfos = diagnosticInfos;
            response.ResponseHeader.StringTable = stringTable;
            return response;
        }

        /// <inheritdoc/>
        public override async Task<BrowseNextResponse> BrowseNextAsync(RequestHeader? requestHeader,
            bool releaseContinuationPoints, ByteStringCollection continuationPoints, CancellationToken ct)
        {
            using var activity = StartActivity(nameof(BrowseNextAsync));

            var operationLimit = OperationLimits.MaxBrowseContinuationPoints;
            if (operationLimit == 0 || operationLimit >= continuationPoints.Count)
            {
                return await base.BrowseNextAsync(requestHeader, releaseContinuationPoints,
                    continuationPoints, ct).ConfigureAwait(false);
            }

            requestHeader ??= new RequestHeader();
            BrowseNextResponse? response = null;
            InitResponseCollections<BrowseResult, BrowseResultCollection>(out var results,
                out var diagnosticInfos, out var stringTable, continuationPoints.Count, operationLimit);
            foreach (var continuationPointsBatch in continuationPoints
                .Batch<byte[], ByteStringCollection>(operationLimit))
            {
                requestHeader.RequestHandle = 0;
                response = await base.BrowseNextAsync(requestHeader, releaseContinuationPoints,
                    continuationPointsBatch, ct).ConfigureAwait(false);

                var batchResults = response.Results;
                var batchDiagnosticInfos = response.DiagnosticInfos;

                Ua.ClientBase.ValidateResponse(batchResults, continuationPointsBatch);
                Ua.ClientBase.ValidateDiagnosticInfos(batchDiagnosticInfos, continuationPointsBatch);

                AddResponses<BrowseResult, BrowseResultCollection>(
                    ref results, ref diagnosticInfos, ref stringTable, batchResults,
                    batchDiagnosticInfos, response.ResponseHeader.StringTable);
            }

            Debug.Assert(response != null, "Empty ops should have been covered in fast path");
            response.Results = results;
            response.DiagnosticInfos = diagnosticInfos;
            response.ResponseHeader.StringTable = stringTable;
            return response;
        }

        /// <inheritdoc/>
        public override async Task<TranslateBrowsePathsToNodeIdsResponse> TranslateBrowsePathsToNodeIdsAsync(
            RequestHeader? requestHeader, BrowsePathCollection browsePaths, CancellationToken ct)
        {
            using var activity = StartActivity(nameof(TranslateBrowsePathsToNodeIdsAsync));

            var operationLimit = OperationLimits.MaxNodesPerTranslateBrowsePathsToNodeIds;
            if (operationLimit == 0 || operationLimit >= browsePaths.Count)
            {
                return await base.TranslateBrowsePathsToNodeIdsAsync(requestHeader,
                    browsePaths, ct).ConfigureAwait(false);
            }

            requestHeader ??= new RequestHeader();
            TranslateBrowsePathsToNodeIdsResponse? response = null;
            InitResponseCollections<BrowsePathResult, BrowsePathResultCollection>(out var results,
                out var diagnosticInfos, out var stringTable, browsePaths.Count, operationLimit);
            foreach (var batchBrowsePaths in browsePaths
                .Batch<BrowsePath, BrowsePathCollection>(operationLimit))
            {
                requestHeader.RequestHandle = 0;
                response = await base.TranslateBrowsePathsToNodeIdsAsync(requestHeader,
                    batchBrowsePaths, ct).ConfigureAwait(false);

                var batchResults = response.Results;
                var batchDiagnosticInfos = response.DiagnosticInfos;
                Ua.ClientBase.ValidateResponse(batchResults, batchBrowsePaths);
                Ua.ClientBase.ValidateDiagnosticInfos(batchDiagnosticInfos, batchBrowsePaths);

                AddResponses<BrowsePathResult, BrowsePathResultCollection>(
                    ref results, ref diagnosticInfos, ref stringTable, batchResults,
                    batchDiagnosticInfos, response.ResponseHeader.StringTable);
            }

            Debug.Assert(response != null, "Empty ops should have been covered in fast path");
            response.Results = results;
            response.DiagnosticInfos = diagnosticInfos;
            response.ResponseHeader.StringTable = stringTable;
            return response;
        }

        /// <inheritdoc/>
        public override async Task<RegisterNodesResponse> RegisterNodesAsync(
            RequestHeader? requestHeader, NodeIdCollection nodesToRegister,
            CancellationToken ct)
        {
            using var activity = StartActivity(nameof(RegisterNodesAsync));

            var operationLimit = OperationLimits.MaxNodesPerRegisterNodes;
            if (operationLimit == 0 || operationLimit >= nodesToRegister.Count)
            {
                return await base.RegisterNodesAsync(requestHeader, nodesToRegister,
                    ct).ConfigureAwait(false);
            }

            requestHeader ??= new RequestHeader();
            RegisterNodesResponse? response = null;
            var registeredNodeIds = new NodeIdCollection();
            foreach (var batchNodesToRegister in nodesToRegister
                .Batch<NodeId, NodeIdCollection>(operationLimit))
            {
                requestHeader.RequestHandle = 0;
                response = await base.RegisterNodesAsync(requestHeader,
                    batchNodesToRegister, ct).ConfigureAwait(false);

                var batchRegisteredNodeIds = response.RegisteredNodeIds;
                Ua.ClientBase.ValidateResponse(batchRegisteredNodeIds, batchNodesToRegister);
                registeredNodeIds.AddRange(batchRegisteredNodeIds);
            }

            Debug.Assert(response != null, "Empty ops should have been covered in fast path");
            response.RegisteredNodeIds = registeredNodeIds;
            return response;
        }

        /// <inheritdoc/>
        public override async Task<UnregisterNodesResponse> UnregisterNodesAsync(
            RequestHeader? requestHeader, NodeIdCollection nodesToUnregister,
            CancellationToken ct)
        {
            using var activity = StartActivity(nameof(UnregisterNodesAsync));

            var operationLimit = OperationLimits.MaxNodesPerRegisterNodes;
            if (operationLimit == 0 || operationLimit >= nodesToUnregister.Count)
            {
                return await base.UnregisterNodesAsync(requestHeader, nodesToUnregister,
                    ct).ConfigureAwait(false);
            }

            requestHeader ??= new RequestHeader();
            UnregisterNodesResponse? response = null;
            foreach (var batchNodesToUnregister in nodesToUnregister
                .Batch<NodeId, NodeIdCollection>(operationLimit))
            {
                requestHeader.RequestHandle = 0;
                response = await base.UnregisterNodesAsync(requestHeader,
                    batchNodesToUnregister, ct).ConfigureAwait(false);
            }
            Debug.Assert(response != null, "Empty ops should have been covered in fast path");
            return response;
        }

        /// <inheritdoc/>
        public override async Task<ReadResponse> ReadAsync(RequestHeader? requestHeader,
            double maxAge, TimestampsToReturn timestampsToReturn,
            ReadValueIdCollection nodesToRead, CancellationToken ct)
        {
            using var activity = StartActivity(nameof(ReadAsync));

            var operationLimit = OperationLimits.MaxNodesPerRead;
            if (operationLimit == 0 || operationLimit >= nodesToRead.Count)
            {
                return await base.ReadAsync(requestHeader, maxAge, timestampsToReturn,
                    nodesToRead, ct).ConfigureAwait(false);
            }

            requestHeader ??= new RequestHeader();
            ReadResponse? response = null;
            InitResponseCollections<DataValue, DataValueCollection>(out var results,
                out var diagnosticInfos, out var stringTable, nodesToRead.Count, operationLimit);
            foreach (var batchAttributesToRead in nodesToRead
                .Batch<ReadValueId, ReadValueIdCollection>(operationLimit))
            {
                requestHeader.RequestHandle = 0;
                response = await base.ReadAsync(requestHeader, maxAge, timestampsToReturn,
                    batchAttributesToRead, ct).ConfigureAwait(false);

                var batchResults = response.Results;
                var batchDiagnosticInfos = response.DiagnosticInfos;

                Ua.ClientBase.ValidateResponse(batchResults, batchAttributesToRead);
                Ua.ClientBase.ValidateDiagnosticInfos(batchDiagnosticInfos, batchAttributesToRead);

                AddResponses<DataValue, DataValueCollection>(
                    ref results, ref diagnosticInfos, ref stringTable, batchResults,
                    batchDiagnosticInfos, response.ResponseHeader.StringTable);
            }

            Debug.Assert(response != null, "Empty ops should have been covered in fast path");
            response.Results = results;
            response.DiagnosticInfos = diagnosticInfos;
            response.ResponseHeader.StringTable = stringTable;
            return response;
        }

        /// <inheritdoc/>
        public override async Task<HistoryReadResponse> HistoryReadAsync(
            RequestHeader? requestHeader, ExtensionObject historyReadDetails,
            TimestampsToReturn timestampsToReturn, bool releaseContinuationPoints,
            HistoryReadValueIdCollection nodesToRead, CancellationToken ct)
        {
            using var activity = StartActivity(nameof(HistoryReadAsync));

            var operationLimit = OperationLimits.MaxNodesPerHistoryReadData;
            if (historyReadDetails?.TypeId == DataTypeIds.ReadEventDetails ||
                historyReadDetails?.Body is ReadEventDetails)
            {
                operationLimit = OperationLimits.MaxNodesPerHistoryReadEvents;
            }

            if (operationLimit == 0 || operationLimit >= nodesToRead.Count)
            {
                return await base.HistoryReadAsync(requestHeader, historyReadDetails,
                    timestampsToReturn, releaseContinuationPoints, nodesToRead,
                    ct).ConfigureAwait(false);
            }

            requestHeader ??= new RequestHeader();
            HistoryReadResponse? response = null;
            InitResponseCollections<HistoryReadResult, HistoryReadResultCollection>(
                out var results, out var diagnosticInfos, out var stringTable,
                nodesToRead.Count, operationLimit);
            foreach (var batchNodesToRead in nodesToRead
                .Batch<HistoryReadValueId, HistoryReadValueIdCollection>(operationLimit))
            {
                requestHeader.RequestHandle = 0;
                response = await base.HistoryReadAsync(requestHeader, historyReadDetails,
                    timestampsToReturn, releaseContinuationPoints, batchNodesToRead,
                    ct).ConfigureAwait(false);

                var batchResults = response.Results;
                var batchDiagnosticInfos = response.DiagnosticInfos;

                Ua.ClientBase.ValidateResponse(batchResults, batchNodesToRead);
                Ua.ClientBase.ValidateDiagnosticInfos(batchDiagnosticInfos, batchNodesToRead);

                AddResponses<HistoryReadResult, HistoryReadResultCollection>(
                    ref results, ref diagnosticInfos, ref stringTable,
                    batchResults, batchDiagnosticInfos, response.ResponseHeader.StringTable);
            }

            Debug.Assert(response != null, "Empty ops should have been covered in fast path");
            response.Results = results;
            response.DiagnosticInfos = diagnosticInfos;
            response.ResponseHeader.StringTable = stringTable;
            return response;
        }

        /// <inheritdoc/>
        public override async Task<WriteResponse> WriteAsync(RequestHeader? requestHeader,
            WriteValueCollection nodesToWrite, CancellationToken ct)
        {
            using var activity = StartActivity(nameof(WriteAsync));

            var operationLimit = OperationLimits.MaxNodesPerWrite;
            if (operationLimit == 0 || operationLimit >= nodesToWrite.Count)
            {
                return await base.WriteAsync(requestHeader, nodesToWrite,
                    ct).ConfigureAwait(false);
            }

            requestHeader ??= new RequestHeader();
            WriteResponse? response = null;
            InitResponseCollections<StatusCode, StatusCodeCollection>(out var results,
                out var diagnosticInfos, out var stringTable, nodesToWrite.Count, operationLimit);
            foreach (var batchNodesToWrite in nodesToWrite
                .Batch<WriteValue, WriteValueCollection>(operationLimit))
            {
                requestHeader.RequestHandle = 0;
                response = await base.WriteAsync(requestHeader,
                    batchNodesToWrite, ct).ConfigureAwait(false);

                var batchResults = response.Results;
                var batchDiagnosticInfos = response.DiagnosticInfos;

                Ua.ClientBase.ValidateResponse(batchResults, batchNodesToWrite);
                Ua.ClientBase.ValidateDiagnosticInfos(batchDiagnosticInfos, batchNodesToWrite);

                AddResponses<StatusCode, StatusCodeCollection>(
                    ref results, ref diagnosticInfos, ref stringTable, batchResults,
                    batchDiagnosticInfos, response.ResponseHeader.StringTable);
            }

            Debug.Assert(response != null, "Empty ops should have been covered in fast path");
            response.Results = results;
            response.DiagnosticInfos = diagnosticInfos;
            response.ResponseHeader.StringTable = stringTable;
            return response;
        }

        /// <inheritdoc/>
        public override async Task<HistoryUpdateResponse> HistoryUpdateAsync(
            RequestHeader? requestHeader, ExtensionObjectCollection historyUpdateDetails,
            CancellationToken ct)
        {
            using var activity = StartActivity(nameof(HistoryUpdateAsync));

            var operationLimit = OperationLimits.MaxNodesPerHistoryUpdateData;
            if (historyUpdateDetails.Count > 0 &&
                (historyUpdateDetails[0].TypeId == DataTypeIds.UpdateEventDetails ||
                historyUpdateDetails[0]?.Body is UpdateEventDetails))
            {
                operationLimit = OperationLimits.MaxNodesPerHistoryUpdateEvents;
            }

            if (operationLimit == 0 || operationLimit >= historyUpdateDetails.Count)
            {
                return await base.HistoryUpdateAsync(requestHeader, historyUpdateDetails,
                    ct).ConfigureAwait(false);
            }

            requestHeader ??= new RequestHeader();
            HistoryUpdateResponse? response = null;
            InitResponseCollections<HistoryUpdateResult, HistoryUpdateResultCollection>(
                out var results, out var diagnosticInfos, out var stringTable,
                historyUpdateDetails.Count, operationLimit);
            foreach (var batchHistoryUpdateDetails in historyUpdateDetails
                .Batch<ExtensionObject, ExtensionObjectCollection>(operationLimit))
            {
                requestHeader.RequestHandle = 0;
                response = await base.HistoryUpdateAsync(requestHeader,
                    batchHistoryUpdateDetails, ct).ConfigureAwait(false);

                var batchResults = response.Results;
                var batchDiagnosticInfos = response.DiagnosticInfos;

                Ua.ClientBase.ValidateResponse(batchResults, batchHistoryUpdateDetails);
                Ua.ClientBase.ValidateDiagnosticInfos(batchDiagnosticInfos, batchHistoryUpdateDetails);

                AddResponses<HistoryUpdateResult, HistoryUpdateResultCollection>(
                    ref results, ref diagnosticInfos, ref stringTable, batchResults,
                    batchDiagnosticInfos, response.ResponseHeader.StringTable);
            }

            Debug.Assert(response != null, "Empty ops should have been covered in fast path");
            response.Results = results;
            response.DiagnosticInfos = diagnosticInfos;
            response.ResponseHeader.StringTable = stringTable;
            return response;
        }

        /// <inheritdoc/>
        public override async Task<CallResponse> CallAsync(RequestHeader? requestHeader,
            CallMethodRequestCollection methodsToCall, CancellationToken ct)
        {
            using var activity = StartActivity(nameof(CallAsync));

            var operationLimit = OperationLimits.MaxNodesPerMethodCall;
            if (operationLimit == 0 || operationLimit >= methodsToCall.Count)
            {
                return await base.CallAsync(requestHeader, methodsToCall,
                    ct).ConfigureAwait(false);
            }

            requestHeader ??= new RequestHeader();
            CallResponse? response = null;
            InitResponseCollections<CallMethodResult, CallMethodResultCollection>(out var results,
                out var diagnosticInfos, out var stringTable, methodsToCall.Count, operationLimit);
            foreach (var batchMethodsToCall in methodsToCall
                .Batch<CallMethodRequest, CallMethodRequestCollection>(operationLimit))
            {
                requestHeader.RequestHandle = 0;
                response = await base.CallAsync(requestHeader,
                    batchMethodsToCall, ct).ConfigureAwait(false);

                var batchResults = response.Results;
                var batchDiagnosticInfos = response.DiagnosticInfos;

                Ua.ClientBase.ValidateResponse(batchResults, batchMethodsToCall);
                Ua.ClientBase.ValidateDiagnosticInfos(batchDiagnosticInfos, batchMethodsToCall);

                AddResponses<CallMethodResult, CallMethodResultCollection>(
                    ref results, ref diagnosticInfos, ref stringTable, batchResults,
                    batchDiagnosticInfos, response.ResponseHeader.StringTable);
            }

            Debug.Assert(response != null, "Empty ops should have been covered in fast path");
            response.Results = results;
            response.DiagnosticInfos = diagnosticInfos;
            response.ResponseHeader.StringTable = stringTable;
            return response;
        }

        /// <inheritdoc/>
        public override async Task<CreateMonitoredItemsResponse> CreateMonitoredItemsAsync(
            RequestHeader? requestHeader, uint subscriptionId, TimestampsToReturn timestampsToReturn,
            MonitoredItemCreateRequestCollection itemsToCreate, CancellationToken ct)
        {
            using var activity = StartActivity(nameof(CreateMonitoredItemsAsync));

            var operationLimit = OperationLimits.MaxMonitoredItemsPerCall;
            if (operationLimit == 0 || operationLimit >= itemsToCreate.Count)
            {
                return await base.CreateMonitoredItemsAsync(requestHeader, subscriptionId,
                    timestampsToReturn, itemsToCreate, ct).ConfigureAwait(false);
            }

            requestHeader ??= new RequestHeader();
            CreateMonitoredItemsResponse? response = null;
            InitResponseCollections<MonitoredItemCreateResult, MonitoredItemCreateResultCollection>(
                out var results, out var diagnosticInfos, out var stringTable, itemsToCreate.Count,
                operationLimit);
            foreach (var batchItemsToCreate in itemsToCreate
                .Batch<MonitoredItemCreateRequest, MonitoredItemCreateRequestCollection>(operationLimit))
            {
                requestHeader.RequestHandle = 0;
                response = await base.CreateMonitoredItemsAsync(requestHeader, subscriptionId,
                    timestampsToReturn, batchItemsToCreate, ct).ConfigureAwait(false);

                var batchResults = response.Results;
                var batchDiagnosticInfos = response.DiagnosticInfos;
                Ua.ClientBase.ValidateResponse(batchResults, batchItemsToCreate);
                Ua.ClientBase.ValidateDiagnosticInfos(batchDiagnosticInfos, batchItemsToCreate);

                AddResponses<MonitoredItemCreateResult, MonitoredItemCreateResultCollection>(
                    ref results, ref diagnosticInfos, ref stringTable, batchResults,
                    batchDiagnosticInfos, response.ResponseHeader.StringTable);
            }

            Debug.Assert(response != null, "Empty ops should have been covered in fast path");
            response.Results = results;
            response.DiagnosticInfos = diagnosticInfos;
            response.ResponseHeader.StringTable = stringTable;
            return response;
        }

        /// <inheritdoc/>
        public override async Task<ModifyMonitoredItemsResponse> ModifyMonitoredItemsAsync(
            RequestHeader? requestHeader, uint subscriptionId, TimestampsToReturn timestampsToReturn,
            MonitoredItemModifyRequestCollection itemsToModify, CancellationToken ct)
        {
            using var activity = StartActivity(nameof(ModifyMonitoredItemsAsync));

            var operationLimit = OperationLimits.MaxMonitoredItemsPerCall;
            if (operationLimit == 0 || operationLimit >= itemsToModify.Count)
            {
                return await base.ModifyMonitoredItemsAsync(requestHeader, subscriptionId,
                    timestampsToReturn, itemsToModify, ct).ConfigureAwait(false);
            }

            requestHeader ??= new RequestHeader();
            ModifyMonitoredItemsResponse? response = null;
            InitResponseCollections<MonitoredItemModifyResult, MonitoredItemModifyResultCollection>(
                out var results, out var diagnosticInfos, out var stringTable,
                itemsToModify.Count, operationLimit);
            foreach (var batchItemsToModify in itemsToModify
                .Batch<MonitoredItemModifyRequest, MonitoredItemModifyRequestCollection>(operationLimit))
            {
                requestHeader.RequestHandle = 0;
                response = await base.ModifyMonitoredItemsAsync(requestHeader, subscriptionId,
                    timestampsToReturn, batchItemsToModify, ct).ConfigureAwait(false);

                var batchResults = response.Results;
                var batchDiagnosticInfos = response.DiagnosticInfos;
                Ua.ClientBase.ValidateResponse(batchResults, batchItemsToModify);
                Ua.ClientBase.ValidateDiagnosticInfos(batchDiagnosticInfos, batchItemsToModify);

                AddResponses<MonitoredItemModifyResult, MonitoredItemModifyResultCollection>(
                    ref results, ref diagnosticInfos, ref stringTable, batchResults,
                    batchDiagnosticInfos, response.ResponseHeader.StringTable);
            }

            Debug.Assert(response != null, "Empty ops should have been covered in fast path");
            response.Results = results;
            response.DiagnosticInfos = diagnosticInfos;
            response.ResponseHeader.StringTable = stringTable;
            return response;
        }

        /// <inheritdoc/>
        public override async Task<SetMonitoringModeResponse> SetMonitoringModeAsync(
            RequestHeader? requestHeader, uint subscriptionId, MonitoringMode monitoringMode,
            UInt32Collection monitoredItemIds, CancellationToken ct)
        {
            using var activity = StartActivity(nameof(SetMonitoringModeResponse));

            var operationLimit = OperationLimits.MaxMonitoredItemsPerCall;
            if (operationLimit == 0 || operationLimit >= monitoredItemIds.Count)
            {
                return await base.SetMonitoringModeAsync(requestHeader, subscriptionId,
                    monitoringMode, monitoredItemIds, ct).ConfigureAwait(false);
            }

            requestHeader ??= new RequestHeader();
            SetMonitoringModeResponse? response = null;
            InitResponseCollections<StatusCode, StatusCodeCollection>(out var results,
                out var diagnosticInfos, out var stringTable, monitoredItemIds.Count, operationLimit);
            foreach (var batchMonitoredItemIds in monitoredItemIds
                .Batch<uint, UInt32Collection>(operationLimit))
            {
                requestHeader.RequestHandle = 0;
                response = await base.SetMonitoringModeAsync(requestHeader, subscriptionId,
                    monitoringMode, batchMonitoredItemIds, ct).ConfigureAwait(false);

                var batchResults = response.Results;
                var batchDiagnosticInfos = response.DiagnosticInfos;

                Ua.ClientBase.ValidateResponse(batchResults, batchMonitoredItemIds);
                Ua.ClientBase.ValidateDiagnosticInfos(batchDiagnosticInfos, batchMonitoredItemIds);

                AddResponses<StatusCode, StatusCodeCollection>(
                    ref results, ref diagnosticInfos, ref stringTable, batchResults,
                    batchDiagnosticInfos, response.ResponseHeader.StringTable);
            }

            Debug.Assert(response != null, "Empty ops should have been covered in fast path");
            response.Results = results;
            response.DiagnosticInfos = diagnosticInfos;
            response.ResponseHeader.StringTable = stringTable;
            return response;
        }

        /// <inheritdoc/>
        public override async Task<SetTriggeringResponse> SetTriggeringAsync(
            RequestHeader? requestHeader, uint subscriptionId, uint triggeringItemId,
            UInt32Collection linksToAdd, UInt32Collection linksToRemove, CancellationToken ct)
        {
            using var activity = StartActivity(nameof(SetTriggeringAsync));

            var operationLimit = OperationLimits.MaxMonitoredItemsPerCall;
            if (operationLimit == 0 || operationLimit >= linksToAdd.Count + linksToRemove.Count)
            {
                return await base.SetTriggeringAsync(requestHeader, subscriptionId,
                    triggeringItemId, linksToAdd, linksToRemove, ct).ConfigureAwait(false);
            }

            requestHeader ??= new RequestHeader();
            SetTriggeringResponse? response = null;
            InitResponseCollections<StatusCode, StatusCodeCollection>(out var addResults,
                out var addDiagnosticInfos, out var stringTable, linksToAdd.Count,
                operationLimit);
            InitResponseCollections<StatusCode, StatusCodeCollection>(out var removeResults,
                out var removeDiagnosticInfos, out _, linksToRemove.Count, operationLimit);
            foreach (var batchLinksToAdd in linksToAdd
                .Batch<uint, UInt32Collection>(operationLimit))
            {
                UInt32Collection batchLinksToRemove;
                if (operationLimit == 0)
                {
                    batchLinksToRemove = linksToRemove;
                    linksToRemove = [];
                }
                else if (batchLinksToAdd.Count < operationLimit)
                {
                    batchLinksToRemove = new UInt32Collection(
                        linksToRemove.Take((int)operationLimit - batchLinksToAdd.Count));
                    linksToRemove = new UInt32Collection(
                        linksToRemove.Skip(batchLinksToRemove.Count));
                }
                else
                {
                    batchLinksToRemove = [];
                }

                requestHeader.RequestHandle = 0;
                response = await base.SetTriggeringAsync(requestHeader, subscriptionId,
                    triggeringItemId, batchLinksToAdd, batchLinksToRemove, ct).ConfigureAwait(false);

                var batchAddResults = response.AddResults;
                var batchAddDiagnosticInfos = response.AddDiagnosticInfos;
                var batchRemoveResults = response.RemoveResults;
                var batchRemoveDiagnosticInfos = response.RemoveDiagnosticInfos;

                Ua.ClientBase.ValidateResponse(batchAddResults, batchLinksToAdd);
                Ua.ClientBase.ValidateDiagnosticInfos(batchAddDiagnosticInfos, batchLinksToAdd);
                Ua.ClientBase.ValidateResponse(batchRemoveResults, batchLinksToRemove);
                Ua.ClientBase.ValidateDiagnosticInfos(batchRemoveDiagnosticInfos, batchLinksToRemove);

                AddResponses<StatusCode, StatusCodeCollection>(
                    ref addResults, ref addDiagnosticInfos, ref stringTable,
                    batchAddResults, batchAddDiagnosticInfos,
                    response.ResponseHeader.StringTable);

                AddResponses<StatusCode, StatusCodeCollection>(
                    ref removeResults, ref removeDiagnosticInfos, ref stringTable,
                    batchRemoveResults, batchRemoveDiagnosticInfos,
                    response.ResponseHeader.StringTable);
            }

            if (linksToRemove.Count > 0)
            {
                foreach (var batchLinksToRemove in linksToRemove
                    .Batch<uint, UInt32Collection>(operationLimit))
                {
                    requestHeader.RequestHandle = 0;
                    var batchLinksToAdd = new UInt32Collection();
                    response = await base.SetTriggeringAsync(requestHeader, subscriptionId,
                        triggeringItemId, batchLinksToAdd, batchLinksToRemove,
                        ct).ConfigureAwait(false);

                    var batchAddResults = response.AddResults;
                    var batchAddDiagnosticInfos = response.AddDiagnosticInfos;
                    var batchRemoveResults = response.RemoveResults;
                    var batchRemoveDiagnosticInfos = response.RemoveDiagnosticInfos;

                    Ua.ClientBase.ValidateResponse(batchAddResults, batchLinksToAdd);
                    Ua.ClientBase.ValidateDiagnosticInfos(batchAddDiagnosticInfos, batchLinksToAdd);
                    Ua.ClientBase.ValidateResponse(batchRemoveResults, batchLinksToRemove);
                    Ua.ClientBase.ValidateDiagnosticInfos(batchRemoveDiagnosticInfos, batchLinksToRemove);

                    AddResponses<StatusCode, StatusCodeCollection>(
                        ref addResults, ref addDiagnosticInfos, ref stringTable,
                        batchAddResults, batchAddDiagnosticInfos,
                        response.ResponseHeader.StringTable);

                    AddResponses<StatusCode, StatusCodeCollection>(
                        ref removeResults, ref removeDiagnosticInfos, ref stringTable,
                        batchRemoveResults, batchRemoveDiagnosticInfos,
                        response.ResponseHeader.StringTable);
                }
            }

            Debug.Assert(response != null, "Empty ops should have been covered in fast path");
            response.AddResults = addResults;
            response.AddDiagnosticInfos = addDiagnosticInfos;
            response.RemoveResults = removeResults;
            response.RemoveDiagnosticInfos = removeDiagnosticInfos;
            response.ResponseHeader.StringTable = stringTable;
            return response;
        }

        /// <inheritdoc/>
        public override async Task<DeleteMonitoredItemsResponse> DeleteMonitoredItemsAsync(
            RequestHeader? requestHeader, uint subscriptionId, UInt32Collection monitoredItemIds,
            CancellationToken ct)
        {
            using var activity = StartActivity(nameof(DeleteMonitoredItemsAsync));

            var operationLimit = OperationLimits.MaxMonitoredItemsPerCall;
            if (operationLimit == 0 || operationLimit >= monitoredItemIds.Count)
            {
                return await base.DeleteMonitoredItemsAsync(requestHeader, subscriptionId,
                    monitoredItemIds, ct).ConfigureAwait(false);
            }

            requestHeader ??= new RequestHeader();
            DeleteMonitoredItemsResponse? response = null;
            InitResponseCollections<StatusCode, StatusCodeCollection>(out var results,
                out var diagnosticInfos, out var stringTable, monitoredItemIds.Count, operationLimit);
            foreach (var batchMonitoredItemIds in monitoredItemIds
                .Batch<uint, UInt32Collection>(operationLimit))
            {
                requestHeader.RequestHandle = 0;
                response = await base.DeleteMonitoredItemsAsync(requestHeader,
                    subscriptionId, batchMonitoredItemIds, ct).ConfigureAwait(false);
                var batchResults = response.Results;
                var batchDiagnosticInfos = response.DiagnosticInfos;

                Ua.ClientBase.ValidateResponse(batchResults, batchMonitoredItemIds);
                Ua.ClientBase.ValidateDiagnosticInfos(batchDiagnosticInfos, batchMonitoredItemIds);

                AddResponses<StatusCode, StatusCodeCollection>(
                    ref results, ref diagnosticInfos, ref stringTable, batchResults,
                    batchDiagnosticInfos, response.ResponseHeader.StringTable);
            }

            Debug.Assert(response != null, "Empty ops should have been covered in fast path");
            response.Results = results;
            response.DiagnosticInfos = diagnosticInfos;
            response.ResponseHeader.StringTable = stringTable;
            return response;
        }

        /// <inheritdoc/>
        public override async Task<AddNodesResponse> AddNodesAsync(RequestHeader? requestHeader,
            AddNodesItemCollection nodesToAdd, CancellationToken ct)
        {
            using var activity = StartActivity(nameof(AddNodesAsync));

            var operationLimit = OperationLimits.MaxNodesPerNodeManagement;
            if (operationLimit == 0 || operationLimit >= nodesToAdd.Count)
            {
                return await base.AddNodesAsync(requestHeader, nodesToAdd,
                    ct).ConfigureAwait(false);
            }

            requestHeader ??= new RequestHeader();
            AddNodesResponse? response = null;
            InitResponseCollections<AddNodesResult, AddNodesResultCollection>(out var results,
                out var diagnosticInfos, out var stringTable, nodesToAdd.Count, operationLimit);
            foreach (var batchNodesToAdd in nodesToAdd
                .Batch<AddNodesItem, AddNodesItemCollection>(operationLimit))
            {
                requestHeader.RequestHandle = 0;
                response = await base.AddNodesAsync(requestHeader, batchNodesToAdd,
                    ct).ConfigureAwait(false);
                var batchResults = response.Results;
                var batchDiagnosticInfos = response.DiagnosticInfos;
                Ua.ClientBase.ValidateResponse(batchResults, batchNodesToAdd);
                Ua.ClientBase.ValidateDiagnosticInfos(batchDiagnosticInfos, batchNodesToAdd);

                AddResponses<AddNodesResult, AddNodesResultCollection>(ref results,
                    ref diagnosticInfos, ref stringTable, batchResults, batchDiagnosticInfos,
                    response.ResponseHeader.StringTable);
            }

            Debug.Assert(response != null, "Empty ops should have been covered in fast path");
            response.Results = results;
            response.DiagnosticInfos = diagnosticInfos;
            response.ResponseHeader.StringTable = stringTable;
            return response;
        }

        /// <inheritdoc/>
        public override async Task<AddReferencesResponse> AddReferencesAsync(RequestHeader? requestHeader,
            AddReferencesItemCollection referencesToAdd, CancellationToken ct)
        {
            using var activity = StartActivity(nameof(AddReferencesAsync));

            var operationLimit = OperationLimits.MaxNodesPerNodeManagement;
            if (operationLimit == 0 || operationLimit >= referencesToAdd.Count)
            {
                return await base.AddReferencesAsync(requestHeader, referencesToAdd,
                    ct).ConfigureAwait(false);
            }

            requestHeader ??= new RequestHeader();
            AddReferencesResponse? response = null;
            InitResponseCollections<StatusCode, StatusCodeCollection>(out var results,
                out var diagnosticInfos, out var stringTable, referencesToAdd.Count,
                operationLimit);
            foreach (var batchReferencesToAdd in referencesToAdd
                .Batch<AddReferencesItem, AddReferencesItemCollection>(operationLimit))
            {
                requestHeader.RequestHandle = 0;
                response = await base.AddReferencesAsync(requestHeader, batchReferencesToAdd,
                    ct).ConfigureAwait(false);

                var batchResults = response.Results;
                var batchDiagnosticInfos = response.DiagnosticInfos;
                Ua.ClientBase.ValidateResponse(batchResults, batchReferencesToAdd);
                Ua.ClientBase.ValidateDiagnosticInfos(batchDiagnosticInfos, batchReferencesToAdd);

                AddResponses<StatusCode, StatusCodeCollection>(ref results, ref diagnosticInfos,
                    ref stringTable, batchResults, batchDiagnosticInfos,
                    response.ResponseHeader.StringTable);
            }

            Debug.Assert(response != null, "Empty ops should have been covered in fast path");
            response.Results = results;
            response.DiagnosticInfos = diagnosticInfos;
            response.ResponseHeader.StringTable = stringTable;
            return response;
        }

        /// <inheritdoc/>
        public override async Task<DeleteNodesResponse> DeleteNodesAsync(RequestHeader? requestHeader,
            DeleteNodesItemCollection nodesToDelete, CancellationToken ct)
        {
            using var activity = StartActivity(nameof(DeleteNodesAsync));

            var operationLimit = OperationLimits.MaxNodesPerNodeManagement;
            if (operationLimit == 0 || operationLimit >= nodesToDelete.Count)
            {
                return await base.DeleteNodesAsync(requestHeader, nodesToDelete,
                    ct).ConfigureAwait(false);
            }

            requestHeader ??= new RequestHeader();
            DeleteNodesResponse? response = null;
            InitResponseCollections<StatusCode, StatusCodeCollection>(out var results,
                out var diagnosticInfos, out var stringTable, nodesToDelete.Count, operationLimit);
            foreach (var batchNodesToDelete in nodesToDelete
                .Batch<DeleteNodesItem, DeleteNodesItemCollection>(operationLimit))
            {
                requestHeader.RequestHandle = 0;
                response = await base.DeleteNodesAsync(requestHeader,
                    batchNodesToDelete, ct).ConfigureAwait(false);

                var batchResults = response.Results;
                var batchDiagnosticInfos = response.DiagnosticInfos;
                Ua.ClientBase.ValidateResponse(batchResults, batchNodesToDelete);
                Ua.ClientBase.ValidateDiagnosticInfos(batchDiagnosticInfos, batchNodesToDelete);

                AddResponses<StatusCode, StatusCodeCollection>(ref results, ref diagnosticInfos,
                    ref stringTable, batchResults, batchDiagnosticInfos,
                    response.ResponseHeader.StringTable);
            }

            Debug.Assert(response != null, "Empty ops should have been covered in fast path");
            response.Results = results;
            response.DiagnosticInfos = diagnosticInfos;
            response.ResponseHeader.StringTable = stringTable;
            return response;
        }

        /// <inheritdoc/>
        public override async Task<DeleteReferencesResponse> DeleteReferencesAsync(
            RequestHeader? requestHeader, DeleteReferencesItemCollection referencesToDelete,
            CancellationToken ct)
        {
            using var activity = StartActivity(nameof(DeleteReferencesAsync));

            var operationLimit = OperationLimits.MaxNodesPerNodeManagement;
            if (operationLimit == 0 || operationLimit >= referencesToDelete.Count)
            {
                return await base.DeleteReferencesAsync(requestHeader, referencesToDelete,
                    ct).ConfigureAwait(false);
            }

            requestHeader ??= new RequestHeader();
            DeleteReferencesResponse? response = null;
            InitResponseCollections<StatusCode, StatusCodeCollection>(out var results,
                out var diagnosticInfos, out var stringTable, referencesToDelete.Count,
                operationLimit);
            foreach (var batchReferencesToDelete in referencesToDelete
                .Batch<DeleteReferencesItem, DeleteReferencesItemCollection>(operationLimit))
            {
                requestHeader.RequestHandle = 0;
                response = await base.DeleteReferencesAsync(requestHeader,
                    batchReferencesToDelete, ct).ConfigureAwait(false);

                var batchResults = response.Results;
                var batchDiagnosticInfos = response.DiagnosticInfos;
                Ua.ClientBase.ValidateResponse(batchResults, batchReferencesToDelete);
                Ua.ClientBase.ValidateDiagnosticInfos(batchDiagnosticInfos, batchReferencesToDelete);

                AddResponses<StatusCode, StatusCodeCollection>(ref results, ref diagnosticInfos,
                    ref stringTable, batchResults, batchDiagnosticInfos,
                    response.ResponseHeader.StringTable);
            }

            Debug.Assert(response != null, "Empty ops should have been covered in fast path");
            response.Results = results;
            response.DiagnosticInfos = diagnosticInfos;
            response.ResponseHeader.StringTable = stringTable;
            return response;
        }

        /// <inheritdoc/>
        protected override void RequestCompleted(IServiceRequest? request, IServiceResponse? response,
            string serviceName)
        {
            var requestHandle = response?.ResponseHeader?.RequestHandle;
            requestHandle ??= request?.RequestHeader?.RequestHandle;
            var timestamp = request?.RequestHeader?.Timestamp;
            var result = response?.ResponseHeader?.ServiceResult;
            if (TraceActivityUsingLogger)
            {
                var duration = timestamp != null ? DateTime.UtcNow - timestamp : TimeSpan.Zero;
                if (result == null || ServiceResult.IsGood(result))
                {
                    _logger.LogInformation("{Activity}#{Handle} success received after {Elapsed}.",
                        serviceName, requestHandle, duration);
                }
                else
                {
                    _logger.LogError("{Activity}#{Handle} failed with {StatusCode} in {Elapsed}.",
                        serviceName, requestHandle, result, duration);
                }
            }
            var context = Activity.Current;
            if (context == null)
            {
                return;
            }
            context.AddEvent(new ActivityEvent("Completed", tags:
            [
                System.Collections.Generic.KeyValuePair.Create("RequestHandle", (object?)requestHandle),
                System.Collections.Generic.KeyValuePair.Create("ServiceResult", (object?)result)
            ]));
        }

        /// <inheritdoc/>
        protected override void UpdateRequestHeader(IServiceRequest request, bool useDefaults,
            string serviceName)
        {
            request.RequestHeader ??= new RequestHeader();
            if (request.RequestHeader.ReturnDiagnostics == 0)
            {
                request.RequestHeader.ReturnDiagnostics = (uint)(int)ReturnDiagnostics;
            }
            if (request.RequestHeader.TimeoutHint == 0)
            {
                request.RequestHeader.TimeoutHint = (uint)OperationTimeout;
            }
            request.RequestHeader.RequestHandle = NewRequestHandle();
            request.RequestHeader.AuthenticationToken = AuthenticationToken;
            request.RequestHeader.Timestamp = DateTime.UtcNow;
            request.RequestHeader.AuditEntryId = CreateAuditLogEntry(request);

            if (TraceActivityUsingLogger)
            {
                _logger.LogInformation("{Activity}#{Handle} started...", serviceName,
                    request.RequestHeader.RequestHandle);
            }
            var context = Activity.Current;
            if (context == null)
            {
                return;
            }

            context.AddEvent(new ActivityEvent("Started", tags:
            [
                System.Collections.Generic.KeyValuePair.Create("RequestHandle",
                    (object?)request.RequestHeader.RequestHandle),
                System.Collections.Generic.KeyValuePair.Create("AuditEntryId",
                    (object?)request.RequestHeader.AuditEntryId)
            ]));
            var traceData = new AdditionalParametersType();
            // Determine the trace flag based on the 'Recorded' status.
            var traceFlags = (context.ActivityTraceFlags & ActivityTraceFlags.Recorded) != 0
                ? "01" : "00";

            // Construct the traceparent header, adhering to the W3C Trace Context format.
            var traceparent = $"00-{context.TraceId}-{context.SpanId}-{traceFlags}";
            traceData.Parameters.Add(new Opc.Ua.KeyValuePair
            {
                Key = "traceparent",
                Value = traceparent
            });
            if (request.RequestHeader.AdditionalHeader?.Body == null)
            {
                request.RequestHeader.AdditionalHeader = new ExtensionObject(traceData);
            }
            else if (request.RequestHeader.AdditionalHeader.Body is
                AdditionalParametersType existingParameters)
            {
                // Merge the trace data into the existing parameters.
                existingParameters.Parameters.AddRange(traceData.Parameters);
            }
        }

        /// <summary>
        /// Helper to start activity
        /// </summary>
        /// <param name="activity"></param>
        /// <returns></returns>
        private Activity? StartActivity(string activity)
        {
            return Observability.ActivitySource?.StartActivity(activity[..^5]);
        }

        /// <summary>
        /// Initialize the collections for a service call.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <typeparam name="C"></typeparam>
        /// <param name="results"></param>
        /// <param name="diagnosticInfos"></param>
        /// <param name="stringTable"></param>
        /// <param name="count"></param>
        /// <param name="operationLimit"></param>
        /// <remarks>
        /// Preset the result collections with null if the operation limit
        /// is sufficient or with the final size if batching is necessary.
        /// </remarks>
        private static void InitResponseCollections<T, C>(out C results,
            out DiagnosticInfoCollection diagnosticInfos,
            out StringCollection stringTable, int count, uint operationLimit)
            where C : List<T>, new()
        {
            Debug.Assert(count > operationLimit);
            results = new C() { Capacity = count };
            diagnosticInfos = new DiagnosticInfoCollection(count);
            stringTable = [];
        }

        /// <summary>
        /// Add the result of a batched service call to the results.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <typeparam name="C"></typeparam>
        /// <param name="results"></param>
        /// <param name="diagnosticInfos"></param>
        /// <param name="stringTable"></param>
        /// <param name="batchedResults"></param>
        /// <param name="batchedDiagnosticInfos"></param>
        /// <param name="batchedStringTable"></param>
        /// <remarks>
        /// Assigns the batched collection result to the result if the result
        /// collection is not initialized, adds the range to the result
        /// collections otherwise.
        /// The string table indexes are updated in the diagnostic infos if necessary.
        /// </remarks>
        private static void AddResponses<T, C>(ref C results,
            ref DiagnosticInfoCollection diagnosticInfos, ref StringCollection stringTable,
            C batchedResults, DiagnosticInfoCollection batchedDiagnosticInfos,
            StringCollection batchedStringTable) where C : List<T>
        {
            var hasDiagnosticInfos = diagnosticInfos.Count > 0;
            var hasEmptyDiagnosticInfos = diagnosticInfos.Count == 0 && results.Count > 0;
            var hasBatchDiagnosticInfos = batchedDiagnosticInfos.Count > 0;
            var correctionCount = 0;
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
                for (var i = 0; i < correctionCount; i++)
                {
                    diagnosticInfos.Add(null);
                }
            }
            else if (batchedStringTable.Count > 0)
            {
                // correct indexes in the string table
                var stringTableOffset = stringTable.Count;
                foreach (var diagnosticInfo in batchedDiagnosticInfos)
                {
                    UpdateDiagnosticInfoIndexes(diagnosticInfo, stringTableOffset);
                }
            }
            results.AddRange(batchedResults);
            diagnosticInfos.AddRange(batchedDiagnosticInfos);
            stringTable.AddRange(batchedStringTable);

            static void UpdateDiagnosticInfoIndexes(DiagnosticInfo diagnosticInfo,
                int stringTableOffset)
            {
                var depth = 0;
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
        }

        private readonly ILogger _logger;
    }
}
