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
        private readonly INodeManager m_nodeManager;

        /// <summary>
        /// Initializes a new instance of the <see cref="AsyncNodeManagerAdapter"/> class.
        /// </summary>
        /// <param name="nodeManager">The synchronous node manager to wrap.</param>
        /// <exception cref="ArgumentNullException"><paramref name="nodeManager"/> is <c>null</c>.</exception>
        public AsyncNodeManagerAdapter(INodeManager nodeManager)
        {
            m_nodeManager = nodeManager ?? throw new ArgumentNullException(nameof(nodeManager));
        }

        /// <inheritdoc/>
        public ValueTask CallAsync(OperationContext context,
                                   IList<CallMethodRequest> methodsToCall,
                                   IList<CallMethodResult> results,
                                   IList<ServiceResult> errors,
                                   CancellationToken cancellationToken = default)
        {
            if (m_nodeManager is ICallAsyncNodeManager asyncNodeManager)
            {
                return asyncNodeManager.CallAsync(context, methodsToCall, results, errors, cancellationToken);
            }

            m_nodeManager.Call(context, methodsToCall, results, errors);

            // Return a completed ValueTask since the underlying call is synchronous.
            return default;
        }

        /// <inheritdoc/>
        public ValueTask ConditionRefreshAsync(OperationContext context,
                                               IList<IEventMonitoredItem> monitoredItems,
                                               CancellationToken cancellationToken = default)
        {
            if (m_nodeManager is IConditionRefreshAsyncNodeManager asyncNodeManager)
            {
                return asyncNodeManager.ConditionRefreshAsync(context, monitoredItems, cancellationToken);
            }

            m_nodeManager.ConditionRefresh(context, monitoredItems);

            // Return a completed ValueTask since the underlying call is synchronous.
            return default;
        }

        /// <inheritdoc/>
        public ValueTask HistoryReadAsync(OperationContext context,
                                          HistoryReadDetails details,
                                          TimestampsToReturn timestampsToReturn,
                                          bool releaseContinuationPoints,
                                          IList<HistoryReadValueId> nodesToRead,
                                          IList<HistoryReadResult> results,
                                          IList<ServiceResult> errors,
                                          CancellationToken cancellationToken = default)
        {
            if (m_nodeManager is IHistoryReadAsyncNodeManager asyncNodeManager)
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

            m_nodeManager.HistoryRead(context, details, timestampsToReturn, releaseContinuationPoints, nodesToRead, results, errors);

            // Return a completed ValueTask since the underlying call is synchronous.
            return default;
        }

        /// <inheritdoc/>
        public ValueTask HistoryUpdateAsync(OperationContext context,
                                            Type detailsType,
                                            IList<HistoryUpdateDetails> nodesToUpdate,
                                            IList<HistoryUpdateResult> results,
                                            IList<ServiceResult> errors,
                                            CancellationToken cancellationToken = default)
        {
            if (m_nodeManager is IHistoryUpdateAsyncNodeManager asyncNodeManager)
            {
                return asyncNodeManager.HistoryUpdateAsync(context,
                                                           detailsType,
                                                           nodesToUpdate,
                                                           results,
                                                           errors,
                                                           cancellationToken);
            }

            m_nodeManager.HistoryUpdate(context, detailsType, nodesToUpdate, results, errors);

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
            if (m_nodeManager is IReadAsyncNodeManager asyncNodeManager)
            {
                return asyncNodeManager.ReadAsync(context, maxAge, nodesToRead, values, errors, cancellationToken);
            }

            m_nodeManager.Read(context, maxAge, nodesToRead, values, errors);

            // Return a completed ValueTask since the underlying call is synchronous.
            return default;
        }

        /// <inheritdoc/>
        public ValueTask TranslateBrowsePathAsync(OperationContext context,
                                                  object sourceHandle,
                                                  RelativePathElement relativePath,
                                                  IList<ExpandedNodeId> targetIds,
                                                  IList<NodeId> unresolvedTargetIds,
                                                  CancellationToken cancellationToken = default)
        {
            if (m_nodeManager is ITranslateBrowsePathAsyncNodeManager asyncNodeManager)
            {
                return asyncNodeManager.TranslateBrowsePathAsync(context, sourceHandle, relativePath, targetIds, unresolvedTargetIds, cancellationToken);
            }

            m_nodeManager.TranslateBrowsePath(context, sourceHandle, relativePath, targetIds, unresolvedTargetIds);

            // Return a completed ValueTask since the underlying call is synchronous.
            return default;
        }

        /// <inheritdoc/>
        public ValueTask WriteAsync(OperationContext context,
                                    IList<WriteValue> nodesToWrite,
                                    IList<ServiceResult> errors,
                                    CancellationToken cancellationToken = default)
        {
            if (m_nodeManager is IWriteAsyncNodeManager asyncNodeManager)
            {
                return asyncNodeManager.WriteAsync(context, nodesToWrite, errors, cancellationToken);
            }

            m_nodeManager.Write(context, nodesToWrite, errors);

            // Return a completed ValueTask since the underlying call is synchronous.
            return default;
        }
    }
}
