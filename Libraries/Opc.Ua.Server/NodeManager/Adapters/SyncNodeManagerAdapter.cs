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

namespace Opc.Ua.Server
{
    /// <summary>
    /// The factory for an <see cref="SyncNodeManagerAdapter"/>
    /// </summary>
    public static class SyncNodeManagerAdapterFactory
    {
        /// <summary>
        /// returns an instance of <see cref="INodeManager"/>
        /// if the NodeManager does not implement the interface uses the <see cref="SyncNodeManagerAdapter"/>
        /// to create an ISyncNodeManager compatible object
        /// </summary>
        public static INodeManager3 ToSyncNodeManager(this IAsyncNodeManager nodeManager)
        {
            if (nodeManager is INodeManager3 syncNodeManager)
            {
                return syncNodeManager;
            }
            return new SyncNodeManagerAdapter(nodeManager);
        }
    }

    /// <summary>
    /// An adapter that makes a asynchronous IAsyncNodeManager conform to the INodeManager interface.
    /// </summary>
    /// <remarks>
    /// This allows asynchronous nodeManagers to be treated as synchronous, which can help
    /// compatibility with existing code.
    /// </remarks>
    public class SyncNodeManagerAdapter : INodeManager3
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="SyncNodeManagerAdapter"/> class.
        /// </summary>
        /// <param name="nodeManager">The asynchronous node manager to wrap.</param>
        /// <exception cref="ArgumentNullException"><paramref name="nodeManager"/> is <c>null</c>.</exception>
        public SyncNodeManagerAdapter(IAsyncNodeManager nodeManager)
        {
            m_nodeManager = nodeManager ?? throw new ArgumentNullException(nameof(nodeManager));
        }

        /// <inheritdoc/>
        public IEnumerable<string> NamespaceUris => m_nodeManager.NamespaceUris;

        /// <inheritdoc/>
        public void CreateAddressSpace(IDictionary<NodeId, IList<IReference>> externalReferences)
        {
            m_nodeManager.CreateAddressSpaceAsync(externalReferences).AsTask().GetAwaiter().GetResult();
        }

        /// <inheritdoc/>
        public void DeleteAddressSpace()
        {
            m_nodeManager.DeleteAddressSpaceAsync().AsTask().GetAwaiter().GetResult();
        }

        /// <inheritdoc/>
        public object GetManagerHandle(NodeId nodeId)
        {
            return m_nodeManager.GetManagerHandleAsync(nodeId).AsTask().GetAwaiter().GetResult();
        }

        /// <inheritdoc/>
        public void AddReferences(IDictionary<NodeId, IList<IReference>> references)
        {
            m_nodeManager.AddReferencesAsync(references).AsTask().GetAwaiter().GetResult();
        }

        /// <inheritdoc/>
        public ServiceResult DeleteReference(object sourceHandle, NodeId referenceTypeId, bool isInverse, ExpandedNodeId targetId, bool deleteBidirectional)
        {
            return m_nodeManager.DeleteReferenceAsync(sourceHandle, referenceTypeId, isInverse, targetId, deleteBidirectional)
                .AsTask().GetAwaiter().GetResult();
        }

        /// <inheritdoc/>
        public NodeMetadata GetNodeMetadata(OperationContext context, object targetHandle, BrowseResultMask resultMask)
        {
            return m_nodeManager.GetNodeMetadataAsync(context, targetHandle, resultMask).AsTask().GetAwaiter().GetResult();
        }

        /// <inheritdoc/>
        public void Browse(OperationContext context, ref ContinuationPoint continuationPoint, IList<ReferenceDescription> references)
        {
            continuationPoint = m_nodeManager.BrowseAsync(context, continuationPoint, references).AsTask().GetAwaiter().GetResult();
        }

        /// <inheritdoc/>
        public void TranslateBrowsePath(
            OperationContext context,
            object sourceHandle,
            RelativePathElement relativePath,
            IList<ExpandedNodeId> targetIds,
            IList<NodeId> unresolvedTargetIds)
        {
            m_nodeManager.TranslateBrowsePathAsync(context, sourceHandle, relativePath, targetIds, unresolvedTargetIds).AsTask().GetAwaiter().GetResult();
        }

