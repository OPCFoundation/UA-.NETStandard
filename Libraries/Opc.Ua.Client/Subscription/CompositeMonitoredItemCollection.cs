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
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;

namespace Opc.Ua.Client.Subscriptions.MonitoredItems
{
    /// <summary>
    /// <para>
    /// <see cref="IMonitoredItemCollection"/> implementation backing
    /// a <see cref="LogicalSubscription"/>. Aggregates the
    /// per-partition collections of all underlying partition
    /// subscriptions so callers see one logical collection while the
    /// engine transparently splits monitored items across multiple
    /// server-side subscriptions.
    /// </para>
    /// <para>
    /// Behaviour summary:
    /// <list type="bullet">
    /// <item><description>Global name uniqueness — the composite
    /// rejects a <see cref="TryAdd"/> whose name already exists in
    /// any partition; the global name and client-handle indexes are
    /// hoisted into the composite for O(1) lookup.</description></item>
    /// <item><description>Strict-affinity placement — placement is
    /// driven by <see cref="PartitionPlacementPolicy"/>; once an
    /// affinity tag is pinned, every item in the group lands in the
    /// same partition or the add fails.</description></item>
    /// <item><description>On-demand partition creation — when no
    /// partition has capacity, the composite mints a new partition
    /// via the supplied factory and adds it to the shared partition
    /// list under the composite's lock.</description></item>
    /// <item><description>Single-partition fast path — when no
    /// partition factory is supplied (the legacy single-partition
    /// mode), the composite delegates to the primary partition's
    /// own collection.</description></item>
    /// </list>
    /// </para>
    /// </summary>
    internal sealed class CompositeMonitoredItemCollection : IMonitoredItemCollection
    {
        /// <summary>
        /// Construct a composite over the supplied (shared) partition
        /// list. The composite mutates <paramref name="partitions"/>
        /// in-place when a new partition is minted via
        /// <paramref name="partitionFactory"/>; the owning
        /// <see cref="LogicalSubscription"/> exposes the same list as
        /// its <see cref="ILogicalSubscription.Partitions"/> surface
        /// so the wrapper and the composite stay in sync.
        /// </summary>
        /// <param name="partitions">Shared partition list. Must
        /// contain at least the primary partition.</param>
        /// <param name="partitionLock">Shared lock guarding
        /// <paramref name="partitions"/>. Held during placement,
        /// name lookup, and (briefly) partition creation.</param>
        /// <param name="policy">Placement policy. <c>null</c> selects
        /// single-partition mode (no splits).</param>
        /// <param name="partitionFactory">Synchronous factory that
        /// constructs a new partition. <c>null</c> selects
        /// single-partition mode.</param>
        /// <param name="timeProvider">Time provider for idle-delete
        /// timer scheduling. Defaults to
        /// <see cref="TimeProvider.System"/> when <c>null</c>.</param>
        /// <param name="secondaryIdleTimeout">Idle timeout after
        /// which an empty secondary partition is removed by
        /// <paramref name="secondaryDisposer"/>. Pass
        /// <see cref="Timeout.InfiniteTimeSpan"/>
        /// (the default) to disable idle-delete.</param>
        /// <param name="secondaryDisposer">Async callback that
        /// removes the partition from the owning subscription
        /// manager's dispatch registry and disposes it. <c>null</c>
        /// disables idle-delete.</param>
        public CompositeMonitoredItemCollection(
            List<IManagedSubscription> partitions,
            object partitionLock,
            PartitionPlacementPolicy? policy = null,
            Func<IManagedSubscription>? partitionFactory = null,
            TimeProvider? timeProvider = null,
            TimeSpan? secondaryIdleTimeout = null,
            Func<IManagedSubscription, ValueTask>? secondaryDisposer = null)
        {
            if (partitions == null)
            {
                throw new ArgumentNullException(nameof(partitions));
            }

            if (partitions.Count == 0)
            {
                throw new ArgumentException(
                    "Composite requires at least the primary partition.",
                    nameof(partitions));
            }
            m_partitions = partitions;
            m_partitionLock = partitionLock ?? throw new ArgumentNullException(nameof(partitionLock));
            m_policy = policy;
            m_partitionFactory = partitionFactory;
            m_timeProvider = timeProvider ?? TimeProvider.System;
            m_secondaryIdleTimeout = secondaryIdleTimeout
                ?? Timeout.InfiniteTimeSpan;
            m_secondaryDisposer = secondaryDisposer;

            // Seed the policy with the pre-existing partitions so the
            // first placement decision sees a coherent counter map.
            if (m_policy != null)
            {
                foreach (IManagedSubscription partition in partitions)
                {
                    m_policy.OnPartitionAdded(partition);
                }
            }
        }

