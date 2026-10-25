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
    /// Compatibility marker for lifecycles that support invocation-wide publication isolation.
    /// New callers should use the transaction members on <see cref="INodeManagerLifecycle"/>.
    /// </summary>
    public interface INodeManagerPublicationLifecycle : INodeManagerBatchLifecycle
    {
    }

    /// <summary>
    /// Owner-bound revision evidence captured before application preparation.
    /// </summary>
    public interface INodeManagerPublicationCapture
    {
        /// <summary>
        /// Reserves one invocation without requiring an explicit request context.
        /// A stale capture returns an invocation whose <see cref="INodeManagerPublication.IsCurrent"/>
        /// is false and cannot prepare units.
        /// </summary>
        /// <param name="cancellationToken">The token to cancel the operation.</param>
        /// <returns>The invocation that retains publication admission until disposed.</returns>
        ValueTask<INodeManagerPublication> BeginAsync(
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Reserves one invocation. Conflicting lifecycle work is rejected before effects.
        /// A stale capture returns an invocation whose IsCurrent is false and cannot prepare units.
        /// </summary>
        /// <param name="callerContext">Legacy explicit request context. New callers should use the overload without
        /// this parameter; request lifetime is discovered by the lifecycle implementation.</param>
        /// <param name="cancellationToken">The token to cancel the operation.</param>
        /// <returns>The invocation that retains publication admission until disposed.</returns>
        ValueTask<INodeManagerPublication> BeginAsync(
            IOperationContext? callerContext,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Retains publication admission across all units of one invocation.
    /// </summary>
    public interface INodeManagerPublication : IAsyncDisposable
    {
        /// <summary>
        /// Gets whether all captured revisions matched when publication admission was reserved.
        /// </summary>
        bool IsCurrent { get; }

        /// <summary>
        /// Prepares a unit on the existing lifecycle owner. Dispose each unit before the invocation.
        /// Ordinary lifecycle mutations, including mutations from callbacks, must retry after the invocation.
        /// </summary>
        /// <param name="changes">The additions, replacements and retirements to prepare.</param>
        /// <param name="cancellationToken">The token to cancel the operation.</param>
        /// <returns>The unpublished prepared unit.</returns>
        ValueTask<IPreparedNodeManagerBatch> PrepareAsync(
            ArrayOf<NodeManagerBatchChange> changes, CancellationToken cancellationToken = default);

        /// <summary>
        /// Prepares immutable read state without replacing any NodeManager.
        /// At least one image is required; publication uses the same decision and routing switch as other units.
        /// </summary>
        /// <param name="images">The read images to publish.</param>
        /// <param name="cancellationToken">The token to cancel the operation.</param>
        /// <returns>The unpublished prepared unit carrying the images.</returns>
        ValueTask<IPreparedNodeManagerBatch> PrepareReadImagesAsync(
            ArrayOf<INodeManagerReadImage> images, CancellationToken cancellationToken = default);

        /// <summary>
        /// Prepares and inspects a private candidate, then disposes it without publishing.
        /// The candidate is valid only during the inspection callback and cannot be committed.
        /// </summary>
        /// <param name="changes">The additions, replacements and retirements to validate.</param>
        /// <param name="inspectAsync">The callback inspecting the private candidate.</param>
        /// <param name="cancellationToken">The token to cancel the operation.</param>
        /// <returns>A task that completes when the candidate has been inspected and disposed.</returns>
        ValueTask ValidateAsync(
            ArrayOf<NodeManagerBatchChange> changes,
            Func<IPreparedNodeManagerBatch, CancellationToken, ValueTask> inspectAsync,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Optional validation of a publication candidate without allocating serving namespace mappings.
    /// </summary>
    public interface INodeManagerValidationPublication : INodeManagerPublication
    {
    }
}
