# Generators (generating sets)

The draft **Generators companion specification**
(`http://opcfoundation.org/UA/Generators/`) realised end to end by
[`GeneratorServer`](README.md),
and composed with the pump sample by
[`SiteCompositionServer`](../SiteCompositionServer).

## The model

A generating set is two things at once: an **asset** with a nameplate and a
health, and a **machine** whose core is an engine coupled to an alternator. The
specification layers on existing building blocks rather than reinventing them:

| Layer | Role |
|---|---|
| DI (OPC 10000-100) | `GeneratorSetType` derives from `DeviceType`, inheriting the nameplate and `DeviceHealth`; each subsystem derives from `ComponentType` |
| Machinery (OPC 40001-1) | `Identification` add-in and the `MachineryItemState` / `MachineryOperationMode` building blocks |
| Part 9 | `GeneratorProtectionAlarmType`, a subtype of `OffNormalAlarmType` |
| Part 16 | `GeneratorStateMachineType`, a twelve-state `FiniteStateMachineType` |

Composition over deep inheritance: subsystems are separate `ComponentType`
subtypes referenced by `HasComponent`, so a set is assembled from exactly the
components it has.

## Source generation

The sample generates the model locally from vendored NodeSet2 files rather than
referencing a library:

```xml
<AdditionalFiles Include="Model\Opc.Ua.Generators.NodeSet2.xml">
  <ModelSourceGeneratorPrefix>Opc.Ua.Generators</ModelSourceGeneratorPrefix>
</AdditionalFiles>
<AdditionalFiles Include="Model\Opc.Ua.Generators.NodeSet2.csv" />
```

The CSV alongside each XML is the **stable NodeId table**. Without it the
generator assigns synthetic NodeIds at compile time, which breaks byte-stable
wire formats for clients that cache NodeIds across builds.

### The Machinery dependency

The specification composes three Machinery types — `MachineryItemState_StateMachineType`,
`MachineryOperationModeStateMachineType` and `MachineIdentificationType` — that the
reduced Machinery nodeset carried by the pump sample does not define. The full
official nodeset defines them but **does not survive the model source generator**
(it fails with `MODELGEN003`), and it declares a dependency on the IA namespace
through a single optional `Stacklight` member that a generating set does not have.

`Model/prepare_machinery_nodeset.py` therefore derives a reduced-but-sufficient
set from the official nodeset by whitelist, strips the references left dangling
and drops the IA dependency. Deriving it mechanically keeps the provenance
checkable, and the whitelist is the only thing to edit when more types are needed.

Removing the IA URI is index-safe because it is the last entry in
`NamespaceUris`, so the Machinery (1) and DI (2) indices used throughout the file
do not move.

## Simulation design

The sample's organising idea is that **load fraction is the only independent
variable**. Everything else is a function of it:

```
V̇_f(x) = 3.67 + 100·x                      fuel rate       [L/h]
η(x)   = P(x) / (V̇_f(x) · ρ · LHV)         efficiency
S      = P / PF                            apparent power  [VA]
I      = S / (√3 · V_LL)                   line current    [A]
f      = N · p / 120                       frequency       [Hz]
```

This is not a stylistic choice. When each measurement is an independent
oscillator — as the pump sample's simulation once was — a server happily
publishes a duty point that no real machine could occupy, and no test can catch
it because there is nothing for the values to be inconsistent *with*. Deriving
them from one variable means `P = √3·V·I·PF` and `η = P/(V̇·ρ·LHV)` hold at every
tick by construction, and `GeneratorDatasheetConformanceTests` asserts exactly
that.

The published datasheet, the engineering ranges, the trip points and the
simulation all read the same constants, so they cannot drift apart.

## Control surfaces

The specification defines three ways to observe and command a set, and all three
are driven from the **same** decider: the simulation's operating state. This is
the design point worth copying. A state machine advanced independently of the
physics that produced it will eventually report a machine as running while the
simulation says it is stopped — and because both halves are internally consistent,
nothing at run time notices.

### `GeneratorStateMachineType`

Twelve states and twenty-two declared transitions, mandatory on the type. Because
the node already exists, the sample attaches behaviour to it in **lifecycle mode**
(`StateMachineBuilder.For(machine, context)`) rather than defining states — see
[State machines](../../../docs/StateMachines.md).

The simulation raises a transition callback; the address space follows it. Both
`CurrentState` and its `Id` property are written, because a client that receives a
state *name* it cannot resolve to a state *node* is no better off than one that
received nothing.

The state and transition number tables are deliberately kept apart from the node
manager, in `GeneratorStateMap`, and a test holds them against the simulation's
own legality function in **both** directions: a transition the physics permits but
the map lacks moves a machine without telling a client; one the map holds but the
physics refuses is dead weight that looks supported.

### `GeneratorProtectionAlarmType`

One instance **per protection function** rather than a single instance whose
`ProtectionFunction` changes. A set can trip on low oil pressure and overspeed in
the same moment, and collapsing them loses the second; it is also how a real
control panel annunciates.

