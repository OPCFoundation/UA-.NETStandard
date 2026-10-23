/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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

using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Server.Historian;

namespace Opc.Ua.Server
{
    public partial class MasterNodeManager
    {
        /// <summary>
        /// Sets the lifecycle that decides which history sources need captured continuation ownership.
        /// </summary>
        /// <param name="lifecycle">The node manager lifecycle.</param>
        internal void SetHistoryContinuationLifecycle(NodeManagerLifecycle lifecycle)
        {
            Volatile.Write(ref m_historyContinuationLifecycle, lifecycle);
        }

        /// <summary>
        /// Determines whether continuation points of a node manager must retain the captured generation.
        /// </summary>
        /// <param name="manager">The node manager serving the history read.</param>
        /// <returns>True if the lifecycle owns the node manager as a history source.</returns>
        internal bool RequiresHistoryContinuationOwnership(IAsyncNodeManager manager)
        {
            return Volatile.Read(ref m_historyContinuationLifecycle)?.OwnsHistorySource(manager) == true;
        }

        /// <summary>
        /// Captures the node managers a history continuation depends on.
        /// </summary>
        /// <param name="context">The operation whose routing view owns the read.</param>
        /// <param name="state">The continuation state to capture.</param>
        /// <param name="cancellationToken">The token used to cancel the operation.</param>
        /// <returns>Good if captured or not required; otherwise the reason it cannot be captured.</returns>
        internal ValueTask<ServiceResult> CaptureHistoryContinuationAsync(
            OperationContext context,
            HistorianContinuationState state,
            CancellationToken cancellationToken)
        {
            return m_serviceDispatch.CaptureHistoryContinuationAsync(context, state, cancellationToken);
        }

        private NodeManagerLifecycle? m_historyContinuationLifecycle;
    }
}
