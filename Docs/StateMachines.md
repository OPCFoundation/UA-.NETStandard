# Part 16 State Machines

This guide describes the generic, extensible OPC UA Part 16 state-
machine support layered on top of the source-generated ObjectType
proxies and the existing `FiniteStateMachineState` server base.

For the formal model, see
[OPC UA Part 16 — State Machines](https://reference.opcfoundation.org/specs/OPC-10000-16/full).

## Quick reference

| Concern                                | Client entry point                                                    | Server entry point                       |
|----------------------------------------|------------------------------------------------------------------------|------------------------------------------|
| Read the current state                 | `StateMachineTypeClient.GetCurrentStateAsync`                          | `FiniteStateMachineState.CurrentState`    |
| Read the current state + last transition (finite) | `FiniteStateMachineTypeClient.GetCurrentFiniteStateAsync`   | `FiniteStateMachineState.LastState/Transition` |
| Observe transitions (stream)           | `(Finite)StateMachineTypeClient.ObserveFiniteTransitionsAsync`         | n/a                                       |
| Wait for a target state                | `(Finite)StateMachineTypeClient.WaitForStateAsync`                     | n/a                                       |
| Enumerate states / transitions         | `FiniteStateMachineTypeClient.GetAvailableStatesAsync` / `GetAvailableTransitionsAsync` | n/a                       |
| Build a state machine declaratively    | n/a                                                                    | `StateMachineBuilder` + `FluentFiniteStateMachineState` |

## Client side

The source-generated `*TypeClient` proxies for state-machine types
(`StateMachineTypeClient`, `FiniteStateMachineTypeClient`,
`ShelvedStateMachineTypeClient`, `ExclusiveLimitStateMachineTypeClient`,
`ProgramStateMachineTypeClient`, and any vendor subtypes) all
inherit the generic API automatically — implemented as extension
methods that hang off the proxy base.

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
transparently. Vendor-declared methods (e.g. `BeginPreparation`)
are emitted as instance methods on the vendor client by the
generator.

## Server side

### Building a state machine declaratively

```csharp
using Opc.Ua.Server.StateMachines;

StateMachineDefinition definition = new StateMachineBuilder()
    .AddState(id: 1, "Off", isInitial: true)
    .AddState(id: 2, "On")
    .AddTransition(id: 10, "OffToOn", from: 1, to: 2)
    .AddTransition(id: 20, "OnToOff", from: 2, to: 1)
    .OnCause(causeId: 100, from: 1, transition: 10)
    .OnCause(causeId: 200, from: 2, transition: 20)
    .Build();

var sm = new FluentFiniteStateMachineState(parent, definition);
sm.Create(systemContext, ...);
```

`FluentFiniteStateMachineState` is a generic `FiniteStateMachineState`
subclass whose `StateTable` / `TransitionTable` / `TransitionMappings`
/ `CauseMappings` overrides read from the supplied definition. No
hand-rolled boilerplate tables; the builder validates structural
integrity (every transition's `from`/`to` must reference declared
states, every cause mapping must reference a declared transition,
ids unique).

### Extensibility recipes

* **Add convenience properties.** Subclass
  `FluentFiniteStateMachineState`, override `CreateChildren` if you
  need to add vendor-specific child variables, and keep the
  definition-driven tables intact.
* **Reuse a generator-emitted concrete subtype.** Construct an
  instance of the generated state class
  (e.g. `ShelvedStateMachineState`) and **leave its hardcoded
  tables in place** — they are authoritative for standard NodeSet
  types. Use the fluent builder only for ad-hoc / vendor types
  outside the standard set.
* **Hook transitions.** The existing
  `FiniteStateMachineState.OnBeforeTransition`,
  `OnAfterTransition`, and `OnCheckUserPermission` callbacks work
  unchanged on `FluentFiniteStateMachineState` — assign delegates
  after construction:

  ```csharp
  sm.OnBeforeTransition = (ctx, machine, transitionId, causeId, ins, outs) =>
      hasPermission ? ServiceResult.Good : StatusCodes.BadUserAccessDenied;
  ```

## Tests

* `Tests/Opc.Ua.Server.Tests/StateMachines/StateMachineBuilderTests.cs`
  validates the builder + `FluentFiniteStateMachineState` round-trip.
* The Part 9 conformance tests in
  `Tests/Opc.Ua.History.Tests/AlarmsAndConditions*.cs` exercise
  `AlarmClient.GetShelvingStateAsync` /
  `ObserveShelvingTransitionsAsync` end-to-end against the
  reference server.

## See also

- [Streaming subscription](StreamingSubscription.md) — the
  `IStreamingSubscription` surface the observation methods consume.
- [Alarms and conditions](AlarmsAndConditions.md) — Part 9 alarm
  client that exposes the shelving-state-machine helpers.
- [Source-generated NodeManagers](SourceGeneratedNodeManagers.md)
  — how vendor NodeSets get their `*TypeClient` proxies emitted.
