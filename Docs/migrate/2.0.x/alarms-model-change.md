# Alarms and Address-Space Model Changes

> **When to read this:** Read this for `AlarmConditionState` state-transition behaviour, the auto-emitted `GeneralModelChangeEvent` from `CustomNodeManager.CreateNode/DeleteNode`, and the address-space `ModelChangeAggregator` migration.

## Alarms and Conditions

Two changes require attention.

### `AlarmConditionState` state-transition behavior

The state-machine setters on `AlarmConditionState` previously did not
implement several cross-state spec requirements. 1.6 makes them
compliant:

| Behavior | Spec | Was (≤ 1.5.378) | Is (1.6) |
|---|---|---|---|
| Activating an alarm with `LatchedState` populated | §4.8 | `LatchedState` untouched | `LatchedState.Id = true` automatically |
| Activating an alarm with `SilenceState` populated and silenced | §4.8 | `SilenceState` stayed silenced | `SilenceState.Id = false` (audible again) |
| `SuppressedOrShelved` flag computation | §5.8.2 | considered Suppressed + Shelved only | also considers `OutOfServiceState` |
| `GetRetainState` for latched alarms | §5.5.2 | did not include LatchedState | latched alarms are retained while `LatchedState.Id = true` |
| `EffectiveDisplayName` composition | §5.8.2 | Active + Suppressed + Shelved + Acked + Confirmed | additionally includes OutOfService and Latched |

**Migration:** If you have alarms with `LatchedState`,
`SilenceState`, or `OutOfServiceState` populated and you relied on
the prior behavior, the spec-compliant behavior is what your
operators expected anyway. To restore the old behavior, do not
populate those optional state nodes (leave them `null`).

The quickstart reference server (`Applications/Quickstarts.Servers/
Alarms/AlarmHolders/AlarmConditionTypeHolder.cs`) now creates the
`SilenceState`, `OutOfServiceState`, and `LatchedState` nodes by
default — so the conformance tests exercise the new compliant
behavior end-to-end.

The quickstart `AlarmNodeManager` itself was also modernized:

* it now derives from `AsyncCustomNodeManager` (was
  `CustomNodeManager2`) and uses the async lifecycle overrides
  (`CreateAddressSpaceAsync`, `CallAsync`, `ConditionRefreshAsync`),
  matching the stack-wide pattern used by `WotConnectivityNodeManager`,
  `FluentNodeManagerBase`, etc.;
* it demonstrates the new `AlarmGroup` + `AlarmSuppressionEngine`
  helpers end-to-end with an `/Alarms/AnalogGroup` group and a
  writable `/Alarms/MaintenanceMode` boolean — clients can flip
  MaintenanceMode and watch every member alarm transition into
  `SuppressedState`. See
  [Alarms and Conditions](../../AlarmsAndConditions.md#alarm-groups-and-first-in-group)
  for the developer guide.

Neither change is breaking for stack consumers — they only affect
the quickstart demo project that ships with the reference server.

### Auto-emit `GeneralModelChangeEvent` from `CustomNodeManager`

`CustomNodeManager.CreateNode(...)` and `DeleteNode(...)` (and the
async equivalents on `AsyncCustomNodeManager`) now record the change
in a per-instance `ModelChangeAggregator` and emit a
`GeneralModelChangeEvent` at the end of the call. This was required
by Part 5 §6.4.32 but was previously left to derived classes.

If clients were already subscribed to `BaseEventType` on the server
notifier, they will start receiving `GeneralModelChangeEvent`. Existing
clients that filter events by `EventTypeId` (the common case) keep
receiving only the types they asked for. Clients that subscribe to
the broad `BaseEventType` and want to skip model-change traffic should
add a `not OfType GeneralModelChangeEventType` clause to their
`EventFilter`.

```csharp
// To opt out of auto-emit in a derived node manager:
public MyNodeManager(...)
{
    ModelChangeEmissionEnabled = false;
}
```

The aggregator API (`ModelChangeAggregator.RecordNodeAdded/Deleted/
ReferenceAdded/ReferenceDeleted/DataTypeChanged`, `Drain`,
`HasPending`) is also available for manual control — see
[Model Change Tracking](../../ModelChangeTracking.md).

## Address-space model change tracking

### New `INodeCache.InvalidateNode` member

`INodeCache` gains a new abstract member in 1.6:

```csharp
void InvalidateNode(NodeId nodeId);
```

The stack's built-in `NodeCache` implements this with true per-node
eviction. The `ModelChangeTracker` uses it to keep the cache in sync
with server-reported address-space changes — see
[Model Change Tracking](../../ModelChangeTracking.md).

**Migration:** Custom `INodeCache` implementations must add an
implementation. The simplest is to delegate to `Clear()`:

```csharp
public sealed class MyNodeCache : INodeCache
{
    public void Clear() { /* ... */ }

    // Add this:
    public void InvalidateNode(NodeId nodeId) => Clear();

    // ... rest of INodeCache ...
}
```

Implementations that can perform per-node eviction should do so —
the tracker is most efficient when targeted invalidation is
available.

---

**See also**

- Related: [node-states.md](node-states.md), [sessions-subscriptions.md](sessions-subscriptions.md).
- [2.0 migration index](README.md) — analyzer quick-start + symptom → sub-doc table.
- [Migration Guide](../../MigrationGuide.md) — landing page across versions.

