using System;
using System.Collections.Generic;
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
    public class AsyncNodeManagerAdapter : IAsyncNodeManager
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="AsyncNodeManagerAdapter"/> class.
        /// </summary>
        /// <param name="nodeManager">The synchronous node manager to wrap.</param>
        /// <exception cref="ArgumentNullException"><paramref name="nodeManager"/> is <c>null</c>.</exception>
        public AsyncNodeManagerAdapter(INodeManager nodeManager)
        {
            NodeManager = nodeManager ?? throw new ArgumentNullException(nameof(nodeManager));
        }

        /// <inheritdoc/>
        public IEnumerable<string> NamespaceUris => NodeManager.NamespaceUris;

        /// <summary>
        ///  The node manager being adapted.
        /// </summary>
        public INodeManager NodeManager { get; }

        /// <inheritdoc/>
        public ValueTask AddReferencesAsync(
            IDictionary<NodeId, IList<IReference>> references,
            CancellationToken cancellationToken = default)
        {
            if (NodeManager is IAsyncNodeManager asyncNodeManager)
            {
                return asyncNodeManager.AddReferencesAsync(references, cancellationToken);
            }

            NodeManager.AddReferences(references);

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
            if (NodeManager is IBrowseAsyncNodeManager asyncNodeManager)
            {
                return asyncNodeManager.BrowseAsync(context, continuationPoint, references, cancellationToken);
            }

            NodeManager.Browse(context, ref continuationPoint, references);

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
            if (NodeManager is ICallAsyncNodeManager asyncNodeManager)
            {
                return asyncNodeManager.CallAsync(context, methodsToCall, results, errors, cancellationToken);
            }

            NodeManager.Call(context, methodsToCall, results, errors);

            // Return a completed ValueTask since the underlying call is synchronous.
            return default;
        }

        /// <inheritdoc/>
        public ValueTask ConditionRefreshAsync(
            OperationContext context,
            IList<IEventMonitoredItem> monitoredItems,
            CancellationToken cancellationToken = default)
        {
            if (NodeManager is IConditionRefreshAsyncNodeManager asyncNodeManager)
            {
                return asyncNodeManager.ConditionRefreshAsync(context, monitoredItems, cancellationToken);
            }

            NodeManager.ConditionRefresh(context, monitoredItems);

            // Return a completed ValueTask since the underlying call is synchronous.
            return default;
        }

        /// <inheritdoc/>
        public ValueTask CreateAddressSpaceAsync(
            IDictionary<NodeId, IList<IReference>> externalReferences,
            CancellationToken cancellationToken = default)
        {
            if (NodeManager is IAsyncNodeManager asyncNodeManager)
            {
                return asyncNodeManager.CreateAddressSpaceAsync(externalReferences, cancellationToken);
            }

            NodeManager.CreateAddressSpace(externalReferences);

            // Return a completed ValueTask since the underlying call is synchronous.
            return default;
        }

        /// <inheritdoc/>
        public ValueTask DeleteAddressSpaceAsync(CancellationToken cancellationToken = default)
        {
            if (NodeManager is IAsyncNodeManager asyncNodeManager)
            {
                return asyncNodeManager.DeleteAddressSpaceAsync(cancellationToken);
            }

            NodeManager.DeleteAddressSpace();

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
            if (NodeManager is IAsyncNodeManager asyncNodeManager)
            {
                return asyncNodeManager.DeleteReferenceAsync(sourceHandle,
                                                             referenceTypeId,
                                                             isInverse,
                                                             targetId,
                                                             deleteBidirectional,
                                                             cancellationToken);
            }

            ServiceResult result = NodeManager.DeleteReference(sourceHandle,
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
            if (NodeManager is IAsyncNodeManager asyncNodeManager)
            {
                return asyncNodeManager.GetManagerHandleAsync(nodeId, cancellationToken);
            }

            object handle = NodeManager.GetManagerHandle(nodeId);

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
            if (NodeManager is IAsyncNodeManager asyncNodeManager)
            {
                return asyncNodeManager.GetNodeMetadataAsync(context, targetHandle, resultMask, cancellationToken);
            }

            NodeMetadata nodeMetadata = NodeManager.GetNodeMetadata(context, targetHandle, resultMask);

            // Return a completed ValueTask since the underlying call is synchronous.
            return new ValueTask<NodeMetadata>(nodeMetadata);
        }

        /// <inheritdoc/>
        public ValueTask<NodeMetadata> GetPermissionMetadataAsync(
            OperationContext context,
            object targetHandle,
            BrowseResultMask resultMask,
            Dictionary<NodeId, List<object>> uniqueNodesServiceAttributesCache,
            bool permissionsOnly,
            CancellationToken cancellationToken = default)
        {
            if (NodeManager is IAsyncNodeManager asyncNodeManager)
            {
                return asyncNodeManager.GetPermissionMetadataAsync(
                    context,
                    targetHandle,
                    resultMask,
                    uniqueNodesServiceAttributesCache,
                    permissionsOnly,
                    cancellationToken);
            }
            if (NodeManager is INodeManager2 nodeManager2)
            {
                NodeMetadata nodeMetadata = nodeManager2.GetPermissionMetadata(
                    context,
                    targetHandle,
                    resultMask,
                    uniqueNodesServiceAttributesCache,
                    permissionsOnly);
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
            if (NodeManager is IHistoryReadAsyncNodeManager asyncNodeManager)
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

            NodeManager.HistoryRead(context, details, timestampsToReturn, releaseContinuationPoints, nodesToRead, results, errors);

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
            if (NodeManager is IHistoryUpdateAsyncNodeManager asyncNodeManager)
            {
                return asyncNodeManager.HistoryUpdateAsync(context,
                                                           detailsType,
                                                           nodesToUpdate,
                                                           results,
                                                           errors,
                                                           cancellationToken);
            }

            NodeManager.HistoryUpdate(context, detailsType, nodesToUpdate, results, errors);

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
            if (NodeManager is IAsyncNodeManager asyncNodeManager)
            {
                return asyncNodeManager.IsNodeInViewAsync(context, viewId, nodeHandle, cancellationToken);
            }

            if (NodeManager is INodeManager2 nodeManager2)
            {
                bool result = nodeManager2.IsNodeInView(context, viewId, nodeHandle);
                return new ValueTask<bool>(result);
            }

            throw new NotImplementedException();
        }

        /// <inheritdoc/>
        public ValueTask ReadAsync(
            OperationContext context,
            double maxAge,
            IList<ReadValueId> nodesToRead,
            IList<DataValue> values,
            IList<ServiceResult> errors,
            CancellationToken cancellationToken = default)
        {
            if (NodeManager is IReadAsyncNodeManager asyncNodeManager)
            {
                return asyncNodeManager.ReadAsync(context, maxAge, nodesToRead, values, errors, cancellationToken);
            }

            NodeManager.Read(context, maxAge, nodesToRead, values, errors);

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
            if (NodeManager is IAsyncNodeManager asyncNodeManager)
            {
                return asyncNodeManager.SessionClosingAsync(context, sessionId, deleteSubscriptions, cancellationToken);
            }

            if (NodeManager is INodeManager2 nodeManager2)
            {
                nodeManager2.SessionClosing(context, sessionId, deleteSubscriptions);
            }

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
            if (NodeManager is ITranslateBrowsePathAsyncNodeManager asyncNodeManager)
            {
                return asyncNodeManager.TranslateBrowsePathAsync(context, sourceHandle, relativePath, targetIds, unresolvedTargetIds, cancellationToken);
            }

            NodeManager.TranslateBrowsePath(context, sourceHandle, relativePath, targetIds, unresolvedTargetIds);

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
            if (NodeManager is IWriteAsyncNodeManager asyncNodeManager)
            {
                return asyncNodeManager.WriteAsync(context, nodesToWrite, errors, cancellationToken);
            }

            NodeManager.Write(context, nodesToWrite, errors);

            // Return a completed ValueTask since the underlying call is synchronous.
            return default;
        }
    }
}
