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
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Service-dispatch surface of <see cref="MasterNodeManager"/>: the OPC UA session
    /// service entry points and the protected helpers derived classes build on. The
    /// implementations live in <see cref="NodeManagerServiceDispatcher"/>, which reads
    /// the routing snapshot and never touches lifecycle coordination state; the members
    /// here stay on <see cref="MasterNodeManager"/> so overrides, protected access, and
    /// the public API surface are unchanged.
    /// </summary>
    public partial class MasterNodeManager
    {
        /// <summary>
        /// Determine the required history access permission depending on the HistoryUpdateDetails
        /// </summary>
        /// <param name="historyUpdateDetails">The HistoryUpdateDetails passed in</param>
        /// <returns>The corresponding history access permission</returns>
        protected internal static PermissionType DetermineHistoryAccessPermission(
            HistoryUpdateDetails historyUpdateDetails)
        {
            Type detailsType = historyUpdateDetails.GetType();

            if (detailsType == typeof(UpdateDataDetails))
            {
                var updateDataDetails = (UpdateDataDetails)historyUpdateDetails;
                return GetHistoryPermissionType(updateDataDetails.PerformInsertReplace);
            }
            else if (detailsType == typeof(UpdateStructureDataDetails))
            {
                var updateStructureDataDetails = (UpdateStructureDataDetails)historyUpdateDetails;
                return GetHistoryPermissionType(updateStructureDataDetails.PerformInsertReplace);
            }
            else if (detailsType == typeof(UpdateEventDetails))
            {
                var updateEventDetails = (UpdateEventDetails)historyUpdateDetails;
                return GetHistoryPermissionType(updateEventDetails.PerformInsertReplace);
            }
            else if (detailsType == typeof(DeleteRawModifiedDetails) ||
                detailsType == typeof(DeleteAtTimeDetails) ||
                detailsType == typeof(DeleteEventDetails))
            {
                return PermissionType.DeleteHistory;
            }

            return PermissionType.ModifyHistory;
        }

        /// <summary>
        ///  Determine the History PermissionType depending on PerformUpdateType
        /// </summary>
        /// <returns>The corresponding PermissionType</returns>
        protected static PermissionType GetHistoryPermissionType(PerformUpdateType updateType)
        {
            switch (updateType)
            {
                case PerformUpdateType.Insert:
                    return PermissionType.InsertHistory;
                case PerformUpdateType.Update:
                    return PermissionType.InsertHistory | PermissionType.ModifyHistory;
                case PerformUpdateType.Replace:
                case PerformUpdateType.Remove:
                    return PermissionType.ModifyHistory;
                default:
                    Debug.Fail($"Unexpected update type {updateType}");
                    return PermissionType.ModifyHistory;
            }
        }

        /// <inheritdoc/>
        public virtual object? GetManagerHandle(NodeId nodeId, out INodeManager? nodeManager)
        {
            return m_serviceDispatch.GetManagerHandle(nodeId, out nodeManager);
        }

        /// <inheritdoc/>
        [Obsolete("Use GetManagerHandleAsync instead.")]
        public virtual object? GetManagerHandle(NodeId nodeId, out IAsyncNodeManager? nodeManager)
        {
            (object? handle, IAsyncNodeManager? nodeManager) result =
                GetManagerHandleAsync(nodeId).AsTask().GetAwaiter().GetResult();

            nodeManager = result.nodeManager;

            return result.handle;
        }

        /// <inheritdoc/>
        public virtual ValueTask<(object? handle, IAsyncNodeManager? nodeManager)>
            GetManagerHandleAsync(NodeId nodeId, CancellationToken cancellationToken = default)
        {
            return m_serviceDispatch.GetManagerHandleAsync(nodeId, cancellationToken);
        }

        /// <summary>
        /// Registers a set of node ids.
        /// </summary>
        /// <remarks>
        /// The default master node manager returns the requested node ids unchanged, so registered-node
        /// results remain stable across replicas without additional mirroring.
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="nodesToRegister"/> is <c>null</c>.</exception>
        public virtual void RegisterNodes(
            OperationContext context,
            ArrayOf<NodeId> nodesToRegister,
            out ArrayOf<NodeId> registeredNodeIds)
        {
            // return the node id provided.
            registeredNodeIds = nodesToRegister;

            m_logger.MasterNodeManagerRegisterNodesCountCount(nodesToRegister.Count);

            // it is up to the node managers to assign the handles.
            /*
            List<bool> processedNodes = new List<bool>(new bool[itemsToDelete.Count]);

            for (int ii = 0; ii < m_nodeManagers.Count; ii++)
            {
                m_nodeManagers[ii].RegisterNodes(
                    context,
                    nodesToRegister,
                    registeredNodeIds,
                    processedNodes);
            }
            */
        }

        /// <inheritdoc/>
        public virtual void UnregisterNodes(
            OperationContext context,
            ArrayOf<NodeId> nodesToUnregister)
        {
            m_logger.MasterNodeManagerUnregisterNodesCountCount(nodesToUnregister.Count);

            // it is up to the node managers to assign the handles.
            /*
            List<bool> processedNodes = new List<bool>(new bool[itemsToDelete.Count]);

            for (int ii = 0; ii < m_nodeManagers.Count; ii++)
            {
                m_nodeManagers[ii].RegisterNodes(
                    context,
                    nodesToUnregister,
                    processedNodes);
            }
            */
        }

        /// <summary>
        /// Translates a start node id plus a relative paths into a node id.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="browsePaths"/> is <c>null</c>.</exception>
        /// <exception cref="ServiceResultException"></exception>
        [Obsolete("Use TranslateBrowsePathsToNodeIdsAsync instead.")]
        public virtual void TranslateBrowsePathsToNodeIds(
            OperationContext context,
            ArrayOf<BrowsePath> browsePaths,
            out ArrayOf<BrowsePathResult> results,
            out ArrayOf<DiagnosticInfo> diagnosticInfos)
        {
            (results, diagnosticInfos) = TranslateBrowsePathsToNodeIdsAsync(
                context,
                browsePaths).AsTask().GetAwaiter().GetResult();
        }

        /// <inheritdoc/>
        public virtual ValueTask<(ArrayOf<BrowsePathResult> results, ArrayOf<DiagnosticInfo> diagnosticInfos)>
            TranslateBrowsePathsToNodeIdsAsync(
            OperationContext context,
            ArrayOf<BrowsePath> browsePaths,
            CancellationToken cancellationToken = default)
        {
            return m_serviceDispatch.TranslateBrowsePathsToNodeIdsAsync(
                context,
                browsePaths,
                cancellationToken);
        }

        /// <summary>
        /// Updates the diagnostics return parameter.
        /// </summary>
        protected void UpdateDiagnostics(
            OperationContext context,
            bool diagnosticsExist,
            ref List<DiagnosticInfo> diagnosticInfos)
        {
            m_serviceDispatch.UpdateDiagnostics(context, diagnosticsExist, ref diagnosticInfos);
        }

        /// <summary>
        /// Translates a browse path.
        /// </summary>
        protected ValueTask<ServiceResult> TranslateBrowsePathAsync(
            OperationContext context,
            BrowsePath browsePath,
            BrowsePathResult result,
            CancellationToken cancellationToken)
        {
            return m_serviceDispatch.TranslateBrowsePathAsync(context, browsePath, result, cancellationToken);
        }

        /// <inheritdoc/>
        public virtual ValueTask<(ArrayOf<BrowseResult> results, ArrayOf<DiagnosticInfo> diagnosticInfos)> BrowseAsync(
            OperationContext context,
            ViewDescription view,
            uint maxReferencesPerNode,
            ArrayOf<BrowseDescription> nodesToBrowse,
            CancellationToken cancellationToken = default)
        {
            return m_serviceDispatch.BrowseAsync(
                context,
                view,
                maxReferencesPerNode,
                nodesToBrowse,
                cancellationToken);
        }

        /// <inheritdoc/>
        public virtual ValueTask<(ArrayOf<BrowseResult> results, ArrayOf<DiagnosticInfo> diagnosticInfos)>
            BrowseNextAsync(
                OperationContext context,
                bool releaseContinuationPoints,
                ArrayOf<ByteString> continuationPoints,
                CancellationToken cancellationToken = default)
        {
            return m_serviceDispatch.BrowseNextAsync(
                context,
                releaseContinuationPoints,
                continuationPoints,
                cancellationToken);
        }

        /// <summary>
        /// Returns the set of references that meet the filter criteria.
        /// </summary>
        protected ValueTask<ServiceResult> BrowseAsync(
            OperationContext context,
            ViewDescription? view,
            uint maxReferencesPerNode,
            bool assignContinuationPoint,
            BrowseDescription nodeToBrowse,
            BrowseResult result,
            CancellationToken cancellationToken = default)
        {
            return m_serviceDispatch.BrowseAsync(
                context,
                view,
                maxReferencesPerNode,
                assignContinuationPoint,
                nodeToBrowse,
                result,
                cancellationToken);
        }

        /// <summary>
        /// Loops until browse is complete for max results reached.
        /// </summary>
        protected ValueTask<(
            ServiceResult serviceResult,
            ContinuationPoint? cp,
            ArrayOf<ReferenceDescription> references
            )> FetchReferencesAsync(
                OperationContext context,
                bool assignContinuationPoint,
                ContinuationPoint cp,
                ArrayOf<ReferenceDescription> references,
                CancellationToken cancellationToken = default)
        {
            return m_serviceDispatch.FetchReferencesAsync(
                context,
                assignContinuationPoint,
                cp,
                references,
                cancellationToken);
        }

        /// <inheritdoc/>
        public ValueTask<NodeState?> FindNodeInAddressSpaceAsync(NodeId nodeId, CancellationToken cancellationToken = default)
        {
            return m_serviceDispatch.FindNodeInAddressSpaceAsync(nodeId, cancellationToken);
        }

        /// <inheritdoc/>
        public virtual ValueTask<(ArrayOf<DataValue> values, ArrayOf<DiagnosticInfo> diagnosticInfos)> ReadAsync(
            OperationContext context,
            double maxAge,
            TimestampsToReturn timestampsToReturn,
            ArrayOf<ReadValueId> nodesToRead,
            CancellationToken cancellationToken = default)
        {
            return m_serviceDispatch.ReadAsync(
                context,
                maxAge,
                timestampsToReturn,
                nodesToRead,
                cancellationToken);
        }

        /// <inheritdoc/>
        public virtual ValueTask<(ArrayOf<HistoryReadResult> values, ArrayOf<DiagnosticInfo> diagnosticInfos)> HistoryReadAsync(
            OperationContext context,
            ExtensionObject historyReadDetails,
            TimestampsToReturn timestampsToReturn,
            bool releaseContinuationPoints,
            ArrayOf<HistoryReadValueId> nodesToRead,
            CancellationToken cancellationToken = default)
        {
            return m_serviceDispatch.HistoryReadAsync(
                context,
                historyReadDetails,
                timestampsToReturn,
                releaseContinuationPoints,
                nodesToRead,
                cancellationToken);
        }

        /// <inheritdoc/>
        public virtual ValueTask<(ArrayOf<StatusCode> results, ArrayOf<DiagnosticInfo> diagnosticInfos)> WriteAsync(
            OperationContext context,
            ArrayOf<WriteValue> nodesToWrite,
            CancellationToken cancellationToken = default)
        {
            return m_serviceDispatch.WriteAsync(context, nodesToWrite, cancellationToken);
        }

        /// <inheritdoc/>
        public virtual ValueTask<(ArrayOf<HistoryUpdateResult> results, ArrayOf<DiagnosticInfo> diagnosticInfos)>
            HistoryUpdateAsync(
                OperationContext context,
                ArrayOf<ExtensionObject> historyUpdateDetails,
                CancellationToken cancellationToken = default)
        {
            return m_serviceDispatch.HistoryUpdateAsync(context, historyUpdateDetails, cancellationToken);
        }

        /// <inheritdoc/>
        public virtual ValueTask<(ArrayOf<CallMethodResult> results, ArrayOf<DiagnosticInfo> diagnosticInfos)>
            CallAsync(
                OperationContext context,
                ArrayOf<CallMethodRequest> methodsToCall,
                CancellationToken cancellationToken = default)
        {
            return m_serviceDispatch.CallAsync(context, methodsToCall, cancellationToken);
        }

        /// <inheritdoc/>
        public virtual ValueTask ConditionRefreshAsync(
            OperationContext context,
            IList<IEventMonitoredItem> monitoredItems,
            CancellationToken cancellationToken = default)
        {
            return m_serviceDispatch.ConditionRefreshAsync(context, monitoredItems, cancellationToken);
        }

        /// <summary>
        /// Creates a set of monitored items.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="context"/> is <c>null</c>.</exception>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        /// <exception cref="ServiceResultException"></exception>
        [Obsolete("Use CreateMonitoredItemsAsync")]
        public virtual void CreateMonitoredItems(
            OperationContext context,
            uint subscriptionId,
            double publishingInterval,
            TimestampsToReturn timestampsToReturn,
            ArrayOf<MonitoredItemCreateRequest> itemsToCreate,
            IList<ServiceResult> errors,
            IList<MonitoringFilterResult> filterResults,
            IList<IMonitoredItem> monitoredItems,
            bool createDurable)
        {
            CreateMonitoredItemsAsync(
                context,
                subscriptionId,
                publishingInterval,
                timestampsToReturn,
                itemsToCreate,
                errors,
                filterResults,
                monitoredItems,
                createDurable).AsTask().GetAwaiter().GetResult();
        }

        /// <inheritdoc/>
        public virtual ValueTask CreateMonitoredItemsAsync(
            OperationContext context,
            uint subscriptionId,
            double publishingInterval,
            TimestampsToReturn timestampsToReturn,
            ArrayOf<MonitoredItemCreateRequest> itemsToCreate,
            IList<ServiceResult> errors,
            IList<MonitoringFilterResult> filterResults,
            IList<IMonitoredItem> monitoredItems,
            bool createDurable,
            CancellationToken cancellationToken = default)
        {
            return m_serviceDispatch.CreateMonitoredItemsAsync(
                context,
                subscriptionId,
                publishingInterval,
                timestampsToReturn,
                itemsToCreate,
                errors,
                filterResults,
                monitoredItems,
                createDurable,
                cancellationToken);
        }

        /// <inheritdoc/>
        public virtual ValueTask RestoreMonitoredItemsAsync(
            IList<IStoredMonitoredItem> itemsToRestore,
            IList<IMonitoredItem> monitoredItems,
            IUserIdentity savedOwnerIdentity,
            CancellationToken cancellationToken = default)
        {
            return m_serviceDispatch.RestoreMonitoredItemsAsync(
                itemsToRestore,
                monitoredItems,
                savedOwnerIdentity,
                cancellationToken);
        }

        /// <summary>
        /// Pre-hydrates monitored-item data/event queues from the configured
        /// <see cref="ISubscriptionStore"/> so the synchronous monitored-item creation path can
        /// consume them without blocking on an asynchronous store.
        /// </summary>
        /// <param name="itemsToRestore">The monitored items being restored.</param>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        private ValueTask PreHydrateMonitoredItemQueuesAsync(
            IList<IStoredMonitoredItem> itemsToRestore,
            CancellationToken cancellationToken)
        {
            return m_serviceDispatch.PreHydrateMonitoredItemQueuesAsync(itemsToRestore, cancellationToken);
        }

        /// <summary>
        /// Modifies a set of monitored items.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="context"/> is <c>null</c>.</exception>
        /// <exception cref="ServiceResultException"></exception>
        [Obsolete("Use ModifyMonitoredItemsAsync")]
        public virtual void ModifyMonitoredItems(
            OperationContext context,
            TimestampsToReturn timestampsToReturn,
            IList<IMonitoredItem> monitoredItems,
            ArrayOf<MonitoredItemModifyRequest> itemsToModify,
            IList<ServiceResult> errors,
            IList<MonitoringFilterResult> filterResults)
        {
            ModifyMonitoredItemsAsync(
                context,
                timestampsToReturn,
                monitoredItems,
                itemsToModify,
                errors,
                filterResults).AsTask().GetAwaiter().GetResult();
        }

        /// <inheritdoc/>
        public virtual ValueTask ModifyMonitoredItemsAsync(
            OperationContext context,
            TimestampsToReturn timestampsToReturn,
            IList<IMonitoredItem> monitoredItems,
            ArrayOf<MonitoredItemModifyRequest> itemsToModify,
            IList<ServiceResult> errors,
            IList<MonitoringFilterResult> filterResults,
            CancellationToken cancellationToken = default)
        {
            return m_serviceDispatch.ModifyMonitoredItemsAsync(
                context,
                timestampsToReturn,
                monitoredItems,
                itemsToModify,
                errors,
                filterResults,
                cancellationToken);
        }

        /// <summary>
        /// Transfers a set of monitored items.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="context"/> is <c>null</c>.</exception>
        [Obsolete("Use TransferMonitoredItemsAsync")]
        public virtual void TransferMonitoredItems(
            OperationContext context,
            bool sendInitialValues,
            IList<IMonitoredItem> monitoredItems,
            IList<ServiceResult> errors)
        {
            TransferMonitoredItemsAsync(
                context,
                sendInitialValues,
                monitoredItems,
                errors,
                new MonitoredItemTransferOptions()).AsTask().GetAwaiter().GetResult();
        }

        /// <summary>
        /// Transfers a set of monitored items.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="context"/> is <c>null</c>.</exception>
        [Obsolete("Use TransferMonitoredItemsAsync with MonitoredItemTransferOptions.")]
        public virtual ValueTask TransferMonitoredItemsAsync(
            OperationContext context,
            bool sendInitialValues,
            IList<IMonitoredItem> monitoredItems,
            IList<ServiceResult> errors,
            CancellationToken cancellationToken = default)
        {
            return TransferMonitoredItemsAsync(
                context,
                sendInitialValues,
                monitoredItems,
                errors,
                new MonitoredItemTransferOptions(),
                cancellationToken);
        }

        /// <inheritdoc/>
        public virtual ValueTask TransferMonitoredItemsAsync(
            OperationContext context,
            bool sendInitialValues,
            IList<IMonitoredItem> monitoredItems,
            IList<ServiceResult> errors,
            MonitoredItemTransferOptions transferOptions,
            CancellationToken cancellationToken = default)
        {
            return m_serviceDispatch.TransferMonitoredItemsAsync(
                context,
                sendInitialValues,
                monitoredItems,
                errors,
                transferOptions,
                cancellationToken);
        }

        /// <summary>
        /// Deletes a set of monitored items.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="context"/> is <c>null</c>.</exception>
        [Obsolete("Use DeleteMonitoredItemsAsync")]
        public virtual void DeleteMonitoredItems(
            OperationContext context,
            uint subscriptionId,
            IList<IMonitoredItem> itemsToDelete,
            IList<ServiceResult> errors)
        {
            DeleteMonitoredItemsAsync(
                context,
                subscriptionId,
                itemsToDelete,
                errors).AsTask().GetAwaiter().GetResult();
        }

        /// <inheritdoc/>
        public virtual ValueTask DeleteMonitoredItemsAsync(
            OperationContext context,
            uint subscriptionId,
            IList<IMonitoredItem> itemsToDelete,
            IList<ServiceResult> errors,
            CancellationToken cancellationToken = default)
        {
            return m_serviceDispatch.DeleteMonitoredItemsAsync(
                context,
                subscriptionId,
                itemsToDelete,
                errors,
                cancellationToken);
        }

        /// <inheritdoc/>
        public virtual ValueTask SetMonitoringModeAsync(
            OperationContext context,
            MonitoringMode monitoringMode,
            IList<IMonitoredItem> itemsToModify,
            IList<ServiceResult> errors,
            CancellationToken cancellationToken = default)
        {
            return m_serviceDispatch.SetMonitoringModeAsync(
                context,
                monitoringMode,
                itemsToModify,
                errors,
                cancellationToken);
        }

        /// <summary>
        /// The maximum number of continuation points per Browse request, read live so
        /// service dispatch observes the configured value on every call.
        /// </summary>
        internal uint MaxContinuationPointsPerBrowse => m_maxContinuationPointsPerBrowse;

        /// <summary>
        /// Validates a monitoring attributes parameter.
        /// </summary>
        protected internal static ServiceResult? ValidateMonitoringAttributes(MonitoringParameters attributes)
        {
            // check for null structure.
            if (attributes == null)
            {
                return new ServiceResult(StatusCodes.BadStructureMissing);
            }

            // If a filter was specified, it needs to be a known filter structure.
            if (!attributes.Filter.IsNull &&
                !attributes.Filter.TryGetValue(out MonitoringFilter? _))
            {
                return new ServiceResult(StatusCodes.BadMonitoredItemFilterInvalid);
            }

            // passed basic validation.
            return null;
        }

        /// <summary>
        /// Validates a monitoring filter.
        /// </summary>
        protected internal static ServiceResult? ValidateMonitoringFilter(ExtensionObject filter)
        {
            // check that no filter is specified for non-value attributes.
            if (!filter.IsNull)
            {
                // validate data change filter.
                if (filter.TryGetValue(out DataChangeFilter? datachangeFilter))
                {
                    ServiceResult error = datachangeFilter.Validate();

                    if (ServiceResult.IsBad(error))
                    {
                        return error;
                    }
                }
            }

            // passed basic validation.
            return null;
        }

        /// <summary>
        /// Validates a monitored item create request parameter.
        /// </summary>
        protected ValueTask<ServiceResult?> ValidateMonitoredItemCreateRequestAsync(
            OperationContext operationContext,
            MonitoredItemCreateRequest item,
            CancellationToken cancellationToken = default)
        {
            return m_serviceDispatch.ValidateMonitoredItemCreateRequestAsync(
                operationContext,
                item,
                cancellationToken);
        }

        /// <summary>
        /// Validates a monitored item modify request parameter.
        /// </summary>
        protected internal static ServiceResult? ValidateMonitoredItemModifyRequest(
            MonitoredItemModifyRequest item)
        {
            // check for null structure.
            if (item == null)
            {
                return new ServiceResult(StatusCodes.BadStructureMissing);
            }

            // check for null structure.
            MonitoringParameters attributes = item.RequestedParameters;

            ServiceResult? error = ValidateMonitoringAttributes(attributes);

            if (ServiceResult.IsBad(error))
            {
                return error;
            }

            // validate monitoring filter.
            error = ValidateMonitoringFilter(attributes.Filter);

            if (ServiceResult.IsBad(error))
            {
                return error;
            }

            // passed basic validation.
            return null;
        }

        /// <summary>
        /// Validates a call request item parameter and checks access rights and role permissions.
        /// </summary>
        protected ValueTask<ServiceResult> ValidateCallRequestItemAsync(
            OperationContext operationContext,
            CallMethodRequest callMethodRequest,
            Dictionary<NodeId, Variant[]>? uniqueNodesReadAttributes = null,
            bool permissionsOnly = false,
            CancellationToken cancellationToken = default)
        {
            return m_serviceDispatch.ValidateCallRequestItemAsync(
                operationContext,
                callMethodRequest,
                uniqueNodesReadAttributes,
                permissionsOnly,
                cancellationToken);
        }

        /// <summary>
        /// Validates a Read or MonitoredItemCreate request. It validates also access rights and role permissions
        /// </summary>
        protected ValueTask<ServiceResult?> ValidateReadRequestAsync(
            OperationContext operationContext,
            ReadValueId readValueId,
            Dictionary<NodeId, Variant[]>? uniqueNodesReadAttributes = null,
            bool permissionsOnly = false,
            CancellationToken cancellationToken = default)
        {
            return m_serviceDispatch.ValidateReadRequestAsync(
                operationContext,
                readValueId,
                uniqueNodesReadAttributes,
                permissionsOnly,
                cancellationToken);
        }

        /// <summary>
        /// Validates a Write request. It validates also access rights and role permissions
        /// </summary>
        protected ValueTask<ServiceResult?> ValidateWriteRequestAsync(
            OperationContext operationContext,
            WriteValue writeValue,
            Dictionary<NodeId, Variant[]>? uniqueNodesServiceAttributes = null,
            bool permissionsOnly = false,
            CancellationToken cancellationToken = default)
        {
            return m_serviceDispatch.ValidateWriteRequestAsync(
                operationContext,
                writeValue,
                uniqueNodesServiceAttributes,
                permissionsOnly,
                cancellationToken);
        }

        /// <summary>
        /// Validates a HistoryRead request. It validates also access rights and role permissions
        /// </summary>
        protected ValueTask<ServiceResult?> ValidateHistoryReadRequestAsync(
            OperationContext operationContext,
            HistoryReadValueId historyReadValueId,
            CancellationToken cancellationToken = default)
        {
            return m_serviceDispatch.ValidateHistoryReadRequestAsync(
                operationContext,
                historyReadValueId,
                cancellationToken);
        }

        /// <summary>
        ///  Validates a HistoryUpdate request. It validates also access rights and role permissions
        /// </summary>
        protected ValueTask<ServiceResult?> ValidateHistoryUpdateRequestAsync(
            OperationContext operationContext,
            HistoryUpdateDetails historyUpdateDetails,
            CancellationToken cancellationToken = default)
        {
            return m_serviceDispatch.ValidateHistoryUpdateRequestAsync(
                operationContext,
                historyUpdateDetails,
                cancellationToken);
        }

        /// <summary>
        /// Check if the Base NodeClass attributes and NameSpace meta-data attributes
        /// are valid for the given operation context of the specified node.
        /// </summary>
        /// <param name="context">The Operation Context</param>
        /// <param name="nodeId">The node whose attributes are validated</param>
        /// <param name="requestedPermision">The requested permission</param>
        /// <param name="uniqueNodesServiceAttributes">The cache holding the values of the attributes neeeded to be used in subsequent calls</param>
        /// <param name="permissionsOnly">Only the AccessRestrictions and RolePermission attributes are read. Should be false if uniqueNodesServiceAttributes is not null</param>
        /// <param name="cancellationToken">The Cancellation Token</param>
        /// <returns>StatusCode Good if permission is granted, BadUserAccessDenied if not granted
        /// or a bad status code describing the validation process failure </returns>
        protected ValueTask<ServiceResult> ValidatePermissionsAsync(
            OperationContext context,
            NodeId nodeId,
            PermissionType requestedPermision,
            Dictionary<NodeId, Variant[]>? uniqueNodesServiceAttributes = null,
            bool permissionsOnly = false,
            CancellationToken cancellationToken = default)
        {
            return m_serviceDispatch.ValidatePermissionsAsync(
                context,
                nodeId,
                requestedPermision,
                uniqueNodesServiceAttributes,
                permissionsOnly,
                cancellationToken);
        }

        /// <summary>
        /// Check if the Base NodeClass attributes and NameSpace meta-data attributes
        /// are valid for the given operation context of the specified node.
        /// </summary>
        /// <param name="context">The Operation Context</param>
        /// <param name="nodeManager">The node manager handling the nodeHandle</param>
        /// <param name="nodeHandle">The node handle of the node whose attributes are validated</param>
        /// <param name="requestedPermision">The requested permission</param>
        /// <param name="uniqueNodesServiceAttributes">The cache holding the values of the attributes neeeded to be used in subsequent calls</param>
        /// <param name="permissionsOnly">Only the AccessRestrictions and RolePermission attributes are read. Should be false if uniqueNodesServiceAttributes is not null</param>
        /// <param name="cancellationToken">The Cancellation Token</param>
        /// <returns>StatusCode Good if permission is granted, BadUserAccessDenied if not granted
        /// or a bad status code describing the validation process failure </returns>
        protected ValueTask<ServiceResult> ValidatePermissionsAsync(
            OperationContext context,
            IAsyncNodeManager? nodeManager,
            object? nodeHandle,
            PermissionType requestedPermision,
            Dictionary<NodeId, Variant[]>? uniqueNodesServiceAttributes = null,
            bool permissionsOnly = false,
            CancellationToken cancellationToken = default)
        {
            return m_serviceDispatch.ValidatePermissionsAsync(
                context,
                nodeManager,
                nodeHandle,
                requestedPermision,
                uniqueNodesServiceAttributes,
                permissionsOnly,
                cancellationToken);
        }

        /// <summary>
        /// Validate the AccessRestrictions attribute
        /// </summary>
        /// <param name="context">The Operation Context</param>
        /// <param name="nodeMetadata">Metadata</param>
        /// <returns>Good if the AccessRestrictions passes the validation</returns>
        protected internal static ServiceResult ValidateAccessRestrictions(
            OperationContext context,
            NodeMetadata nodeMetadata)
        {
            ServiceResult serviceResult = StatusCodes.Good;

            // Type hierarchy nodes (ObjectType/VariableType and their children)
            // are universally accessible regardless of AccessRestrictions.
            if (nodeMetadata.IsPartOfTypeHierarchy)
            {
                return serviceResult;
            }

            AccessRestrictionType restrictions = AccessRestrictionType.None;

            if (nodeMetadata.AccessRestrictions != AccessRestrictionType.None)
            {
                restrictions = nodeMetadata.AccessRestrictions;
            }
            else if (nodeMetadata.DefaultAccessRestrictions != AccessRestrictionType.None)
            {
                restrictions = nodeMetadata.DefaultAccessRestrictions;
            }
            if (restrictions != AccessRestrictionType.None)
            {
                bool encryptionRequired =
                    (restrictions & AccessRestrictionType.EncryptionRequired) ==
                    AccessRestrictionType.EncryptionRequired;
                bool signingRequired =
                    (restrictions & AccessRestrictionType.SigningRequired) ==
                    AccessRestrictionType.SigningRequired;
                bool sessionRequired =
                    (restrictions & AccessRestrictionType.SessionRequired) ==
                    AccessRestrictionType.SessionRequired;
                bool applyRestrictionsToBrowse =
                    (restrictions & AccessRestrictionType.ApplyRestrictionsToBrowse) ==
                    AccessRestrictionType.ApplyRestrictionsToBrowse;

                bool browseOperation =
                    context.RequestType
                        is RequestType.Browse
                            or RequestType.BrowseNext
                            or RequestType.TranslateBrowsePathsToNodeIds;

                // Access restriction validation runs while a request is being processed,
                // which always carries a channel context with an endpoint description.
                EndpointDescription endpointDescription =
                    context.ChannelContext!.EndpointDescription!;

                if ((
                        encryptionRequired &&
                        endpointDescription.SecurityMode != MessageSecurityMode.SignAndEncrypt &&
                        endpointDescription.TransportProfileUri !=
                            Profiles.HttpsBinaryTransport &&
                        ((applyRestrictionsToBrowse && browseOperation) || !browseOperation)
                    ) ||
                    (
                        signingRequired &&
                        endpointDescription.SecurityMode != MessageSecurityMode.Sign &&
                        endpointDescription.SecurityMode != MessageSecurityMode.SignAndEncrypt &&
                        endpointDescription.TransportProfileUri !=
                            Profiles.HttpsBinaryTransport &&
                        ((applyRestrictionsToBrowse && browseOperation) || !browseOperation)
                    ) ||
                    (sessionRequired && context.Session == null))
                {
                    serviceResult = ServiceResult.Create(
                        StatusCodes.BadSecurityModeInsufficient,
                        "Access restricted to nodeId {0} due to insufficient security mode.",
                        nodeMetadata.NodeId);
                }
            }

            return serviceResult;
        }

        /// <summary>
        /// Validates the role permissions
        /// </summary>
        protected internal static ServiceResult ValidateRolePermissions(
            OperationContext context,
            NodeMetadata nodeMetadata,
            PermissionType requestedPermission,
            ILogger? logger = null)
        {
            if (nodeMetadata == null || requestedPermission == PermissionType.None)
            {
                // no permission is required hence the validation passes
                return StatusCodes.Good;
            }

            // Type hierarchy nodes (ObjectType/VariableType and their children)
            // are universally accessible regardless of RolePermissions.
            if (nodeMetadata.IsPartOfTypeHierarchy)
            {
                return StatusCodes.Good;
            }

            // get the intersection of user role permissions and role permissions
            ArrayOf<RolePermissionType> userRolePermissions = default;
            if (!nodeMetadata.UserRolePermissions.IsEmpty)
            {
                userRolePermissions = nodeMetadata.UserRolePermissions;
            }
            else if (!nodeMetadata.DefaultUserRolePermissions.IsEmpty)
            {
                userRolePermissions = nodeMetadata.DefaultUserRolePermissions;
            }

            ArrayOf<RolePermissionType> rolePermissions;
            if (!nodeMetadata.RolePermissions.IsEmpty)
            {
                rolePermissions = nodeMetadata.RolePermissions;
            }
            else
            {
                rolePermissions = nodeMetadata.DefaultRolePermissions;
            }

            if (userRolePermissions.IsEmpty && rolePermissions.IsEmpty)
            {
                // there is no restriction from role permissions
                return StatusCodes.Good;
            }

            // group all permissions defined in rolePermissions by RoleId
            var roleIdPermissions = new Dictionary<NodeId, PermissionType>();
            if (!rolePermissions.IsEmpty)
            {
                foreach (RolePermissionType rolePermission in rolePermissions)
                {
                    if (roleIdPermissions.ContainsKey(rolePermission.RoleId))
                    {
                        roleIdPermissions[rolePermission.RoleId] |= (PermissionType)rolePermission
                            .Permissions;
                    }
                    else
                    {
                        roleIdPermissions[rolePermission.RoleId] =
                            (PermissionType)rolePermission.Permissions;
                    }
                }
            }

            // group all permissions defined in userRolePermissions by RoleId
            var roleIdPermissionsDefinedForUser = new Dictionary<NodeId, PermissionType>();
            if (!userRolePermissions.IsEmpty)
            {
                foreach (RolePermissionType rolePermission in userRolePermissions)
                {
                    if (roleIdPermissionsDefinedForUser.ContainsKey(rolePermission.RoleId))
                    {
                        roleIdPermissionsDefinedForUser[rolePermission.RoleId] |= (PermissionType)
                            rolePermission.Permissions;
                    }
                    else
                    {
                        roleIdPermissionsDefinedForUser[rolePermission.RoleId] =
                            (PermissionType)rolePermission.Permissions;
                    }
                }
            }

            Dictionary<NodeId, PermissionType> commonRoleIdPermissions;
            if (rolePermissions.IsEmpty)
            {
                // there were no role permissions defined for this node only user role permissions
                commonRoleIdPermissions = roleIdPermissionsDefinedForUser;
            }
            else if (userRolePermissions.IsEmpty)
            {
                // there were no user role permissions defined only role permissions for the node
                commonRoleIdPermissions = roleIdPermissions;
            }
            else
            {
                commonRoleIdPermissions = [];
                // intersect role permissions from node and user
                foreach (NodeId roleId in roleIdPermissions.Keys)
                {
                    if (roleIdPermissionsDefinedForUser.TryGetValue(
                        roleId,
                        out PermissionType value))
                    {
                        commonRoleIdPermissions[roleId] = roleIdPermissions[roleId] & value;
                    }
                }
            }

            ArrayOf<NodeId> currentRoleIds = context?.UserIdentity?.GrantedRoleIds ?? default;
            if (currentRoleIds.IsEmpty)
            {
                logger?.CurrentUserHasNoGrantedRole();
                return ServiceResult.Create(
                    StatusCodes.BadUserAccessDenied,
                    "Current user has no granted role.");
            }

            PermissionType userActualPermissions = PermissionType.None;

            foreach (NodeId currentRoleId in currentRoleIds)
            {
                if (commonRoleIdPermissions.TryGetValue(currentRoleId, out PermissionType value))
                {
                    userActualPermissions |= value;
                    if ((value & requestedPermission) != PermissionType.None)
                    {
                        // there is one role that current session has na is listed in requested role
                        return StatusCodes.Good;
                    }
                }
            }

            logger?.RolePermissionsValidationFailedForNodeNodeId(
                nodeMetadata.NodeId,
                requestedPermission,
                userActualPermissions);

            return ServiceResult.Create(
                StatusCodes.BadUserAccessDenied,
                "The requested permission {0} is not granted for node id {1}.",
                requestedPermission,
                nodeMetadata.NodeId);
        }

        ValueTask<IMonitoredItemTransferTransaction>
            IMonitoredItemTransferCoordinator.PrepareMonitoredItemsTransferAsync(
                OperationContext destinationContext,
                bool sendInitialValues,
                IList<IMonitoredItem> monitoredItems,
                IList<ServiceResult> errors,
                MonitoredItemTransferOptions transferOptions,
                CancellationToken cancellationToken)
        {
            return m_serviceDispatch.PrepareMonitoredItemsTransferAsync(
                destinationContext,
                sendInitialValues,
                monitoredItems,
                errors,
                transferOptions,
                cancellationToken);
        }

        private readonly uint m_maxContinuationPointsPerBrowse;
        private readonly NodeManagerServiceDispatcher m_serviceDispatch;
    }
}
