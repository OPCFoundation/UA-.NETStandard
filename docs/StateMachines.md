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
`WithCause`, `WithTimedTransition`, `ConfigureStateMachine`). Async
overloads — `OnEnterStateAsync`, `OnExitStateAsync`,
`OnTransitionAsync` — accept `Func<ISystemContext, TState,
CancellationToken, ValueTask>` (or the four-arg form for transition
observers) and are invoked **fire-and-forget on the thread pool**
from the synchronous transition path. The handler therefore runs on
a fully-async path (no `GetAwaiter().GetResult()` / `Wait()` /
`.Result` anywhere) without blocking the transition or the calling
client. Exceptions thrown from an async handler are captured and
logged via `Debug.WriteLine`. Pre-transition guards
(`OnBeforeTransition`, `When*`) intentionally remain sync-only
because they must veto the transition synchronously before the
state-machine state mutation completes. The definition methods
(`AddState`, `AddTransition`, `OnCause`, `UseElementNamespace`) are
only available in definition mode.

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
[streaming subscription](Subscriptions.md#streaming-subscriptions):

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

Reads the optional `AvailableStates` / `AvailableTransitions` NodeId-array
properties and then reads each referenced node's browse name and number.
An absent or invalid availability property produces an empty result.

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

### Device Integration (DI) software-update alignment

`SoftwareUpdateClient` (in `Opc.Ua.Di.Client`) wraps the same generic
API for each of the four child state machines of OPC 10000-100 §10.3
(`PrepareForUpdate`, `Installation`, `Confirmation`, `PowerCycle`):

```csharp
var su = new SoftwareUpdateClient(session, suNodeId, telemetry);

FiniteStateSnapshot? state = await su.GetInstallationStateAsync(ct);

await foreach (FiniteStateSnapshot snap in su
    .ObserveInstallationTransitionsAsync(streaming, options: null, ct))
{
    // …
}

await su.InstallSoftwarePackageAsync("urn:acme", "2.0.0",
    ArrayOf.Empty<string>(), default, ct);
```

See [`SoftwareUpdate.md`](SoftwareUpdate.md) for the full SU
facet — including the server-side `OnInstallationStateChanged` /
`OnPrepareStateChanged` / `OnConfirmStateChanged` instrumentation
hooks that the DI server attaches to its built-in FSM lifecycle.

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

`WithSubStateMachine` and `WithInitialState` are order-independent:
whichever is called second brings the sub-SM in line with the
parent's current state. (`WithInitialState` goes through `SetState`,
which is not a transition and so still runs no user lifecycle
handler — but it does re-arm timed transitions and synchronize
sub-state machines.)

A state declared with `isInitial: true` is applied automatically when
the machine is created and its state node is materialized as an
`InitialStateType` (OPC 10000-16 §4.4.10), so the minimal example
above is fully functional without an explicit `WithInitialState`
call; call `WithInitialState` to start in a different state. A
sub-state machine's declared initial state is applied only while its
parent is in the attached state.

While suspended, the child FSM's `CurrentState` and `LastTransition`
read with `Bad_StateNotActive` (per OPC 10000-16 §4.4.6), and its
`DoTransition` / `DoCause` return `BadStateNotActive`. The flag is
exposed publicly as `FluentFiniteStateMachineState.IsSuspended` for
diagnostics.

The resulting address space follows Part 16 §4.4.16 and the standard
NodeSets:

```
LimitAlarm            --HasComponent-->       Active   (StateType)
Active                --HasSubStateMachine--> LimitState
LimitAlarm            --HasComponent-->       LimitState
```

`HasSubStateMachine` is a **non-hierarchical** reference, so it
cannot double as the parent/child reference — the sub-SM is a
`HasComponent` child of the state machine and the spec reference
hangs off the parent *state* node.

### Server side — materialized state and transition nodes

To give `HasSubStateMachine` a state node to hang off — and to make
`CurrentState/Id` and `LastTransition/Id` resolve to something a
client can actually browse — `FluentFiniteStateMachineState`
materializes a node per declared state and transition when the machine
is created. All of them are `HasComponent` children of the machine.

**States** — one `StateType` node each, carrying a `StateNumber`
property equal to the numeric state id. `CurrentState/Id` points at
the node for the current state and tracks it across transitions, and
the optional `AvailableStates` property lists them, so
`GetAvailableStatesAsync` works against a fluent-built server.

**Transitions** — one `TransitionType` node each, carrying a
`TransitionNumber` property and the Part 16 §4.4.11 references:

* `FromState` / `ToState` to the two state nodes;
* `HasEffect` to `TransitionEventType`, unless the transition was
  declared with `hasEffect: false` — forward-only, matching how
  companion NodeSets declare it (the target is a standard node owned
  by another node manager, so no inverse edge is registered there);
* `HasCause` to the method node, added by `WithCause(methodNodeId)`
  for every transition the cause can trigger.

`AvailableTransitions` lists them, and the optional `LastTransition`
variable is materialized too — without it the base class has nowhere
to record a completed transition and `ObserveFiniteTransitionsAsync`
has nothing to subscribe to.

