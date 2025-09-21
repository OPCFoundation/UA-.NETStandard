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
        public static INodeManager2 ToSyncNodeManager(this IAsyncNodeManager nodeManager)
        {
            if (nodeManager is INodeManager2 syncNodeManager)
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
    public class SyncNodeManagerAdapter : INodeManager2
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="SyncNodeManagerAdapter"/> class.
        /// </summary>
        /// <param name="nodeManager">The asynchronous node manager to wrap.</param>
        /// <exception cref="ArgumentNullException"><paramref name="nodeManager"/> is <c>null</c>.</exception>
        public SyncNodeManagerAdapter(IAsyncNodeManager nodeManager)
        {
            m_nodeManager = nodeManager;
        }

        private readonly IAsyncNodeManager m_nodeManager;

        /// <inheritdoc/>
        public IEnumerable<string> NamespaceUris => throw new NotImplementedException();

        /// <inheritdoc/>
        public void CreateAddressSpace(IDictionary<NodeId, IList<IReference>> externalReferences)
        {
            throw new NotImplementedException();
        }

        /// <inheritdoc/>
        public void DeleteAddressSpace()
        {
            throw new NotImplementedException();
        }

        /// <inheritdoc/>
        public object GetManagerHandle(NodeId nodeId)
        {
            throw new NotImplementedException();
        }

        /// <inheritdoc/>
        public void AddReferences(IDictionary<NodeId, IList<IReference>> references)
        {
            throw new NotImplementedException();
        }

        /// <inheritdoc/>
        public ServiceResult DeleteReference(object sourceHandle, NodeId referenceTypeId, bool isInverse, ExpandedNodeId targetId, bool deleteBidirectional)
        {
            throw new NotImplementedException();
        }

        /// <inheritdoc/>
        public NodeMetadata GetNodeMetadata(OperationContext context, object targetHandle, BrowseResultMask resultMask)
        {
            throw new NotImplementedException();
        }

        /// <inheritdoc/>
        public void Browse(OperationContext context, ref ContinuationPoint continuationPoint, IList<ReferenceDescription> references)
        {
            throw new NotImplementedException();
        }

        /// <inheritdoc/>
        public void TranslateBrowsePath(
            OperationContext context,
            object sourceHandle,
            RelativePathElement relativePath,
            IList<ExpandedNodeId> targetIds,
            IList<NodeId> unresolvedTargetIds)
        {
            throw new NotImplementedException();
        }

        /// <inheritdoc/>
        public void Read(OperationContext context, double maxAge, IList<ReadValueId> nodesToRead, IList<DataValue> values, IList<ServiceResult> errors)
        {
            throw new NotImplementedException();
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
            throw new NotImplementedException();
        }

        /// <inheritdoc/>
        public void Write(OperationContext context, IList<WriteValue> nodesToWrite, IList<ServiceResult> errors)
        {
            throw new NotImplementedException();
        }

        /// <inheritdoc/>
        public void HistoryUpdate(
            OperationContext context,
            Type detailsType,
            IList<HistoryUpdateDetails> nodesToUpdate,
            IList<HistoryUpdateResult> results,
            IList<ServiceResult> errors)
        {
            throw new NotImplementedException();
        }

        /// <inheritdoc/>
        public void Call(OperationContext context, IList<CallMethodRequest> methodsToCall, IList<CallMethodResult> results, IList<ServiceResult> errors)
        {
            throw new NotImplementedException();
        }

        /// <inheritdoc/>
        public ServiceResult SubscribeToEvents(
            OperationContext context,
            object sourceId,
            uint subscriptionId,
            IEventMonitoredItem monitoredItem,
            bool unsubscribe)
        {
            throw new NotImplementedException();
        }

        /// <inheritdoc/>
        public ServiceResult SubscribeToAllEvents(OperationContext context, uint subscriptionId, IEventMonitoredItem monitoredItem, bool unsubscribe)
        {
            throw new NotImplementedException();
        }

        /// <inheritdoc/>
        public ServiceResult ConditionRefresh(OperationContext context, IList<IEventMonitoredItem> monitoredItems)
        {
            throw new NotImplementedException();
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
            throw new NotImplementedException();
        }

        /// <inheritdoc/>
        public void RestoreMonitoredItems(IList<IStoredMonitoredItem> itemsToRestore, IList<IMonitoredItem> monitoredItems, IUserIdentity savedOwnerIdentity)
        {
            throw new NotImplementedException();
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
            throw new NotImplementedException();
        }

        /// <inheritdoc/>
        public void DeleteMonitoredItems(
            OperationContext context,
            IList<IMonitoredItem> monitoredItems,
            IList<bool> processedItems,
            IList<ServiceResult> errors)
        {
            throw new NotImplementedException();
        }

        /// <inheritdoc/>
        public void TransferMonitoredItems(
            OperationContext context,
            bool sendInitialValues,
            IList<IMonitoredItem> monitoredItems,
            IList<bool> processedItems,
            IList<ServiceResult> errors)
        {
            throw new NotImplementedException();
        }

        /// <inheritdoc/>
        public void SetMonitoringMode(
            OperationContext context,
            MonitoringMode monitoringMode,
            IList<IMonitoredItem> monitoredItems,
            IList<bool> processedItems,
            IList<ServiceResult> errors)
        {
            throw new NotImplementedException();
        }

        /// <inheritdoc/>
        public void SessionClosing(OperationContext context, NodeId sessionId, bool deleteSubscriptions)
        {
            throw new NotImplementedException();
        }

        /// <inheritdoc/>
        public bool IsNodeInView(OperationContext context, NodeId viewId, object nodeHandle)
        {
            throw new NotImplementedException();
        }

        /// <inheritdoc/>
        public NodeMetadata GetPermissionMetadata(
            OperationContext context,
            object targetHandle,
            BrowseResultMask resultMask,
            Dictionary<NodeId, List<object>> uniqueNodesServiceAttributesCache,
            bool permissionsOnly)
        {
            throw new NotImplementedException();
        }
    }
}
