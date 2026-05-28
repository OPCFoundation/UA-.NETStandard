# Fluent state-machine builder

The `IStateMachineBuilder<TState>` fluent surface configures any
`FiniteStateMachineState` subclass — including the source-generated
companion-spec proxies (`InstallationStateMachineState`,
`PrepareForUpdateStateMachineState`, ...) and the stack-shipped
subclasses (`ProgramStateMachineState`, `ShelvedStateMachineState`,
`ExclusiveLimitStateMachineState`).

Goals:

- Reduce per-state-machine wiring boilerplate to a fluent chain.
- Compose with any pre-wired stack handlers (no silent overwrite).
- Integrate with the simulation registry for timed transitions.
- AOT-safe; no reflection.
- Configurator only — the builder does NOT attempt to build state /
  transition / cause tables at runtime. Those remain compiled into
  the subclass per the model design.

## Entry point

```csharp
INodeBuilder<TState> nodeBuilder =
    builder.Node<TState>("Path/To/StateMachine");

IStateMachineBuilder<TState> smBuilder = nodeBuilder.AsStateMachine();
```

`AsStateMachine<TState>()` is the only entry point. The returned
builder installs composed `OnBeforeTransition` / `OnAfterTransition`
coordinators on the underlying state machine that preserve any
pre-existing handler and fan out to user-registered callbacks.

## Surface

| Method | Purpose |
|--------|---------|
| `WithInitialState(uint stateId)` | Force initial state via `SetState`. Does NOT fire transition callbacks (matches stack semantics). |
| `OnEnterState(uint stateId, Action)` | Fired after the machine transitions INTO the given state. |
| `OnExitState(uint stateId, Action)` | Fired after the machine transitions OUT of the given state. |
| `OnTransition(Action<from, to>)` | Fired on every successful transition. Receives the `fromStateId` (captured pre-transition) and `toStateId`. |
| `OnBeforeTransition(Func<fromStateId, ServiceResult>)` | Pre-transition guard. Return non-`Good` to cancel. |
| `WithCause(NodeId methodId, uint causeId)` | Wires the method to invoke `DoCause` with the given cause ID. |
| `WithTimedTransition(uint fromStateId, TimeSpan timeout, uint causeId)` | Auto-fires `DoCause` when the machine has been in `fromStateId` for `timeout`. Requires `FluentNodeManagerBase`. |
| `ConfigureStateMachine(Action<TState>)` | Escape hatch for full state-class access. |
| `Done()` | Returns the owning `INodeBuilder` for chain termination. |

### Identifier semantics

All `stateId` / `transitionId` / `causeId` parameters are the **internal
numeric IDs** assigned by the subclass's state / transition / cause
tables — typically OPC UA NodeIds like
`Objects.ProgramStateMachineType_Ready` or
`Methods.ProgramStateMachineType_Start`.

**They are NOT the OPC UA `Number` field** (`1`, `2`, `3`, ...). The
stack's `SetState`, `GetCurrentStateId`, `TransitionMappings`, and
`CauseMappings` are all keyed by these internal IDs, not the
`Number`. Use the generated `StateIds` / `TransitionIds` / `MethodIds`
constants from the model whenever possible.

## Example — ProgramStateMachineState

```csharp
builder
    .Node<ProgramStateMachineState>("Programs/Player")
    .AsStateMachine()
    .WithInitialState(Objects.ProgramStateMachineType_Ready)
    .OnEnterState(Objects.ProgramStateMachineType_Running,
        (ctx, sm) => m_logger.LogInformation("Program started."))
    .OnExitState(Objects.ProgramStateMachineType_Running,
        (ctx, sm) => m_logger.LogInformation("Program ended."))
    .OnBeforeTransition((ctx, sm, fromStateId) =>
        m_user.HasRole(Roles.Operator) ? ServiceResult.Good : StatusCodes.BadUserAccessDenied)
    .WithCause(
        startMethodNodeId,
        Methods.ProgramStateMachineType_Start)
    .WithCause(
        haltMethodNodeId,
        Methods.ProgramStateMachineType_Halt);
```

## Example — Timed transition

`WithTimedTransition` arms a timer when the machine enters
`fromStateId` and auto-fires the cause when the timer expires. The
owner manager must derive from `FluentNodeManagerBase` because the
simulation registry drives the timer.

```csharp
builder
    .Node<InstallationStateMachineState>("SoftwareUpdate/Installation")
    .AsStateMachine()
    .WithTimedTransition(
        fromStateId: Objects.InstallationStateMachineType_Installing,
        timeout: TimeSpan.FromMinutes(10),
        causeId: Methods.InstallationStateMachineType_InstallationFailed);
```

The tick interval is 100 ms — fine enough for second-scale timeouts.
If the machine transitions off the armed state before the timeout
elapses (server-driven by a method call or guard), the timer
de-arms automatically. Re-arming on re-entry is automatic.

## Composition with existing handlers

The builder is non-destructive: pre-existing
`OnBeforeTransition` / `OnAfterTransition` handlers on the state
machine are captured at `AsStateMachine()` time and invoked alongside
the builder's coordinator.

Execution order:

1. **Before** — existing `OnBeforeTransition` first; if it returns a
   bad result, the transition is cancelled and no fluent guards run.
   Otherwise fluent guards run in registration order. First bad
   result wins.
2. **After** — existing `OnAfterTransition` first; then fluent
   `OnExitState` (for `fromStateId`), then `OnEnterState` (for
   `toStateId`), then `OnTransition` handlers. Observer exceptions
   are caught and do not abort the transition.

## Threading

State-machine transitions are inherently sequential on a single
`FiniteStateMachineState` instance — only one transition is in flight
at any time. The builder relies on this guarantee for the
`fromStateId` capture (set in `OnBeforeTransition`, consumed in
`OnAfterTransition`).

Concurrent transitions across separate state machine instances are
fully independent.

## Plan-question resolutions

The `plans/StateMachineBuilder.md` design listed four open questions.
This implementation resolves them as follows:

1. **Generator emits StateNumbers / TransitionNumbers enums?** — Out
   of scope for v1; the source generator already emits well-known
   NodeId constants under `Objects.*` / `Methods.*` which serve the
   same purpose. A future generator pass can synthesise typed enums.
2. **OnTransition signature** — `Action`; `OnBeforeTransition` is the
   `Func<…, ServiceResult>` guard variant.
3. **Method binding mechanism** — `WithCause` installs an internal
   `OnCallMethod2` handler that invokes `DoCause(context, method,
   causeId, inputs, outputs)`. The stack's cause-to-transition
   mapping is honoured. Pre-wired methods are rejected with
   `BadInvalidState` — use `ConfigureStateMachine` for full escape.
4. **Timed transitions and `FluentNodeManagerBase`** — fail-fast with
   `InvalidOperationException` when the owning manager is not a
   `FluentNodeManagerBase`.

## Limitations and future work

- **Pre-transition `to` state** is not exposed to guards (the stack
  computes `to` from protected `TransitionMappings`). Guards see only
  `fromStateId`. Workaround: read `sm.CurrentState.Id.Value` directly
  if you need the current state from inside the guard body.
- **Client-side observer** (`StateMachineClient` with event
  subscription on `TransitionEventType`) is deferred to a follow-up
  PR — the surface depends on session event-filter wiring that is
  its own scope.
- **CreateProgramStateMachine / CreateShelvedStateMachine / …
  helpers** that materialise the state machine instance under a
  parent are deferred. The current builder requires the state
  machine to be present in the address space; use
  `NodeManagerBuilder.Node<TState>(...)` to resolve it.
