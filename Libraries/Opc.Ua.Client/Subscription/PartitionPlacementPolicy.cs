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

namespace Opc.Ua.Client.Subscriptions.MonitoredItems
{
    /// <summary>
    /// <para>
    /// First-fit placement decisions for monitored items added to a
    /// <see cref="LogicalSubscription"/>'s
    /// <see cref="CompositeMonitoredItemCollection"/>. The policy is
    /// owned by the composite collection and consulted under the
    /// composite's lock, so all members assume single-threaded
    /// access.
    /// </para>
    /// <para>
    /// Strict-affinity contract: items tagged with a non-null
    /// <see cref="MonitoredItemOptions.Affinity"/> stay pinned to
    /// the partition that received the first item in the group.
    /// Subsequent items in the same group land in the same partition
    /// (so spec-only-within-subscription features like
    /// <c>SetTriggering</c> remain usable). Once the pinned partition
    /// reaches the per-partition cap, further additions in the same
    /// affinity group are rejected via
    /// <see cref="PlacementDecision.RejectStrictAffinityFull"/>
    /// rather than silently splitting the group.
    /// </para>
    /// <para>
    /// Reactive cap fallback: callers report
    /// <c>Bad_TooManyMonitoredItems</c> outcomes via
    /// <see cref="OnPartitionCapReached"/>. The policy then marks
    /// the partition "no-grow" so future placements skip it and the
    /// next add mints a fresh partition. The per-partition cap
    /// (<see cref="MaxItemsPerPartition"/>) itself is not lowered —
    /// fresh partitions still start with the original cap, which is
    /// the right behaviour when the rejection was driven by a
    /// transient or partition-local condition (concurrent server-
    /// side activity, per-handler limits, etc.) rather than a hard
    /// per-subscription limit. This handles servers whose actual
    /// limit is lower than the advertised
    /// <see cref="ServerCapabilities.MaxMonitoredItemsPerSubscription"/>.
    /// </para>
    /// <para>
    /// DoS guard: the policy enforces
    /// <see cref="MaxPartitionCount"/> as a hard upper bound on the
    /// number of partitions a single logical subscription is
    /// allowed to grow to. Once that count is reached the policy
    /// stops minting and returns
    /// <see cref="PlacementDecision.RejectMaxPartitionCountReached"/>
    /// — preventing a hostile or buggy server from amplifying every
    /// <c>Bad_TooManyMonitoredItems</c> reply into unbounded
    /// client-side memory and server-side subscription handle
    /// growth.
    /// </para>
    /// </summary>
    internal sealed class PartitionPlacementPolicy
    {
        /// <summary>
        /// Construct a placement policy with the given upper bound on
        /// the number of monitored items per partition. <c>0</c> is
        /// treated as <see cref="uint.MaxValue"/> per OPC UA Part 4
        /// §5.13.2 — the server reports <c>0</c> for the
        /// <c>MaxMonitoredItemsPerSubscription</c> capability when no
        /// hard cap is enforced.
        /// </summary>
        /// <param name="maxItemsPerPartition">
        /// Upper bound on the number of monitored items per partition
        /// (inclusive). When zero, treated as <see cref="uint.MaxValue"/>.
        /// </param>
        /// <param name="maxPartitionCount">
        /// Hard upper bound on the number of partitions the logical
        /// subscription is allowed to grow to. When the policy would
        /// otherwise mint a new partition past this count it returns
        /// <see cref="PlacementDecision.RejectMaxPartitionCountReached"/>.
        /// <c>0</c> is treated as <see cref="uint.MaxValue"/> (no
        /// hard cap) for back-compat with construction sites that
        /// do not yet thread the value through; production wiring
        /// must always supply the configured cap from
        /// <see cref="SubscriptionOptions.MaxPartitionCount"/>.
        /// </param>
        public PartitionPlacementPolicy(
            uint maxItemsPerPartition,
            uint maxPartitionCount = 0)
        {
            m_maxItemsPerPartition = maxItemsPerPartition == 0
                ? uint.MaxValue
                : maxItemsPerPartition;
            m_maxPartitionCount = maxPartitionCount == 0
                ? uint.MaxValue
                : maxPartitionCount;
        }

