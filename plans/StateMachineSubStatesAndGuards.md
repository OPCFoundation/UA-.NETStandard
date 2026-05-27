# Plan: Sub-states + per-transition guards for the state-machine builder

## Q1: Does the current model support sub-states?

**Short answer**: Not natively. Both client and server side lack sub-state-machine awareness in the fluent / typed API.

### Current state

| Capability | Server (fluent builder) | Client (`*TypeClient` extensions) |
|---|---|---|
| Add a `HasSubStateMachine` child to a parent state | ❌ no fluent method — would have to hand-roll via raw `NodeState` APIs | n/a |
| Activate sub-SM on parent-state entry | ❌ caller has to wire `OnEnterState` + manually call `SetState` on the child | n/a |
| Reset / suspend sub-SM on parent-state exit | ❌ caller has to wire `OnExitState` manually | n/a |
| Read sub-SM's current state through a parent client | n/a | ❌ no helper — caller has to browse `HasSubStateMachine`, construct a separate `FiniteStateMachineTypeClient`, then read |
| Observe combined "effective state" (parent + active sub-SM) | n/a | ❌ no helper |

### Why sub-states matter

OPC UA Part 16 §5.2.3 defines `HasSubStateMachine`. The standard alarm hierarchy
uses it heavily:

* `ExclusiveLimitAlarmType.LimitState` IS an `ExclusiveLimitStateMachineType` —
  the parent's `Active` state has a sub-SM that further distinguishes
  `HighHigh / High / Low / LowLow`.
* `ProgramStateMachineType` runs hierarchically — `Running` parent state contains
  several sub-program-step state machines in real applications.
* Vendor process-control servers commonly compose state machines (e.g.
  `Installation.Running` containing a sub-SM for `Verify → Deploy → Activate`).

So sub-states are not exotic — they're the canonical way OPC UA composes
state machines. Today consumers can wire them by hand using raw `NodeState`
APIs, but the fluent builder offers no help and the client API doesn't
recognize them.

## Q2: Does the current model support guard clauses?

**Short answer**: Yes — but only as a single coarse "guard everything" entry
point. A more fluent per-transition / per-cause / per-state-entry shape
would be a useful addition.

### Current state

* `StateMachineBuilder<TState>.OnBeforeTransition(Func<ctx, sm, transitionId, causeId, ServiceResult> guard)`
  fires for **every** transition. The caller must inspect `transitionId` /
  `causeId` to scope the guard.
* Multiple `OnBeforeTransition` calls compose (each registered guard runs
  in registration order — first non-Good wins).

This is functional but not very fluent for the common
"guard a specific transition" pattern:

```csharp
// Today: callers manually filter by transition id
.OnBeforeTransition((ctx, sm, tid, cid) =>
    tid == TransitionIds.Activate
        ? (HasPermission(ctx, sm) ? ServiceResult.Good : StatusCodes.BadUserAccessDenied)
        : ServiceResult.Good)
```

### What a fluent guard API would look like

```csharp
// Proposed sugar — purely additive, compiles down to OnBeforeTransition:
.WhenTransition(TransitionIds.Activate, (ctx, sm) => HasPermission(ctx, sm))
.WhenCause(CauseIds.Acknowledge, (ctx, sm) => sm.CanAcknowledge)
.WhenEnter(StateIds.Running, (ctx, sm) => sm.PreflightOk)
```

Semantics:
* Returning `false` vetoes with `StatusCodes.BadUserAccessDenied` (default) or
  a caller-supplied status code overload.
* Multiple guards on the same trigger are AND-ed.

## Are they useful additions?

**Yes — both are.** Distinct rationale per item:

| Feature | Why useful | Cost |
|---|---|---|
| **Sub-states (fluent + client)** | Standard Part 16 feature; standard alarm types use it (`ExclusiveLimitAlarmType.LimitState`); vendor process-control servers compose hierarchical SMs as the norm; today consumers reinvent it. | ~250-400 lines incl. tests; design complexity around parent-exit / child-reset semantics |
| **Per-transition guards (fluent sugar)** | Common pattern; OnBeforeTransition is awkward for it; matches the "fluent DSL" feel of the rest of the builder. | ~60-100 lines incl. tests; trivial — just filtering on top of OnBeforeTransition |

