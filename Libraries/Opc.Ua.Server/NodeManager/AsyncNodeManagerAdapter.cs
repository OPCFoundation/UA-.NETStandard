using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
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
        public ValueTask CallAsync(
            OperationContext context,
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
    }
}
