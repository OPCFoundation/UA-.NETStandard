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
    /// Source-compatible entry points that forward to the canonical context-free transaction members.
    /// Request and binding callbacks do not require explicit contexts to release global binding admission.
    /// </summary>
    public static class NodeManagerBatchLifecycleExtensions
    {
        /// <summary>
        /// Legacy context-free preparation helper.
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

            return lifecycle.PrepareAsync(changes, cancellationToken);
        }

        /// <summary>
        /// Reserves an invocation without an explicit request context.
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

            return capture.BeginAsync(cancellationToken);
        }
    }
}
