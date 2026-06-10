# Unbounded Monitored Items (V2 subscriptions)

The V2 subscription engine
(`Opc.Ua.Client.Subscriptions.ISubscription`, returned from
`ManagedSession.SubscriptionManager.Add(...)`) lets a single logical
subscription hold an effectively unlimited number of monitored items.
When the per-subscription cap (`MaxMonitoredItemsPerSubscription` from
the server's capabilities, OPC UA Part 4 §5.13.2) would be exceeded,
the engine transparently splits monitored items across additional
server-side **partition** subscriptions and presents one logical
collection to the caller. Single-partition workloads (the common
case) keep their pre-existing fast path; partitioning kicks in only
when needed.

## How it works

* `ISubscriptionManager.Add(handler, options)` returns a
  `LogicalSubscription` wrapper that implements `ISubscription` and
  `IPartitionedSubscription`. Pattern-match on the latter to
  introspect the partition layout:

  ```csharp
  ISubscription subscription = session.SubscriptionManager.Add(handler, optionsMonitor);
  if (subscription is IPartitionedSubscription partitioned)
  {
      Console.WriteLine($"Spread over {partitioned.PartitionCount} partition(s)");
      foreach (uint partitionId in partitioned.PartitionIds)
      {
          Console.WriteLine($"  partition server id: {partitionId}");
      }
  }
  ```

* `ISubscription.MonitoredItems` is backed by a composite collection
  that aggregates every partition's items behind one
  `IMonitoredItemCollection`. `TryAdd` / `TryRemove` /
  `TryGetMonitoredItemByName` / `TryGetMonitoredItemByClientHandle`
  enumerate every partition; the caller never needs to know which
  partition owns which item.

* When the placement policy decides a new partition is needed, the
  engine constructs a sibling server-side subscription using the
  same `SubscriptionOptions` and the same notification handler. The
  new partition is registered in the manager's publish-dispatch
  registry so publish responses route to it like any other
  subscription.

* `ISubscriptionNotificationHandler` callbacks always receive the
  logical wrapper as the `ISubscription subscription` parameter.
  When more than one partition is active the engine serialises calls
  through a per-wrapper semaphore so consumers observe one handler
  invocation at a time, matching the single-partition behaviour they
  came to rely on under V2.

* Notifications include the source partition's server-side id via
  the `PartitionServerId` field on `DataValueChange` and
  `EventNotification`. Sequence numbers stay per-partition
  (`(PartitionServerId, sequenceNumber)` disambiguates across
  partitions).

## Affinity — pinning items to the same partition

OPC UA `SetTriggering` is scoped to one server-side subscription
(`Part 4 §5.13.6`). Items that need to participate in a triggering
relationship (or any other per-subscription feature) must land in the
same partition. Pin them via
`MonitoredItemOptions.Affinity`:

```csharp
var optionsMonitor = new OptionsMonitor<MonitoredItemOptions>(new MonitoredItemOptions
{
    StartNodeId = new NodeId("MyVariable", 2),
    SamplingInterval = TimeSpan.FromMilliseconds(500),
    Affinity = "alarms-group"   // every item with this tag stays together
});
subscription.MonitoredItems.TryAdd("MyItem", optionsMonitor, out _);
```

* Items with the same non-null `Affinity` value are guaranteed to
  share a partition.
* Once the partition reaches the per-partition cap the next
  `TryAdd` with the same tag returns `false` — the contract is
  **strict** so the group never splits. Callers must shrink the
  group, raise `MaxMonitoredItemsPerPartition`, or pick a different
  tag.
* `null` (the default) places no co-location constraint; items
  without affinity fill partitions first-fit.

`LogicalSubscription.SetTriggeringAsync(triggering, linksToAdd,
linksToRemove)` validates that every linked item shares the same
partition as the triggering item and throws `ArgumentException`
otherwise. The exception message points callers at `Affinity` for
remediation.

## Configuration

All knobs live on `SubscriptionOptions`:

| Property | Default | Effect |
| --- | --- | --- |
| `DisableUnboundedItemMode` | `false` | When `true`, the wrapper is bound to one server-side subscription; `TryAdd` calls beyond the server cap surface `Bad_TooManyMonitoredItems` per-item like the pre-V2 engine. |
| `MaxMonitoredItemsPerPartition` | `null` | Per-partition upper bound. `null` means "let the reactive fallback discover the server's effective cap". Set to a smaller value to keep partitions small for snapshot/transfer scaling, or to a larger value to override an artificially low server limit. |
| `SecondaryPartitionIdleTimeout` | `30s` | Idle timeout after which an empty **secondary** partition is deleted from the server. The primary partition is never deleted while the logical subscription is alive so the wrapper's server-side identifier stays stable. Set to `TimeSpan.Zero` for immediate delete; set to `Timeout.InfiniteTimeSpan` to disable idle-delete. |

The reactive cap fallback is always on whenever
`DisableUnboundedItemMode` is `false`: the engine watches every
`CreateMonitoredItems` response and, on the first
`Bad_TooManyMonitoredItems` outcome, marks the offending partition
**no-grow** so subsequent placements fan out to a new partition. This
handles servers whose actual limit is lower than the advertised
capability and servers whose limit changes between session lifetimes.

## Snapshot, save / load, and transfer

Multi-partition logical subscriptions snapshot every partition. The
capture path is:

* `LogicalSubscription.SnapshotAllPartitions()` returns
  `IReadOnlyList<SubscriptionStateSnapshot>` — one snapshot per
  partition, ordered primary-first.
* Every snapshot in the list carries the wrapper's stable
  `LogicalGroupId` (a lazy-generated GUID cached for the lifetime of
  the wrapper) and an incrementing `PartitionIndex` (`0` for the
  primary, `1+` for secondaries in mint order).
* `ManagedSession.SnapshotSubscriptions()` and
  `ISubscriptionManager.SaveAsync(...)` flatten across partitions
  automatically, so callers persisting the list see the full state.
* Single-partition wrappers continue to emit exactly one snapshot.
  Old V1 snapshot files have `LogicalGroupId == null` and load via
  the standalone path unchanged.

The restore path:

* `ISubscriptionManager.LoadAsync(...)` and
  `ManagedSession.RestoreSubscriptionsAsync(...)` group incoming
  snapshots by `LogicalGroupId`. Snapshots with a `null` group
  restore as standalone subscriptions; non-null groups become one
  multi-partition `LogicalSubscription` wrapper via
  `SubscriptionManager.RestoreGroupAsync(...)` (internal).
* Restored secondary partitions go through a new internal
  `LogicalSubscription.AppendPreloadedPartition(...)` hook so the
  composite collection's placement policy + idle-delete timer + (if
  applicable) durable hook are wired exactly as if the partition had
  been minted on demand.
