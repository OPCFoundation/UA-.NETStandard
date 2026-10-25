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
    /// Owns an unpublished unit and all resources acquired while preparing it.
    /// </summary>
    /// <remarks>
    /// Durable decision callbacks cannot re-enter NodeManager lifecycle operations. Nested lifecycle operations
    /// are rejected before they can wait on the active commit or alter its prepared unit.
    /// </remarks>
    public interface IPreparedNodeManagerBatch : IAsyncDisposable
    {
        /// <summary>
        /// Gets the exact candidate registrations in addition/replacement order.
        /// They are not live until the commit publishes the unit.
        /// </summary>
        ArrayOf<NodeManagerRegistration> Registrations { get; }

        /// <summary>
        /// Gets whether the prepared routing view has been published.
        /// </summary>
        bool IsCommitted { get; }

        /// <summary>
        /// Binds immutable application state before commit. The sequence is copied and may be bound only once.
        /// Each image must belong to a distinct NodeManager in the resulting routing image.
        /// </summary>
        /// <param name="images">The read images to bind, one per NodeManager.</param>
        void BindReadImages(ArrayOf<INodeManagerReadImage> images);

        /// <summary>
        /// Rechecks ownership and the captured routing revision, invokes the durable decision once,
        /// then publishes one routing image. The routing-view publication is the commit point;
        /// returning from the durable decision callback alone does not make this unit committed.
        /// A callback that cannot determine its durable outcome must surface that uncertainty, and
        /// the caller must reconcile it before retrying. After the routing view is published,
        /// cancellation cannot undo the unit; cleanup failures are returned with the committed registrations.
        /// Committed reconciliation releases operation ownership before this call completes,
        /// including when it returns a cleanup warning. Disposal is then optional and idempotent.
        /// An uncommitted unit must still be disposed after a failed commit attempt.
        /// </summary>
        /// <param name="decideAsync">The callback performing the durable decision.</param>
        /// <param name="cancellationToken">The token to cancel the operation before the decision.</param>
        /// <returns>The committed registrations and any post-commit cleanup failure.</returns>
        /// <exception cref="InvalidOperationException">
        /// Routing changed after preparation; the durable decision is not invoked.
        /// </exception>
        ValueTask<NodeManagerBatchResult> CommitAsync(
            Func<CancellationToken, ValueTask> decideAsync,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Publishes the same unit and its already-prepared in-memory state at the routing-view
        /// commit point, before readiness and retirement reconciliation.
        /// The publication callback must perform no I/O or additional durable decision.
        /// A callback failure occurs after commit and cannot make the batch uncommitted.
        /// </summary>
        /// <param name="decideAsync">The callback performing the durable decision.</param>
        /// <param name="publishCommittedState">The callback publishing the prepared in-memory state.</param>
        /// <param name="cancellationToken">The token to cancel the operation before the decision.</param>
        /// <returns>The committed registrations and any post-commit cleanup failure.</returns>
        ValueTask<NodeManagerBatchResult> CommitAsync(
            Func<CancellationToken, ValueTask> decideAsync,
            Action publishCommittedState,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Selects how monitored items owned by a replaced NodeManager are handled.
    /// </summary>
    public enum NodeManagerMonitoredItemTransitionMode
    {
        /// <summary>
        /// Move compatible monitored items to the replacement and invalidate items whose nodes are unavailable.
        /// </summary>
        Migrate,

        /// <summary>
        /// Invalidate monitored items owned by the retiring NodeManager.
        /// </summary>
        Invalidate
    }

    /// <summary>
    /// One addition, replacement or retirement in a prepared publication unit.
    /// </summary>
    public sealed class NodeManagerBatchChange
    {
        /// <summary>
        /// Creates a change.
        /// </summary>
        /// <param name="current">The registration being replaced or retired, or null for an addition.</param>
        /// <param name="factory">The candidate factory, or null for a retirement.</param>
        /// <param name="transitionMode">The monitored-item transition used for the retiring generation.</param>
        private NodeManagerBatchChange(
            NodeManagerRegistration? current,
            IAsyncNodeManagerFactory? factory,
            NodeManagerMonitoredItemTransitionMode transitionMode)
        {
            Current = current;
            Factory = factory;
            TransitionMode = transitionMode;
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
        /// Gets the monitored-item transition applied to the retiring generation.
        /// </summary>
        public NodeManagerMonitoredItemTransitionMode TransitionMode { get; }

        /// <summary>
        /// Gets whether old monitored sources are invalidated.
        /// </summary>
        public bool Immediate => TransitionMode == NodeManagerMonitoredItemTransitionMode.Invalidate;

        /// <summary>
        /// Adds a new privately prepared owner.
        /// </summary>
        /// <param name="factory">The factory creating the new owner.</param>
        /// <returns>The addition change.</returns>
        public static NodeManagerBatchChange Add(IAsyncNodeManagerFactory factory)
        {
            return new NodeManagerBatchChange(
                null,
                factory ?? throw new ArgumentNullException(nameof(factory)),
                NodeManagerMonitoredItemTransitionMode.Migrate);
        }

        /// <summary>
        /// Replaces an exact registration while preserving its logical identity.
        /// </summary>
        /// <param name="current">The exact registration being replaced.</param>
        /// <param name="factory">The factory creating the replacement owner.</param>
        /// <param name="transitionMode">How monitored items owned by the current generation are handled.</param>
        /// <returns>The replacement change.</returns>
        public static NodeManagerBatchChange Replace(
            NodeManagerRegistration current,
            IAsyncNodeManagerFactory factory,
            NodeManagerMonitoredItemTransitionMode transitionMode =
                NodeManagerMonitoredItemTransitionMode.Migrate)
        {
            return new NodeManagerBatchChange(
                current ?? throw new ArgumentNullException(nameof(current)),
                factory ?? throw new ArgumentNullException(nameof(factory)),
                transitionMode);
        }

        /// <summary>
        /// Replaces an exact registration using the legacy immediate flag.
        /// </summary>
        /// <param name="current">The exact registration being replaced.</param>
        /// <param name="factory">The factory creating the replacement owner.</param>
        /// <param name="immediate">Whether old monitored sources are invalidated.</param>
        /// <returns>The replacement change.</returns>
        public static NodeManagerBatchChange Replace(
            NodeManagerRegistration current,
            IAsyncNodeManagerFactory factory,
            bool immediate)
        {
            return Replace(
                current,
                factory,
                immediate
                    ? NodeManagerMonitoredItemTransitionMode.Invalidate
                    : NodeManagerMonitoredItemTransitionMode.Migrate);
        }

        /// <summary>
        /// Retires an exact registration in the same switch as the other changes.
        /// </summary>
        /// <param name="current">The exact registration being retired.</param>
        /// <returns>The retirement change.</returns>
        public static NodeManagerBatchChange Remove(NodeManagerRegistration current)
        {
            return new NodeManagerBatchChange(
                current ?? throw new ArgumentNullException(nameof(current)),
                null,
                NodeManagerMonitoredItemTransitionMode.Invalidate);
        }

        /// <summary>
        /// Retires an exact registration using the legacy immediate flag.
        /// </summary>
        /// <param name="current">The exact registration being retired.</param>
        /// <param name="immediate">Ignored. Retiring a NodeManager always invalidates its monitored items.</param>
        /// <returns>The retirement change.</returns>
        public static NodeManagerBatchChange Remove(NodeManagerRegistration current, bool immediate)
        {
            return Remove(current);
        }
    }

    /// <summary>
    /// The committed registrations and any post-decision reconciliation failure.
    /// </summary>
    public sealed class NodeManagerBatchResult
    {
        /// <summary>
        /// Creates a result.
        /// </summary>
        /// <param name="registrations">The committed additions and replacements.</param>
        /// <param name="retired">The number of old generations whose cleanup completed.</param>
        /// <param name="cleanupFailure">The post-commit failure, if any.</param>
        /// <param name="retirement">The task that completes after every retired generation is cleaned up.</param>
        /// <param name="revision">The published routing revision.</param>
        /// <param name="slots">The stable slots in the published routing view.</param>
        internal NodeManagerBatchResult(
            ArrayOf<NodeManagerRegistration> registrations,
            uint retired,
            Exception? cleanupFailure,
            long revision,
            NodeManagerSlot[] slots,
            Task retirement)
        {
            Registrations = registrations;
            Retired = retired;
            CleanupFailure = cleanupFailure;
            Revision = revision;
            Slots = new ArrayOf<NodeManagerSlot>(slots);
            Retirement = retirement ?? throw new ArgumentNullException(nameof(retirement));
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

        /// <summary>
        /// Gets the revision of the published routing view.
        /// </summary>
        public long Revision { get; }

        /// <summary>
        /// Gets the stable slots in the published routing view.
        /// </summary>
        public ArrayOf<NodeManagerSlot> Slots { get; }

        /// <summary>
        /// Gets a task that completes when every generation retired by this batch has been fully cleaned up,
        /// or faults if asynchronous cleanup fails.
        /// </summary>
        public Task Retirement { get; }
    }
}
