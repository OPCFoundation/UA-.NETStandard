/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Client.Subscriptions.MonitoredItems;

namespace Opc.Ua.Client.Subscriptions
{
    /// <summary>
    /// <para>
    /// V2 logical-subscription wrapper. Represents one logical
    /// subscription to the consumer while transparently owning one
    /// or more underlying server-side partition subscriptions (each
    /// an <see cref="IManagedSubscription"/>) when the per-partition
    /// monitored-item cap would be exceeded.
    /// </para>
    /// <para>
    /// In the foundational state landed by the first milestone the
    /// wrapper supports exactly one primary partition; subsequent
    /// milestones extend it with on-demand partition creation, a
    /// composite monitored-item collection, partition-affinity
    /// placement, dispatch serialisation, and the idle-delete /
    /// recreate / transfer / save-load fan-out paths.
    /// </para>
    /// </summary>
    internal sealed class LogicalSubscription : ILogicalSubscription
    {
        /// <summary>
        /// Construct a logical subscription that wraps a single
        /// pre-created primary partition. All
        /// <see cref="ISubscription"/> members delegate to the
        /// primary unchanged so the single-partition fast path is
        /// indistinguishable from the pre-wrapper behaviour. This
        /// constructor is intended for callers (and tests) that do
        /// not need on-demand partition creation.
        /// </summary>
        /// <param name="primary">The pre-created server-side
        /// subscription that serves as this logical subscription's
        /// primary partition. Must not be <c>null</c>.</param>
        public LogicalSubscription(IManagedSubscription primary)
            : this(primary, placementPolicy: null, partitionFactory: null)
        {
        }

        /// <summary>
        /// Construct a logical subscription that wraps a primary
        /// partition and is permitted to grow secondary partitions
        /// on demand. When <paramref name="placementPolicy"/> and
        /// <paramref name="partitionFactory"/> are both supplied,
        /// <see cref="MonitoredItems"/> returns a composite
        /// collection that uses the policy to place items and the
        /// factory to mint new partitions when no existing partition
        /// has capacity. When either is <c>null</c> the wrapper
        /// degrades to single-partition mode so callers can opt out.
        /// </summary>
        /// <param name="primary">Primary partition (required).</param>
        /// <param name="placementPolicy">Optional placement policy
        /// driving the composite collection. Required for
        /// multi-partition operation.</param>
        /// <param name="partitionFactory">Optional synchronous
        /// factory invoked when a new partition is required. The
        /// factory is responsible for any registration with the
        /// owning <see cref="SubscriptionManager"/>'s dispatch
        /// registry. Required for multi-partition operation.</param>
        /// <param name="timeProvider">Optional time provider used to
        /// schedule the secondary-partition idle-delete timer.
        /// Defaults to <see cref="TimeProvider.System"/>.</param>
        /// <param name="secondaryIdleTimeout">Optional idle timeout
        /// applied to secondary partitions; secondary partitions
        /// that have had no monitored items for this long are removed
        /// via <paramref name="secondaryDisposer"/>. Pass
        /// <see cref="System.Threading.Timeout.InfiniteTimeSpan"/>
        /// (the default) to disable idle-delete.</param>
        /// <param name="secondaryDisposer">Async callback to dispose
        /// a secondary partition once its idle timeout elapses. The
        /// callback is responsible for removing the partition from
        /// the owning manager's dispatch registry. <c>null</c>
        /// disables idle-delete.</param>
        public LogicalSubscription(
            IManagedSubscription primary,
            PartitionPlacementPolicy? placementPolicy,
            Func<IManagedSubscription>? partitionFactory,
            TimeProvider? timeProvider = null,
            TimeSpan? secondaryIdleTimeout = null,
            Func<IManagedSubscription, ValueTask>? secondaryDisposer = null)
        {
            if (primary == null)
            {
                throw new ArgumentNullException(nameof(primary));
            }
            m_partitions = [primary];
            m_partitionLock = new object();
            m_monitoredItems = new CompositeMonitoredItemCollection(
                m_partitions,
                m_partitionLock,
                placementPolicy,
                partitionFactory == null ? null : () => AppendPartition(partitionFactory),
                timeProvider,
                secondaryIdleTimeout,
                secondaryDisposer);
        }

