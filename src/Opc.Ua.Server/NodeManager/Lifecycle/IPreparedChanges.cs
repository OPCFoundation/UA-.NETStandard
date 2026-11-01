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
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Owns optimistic revision evidence across sequential lifecycle change sets.
    /// It does not reserve mutation admission while factories or reconciliation callbacks run.
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
    }

    /// <summary>
    /// Owns unpublished candidates and typed read metadata until commit or asynchronous abort.
    /// </summary>
    public interface IPreparedChanges : IAsyncDisposable
    {
        /// <summary>
        /// Gets the targeted slots in change order, including additions and removals.
        /// </summary>
        ArrayOf<NodeManagerSlot> Slots { get; }

        /// <summary>
        /// Gets candidate generation handles in addition/replacement order, before they are live.
        /// </summary>
        ArrayOf<NodeManagerRegistration> Registrations { get; }

        /// <summary>
        /// Gets whether this unit crossed the routing publication commit point.
        /// </summary>
        bool IsCommitted { get; }

        /// <summary>
        /// Binds immutable metadata to the generation selected for a target slot.
        /// The image must name that exact owner; each slot may be bound only once before consumption.
        /// </summary>
        /// <param name="slot">The targeted slot, which must remain in the resulting view.</param>
        /// <param name="metadata">The generation-owned immutable image.</param>
        void SetMetadata(NodeManagerSlot slot, INodeManagerReadImage metadata);

        /// <summary>
        /// Decides once and publishes once, then returns committed reconciliation and retirement status.
        /// A null decision uses an in-memory decision. A stale unit is consumed before deciding, without retry.
        /// </summary>
        /// <param name="decideAsync">The external durable decision, or null for in-memory publication.</param>
        /// <param name="cancellationToken">Cancels before publication.</param>
        /// <returns>The authoritative committed result.</returns>
        ValueTask<LifecycleResult> CommitAsync(
            Func<CancellationToken, ValueTask>? decideAsync = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// Publishes prepared application state after the routing swap and before reconciliation.
        /// Failure is a committed issue; the callback must be idempotent for explicitly requested retry.
        /// </summary>
        /// <param name="decideAsync">The durable decision, or null for an in-memory decision.</param>
        /// <param name="publishCommittedState">
        /// The in-memory state publication, without I/O or a second decision.
        /// </param>
        /// <param name="cancellationToken">Cancels before publication.</param>
        /// <returns>The authoritative committed result.</returns>
        ValueTask<LifecycleResult> CommitAsync(
            Func<CancellationToken, ValueTask>? decideAsync,
            Action publishCommittedState,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Adapts existing publication ownership without adding another revision or lifetime owner.
    /// </summary>
    /// <param name="publication">The existing owner.</param>
    internal sealed class LifecyclePublicationWriter(INodeManagerPublication publication) : ILifecycleWriter
    {
        /// <inheritdoc/>
        public bool IsCurrent => publication.IsCurrent;

        /// <inheritdoc/>
        public ValueTask<IPreparedChanges> PrepareAsync(
            ArrayOf<LifecycleChange> changes, CancellationToken cancellationToken = default)
        {
            return publication.PrepareAsync(changes, cancellationToken);
        }

        /// <inheritdoc/>
        public async ValueTask<IPreparedChanges> PrepareReadImagesAsync(
            ArrayOf<INodeManagerReadImage> images, CancellationToken cancellationToken = default)
        {
            ArrayOf<INodeManagerReadImage> captured = [.. images];
            IPreparedNodeManagerBatch prepared = await publication.PrepareReadImagesAsync(
                captured, cancellationToken).ConfigureAwait(false);
            try
            {
                return new PreparedLifecycleChanges(prepared, captured);
            }
            catch (Exception failure) when (failure is not OutOfMemoryException)
            {
                await prepared.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }

        /// <inheritdoc/>
        public ValueTask DisposeAsync()
        {
            return publication.DisposeAsync();
        }
    }

    /// <summary>
    /// Adapts prepared ownership while accumulating slot-checked metadata before the underlying unit is consumed.
    /// </summary>
    internal sealed class PreparedLifecycleChanges : IPreparedChanges
    {
        /// <summary>
        /// Captures changed slots and candidate registrations without a second lifecycle owner.
        /// </summary>
        /// <param name="prepared">The underlying unpublished owner.</param>
        /// <param name="changes">The captured changes, in preparation order.</param>
        internal PreparedLifecycleChanges(IPreparedNodeManagerBatch prepared, ArrayOf<LifecycleChange> changes)
        {
            m_prepared = prepared;
            int expectedCandidates = (changes.ToArray() ?? []).Count(change => change.Factory is not null);
            if (prepared.Registrations.Count != expectedCandidates)
            {
                throw new InvalidOperationException(
                    "The provider returned a different number of candidate registrations.");
            }
            int candidateIndex = 0;
            Slots = (changes.ToArray() ?? []).Select(change =>
            {
                NodeManagerSlot? slot = change.Slot;
                if (change.Factory is not null)
                {
                    slot = prepared.Registrations[candidateIndex++].Slot;
                    if (change.Slot is { } target && !ReferenceEquals(slot, target))
                    {
                        throw new InvalidOperationException("The provider did not preserve the target slot.");
                    }
                }
                return slot ?? throw new InvalidOperationException("A prepared change has no target slot.");
            }).ToArrayOf();
        }

        /// <summary>
        /// Captures slots for an already-bound metadata-only unit.
        /// </summary>
        /// <param name="prepared">The underlying unpublished owner.</param>
        /// <param name="images">The images already bound during preparation.</param>
        internal PreparedLifecycleChanges(IPreparedNodeManagerBatch prepared, ArrayOf<INodeManagerReadImage> images)
        {
            m_prepared = prepared;
            Slots = (images.ToArray() ?? [])
                .Select(image => NodeManagerGenerationRegistry.GetOrCreate(image.Owner).Slot).ToArrayOf();
            m_metadataBound = true;
        }

        /// <inheritdoc/>
        public ArrayOf<NodeManagerSlot> Slots { get; }

        /// <inheritdoc/>
        public ArrayOf<NodeManagerRegistration> Registrations => m_prepared.Registrations;

        /// <inheritdoc/>
        public bool IsCommitted => m_prepared.IsCommitted;

        /// <summary>
        /// Transfers prepared ownership to the adapter, disposing malformed provider results on failure.
        /// </summary>
        /// <param name="prepared">The provider's unpublished owner.</param>
        /// <param name="changes">The changes whose candidate slots must be captured.</param>
        /// <returns>The adapter owning the prepared result.</returns>
        internal static async ValueTask<IPreparedChanges> CreateAsync(
            IPreparedNodeManagerBatch prepared, ArrayOf<LifecycleChange> changes)
        {
            try
            {
                return new PreparedLifecycleChanges(prepared, changes);
            }
            catch (Exception failure) when (failure is not OutOfMemoryException)
            {
                await prepared.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }

        /// <inheritdoc/>
        public void SetMetadata(NodeManagerSlot slot, INodeManagerReadImage metadata)
        {
            if (slot is null)
            {
                throw new ArgumentNullException(nameof(slot));
            }
            if (metadata is null)
            {
                throw new ArgumentNullException(nameof(metadata));
            }
            lock (m_state)
            {
                if (m_consumed || m_metadataBound)
                {
                    throw new InvalidOperationException("Metadata cannot be changed after binding or consumption.");
                }
                NodeManagerRegistration? candidate =
                    (Registrations.ToArray() ?? []).FirstOrDefault(value => value.Slot == slot);
                if (!Slots.Contains(slot) ||
                    candidate is null ||
                    !ReferenceEquals(candidate.NodeManager, metadata.Owner))
                {
                    throw new ArgumentException("Metadata must belong to a candidate generation in this unit.",
                        nameof(metadata));
                }
                if (!m_metadata.TryAdd(slot, metadata))
                {
                    throw new InvalidOperationException("Metadata is already bound for this slot.");
                }
            }
        }

        /// <inheritdoc/>
        public ValueTask<LifecycleResult> CommitAsync(
            Func<CancellationToken, ValueTask>? decideAsync = null, CancellationToken cancellationToken = default)
        {
            return CommitCoreAsync(decideAsync, null, cancellationToken);
        }

        /// <inheritdoc/>
        public ValueTask<LifecycleResult> CommitAsync(
            Func<CancellationToken, ValueTask>? decideAsync,
            Action publishCommittedState,
            CancellationToken cancellationToken = default)
        {
            return CommitCoreAsync(decideAsync,
                publishCommittedState ?? throw new ArgumentNullException(nameof(publishCommittedState)),
                cancellationToken);
        }

        /// <inheritdoc/>
        public ValueTask DisposeAsync()
        {
            lock (m_state)
            {
                m_consumed = true;
            }
            return m_prepared.DisposeAsync();
        }

        /// <summary>
        /// Binds accumulated metadata once and delegates decision/publication to the existing owner.
        /// </summary>
        /// <param name="decideAsync">The durable decision, if any.</param>
        /// <param name="publishCommittedState">The committed state callback, if any.</param>
        /// <param name="cancellationToken">Cancels before publication.</param>
        /// <returns>The underlying result's authoritative status owner.</returns>
        /// <exception cref="InvalidOperationException">The prepared changes have already been consumed.</exception>
        private async ValueTask<LifecycleResult> CommitCoreAsync(
            Func<CancellationToken, ValueTask>? decideAsync,
            Action? publishCommittedState,
            CancellationToken cancellationToken)
        {
            lock (m_state)
            {
                if (m_consumed)
                {
                    throw new InvalidOperationException("The prepared changes have already been consumed.");
                }
                m_consumed = true;
                if (m_metadata.Count != 0)
                {
                    m_prepared.BindReadImages(m_metadata.Values.ToArrayOf());
                }
                m_metadata.Clear();
            }
            decideAsync ??= static _ => default;
            NodeManagerBatchResult result = publishCommittedState is null
                ? await m_prepared.CommitAsync(decideAsync, cancellationToken).ConfigureAwait(false)
                : await m_prepared.CommitAsync(decideAsync, publishCommittedState, cancellationToken)
                    .ConfigureAwait(false);
            return result.CommittedResult;
        }

        /// <summary>The existing prepared lifetime owner.</summary>
        private readonly IPreparedNodeManagerBatch m_prepared;

        /// <summary>Serializes local metadata binding with consumption.</summary>
        private readonly Lock m_state = new();

        /// <summary>The typed images awaiting one batch binding.</summary>
        private readonly Dictionary<NodeManagerSlot, INodeManagerReadImage> m_metadata = [];

        /// <summary>Whether the adapter has admitted commit or disposal.</summary>
        private bool m_consumed;

        /// <summary>Whether preparation already bound all metadata.</summary>
        private readonly bool m_metadataBound;
    }
}
