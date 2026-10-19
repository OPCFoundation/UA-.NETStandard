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

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Optional lifecycle capability for privately preparing a complete publication unit.
    /// </summary>
    public interface INodeManagerBatchLifecycle : INodeManagerLifecycle
    {
        /// <summary>
        /// Prepares the requested additions, replacements and retirements without publishing them.
        /// The change sequence is copied before the operation can yield.
        /// The returned owner must be committed or asynchronously disposed.
        /// </summary>
        /// <param name="changes">The additions, replacements and retirements to prepare.</param>
        /// <param name="callerContext">The request the caller is running under, or null for an ordinary external
        /// caller. Pass the context when preparing from a binding admission callback.</param>
        /// <param name="cancellationToken">The token to cancel the operation.</param>
        /// <returns>The unpublished prepared unit, which owns all resources acquired while preparing.</returns>
        ValueTask<IPreparedNodeManagerBatch> PrepareAsync(
            ArrayOf<NodeManagerBatchChange> changes,
            IOperationContext? callerContext,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Source-compatible entry points for ordinary external callers of the batch lifecycle.
    /// They forward to the canonical overload with a null caller context and hold no ambient state.
    /// Code running inside an OPC UA request or binding callback must use the explicit context overload.
    /// </summary>
    public static class NodeManagerBatchLifecycleExtensions
    {
        /// <summary>
        /// Prepares a unit for an ordinary external caller that is not running under a request.
        /// </summary>
        /// <param name="lifecycle">The batch lifecycle.</param>
        /// <param name="changes">The additions, replacements and retirements to prepare.</param>
        /// <param name="cancellationToken">The token to cancel the operation.</param>
        /// <returns>The unpublished prepared unit.</returns>
        public static ValueTask<IPreparedNodeManagerBatch> PrepareAsync(
            this INodeManagerBatchLifecycle lifecycle,
            ArrayOf<NodeManagerBatchChange> changes,
            CancellationToken cancellationToken = default)
        {
            if (lifecycle == null)
            {
                throw new ArgumentNullException(nameof(lifecycle));
            }

            return lifecycle.PrepareAsync(changes, null, cancellationToken);
        }

        /// <summary>
        /// Reserves an invocation for an ordinary external caller that is not running under a request.
        /// </summary>
        /// <param name="capture">The publication capture.</param>
        /// <param name="cancellationToken">The token to cancel the operation.</param>
        /// <returns>The invocation that retains publication admission until disposed.</returns>
        public static ValueTask<INodeManagerPublication> BeginAsync(
            this INodeManagerPublicationCapture capture,
            CancellationToken cancellationToken = default)
        {
            if (capture == null)
            {
                throw new ArgumentNullException(nameof(capture));
            }

            return capture.BeginAsync(null, cancellationToken);
        }
    }
}