NodeIds are **per instance**, derived from the machine's own NodeId,
so several machines built from one definition coexist in a single
address space. Use `sm.GetStateId(nodeId)` / `sm.GetTransitionId(nodeId)`
and their inverses `sm.GetStateNodeId(stateId)` /
`sm.GetTransitionNodeId(transitionId)` to move between a NodeId and
the numeric id used by `AddState` / `AddTransition` / `OnCause` and
the lifecycle hooks — never parse the NodeId's identifier directly.

Element nodes land in the namespace given to `UseElementNamespace`,
or in the state machine's own namespace when that was not called or
the URI is not registered with the server (the element namespace
defaults to the OPC UA namespace, which must never host vendor
nodes).

Because element NodeIds are derived from browse names, browse names
must be unique across states and transitions (and must not shadow the
standard `CurrentState` / `LastTransition` / `AvailableStates` /
`AvailableTransitions` children); the builder rejects duplicates when
the definition freezes.

Cost is one node plus one property per declared state and transition,
per machine instance — worth weighing for servers that instantiate
many machines.

### Executable causes

A cause is a Method, and whether it may fire depends on the state the
machine is in. Part 3 §5.7 defines `Executable` / `UserExecutable` as
exactly that question, and Part 4 §5.11.2 has `Call` refuse a Method
that is not executable with `Bad_NotExecutable`.

`WithCause(methodNodeId)` therefore answers both attributes from
`IsCausePermitted(context, causeId, checkUserAccessRights)` alongside
installing the call handler — `Executable` ignoring user rights,
`UserExecutable` running the machine's `OnCheckUserPermission`
callback as well. A client can read them after every transition and
offer only the causes that currently apply, instead of duplicating the
server's state table or calling one to find out.

Pass `reportExecutable: false` to opt out, for servers that
deliberately keep their methods executable and answer at call time.

The stack-shipped `ProgramStateMachineState` does the same for its own
`Start` / `Suspend` / `Resume` / `Halt` / `Reset` methods when they are
created, so a Program server no longer repeats the wiring per method.
Those five are optional placeholders on the type, so only the ones an
instance declares are wired, matched on browse name alone — a subtype
that redeclares them puts them in its own namespace. Call
`WireCauseMethods(context)` yourself if you add one to an
already-created instance; `Create` rebuilds the child list from the
type template, so children added by hand before it are not there when
the automatic pass runs.

### Client side — sub-SM observation

```csharp
FiniteStateMachineTypeClient parent =
    new FiniteStateMachineTypeClient(session, alarmId, telemetry);

// The state NodeId comes from AvailableStates ...
IReadOnlyList<FiniteStateInfo> states =
    await parent.GetAvailableStatesAsync(ct);
NodeId activeStateId = states.First(s => s.BrowseName.Name == "Active").NodeId;

// ... or from a snapshot's CurrentStateId, to reach the sub-SM of
// whatever state the machine is in right now.
FiniteStateMachineTypeClient? limitSm =
    await parent.GetSubStateMachineAsync(
        parentStateNodeId: activeStateId, telemetry, ct);

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

### Fluent builder behavior — `HasSubStateMachine` is on the state node

Earlier fluent-builder implementations attached `HasSubStateMachine` to the state machine
**root**, because the fluent builder did not materialize state nodes.
It now sits on the parent **state** node per Part 16 §4.4.16, and the
root reference is gone.

Callers that passed the state machine's `ObjectId` to
`GetSubStateMachineAsync` must pass a state NodeId instead — from
`GetAvailableStatesAsync` or from a snapshot's `CurrentStateId`.

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
  hardcoded in a stack/vendor subclass). It also answers the method's
  `Executable` / `UserExecutable` attributes from `IsCausePermitted`
  — see [Executable causes](#executable-causes).
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

* `tests/Opc.Ua.Server.Tests/StateMachines/StateMachineBuilderTests.cs`
  validates definition-mode chaining, validation, and freeze
  semantics.
* `tests/Opc.Ua.Server.Tests/StateMachines/StateMachineBuilderLifecycleTests.cs`
  exercises every lifecycle method, including timed transitions and
  the layering behavior on top of pre-existing delegates.
* `tests/Opc.Ua.Server.Tests/StateMachines/FluentFiniteStateMachineStateTests.cs`
  covers the table projections.
* `tests/Opc.Ua.Server.Tests/StateMachines/StateMachineBuilderCauseExecutableTests.cs`
  and `tests/Opc.Ua.Core.Tests/Stack/State/ProgramStateMachineStateTests.cs`
  cover the `Executable` / `UserExecutable` reporting of causes.
* The Part 9 conformance tests in
  `tests/Opc.Ua.History.Tests/AlarmsAndConditions*.cs` exercise
  `AlarmClient.GetShelvingStateAsync` and
  `ObserveShelvingTransitionsAsync` end-to-end against the
  reference server.

## See also

- [Subscriptions and Monitored Items](Subscriptions.md#streaming-subscriptions) — the
  `IStreamingSubscription` surface the observation methods consume.
- [Alarms and conditions](AlarmsAndConditions.md) — Part 9 alarm
  client that exposes the shelving-state-machine helpers.
- [Device Integration (DI) software-update facet](SoftwareUpdate.md)
  — the four DI SU state machines, the typed
  `SoftwareUpdateClient.StateMachine` partial, and the server-side
  `On*StateChanged` instrumentation hooks.
- [Source-generated node sources](NodeManagers.md#source-generated-node-sources) —
  how vendor NodeSets get their `*TypeClient` proxies emitted.