        /// <summary>
        /// Effective per-partition upper bound applied to placement
        /// decisions. Exposed for diagnostics and tests; matches the
        /// value passed to the constructor (or <see cref="uint.MaxValue"/>
        /// when the constructor received <c>0</c>).
        /// </summary>
        public uint MaxItemsPerPartition => m_maxItemsPerPartition;

        /// <summary>
        /// Effective hard upper bound on the number of partitions
        /// the logical subscription may grow to. Exposed for
        /// diagnostics and tests; matches the value passed to the
        /// constructor (or <see cref="uint.MaxValue"/> when the
        /// constructor received <c>0</c>).
        /// </summary>
        public uint MaxPartitionCount => m_maxPartitionCount;

        /// <summary>
        /// Decide which existing partition should host a monitored
        /// item with the given options, or signal that a fresh
        /// partition must be minted. Must be called under the
        /// owning composite's lock.
        /// </summary>
        /// <param name="options">Options snapshot for the item being
        /// placed. Only <see cref="MonitoredItemOptions.Affinity"/>
        /// influences the decision today.</param>
        /// <param name="partitions">All partitions currently owned by
        /// the logical subscription, in age order. The first entry is
        /// the primary partition.</param>
        public PlacementDecision Decide(
            MonitoredItemOptions options,
            IReadOnlyList<IManagedSubscription> partitions)
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }
            if (partitions == null)
            {
                throw new ArgumentNullException(nameof(partitions));
            }

            string? affinity = options.Affinity;
            if (affinity != null &&
                m_affinityIndex.TryGetValue(affinity, out IManagedSubscription? pinned))
            {
                // Strict-affinity contract: an already-pinned group
                // must keep going to its partition or the add fails.
                // The no-grow flag is honoured even for the pinned
                // partition — once the server has refused at a count
                // we must not push more in.
                if (HasCapacity(pinned))
                {
                    return PlacementDecision.CreateUseExisting(pinned);
                }
                return PlacementDecision.CreateRejectStrictAffinityFull(pinned);
            }

            // First-fit across partitions in age order. Skip
            // partitions flagged no-grow by the reactive fallback.
            for (int i = 0; i < partitions.Count; i++)
            {
                IManagedSubscription candidate = partitions[i];
                if (HasCapacity(candidate))
                {
                    return PlacementDecision.CreateUseExisting(candidate);
                }
            }

            // No existing partition can accept the item. Enforce
            // the hard partition-count cap before signalling that
            // a fresh partition must be minted; once the cap is hit
            // the wrapper must refuse further growth (DoS guard
            // against a hostile or buggy server amplifying every
            // Bad_TooManyMonitoredItems reply into unbounded
            // partition fan-out).
            if ((uint)partitions.Count >= m_maxPartitionCount)
            {
                return PlacementDecision.CreateRejectMaxPartitionCountReached();
            }

