# State machine — remaining work

The Part 16 state machine implementation (definition + lifecycle
builder modes, sub-state machines, per-transition guards, typed
Object-child accessors, transitive sub-SM observation, async
lifecycle hooks) is fully shipped. One item is intentionally
deferred.

## Deferred: per-spec `HasSubStateMachine` reference placement

OPC UA Part 16 §B.3 places the `HasSubStateMachine` reference on the
parent *state* node. The current implementation attaches it from the
FSM root because `FluentFiniteStateMachineState` does not materialize
per-state instance NodeStates — states are entries in a private
table, not separate NodeStates. The shipped wiring is browseable
from the FSM root via `GetSubStateMachineAsync` and the
source-generated typed accessors, so existing clients keep working.

Strict per-spec placement would require either:

* materializing per-state instance NodeStates (heavy refactor of
  `FluentFiniteStateMachineState`), or
* attaching the reference to the shared type-level state nodes
  (`Objects.{Type}_StateName`) — incorrect because those are shared
  across every instance of the parent FSM type.

**Status:** documented as deferred in `Docs/StateMachines.md`; no
known consumer relies on the spec-strict placement.