The guards are low-risk sugar. Sub-states are a more substantive feature
addition but high-value because they unlock Part 16 §5.2.3 semantics that
applications need.

## Proposed scope (this PR)

Land both. The guards land first as a quick win; sub-states land second as
the larger feature.

### Phase 1 — Fluent guards

* `WhenTransition(uint transitionId, Func<ISystemContext, TState, bool> predicate, ServiceResult? denyStatus = null)`
* `WhenCause(uint causeId, Func<ISystemContext, TState, bool> predicate, ServiceResult? denyStatus = null)`
* `WhenEnter(uint stateId, Func<ISystemContext, TState, bool> predicate, ServiceResult? denyStatus = null)`
  — note: "WhenEnter" guards a transition based on the TO-state. Useful for
  symmetric pre-checks like "you can't enter Running without preflight ok".
* `WhenExit(uint stateId, ...)` — guards leaving a state.

Implementation: each `When*` method registers an entry in a dispatch table
inside `StateMachineDispatcher<TState>`. The existing `DispatchBefore` pipeline
gains a per-table lookup step before the global `m_guards` list. Per-table
guards run in registration order; multiple guards on the same trigger AND
together (first `false` returns the deny status).

Tests (~8):
* `WhenTransitionVetoOnFalse`
* `WhenTransitionAllowsOnTrue`
* `WhenCauseScopesToCauseId`
* `WhenEnterScopesToToState`
* `WhenExitScopesToFromState`
* `MultipleGuardsOnSameTransitionComposeWithAnd`
* `GuardsRunBeforeGlobalOnBeforeTransitionGuards`
* `CustomDenyStatusFlowsThroughDoTransitionResult`

### Phase 2 — Sub-state machines (server-side)

* New builder method:
  ```csharp
  public StateMachineBuilder<TState> WithSubStateMachine(
      uint parentStateId,
      QualifiedName browseName,
      Action<StateMachineBuilder<FluentFiniteStateMachineState>> configure);
  ```
* Auto-wires the lifecycle:
  * Adds the child as `HasSubStateMachine`-referenced object of the parent.
  * On entry into `parentStateId`, sub-SM resets to its initial state.
  * On exit from `parentStateId`, sub-SM transitions are suspended.
* Internally creates a `FluentFiniteStateMachineState`, runs the user's
  `configure` against a builder for it, freezes the child definition.
* Stores a registry of `parentStateId → ISubStateMachineHandle` on the
  parent's dispatcher so observers (client side) can query.

Tests (~7):
* `WithSubStateMachineAddsHasSubStateMachineReference`
* `WithSubStateMachineRequiresFluentFiniteStateMachineParent` (or allow
  other parents? scope decision below)
* `SubStateMachineActivatesOnParentStateEntry`
* `SubStateMachineResetsToInitialStateOnReentry`
* `SubStateMachineSuspendedAfterParentStateExit`
* `SubStateMachineTransitionInvokesParentObserver`
* `MultipleSubStateMachinesPerParent` (one per state vs. multiple)

### Phase 3 — Sub-state machines (client-side)

Add to `FiniteStateMachineTypeClientExtensions`:

```csharp
/// <summary>
/// Resolves the sub-state-machine instance attached to
/// <paramref name="parentStateId"/> via <c>HasSubStateMachine</c>.
/// Returns null when no sub-SM is attached.
/// </summary>
public static async ValueTask<FiniteStateMachineTypeClient?>
    GetSubStateMachineAsync(
        this FiniteStateMachineTypeClient parent,
        NodeId parentStateId,
        ITelemetryContext telemetry,
        CancellationToken ct = default);

/// <summary>
/// Yields a snapshot every time either the parent transitions OR the
/// currently active sub-SM transitions. The snapshot's
/// <see cref="FiniteStateSnapshot.SubMachine"/> property
/// (a new field) carries the sub-SM's current state.
/// </summary>
public static IAsyncEnumerable<FiniteStateSnapshot>
    ObserveEffectiveStateAsync(
        this FiniteStateMachineTypeClient parent,
        IStreamingSubscription streaming,
        ITelemetryContext telemetry,
        CancellationToken ct = default);
```

Extend `FiniteStateSnapshot` with optional `SubMachine` field:

```csharp
public sealed record FiniteStateSnapshot(...)
{
    public FiniteStateSnapshot? SubMachine { get; init; }
}
```

