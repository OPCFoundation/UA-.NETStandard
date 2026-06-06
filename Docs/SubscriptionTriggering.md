# Subscription Triggering (V2)

The V2 subscription engine (`Libraries/Opc.Ua.Client/Subscription`)
supports OPC UA Part 4 §5.13.5 *SetTriggering* through a hybrid API:

- **Declarative** — set `MonitoredItemOptions.TriggeredByNames` when
  the item is added. The engine reconciles the desired triggering
  topology with the server when items finish creating.
- **Imperative** — call
  `ISubscription.SetTriggeringAsync(IMonitoredItem triggeringItem,
  add, remove, ct)` at any time. The call returns a
  `SetTriggeringResult` with per-link statuses.

Both paths flow through the same batched apply pipeline: multiple
operations targeting the same triggering item collapse into a single
SetTriggering RPC carrying both the merged `linksToAdd` and
`linksToRemove` lists. Per Part 4 §5.13.5.2 the server processes
`linksToRemove` before `linksToAdd`, which matches the engine's
last-intent-wins conflict resolution.

- [Quick reference](#quick-reference)
- [Concept overview](#concept-overview)
- [Declarative API](#declarative-api)
- [Imperative API](#imperative-api)
- [N:M relationships](#nm-relationships)
- [Save / load / restore behavior](#save--load--restore-behavior)
- [Error handling](#error-handling)
- [Comparison with the classic engine](#comparison-with-the-classic-engine)

## Quick reference

| Concern | API |
|---|---|
| Declare initial triggers on add | `MonitoredItemOptions.TriggeredByNames` (`IReadOnlyList<string>`) |
| Add/remove links imperatively | `ISubscription.SetTriggeringAsync(IMonitoredItem, add, remove, ct)` |
| Convenience name-based wrapper | `ISubscription.SetTriggeringAsync(string triggeringName, string[] triggeredNames)` |
| Inspect "what triggers me" | `IMonitoredItem.TriggeringItems` |
| Inspect "what do I trigger" | `IMonitoredItem.TriggeredItems` |
| Per-link result type | `SetTriggeringResult(TriggeringItem, AddResults, RemoveResults, ServiceResult)` |
| Snapshot persistence field | `MonitoredItemStateSnapshot.TriggeredByNames` (`ArrayOf<string>`) |

## Concept overview

Triggering links a "triggering" monitored item to one or more
"triggered" items. When the triggering item fires a notification, the
triggered items' queued notifications are reported in the next publish
even if their monitoring mode is `Sampling` (which would otherwise
suppress reporting). This is the canonical pattern for "sample many
items at high rate, report on demand". See Part 4 §5.13.1.6 for the
full triggering model.

The V2 engine models the topology as a per-item **desired state**:
each monitored item carries a list of stable triggering-item names
(`DesiredTriggeredByNames`) that reflects what the caller wants. The
engine reconciles desired state against the server via SetTriggering
RPCs whenever:

- An options change pushes a different `TriggeredByNames` value.
- The imperative API mutates the desired set.
- An item is `Reset` (recreate scenario) — the engine replays the
  desired set so transient server-side state matches the durable
  client-side intent.

The OPC UA spec allows a triggered item to be linked to **multiple**
triggering items (N:M); the V2 API exposes this directly via the
plural `TriggeringItems` / `TriggeredItems` projections.

## Declarative API

The simplest way to declare triggering is via `MonitoredItemOptions`
at item-add time:

```csharp
ManagedSession session = await new ManagedSessionBuilder(config, telemetry)
    .UseEndpoint(endpoint)
    .ConnectAsync(ct);

ISubscription sub = session.AddSubscription(handler, new SubscriptionOptions
{
    PublishingInterval = TimeSpan.FromSeconds(1)
});

// Triggering item — reports normally at its sampling interval.
sub.TryAddMonitoredItem("trig",
    VariableIds.Server_ServerStatus_CurrentTime,
    o => o with { MonitoringMode = MonitoringMode.Reporting },
    out _);

// Triggered item — samples in the background; only reports when
// "trig" reports.
sub.TryAddMonitoredItem("sensor",
    new NodeId("Sensor1", 2),
    o => o with
    {
        MonitoringMode = MonitoringMode.Sampling,
        TriggeredByNames = ["trig"]
    },
    out _);
```

The engine batches the underlying `CreateMonitoredItems` /
`SetTriggering` RPCs and applies them on the next subscription
ApplyChanges pass. There is no need to explicitly call any method to
"finish" the triggering setup — once both items are `Created`, the
engine issues the `SetTriggering` call automatically.

Order of `TryAddMonitoredItem` calls does not matter; the engine
will keep the triggering operation queued until both items are
created on the server, then issue the RPC.

## Imperative API

Mutate triggering relationships at any time after items exist via
`SetTriggeringAsync`:

```csharp
SetTriggeringResult result = await sub.SetTriggeringAsync(
    triggeringItem: trig,
    linksToAdd: [sensor1, sensor2],
    linksToRemove: null,
    ct: ct);

foreach ((IMonitoredItem item, StatusCode status) in result.AddResults)
{
    if (!StatusCode.IsGood(status))
    {
        // BadMonitoredItemIdInvalid, BadInvalidState, etc.
    }
}
```

The call queues a single `TriggeringOperation` and returns a
`ValueTask` that completes when Phase 4 of the next ApplyChanges
pass applies it. The result's per-link entries are in the same order
as the input lists for easy pairing.

Convenience overloads use stable monitored-item names:

```csharp
await sub.SetTriggeringAsync("trig", "sensor1", "sensor2");

// add + remove in one call (server processes remove before add per §5.13.5.2):
await sub.SetTriggeringAsync(
    "trig",
    add: ["sensor3"],
    remove: ["sensor1"],
    ct: ct);
```

Unknown names throw `ArgumentException` synchronously.

## N:M relationships

A triggered item may be linked to many triggering items, and a
triggering item may have many triggered items. Use multiple
`SetTriggeringAsync` calls (or include the same triggered name under
multiple triggers' `TriggeredByNames`):

```csharp
sub.TryAddMonitoredItem("trigA", ..., out var trigA);
sub.TryAddMonitoredItem("trigB", ..., out var trigB);
sub.TryAddMonitoredItem("shared",
    nodeId,
    o => o with
    {
        MonitoringMode = MonitoringMode.Sampling,
        TriggeredByNames = ["trigA", "trigB"]
    },
    out var shared);

// "shared" reports whenever EITHER trigA OR trigB fires:
Assert.That(shared.TriggeringItems, Is.EquivalentTo(new[] { trigA, trigB }));
Assert.That(trigA.TriggeredItems, Has.Member(shared));
Assert.That(trigB.TriggeredItems, Has.Member(shared));
```

`TriggeringItems` and `TriggeredItems` are read-only projections
resolved on demand against the subscription's monitored-item
collection. They reflect the *desired* topology (intent) — so they
work immediately on `TryAdd` before any RPC fires, and survive
restore-from-snapshot without depending on server state.

## Save / load / restore behavior

The triggering topology round-trips through the V2 snapshot path:

- `MonitoredItemStateSnapshot.TriggeredByNames` captures the runtime
  desired state at snapshot time.
- `SubscriptionManager.SaveAsync` / `LoadAsync` (and the in-memory
  `SnapshotSubscriptions` / `RestoreSubscriptionsAsync` extensions)
  persist this field via the standard binary encoder.

On load, the engine takes one of three paths:

| Scenario | Behavior |
|---|---|
| **TransferSubscriptions success** | Local desired state is restored from the snapshot. NO SetTriggering RPC is issued — per Part 4 §5.13.5 conformant servers preserve server-side triggering relationships across a session transfer. |
| **Recreate-fallback** (transfer rejected or unsupported) | Items reset to "not created"; the saved `DesiredTriggeredByNames` is preserved on each item; Phase 4 of ApplyChanges replays SetTriggering after items finish re-creating. |
| **In-session `RecreateAsync`** (forced recreate) | Same as recreate-fallback — desired state preserved through reset, replayed after re-creation. |

This closes the gap from issue
[#3834](https://github.com/OPCFoundation/UA-.NETStandard/issues/3834):
triggering links are now automatically restored on
`TransferSubscriptions` *and* on recreate/reconnect; no manual
re-issue of `SetTriggering` is required by callers.

If a server fails to preserve links across transfer (contrary to the
spec), the V2 client view will indicate the links exist while
server-side notifications stop firing. Callers detecting this can
manually re-issue `Subscription.SetTriggeringAsync` per relationship;
a defensive opt-in replay flag may be added in a future release if
real-world reports emerge.

## Error handling

Per Part 4 §5.13.5.4 (Table 74) the only spec-specific per-link status
code is `Bad_MonitoredItemIdInvalid`. Service-level codes (Table 73)
include `Bad_NothingToDo`, `Bad_TooManyOperations`, and
`Bad_SubscriptionIdInvalid`.

The engine handles these as follows:

| Condition | Engine response |
|---|---|
| Per-link `Bad_MonitoredItemIdInvalid` on add | Surfaced via the returned `SetTriggeringResult.AddResults[i].Status`; the triggered item's desired-state entry for that triggering name is rolled back so it matches reality. |
| Per-link `Bad` on remove | Surfaced via `RemoveResults[i].Status`; the desired-state entry is re-added so the snapshot continues to reflect the still-existing server link. |
| Service-level `Bad_SubscriptionIdInvalid` | Operations are re-queued (not failed terminally); they replay after the subscription state machine recreates the subscription. |
| Service-level `Bad_TooManyOperations` | Surfaced as a fatal `ServiceResultException` on all contributing TCSs. Callers wishing to recover should chunk their `SetTriggeringAsync` calls manually; auto-chunking is a planned follow-up. |
| Communication errors (timeout, channel closed) | Routed through the existing retry policy; bounded by `MaxTriggeringRetryCount` to prevent infinite loops. |
| Triggering item never reaches `Created` | After `MaxTriggeringRetryCount` re-queues, the operation completes with the triggering item's propagated error status (or `Bad_MonitoredItemIdInvalid` as a fallback). |

## Comparison with the classic engine

| Concern | Classic | V2 |
|---|---|---|
| API surface | `Subscription.SetTriggeringAsync(MonitoredItem trig, ArrayOf<MonitoredItem> add, ArrayOf<MonitoredItem> remove, ct)` | `ISubscription.SetTriggeringAsync(IMonitoredItem, IReadOnlyCollection<IMonitoredItem>?, IReadOnlyCollection<IMonitoredItem>?, CancellationToken)` |
| Result shape | `SetTriggeringResponse` (raw service response) | `SetTriggeringResult` (per-link tuples) |
| Cardinality | 1:1 from the triggered side (`MonitoredItem.TriggeringItemId`) | N:M (`IMonitoredItem.TriggeringItems` plural) |
| Declarative option | None | `MonitoredItemOptions.TriggeredByNames` |
| Batching | Per call — one RPC per `SetTriggeringAsync` invocation | Per batch — multiple imperative + declarative changes coalesce per triggering item |
| Save/restore | `MonitoredItemState.TriggeredItems` (server-id based) | `MonitoredItemStateSnapshot.TriggeredByNames` (name based, N:M) |
| Reconnect / transfer replay | `RestoreTriggeringAsync` re-issues SetTriggering after every reconnect | TransferSubscriptions: NO replay (server preserves links per spec); Recreate: automatic replay via Phase 4 |

See [MigrationGuide.md](MigrationGuide.md) for the migration steps
from a classic triggering setup to the V2 surface.