        /// <inheritdoc/>
        public void Read(OperationContext context, double maxAge, IList<ReadValueId> nodesToRead, IList<DataValue> values, IList<ServiceResult> errors)
        {
            m_nodeManager.ReadAsync(context, maxAge, nodesToRead, values, errors).AsTask().GetAwaiter().GetResult();
        }

        /// <inheritdoc/>
        public void HistoryRead(
            OperationContext context,
            HistoryReadDetails details,
            TimestampsToReturn timestampsToReturn,
            bool releaseContinuationPoints,
            IList<HistoryReadValueId> nodesToRead,
            IList<HistoryReadResult> results,
            IList<ServiceResult> errors)
        {
            m_nodeManager.HistoryReadAsync(context, details, timestampsToReturn, releaseContinuationPoints, nodesToRead, results, errors)
                .AsTask().GetAwaiter().GetResult();
        }

        /// <inheritdoc/>
        public void Write(OperationContext context, IList<WriteValue> nodesToWrite, IList<ServiceResult> errors)
        {
            m_nodeManager.WriteAsync(context, nodesToWrite, errors).AsTask().GetAwaiter().GetResult();
        }

        /// <inheritdoc/>
        public void HistoryUpdate(
            OperationContext context,
            Type detailsType,
            IList<HistoryUpdateDetails> nodesToUpdate,
            IList<HistoryUpdateResult> results,
            IList<ServiceResult> errors)
        {
            m_nodeManager.HistoryUpdateAsync(context, detailsType, nodesToUpdate, results, errors).AsTask().GetAwaiter().GetResult();
        }

        /// <inheritdoc/>
        public void Call(OperationContext context, IList<CallMethodRequest> methodsToCall, IList<CallMethodResult> results, IList<ServiceResult> errors)
        {
            m_nodeManager.CallAsync(context, methodsToCall, results, errors).AsTask().GetAwaiter().GetResult();
        }

        /// <inheritdoc/>
        public ServiceResult SubscribeToEvents(
            OperationContext context,
            object sourceId,
            uint subscriptionId,
            IEventMonitoredItem monitoredItem,
            bool unsubscribe)
        {
            return m_nodeManager.SubscribeToEventsAsync(context, sourceId, subscriptionId, monitoredItem, unsubscribe).AsTask().GetAwaiter().GetResult();
        }

        /// <inheritdoc/>
        public ServiceResult SubscribeToAllEvents(OperationContext context, uint subscriptionId, IEventMonitoredItem monitoredItem, bool unsubscribe)
        {
            return m_nodeManager.SubscribeToAllEventsAsync(context, subscriptionId, monitoredItem, unsubscribe).AsTask().GetAwaiter().GetResult();
        }

        /// <inheritdoc/>
        public ServiceResult ConditionRefresh(OperationContext context, IList<IEventMonitoredItem> monitoredItems)
        {
            m_nodeManager.ConditionRefreshAsync(context, monitoredItems).AsTask().GetAwaiter().GetResult();
            return ServiceResult.Good;
        }

        /// <inheritdoc/>
        public void CreateMonitoredItems(
            OperationContext context,
            uint subscriptionId,
            double publishingInterval,
            TimestampsToReturn timestampsToReturn,
            IList<MonitoredItemCreateRequest> itemsToCreate,
            IList<ServiceResult> errors,
            IList<MonitoringFilterResult> filterErrors,
            IList<IMonitoredItem> monitoredItems,
            bool createDurable,
            MonitoredItemIdFactory monitoredItemIdFactory)
        {
            m_nodeManager.CreateMonitoredItemsAsync(context, subscriptionId, publishingInterval, timestampsToReturn,
                itemsToCreate, errors, filterErrors, monitoredItems, createDurable, monitoredItemIdFactory)
                .AsTask().GetAwaiter().GetResult();
        }

