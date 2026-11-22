// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Captures routing, type and factory evidence before application input acquisition.
    /// </summary>
    public interface ILifecycleCapture
    {
        /// <summary>
        /// Begins an independently owned writer without recapturing revisions.
        /// A stale capture yields a stale writer that rejects preparation before factory effects.
        /// </summary>
        /// <param name="cancellationToken">Cancels admission.</param>
        /// <returns>The writer that must be asynchronously disposed.</returns>
        ValueTask<ILifecycleWriter> BeginAsync(CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Owns optimistic revision evidence across sequential change sets without excluding other writers.
    /// </summary>
    public interface ILifecycleWriter : IAsyncDisposable
    {
        /// <summary>
        /// Gets whether captured routing and metadata evidence remains current.
        /// </summary>
        bool IsCurrent { get; }

        /// <summary>
        /// Prepares a change set against this writer's evidence, rejecting stale work before factory effects.
        /// </summary>
        /// <param name="changes">The slot-targeted changes.</param>
        /// <param name="cancellationToken">Cancels preparation.</param>
        /// <returns>The unpublished owner.</returns>
        ValueTask<IPreparedChanges> PrepareAsync(
            ArrayOf<LifecycleChange> changes, CancellationToken cancellationToken = default);

        /// <summary>
        /// Prepares typed immutable metadata for live owners without replacing their managers.
        /// </summary>
        /// <param name="images">The immutable generation-owned images.</param>
        /// <param name="cancellationToken">Cancels preparation.</param>
        /// <returns>The unpublished metadata unit.</returns>
        ValueTask<IPreparedChanges> PrepareReadImagesAsync(
            ArrayOf<INodeManagerReadImage> images, CancellationToken cancellationToken = default);

        /// <summary>
        /// Inspects and validates private candidates, then joins their cleanup without publishing.
        /// A retained inspection view cannot commit or access candidates after inspection ends.
        /// </summary>
        /// <param name="changes">The changes to validate.</param>
        /// <param name="inspectAsync">Inspects candidates and binds metadata.</param>
        /// <param name="cancellationToken">Cancels preparation or validation.</param>
        /// <returns>Completion after validation and candidate cleanup.</returns>
        ValueTask ValidateAsync(
            ArrayOf<LifecycleChange> changes,
            Func<IPreparedChanges, CancellationToken, ValueTask> inspectAsync,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Owns unpublished candidates and typed read metadata until commit or asynchronous abort.
    /// Terminal units release their preparation payload; unresolved cleanup belongs to the lifecycle.
    /// </summary>
    public interface IPreparedChanges : IAsyncDisposable
    {
        /// <summary>
        /// Gets the targeted slots in change order, including additions and removals.
        /// </summary>
        ArrayOf<NodeManagerSlot> Slots { get; }

        /// <summary>
        /// Gets weak candidate registration snapshots in addition/replacement order.
        /// </summary>
        ArrayOf<NodeManagerRegistrationSnapshot> Registrations { get; }

        /// <summary>
        /// Gets whether this unit crossed the routing publication commit point.
        /// </summary>
        bool IsCommitted { get; }

        /// <summary>
        /// Binds immutable metadata once per slot before consumption.
        /// The exact owner must be a candidate or an unchanged live generation in the resulting view.
        /// </summary>
        /// <param name="slot">The slot that remains in the resulting view.</param>
        /// <param name="metadata">The generation-owned immutable image.</param>
        void SetMetadata(NodeManagerSlot slot, INodeManagerReadImage metadata);

        /// <summary>
        /// Binds immutable metadata using its exact owner's registered slot.
        /// </summary>
        /// <param name="metadata">The candidate or unchanged live owner's immutable image.</param>
        void SetMetadata(INodeManagerReadImage metadata);

        /// <summary>
        /// Decides and publishes once, returning reconciliation and independent retirement status.
        /// A null decision uses an in-memory decision. Stale work is consumed before deciding, without retry.
        /// </summary>
        /// <param name="decideAsync">The external durable decision, or null for an in-memory decision.</param>
        /// <param name="cancellationToken">Cancels before publication.</param>
        /// <returns>The authoritative committed result.</returns>
        ValueTask<LifecycleResult> CommitAsync(
            Func<CancellationToken, ValueTask>? decideAsync = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// Publishes prepared application state after the routing swap and before reconciliation.
        /// Failure is a committed issue; this callback must be idempotent for explicitly requested retry.
        /// </summary>
        /// <param name="decideAsync">The durable decision, or null for an in-memory decision.</param>
        /// <param name="publishCommittedState">
        /// The in-memory state publication, without I/O or another decision.
        /// </param>
        /// <param name="cancellationToken">Cancels before publication.</param>
        /// <returns>The authoritative committed result.</returns>
        ValueTask<LifecycleResult> CommitAsync(
            Func<CancellationToken, ValueTask>? decideAsync,
            Action publishCommittedState,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Commits with a durable decision that can identify its own reservation when calling back into the host.
        /// </summary>
        /// <param name="decideAsync">The durable decision, called with its revocable context.</param>
        /// <param name="publishCommittedState">Optional application state to install after the routing swap.</param>
        /// <param name="cancellationToken">Cancels before publication.</param>
        /// <returns>The authoritative committed result.</returns>
        ValueTask<LifecycleResult> CommitWithDecisionAsync(
            Func<NodeManagerDecisionContext, CancellationToken, ValueTask> decideAsync,
            Action? publishCommittedState = null,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Publishes in memory, rejecting re-entry from an active decision on the owning lifecycle.
        /// </summary>
        /// <param name="decisionContext">The context supplied to the durable decision.</param>
        /// <param name="cancellationToken">Cancels before publication.</param>
        /// <returns>The authoritative committed result.</returns>
        ValueTask<LifecycleResult> CommitInMemoryAsync(
            NodeManagerDecisionContext decisionContext,
            CancellationToken cancellationToken);

        /// <summary>
        /// Disposes the prepared changes, rejecting re-entry from an active decision on the owning lifecycle.
        /// </summary>
        /// <param name="decisionContext">The context supplied to the durable decision.</param>
        /// <returns>Completion of disposal.</returns>
        ValueTask DisposeAsync(NodeManagerDecisionContext decisionContext);
    }

    /// <summary>
    /// Identifies a durable decision without owning its writer, server, or routing image.
    /// </summary>
    public sealed class NodeManagerDecisionContext
    {
        internal NodeManagerDecisionContext(Guid writerId)
        {
            WriterId = writerId;
        }

        /// <summary>
        /// Rejects namespace and node-management mutations on the host that owns this reservation.
        /// The check remains in effect until publication releases its reservation.
        /// </summary>
        /// <param name="host">The host to call.</param>
        /// <exception cref="ArgumentNullException"><paramref name="host"/> is null.</exception>
        internal void CheckAddressSpaceMutation(IMasterNodeManager host)
        {
            if (host is null)
            {
                throw new ArgumentNullException(nameof(host));
            }
            if (host is MasterNodeManager stock)
            {
                stock.ThrowIfDecisionReservesAddressSpace(this);
            }
        }

        /// <summary>
        /// Gets the identity of the writer invoking the decision.
        /// </summary>
        internal Guid WriterId { get; }

        /// <summary>
        /// Gets whether the decision callback is still running.
        /// </summary>
        internal bool IsActive => Volatile.Read(ref m_active) != 0;

        /// <summary>
        /// Gets whether the publication reservation is still open.
        /// </summary>
        internal bool IsOpen => Volatile.Read(ref m_open) != 0;

        /// <summary>
        /// Marks the decision active until the callback exits.
        /// </summary>
        /// <exception cref="InvalidOperationException">The decision is already active.</exception>
        internal void Activate()
        {
            if (Interlocked.CompareExchange(ref m_active, 1, 0) != 0)
            {
                throw new InvalidOperationException("The durable decision callback is already active.");
            }
        }

        /// <summary>
        /// Ends callback activity without closing the publication reservation.
        /// </summary>
        internal void Deactivate()
        {
            Volatile.Write(ref m_active, 0);
        }

        /// <summary>
        /// Revokes access to the publication reservation.
        /// </summary>
        internal bool Close()
        {
            return Interlocked.Exchange(ref m_open, 0) != 0;
        }

        private int m_active;
        private int m_open = 1;
    }

    /// <summary>
    /// Selects how monitored items owned by a replaced NodeManager are handled.
    /// </summary>
    public enum NodeManagerMonitoredItemTransitionMode
    {
        /// <summary>
        /// Move compatible items and invalidate items whose nodes are unavailable.
        /// </summary>
        Migrate,

        /// <summary>
        /// Invalidate monitored items owned by the retiring NodeManager.
        /// </summary>
        Invalidate
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
        Retirement,

        /// <summary>
        /// Server-wide historian capability and event notifier refresh.
        /// </summary>
        HistorianCapabilities
    }

    /// <summary>
    /// An immutable snapshot of a pending or superseded reconciliation step.
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
        /// Gets whether the owning generation or server was superseded.
        /// </summary>
        public bool IsSuperseded { get; }

        /// <summary>
        /// Gets whether reconciliation can retry this step, excluding physical retirement.
        /// </summary>
        public bool IsRetryable { get; }
    }

    /// <summary>
    /// The authoritative committed revision with weak registration views and non-owning status tickets.
    /// </summary>
    public sealed class LifecycleResult
    {
        /// <summary>
        /// Creates a committed result without retaining candidate or callback ownership.
        /// </summary>
        internal LifecycleResult(
            ArrayOf<NodeManagerRegistrationSnapshot> registrations,
            uint retired,
            Exception? cleanupFailure,
            long revision,
            ArrayOf<NodeManagerSlot> slots,
            Task retirement,
            GenerationReconciler.Reconciliation? reconciliation = null,
            ArrayOf<NodeManagerGenerationSnapshot> publishedSlots = default)
        {
            Registrations = registrations;
            RetiredCount = retired;
            CleanupFailure = cleanupFailure;
            Revision = revision;
            Slots = slots;
            PublishedSlots = publishedSlots;
            Retired = retirement ?? throw new ArgumentNullException(nameof(retirement));
            m_reconciliation = reconciliation;
        }

        /// <summary>
        /// Gets weak snapshots of committed additions and replacements.
        /// </summary>
        public ArrayOf<NodeManagerRegistrationSnapshot> Registrations { get; }

        /// <summary>
        /// Gets the number of old generations cleaned up before this result was created.
        /// </summary>
        public uint RetiredCount { get; }

        /// <summary>
        /// Gets the original post-commit failure, never an aborted publication.
        /// </summary>
        public Exception? CleanupFailure { get; }

        /// <summary>
        /// Gets the published routing revision.
        /// </summary>
        public long Revision { get; }

        /// <summary>
        /// Gets stable slots in the published view.
        /// </summary>
        public ArrayOf<NodeManagerSlot> Slots { get; }

        /// <summary>
        /// Gets generation snapshots at publication, unaffected by later slot replacement.
        /// </summary>
        public ArrayOf<NodeManagerGenerationSnapshot> PublishedSlots { get; }

        /// <summary>
        /// Gets physical retirement completion, including earlier failed attempts after eventual cleanup.
        /// </summary>
        public Task Retired { get; }

        /// <summary>
        /// Gets physical retirement completion independently of reconciliation.
        /// </summary>
        public NodeManagerRetirementStatus RetirementStatus => Retired.Status switch
        {
            TaskStatus.RanToCompletion => NodeManagerRetirementStatus.Completed,
            TaskStatus.Faulted => NodeManagerRetirementStatus.CompletedWithFailure,
            TaskStatus.Canceled => NodeManagerRetirementStatus.Canceled,
            _ => NodeManagerRetirementStatus.Pending
        };

        /// <summary>
        /// Gets current issues while preserving the initial commit failure in <see cref="CleanupFailure"/>.
        /// </summary>
        public ArrayOf<NodeManagerReconciliationIssue> Issues => m_reconciliation?.Issues ?? [];

        /// <summary>
        /// Joins explicit retry of unfinished reconciliation, never factories, decisions or publication.
        /// </summary>
        /// <param name="cancellationToken">Cancels this caller's wait, not admitted work.</param>
        /// <returns>Completion of the shared attempt.</returns>
        public ValueTask RetryReconciliationAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return m_reconciliation?.RetryAsync(cancellationToken) ?? default;
        }

        /// <summary>The non-owning reconciliation status ticket.</summary>
        private readonly GenerationReconciler.Reconciliation? m_reconciliation;
    }

    /// <summary>
    /// Describes physical retirement completion, not publication outcome.
    /// </summary>
    public enum NodeManagerRetirementStatus
    {
        /// <summary>
        /// An old generation is still waiting for drain or cleanup.
        /// </summary>
        Pending,

        /// <summary>
        /// Every old generation was cleaned up without a failed attempt.
        /// </summary>
        Completed,

        /// <summary>
        /// Every old generation was cleaned up, but an earlier attempt failed.
        /// </summary>
        CompletedWithFailure,

        /// <summary>
        /// Retirement was canceled; cleanup completion is not established.
        /// </summary>
        Canceled
    }
}