Tests (~5):
* `GetSubStateMachineReturnsNullWhenNoSubMachineAttached`
* `GetSubStateMachineResolvesShelvingStateMachineForAlarm`
* `ObserveEffectiveStateYieldsParentOnlyWhenNoSubMachine`
* `ObserveEffectiveStateYieldsSubMachineSnapshotWhenParentActiveStateHasOne`
* `ObserveEffectiveStateSwitchesSubMachineOnParentTransition`

## Open questions to resolve during implementation

1. **Sub-SM persistence across reentry.** Part 16 doesn't strictly mandate
   "reset to initial state on each parent entry". Some implementations
   preserve the sub-SM's last state on re-entry (resume semantics).
   Decision: default to reset; add an opt-in `preserveOnReentry: true`
   parameter on `WithSubStateMachine`.

2. **Lifecycle hook for parent-exit.** When the parent leaves the
   sub-SM-attached state, do we (a) actively transition the sub-SM to a
   sentinel "inactive" state, (b) suspend it (current state retained but
   no transitions accepted), or (c) leave it untouched?
   Decision: (b) suspend — matches the spec wording "not active" without
   spurious state changes. Make `IsCausePermitted` return false while
   suspended.

3. **Generated SubMachine client API for typed sub-SM proxies.**
   `AlarmConditionType.ShelvingState` is typed `ShelvedStateMachineType`. A
   strongly-typed `GetShelvingStateMachineAsync()` returning
   `ShelvedStateMachineTypeClient` would be cleaner than the generic
   `GetSubStateMachineAsync(parentStateId)`. But emitting per-attachment
   typed accessors requires source-generator changes — defer to a v2.

4. **Conflict between `ExclusiveLimitAlarmType` LimitState and the new
   `WithSubStateMachine`.** Standard alarm types ALREADY have hardcoded
   sub-SMs via generated `*State` classes. The new builder must coexist
   with these without redefining them. Decision: `WithSubStateMachine`
   only applies in definition-mode (when the parent is a
   `FluentFiniteStateMachineState`); lifecycle-mode adoption of
   stack-shipped subclasses uses the existing typed sub-SM accessors.

5. **Default deny status for guards.** Returning `false` from a
   predicate defaults to `BadUserAccessDenied`. Some guards want
   `BadInvalidState` or `BadPreconditionNotMet`. The overload taking a
   `ServiceResult` lets callers customize. Document the default.

## Files

### Updated
* `Libraries/Opc.Ua.Server/StateMachines/StateMachineBuilder.cs`
  — add `WhenTransition` / `WhenCause` / `WhenEnter` / `WhenExit` +
  `WithSubStateMachine`.
* `Libraries/Opc.Ua.Client/StateMachines/FiniteStateMachineTypeClientExtensions.cs`
  — add `GetSubStateMachineAsync` / `ObserveEffectiveStateAsync`.
* `Libraries/Opc.Ua.Client/StateMachines/StateMachineSnapshot.cs`
  — add optional `SubMachine` field to `FiniteStateSnapshot`.
* `Docs/StateMachines.md`
  — new sections: "Guard clauses (fluent sugar)", "Sub-state machines".

### New tests
* `Tests/Opc.Ua.Server.Tests/StateMachines/StateMachineBuilderGuardTests.cs`
  (~8 tests)
* `Tests/Opc.Ua.Server.Tests/StateMachines/StateMachineBuilderSubStateTests.cs`
  (~7 tests)
* `Tests/Opc.Ua.Client.Tests/StateMachines/SubStateMachineClientTests.cs`
  (~5 tests)

## Verification

```
dotnet build UA.slnx -c Debug --nologo
dotnet test Tests/Opc.Ua.Server.Tests --filter "Category=StateMachines"
dotnet test Tests/Opc.Ua.Client.Tests --filter "Category=StateMachines"
```

Acceptance: all 48 existing state-machine tests continue to pass; ~20
new tests pass; docs build clean.

## Why two phases in one PR

Phase 1 (guards) is trivial sugar that doesn't change any existing
behavior. Phase 2/3 (sub-states) is the meatier feature. Combining them
keeps a coherent story ("hierarchical state machine support") in a
single PR and avoids the staging cost of a follow-up. If size becomes a
concern, Phase 1 can split out as a quick first PR — but the design is
straightforward and self-contained so I'd land it all together.
