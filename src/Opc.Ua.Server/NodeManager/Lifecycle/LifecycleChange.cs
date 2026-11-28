// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Server
{
    /// <summary>
    /// One slot-targeted change in a lifecycle publication unit.
    /// </summary>
    public sealed class LifecycleChange
    {
        /// <summary>
        /// Creates an immutable addition, replacement or removal.
        /// </summary>
        /// <param name="slot">The target slot, or null for an addition.</param>
        /// <param name="factory">The candidate factory, or null for a removal.</param>
        /// <param name="transitionMode">The monitored-item transition.</param>
        private LifecycleChange(
            NodeManagerSlot? slot,
            IAsyncNodeManagerFactory? factory,
            NodeManagerMonitoredItemTransitionMode transitionMode)
        {
            Slot = slot;
            Factory = factory;
            TransitionMode = transitionMode;
        }

        /// <summary>
        /// Gets the stable target, resolved once when preparation captures its routing revision.
        /// </summary>
        public NodeManagerSlot? Slot { get; }

        /// <summary>
        /// Gets the private candidate factory, or null for removal.
        /// </summary>
        public IAsyncNodeManagerFactory? Factory { get; }

        /// <summary>
        /// Gets the transition applied to the generation selected during preparation.
        /// </summary>
        public NodeManagerMonitoredItemTransitionMode TransitionMode { get; }

        /// <summary>
        /// Adds a privately constructed manager in a new slot.
        /// </summary>
        /// <param name="factory">The candidate factory.</param>
        /// <returns>The addition.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="factory"/> is null.</exception>
        public static LifecycleChange Add(IAsyncNodeManagerFactory factory)
        {
            return new LifecycleChange(
                null, factory ?? throw new ArgumentNullException(nameof(factory)),
                NodeManagerMonitoredItemTransitionMode.Migrate);
        }

        /// <summary>
        /// Replaces the generation current in the slot when preparation starts.
        /// </summary>
        /// <param name="slot">The stable slot owned by the lifecycle.</param>
        /// <param name="factory">The replacement factory.</param>
        /// <param name="transitionMode">The monitored-item transition.</param>
        /// <returns>The replacement.</returns>
        /// <exception cref="ArgumentOutOfRangeException">The transition mode is not supported.</exception>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="slot"/> or <paramref name="factory"/> is null.
        /// </exception>
        public static LifecycleChange Replace(
            NodeManagerSlot slot,
            IAsyncNodeManagerFactory factory,
            NodeManagerMonitoredItemTransitionMode transitionMode = NodeManagerMonitoredItemTransitionMode.Migrate)
        {
            if (transitionMode is not (NodeManagerMonitoredItemTransitionMode.Migrate or
                NodeManagerMonitoredItemTransitionMode.Invalidate))
            {
                throw new ArgumentOutOfRangeException(nameof(transitionMode));
            }
            return new LifecycleChange(
                slot ?? throw new ArgumentNullException(nameof(slot)),
                factory ?? throw new ArgumentNullException(nameof(factory)), transitionMode);
        }

        /// <summary>
        /// Reloads the generation selected from the stable slot during preparation.
        /// </summary>
        /// <param name="slot">The target slot.</param>
        /// <param name="factory">The replacement factory.</param>
        /// <param name="transitionMode">The monitored-item transition.</param>
        /// <returns>The replacement change.</returns>
        public static LifecycleChange Reload(
            NodeManagerSlot slot,
            IAsyncNodeManagerFactory factory,
            NodeManagerMonitoredItemTransitionMode transitionMode = NodeManagerMonitoredItemTransitionMode.Migrate)
        {
            return Replace(slot, factory, transitionMode);
        }

        /// <summary>
        /// Removes the generation current in the slot when preparation starts.
        /// </summary>
        /// <param name="slot">The stable slot owned by the lifecycle.</param>
        /// <returns>The removal.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="slot"/> is null.</exception>
        public static LifecycleChange Remove(NodeManagerSlot slot)
        {
            return new LifecycleChange(
                slot ?? throw new ArgumentNullException(nameof(slot)),
                null, NodeManagerMonitoredItemTransitionMode.Invalidate);
        }
    }

    /// <summary>
    /// Convenience operations over the native lifecycle transaction contract.
    /// </summary>
    public static class LifecycleChangeExtensions
    {
        /// <summary>
        /// Adapts an existing synchronous factory to the asynchronous change-set boundary.
        /// Factory creation remains synchronous; this does not block on asynchronous work.
        /// </summary>
        /// <param name="factory">The existing factory.</param>
        /// <returns>The shared compatibility factory adapter.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="factory"/> is null.</exception>
        // TODO: Remove this naming exemption when RCS1047 recognizes async-interface adapter names.
#pragma warning disable RCS1047 // AsAsync names an interface adapter, not asynchronous execution.
#if NET5_0_OR_GREATER
        [Obsolete(SyncNodeManagerDeprecation.INodeManagerFactory,
            DiagnosticId = SyncNodeManagerDeprecation.DiagnosticId,
            UrlFormat = SyncNodeManagerDeprecation.UrlFormat)]
#else
        [Obsolete(SyncNodeManagerDeprecation.INodeManagerFactory)]
#endif
        public static IAsyncNodeManagerFactory AsAsync(this INodeManagerFactory factory)
        {
            return new SyncFactoryAdapter(
                factory ?? throw new ArgumentNullException(nameof(factory)));
        }
#pragma warning restore RCS1047

        /// <summary>
        /// Prepares and commits one change set with an in-memory decision, without conflict retries.
        /// Use preparation and explicit commit for an external durable decision or application-state callback.
        /// </summary>
        /// <param name="lifecycle">The lifecycle owning the target slots.</param>
        /// <param name="changes">The additions, replacements and removals.</param>
        /// <param name="cancellationToken">Cancels preparation or the pre-publication decision.</param>
        /// <returns>The authoritative committed result, including any reconciliation issues.</returns>
        /// <exception cref="NodeManagerLifecycleConflictException">
        /// Another publication changed an input of the prepared changes; nothing was published. Apply again to retry.
        /// </exception>
        /// <exception cref="ArgumentNullException"><paramref name="lifecycle"/> is null.</exception>
        public static async ValueTask<LifecycleResult> ApplyAsync(
            this INodeManagerLifecycle lifecycle,
            ArrayOf<LifecycleChange> changes,
            CancellationToken cancellationToken = default)
        {
            if (lifecycle is null)
            {
                throw new ArgumentNullException(nameof(lifecycle));
            }
            IPreparedChanges prepared = await lifecycle.PrepareAsync(changes, cancellationToken).ConfigureAwait(false);
            await using (prepared.ConfigureAwait(false))
            {
                return await prepared.CommitAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Begins optimistic revision ownership for sequential units without holding a writer across factories.
        /// </summary>
        /// <param name="lifecycle">The lifecycle owning the publication.</param>
        /// <param name="expectedRevision">The required routing revision, or null to capture the current view.</param>
        /// <param name="cancellationToken">Cancels admission.</param>
        /// <returns>The writer capability; stale captures cannot prepare or commit.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="lifecycle"/> is null.</exception>
        /// <exception cref="InvalidOperationException">
        /// The required publication revision is no longer current.
        /// </exception>
        public static async ValueTask<ILifecycleWriter> BeginAsync(
            this INodeManagerLifecycle lifecycle,
            long? expectedRevision = null,
            CancellationToken cancellationToken = default)
        {
            if (lifecycle is null)
            {
                throw new ArgumentNullException(nameof(lifecycle));
            }
            cancellationToken.ThrowIfCancellationRequested();
            ILifecycleWriter writer = await lifecycle.CaptureChanges()
                .BeginAsync(cancellationToken).ConfigureAwait(false);
            if (expectedRevision is { } revision &&
                (lifecycle.Revision != revision || !writer.IsCurrent))
            {
                await writer.DisposeAsync().ConfigureAwait(false);
                throw new InvalidOperationException("The expected lifecycle revision is no longer current.");
            }
            return writer;
        }

        /// <summary>
        /// Adapts released synchronous factory creation without blocking asynchronous work.
        /// </summary>
        /// <param name="factory">The released factory-list implementation.</param>
#pragma warning disable CS0618, UA0031 // Compatibility bridge for the released synchronous factory list.
        // TODO: Remove this bridge when synchronous factories are removed.
        internal sealed class SyncFactoryAdapter(INodeManagerFactory factory) : IAsyncNodeManagerFactory
        {
            /// <inheritdoc/>
            public ArrayOf<string> NamespacesUris => m_factory.NamespacesUris;

            /// <inheritdoc/>
            public ValueTask<IAsyncNodeManager> CreateAsync(
                IServerInternal server,
                ApplicationConfiguration configuration,
                CancellationToken ct = default)
            {
                ct.ThrowIfCancellationRequested();
                return new ValueTask<IAsyncNodeManager>(
                    m_factory.Create(server, configuration).ToAsyncNodeManager());
            }

            /// <summary>The released factory whose synchronous creation is adapted.</summary>
            private readonly INodeManagerFactory m_factory = factory
                ?? throw new ArgumentNullException(nameof(factory));
        }
#pragma warning restore CS0618, UA0031
    }
}