        /// <summary>
        /// Attach a forwarding handler whose lifetime is tied to
        /// this logical subscription. Disposed on
        /// <see cref="DisposeAsync"/> so the semaphore-based serial
        /// dispatch primitive is released along with the wrapper.
        /// Called by <see cref="SubscriptionManager.Add"/> when
        /// running in multi-partition mode.
        /// </summary>
        internal void AttachForwardingHandler(PartitionForwardingHandler handler)
        {
            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler));
            }
            m_forwardingHandler = handler;
        }

        /// <inheritdoc/>
        public bool Created
        {
            get
            {
                // Per-rubber-duck guidance: a logical subscription is
                // considered Created only when every partition has been
                // created on the server. With one partition this collapses
                // to the partition's own Created flag.
                foreach (IManagedSubscription partition in SnapshotPartitions())
                {
                    if (!partition.Created)
                    {
                        return false;
                    }
                }
                return m_partitions.Count > 0;
            }
        }

        /// <inheritdoc/>
        public TimeSpan CurrentPublishingInterval => Primary.CurrentPublishingInterval;

        /// <inheritdoc/>
        public byte CurrentPriority => Primary.CurrentPriority;

        /// <inheritdoc/>
        public uint CurrentLifetimeCount => Primary.CurrentLifetimeCount;

        /// <inheritdoc/>
        public uint CurrentKeepAliveCount => Primary.CurrentKeepAliveCount;

        /// <inheritdoc/>
        public bool CurrentPublishingEnabled => Primary.CurrentPublishingEnabled;

        /// <inheritdoc/>
        public uint CurrentMaxNotificationsPerPublish
            => Primary.CurrentMaxNotificationsPerPublish;

        /// <inheritdoc/>
        public IMonitoredItemCollection MonitoredItems => m_monitoredItems;

        /// <inheritdoc/>
        public long MissingMessageCount
        {
            get
            {
                long total = 0;
                foreach (IManagedSubscription partition in SnapshotPartitions())
                {
                    total += partition.MissingMessageCount;
                }
                return total;
            }
        }

        /// <inheritdoc/>
        public long RepublishMessageCount
        {
            get
            {
                long total = 0;
                foreach (IManagedSubscription partition in SnapshotPartitions())
                {
                    total += partition.RepublishMessageCount;
                }
                return total;
            }
        }

        /// <summary>
        /// Server-assigned subscription id of the primary partition.
        /// Mirrors <see cref="IMessageProcessor.Id"/> on the primary;
        /// remains stable for the lifetime of the logical
        /// subscription so callers that log or correlate by id see a
        /// consistent value even if secondary partitions are added
        /// or removed.
        /// </summary>
        public uint ServerId => Primary.Id;

        /// <inheritdoc/>
        public int PartitionCount
        {
            get
            {
                lock (m_partitionLock)
                {
                    return m_partitions.Count;
                }
            }
        }

        /// <inheritdoc/>
        public IReadOnlyList<uint> PartitionIds
        {
            get
            {
                lock (m_partitionLock)
                {
                    if (m_partitions.Count == 1)
                    {
                        return [m_partitions[0].Id];
                    }
                    var ids = new uint[m_partitions.Count];
                    for (int i = 0; i < m_partitions.Count; i++)
                    {
                        ids[i] = m_partitions[i].Id;
                    }
                    return ids;
                }
            }
        }

        /// <inheritdoc/>
        public IReadOnlyList<IManagedSubscription> Partitions
        {
            get
            {
                // Defensive snapshot — never hand out the live list so
                // callers cannot observe a torn read while a new
                // partition is being added under m_partitionLock.
                return SnapshotPartitions();
            }
        }

        /// <inheritdoc/>
        public async ValueTask ConditionRefreshAsync(CancellationToken ct = default)
        {
            // Fan ConditionRefresh out to every partition so the
            // logical subscription's view of conditions is fully
            // refreshed even when monitored items are split across
            // partitions. Per OPC UA Part 9 §5.5.7 the call is
            // per-subscription, so each partition must issue its own
            // ConditionRefresh request.
            foreach (IManagedSubscription partition in SnapshotPartitions())
            {
                await partition.ConditionRefreshAsync(ct).ConfigureAwait(false);
            }
        }

        /// <inheritdoc/>
        public async ValueTask<TimeSpan> SetAsDurableAsync(
            TimeSpan lifetime,
            CancellationToken ct = default)
        {
            // Persist the intent so on-demand secondary partitions
            // created later inherit the same durable lifetime via the
            // partition state-machine's OnAfterCreateAsync hook,
            // which guarantees the OPC UA Part 4 §5.13.9 ordering
            // requirement (SetSubscriptionDurable must precede any
            // CreateMonitoredItems).
            m_durableLifetime = lifetime;
            TimeSpan minRevised = TimeSpan.MaxValue;
            foreach (IManagedSubscription partition in SnapshotPartitions())
            {
                // For partitions that are already Created, apply the
                // durable call inline so the caller's await returns
                // only once the server has accepted (or rejected) it.
                // For partitions that are still racing through
                // Create (or that get re-created on reconnect), wire
                // the OnAfterCreateAsync hook so the state machine
                // applies SetSubscriptionDurable between the
                // CreateSubscription response and the first
                // CreateMonitoredItems request.
                InstallDurableHook(partition, lifetime);
                if (partition.Created)
                {
                    TimeSpan revised = await partition.SetAsDurableAsync(
                        lifetime, ct).ConfigureAwait(false);
                    if (revised < minRevised)
                    {
                        minRevised = revised;
                    }
                }
            }
            return minRevised == TimeSpan.MaxValue ? lifetime : minRevised;
        }

        /// <inheritdoc/>
        public async ValueTask RecreateAsync(CancellationToken ct = default)
        {
            foreach (IManagedSubscription partition in SnapshotPartitions())
            {
                await partition.RecreateAsync(ct).ConfigureAwait(false);
            }
        }

        /// <inheritdoc/>
        public void NotifySubscriptionManagerPaused(bool paused)
        {
            foreach (IManagedSubscription partition in SnapshotPartitions())
            {
                partition.NotifySubscriptionManagerPaused(paused);
            }
        }

        /// <summary>
        /// Capture an immutable snapshot of every partition that
        /// backs this logical subscription. The returned list is
        /// ordered primary-first (index <c>0</c>) followed by
        /// secondary partitions in mint order; every snapshot is
        /// stamped with the wrapper's stable <c>LogicalGroupId</c>
        /// and its <c>PartitionIndex</c> so a later
        /// <c>LoadAsync</c> can regroup them into one
        /// <see cref="LogicalSubscription"/> via
        /// <c>SubscriptionManager.RestoreGroupAsync</c>.
        /// </summary>
        /// <remarks>
        /// Single-partition wrappers return a one-element list whose
        /// element matches <see cref="Snapshot"/>.
        /// </remarks>
        public IReadOnlyList<SubscriptionStateSnapshot> SnapshotAllPartitions()
        {
            IReadOnlyList<IManagedSubscription> partitions = SnapshotPartitions();
            string groupId = GetOrCreateLogicalGroupId();
            var result = new List<SubscriptionStateSnapshot>(partitions.Count);
            for (int i = 0; i < partitions.Count; i++)
            {
                if (partitions[i] is Subscription concrete)
                {
                    SubscriptionStateSnapshot snap = concrete.Snapshot();
                    result.Add(snap with
                    {
                        LogicalGroupId = groupId,
                        PartitionIndex = i
                    });
                }
            }
            return result;
        }

        /// <summary>
        /// Capture an immutable snapshot of this logical subscription's
        /// primary partition, including its configuration, server-side
        /// identifier, and the per-item state. The returned snapshot
        /// is stamped with the wrapper's <c>LogicalGroupId</c> and
        /// <c>PartitionIndex=0</c> so it round-trips through a
        /// group-aware <c>LoadAsync</c> as a single-partition logical
        /// subscription. Use <see cref="SnapshotAllPartitions"/> when
        /// you need every partition's state.
        /// </summary>
        public SubscriptionStateSnapshot Snapshot()
        {
            if (Primary is Subscription concrete)
            {
                SubscriptionStateSnapshot snap = concrete.Snapshot();
                return snap with
                {
                    LogicalGroupId = GetOrCreateLogicalGroupId(),
                    PartitionIndex = 0
                };
            }
            throw new InvalidOperationException(
                "Primary partition does not support snapshots.");
        }

        /// <summary>
        /// Stable <c>LogicalGroupId</c> assigned on first request and
        /// cached for the wrapper's lifetime. The id is a fresh GUID
        /// so a snapshot taken from one logical subscription cannot
        /// collide with a snapshot taken from another wrapper inside
        /// the same session (or any other session). Lazy generation
        /// avoids handing out an id when the caller never asks for
        /// snapshots.
        /// </summary>
        private string GetOrCreateLogicalGroupId()
        {
            string? existing = m_logicalGroupId;
            if (existing != null)
            {
                return existing;
            }
            string fresh = Guid.NewGuid().ToString("N");
            string? prior = Interlocked.CompareExchange(
                ref m_logicalGroupId, fresh, null);
            return prior ?? fresh;
        }

        /// <summary>
        /// Configure triggering relationships between monitored items
        /// in this logical subscription. Per OPC UA Part 4 §5.13.6
        /// the OPC UA SetTriggering service is scoped to a single
        /// server-side subscription. When the logical subscription
        /// spans multiple partitions, this method rejects calls that
        /// would cross a partition boundary — every linked item must
        /// share the same partition as the triggering item. Callers
        /// can guarantee co-location by pinning items via
        /// <see cref="MonitoredItems.MonitoredItemOptions.Affinity"/>.
        /// </summary>
        /// <exception cref="ArgumentException">When the triggering
        /// item or any of the linked items resolves to a different
        /// partition.</exception>
        public ValueTask<SetTriggeringResponse> SetTriggeringAsync(
            uint triggeringItemClientHandle,
            IReadOnlyList<uint> linksToAdd,
            IReadOnlyList<uint> linksToRemove,
            CancellationToken ct = default)
        {
            if (linksToAdd == null)
            {
                throw new ArgumentNullException(nameof(linksToAdd));
            }
            if (linksToRemove == null)
            {
                throw new ArgumentNullException(nameof(linksToRemove));
            }

            // Resolve the triggering item's owning partition. If we
            // only have one partition (the common case) the answer is
            // trivially the primary and we can skip the per-link
            // boundary check.
            IReadOnlyList<IManagedSubscription> snapshot = SnapshotPartitions();
            IManagedSubscription owner = ResolveOwningPartition(
                triggeringItemClientHandle, snapshot)
                ?? throw new ArgumentException(
                    $"Triggering item with client handle {triggeringItemClientHandle} " +
                    "is not part of this subscription.",
                    nameof(triggeringItemClientHandle));

            if (snapshot.Count > 1)
            {
                EnsureSamePartition(owner, linksToAdd, snapshot,
                    nameof(linksToAdd));
                EnsureSamePartition(owner, linksToRemove, snapshot,
                    nameof(linksToRemove));
            }

            if (owner is Subscription concrete)
            {
                return concrete.SetTriggeringAsync(
                    triggeringItemClientHandle, linksToAdd, linksToRemove, ct);
            }
            throw new InvalidOperationException(
                "Owning partition does not support SetTriggering.");
        }

        private static IManagedSubscription? ResolveOwningPartition(
            uint clientHandle,
            IReadOnlyList<IManagedSubscription> partitions)
        {
            foreach (IManagedSubscription partition in partitions)
            {
                if (partition.MonitoredItems
                    .TryGetMonitoredItemByClientHandle(clientHandle, out _))
                {
                    return partition;
                }
            }
            return null;
        }

        private static void EnsureSamePartition(
            IManagedSubscription owner,
            IReadOnlyList<uint> handles,
            IReadOnlyList<IManagedSubscription> partitions,
            string paramName)
        {
            foreach (uint handle in handles)
            {
                IManagedSubscription? itemPartition = ResolveOwningPartition(
                    handle, partitions);
                if (itemPartition == null)
                {
                    throw new ArgumentException(
                        $"Monitored item with client handle {handle} " +
                        "is not part of this subscription.",
                        paramName);
                }
                if (!ReferenceEquals(itemPartition, owner))
                {
                    throw new ArgumentException(
                        $"Monitored item with client handle {handle} " +
                        "is in a different partition than the triggering item. " +
                        "Use MonitoredItemOptions.Affinity to pin triggering " +
                        "and triggered items into the same partition.",
                        paramName);
                }
            }
        }

        /// <summary>
        /// Append a preloaded partition to this wrapper. Used by
        /// <see cref="SubscriptionManager.RestoreGroupAsync"/> when
        /// restoring a multi-partition snapshot group: the primary
        /// is loaded via the standard restore path and each
        /// secondary is built externally (with its
        /// <see cref="SubscriptionLoadState"/>), then absorbed here
        /// so the wrapper's partition list, composite-collection
        /// policy, and durable-hook intent all see the new partition.
        /// </summary>
        /// <param name="partition">Fully-constructed secondary
        /// partition. Must not already be a member of the wrapper.</param>
        /// <exception cref="ArgumentNullException">When
        /// <paramref name="partition"/> is <c>null</c>.</exception>
        /// <exception cref="InvalidOperationException">When the
        /// wrapper was constructed in single-partition mode (no
        /// placement policy / factory).</exception>
        internal void AppendPreloadedPartition(IManagedSubscription partition)
        {
            if (partition == null)
            {
                throw new ArgumentNullException(nameof(partition));
            }
            lock (m_partitionLock)
            {
                if (m_partitions.Contains(partition))
                {
                    return;
                }
                m_partitions.Add(partition);
                m_monitoredItems.NotifyPreloadedPartitionAdded(partition);
            }
            // Propagate durable intent to the new partition just
            // like the on-demand factory path would; preloaded
            // partitions go through the same hook so the
            // SetSubscriptionDurable ordering rule is satisfied on
            // recreate.
            TryApplyDurableToNewPartition(partition);
        }

        /// <inheritdoc/>
        public ValueTask DisposeAsync()
        {
            return DisposeCoreAsync();
        }

        private async ValueTask DisposeCoreAsync()
        {
            // Stop any armed secondary-partition idle timers first so
            // they cannot fire against partitions we are tearing
            // down.
            m_monitoredItems.DisposeIdleTimers();
            // Dispose partitions in reverse-add order; secondary partitions
            // (added on demand) hold references back to the primary's
            // notification handler in subsequent milestones, so removing
            // them first avoids the secondary's dispatch worker observing
            // a disposed primary handler.
            IReadOnlyList<IManagedSubscription> snapshot = SnapshotPartitions();
            for (int i = snapshot.Count - 1; i >= 0; i--)
            {
                await snapshot[i].DisposeAsync().ConfigureAwait(false);
            }
            // Forwarding handler (if any) outlives the partitions so
            // their in-flight notification callbacks can run their
            // semaphore release before the primitive is freed.
            m_forwardingHandler?.Dispose();
        }

        /// <summary>
        /// Reactive-fallback entry point. Called by a partition's
        /// <see cref="Subscription.OnPartitionCapReached"/> hook when
        /// the server rejects a CreateMonitoredItems request with
        /// <see cref="StatusCodes.BadTooManyMonitoredItems"/>. Marks
        /// the offending partition no-grow so subsequent placements
        /// land in a different (or freshly minted) partition. The
        /// already-failed item itself is surfaced to the caller via
        /// the standard per-item error path.
        /// </summary>
        internal void OnPartitionCapReached(IManagedSubscription partition)
        {
            if (partition == null)
            {
                throw new ArgumentNullException(nameof(partition));
            }
            m_monitoredItems.OnPartitionCapReached(partition);
        }

        /// <summary>
        /// Synchronously install the durable-apply hook on a
        /// freshly-minted secondary partition so its
        /// <see cref="Subscription.OnAfterCreateAsync"/> callback
        /// invokes <see cref="Subscription.SetAsDurableAsync"/>
        /// between the partition's <c>CreateSubscription</c>
        /// response and its first <c>CreateMonitoredItems</c>
        /// request — satisfying OPC UA Part 4 §5.13.9 ordering. When
        /// no durable intent has been recorded the call is a no-op.
        /// </summary>
        internal void TryApplyDurableToNewPartition(IManagedSubscription partition)
        {
            if (partition == null)
            {
                throw new ArgumentNullException(nameof(partition));
            }
            TimeSpan? lifetime = m_durableLifetime;
            if (lifetime == null)
            {
                return;
            }
            InstallDurableHook(partition, lifetime.Value);
        }

        /// <summary>
        /// Wire the partition's
        /// <see cref="Subscription.OnAfterCreateAsync"/> hook to
        /// invoke <see cref="Subscription.SetAsDurableAsync"/>. The
        /// hook fires once per partition lifetime — the state
        /// machine clears it after the first Create — so the wrapper
        /// reinstalls it on every <see cref="SetAsDurableAsync"/>
        /// call to cover reconnect / recreate cycles.
        /// </summary>
        private static void InstallDurableHook(
            IManagedSubscription partition, TimeSpan lifetime)
        {
            if (partition is Subscription concrete)
            {
                concrete.OnAfterCreateAsync = async ct =>
                {
                    // Discard the server-revised lifetime; the
                    // wrapper caches its own intent and reports the
                    // min revised lifetime from the synchronous Apply
                    // path. Hook failures are caught by the state
                    // machine and logged.
                    _ = await partition.SetAsDurableAsync(lifetime, ct)
                        .ConfigureAwait(false);
                };
            }
        }

        /// <summary>
        /// Stable snapshot of the partition list under
        /// <see cref="m_partitionLock"/>. Used by every iterator on
        /// the wrapper so an in-flight partition append from a
        /// concurrent <see cref="CompositeMonitoredItemCollection.TryAdd"/>
        /// cannot tear the read.
        /// </summary>
        private IReadOnlyList<IManagedSubscription> SnapshotPartitions()
        {
            lock (m_partitionLock)
            {
                if (m_partitions.Count == 1)
                {
                    return [m_partitions[0]];
                }
                return [.. m_partitions];
            }
        }

        /// <summary>
        /// Append-partition hook handed to the composite collection's
        /// factory. Wraps the caller-supplied factory so the wrapper's
        /// partition lock guards the actual List&lt;T&gt;.Add — without
        /// the wrap a concurrent <see cref="SnapshotPartitions"/>
        /// reader could see a half-mutated list.
        /// </summary>
        private IManagedSubscription AppendPartition(
            Func<IManagedSubscription> innerFactory)
        {
            IManagedSubscription added = innerFactory();
            // The actual List<T>.Add is performed by the composite under
            // the same m_partitionLock; this helper exists so future
            // milestones can hook side effects (notification handler
            // attach, durable apply, etc.) on append without changing
            // the composite contract.
            return added;
        }

        /// <summary>
        /// Primary partition that backs this logical subscription.
        /// Exposed to test assemblies (via <c>InternalsVisibleTo</c>)
        /// so partition-internal API such as
        /// <c>LastSequenceNumberProcessed</c> remains reachable
        /// through the wrapper for legacy assertions; production
        /// callers should use the public <see cref="ISubscription"/>
        /// or <see cref="IPartitionedSubscription"/> surface.
        /// </summary>
        internal IManagedSubscription Primary => m_partitions[0];

        private readonly List<IManagedSubscription> m_partitions;
        private readonly object m_partitionLock;
        private readonly CompositeMonitoredItemCollection m_monitoredItems;
        private PartitionForwardingHandler? m_forwardingHandler;
        private TimeSpan? m_durableLifetime;
        private string? m_logicalGroupId;
    }
}
