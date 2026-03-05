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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Server
{
    /// <summary>
    /// The factory for an <see cref="AsyncNodeManagerAdapter"/>
    /// </summary>
    public static class AsyncNodeManagerAdapterFactory
    {
        /// <summary>
        /// returns an instance of <see cref="IAsyncNodeManager"/>
        /// if the NodeManager does not implement the interface uses the <see cref="AsyncNodeManagerAdapter"/>
        /// to create an IAsyncNodeManager compatible object
        /// </summary>
        public static IAsyncNodeManager ToAsyncNodeManager(this INodeManager nodeManager)
        {
            if (nodeManager is IAsyncNodeManager asyncNodeManager)
            {
                return asyncNodeManager;
            }
            return new AsyncNodeManagerAdapter(nodeManager);
        }
    }

    /// <summary>
    /// An adapter that makes a synchronous INodeManager conform to the IAsyncNodeManager interface.
    /// </summary>
    /// <remarks>
    /// This allows synchronous, or only partially asynchronous node managers to be treated as asynchronous, which can help
    /// unify the calling logic within the MasterNodeManager.
    /// </remarks>
    public class AsyncNodeManagerAdapter : IAsyncNodeManager, IDisposable
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="AsyncNodeManagerAdapter"/> class.
        /// </summary>
        /// <param name="nodeManager">The synchronous node manager to wrap.</param>
        /// <exception cref="ArgumentNullException"><paramref name="nodeManager"/> is <c>null</c>.</exception>
        public AsyncNodeManagerAdapter(INodeManager nodeManager)
        {
            SyncNodeManager = nodeManager ?? throw new ArgumentNullException(nameof(nodeManager));
        }

        /// <inheritdoc/>
        public IEnumerable<string> NamespaceUris => SyncNodeManager.NamespaceUris;

        /// <inheritdoc/>
        public INodeManager SyncNodeManager { get; }

        /// <inheritdoc/>
        public ValueTask AddReferencesAsync(
            IDictionary<NodeId, IList<IReference>> references,
            CancellationToken cancellationToken = default)
        {
            if (SyncNodeManager is IAsyncNodeManager asyncNodeManager)
            {
                return asyncNodeManager.AddReferencesAsync(references, cancellationToken);
            }

            SyncNodeManager.AddReferences(references);

            // Return a completed ValueTask since the underlying call is synchronous.
            return default;
        }

        /// <inheritdoc/>
        public ValueTask<ContinuationPoint> BrowseAsync(
            OperationContext context,
            ContinuationPoint continuationPoint,
            IList<ReferenceDescription> references,
            CancellationToken cancellationToken = default)
        {
            if (SyncNodeManager is IBrowseAsyncNodeManager asyncNodeManager)
            {
                return asyncNodeManager.BrowseAsync(context, continuationPoint, references, cancellationToken);
            }

            SyncNodeManager.Browse(context, ref continuationPoint, references);

            // Return a completed ValueTask since the underlying call is synchronous.
            return new ValueTask<ContinuationPoint>(continuationPoint);
        }

        /// <inheritdoc/>
        public ValueTask CallAsync(
            OperationContext context,
            IList<CallMethodRequest> methodsToCall,
            IList<CallMethodResult> results,
            IList<ServiceResult> errors,
            CancellationToken cancellationToken = default)
        {
            if (SyncNodeManager is ICallAsyncNodeManager asyncNodeManager)
            {
                return asyncNodeManager.CallAsync(context, methodsToCall, results, errors, cancellationToken);
            }

            SyncNodeManager.Call(context, methodsToCall, results, errors);

            // Return a completed ValueTask since the underlying call is synchronous.
            return default;
        }

        /// <inheritdoc/>
        public ValueTask<ServiceResult> ConditionRefreshAsync(
            OperationContext context,
            IList<IEventMonitoredItem> monitoredItems,
            CancellationToken cancellationToken = default)
        {
            if (SyncNodeManager is IConditionRefreshAsyncNodeManager asyncNodeManager)
            {
                return asyncNodeManager.ConditionRefreshAsync(context, monitoredItems, cancellationToken);
            }

            SyncNodeManager.ConditionRefresh(context, monitoredItems);

            // Return a completed ValueTask since the underlying call is synchronous.
            return default;
        }

        /// <inheritdoc/>
        public ValueTask CreateAddressSpaceAsync(
            IDictionary<NodeId, IList<IReference>> externalReferences,
            CancellationToken cancellationToken = default)
        {
            if (SyncNodeManager is IAsyncNodeManager asyncNodeManager)
            {
                return asyncNodeManager.CreateAddressSpaceAsync(externalReferences, cancellationToken);
            }

            SyncNodeManager.CreateAddressSpace(externalReferences);

            // Return a completed ValueTask since the underlying call is synchronous.
            return default;
        }

        /// <inheritdoc/>
        public ValueTask CreateMonitoredItemsAsync(OperationContext context,
                                                   uint subscriptionId,
                                                   double publishingInterval,
                                                   TimestampsToReturn timestampsToReturn,
                                                   IList<MonitoredItemCreateRequest> itemsToCreate,
                                                   IList<ServiceResult> errors,
                                                   IList<MonitoringFilterResult> filterErrors,
                                                   IList<IMonitoredItem> monitoredItems,
                                                   bool createDurable,
                                                   MonitoredItemIdFactory monitoredItemIdFactory,
                                                   CancellationToken cancellationToken = default)
        {
            if (SyncNodeManager is ICreateMonitoredItemsAsyncNodeManager asyncNodeManager)
            {
                return asyncNodeManager.CreateMonitoredItemsAsync(context,
                                                                  subscriptionId,
                                                                  publishingInterval,
                                                                  timestampsToReturn,
                                                                  itemsToCreate,
                                                                  errors,
                                                                  filterErrors,
                                                                  monitoredItems,
                                                                  createDurable,
                                                                  monitoredItemIdFactory,
                                                                  cancellationToken);
            }

            SyncNodeManager.CreateMonitoredItems(context,
                                              subscriptionId,
                                              publishingInterval,
                                              timestampsToReturn,
                                              itemsToCreate,
                                              errors,
                                              filterErrors,
                                              monitoredItems,
                                              createDurable,
                                              monitoredItemIdFactory);

            // Return a completed ValueTask since the underlying call is synchronous.
            return default;
        }

        /// <inheritdoc/>
        public ValueTask DeleteAddressSpaceAsync(CancellationToken cancellationToken = default)
        {
            if (SyncNodeManager is IAsyncNodeManager asyncNodeManager)
            {
                return asyncNodeManager.DeleteAddressSpaceAsync(cancellationToken);
            }

            SyncNodeManager.DeleteAddressSpace();

            // Return a completed ValueTask since the underlying call is synchronous.
            return default;
        }

        /// <inheritdoc/>
        public ValueTask DeleteMonitoredItemsAsync(OperationContext context,
                                                   IList<IMonitoredItem> monitoredItems,
                                                   IList<bool> processedItems,
                                                   IList<ServiceResult> errors,
                                                   CancellationToken cancellationToken = default)
        {
            if (SyncNodeManager is IDeleteMonitoredItemsAsyncNodeManager asyncNodeManager)
            {
                return asyncNodeManager.DeleteMonitoredItemsAsync(context, monitoredItems, processedItems, errors, cancellationToken);
            }

            SyncNodeManager.DeleteMonitoredItems(context, monitoredItems, processedItems, errors);

            // Return a completed ValueTask since the underlying call is synchronous.
            return default;
        }

        /// <inheritdoc/>
        public ValueTask<ServiceResult> DeleteReferenceAsync(
            object sourceHandle,
            NodeId referenceTypeId,
            bool isInverse,
            ExpandedNodeId targetId,
            bool deleteBidirectional,
            CancellationToken cancellationToken = default)
        {
            if (SyncNodeManager is IAsyncNodeManager asyncNodeManager)
            {
                return asyncNodeManager.DeleteReferenceAsync(sourceHandle,
                                                             referenceTypeId,
                                                             isInverse,
                                                             targetId,
                                                             deleteBidirectional,
                                                             cancellationToken);
            }

            ServiceResult result = SyncNodeManager.DeleteReference(sourceHandle,
                                                                 referenceTypeId,
                                                                 isInverse,
                                                                 targetId,
                                                                 deleteBidirectional);

            // Return a completed ValueTask since the underlying call is synchronous.
            return new ValueTask<ServiceResult>(result);
        }

        /// <inheritdoc/>
        public ValueTask<object> GetManagerHandleAsync(NodeId nodeId, CancellationToken cancellationToken = default)
        {
            if (SyncNodeManager is IAsyncNodeManager asyncNodeManager)
            {
                return asyncNodeManager.GetManagerHandleAsync(nodeId, cancellationToken);
            }

            object handle = SyncNodeManager.GetManagerHandle(nodeId);

            // Return a completed ValueTask since the underlying call is synchronous.
            return new ValueTask<object>(handle);
        }

        /// <inheritdoc/>
        public ValueTask<NodeMetadata> GetNodeMetadataAsync(
            OperationContext context,
            object targetHandle,
            BrowseResultMask resultMask,
            CancellationToken cancellationToken = default)
        {
            if (SyncNodeManager is IAsyncNodeManager asyncNodeManager)
            {
                return asyncNodeManager.GetNodeMetadataAsync(context, targetHandle, resultMask, cancellationToken);
            }

            NodeMetadata nodeMetadata = SyncNodeManager.GetNodeMetadata(context, targetHandle, resultMask);

            // Return a completed ValueTask since the underlying call is synchronous.
            return new ValueTask<NodeMetadata>(nodeMetadata);
        }

        /// <inheritdoc/>
        public ValueTask<NodeMetadata> GetPermissionMetadataAsync(
            OperationContext context,
            object targetHandle,
            BrowseResultMask resultMask,
            Dictionary<NodeId, Variant[]> uniqueNodesServiceAttributesCache,
            bool permissionsOnly,
            CancellationToken cancellationToken = default)
        {
            if (SyncNodeManager is IAsyncNodeManager asyncNodeManager)
            {
                return asyncNodeManager.GetPermissionMetadataAsync(
                    context,
                    targetHandle,
                    resultMask,
                    uniqueNodesServiceAttributesCache,
                    permissionsOnly,
                    cancellationToken);
            }
            if (SyncNodeManager is INodeManager2 nodeManager2)
            {
                Dictionary<NodeId, List<object>> syncuniqueNodesServiceAttributesCache = null;

                if (targetHandle is NodeHandle nodeHandle &&
                    uniqueNodesServiceAttributesCache?.TryGetValue(nodeHandle.NodeId, out Variant[] attributes) == true)
                {
                    List<object> boxedAttributes = new(attributes.Length);
                    foreach (Variant value in attributes)
                    {
                        boxedAttributes.Add(value.AsBoxedObject());
                    }

                    syncuniqueNodesServiceAttributesCache = new Dictionary<NodeId, List<object>>
                    {
                        { nodeHandle.NodeId, boxedAttributes }
                    };
                }

                NodeMetadata nodeMetadata = nodeManager2.GetPermissionMetadata(
                    context,
                    targetHandle,
                    resultMask,
                    syncuniqueNodesServiceAttributesCache,
                    permissionsOnly);

                if (targetHandle is NodeHandle nodeHandleAfter &&
                    syncuniqueNodesServiceAttributesCache?.TryGetValue(nodeHandleAfter.NodeId, out List<object> attributesAfter) == true)
                {
                    var attributesArray = new List<Variant>();
                    foreach (object attribute in attributesAfter)
                    {
                        if (attribute is Variant rawValue)
                        {
                            attributesArray.Add(rawValue);
                        }
                        else
                        {
                            attributesArray.Add(Variant.Null);
                        }
                    }

                    uniqueNodesServiceAttributesCache[nodeHandleAfter.NodeId] = [.. attributesArray];
                }
                return new ValueTask<NodeMetadata>(nodeMetadata);
            }

            return new ValueTask<NodeMetadata>((NodeMetadata)null);
        }

        /// <inheritdoc/>
        public ValueTask HistoryReadAsync(
            OperationContext context,
            HistoryReadDetails details,
            TimestampsToReturn timestampsToReturn,
            bool releaseContinuationPoints,
            IList<HistoryReadValueId> nodesToRead,
            IList<HistoryReadResult> results,
            IList<ServiceResult> errors,
            CancellationToken cancellationToken = default)
        {
            if (SyncNodeManager is IHistoryReadAsyncNodeManager asyncNodeManager)
            {
                return asyncNodeManager.HistoryReadAsync(context,
                                                         details,
                                                         timestampsToReturn,
                                                         releaseContinuationPoints,
                                                         nodesToRead,
                                                         results,
                                                         errors,
                                                         cancellationToken);
            }

            SyncNodeManager.HistoryRead(context, details, timestampsToReturn, releaseContinuationPoints, nodesToRead, results, errors);

            // Return a completed ValueTask since the underlying call is synchronous.
            return default;
        }

        /// <inheritdoc/>
        public ValueTask HistoryUpdateAsync(
            OperationContext context,
            Type detailsType,
            IList<HistoryUpdateDetails> nodesToUpdate,
            IList<HistoryUpdateResult> results,
            IList<ServiceResult> errors,
            CancellationToken cancellationToken = default)
        {
            if (SyncNodeManager is IHistoryUpdateAsyncNodeManager asyncNodeManager)
            {
                return asyncNodeManager.HistoryUpdateAsync(context,
                                                           detailsType,
                                                           nodesToUpdate,
                                                           results,
                                                           errors,
                                                           cancellationToken);
            }

            SyncNodeManager.HistoryUpdate(context, detailsType, nodesToUpdate, results, errors);

            // Return a completed ValueTask since the underlying call is synchronous.
            return default;
        }

        /// <inheritdoc/>
        public ValueTask<bool> IsNodeInViewAsync(
            OperationContext context,
            NodeId viewId,
            object nodeHandle,
            CancellationToken cancellationToken = default)
        {
            if (SyncNodeManager is IAsyncNodeManager asyncNodeManager)
            {
                return asyncNodeManager.IsNodeInViewAsync(context, viewId, nodeHandle, cancellationToken);
            }

            if (SyncNodeManager is INodeManager2 nodeManager2)
            {
                bool result = nodeManager2.IsNodeInView(context, viewId, nodeHandle);
                return new ValueTask<bool>(result);
            }

            throw new NotImplementedException();
        }

        /// <inheritdoc/>
        public ValueTask ModifyMonitoredItemsAsync(OperationContext context,
                                                   TimestampsToReturn timestampsToReturn,
                                                   IList<IMonitoredItem> monitoredItems,
                                                   IList<MonitoredItemModifyRequest> itemsToModify,
                                                   IList<ServiceResult> errors,
                                                   IList<MonitoringFilterResult> filterErrors,
                                                   CancellationToken cancellationToken = default)
        {
            if (SyncNodeManager is IModifyMonitoredItemsAsyncNodeManager asyncNodeManager)
            {
                return asyncNodeManager.ModifyMonitoredItemsAsync(
                    context,
                    timestampsToReturn,
                    monitoredItems,
                    itemsToModify,
                    errors,
                    filterErrors,
                    cancellationToken);
            }

            SyncNodeManager.ModifyMonitoredItems(context, timestampsToReturn, monitoredItems, itemsToModify, errors, filterErrors);

            // Return a completed ValueTask since the underlying call is synchronous.
            return default;
        }

        /// <inheritdoc/>
        public ValueTask ReadAsync(OperationContext context,
                                   double maxAge,
                                   IList<ReadValueId> nodesToRead,
                                   IList<DataValue> values,
                                   IList<ServiceResult> errors,
                                   CancellationToken cancellationToken = default)
        {
            if (SyncNodeManager is IReadAsyncNodeManager asyncNodeManager)
            {
                return asyncNodeManager.ReadAsync(context, maxAge, nodesToRead, values, errors, cancellationToken);
            }

            SyncNodeManager.Read(context, maxAge, nodesToRead, values, errors);

            // Return a completed ValueTask since the underlying call is synchronous.
            return default;
        }

        /// <inheritdoc/>
        public ValueTask RestoreMonitoredItemsAsync(IList<IStoredMonitoredItem> itemsToRestore,
                                                    IList<IMonitoredItem> monitoredItems,
                                                    IUserIdentity savedOwnerIdentity,
                                                    CancellationToken cancellationToken = default)
        {
            if (SyncNodeManager is IAsyncNodeManager asyncNodeManager)
            {
                return asyncNodeManager.RestoreMonitoredItemsAsync(itemsToRestore, monitoredItems, savedOwnerIdentity, cancellationToken);
            }

            SyncNodeManager.RestoreMonitoredItems(itemsToRestore, monitoredItems, savedOwnerIdentity);

            // Return a completed ValueTask since the underlying call is synchronous.
            return default;
        }

        /// <inheritdoc/>
        public ValueTask SessionClosingAsync(
            OperationContext context,
            NodeId sessionId,
            bool deleteSubscriptions,
            CancellationToken cancellationToken = default)
        {
            if (SyncNodeManager is IAsyncNodeManager asyncNodeManager)
            {
                return asyncNodeManager.SessionClosingAsync(context, sessionId, deleteSubscriptions, cancellationToken);
            }

            if (SyncNodeManager is INodeManager2 nodeManager2)
            {
                nodeManager2.SessionClosing(context, sessionId, deleteSubscriptions);
            }

            // Return a completed ValueTask since the underlying call is synchronous.
            return default;
        }

        /// <inheritdoc/>
        public ValueTask SetMonitoringModeAsync(OperationContext context,
                                                MonitoringMode monitoringMode,
                                                IList<IMonitoredItem> monitoredItems,
                                                IList<bool> processedItems,
                                                IList<ServiceResult> errors,
                                                CancellationToken cancellationToken = default)
        {
            if (SyncNodeManager is ISetMonitoringModeAsyncNodeManager asyncNodeManager)
            {
                return asyncNodeManager.SetMonitoringModeAsync(context, monitoringMode, monitoredItems, processedItems, errors, cancellationToken);
            }

            SyncNodeManager.SetMonitoringMode(context, monitoringMode, monitoredItems, processedItems, errors);

            // Return a completed ValueTask since the underlying call is synchronous.
            return default;
        }

        /// <inheritdoc/>
        public ValueTask<ServiceResult> SubscribeToAllEventsAsync(OperationContext context,
                                                                  uint subscriptionId,
                                                                  IEventMonitoredItem monitoredItem,
                                                                  bool unsubscribe,
                                                                  CancellationToken cancellationToken = default)
        {
            if (SyncNodeManager is IAsyncNodeManager asyncNodeManager)
            {
                return asyncNodeManager.SubscribeToAllEventsAsync(context, subscriptionId, monitoredItem, unsubscribe, cancellationToken);
            }

            ServiceResult result = SyncNodeManager.SubscribeToAllEvents(context, subscriptionId, monitoredItem, unsubscribe);

            // Return a completed ValueTask since the underlying call is synchronous.
            return new ValueTask<ServiceResult>(result);
        }

        /// <inheritdoc/>
        public ValueTask<ServiceResult> SubscribeToEventsAsync(OperationContext context,
                                                               object sourceId,
                                                               uint subscriptionId,
                                                               IEventMonitoredItem monitoredItem,
                                                               bool unsubscribe,
                                                               CancellationToken cancellationToken = default)
        {
            if (SyncNodeManager is IAsyncNodeManager asyncNodeManager)
            {
                return asyncNodeManager.SubscribeToEventsAsync(context, sourceId, subscriptionId, monitoredItem, unsubscribe, cancellationToken);
            }

            ServiceResult result = SyncNodeManager.SubscribeToEvents(context, sourceId, subscriptionId, monitoredItem, unsubscribe);

            // Return a completed ValueTask since the underlying call is synchronous.
            return new ValueTask<ServiceResult>(result);
        }

        /// <inheritdoc/>
        public ValueTask TransferMonitoredItemsAsync(OperationContext context,
                                                     bool sendInitialValues,
                                                     IList<IMonitoredItem> monitoredItems,
                                                     IList<bool> processedItems,
                                                     IList<ServiceResult> errors,
                                                     CancellationToken cancellationToken = default)
        {
            if (SyncNodeManager is ITransferMonitoredItemsAsyncNodeManager asyncNodeManager)
            {
                return asyncNodeManager.TransferMonitoredItemsAsync(context, sendInitialValues, monitoredItems, processedItems, errors, cancellationToken);
            }

            SyncNodeManager.TransferMonitoredItems(context, sendInitialValues, monitoredItems, processedItems, errors);

            // Return a completed ValueTask since the underlying call is synchronous.
            return default;
        }

        /// <inheritdoc/>
        public ValueTask TranslateBrowsePathAsync(
            OperationContext context,
            object sourceHandle,
            RelativePathElement relativePath,
            IList<ExpandedNodeId> targetIds,
            IList<NodeId> unresolvedTargetIds,
            CancellationToken cancellationToken = default)
        {
            if (SyncNodeManager is ITranslateBrowsePathAsyncNodeManager asyncNodeManager)
            {
                return asyncNodeManager.TranslateBrowsePathAsync(context, sourceHandle, relativePath, targetIds, unresolvedTargetIds, cancellationToken);
            }

            SyncNodeManager.TranslateBrowsePath(context, sourceHandle, relativePath, targetIds, unresolvedTargetIds);

            // Return a completed ValueTask since the underlying call is synchronous.
            return default;
        }

        /// <inheritdoc/>
        public ValueTask WriteAsync(
            OperationContext context,
            IList<WriteValue> nodesToWrite,
            IList<ServiceResult> errors,
            CancellationToken cancellationToken = default)
        {
            if (SyncNodeManager is IWriteAsyncNodeManager asyncNodeManager)
            {
                return asyncNodeManager.WriteAsync(context, nodesToWrite, errors, cancellationToken);
            }

            SyncNodeManager.Write(context, nodesToWrite, errors);

            // Return a completed ValueTask since the underlying call is synchronous.
            return default;
        }

        /// <inheritdoc/>
        public ValueTask<ServiceResult> ValidateEventRolePermissionsAsync(
            IEventMonitoredItem monitoredItem,
            IFilterTarget filterTarget,
            CancellationToken cancellationToken = default)
        {
            if (SyncNodeManager is IAsyncNodeManager asyncNodeManager)
            {
                return asyncNodeManager.ValidateEventRolePermissionsAsync(monitoredItem, filterTarget, cancellationToken);
            }

            if (SyncNodeManager is INodeManager3 nodeManager2)
            {
                return new ValueTask<ServiceResult>(nodeManager2.ValidateEventRolePermissions(monitoredItem, filterTarget));
            }

            return new ValueTask<ServiceResult>(ServiceResult.Good);
        }

        /// <inheritdoc/>
        public ValueTask<ServiceResult> ValidateRolePermissionsAsync(
            OperationContext operationContext,
            NodeId nodeId,
            PermissionType requestedPermission,
            CancellationToken cancellationToken = default)
        {
            if (SyncNodeManager is IAsyncNodeManager asyncNodeManager)
            {
                return asyncNodeManager.ValidateRolePermissionsAsync(operationContext, nodeId, requestedPermission, cancellationToken);
            }

            if (SyncNodeManager is INodeManager3 nodeManager2)
            {
                return new ValueTask<ServiceResult>(nodeManager2.ValidateRolePermissions(operationContext, nodeId, requestedPermission));
            }

            return new ValueTask<ServiceResult>(ServiceResult.Good);
        }

        /// <summary>
        /// Frees any unmanaged resources.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// An overrideable version of the Dispose.
        /// </summary>
        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                Utils.SilentDispose(SyncNodeManager);
            }
        }
    }
}