        /// <summary>
        /// Reactive-fallback entry point. Called by the owning
        /// <see cref="LogicalSubscription"/> when one of its
        /// partitions reports a
        /// <see cref="StatusCodes.BadTooManyMonitoredItems"/>
        /// CreateMonitoredItems result. Marks the partition no-grow
        /// in the placement policy so subsequent
        /// <see cref="TryAdd"/> calls skip it and fan out to a
        /// new partition instead. No-op when the composite is in
        /// single-partition mode (no policy / no factory).
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="partition"/> is <c>null</c>.</exception>
        internal void OnPartitionCapReached(IManagedSubscription partition)
        {
            if (partition == null)
            {
                throw new ArgumentNullException(nameof(partition));
            }
            if (m_policy == null)
            {
                return;
            }
            lock (m_partitionLock)
            {
                m_policy.OnPartitionCapReached(partition);
            }
        }

        /// <summary>
        /// Notify the composite that a preloaded partition (e.g. a
        /// secondary attached by
        /// <see cref="LogicalSubscription.AppendPreloadedPartition"/>
        /// during a multi-partition snapshot restore) has joined the
        /// wrapper's partition list. The composite seeds the
        /// placement policy with the new partition so future
        /// <see cref="TryAdd"/> calls account for its capacity.
        /// Must be called under <c>m_partitionLock</c>.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="partition"/> is <c>null</c>.</exception>
        internal void NotifyPreloadedPartitionAdded(IManagedSubscription partition)
        {
            if (partition == null)
            {
                throw new ArgumentNullException(nameof(partition));
            }
            m_policy?.OnPartitionAdded(partition);
        }

        /// <inheritdoc/>
        public uint Count
        {
            get
            {
                // Read paths always source from partitions so items
                // loaded straight into a partition (e.g. transfer
                // restore) are visible without re-indexing. Single-
                // partition fast path returns the primary's own
                // counter without taking the partition lock.
                if (IsSinglePartitionFastPath)
                {
                    return m_partitions[0].MonitoredItems.Count;
                }
                uint total = 0;
                foreach (IManagedSubscription partition in SnapshotPartitionsLocked())
                {
                    total += partition.MonitoredItems.Count;
                }
                return total;
            }
        }

        /// <inheritdoc/>
        public IEnumerable<IMonitoredItem> Items
        {
            get
            {
                if (IsSinglePartitionFastPath)
                {
                    return m_partitions[0].MonitoredItems.Items;
                }
                var snapshot = new List<IMonitoredItem>();
                foreach (IManagedSubscription partition in SnapshotPartitionsLocked())
                {
                    snapshot.AddRange(partition.MonitoredItems.Items);
                }
                return snapshot;
            }
        }

        /// <inheritdoc/>
        public bool TryGetMonitoredItemByClientHandle(uint clientHandle,
            [MaybeNullWhen(false)] out IMonitoredItem? monitoredItem)
        {
            if (IsSinglePartitionFastPath)
            {
                return m_partitions[0].MonitoredItems
                    .TryGetMonitoredItemByClientHandle(clientHandle, out monitoredItem);
            }
            // Routing index is the authoritative source for TryRemove
            // — see TryAdd / TryRemove. Falls back to partition scan
            // so loaded items the composite has not seen yet are
            // still reachable.
            lock (m_partitionLock)
            {
                if (m_byClientHandle.TryGetValue(clientHandle, out Entry entry))
                {
                    monitoredItem = entry.Item;
                    return true;
                }
            }
            foreach (IManagedSubscription partition in SnapshotPartitionsLocked())
            {
                if (partition.MonitoredItems
                    .TryGetMonitoredItemByClientHandle(clientHandle, out monitoredItem))
                {
                    return true;
                }
            }
            monitoredItem = null;
            return false;
        }

