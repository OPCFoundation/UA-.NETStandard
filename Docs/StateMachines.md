# Part 16 State Machines

This guide describes the generic, extensible OPC UA Part 16 state-
machine support layered on top of the source-generated ObjectType
proxies and the existing `FiniteStateMachineState` server base.

For the formal model, see
[OPC UA Part 16 — State Machines](https://reference.opcfoundation.org/specs/OPC-10000-16/full).

## Two-mode unified builder

The server side ships **one** fluent builder — `StateMachineBuilder` —
with two complementary modes that can be mixed in a single chain:

| Mode             | Entry point                                            | Use when …                                                                 |
|------------------|--------------------------------------------------------|----------------------------------------------------------------------------|
| **Definition**   | `StateMachineBuilder.Create(parent, ctx, nodeId, name)` | You need a state machine that isn't already declared as a generator-emitted `*StateMachineState` subclass. Declare states, transitions, and cause mappings declaratively. |
| **Lifecycle**    | `StateMachineBuilder.For<TState>(stateMachine, ctx)` or `INodeBuilder<TState>.AsStateMachine()` | You already have a `FiniteStateMachineState` subclass (stack-shipped, generator-emitted, or vendor) and want to attach behavior (enter/exit/transition hooks, method-to-cause bindings, auto-transitions). |

Both modes share the same lifecycle surface (`WithInitialState`,
`OnEnterState`, `OnExitState`, `OnTransition`, `OnBeforeTransition`,
`WithCause`, `WithTimedTransition`, `ConfigureStateMachine`). The
definition methods (`AddState`, `AddTransition`, `OnCause`,
`UseElementNamespace`) are only available in definition mode.

## Quick reference

| Concern                                | Client entry point                                                    | Server entry point                                   |
|----------------------------------------|------------------------------------------------------------------------|------------------------------------------------------|
| Read the current state                 | `StateMachineTypeClient.GetCurrentStateAsync`                          | `FiniteStateMachineState.CurrentState`                |
| Read current state + last transition (finite) | `FiniteStateMachineTypeClient.GetCurrentFiniteStateAsync`        | `FiniteStateMachineState.LastState/LastTransition`     |
| Observe transitions (stream)           | `(Finite)StateMachineTypeClient.ObserveFiniteTransitionsAsync`         | n/a                                                   |
| Wait for a target state                | `(Finite)StateMachineTypeClient.WaitForStateAsync`                     | n/a                                                   |
| Enumerate states / transitions         | `FiniteStateMachineTypeClient.GetAvailableStatesAsync` / `GetAvailableTransitionsAsync` | n/a                                  |
| Build a state machine declaratively    | n/a                                                                    | `StateMachineBuilder.Create` (definition mode)         |
| Attach behavior to an existing FSM     | n/a                                                                    | `StateMachineBuilder.For` / `INodeBuilder.AsStateMachine` (lifecycle mode) |

## Client side

The source-generated `*TypeClient` proxies for state-machine types
(`StateMachineTypeClient`, `FiniteStateMachineTypeClient`,
`ShelvedStateMachineTypeClient`, `ExclusiveLimitStateMachineTypeClient`,
`ProgramStateMachineTypeClient`, and any vendor subtypes) all inherit
the generic API automatically — implemented as extension methods
that hang off the proxy base.

### Read the current state

```csharp
var proxy = new ShelvedStateMachineTypeClient(session, conditionId, telemetry);
FiniteStateSnapshot snap = await proxy.GetCurrentFiniteStateAsync(ct);
Console.WriteLine($"State: {snap.CurrentState} ({snap.CurrentStateId})");
Console.WriteLine($"Last transition: {snap.LastTransition} ({snap.LastTransitionId})");
```

### Stream transitions

Pair the proxy with the session's
[streaming subscription](StreamingSubscription.md):

```csharp
ManagedSession session = ...;
IStreamingSubscription streaming = session.DefaultStreaming;

await foreach (FiniteStateSnapshot snap in proxy
    .ObserveFiniteTransitionsAsync(streaming, ct: ct))
{
    Console.WriteLine($"{snap.Timestamp:O}  -> {snap.CurrentState}");
}
```

Each yielded snapshot is refreshed by reading the four state +
transition variables in one round-trip, so consumers see consistent
typed data per transition.

### Wait for a target state

```csharp
FiniteStateSnapshot reached = await proxy.WaitForStateAsync(
    streaming,
    targetStateId: new NodeId(Objects.ShelvedStateMachineType_TimedShelved),
    timeout: TimeSpan.FromSeconds(30),
    ct: ct);
```

The wait composes `ObserveFiniteTransitionsAsync` with timeout and
cancellation; an immediate match against the current state is
short-circuited.

### Enumerate states + transitions

```csharp
IReadOnlyList<FiniteStateInfo> states =
    await proxy.GetAvailableStatesAsync(ct);
IReadOnlyList<FiniteTransitionInfo> transitions =
    await proxy.GetAvailableTransitionsAsync(ct);
```

Browses the state machine instance's `HasComponent` children and
filters by `StateType` / `TransitionType`. Useful for runtime
introspection of vendor state machines.

### Alarm shelving alignment

`AlarmClient` exposes the same API for the `ShelvingState` child of
every Part 9 alarm condition:

```csharp
AlarmClient alarms = session.GetAlarmClient(telemetry);

FiniteStateSnapshot snap = await alarms.GetShelvingStateAsync(conditionId, ct);

await foreach (FiniteStateSnapshot s in alarms
    .ObserveShelvingTransitionsAsync(conditionId, streaming, ct: ct))
{
    // …
}
```

Both methods delegate to `ShelvedStateMachineTypeClient` internally —
same proxy-delegation pattern used by the rest of `AlarmClient`.

### Vendor extensibility

Vendor concrete state machines declared in a NodeSet (e.g.
`MyVendor:FoodPreparationStateMachineType : FiniteStateMachineType`)
automatically get the generic API. The proxy generator emits a
`FoodPreparationStateMachineTypeClient` that inherits from
`FiniteStateMachineTypeClient`, and every extension method
(`GetCurrentFiniteStateAsync`, `ObserveFiniteTransitionsAsync`,
`WaitForStateAsync`, …) applies through the inheritance chain
transparently. Vendor-declared methods (e.g. `BeginPreparation`) are
emitted as instance methods on the vendor client by the generator.

## Server side — definition mode

Use `StateMachineBuilder.Create(...)` when you need a state machine
that isn't already a generator-emitted subclass. The builder owns a
`FluentFiniteStateMachineState` instance, populates its state /
transition / cause tables from your declarations, and freezes the
definition the moment you read `StateMachine` or attach the first
lifecycle hook.

```csharp
using Opc.Ua.Server.StateMachines;

FluentFiniteStateMachineState sm = StateMachineBuilder
    .Create(parent, systemContext,
        nodeId: new NodeId(/*your numeric id*/, namespaceIndex),
        browseName: new QualifiedName("PowerSwitch", namespaceIndex))
    .AddState(id: 1, "Off", isInitial: true)
    .AddState(id: 2, "On")
    .AddTransition(id: 10, "OffToOn", from: 1, to: 2)
    .AddTransition(id: 20, "OnToOff", from: 2, to: 1)
    .OnCause(causeId: 100, from: 1, transition: 10)
    .OnCause(causeId: 200, from: 2, transition: 20)
    .WithInitialState(1)
    .StateMachine;
```

The builder validates structural integrity at the freeze step (the
first lifecycle method or `StateMachine` access): every transition's
`from`/`to` must reference declared states, every cause mapping must
reference a declared transition, and state / transition / cause ids
must be unique within their tables.

## Server side — lifecycle mode

Use `StateMachineBuilder.For(...)` (or
`INodeBuilder<TState>.AsStateMachine()` inside a fluent node-manager
build pipeline) when you already have a state-machine instance —
stack-shipped (`ShelvedStateMachineState`,
`ExclusiveLimitStateMachineState`, `ProgramStateMachineState`),
generator-emitted (`MyVendor.FoodPreparationStateMachineState`), or
the result of an earlier `StateMachineBuilder.Create(...)` chain.

### Stack-shipped state machine

```csharp
// Existing ShelvedStateMachineState that lives under an alarm condition:
ShelvedStateMachineState shelving = alarm.ShelvingState;

StateMachineBuilder.For(shelving, systemContext)
    .OnEnterState(
        Objects.ShelvedStateMachineType_TimedShelved,
        (ctx, sm) => logger.LogInformation("Alarm timed-shelved"))
    .OnExitState(
        Objects.ShelvedStateMachineType_TimedShelved,
        (ctx, sm) => logger.LogInformation("Alarm un-timed-shelved"))
    .OnTransition((ctx, sm, from, to) =>
        logger.LogDebug("Shelving {From} -> {To}", from, to))
    .WithTimedTransition(
        fromStateId: Objects.ShelvedStateMachineType_OneShotShelved,
        timeout: TimeSpan.FromMinutes(15),
        transitionId: Objects.ShelvedStateMachineType_OneShotShelvedToUnshelved);
```

The lifecycle builder layers on top of any pre-existing
`OnBeforeTransition` / `OnAfterTransition` delegates the state
machine had — the stack-shipped behavior continues to run, with
your handlers wrapping it.

### Inside a fluent node-manager build pipeline

```csharp
public sealed class MyNodeManager : FluentNodeManagerBase
{
    protected override void OnConfigure(INodeManagerBuilder builder)
    {
        builder
            .Node(BrowsePaths.SoftwareUpdate.Installation)
            .As<DI.InstallationStateMachineState>()
            .AsStateMachine()
            .OnEnterState(StateNumbers.Installing,
                (ctx, sm) => StartInstall(ctx))
            .OnTransition((ctx, sm, from, to) =>
                _logger.LogInformation("Install: {From} -> {To}", from, to))
            .WithTimedTransition(
                fromStateId: StateNumbers.Installing,
                timeout: TimeSpan.FromMinutes(10),
                transitionId: StateNumbers.InstallingToFailed,
                causeId: StateNumbers.TimeoutCause);
    }
}
```

`AsStateMachine()` pulls the `ISystemContext` from
`nodeBuilder.Builder.Context`. The `INodeBuilder<TState>` typed view
ensures the underlying node is a `FiniteStateMachineState` subclass
at compile time — no runtime casts.

### Lifecycle ordering

For every transition the dispatcher fires handlers in this order:

```
DispatchBefore:
  builder guards (in registration order)
   ↓ veto on any failure
  [original OnBeforeTransition, if any]
   ↓ framework state update (LastState ← CurrentState, CurrentState ← newState)
DispatchAfter:
  [original OnAfterTransition, if any]
   ↓
  OnExitState(from)   (each registered handler, in order)
   ↓
  OnTransition(from, to)   (each observer, in order)
   ↓
  OnEnterState(to)   (each registered handler, in order)
```

Builder guards run **before** any pre-existing
`OnBeforeTransition` — so they can veto without the original
side-effectful pre-handler running. Builder observers run **after**
the original `OnAfterTransition` so stack-shipped audit / change
notifications complete first.

If multiple handlers are registered for the same state or
transition, they fire in the order they were added. Exceptions
thrown by an individual handler do not interrupt the dispatch — they
are caught and logged via `Debug.WriteLine`.

## How the source-generator integrates

* **Client**: every state-machine `*TypeClient` proxy (generated from
  the standard NodeSet *and* every vendor NodeSet you feed through
  the generator) inherits the streaming + read + browse API
  automatically. No hand-written client per state-machine type.
* **Server**: when a NodeSet declares a concrete subtype of
  `FiniteStateMachineType` (vendor or standard), the generator emits
  a `*StateMachineState` server class with the hardcoded
  `StateTable` / `TransitionTable` / `TransitionMappings` /
  `CauseMappings` overrides. Hook behavior onto that instance via
  `StateMachineBuilder.For(...)` — no need to redeclare the tables.

The unified builder gives you a clean migration path:

* **Have a generator-emitted FSM type?** Lifecycle mode only. Build
  the instance the usual way, then attach behavior with
  `StateMachineBuilder.For(...)`.
* **Need an ad-hoc state machine with no generated subclass?**
  Definition mode. `StateMachineBuilder.Create(...)` constructs a
  `FluentFiniteStateMachineState` and lets you chain definitions and
  lifecycle hooks in one expression.

## Guard clauses (fluent sugar)

The lifecycle builder offers a coarse `OnBeforeTransition` that
fires for every transition; the `When*` family provides per-trigger
sugar that compiles down to the same pipeline but scopes the
predicate automatically.

```csharp
StateMachineBuilder.Create(parent, ctx, nodeId, browseName)
    .AddState(1, "Off", isInitial: true)
    .AddState(2, "On")
    .AddTransition(10, "OffToOn", from: 1, to: 2)
    .AddTransition(20, "OnToOff", from: 2, to: 1)
    .WhenTransition(10, (ctx, sm) => HasPermission(ctx))
    .WhenCause(causeId: 100, (ctx, sm) => sm.SafetyInterlockClear)
    .WhenEnter(toStateId: 2, (ctx, sm) => sm.PreflightOk,
        denyStatus: StatusCodes.BadInvalidState)
    .WhenExit(fromStateId: 2, (ctx, sm) => sm.ShutdownComplete);
```

| Method | Scope | Available in |
|---|---|---|
| `OnBeforeTransition(Func<ctx, sm, transitionId, causeId, ServiceResult>)` | Every transition (caller filters) | Both modes |
| `WhenTransition(transitionId, predicate[, denyStatus])` | Transition matches `transitionId` | Both modes |
| `WhenCause(causeId, predicate[, denyStatus])` | Cause matches `causeId` | Both modes |
| `WhenEnter(toStateId, predicate[, denyStatus])` | Transition would enter `toStateId` | Definition mode only |
| `WhenExit(fromStateId, predicate[, denyStatus])` | Machine is leaving `fromStateId` | Both modes |

Returning `false` from a predicate vetoes the transition with
`BadUserAccessDenied` (or the caller-supplied `denyStatus`).
Multiple guards on the same trigger AND together — the first
predicate returning `false` wins, and its `denyStatus` flows
through to `DoTransition`'s return.

All guards (`On*` + `When*`) share one pipeline in registration
order — the FIRST failing guard wins. This means a `When*` guard
declared before an `OnBeforeTransition` guard runs first.

`WhenEnter` is definition-mode-only because the builder needs the
transition table to map `toStateId` to the matching transition ids.
In lifecycle mode the table is protected on the FSM; use
`WhenTransition` with the explicit transition id instead.

## Sub-state machines (hierarchical state)

OPC UA Part 16 §5.2.3 supports nested state machines via the
`HasSubStateMachine` reference. Standard alarms use this pattern
(`ExclusiveLimitAlarmType.LimitState` is itself an
`ExclusiveLimitStateMachineType` distinguishing
`HighHigh / High / Low / LowLow` while the parent alarm is
`Active`). Vendor process-control servers compose hierarchical
state machines extensively.

### Server side — `WithSubStateMachine`

The unified builder attaches a sub-state-machine to a parent state.
The dispatcher auto-manages the lifecycle: parent enters → child
activates (and resets to its initial state unless
`preserveOnReentry: true` is supplied); parent exits → child is
suspended and rejects subsequent transitions until the next
parent re-entry.

```csharp
FluentFiniteStateMachineState parent = StateMachineBuilder
    .Create(parentNode, ctx, parentNodeId, browseName)
    .AddState(1, "Active", isInitial: true)
    .AddState(2, "Inactive")
    .AddTransition(12, "ActiveToInactive", from: 1, to: 2)
    .AddTransition(21, "InactiveToActive", from: 2, to: 1)
    // Attach a sub-state-machine to the Active parent state.
    .WithSubStateMachine(
        parentStateId: 1,
        browseName: new QualifiedName("LimitState", ns),
        configure: child => child
            .AddState(10, "HighHigh", isInitial: true)
            .AddState(11, "High")
            .AddState(12, "Low")
            .AddState(13, "LowLow")
            .AddTransition(110, "HighHighToHigh", from: 10, to: 11),
        preserveOnReentry: false)
    .StateMachine;
```

Available in **definition mode only** — in lifecycle mode the
parent FSM already declares its sub-state machines as part of the
type definition. Observe them through the client-side sub-SM
accessors below.

While suspended, the child FSM's `DoTransition` and `DoCause`
return `BadInvalidState`. The flag is exposed publicly as
`FluentFiniteStateMachineState.IsSuspended` for diagnostics.

### Client side — sub-SM observation

```csharp
FiniteStateMachineTypeClient parent =
    new FiniteStateMachineTypeClient(session, alarmId, telemetry);

// Resolve the sub-SM attached to a parent state:
FiniteStateMachineTypeClient? limitSm =
    await parent.GetSubStateMachineAsync(
        parentStateNodeId: alarmStateNodeId, telemetry, ct);

if (limitSm != null)
{
    FiniteStateSnapshot snap = await limitSm.GetCurrentFiniteStateAsync(ct);
    Console.WriteLine($"Limit state: {snap.CurrentState}");
}

// Observe combined parent + sub-SM transitions:
await foreach (FiniteStateSnapshot snap in parent
    .ObserveEffectiveStateAsync(streaming, telemetry, ct: ct))
{
    Console.WriteLine($"Parent: {snap.CurrentState}");
    if (snap.SubMachine != null)
    {
        Console.WriteLine($"  Sub: {snap.SubMachine.CurrentState}");
    }
}
```

`FiniteStateSnapshot` gains an optional
`SubMachine: FiniteStateSnapshot?` field carrying the snapshot of
the parent's currently-active sub-SM (or `null` if none).
`ObserveEffectiveStateAsync` yields a combined snapshot each time
**either** the parent transitions **or** the parent's currently-active
sub-SM transitions:

* On every parent transition the yield carries the latest known
  snapshot of the sub-SM attached to the new state (or `null` if
  none).
* On every sub-SM transition that occurs while the parent is in the
  state that owns the sub-SM, the yield carries the new sub-SM
  snapshot. Sub-SM transitions occurring while the parent is in a
  different state are silently discarded.

Under the hood all discovered sub-SMs are subscribed once up-front and
their notifications are multiplexed through a `Channel<T>`; sub-SM
events are filtered against the parent's currently-active state.

### Client side — typed sub-SM accessors (generated)

For every `<opc:Object>` child declared on a generator-emitted
ObjectType, the source generator now emits a typed, lazily-resolved
accessor on the parent type's client. For state-machine types, this
gives ergonomic access to known sub-SMs without needing to know the
state NodeId:

```csharp
// Generated on AlarmConditionTypeClient:
ShelvedStateMachineTypeClient? shelving = await alarm
    .GetShelvingStateAsync(telemetry, ct);

// Generated on ExclusiveLimitAlarmTypeClient:
ExclusiveLimitStateMachineTypeClient? limit = await alarm
    .GetLimitStateAsync(telemetry, ct);
```

Properties of the generated accessor:

* **Typed** — returns the concrete sub-SM proxy type, not the generic
  `FiniteStateMachineTypeClient`.
* **Lazy** — first call resolves the child NodeId via
  `TranslateBrowsePathsToNodeIds` against the parent's `ObjectId`.
  Optional children (`ModellingRule="Optional"`) that aren't exposed
  by the server yield `null`.
* **Cached** — subsequent calls return the same instance without
  another browse round-trip. The cache is per-parent-instance.

Vendor models that add their own `<opc:Object>` children on a custom
ObjectType benefit automatically; the generator emits the same shape
for every Object child it encounters.

### Per-spec `HasSubStateMachine` placement (deferred)

Part 16 §B.3 places the `HasSubStateMachine` reference on the parent
state node, not the FSM root. The fluent builder currently attaches
the reference from the FSM root because `FluentFiniteStateMachineState`
does not materialize per-state instance NodeStates — state nodes are
shared across all instances of the type. Browsing
`HasSubStateMachine` from the FSM root via `GetSubStateMachineAsync`
and the typed accessors above works against this wiring; clients that
strictly browse from the state node will not discover the sub-SM. A
future iteration may materialize per-state instance nodes to align
with the spec; this is tracked but deliberately deferred.

## Extensibility recipes

* **Add convenience properties.** Subclass
  `FluentFiniteStateMachineState`, override `CreateChildren` if you
  need vendor-specific child variables, and call
  `StateMachineBuilder.For<MyVendorFsm>(instance, ctx)` to attach
  behavior.
* **Bind a method node to a cause.** `WithCause(methodNodeId)`
  installs an `OnCallMethod2` handler that calls `DoCause(...)`. The
  cause id is derived from the method NodeId's numeric identifier
  (OPC UA convention); the cause→transition mapping is whatever the
  underlying FSM declared (`OnCause(...)` in definition mode or
  hardcoded in a stack/vendor subclass).
* **Drive auto-transitions.** `WithTimedTransition(fromStateId,
  timeout, transitionId, causeId)` arms a `System.Threading.Timer`
  on every entry into `fromStateId` (including the initial state)
  and cancels it on exit. The timer fires `DoTransition(...)` on a
  thread-pool thread, so the standard transition machinery (events,
  audit, observers) runs as expected.
* **Escape hatch.** `ConfigureStateMachine(Action<TState>)` is
  invoked synchronously with the underlying state machine. Use it
  for properties or methods the builder doesn't surface directly.

## Tests

* `Tests/Opc.Ua.Server.Tests/StateMachines/StateMachineBuilderTests.cs`
  validates definition-mode chaining, validation, and freeze
  semantics.
* `Tests/Opc.Ua.Server.Tests/StateMachines/StateMachineBuilderLifecycleTests.cs`
  exercises every lifecycle method, including timed transitions and
  the layering behavior on top of pre-existing delegates.
* `Tests/Opc.Ua.Server.Tests/StateMachines/FluentFiniteStateMachineStateTests.cs`
  covers the table projections.
* The Part 9 conformance tests in
  `Tests/Opc.Ua.History.Tests/AlarmsAndConditions*.cs` exercise
  `AlarmClient.GetShelvingStateAsync` and
  `ObserveShelvingTransitionsAsync` end-to-end against the
  reference server.

## See also

- [Streaming subscription](StreamingSubscription.md) — the
  `IStreamingSubscription` surface the observation methods consume.
- [Alarms and conditions](AlarmsAndConditions.md) — Part 9 alarm
  client that exposes the shelving-state-machine helpers.
- [Source-generated NodeManagers](SourceGeneratedNodeManagers.md) —
  how vendor NodeSets get their `*TypeClient` proxies emitted.
