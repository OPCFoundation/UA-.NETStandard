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
using System.Linq;
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
    /// Slot-targeted consumer operations over the existing prepared transaction contract.
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
#pragma warning disable RCS1047 // AsAsync names the report's factory conversion, not asynchronous execution.
        public static IAsyncNodeManagerFactory AsAsync(this INodeManagerFactory factory)
        {
            return new NodeManagerLifecycleExtensions.SyncFactoryAdapter(
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
                return await prepared.CommitAsync(_ => default, cancellationToken).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Resolves stable targets once through the shared preparation pipeline.
        /// A later routing conflict consumes this prepared unit before its durable decision; it is never rebased.
        /// </summary>
        /// <param name="lifecycle">The lifecycle owning the target slots.</param>
        /// <param name="changes">The change set to prepare.</param>
        /// <param name="cancellationToken">Cancels preparation.</param>
        /// <returns>The unpublished owner that must be committed or asynchronously disposed.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="lifecycle"/> is null.</exception>
        public static async ValueTask<IPreparedChanges> PrepareAsync(
            this INodeManagerLifecycle lifecycle,
            ArrayOf<LifecycleChange> changes,
            CancellationToken cancellationToken = default)
        {
            if (lifecycle is null)
            {
                throw new ArgumentNullException(nameof(lifecycle));
            }
            ArrayOf<LifecycleChange> captured = [.. changes];
            IPreparedNodeManagerBatch prepared = await lifecycle.PrepareAsync(
                ConvertChanges(captured), cancellationToken).ConfigureAwait(false);
            return await PreparedLifecycleChanges.CreateAsync(prepared, captured).ConfigureAwait(false);
        }

        /// <summary>
        /// Prepares slot changes against an invocation's captured publication revision.
        /// A stale invocation is rejected before candidate factories run.
        /// </summary>
        /// <param name="publication">The captured publication invocation.</param>
        /// <param name="changes">The change set to prepare.</param>
        /// <param name="cancellationToken">Cancels preparation.</param>
        /// <returns>The unpublished owner.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="publication"/> is null.</exception>
        public static async ValueTask<IPreparedChanges> PrepareAsync(
            this INodeManagerPublication publication,
            ArrayOf<LifecycleChange> changes,
            CancellationToken cancellationToken = default)
        {
            if (publication is null)
            {
                throw new ArgumentNullException(nameof(publication));
            }
            ArrayOf<LifecycleChange> captured = [.. changes];
            IPreparedNodeManagerBatch prepared = await publication.PrepareAsync(
                ConvertChanges(captured), cancellationToken).ConfigureAwait(false);
            return await PreparedLifecycleChanges.CreateAsync(prepared, captured).ConfigureAwait(false);
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
            INodeManagerPublication publication = await lifecycle.CapturePublication()
                .BeginAsync(cancellationToken).ConfigureAwait(false);
            if (expectedRevision is { } revision &&
                (lifecycle.Revision != revision || !publication.IsCurrent))
            {
                await publication.DisposeAsync().ConfigureAwait(false);
                throw new InvalidOperationException("The expected lifecycle revision is no longer current.");
            }
            return new LifecyclePublicationWriter(publication);
        }

        /// <summary>
        /// Captures the input sequence without resolving slots outside preparation admission.
        /// </summary>
        /// <param name="changes">The caller's changes.</param>
        /// <returns>The shared transaction representation.</returns>
        /// <exception cref="ArgumentException">The change set is null, empty or contains a null entry.</exception>
        private static ArrayOf<NodeManagerBatchChange> ConvertChanges(ArrayOf<LifecycleChange> changes)
        {
            if (changes.IsNull || changes.Count == 0)
            {
                throw new ArgumentException("A change set must contain at least one change.", nameof(changes));
            }
            return (changes.ToArray() ?? []).Select(change => NodeManagerBatchChange.FromChange(
                change ?? throw new ArgumentException("A change cannot be null.", nameof(changes)))).ToArrayOf();
        }
    }
}