Because `OffNormalAlarmType` takes *healthy* as the normal state, each instance
carries `InputNode` pointing at the variable it supervises. Without it a client can
see that something tripped but not what was being watched.

**Optional members must be opted into.** `ProtectionFunction` is mandatory on the
type and is materialised by the generated factory; `IsShutdown` and `SubsystemName`
are optional and are not. Writing to an optional member with `CreateOrReplace`
alone produces a child that exists, appears in `GetChildren` and holds the value —
but carries no `ReferenceTypeId`, so no browse can reach it and the property is
absent from a client's view. Call `AddXxx(context)` before writing. The failure is
silent in both directions: the code looks right, and the server answers reads on
everything it does publish, so only a client comparing against the type definition
notices.

**A simulation that cannot leave its healthy band cannot exercise its
protections.** The datasheet curves are bounded inside every trip point by
construction, so a faithful simulation of a healthy machine leaves the entire
protection path — alarms, shutdown class, `ResetFaults`, the `Fault` branch of the
state machine — as dead code, while the documentation describes it as working. The
sample therefore injects a deviation into the *measurement* rather than the curve,
which is what a real fault is, and rotates it so a long run exercises every
protection. The lesson generalises: when the model says a thing cannot happen, the
code that handles it needs a deliberate way to be reached, or it is untested.

**Trip after evaluating, not during.** Stopping the set inside the evaluation loop
makes its remaining conditions read healthy, so simultaneous trips collapse to
whichever protection came first in the table — defeating the reason for having one
alarm instance per function.

**A shutdown protection must latch.** The trip removes the condition that caused
it, because the supervised quantities are only meaningful while the machine runs.
An alarm that tracks its input therefore annunciates for one tick and clears,
which tells an operator that something stopped the set but not what. Latch until
the set leaves the shutdown state. This one is easy to miss in review and in unit
tests — the condition logic is correct in isolation; the defect only appears when
the trip and the supervision interact over time.

**Clearing is an event, not just a state change.** A client learns of condition
state changes only through events. Clearing an alarm node without reporting leaves
an alarm-list client displaying it as active and retained until a
`ConditionRefresh`.

Trip conditions read the hysteresis the simulation already applies, so alarms latch
and clear cleanly rather than chattering on the threshold, and events are reported
only on change. A shutdown-class trip stops the machine; a warning does not —
reporting a trip without stopping the set would publish a generator that is on fire
and still loaded.

One trap worth naming: supervising low oil pressure during cranking trips every set
the moment it tries to start, because pressure has not built yet. Real sets bypass
that trip for exactly this reason.

### Methods

Legality is decided in exactly one place, so no caller can drive a machine from
`Off` straight to `Loaded` by picking the right method. A refused request answers
`BadInvalidState` rather than silently doing nothing — a method that appears to
succeed without acting is indistinguishable from a real success, which is worse
than an honest refusal.

The method semantics are expressed against the simulation rather than against
address-space nodes, which keeps each handler down to one line and lets the
behaviour be tested without standing up a server.

### Concurrency

The simulation tick runs on a thread-pool thread while client requests are served
on their own threads, and both drive state transitions and write the same
address-space nodes. They are serialised by a single gate held across the whole
tick and across every method handler.

This matters more than it looks. Without it, a tick moving a set to `Cooldown` and
a concurrent `EmergencyStop` both fire the state-change callback, and the paired
`CurrentState` / `CurrentState.Id` writes interleave — leaving a client with a
state *name* from one transition and a state *node* from the other. That is the
precise failure the paired write exists to prevent, reintroduced by the threading
model rather than by the write itself. One gate for the whole plant rather than one
per set: a tick is microseconds of arithmetic, so the contention is irrelevant, and
a single gate cannot be acquired in the wrong order.

## Cross-server composition

`SiteCompositionServer` demonstrates composing several servers at a supervisory
level. It owns no devices; it publishes a site stage and one **cross-server
component binding** per subordinate, carrying that server's `ComponentServerUri`
and `ComponentEndpointUrl`:

```
Opc.Ua.OpenUsd.Connector --server <site> --federate --view
```

With `--federate` the connector opens a session to each named server, discovers
its representations and drives its bindings into the same stage — one scene, live
machines, three servers.

Nothing is mirrored. The site server never proxies a subordinate's address space,
so there is no cache to invalidate and no second copy of the truth.

`--federate` is opt-in because the endpoint the connector dials comes from the
server being rendered rather than from the operator, which makes honouring it a
trust decision. Federation is also best-effort per component: a subordinate that
is down is logged and skipped, and the rest of the scene still renders.

## See also

- [`GeneratorServer`](README.md) and its [datasheet](DATASHEET.md)
- [`SiteCompositionServer`](../SiteCompositionServer)
- [OpenUSD](../../../docs/OpenUsd.md)
- [Device Integration](../../../docs/DeviceIntegration.md)
- [State machines](../../../docs/StateMachines.md)