            return PlacementDecision.CreateNeedNewPartition();
        }

        /// <summary>
        /// Notify the policy that a new partition has been added to
        /// the logical subscription so capacity tracking starts at
        /// zero for it. Called by the composite collection right
        /// after the partition is registered in the partition list.
        /// </summary>
        public void OnPartitionAdded(IManagedSubscription partition)
        {
            if (partition == null)
            {
                throw new ArgumentNullException(nameof(partition));
            }
            // Idempotent — a re-add (e.g. after a transient remove
            // race) leaves the counter at its current value.
            if (!m_perPartitionCount.ContainsKey(partition))
            {
                m_perPartitionCount[partition] = 0;
            }
        }

        /// <summary>
        /// Notify the policy that a partition has been removed from
        /// the logical subscription. Drops the partition's counter,
        /// no-grow flag, and any affinity entries pointing at it.
        /// </summary>
        public void OnPartitionRemoved(IManagedSubscription partition)
        {
            if (partition == null)
            {
                throw new ArgumentNullException(nameof(partition));
            }
            m_perPartitionCount.Remove(partition);
            m_noGrow.Remove(partition);

            // Drop affinity bindings that point at the gone partition
            // so a future TryAdd for the same affinity tag is free to
            // pick or mint a fresh partition.
            List<string>? toRemove = null;
            foreach (KeyValuePair<string, IManagedSubscription> entry in m_affinityIndex)
            {
                if (ReferenceEquals(entry.Value, partition))
                {
                    toRemove ??= [];
                    toRemove.Add(entry.Key);
                }
            }
            if (toRemove != null)
            {
                foreach (string key in toRemove)
                {
                    m_affinityIndex.Remove(key);
                }
            }
        }

        /// <summary>
        /// Notify the policy that an item with the given options has
        /// been successfully added to the chosen partition. Called by
        /// the composite collection after the partition's TryAdd
        /// returns true.
        /// </summary>
        public void OnItemAdded(
            MonitoredItemOptions options,
            IManagedSubscription partition)
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }
            if (partition == null)
            {
                throw new ArgumentNullException(nameof(partition));
            }

            if (m_perPartitionCount.TryGetValue(partition, out uint count))
            {
                m_perPartitionCount[partition] = count + 1;
            }
            else
            {
                // Late OnPartitionAdded — fold it in here so callers
                // that forget to register still get a coherent count.
                m_perPartitionCount[partition] = 1;
            }

            string? affinity = options.Affinity;
            if (affinity != null && !m_affinityIndex.ContainsKey(affinity))
            {
                m_affinityIndex[affinity] = partition;
            }
        }

        /// <summary>
        /// Notify the policy that an item has been removed from the
        /// given partition. The affinity index intentionally stays —
        /// strict pinning persists for the lifetime of the partition.
        /// </summary>
        public void OnItemRemoved(IManagedSubscription partition)
        {
            if (partition == null)
            {
                throw new ArgumentNullException(nameof(partition));
            }
            if (m_perPartitionCount.TryGetValue(partition, out uint count) && count > 0)
            {
                m_perPartitionCount[partition] = count - 1;
            }
        }

        /// <summary>
        /// Reactive fallback hook: the server returned
        /// <c>Bad_TooManyMonitoredItems</c> for an item targeting
        /// the given partition. The policy lowers the effective cap
        /// for that partition to its current count and flags the
        /// partition no-grow so future placements bypass it.
        /// </summary>
        public void OnPartitionCapReached(IManagedSubscription partition)
        {
            if (partition == null)
            {
                throw new ArgumentNullException(nameof(partition));
            }
            m_noGrow.Add(partition);
        }

        /// <summary>
        /// Number of items the policy believes are placed in the
        /// partition. Exposed for tests and diagnostics; the value
        /// is incremented on <see cref="OnItemAdded"/> and
        /// decremented on <see cref="OnItemRemoved"/>.
        /// </summary>
        public uint GetCount(IManagedSubscription partition)
        {
            if (partition == null)
            {
                throw new ArgumentNullException(nameof(partition));
            }
            return m_perPartitionCount.TryGetValue(partition, out uint count) ? count : 0;
        }

        /// <summary>
        /// Whether the partition is currently marked no-grow by the
        /// reactive cap fallback.
        /// </summary>
        public bool IsNoGrow(IManagedSubscription partition)
        {
            if (partition == null)
            {
                throw new ArgumentNullException(nameof(partition));
            }
            return m_noGrow.Contains(partition);
        }

        /// <summary>
        /// Partition currently pinned to the affinity tag, or
        /// <c>null</c> if the tag is not yet bound. Exposed for
        /// tests and diagnostics.
        /// </summary>
        public IManagedSubscription? TryGetAffinityPartition(string affinity)
        {
            if (affinity == null)
            {
                throw new ArgumentNullException(nameof(affinity));
            }
            return m_affinityIndex.TryGetValue(affinity, out IManagedSubscription? partition)
                ? partition
                : null;
        }

        private bool HasCapacity(IManagedSubscription partition)
        {
            if (m_noGrow.Contains(partition))
            {
                return false;
            }
            uint count = m_perPartitionCount.TryGetValue(partition, out uint c) ? c : 0;
            return count < m_maxItemsPerPartition;
        }

        private readonly uint m_maxItemsPerPartition;
        private readonly uint m_maxPartitionCount;
        private readonly Dictionary<IManagedSubscription, uint> m_perPartitionCount = [];
        private readonly Dictionary<string, IManagedSubscription> m_affinityIndex
            = new(StringComparer.Ordinal);
        private readonly HashSet<IManagedSubscription> m_noGrow = [];
    }

    /// <summary>
    /// Outcome of <see cref="PartitionPlacementPolicy.Decide"/>. One
    /// of four mutually exclusive states.
    /// </summary>
    internal readonly struct PlacementDecision
    {
        /// <summary>
        /// Use the existing partition <see cref="Partition"/>. The
        /// caller forwards the TryAdd to that partition's collection.
        /// </summary>
        public bool UseExistingPartition { get; }

        /// <summary>
        /// No existing partition has capacity. The caller mints a new
        /// partition via the factory, registers it, then forwards the
        /// TryAdd to the new partition's collection.
        /// </summary>
        public bool RequiresNewPartition { get; }

        /// <summary>
        /// The item's affinity group is already pinned to a partition
        /// that has reached the per-partition cap. The caller must
        /// reject the TryAdd (strict-affinity contract). The pinned
        /// partition is reported in <see cref="Partition"/> for
        /// diagnostics.
        /// </summary>
        public bool RejectStrictAffinityFull { get; }

        /// <summary>
        /// The logical subscription has hit its
        /// <see cref="PartitionPlacementPolicy.MaxPartitionCount"/>
        /// guard and may not grow further. The caller must reject
        /// the TryAdd. Acts as the DoS safeguard against hostile or
        /// buggy servers that reply
        /// <c>Bad_TooManyMonitoredItems</c> to every
        /// <c>CreateMonitoredItems</c> request.
        /// </summary>
        public bool RejectMaxPartitionCountReached { get; }

        /// <summary>
        /// Target partition for
        /// <see cref="UseExistingPartition"/>; pinned partition for
        /// <see cref="RejectStrictAffinityFull"/>; <c>null</c> for
        /// <see cref="RequiresNewPartition"/> and
        /// <see cref="RejectMaxPartitionCountReached"/>.
        /// </summary>
        public IManagedSubscription? Partition { get; }

        private PlacementDecision(
            bool useExisting,
            bool requiresNew,
            bool rejectStrict,
            bool rejectMaxPartitions,
            IManagedSubscription? partition)
        {
            UseExistingPartition = useExisting;
            RequiresNewPartition = requiresNew;
            RejectStrictAffinityFull = rejectStrict;
            RejectMaxPartitionCountReached = rejectMaxPartitions;
            Partition = partition;
        }

        internal static PlacementDecision CreateUseExisting(IManagedSubscription partition)
        {
            return new PlacementDecision(true, false, false, false, partition);
        }

        internal static PlacementDecision CreateNeedNewPartition()
        {
            return new PlacementDecision(false, true, false, false, null);
        }

        internal static PlacementDecision CreateRejectStrictAffinityFull(IManagedSubscription pinned)
        {
            return new PlacementDecision(false, false, true, false, pinned);
        }

        internal static PlacementDecision CreateRejectMaxPartitionCountReached()
        {
            return new PlacementDecision(false, false, false, true, null);
        }
    }
}