        /// <inheritdoc/>
        public bool TryGetMonitoredItemByName(string name,
            [MaybeNullWhen(false)] out IMonitoredItem? monitoredItem)
        {
            if (name == null)
            {
                throw new ArgumentNullException(nameof(name));
            }
            if (IsSinglePartitionFastPath)
            {
                return m_partitions[0].MonitoredItems
                    .TryGetMonitoredItemByName(name, out monitoredItem);
            }
            lock (m_partitionLock)
            {
                if (m_byName.TryGetValue(name, out Entry entry))
                {
                    monitoredItem = entry.Item;
                    return true;
                }
            }
            foreach (IManagedSubscription partition in SnapshotPartitionsLocked())
            {
                if (partition.MonitoredItems
                    .TryGetMonitoredItemByName(name, out monitoredItem))
                {
                    return true;
                }
            }
            monitoredItem = null;
            return false;
        }

        /// <summary>
        /// Take a stable snapshot of the partition list under the
        /// composite's lock so iteration is safe across concurrent
        /// partition appends. Single-partition shortcut to avoid
        /// allocating an array.
        /// </summary>
        private IReadOnlyList<IManagedSubscription> SnapshotPartitionsLocked()
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

        /// <inheritdoc/>
        public bool TryAdd(string name,
            IOptionsMonitor<MonitoredItemOptions> options,
            out IMonitoredItem? monitoredItem)
        {
            if (name == null)
            {
                throw new ArgumentNullException(nameof(name));
            }
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            // Single-partition fast path: directly delegate so the
            // composite is invisible for the common case and we do
            // not double-index names. The wrapper guarantees the
            // primary is non-null at this point.
            if (IsSinglePartitionFastPath)
            {
                return m_partitions[0].MonitoredItems.TryAdd(name, options, out monitoredItem);
            }

            MonitoredItemOptions snapshot = options.CurrentValue;
            lock (m_partitionLock)
            {
                if (m_byName.ContainsKey(name))
                {
                    monitoredItem = null;
                    return false;
                }
                // Loaded items (e.g. transfer restore) live in the
                // partition collections only, so cross-check before
                // proceeding to placement; the partition's own TryAdd
                // would also reject the duplicate but the early exit
                // avoids the unnecessary placement decision.
                foreach (IManagedSubscription partition in m_partitions)
                {
                    if (partition.MonitoredItems
                        .TryGetMonitoredItemByName(name, out _))
                    {
                        monitoredItem = null;
                        return false;
                    }
                }

                PlacementDecision decision = m_policy!.Decide(snapshot, m_partitions);
                if (decision.RejectStrictAffinityFull)
                {
                    monitoredItem = null;
                    return false;
                }
                if (decision.RejectMaxPartitionCountReached)
                {
                    // DoS guard: the wrapper has reached its hard
                    // partition-count cap. Refuse to grow further so
                    // a hostile server cannot amplify every
                    // Bad_TooManyMonitoredItems reply into unbounded
                    // partition fan-out.
                    monitoredItem = null;
                    return false;
                }

                IManagedSubscription chosen;
                if (decision.UseExistingPartition)
                {
                    chosen = decision.Partition!;
                }
                else
                {
                    // Mint a new partition. The factory is expected to
                    // return a partition that is already registered in
                    // any owning dispatch registry — the composite only
                    // records it in the wrapper's partition list.
                    chosen = m_partitionFactory!();
                    m_partitions.Add(chosen);
                    m_policy.OnPartitionAdded(chosen);
                }

                // Forward TryAdd to the chosen partition's own
                // collection. We deliberately hold the partition lock
                // through this call to keep capacity accounting and
                // the global name index consistent — the partition's
                // MonitoredItemManager.TryAdd is a quick local op
                // that cannot recurse back into this composite.
                if (!chosen.MonitoredItems.TryAdd(name, options, out IMonitoredItem? added) ||
                    added == null)
                {
                    monitoredItem = null;
                    return false;
                }
                var entry = new Entry(added, chosen);
                m_byName[name] = entry;
                m_byClientHandle[added.ClientHandle] = entry;
                m_policy.OnItemAdded(snapshot, chosen);
                monitoredItem = added;
                return true;
            }
        }

