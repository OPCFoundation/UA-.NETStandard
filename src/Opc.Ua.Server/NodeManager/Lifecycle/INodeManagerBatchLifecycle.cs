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
    /// Compatibility marker for lifecycles that expose prepared transactions.
    /// New callers should use the transaction members on <see cref="INodeManagerLifecycle"/>.
    /// </summary>
    public interface INodeManagerBatchLifecycle : INodeManagerLifecycle
    {
    }

    /// <summary>
    /// Source-compatible entry points for the one-release context migration window.
    /// Legacy contexts are ignored; request callbacks use the canonical transaction path.
    /// </summary>
    public static class NodeManagerBatchLifecycleExtensions
    {
        /// <summary>
        /// Preserves static calls to the former batch preparation helper.
        /// </summary>
        /// <param name="lifecycle">The batch lifecycle.</param>
        /// <param name="changes">The changes to prepare.</param>
        /// <param name="cancellationToken">The token to cancel preparation.</param>
        /// <returns>The unpublished prepared owner.</returns>
        [Obsolete("Use the PrepareAsync member on INodeManagerLifecycle.")]
        public static ValueTask<IPreparedNodeManagerBatch> PrepareAsync(
            this INodeManagerBatchLifecycle lifecycle,
            ArrayOf<NodeManagerBatchChange> changes,
            CancellationToken cancellationToken = default)
        {
            if (lifecycle is null)
            {
                throw new ArgumentNullException(nameof(lifecycle));
            }
            return lifecycle.PrepareAsync(changes, cancellationToken);
        }

        /// <summary>
        /// Prepares a transaction, ignoring the legacy request context.
        /// </summary>
        /// <param name="lifecycle">The lifecycle.</param>
        /// <param name="changes">The additions, replacements and retirements to prepare.</param>
        /// <param name="callerContext">The unused legacy request context.</param>
        /// <param name="cancellationToken">The token to cancel the operation.</param>
        /// <returns>The unpublished prepared unit.</returns>
        [Obsolete("Use PrepareAsync(changes, cancellationToken). The request context is no longer required.")]
        public static ValueTask<IPreparedNodeManagerBatch> PrepareAsync(
            this INodeManagerLifecycle lifecycle,
            ArrayOf<NodeManagerBatchChange> changes,
            IOperationContext? callerContext,
            CancellationToken cancellationToken = default)
        {
            if (lifecycle == null)
            {
                throw new ArgumentNullException(nameof(lifecycle));
            }

            return lifecycle.PrepareAsync(changes, cancellationToken);
        }

        /// <summary>
        /// Reserves an invocation, ignoring the legacy request context.
        /// </summary>
        /// <param name="capture">The publication capture.</param>
        /// <param name="callerContext">The unused legacy request context.</param>
        /// <param name="cancellationToken">The token to cancel the operation.</param>
        /// <returns>The invocation that retains publication admission until disposed.</returns>
        [Obsolete("Use BeginAsync(cancellationToken). The request context is no longer required.")]
        public static ValueTask<INodeManagerPublication> BeginAsync(
            this INodeManagerPublicationCapture capture,
            IOperationContext? callerContext,
            CancellationToken cancellationToken = default)
        {
            if (capture == null)
            {
                throw new ArgumentNullException(nameof(capture));
            }

            return capture.BeginAsync(cancellationToken);
        }

        /// <summary>
        /// Preserves static calls to the former context-free admission helper.
        /// </summary>
        /// <param name="capture">The publication capture.</param>
        /// <param name="cancellationToken">The token to cancel admission.</param>
        /// <returns>The invocation retaining publication admission.</returns>
        [Obsolete("Use the BeginAsync member on INodeManagerPublicationCapture.")]
        public static ValueTask<INodeManagerPublication> BeginAsync(
            this INodeManagerPublicationCapture capture,
            CancellationToken cancellationToken = default)
        {
            if (capture is null)
            {
                throw new ArgumentNullException(nameof(capture));
            }
            return capture.BeginAsync(cancellationToken);
        }
    }
}