* When `transferSubscriptions: true` is passed, each partition's
  saved server-side id is preserved via `TransferSubscriptions`;
  partitions whose transfer fails fall back to recreate.
* **Strict grouping** is enforced: a non-null `LogicalGroupId` must
  appear on a contiguous `0..N-1` sequence of `PartitionIndex`
  values with no duplicates and exactly one primary. Malformed
  snapshot groups throw `ServiceResultException(BadDecodingError)`
  naming the offending `LogicalGroupId` — a corrupt or hand-edited
  snapshot fails loudly rather than silently fragmenting state.

`MonitoredItemStateSnapshot.Affinity` round-trips so restored
subscriptions regroup items into the same affinity-pinned partition
the source had.

## Durable subscriptions

`SetAsDurableAsync(lifetime)` records the durable intent on the
wrapper and applies it synchronously to every partition that is
already `Created` (it returns the minimum revised lifetime across
those partitions). For partitions that have not yet completed
their initial `CreateSubscription` — both the primary on a brand
new subscription and any future secondary minted by the placement
policy under capacity pressure — the wrapper installs an
`OnAfterCreateAsync` hook on the partition's state machine. The
state machine awaits the hook exactly once between
`CreateSubscription` and the first `CreateMonitoredItems`,
satisfying the OPC UA Part 4 §5.13.9 ordering rule that
`SetSubscriptionDurable` must precede any monitored-item creation.
The hook is cleared after invocation; the wrapper re-installs it on
each `SetAsDurableAsync` call so it survives reconnect / recreate
cycles. Hook failures are logged as a warning and surfaced via
`SubscriptionState.Modified` rather than tearing the partition
down.

## Performance

Single-partition workloads observe **no overhead** from the wrapper
— the composite collection short-circuits to the primary's own
collection without taking any extra locks or building indexes.
Multi-partition workloads pay:

* One semaphore acquire/release per notification callback (small).
* O(P) partition scans for `TryGetMonitoredItemByName` /
  `TryGetMonitoredItemByClientHandle` misses against the composite
  index (P is typically single-digit).
* One extra `Subscription` instance per partition (each holds its
  own state machine, ack queue position, and notification pipeline).

## Limitations and roadmap

Cross-partition `SetTriggering` is rejected by design: per OPC UA
Part 4 §5.13.6 the service is scoped to a single server-side
subscription, and re-grouping already-placed items would require
delete + recreate (losing per-item server ids and briefly stopping
publishing). Plan `Affinity` up-front for items that need to
participate in a triggering relationship; the engine will keep the
group co-located for the lifetime of the wrapper.

This is the only multi-partition feature gap — single-partition
workloads cover the vast majority of OPC UA client use cases
unchanged.