        /// <inheritdoc/>
        public bool TryRemove(uint clientHandle)
        {
            if (IsSinglePartitionFastPath)
            {
                return m_partitions[0].MonitoredItems.TryRemove(clientHandle);
            }
            IManagedSubscription? owner = null;
            lock (m_partitionLock)
            {
                if (m_byClientHandle.TryGetValue(clientHandle, out Entry entry))
                {
                    owner = entry.Partition;
                    string? name = entry.Item.Name;
                    m_byClientHandle.Remove(clientHandle);
                    m_byName.Remove(name);
                    m_policy!.OnItemRemoved(owner);
                }
                else
                {
                    // Loaded item: not in the composite index. Find
                    // the owning partition by scanning so TryRemove
                    // still routes correctly for transfer-restored
                    // items.
                    foreach (IManagedSubscription partition in m_partitions)
                    {
                        if (partition.MonitoredItems.TryGetMonitoredItemByClientHandle(
                            clientHandle, out _))
                        {
                            owner = partition;
                            break;
                        }
                    }
                    if (owner == null)
                    {
                        return false;
                    }
                }
            }
            // Partition-side TryRemove runs outside the composite
            // lock so its async state-machine signalling never
            // contends with composite operations.
            bool removed = owner.MonitoredItems.TryRemove(clientHandle);
            if (removed)
            {
                MaybeArmIdleTimer(owner);
            }
            return removed;
        }

        /// <summary>
        /// Arm the idle-delete timer for a secondary partition that
        /// just became empty. No-op when idle-delete is not
        /// configured, when <paramref name="partition"/> is the
        /// primary, or when the partition still has items. The
        /// primary partition is never deleted while the logical
        /// subscription is alive so its server-side identifier stays
        /// stable for log/correlation consumers.
        /// </summary>
        private void MaybeArmIdleTimer(IManagedSubscription partition)
        {
            if (m_secondaryDisposer == null ||
                m_secondaryIdleTimeout == Timeout.InfiniteTimeSpan)
            {
                return;
            }
            lock (m_partitionLock)
            {
                if (m_partitions.Count == 0 ||
                    ReferenceEquals(m_partitions[0], partition))
                {
                    return;
                }
                if (partition.MonitoredItems.Count > 0)
                {
                    return;
                }
                // Replace any in-flight timer for the same partition
                // so successive empty→non-empty→empty transitions
                // restart the countdown rather than firing early.
                if (m_idleTimers.Remove(partition, out ITimer? existing))
                {
                    existing.Dispose();
                }
                m_idleTimers[partition] = m_timeProvider.CreateTimer(
                    _ => RunIdleDelete(partition),
                    null,
                    m_secondaryIdleTimeout,
                    Timeout.InfiniteTimeSpan);
            }
        }

        private void RunIdleDelete(IManagedSubscription partition)
        {
            _ = Task.Run(async () =>
            {
                Func<IManagedSubscription, ValueTask>? disposer;
                lock (m_partitionLock)
                {
                    if (m_idleTimers.Remove(partition, out ITimer? timer))
                    {
                        timer.Dispose();
                    }
                    // Re-check the gate now that we have the lock:
                    // an item add between timer-arm and timer-fire
                    // must keep the partition alive.
                    if (!m_partitions.Contains(partition) ||
                        partition.MonitoredItems.Count > 0 ||
                        (m_partitions.Count > 0 &&
                            ReferenceEquals(m_partitions[0], partition)))
                    {
                        return;
                    }
                    m_partitions.Remove(partition);
                    m_policy?.OnPartitionRemoved(partition);
                    // Drop any composite-side index entries that
                    // referenced this partition (should be none at
                    // this point because Count was zero, but a
                    // belt-and-braces sweep guards against late race
                    // recoveries that re-added an entry).
                    List<string>? names = null;
                    List<uint>? handles = null;
                    foreach (KeyValuePair<string, Entry> n in m_byName)
                    {
                        if (ReferenceEquals(n.Value.Partition, partition))
                        {
                            names ??= [];
                            names.Add(n.Key);
                        }
                    }
                    foreach (KeyValuePair<uint, Entry> h in m_byClientHandle)
                    {
                        if (ReferenceEquals(h.Value.Partition, partition))
                        {
                            handles ??= [];
                            handles.Add(h.Key);
                        }
                    }
                    if (names != null)
                    {
                        foreach (string n in names)
                        {
                            m_byName.Remove(n);
                        }
                    }
                    if (handles != null)
                    {
                        foreach (uint h in handles)
                        {
                            m_byClientHandle.Remove(h);
                        }
                    }
                    disposer = m_secondaryDisposer;
                }
                if (disposer != null)
                {
                    try
                    {
                        await disposer(partition).ConfigureAwait(false);
                    }
                    catch
                    {
                        // Disposal is best-effort; the manager will
                        // eventually reclaim the partition on session
                        // teardown if it leaks here.
                    }
                }
            });
        }

