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
    /// This compatibility owner adapts to <see cref="IPreparedChanges"/> for slot-based consumers.
    /// Its committed result delegates to <see cref="NodeManagerBatchResult.CommittedResult"/> without copying status.
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
        /// The callback must preserve <see cref="ExecutionContext"/> flow for work that can re-enter namespace
        /// registration; the re-entry guard uses the callback's asynchronous execution context to reject mutations
        /// that would wait on this commit's publication reservation.
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
        /// A callback failure occurs after commit and cannot make the batch uncommitted. The callback may
        /// have partially applied state; it must be idempotent when retried through the committed result,
        /// and must not overwrite a newer durable recovery decision.
        /// The durable decision callback must preserve <see cref="ExecutionContext"/> flow for work that can
        /// re-enter namespace registration; the re-entry guard uses the callback's asynchronous execution context
        /// to reject mutations that would wait on this commit's publication reservation.
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
        /// <param name="targetSlot">The stable target resolved during preparation, or null for legacy changes.</param>
        private NodeManagerBatchChange(
            NodeManagerRegistration? current,
            IAsyncNodeManagerFactory? factory,
            NodeManagerMonitoredItemTransitionMode transitionMode,
            NodeManagerSlot? targetSlot = null)
        {
            Current = current;
            Factory = factory;
            TransitionMode = transitionMode;
            TargetSlot = targetSlot;
        }

        /// <summary>
        /// Gets the stable target for slot-based changes, or null for exact-generation changes and additions.
        /// Providers must resolve its current registration once under preparation admission.
        /// </summary>
        public NodeManagerSlot? TargetSlot { get; }

        /// <summary>
        /// Gets the exact registration being replaced or retired, or null for an addition or stable-slot target.
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
        /// Converts a slot-targeted change to the shared preparation representation.
        /// </summary>
        /// <param name="change">The change whose target is resolved by the lifecycle writer.</param>
        /// <returns>The shared preparation input.</returns>
        internal static NodeManagerBatchChange FromChange(LifecycleChange change)
        {
            return new NodeManagerBatchChange(null, change.Factory, change.TransitionMode, change.Slot);
        }
    }

    /// <summary>
    /// Identifies a post-publication reconciliation stage.
    /// </summary>
    public enum NodeManagerReconciliationStage
    {
        /// <summary>
        /// Prepared application state publication.
        /// </summary>
        ApplicationState,

        /// <summary>
        /// Cross-manager reference reconciliation.
        /// </summary>
        References,

        /// <summary>
        /// Session and all-events bindings.
        /// </summary>
        Bindings,

        /// <summary>
        /// Monitored item recovery.
        /// </summary>
        MonitoredItems,

        /// <summary>
        /// Server readiness notification.
        /// </summary>
        Readiness,

        /// <summary>
        /// Identity policy notification.
        /// </summary>
        Identity,

        /// <summary>
        /// Namespace table notification.
        /// </summary>
        Namespaces,

        /// <summary>
        /// Model change event delivery.
        /// </summary>
        ModelChange,

        /// <summary>
        /// Semantic change event delivery.
        /// </summary>
        SemanticChange,

        /// <summary>
        /// An unexpected host publication completion failure.
        /// </summary>
        Publication,

        /// <summary>
        /// Independently owned retired-generation cleanup.
        /// </summary>
        Retirement
    }

    /// <summary>
    /// A pending or superseded post-publication reconciliation step.
    /// </summary>
    public sealed class NodeManagerReconciliationIssue
    {
        /// <summary>
        /// Creates an immutable issue snapshot.
        /// </summary>
        internal NodeManagerReconciliationIssue(
            NodeManagerReconciliationStage stage,
            Exception initialFailure,
            Exception currentFailure,
            bool superseded,
            bool retryable = true)
        {
            Stage = stage;
            InitialFailure = initialFailure;
            CurrentFailure = currentFailure;
            IsSuperseded = superseded;
            IsRetryable = retryable && !superseded;
        }

        /// <summary>
        /// Gets the failed stage.
        /// </summary>
        public NodeManagerReconciliationStage Stage { get; }

        /// <summary>
        /// Gets the original failure, retained across retry attempts.
        /// </summary>
        public Exception InitialFailure { get; }

        /// <summary>
        /// Gets the latest failure.
        /// </summary>
        public Exception CurrentFailure { get; }

        /// <summary>
        /// Gets whether the owning generation or server was superseded. Such work cannot be retried.
        /// </summary>
        public bool IsSuperseded { get; }

        /// <summary>
        /// Gets whether this result can retry the failed step. Retirement and legacy handoff failures
        /// remain owned by their respective cleanup paths rather than being replayed here.
        /// </summary>
        public bool IsRetryable { get; }
    }

    /// <summary>
    /// The committed registrations and any post-decision reconciliation failure.
    /// </summary>
    public sealed class LifecycleResult
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
        /// <param name="publishedSlots">The generation snapshots captured at publication.</param>
        /// <param name="reconciliation">The non-owning reconciliation status ticket.</param>
        internal LifecycleResult(
            ArrayOf<NodeManagerRegistration> registrations,
            uint retired,
            Exception? cleanupFailure,
            long revision,
            NodeManagerSlot[] slots,
            Task retirement,
            GenerationReconciler.Reconciliation? reconciliation = null,
            ArrayOf<NodeManagerSlotSnapshot> publishedSlots = default)
        {
            Registrations = registrations;
            RetiredCount = retired;
            CleanupFailure = cleanupFailure;
            Revision = revision;
            Slots = new ArrayOf<NodeManagerSlot>(slots);
            PublishedSlots = publishedSlots;
            Retired = retirement ?? throw new ArgumentNullException(nameof(retirement));
            m_reconciliation = reconciliation;
        }

        /// <summary>
        /// Gets the exact committed additions and replacements.
        /// </summary>
        public ArrayOf<NodeManagerRegistration> Registrations { get; }

        /// <summary>
        /// Gets the number of old generations whose cleanup completed before this result was created.
        /// </summary>
        public uint RetiredCount { get; }

        /// <summary>
        /// Gets a post-commit reconciliation failure, if any. It never denotes an aborted publication.
        /// Physical cleanup attempt failures are returned here when they occur before this result is created.
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
        /// Gets the application slot generations at this result's revision. Unlike a slot's current value,
        /// these snapshots do not advance when a reconciliation callback publishes a nested change.
        /// </summary>
        public ArrayOf<NodeManagerSlotSnapshot> PublishedSlots { get; }

        /// <summary>
        /// Gets a task that completes after every generation retired by this batch has been cleaned up, including
        /// retries. The task faults if an earlier cleanup attempt failed, even when a later retry succeeds.
        /// </summary>
        public Task Retired { get; }

        /// <summary>
        /// Gets physical retirement completion independently of reconciliation and supersession.
        /// A failed task denotes completed cleanup with an earlier failed attempt, not permission to republish.
        /// </summary>
        public NodeManagerRetirementStatus RetirementStatus => Retired.Status switch
        {
            TaskStatus.RanToCompletion => NodeManagerRetirementStatus.Completed,
            TaskStatus.Faulted => NodeManagerRetirementStatus.CompletedWithFailure,
            TaskStatus.Canceled => NodeManagerRetirementStatus.Canceled,
            _ => NodeManagerRetirementStatus.Pending
        };

        /// <summary>
        /// Gets current reconciliation issues. Successful steps disappear; superseded steps remain explicit.
        /// The original commit's failure is retained in <see cref="CleanupFailure"/>.
        /// </summary>
        public ArrayOf<NodeManagerReconciliationIssue> Issues => m_reconciliation?.Issues ?? [];

        /// <summary>
        /// Retries only unfinished reconciliation, without another durable decision or view publication.
        /// Concurrent callers join one attempt. Cancellation cancels only this caller's wait, not admitted work.
        /// Application callbacks that throw may already have partially applied state and must be idempotent.
        /// </summary>
        /// <param name="cancellationToken">Cancels waiting for the shared attempt.</param>
        /// <returns>A task completing after the attempt; inspect <see cref="Issues"/> for remaining failures.</returns>
        public ValueTask RetryReconciliationAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return m_reconciliation?.RetryAsync(cancellationToken) ?? default;
        }

        /// <summary>The result-owned status ticket, which does not retain callback ownership.</summary>
        private readonly GenerationReconciler.Reconciliation? m_reconciliation;
    }

    /// <summary>
    /// Compatibility view over the authoritative lifecycle result.
    /// </summary>
    public sealed class NodeManagerBatchResult
    {
        /// <summary>
        /// Creates a compatibility result for the existing batch provider boundary.
        /// </summary>
        /// <param name="registrations">The committed candidate registrations.</param>
        /// <param name="retired">The number physically retired before reconciliation returned.</param>
        /// <param name="cleanupFailure">The initial committed failure.</param>
        /// <param name="revision">The committed routing revision.</param>
        /// <param name="slots">The slots at publication.</param>
        /// <param name="retirement">The physical retirement task.</param>
        /// <param name="reconciliation">The shared reconciliation ticket.</param>
        /// <param name="publishedSlots">The generation snapshots at publication.</param>
        internal NodeManagerBatchResult(
            ArrayOf<NodeManagerRegistration> registrations,
            uint retired,
            Exception? cleanupFailure,
            long revision,
            NodeManagerSlot[] slots,
            Task retirement,
            GenerationReconciler.Reconciliation? reconciliation = null,
            ArrayOf<NodeManagerSlotSnapshot> publishedSlots = default)
            : this(new LifecycleResult(
                registrations, retired, cleanupFailure, revision, slots, retirement, reconciliation, publishedSlots))
        {
        }

        /// <summary>
        /// Creates a compatibility view without copying committed facts or checkpoint ownership.
        /// </summary>
        /// <param name="result">The authoritative result.</param>
        internal NodeManagerBatchResult(LifecycleResult result)
        {
            CommittedResult = result ?? throw new ArgumentNullException(nameof(result));
        }

        /// <summary>
        /// Gets the authoritative result shared by new callers and compatibility failures.
        /// </summary>
        public LifecycleResult CommittedResult { get; }

        /// <summary>
        /// Gets the committed candidate registrations.
        /// </summary>
        public ArrayOf<NodeManagerRegistration> Registrations => CommittedResult.Registrations;

        /// <summary>
        /// Gets the number physically retired before this result was created.
        /// </summary>
        public uint Retired => CommittedResult.RetiredCount;

        /// <summary>
        /// Gets the initial post-publication failure.
        /// </summary>
        public Exception? CleanupFailure => CommittedResult.CleanupFailure;

        /// <summary>
        /// Gets the committed routing revision.
        /// </summary>
        public long Revision => CommittedResult.Revision;

        /// <summary>
        /// Gets the stable slots at publication.
        /// </summary>
        public ArrayOf<NodeManagerSlot> Slots => CommittedResult.Slots;

        /// <summary>
        /// Gets the generation snapshots at publication.
        /// </summary>
        public ArrayOf<NodeManagerSlotSnapshot> PublishedSlots => CommittedResult.PublishedSlots;

        /// <summary>
        /// Gets the independent physical retirement task.
        /// </summary>
        public Task Retirement => CommittedResult.Retired;

        /// <summary>
        /// Gets physical retirement completion.
        /// </summary>
        public NodeManagerRetirementStatus RetirementStatus => CommittedResult.RetirementStatus;

        /// <summary>
        /// Gets the shared current reconciliation issues.
        /// </summary>
        public ArrayOf<NodeManagerReconciliationIssue> Issues => CommittedResult.Issues;

        /// <summary>
        /// Retries only shared unfinished reconciliation without republishing.
        /// </summary>
        /// <param name="cancellationToken">Cancels only this caller's wait.</param>
        /// <returns>Completion of the joined retry attempt.</returns>
        public ValueTask RetryReconciliationAsync(CancellationToken cancellationToken = default)
        {
            return CommittedResult.RetryReconciliationAsync(cancellationToken);
        }
    }

    /// <summary>
    /// Describes the completion task for physical retirement, not the outcome of publication.
    /// </summary>
    public enum NodeManagerRetirementStatus
    {
        /// <summary>
        /// At least one old generation is still waiting for drain or cleanup.
        /// </summary>
        Pending,

        /// <summary>
        /// Every old generation was physically cleaned up without a failed attempt.
        /// </summary>
        Completed,

        /// <summary>
        /// Every old generation was physically cleaned up, but an earlier attempt failed.
        /// </summary>
        CompletedWithFailure,

        /// <summary>
        /// The retirement completion task was canceled; cleanup completion is not established.
        /// </summary>
        Canceled
    }
}
