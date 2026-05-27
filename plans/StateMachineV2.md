# Plan v2: State-machine follow-ups

Three open items called out as "v2" when phase 1 (sub-states + guards)
landed at commit `5f03cc25e`:

1. **Sub-SM transitions between parent transitions** — currently
   `ObserveEffectiveStateAsync` only yields on parent transitions; the
   sub-SM's own transitions inside a parent state are invisible.
2. **Typed sub-SM accessors via source generator** — currently
   consumers get a generic `FiniteStateMachineTypeClient?` from
   `GetSubStateMachineAsync(parentStateNodeId)`; want
   `GetShelvingStateMachineAsync() : ShelvedStateMachineTypeClient` for
   known type relationships.
3. **Per-spec `HasSubStateMachine` reference placement** — current
   server impl attaches the reference from the FSM root; Part 16 §B.3
   says it should be from the parent state node.

## Decision per item

| Item | Verdict |
|---|---|
| 1. Transitive sub-SM observation | **Implement** — high value (the v1 user can already infer parent transitions; the sub-SM events are the missing piece). |
| 2. Typed sub-SM accessors | **Implement** — high value for ergonomics; closes the obvious next question users will ask. |
| 3. Per-spec HasSubStateMachine reference | **Defer + document** — the v1 wiring (HasSubStateMachine from FSM root) is BROWSEABLE from a client and works for `GetSubStateMachineAsync`. The spec-compliant "from state node" placement requires per-instance state nodes which `FluentFiniteStateMachineState` doesn't model (states are entries in a private table, not separate NodeStates). Cost of fixing is high; benefit (slightly closer spec compliance) is low. |

## Phase A — Transitive sub-SM observation

### Goal

`ObserveEffectiveStateAsync` yields a combined snapshot when EITHER the
parent transitions OR the currently-active sub-SM transitions.

### Approach (chosen: pre-subscribe to all)

Per rubber-duck recommendation from the v1 review, pre-subscribe to all
discovered sub-SMs at start. Filter sub-SM events based on the parent's
currently-active state.

```text
[ObserveEffectiveStateAsync entry]
    ↓
Discover (parentStateNodeId → subSmClient) bindings once at start
    ↓
Subscribe to parent's ObserveFiniteTransitionsAsync
For each discovered sub-SM: subscribe to its ObserveFiniteTransitionsAsync
    ↓
Merge all event streams into a single Channel<TaggedSnapshot>
    ↓
On parent event: update activeParentState, read current sub-SM snapshot,
              yield combined snapshot
On sub-SM event: if THIS sub-SM is currently active, yield combined
              snapshot with the new sub-SM snapshot
              else discard
    ↓
[Yield loop until ct cancelled]
```

### Implementation

* **Discovery**: at the start of `ObserveEffectiveStateAsync`,
  enumerate `parent.GetAvailableStatesAsync(ct)` and call
  `GetSubStateMachineAsync(stateNodeId, telemetry, ct)` for each
  resolved state node. Skip states with no sub-SM.