        /// <summary>
        /// Cancel and dispose every armed idle-delete timer. Called
        /// by the wrapper's <c>DisposeAsync</c> so timers do not
        /// outlive the logical subscription and fire against a
        /// torn-down partition list.
        /// </summary>
        internal void DisposeIdleTimers()
        {
            lock (m_partitionLock)
            {
                foreach (ITimer t in m_idleTimers.Values)
                {
                    t.Dispose();
                }
                m_idleTimers.Clear();
            }
        }

        /// <inheritdoc/>
        public IReadOnlyList<IMonitoredItem> Update(
            IReadOnlyList<(string Name, IOptionsMonitor<MonitoredItemOptions> Options)> state)
        {
            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }
            if (IsSinglePartitionFastPath)
            {
                return m_partitions[0].MonitoredItems.Update(state);
            }

            // Diff against the current global view:
            //   * names in `state` not yet known → TryAdd
            //   * names in `state` already known → leave in place
            //     (per-item option updates are surfaced through the
            //     item's own IOptionsMonitor by the caller)
            //   * names absent from `state` → TryRemove
            var keep = new HashSet<string>(state.Count, StringComparer.Ordinal);
            var result = new List<IMonitoredItem>(state.Count);
            foreach ((string itemName, IOptionsMonitor<MonitoredItemOptions> itemOptions) in state)
            {
                keep.Add(itemName);
                if (TryGetMonitoredItemByName(itemName, out IMonitoredItem? existing) &&
                    existing != null)
                {
                    result.Add(existing);
                    continue;
                }
                if (TryAdd(itemName, itemOptions, out IMonitoredItem? added) && added != null)
                {
                    result.Add(added);
                }
            }

            // Remove anything that fell out of the target state.
            List<uint>? toRemove = null;
            lock (m_partitionLock)
            {
                foreach (KeyValuePair<string, Entry> entry in m_byName)
                {
                    if (!keep.Contains(entry.Key))
                    {
                        toRemove ??= [];
                        toRemove.Add(entry.Value.Item.ClientHandle);
                    }
                }
            }
            if (toRemove != null)
            {
                foreach (uint handle in toRemove)
                {
                    TryRemove(handle);
                }
            }
            return result;
        }

        /// <summary>
        /// True when the composite is operating in single-partition
        /// mode — no partition factory and no placement policy were
        /// supplied. The fast path bypasses the composite's global
        /// indexes so behaviour is identical to the pre-wrapper
        /// engine. Once a factory/policy is supplied, every operation
        /// flows through the composite's own indexes regardless of
        /// how many partitions are currently registered.
        /// </summary>
        private bool IsSinglePartitionFastPath
            => m_partitionFactory == null || m_policy == null;

        private readonly List<IManagedSubscription> m_partitions;
        private readonly object m_partitionLock;
        private readonly PartitionPlacementPolicy? m_policy;
        private readonly Func<IManagedSubscription>? m_partitionFactory;
        private readonly TimeProvider m_timeProvider;
        private readonly TimeSpan m_secondaryIdleTimeout;
        private readonly Func<IManagedSubscription, ValueTask>? m_secondaryDisposer;
        private readonly Dictionary<string, Entry> m_byName = new(StringComparer.Ordinal);
        private readonly Dictionary<uint, Entry> m_byClientHandle = [];
        private readonly Dictionary<IManagedSubscription, ITimer> m_idleTimers = [];

        private readonly record struct Entry(IMonitoredItem Item, IManagedSubscription Partition);
    }
}
