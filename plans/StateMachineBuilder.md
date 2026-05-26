# Fluent state-machine configurator — deferred plan

> **Status:** Designed but not implemented. Extracted from the Full DI
> compliance work to ship independently when prioritised.

## Problem statement

Multiple OPC UA features are modelled as state machines:

- **DI software update** — 4 sub-state-machines (`PrepareForUpdate`,
  `Installation`, `PowerCycle`, `Confirmation`).
- **Alarms** — `ShelvedStateMachineState`, `ExclusiveLimitStateMachineState`.
- **Programs** — `ProgramStateMachineState`.
- **Custom domain-specific machines** — pump operating mode, conveyor
  belt phases, batch process steps, etc.

The current `Opc.Ua.Core.Types.State.FiniteStateMachineState` provides
the engine (state/transition tables, mappings, cause lookup) but
nothing fluent on top of it. Each consumer reimplements the same
"wire `OnCall`, mutate `CurrentState`, fire `TransitionEvent`" boilerplate.

## Goals

- Reduce per-state-machine wiring boilerplate from ~50 lines to ~5.
- Integrate cleanly with G2 alarms (shelving) and G4 simulation timer
  (timed transitions).
- AOT-safe, no reflection.
- Configurator only — operate over existing generated/stack
  `FiniteStateMachineState` subclasses. **Do not** attempt arbitrary
  runtime state-table construction (rubber-duck flagged this as out of
  scope for v1 because stack subclasses hardcode their tables).

## Proposed surface

### `Libraries/Opc.Ua.Server/Fluent/IStateMachineBuilder.cs`

```csharp
public interface IStateMachineBuilder<TState>
    where TState : FiniteStateMachineState
{
    TState StateMachine { get; }
    INodeBuilder Builder { get; }

    /// Sets the initial CurrentState / Number / Id on the machine.
    IStateMachineBuilder<TState> WithInitialState(uint stateNumber);

    /// Runs when CurrentState transitions to the specified state.
    IStateMachineBuilder<TState> OnEnterState(uint stateNumber,
        Action<ISystemContext, TState> handler);
    IStateMachineBuilder<TState> OnExitState(uint stateNumber,
        Action<ISystemContext, TState> handler);

    /// Generic transition observer.
    IStateMachineBuilder<TState> OnTransition(
        Action<ISystemContext, TState, uint /*from*/, uint /*to*/> handler);

    /// Pre-transition guard; return non-Good to cancel.
    IStateMachineBuilder<TState> OnBeforeTransition(
        Func<ISystemContext, TState, uint, uint, ServiceResult> guard);

    /// Binds a method node to drive a transition when called.
    IStateMachineBuilder<TState> WithCause(NodeId methodNodeId, uint transitionNumber);

    /// Auto-transition on timeout (G4 simulation timer integration).
    IStateMachineBuilder<TState> WithTimedTransition(
        uint fromStateNumber, TimeSpan timeout, uint toStateNumber);

    /// Escape hatch for full state-class access.
    IStateMachineBuilder<TState> ConfigureStateMachine(Action<TState> configure);
}
```

### Entry-point extension

```csharp
public static IStateMachineBuilder<TState> AsStateMachine<TState>(
    this INodeBuilder<TState> nodeBuilder)
    where TState : FiniteStateMachineState;

// Convenience helpers for stack-shipped subclasses:
public static IStateMachineBuilder<ProgramStateMachineState>
    CreateProgramStateMachine(this INodeBuilder parent, QualifiedName browseName);
public static IStateMachineBuilder<ShelvedStateMachineState>
    CreateShelvedStateMachine(this INodeBuilder parent, QualifiedName browseName);
public static IStateMachineBuilder<ExclusiveLimitStateMachineState>
    CreateExclusiveLimitStateMachine(this INodeBuilder parent, QualifiedName browseName);
```

### Example use

```csharp
builder.Node("SoftwareUpdate/Installation")
    .As<DI.InstallationStateMachineState>()
    .AsStateMachine()
    .WithInitialState(StateNumbers.Idle)
    .OnEnterState(StateNumbers.Installing, (ctx, sm) => StartInstall(packagePath))
    .WithTimedTransition(
        fromStateNumber: StateNumbers.Installing,
        timeout: TimeSpan.FromMinutes(10),
        toStateNumber:   StateNumbers.InstallationFailed)
    .OnTransition((ctx, sm, from, to) =>
        m_logger.LogInformation("State {From} → {To}", from, to));
```

### Client-side observer

`Libraries/Opc.Ua.Di.Client/StateMachineClient.cs`:

```csharp
public sealed class StateMachineClient
{
    public StateMachineClient(ISession session, NodeId stateMachineNodeId, …);

    public event EventHandler<StateTransitionEventArgs>? Transitioned;

    public ValueTask InvokeCauseAsync(NodeId methodNodeId, CancellationToken ct = default);
    public ValueTask<uint> ReadCurrentStateAsync(CancellationToken ct = default);
}
```

## Files to create

- `Libraries/Opc.Ua.Server/Fluent/IStateMachineBuilder.cs`
- `Libraries/Opc.Ua.Server/Fluent/StateMachineBuilderExtensions.cs`
- `Libraries/Opc.Ua.Server/Fluent/StateMachineBuilder.cs` (internal impl)
- `Libraries/Opc.Ua.Di.Client/StateMachineClient.cs`
- `Tests/Opc.Ua.Server.Tests/Fluent/StateMachineBuilderExtensionsTests.cs`
- `Docs/StateMachineBuilder.md`

## Open questions

1. **Generator integration** — when the source generator emits a state
   machine subclass with a hardcoded state table, should it also emit
   `StateNumbers` / `TransitionNumbers` enums for the state machine?
2. **OnTransition vs WithTransitionHandler** — `Func<…, ServiceResult>`
   (cancellation) or simple `Action`?
3. **Method binding mechanism** — install an `OnCall` handler internally
   that calls `m.CauseProcessingCompleted(…)`, or expose a lower-level
   `WithMethodHandler` and let the user wire the cause table?
4. **Timed transitions and G4 dependency** — `WithTimedTransition`
   requires `FluentNodeManagerBase` (for the simulation registry).
   Enforce statically or fail at runtime?

## Tests (planned ~12)

- `WithInitialStateSetsCurrentStateProperty`
- `OnEnterStateFiresWhenStateChanges`
- `OnExitStateFiresWhenLeavingState`
- `OnTransitionReceivesFromAndToNumbers`
- `OnBeforeTransitionCancelsWithBadStatus`
- `WithCauseBindsMethodToTransition`
- `WithTimedTransitionFiresAfterTimeout`
- `WithTimedTransitionCancelledOnDispose`
- `ConfigureStateMachineEscapeHatch`
- `CreateProgramStateMachineHappyPath`
- `CreateShelvedStateMachineIntegratesWithAlarm`
- `StateMachineClient_TransitionEventRoundTrip` (end-to-end)

## Why deferred

The state-machine work is a self-contained foundation, but its main
internal consumer (DI software update) can implement state transitions
directly via the existing `FiniteStateMachineState` API for v1. The
generalised builder can ship later without blocking the rest of the DI
compliance work.

When this plan is picked up:
- It does **not** depend on Phase 8B/8C/8D being complete.
- It does benefit from G4 (simulation timer) being in place — already
  done in Phase 7.
- The DI software-update implementation in Phase 8E can be refactored
  to use the builder once available (mechanical migration — replace
  inline `OnAfterChange` handlers with `.OnTransition` calls).