* **Multiplexed stream**: use `System.Threading.Channels.Channel<TaggedSnapshot>`.
  Each underlying `ObserveFiniteTransitionsAsync` runs on its own task
  (started fire-and-forget under the caller's CT) and writes tagged
  snapshots to the channel.
  ```csharp
  record TaggedSnapshot(NodeId? SubSmAttachedTo, FiniteStateSnapshot Snap);
  ```
  `SubSmAttachedTo` is `null` for parent events; the parent-state
  NodeId the sub-SM is attached to otherwise.
* **Filtering + yielding**: the outer iterator reads from the channel.
  Maintains `currentParentStateId` updated on every parent event.
  Maintains a `Dictionary<NodeId, FiniteStateSnapshot> latestSubSnaps`
  keyed on parentStateNodeId (the LATEST snapshot from each sub-SM,
  for the parent-event case).
  * Parent event: update `currentParentStateId`. Look up the sub-SM
    attached to the new state (if any), read its current snapshot
    fresh (via `GetCurrentFiniteStateAsync` — guaranteed up-to-date
    because we just received its latest events through the channel,
    but a fresh read covers the race window where the sub-SM
    transitioned in the parent-transition-handling window).
    Yield with `SubMachine = thatSnapshot`.
  * Sub-SM event: ignore if the parent's current state isn't the
    one this sub-SM is attached to. Otherwise build a yielded
    snapshot using the parent's last snapshot but with
    `SubMachine = newSubSmSnap`.
* **Cancellation + cleanup**: link the caller's CT to a per-call CTS
  that the parent + all sub-SM subscriptions share. When the caller
  stops enumerating, cancel everything and dispose channel.

### Files

* `Libraries/Opc.Ua.Client/StateMachines/FiniteStateMachineTypeClientExtensions.cs`
  — rewrite `ObserveEffectiveStateAsync` and its `*Impl` helper. Add
  the multiplex helper class privately.
* `Tests/Opc.Ua.Client.Tests/StateMachines/SubStateMachineClientTests.cs`
  — add tests for the transitive behavior (~3 new tests, on top of
  the existing 8 null-guard/sanity tests).

### Tests

* `ObserveEffectiveStateAsync_YieldsCombinedSnapshotOnParentTransition`
  — parent transitions through states; each yielded snapshot has the
  expected `SubMachine` (null or non-null based on attached state).
* `ObserveEffectiveStateAsync_YieldsOnSubSmTransitionInActiveState`
  — parent stays in state X; sub-SM attached to X transitions; expect
  a yielded snapshot with the new sub-SM state.
* `ObserveEffectiveStateAsync_IgnoresSubSmEventsInInactiveStates`
  — parent in state Y; sub-SM attached to state X transitions; expect
  no yielded snapshot.

Tests use the same `EmptyStreamingSubscription` mock pattern as the
existing null-guard tests. For multi-event flow, a richer mock that
queues notifications is needed.

## Phase B — Typed accessors for any contained Object child (lazy + cached)

### Goal

Generalize beyond FSM-typed children: for **every** `<opc:Object>`
child declared on an emitted ObjectType, the source generator emits a
typed, lazily-resolved accessor on the parent type's client.

```csharp
// AlarmConditionType has Object ShelvingState : ShelvedStateMachineType.
// Generated method on AlarmConditionTypeClient:

public async ValueTask<ShelvedStateMachineTypeClient?> GetShelvingStateAsync(
    ITelemetryContext telemetry, CancellationToken ct = default)
{
    // Lazy resolution: first call browses + constructs the proxy.
    // Subsequent calls return the cached instance.
    return await GetOrResolveShelvingStateAsync(telemetry, ct).ConfigureAwait(false);
}
```

This covers:
* `AlarmConditionType.ShelvingState` → `ShelvedStateMachineTypeClient`
* `ExclusiveLimitAlarmType.LimitState` → `ExclusiveLimitStateMachineTypeClient`
* `ProgramStateMachineType.{ProgramSteps...}` (vendor) → vendor `*Client`
* Any vendor model's Object child of an ObjectType → vendor `*Client`

The generated accessor is **typed**, **lazy**, and **cached** —
calling it N times costs one browse + one proxy allocation.

### Approach

Each emitted `*TypeClient` partial class gains:

```csharp
// Generated per Object child:
private global::{ChildType}Client? m_{childName};
private readonly object m_{childName}Lock = new();

/// <summary>
/// Returns the typed proxy for the {ChildBrowseName} child node.
/// Returns null when the server does not expose the child.
/// The proxy is resolved lazily on first call and cached for
/// subsequent calls.
/// </summary>
public async ValueTask<{ChildType}Client?> Get{ChildBrowseName}Async(
    ITelemetryContext telemetry, CancellationToken ct = default)
{
    // Fast path — cached.
    lock (m_{childName}Lock)
    {
        if (m_{childName} != null) return m_{childName};
    }

    // Resolve the child NodeId via TranslateBrowsePathsToNodeIds.
    NodeId childId = await ResolveChildAsync(
        new QualifiedName("{ChildBrowseName}", ns), ct).ConfigureAwait(false);
    if (childId.IsNull) return null;

    var proxy = new {ChildType}Client(Session, childId, telemetry);
    lock (m_{childName}Lock)
    {
        // Double-check — another caller may have resolved concurrently.
        return m_{childName} ??= proxy;
    }
}
```

`ResolveChildAsync` is a small private helper emitted once per class
(reuses `TranslateBrowsePathsToNodeIds` with the parent's `ObjectId`
as starting node).

### Why lazy

* **Cost** — `TranslateBrowsePath` is a round-trip. Eager would inflate
  proxy construction by N round-trips for an FSM type with N
  object children.
* **Robustness** — children with `ModellingRule="Optional"` may not be
  present on every server instance. Lazy resolution returns `null`
  when the child is absent, without failing the parent constructor.
* **Multi-server scenarios** — a vendor accessing N alarms doesn't
  pay N×childCount round-trips at construction.

### Why caching after first resolve

* Subsequent calls are free (one lock + null-check).
* The proxy is immutable for the lifetime of the parent's NodeId; no
  invalidation needed.
* Per-instance state — each `AlarmConditionTypeClient` instance has
  its OWN cache; doesn't leak across instances.

### Generated proxy properties

For ergonomics, also emit a **property-style** accessor for callers
who want sync-looking access (panics if not yet resolved? returns a
cached value?). Decision: NO sync property — the resolution is async,
and exposing a sync property forces callers into `GetAwaiter().GetResult()`
patterns. Stick with the async method.

### Recognizing eligible Object children

Walk `ObjectTypeDesign.Children?.Items`; pick `InstanceDesign` whose
`NodeClass == Object` (i.e., not properties, variables, or methods).
For each, the `TypeDefinitionNode` is an `ObjectTypeDesign` —
the generator already knows how to map that to the corresponding
proxy class name via `ResolveBaseClassName` / namespace resolution.

### Files

* `Tools/Opc.Ua.SourceGeneration.Core/Generators/ObjectTypeProxyGenerator.cs`
  — extend with `CollectDeclaredObjectChildren(ObjectTypeDesign)` and
  the emission logic.
* `Tools/Opc.Ua.SourceGeneration.Core/Generators/ObjectTypeProxyTemplates.cs`
  — add `ObjectChildAccessor` template + `ListOfObjectChildAccessors`
  token hook in the existing class template. Also add the shared
  `ResolveChildAsync` helper emission (one per class, gated by whether
  any object children exist).
* `Tools/Opc.Ua.SourceGeneration.Core/Templating/Tokens.cs`
  — add `ListOfObjectChildAccessors` token.

### Tests

* `Tests/Opc.Ua.Client.Tests/StateMachines/TypedSubStateMachineAccessorTests.cs`
  (new) — assert:
  * `AlarmConditionTypeClient.GetShelvingStateAsync` exists with
    return type `ValueTask<ShelvedStateMachineTypeClient?>`.
  * `ExclusiveLimitAlarmTypeClient.GetLimitStateAsync` exists with
    return type `ValueTask<ExclusiveLimitStateMachineTypeClient?>`.
  * The accessor is `public` and `async` (or returns ValueTask).
  * **Lazy + cached** — calling twice on the same parent client
    triggers one browse round-trip; the second call returns the
    same instance reference.
  * Returns `null` when the server reports the child as missing
    (BadNotFound result).

### Naming

`Get{ChildBrowseName}Async`. Examples:
* `AlarmConditionType.ShelvingState` → `GetShelvingStateAsync`
* `ExclusiveLimitAlarmType.LimitState` → `GetLimitStateAsync`
* Vendor `MyAlarmType.VibrationFilter` → `GetVibrationFilterAsync`

No "StateMachine" suffix — the child's browse name is sufficient. The
return type tells the caller it's a state-machine proxy when
applicable.

### Name-collision protection

If `Get{ChildBrowseName}Async` collides with an already-emitted
method (e.g. a method child named `ShelvingState`), the generator
skips the typed accessor emission and emits a comment indicating
the skip. Verified at generation time via a hash-set of already-
emitted method names. (For the standard NodeSet, no such collisions
exist.)

### Out of scope for Phase B

* Variable children (not Object children) — those are handled by the
  generator's variable-projection logic (separate code path).
* Methods declared as Object children of FSM types (none in the
  standard NodeSet).
* `HasSubStateMachine`-referenced sub-SMs (no `<opc:Object>` child
  declaration). The generic `GetSubStateMachineAsync(parentStateId)`
  remains the entry point for that.

## Phase C — Per-spec HasSubStateMachine (deferred)

The current `WithSubStateMachine` attaches the sub-SM as a
HasSubStateMachine-referenced child of the parent FSM root. Per
Part 16 §B.3 the reference should be from the parent state node.

**Cost**: `FluentFiniteStateMachineState` doesn't materialize per-state
NodeStates — states are entries in a private `StateTable` array. To
add per-state HasSubStateMachine references we'd need to either:

* Materialize per-state instance NodeStates (heavy refactor).
* Attach the reference to the SHARED type-level state node
  (`Objects.{Type}_StateName`) — but those are shared across all
  instances of the type, so every instance of the parent FSM type
  would point to the same sub-SM. Wrong.

**Benefit**: clients that strictly browse `HasSubStateMachine` from
state nodes (rare) would discover the sub-SM. Our v1 wiring is from
the FSM root which is sufficient for `GetSubStateMachineAsync` and
for the typed accessors landing in Phase B.

**Decision**: defer indefinitely. Document the current wiring in
`Docs/StateMachines.md` so consumers know to browse
`HasSubStateMachine` from the FSM root (not state nodes).

## Combined PR strategy

Land Phase A + Phase B as one PR. Both are independent and additive;
neither breaks existing functionality. Phase C is documented as a
deliberate non-goal.

## Verification

```
dotnet build UA.slnx -c Debug --nologo
dotnet test Tests/Opc.Ua.Server.Tests --filter "Category=StateMachines"
dotnet test Tests/Opc.Ua.Client.Tests --filter "Category=StateMachines"
```

Acceptance:
* All 90 existing state-machine tests pass unchanged.
* ~3 new client tests for Phase A pass.
* ~3-4 new client/generation tests for Phase B pass.
* Generated proxy diff (sanity-check `AlarmConditionTypeClient`) shows
  the new typed accessor.

## Open questions to resolve during implementation

* **Phase A — sub-SM event filtering race**: in the window between
  the parent transition and the discovery of sub-SM activation, a
  fast sub-SM event might arrive before `currentParentStateId` is
  updated. Mitigation: process channel events strictly serially, and
  on parent events read a FRESH sub-SM snapshot (not the cached one).
* **Phase A — N+1 subscription cost**: pre-subscribing to every
  sub-SM costs server resources. For state machines with many states
  each carrying a sub-SM, this could be significant. Acceptable for
  v2; add a `subscribeAllAtOnce` option in v3 if needed.
* **Phase B — vendor models that override standard-named children**:
  if a vendor extends `AlarmConditionType` and overrides the
  `ShelvingState` child, the generated accessor uses the vendor's
  override (correct). If the vendor adds a new child with a colliding
  browse name (very rare), the generator should detect and skip.
* **Phase B — method name collision**: `Get{ChildBrowseName}Async`
  could collide with a generator-emitted method for a method child.
  Inspect the generator's existing method-name precedence; if needed,
  suffix `StateMachine` (e.g. `GetShelvingStateMachineAsync` vs raw
  `GetShelvingStateAsync`). The example in this plan already uses
  the `*StateMachine` suffix.

## Risks

* **Phase A complexity** — multi-source `IAsyncEnumerable` merging
  via Channel<T> requires careful task lifetime management. The
  implementation is straightforward but easy to get subtly wrong.
  Rubber-duck before commit is mandatory.
* **Phase B generator surface** — modifying `ObjectTypeProxyGenerator`
  may affect every generated proxy in the codebase. Regression risk:
  ensure new emitted accessors don't conflict with existing emitted
  members. Smoke-test against the full standard NodeSet plus the
  WotCon, Gds, and tests/synthetic models.