        /// <inheritdoc/>
        public void RestoreMonitoredItems(IList<IStoredMonitoredItem> itemsToRestore, IList<IMonitoredItem> monitoredItems, IUserIdentity savedOwnerIdentity)
        {
            m_nodeManager.RestoreMonitoredItemsAsync(itemsToRestore, monitoredItems, savedOwnerIdentity).AsTask().GetAwaiter().GetResult();
        }

        /// <inheritdoc/>
        public void ModifyMonitoredItems(
            OperationContext context,
            TimestampsToReturn timestampsToReturn,
            IList<IMonitoredItem> monitoredItems,
            IList<MonitoredItemModifyRequest> itemsToModify,
            IList<ServiceResult> errors,
            IList<MonitoringFilterResult> filterErrors)
        {
            m_nodeManager.ModifyMonitoredItemsAsync(context, timestampsToReturn, monitoredItems, itemsToModify, errors, filterErrors)
                .AsTask().GetAwaiter().GetResult();
        }

        /// <inheritdoc/>
        public void DeleteMonitoredItems(
            OperationContext context,
            IList<IMonitoredItem> monitoredItems,
            IList<bool> processedItems,
            IList<ServiceResult> errors)
        {
            m_nodeManager.DeleteMonitoredItemsAsync(context, monitoredItems, processedItems, errors).AsTask().GetAwaiter().GetResult();
        }

        /// <inheritdoc/>
        public void TransferMonitoredItems(
            OperationContext context,
            bool sendInitialValues,
            IList<IMonitoredItem> monitoredItems,
            IList<bool> processedItems,
            IList<ServiceResult> errors)
        {
            m_nodeManager.TransferMonitoredItemsAsync(context, sendInitialValues, monitoredItems, processedItems, errors).AsTask().GetAwaiter().GetResult();
        }

        /// <inheritdoc/>
        public void SetMonitoringMode(
            OperationContext context,
            MonitoringMode monitoringMode,
            IList<IMonitoredItem> monitoredItems,
            IList<bool> processedItems,
            IList<ServiceResult> errors)
        {
            m_nodeManager.SetMonitoringModeAsync(context, monitoringMode, monitoredItems, processedItems, errors).AsTask().GetAwaiter().GetResult();
        }

        /// <inheritdoc/>
        public void SessionClosing(OperationContext context, NodeId sessionId, bool deleteSubscriptions)
        {
            m_nodeManager.SessionClosingAsync(context, sessionId, deleteSubscriptions).AsTask().GetAwaiter().GetResult();
        }

        /// <inheritdoc/>
        public bool IsNodeInView(OperationContext context, NodeId viewId, object nodeHandle)
        {
            return m_nodeManager.IsNodeInViewAsync(context, viewId, nodeHandle).AsTask().GetAwaiter().GetResult();
        }

        /// <inheritdoc/>
        public NodeMetadata GetPermissionMetadata(
            OperationContext context,
            object targetHandle,
            BrowseResultMask resultMask,
            Dictionary<NodeId, List<object>> uniqueNodesServiceAttributesCache,
            bool permissionsOnly)
        {
            return m_nodeManager.GetPermissionMetadataAsync(context, targetHandle, resultMask, uniqueNodesServiceAttributesCache, permissionsOnly)
                .AsTask().GetAwaiter().GetResult();
        }

        /// <inheritdoc/>
        public ServiceResult ValidateEventRolePermissions(IEventMonitoredItem monitoredItem, IFilterTarget filterTarget)
        {
            return m_nodeManager.ValidateEventRolePermissionsAsync(monitoredItem, filterTarget).AsTask().GetAwaiter().GetResult();
        }

        /// <inheritdoc/>
        public ServiceResult ValidateRolePermissions(OperationContext operationContext, NodeId nodeId, PermissionType requestedPermission)
        {
            return m_nodeManager.ValidateRolePermissionsAsync(operationContext, nodeId, requestedPermission).AsTask().GetAwaiter().GetResult();
        }

        private readonly IAsyncNodeManager m_nodeManager;
    }
}
