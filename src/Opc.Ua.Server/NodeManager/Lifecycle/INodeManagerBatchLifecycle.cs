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
        ValueTask<IPreparedNodeManagerBatch> PrepareAsync(
            ArrayOf<NodeManagerBatchChange> changes,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Optional isolation of an invocation containing several prepared publication units.
    /// </summary>
    public interface INodeManagerPublicationLifecycle : INodeManagerBatchLifecycle
    {
        /// <summary>
        /// Gets whether the attached owner supports invocation-wide publication isolation.
        /// </summary>
        bool SupportsPublicationIsolation { get; }

        /// <summary>
        /// Captures the current routing, type and factory revisions without reserving publication.
        /// </summary>
        INodeManagerPublicationCapture CapturePublication();
    }

    /// <summary>
    /// Owner-bound revision evidence captured before application preparation.
    /// </summary>
    public interface INodeManagerPublicationCapture
    {
        /// <summary>
        /// Reserves one invocation. Conflicting lifecycle work is rejected before effects.
        /// A stale capture returns an invocation whose IsCurrent is false and cannot prepare units.
        /// </summary>
        ValueTask<INodeManagerPublication> BeginAsync(CancellationToken cancellationToken = default);
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
        ValueTask<IPreparedNodeManagerBatch> PrepareAsync(
            ArrayOf<NodeManagerBatchChange> changes, CancellationToken cancellationToken = default);

        /// <summary>
        /// Prepares immutable read state without replacing any NodeManager.
        /// At least one image is required; publication uses the same decision and routing switch as other units.
        /// </summary>
        ValueTask<IPreparedNodeManagerBatch> PrepareReadImagesAsync(
            ArrayOf<INodeManagerReadImage> images, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Optional validation of a publication candidate without allocating serving namespace mappings.
    /// </summary>
    public interface INodeManagerValidationPublication : INodeManagerPublication
    {
        /// <summary>
        /// Prepares and inspects a private candidate, then disposes it without publishing.
        /// The candidate is valid only during the inspection callback and cannot be committed.
        /// </summary>
        ValueTask ValidateAsync(
            ArrayOf<NodeManagerBatchChange> changes,
            Func<IPreparedNodeManagerBatch, CancellationToken, ValueTask> inspectAsync,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Immutable application state retained with the exact NodeManager routing image of a request.
    /// Implementations must remain readable after their owner is retired.
    /// </summary>
    public interface INodeManagerReadImage
    {
        /// <summary>
        /// Gets the exact NodeManager that owns this state.
        /// </summary>
        IAsyncNodeManager Owner { get; }
    }

    /// <summary>
    /// Provides application state from the request's captured routing image, or the live image outside a request.
    /// </summary>
    public interface INodeManagerReadImageSource
    {
        /// <summary>
        /// Gets the image for an exact owner, or null when no state has been published for that owner.
        /// </summary>
        INodeManagerReadImage? GetReadImage(IAsyncNodeManager owner);
    }

    /// <summary>
    /// Owns an unpublished unit and all resources acquired while preparing it.
    /// </summary>
    public interface IPreparedNodeManagerBatch : IAsyncDisposable
    {
        /// <summary>
        /// Gets the exact candidate registrations in addition/replacement order.
        /// They are not live until the commit publishes the unit.
        /// </summary>
        ArrayOf<NodeManagerRegistration> Registrations { get; }

        /// <summary>
        /// Gets whether this unit has crossed its irreversible publication decision.
        /// </summary>
        bool IsCommitted { get; }

        /// <summary>
        /// Binds immutable application state before commit. The sequence is copied and may be bound only once.
        /// Each image must belong to a distinct NodeManager in the resulting routing image.
        /// </summary>
        void BindReadImages(ArrayOf<INodeManagerReadImage> images);

        /// <summary>
        /// Rechecks ownership and the captured routing revision, invokes the durable decision once,
        /// then publishes one routing image.
        /// Returning from the decision means committed; its confirmed noncommit or indeterminate
        /// exceptions must escape it. A caller with a committed durability warning must retain that
        /// warning and return normally so publication completes. After the decision, cancellation
        /// cannot undo the unit; cleanup failures are returned with the committed registrations.
        /// Committed reconciliation releases operation ownership before this call completes,
        /// including when it returns a cleanup warning. Disposal is then optional and idempotent.
        /// An uncommitted unit must still be disposed after a failed commit attempt.
        /// </summary>
        /// <exception cref="InvalidOperationException">
        /// Routing changed after preparation; the durable decision is not invoked.
        /// </exception>
        ValueTask<NodeManagerBatchResult> CommitAsync(
            Func<CancellationToken, ValueTask> decideAsync,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Commits the same unit and publishes its already-prepared in-memory state immediately
        /// after the routing switch, before readiness and retirement reconciliation.
        /// The publication callback must perform no I/O or additional durable decision.
        /// A callback failure occurs after commit and cannot make the batch uncommitted.
        /// </summary>
        ValueTask<NodeManagerBatchResult> CommitAsync(
            Func<CancellationToken, ValueTask> decideAsync,
            Action publishCommittedState,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// One addition, replacement or retirement in a prepared publication unit.
    /// </summary>
    public sealed class NodeManagerBatchChange
    {
        private NodeManagerBatchChange(
            NodeManagerRegistration? current,
            IAsyncNodeManagerFactory? factory,
            bool immediate)
        {
            Current = current;
            Factory = factory;
            Immediate = immediate;
        }

        /// <summary>
        /// Gets the exact registration being replaced or retired, or null for an addition.
        /// </summary>
        public NodeManagerRegistration? Current { get; }

        /// <summary>
        /// Gets the candidate factory, or null for a retirement without replacement.
        /// </summary>
        public IAsyncNodeManagerFactory? Factory { get; }

        /// <summary>
        /// Gets whether old monitored sources are invalidated instead of gracefully retained.
        /// </summary>
        public bool Immediate { get; }

        /// <summary>
        /// Adds a new privately prepared owner.
        /// </summary>
        public static NodeManagerBatchChange Add(IAsyncNodeManagerFactory factory)
        {
            return new NodeManagerBatchChange(
                null, factory ?? throw new ArgumentNullException(nameof(factory)), false);
        }

        /// <summary>
        /// Replaces an exact registration while preserving its logical identity.
        /// </summary>
        public static NodeManagerBatchChange Replace(
            NodeManagerRegistration current,
            IAsyncNodeManagerFactory factory,
            bool immediate = false)
        {
            return new NodeManagerBatchChange(
                current ?? throw new ArgumentNullException(nameof(current)),
                factory ?? throw new ArgumentNullException(nameof(factory)),
                immediate);
        }

        /// <summary>
        /// Retires an exact registration in the same switch as the other changes.
        /// </summary>
        public static NodeManagerBatchChange Remove(
            NodeManagerRegistration current,
            bool immediate = false)
        {
            return new NodeManagerBatchChange(
                current ?? throw new ArgumentNullException(nameof(current)), null, immediate);
        }
    }

    /// <summary>
    /// The committed registrations and any post-decision reconciliation failure.
    /// </summary>
    public sealed class NodeManagerBatchResult
    {
        internal NodeManagerBatchResult(
            ArrayOf<NodeManagerRegistration> registrations,
            uint retired,
            Exception? cleanupFailure)
        {
            Registrations = registrations;
            Retired = retired;
            CleanupFailure = cleanupFailure;
        }

        /// <summary>
        /// Gets the exact committed additions and replacements.
        /// </summary>
        public ArrayOf<NodeManagerRegistration> Registrations { get; }

        /// <summary>
        /// Gets the number of old generations whose cleanup actually completed.
        /// </summary>
        public uint Retired { get; }

        /// <summary>
        /// Gets a post-commit failure, if any. It never denotes an aborted publication.
        /// </summary>
        public Exception? CleanupFailure { get; }
    }
}
