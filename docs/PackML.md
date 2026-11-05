# PackML

The PackML packages expose a standalone OPC 30050 unit with its nested state
machines, status, administration and methods. An equipment provider authorizes
and accepts requests; the runtime changes an acting state to its destination
only when the equipment reports completion. A successful Method call therefore
does not establish that the machine has reached the requested state.

The model is the unmodified OPC Foundation PackML 1.01 NodeSet. This guide
describes the .NET runtime around that model, not an alternative PackML protocol.
The [published specification](https://reference.opcfoundation.org/specs/OPC-30050/)
defines the wire types and conformance units.
The SDK provides full support for the defined PackML 1.01 capabilities through
the runtime, typed clients, fluent hosting and sample, including the defined
optional server and client capabilities. Individual units expose their configured
capabilities. This describes available
implementation functionality, not certification or the result of a particular
test run.

## Packages and ownership

| Package suffix | Responsibility |
|---|---|
| `Opc.Ua.PackML` | Generated model, codecs, node factories, client proxies and `PackMLStateMachineController` |
| `Opc.Ua.PackML.Server` | Provider contract, standalone hosting, composable runtime and administration |
| `Opc.Ua.PackML.Client` | Qualified discovery, typed reads, subscriptions and commands over a borrowed session |

Use the `OPCFoundation.NetStandard.` prefix for NuGet package names.
The server and client reference the same model assembly; neither generates a
second copy of its types.

The hosting application owns equipment providers and persistence stores.
`PackMLRuntime` owns its units and drains their pending operations before the
address space is deleted. A typed client borrows its session and does not
close or dispose it. In particular, a session obtained from the fluent client's
`Func<CancellationToken, Task<ManagedSession>>` remains container-owned.

## Host a unit

Register an `IPackMLEquipmentProvider` and add PackML to the existing fluent
server builder:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Opc.Ua;
using Opc.Ua.PackML.Server;

services.AddSingleton<FillerEquipment>();
services.AddOpcUa()
    .AddServer(options =>
    {
        options.ApplicationName = "Filler";
        options.ApplicationUri = "urn:factory:filler";
        options.ProductUri = "urn:factory:packml";
        options.EndpointUrls.Add("opc.tcp://localhost:62563/PackML");
    })
    .AddPackML((provider, options) =>
    {
        options.AddUnit(
            "Filler",
            provider.GetRequiredService<FillerEquipment>(),
            unit =>
            {
                unit.TagId = "Line 1 filler";
                unit.Modes =
                [
                    new PackMLMode(),
                    new PackMLMode { Value = 2, Name = "Maintenance" },
                    new PackMLMode { Value = 3, Name = "Manual" }
                ];
                unit.MaterialCount = 2;
                unit.EnableRemoteCommand = true;
            });
    });
```

`FillerEquipment` is application code implementing the provider contract.
Configure certificates, trusted peers and user authentication through the
normal [server hosting](NodeManagers.md) infrastructure; the sample's local
certificate acceptance policy is not a production trust policy.

For composition inside another asynchronous node manager, load
`new NodeStateCollection().AddOpcUaPackML(context)` once and construct
`new PackMLRuntime(manager)`. Call `AddUnitAsync` with an application-qualified
name and the chosen parent; a null parent places the unit directly under
Objects. Keep the runtime until the owner shuts down, then await
`DisposeAsync` before deleting its nodes. This path does not require Machinery,
DI, Scales or a PackML-specific global folder.

The direct `PackMLUnit.CreateAsync` path creates an unregistered unit and its
`ModeDataType`. Its owner must register both nodes, attach the unit to its
parent and arrange event propagation through its node manager.

## Equipment acceptance and completion

`AuthorizeAsync` receives the caller's `ISystemContext`, operation and current
mode/state before the provider is allowed to execute the request. Return a
bad `ServiceResult`, such as `BadUserAccessDenied`, to reject it without
publishing new setpoints. `ExecuteAsync` returns acceptance or rejection.
Both decisions must return a Good or Bad status; an Uncertain result is
reported as a provider contract error rather than successful acceptance.
Equipment safety checks, process interlocks and authorization policy remain
the provider's responsibility.

For a state command, a Good acceptance enters its acting state and leaves
`StateChangeInProcess` true. Record the runtime-assigned `RequestId` with the
equipment operation. When that operation actually finishes, call:

```csharp
ServiceResult result = await unit.CompleteActingStateAsync(
    acceptedRequestId,
    PackMLStateNumbers.Starting,
    cancellationToken);
```

The identity and expected acting state must both match. A stale completion,
duplicate completion or completion for a superseded request returns
`BadInvalidState` without advancing the machine. Abort can supersede an active
command; its completion uses the new identity.

The runtime sets `Executable` to false on every state command that the current
state does not permit. A client calling such a method receives
`BadNotExecutable` from the Call service (OPC 10000-4 §5.11.2), and the provider
never sees the request. An in-process `RequestAsync` for the same command
returns `BadInvalidState`.

Operations on one unit are serialized, including provider calls, snapshots,
completion and checkpoints. No synchronization primitive is held while
application callbacks execute. Operations already admitted execute in admission
order unless canceled before starting. While equipment or persistence work is outstanding, newly arriving
mutations on that unit are rejected instead of waiting behind the callback:
`RequestAsync` returns `BadInvalidState`, and refresh, completion, administration
publication and checkpoint APIs throw `InvalidOperationException`. All mutation
APIs throw `InvalidOperationException` for reentry through the current callback's
execution context. Suppressed execution contexts follow the same busy-admission
policy as independent callers.
Reads of published values and operations on independent units remain available.
Callbacks must not await a previously captured, queued mutation of the same unit:
that admitted operation still follows the callback in execution order.
This is the runtime's concurrency policy, not an OPC 30050 requirement;
[OPC 30050 v1.01 section 6.7.9](https://reference.opcfoundation.org/specs/OPC-30050/v1.01/6.7.9)
describes `BadInvalidState` for an object in an invalid state, rather than a
general prohibition on concurrent calls.

Direct `DisposeAsync` throws `InvalidOperationException` while an application
callback is active; it does not report successful disposal or close admission.
The host-owned runtime has a separate internal shutdown path that cancels active
and admitted work and waits for it to finish before address-space deletion.
Runtime disposal from its own composition callback or any committed unit's
equipment, persistence or notification callback is rejected before closing
runtime admission or starting cleanup on any unit.
Equipment providers do not receive that shutdown authority. A direct unit owner
must cancel its operation through the supplied cancellation token and await it
before disposing the idle unit. Providers must observe cancellation. Idle direct
disposal still drains admitted work, and all disposal paths report their failures.
When both shutdown and owner cleanup fail, an `AggregateException` retains both
errors. Provider exceptions and store errors are propagated to the caller.

A cancellation, timeout or transport failure after a request has been sent can
leave the equipment outcome unknown. The client does not retry such a command.
Read or observe the equipment state and reconcile it with the equipment owner
before sending another request. A persistence failure after acceptance does
not undo the physical operation.

## Modes and state subsets

Each unit defines a concrete Enumeration DataType from `Modes`; its configured
integer values are also administration-array indexes. Values must be unique
and between 1 and 255. `Status.UnitSupportedModes` references that DataType,
and `UnitModeCurrent.DataType` uses it. No global PackML enumeration is
modified.

An empty `PackMLMode.States` selects the complete graph. A non-empty subset
must include its initial state and required parent states; an included acting
state needs its completion destination. `AvailableStates`,
`AvailableTransitions` and Method `Executable` attributes follow the selected
subset. Excluding a state does not create a shortcut transition through it.

Mode changes are allowed from Stopped and Aborted by default.
`ModeChangeStates` can select other stable states when the equipment permits
it. `SetUnitMode(Int32)` sets the Boolean `UnitModeRequested` and
`UnitModeChangeInProcess` flags after authorization. The current mode remains
unchanged until a Good equipment snapshot confirms the accepted mode.
An unsolicited different mode in a Good snapshot is rejected as a provider
contract error. A confirmed change initializes the new mode at its configured
stable initial state; it does not restore an earlier production state.

The client distinguishes `Reset` on MachineState from `ResetExecute` on
ExecuteState. The wire methods are associated with their owning machine and
cannot be substituted merely because the other Reset is executable.

## Status and configuration operations

`ReadAsync` supplies actual speed, equipment-blocked/starved flags, readiness,
current mode, quality and acquisition time. The runtime validates the entire
snapshot before publishing any field. Applications call `RefreshAsync` at
their chosen acquisition cadence; hosting does not start a polling thread.
State timers are refreshed with these calls, explicit checkpoints and state
changes.

`SetMachSpeed` changes the accepted `MachSpeed` setpoint, not `CurMachSpeed`.
Both speed values use primary packages per minute and expose engineering
units and a configured range. NaN, infinity, negative values and values above
`MaximumSpeed` are rejected before reaching the equipment provider.

`SetProduct`, `SetParameter` and Start descriptor payloads use the generated
PackML structures. The provider defines when these configurations can be
accepted. The runtime publishes accepted product/parameter values only after
the provider succeeds; callers do not write Status values directly.

Remote commands are exposed only when `EnableRemoteCommand` is true.
`MaterialCount > 0` exposes `SetInterlock` and readiness status. Their Boolean
meanings differ: `SetInterlock(id, true)` requests an interlock, whereas a
true `MaterialInterlock[id]` means that material is ready. The provider maps
the request to actual equipment behavior. The summary `MaterialInterlocked`
is true if any readiness element is false.

`RemoteParameter` comes from `PackMLEquipmentSnapshot.RemoteParameters`.
Accepting a remote command does not overwrite that equipment-reported value;
the provider publishes its resulting data through the next snapshot.

Unpublished equipment-derived values have `BadWaitingForInitialData`, rather
than a Good zero interpreted as a measurement. Typed client values retain
quality and timestamps even when the value is bad or uncertain.

## Administration and persistence

`PublishAdministrationAsync` projects an equipment-owned sample containing
parameters, alarms, alarm history, warnings, the scalar first-out stop reason,
design speed and production counts. `AdministrationExtents` declares finite
capacities; oversized samples are rejected instead of truncated.
The custom PackML alarm/history references remain those of the published
model. These arrays do not implement UA Historical Access or create a
second Alarms and Conditions engine. Adapt data from the application's
existing alarm and history owners.

Timers use the injected `TimeProvider`'s monotonic clock. Mode timers have one
dimension; state timers have two, indexed by concrete mode value and standard
state number. Whole-second counters retain fractional elapsed time between
refreshes and roll over at `Int32.MaxValue`. Current intervals restart when
their mode or state changes; cumulative intervals remain until an
administrative reset.

A state-machine Reset does not reset production counters or timers.
The local `ResetCounters` operation passes through provider authorization and
acceptance first. It resets timer values and each production `Count` but
preserves `AccCount`. No additional remote ResetCounters method is invented.
Admin reads default to the SecurityAdmin role; use
`AdministrationRolePermissions` to grant the application's chosen roles access.
The option requires at least one explicit role entry. Empty and null arrays
are rejected rather than inheriting the namespace's default permissions; to
allow anonymous reads, explicitly grant them to the Anonymous role.

An optional `IPackMLAdministrationStore` loads timer snapshots before the fresh
initial state is entered and saves after accepted changes or an explicit
`CheckpointAsync`. Call `CheckpointAsync` at the application's durability
cadence and before orderly shutdown. The store must replace a snapshot
atomically and report failures. No commands, active states or downtime
intervals are replayed on restart. The equipment provider owns persistence
of production counts and alarm data.

### Publication order

A subscriber sees status, administration and state-machine values in the
order the unit writes them. After each write, the unit's asynchronous
operations await that node's change notification, including asynchronous
monitored-item sinks, before they write the next node.

The cancellation token is checked when an operation is admitted, and it is
passed to provider and store calls. Value writes take no token. Once an
operation has started changing values, the unit publishes each change with
`CancellationToken.None`, because an abandoned notification would leave
subscribers sampling a stale value.

If the notification of a request indicator fails before the provider runs, the
unit restores the indicators' prior values, status codes and timestamps,
publishes them and does not call the provider. The unit also restores them when
the provider rejects the request with a Bad or Uncertain result. If publishing
the restored indicators also fails, an `AggregateException` carries the
original failure first and then every restoration failure. For a provider
rejection, that first entry is a `ServiceResultException` with the provider's
result; an Uncertain result is wrapped in the `InvalidOperationException` that
reports the invalid outcome. A rejected command changes nothing and
publishes no change masks, as on the synchronous path. When the
state-machine notification fails and the same operation also drains the
`StateChanged` queue and a handler fails, an `AggregateException` carries both
failures. If a concurrent command drains that queue first, it receives the
handler failure, and the asynchronous operation reports only its notification
failure.

`PackMLRuntime.Units` lists a unit only after the parent's change
notification succeeds. Composition operations run one at a time, so a later
composition still detects a duplicate browse name while that notification is
pending, and no `Units` snapshot contains a unit that a failed notification
then rolls back. This is a boundary of the `Units` list only. The unit's node
is added to its parent and to the node manager before the parent notification
is awaited, so while that notification is pending a client can browse, read
and call methods on the node, and a method call can reach the provider. If the
notification then fails, the runtime cancels and drains admitted provider work
before unlinking and deleting the unit's nodes. A command that the provider
accepted in that interval is not reversed. The creation error is preserved;
shutdown and node-deletion failures are reported alongside it.

Each owner keeps its serialization gate for the whole operation, including
the awaited notification callbacks. The runtime holds its composition gate
while it awaits the parent's `OnStateChangedAsync` sink, and a unit holds its
operation gate while it awaits its own node sinks and provider calls. A
callback must not await an operation that needs the gate of the owner that is
awaiting it: a parent notification sink must not await `AddUnitAsync` or
`DisposeAsync` on the same runtime, and a unit's node sink or provider
callback must not await a mutation of that unit. Reentry from the callback's
own execution context throws `InvalidOperationException`. An operation started
from another context is either rejected by the busy-admission rule above, while
provider or persistence work is outstanding, or queues behind the callback,
which then never completes.
Method calls on other units, and on the unit being added, are not part of the
composition gate and run while the parent notification is pending.

A controller raises `StateChanged` events from one queue, and whichever caller
drains the queue raises them in state order. Synchronous admission stays closed
until the asynchronous operation's state-machine notifications finish, then
reopens before the controller raises `StateChanged`. A concurrent command can
still drain the queue at that point, and a handler failure reaches that draining
caller. The controller does not assign handler failures to the operation that
originally queued the change.

`ExecuteAsync` and `CompleteActingStateAsync` await each transition's event and
change notification before applying nested entry rules or completing the next
acting state. A callback therefore reads the live state of that transition,
rather than the final state of an automatically completed chain. Asynchronous
controller operations are serialized through notification delivery; callbacks
must not await reentrant operations that require the same controller.

While an asynchronous operation owns notification delivery, synchronous
commands, completion calls, mode changes and supported-state changes return
`BadInvalidState` without mutating the state. `Initialize` and `ConfigureStates`,
which have no status return, throw `ServiceResultException` with that status.
Admission reopens after publication finishes, including when publication fails.
Read-only controller access remains available. This restriction applies when
the async path is active; ordinary synchronous calls retain their ordering.

A machine subclass passed to the controller must override `DoCauseAsync` or
`DoTransitionAsync` to customize asynchronous transitions. An override of only
the synchronous method still applies to synchronous controller and wire calls,
but is not invoked by the async counterpart. Cancellation is observed before
mutation; after mutation the transition finishes notification delivery without
rolling back the state. A transition failure stops the remaining completion
chain, as it does on the synchronous path. See
[Asynchronous causes and transitions](StateMachines.md#asynchronous-causes-and-transitions)
for the publication and failure rules.

## Typed client

```csharp
using Opc.Ua.PackML.Client;

var packml = new PackMLClient(session, telemetry);
ArrayOf<PackMLUnit> units = await packml.DiscoverAsync(
    cancellationToken: cancellationToken);
NodeId unit = units[0].NodeId;

await packml.SetMachSpeedAsync(unit, 120, cancellationToken);
PackMLStatusSnapshot status = await packml.ReadStatusAsync(unit, cancellationToken);
if (status.CurMachSpeed.IsGood)
{
    Console.WriteLine(status.CurMachSpeed.Value);
}

await packml.StartAsync(unit, [], cancellationToken);
PackMLValue<bool> complete = await packml.WaitForAsync(
    unit,
    PackMLFields.Status.StateChangeInProcess,
    value => !value,
    TimeSpan.FromSeconds(30),
    cancellationToken: cancellationToken);
```

The Start example assumes the unit is Idle and that this client controls its
request sequence. An observed flag alone does not correlate a transition
with a particular caller. For independent clients, inspect the nested current
states and the equipment's operation identity rather than inferring causation
from one Boolean.

Discovery retains qualified instance names and follows namespace URIs instead
of fixed indexes. `ReadStatusAsync` and `ReadAdminAsync` batch reads but do not
promise an atomic equipment snapshot. `IsPresent`, `HasValue` and `IsGood`
distinguish an omitted field, a decode failure and bad quality.
`ObserveAsync` uses the caller's shared streaming subscription, or a managed
session's default subscription, and removes only its own monitored item when
enumeration ends. `WaitForAsync` requires a finite timeout and does not send
or replay equipment commands.

The fluent client has `AddPackMLClient()` for DI; the direct constructor and
session extension remain available without hosting.

## Sample and scope

The [PackML sample](../samples/PackML/README.md) runs a server and a client
over an actual session. It configures Produce, Maintenance and a restricted
Manual mode, separates accepted commands from delayed equipment completion,
rejects a mode change while Execute, and projects speed, product, parameter,
interlock and administration data. It simulates equipment; it must not be
connected to physical control outputs.

```powershell
dotnet run --project samples\PackML\PackMLSample -f net10.0 `
    -p:CustomTestTarget=net10.0 -- --pki-root D:\temp\packml-sample-pki
```

Existing Scales and Lens compositions continue to use
`PackMLStateMachineController` with its synchronous defaults. Standalone
provider hosting does not change those defaults. Asynchronous hosts call
`ExecuteAsync`, `ConfigureStatesAsync` and `CompleteActingStateAsync`, which
await the state-machine notification; the synchronous methods still notify
synchronously. MCP tools and additional Lens UI are outside this package's
scope.

Model/runtime support does not certify an installed machine. The
[PackML Base Functionality Server Facet](https://reference.opcfoundation.org/specs/OPC-30050/7.2.2.1)
also includes inherited Core, Data Access, Method, UA-TCP/Binary, subscription
and time-synchronization requirements, including a monitored-item queue size
of at least five. Verify those capabilities and the selected optional
conformance units in the deployed server before declaring a profile URI.

## Capability boundaries

`IConformanceContributor` reports the PackML capabilities enabled by a unit's
configuration. `PackMLRuntime` contributes only capabilities shared by all
its units, and contributes none when it has no units. Neither surface adds a
server profile URI. A configured capability describes the SDK path available
to the equipment integration; it does not establish that the provider or
installed equipment meets the corresponding specification requirement.

| OPC 30050 capability | Configuration and implementation | Equipment or deployment responsibility |
|---|---|---|
| Base object, Status and Admin | Generated model, `PackMLRuntime`, typed status/admin reads | Supply equipment snapshots and grant administrative access |
| TagID and PackMLVersion | `TagId` and `IncludeVersion`; typed optional-field reads | Supply the equipment identifier; retain the published version property's meaning |
| State information and commands | Per-mode subsets, nested activation, generated methods and correlated completion | Select the actual supported graph and report completion for the accepted request identity |
| Unit mode | Concrete Enumeration, Boolean request/progress flags and snapshot confirmation | Authorize changes and confirm the equipment's current mode |
| Product, parameter and speed configuration | Typed requests and accepted setpoints; actual speed remains snapshot-owned | Enforce process constraints and publish measured speed |
| Remote command and remote parameters | `EnableRemoteCommand`; separate request and snapshot data | Interpret the remote interface and publish resulting equipment data |
| Material interlocks | `MaterialCount`; optional method, readiness array and summary | Map interlock requests to material readiness; this is not a safety function |
| Alarms, history, warnings and stop reason | `AdministrationExtents` and equipment administration samples | Adapt the application's existing alarm/history owners and preserve first-out meaning |
| Mode/state statistics and reset time | Monotonic timers, configured mode indexes, rank-two state arrays and authorized local reset | Select checkpoint durability and implement equipment counter-reset ownership |
| Production counts and design speed | Configured capacities, preserved `AccCount` and typed administration reads | Supply consumed, processed and defective counts and installed design speed |

State-command capabilities depend on whether at least one configured mode
contains their acting state. Optional alarm and count capabilities depend on
their configured capacities. The combined production-count capability requires
positive consumed, processed and defective capacities; an empty current sample
does not disable a configured capability.

The defined server and client conformance units in
[Tables 63 and 64 (§7.1)](https://reference.opcfoundation.org/specs/OPC-30050/v1.01/7.1)
map to the operations and fields above. The typed client reads optional TagID,
version, remote parameters and interlocks through `PackMLFields`; its status
and administration snapshots retain each field's presence, quality and timestamps.
`CommandAsync` routes state commands to their owning machine, and the typed
configuration, remote-command and interlock methods use the generated proxies.
An omitted optional operation returns `BadNotSupported`; a configured operation
that is unavailable in the current state remains subject to server authorization
and executability.

Tables 63 and 64 call the completion capability "PackML State Complete" and
refer to a Complete Method. The actual Method is `ToComplete` in
[Table 10 (§6.3.8)](https://reference.opcfoundation.org/specs/OPC-30050/v1.01/6.3.8),
the published NodeSet and
[§6.7.9](https://reference.opcfoundation.org/specs/OPC-30050/v1.01/6.7.9).
The runtime and client use that wire name: an accepted `ToComplete` enters
Completing, and matching equipment completion enters Complete. No separate
Complete Method is added.

`ConfiguredUnitAdvertisesEveryDefinedServerConformanceUnit` checks that one
fully configured unit advertises every distinct server title in Table 63 and
exercises the completion path. Disabled-capability and production-capacity cases
check conditional advertisement. The real-session cases
`EveryWireCommandReportsActingStateUntilMatchingEquipmentCompletion`,
`OptionalBasePropertiesRetainTheirPublishedTypesAndPresence`,
`EnabledWireOperationsPreservePayloadAndReadinessMeaning` and
`AuthorizedAdministrationSnapshotDecodesMatricesStructuresAndAcquisitionMetadata`
cover the corresponding typed-client paths. These are source-level coverage
references, not an assertion that the current revision has passed execution.

The [PackML test project](../tests/Opc.Ua.PackML.Tests/) contains direct runtime,
administration and real-session scenarios. Existing controller regression cases
remain in [the Scales tests](../tests/Opc.Ua.Scales.Tests/ScalesNodeManagerTests.cs).
Those scenarios do not replace testing the provider, inherited server facets
or an equipment installation. The optional StateModel extensions conformance
unit appears in [Table 66 (§7.2.2.1)](https://reference.opcfoundation.org/specs/OPC-30050/v1.01/7.2.2.1),
but [Table 63 (§7.1.2)](https://reference.opcfoundation.org/specs/OPC-30050/v1.01/7.1)
has no entry for it. [Section 6.3.5](https://reference.opcfoundation.org/specs/OPC-30050/v1.01/6.3.5)
identifies the dashed Hold transitions in Figure 15 as optional state-model
extensions. The runtime exposes and executes those five Hold transitions when a
configured mode includes their source and destination states. The specification
does not say whether the conformance unit requires one, some or all five, or
define other criteria; the runtime therefore does not advertise it. OPC 30050
does not lose base-facet support solely because this optional conformance unit
is undefined.

Table 66 directly requires `Monitor MinQueueSize_05`, independently of the
inherited Standard DataChange Subscription Server Facet, whose
[Part 7 Table 38](https://reference.opcfoundation.org/specs/OPC-10000-7/full)
requires `Monitor MinQueueSize_02`. The SDK supports configurable subscription
queues; verify the revised size and retained values in the application's
server configuration.

[Part 7 v1.04 Table 9](https://reference.opcfoundation.org/specs/OPC-10000-7/v1.04/5.2#tbl_9_monitored-item-services)
also expects this minimum queue size for at least one third of supported
MonitoredItems. Testing one queue does not establish that capacity.
`PackMLMonitoredItemSupportsQueueSizeOfAtLeastFive` in
[`PackMLClientServerTests.cs`](../tests/Opc.Ua.PackML.Tests/PackMLClientServerTests.cs)
contains two cases. The single-item case checks the returned `RevisedQueueSize`
and delivery of five distinct values in order. The capacity case configures and
reads an advertised limit of 102 MonitoredItems, creates all 102, verifies that
the 103rd is rejected, and checks five retained values on each of 34 queues.
This tests the required one-third capacity for that finite configuration.

Run the two cases with:

```powershell
dotnet test tests\Opc.Ua.PackML.Tests\Opc.Ua.PackML.Tests.csproj `
    -f net10.0 -p:CustomTestTarget=net10.0 -m:1 `
    --filter "FullyQualifiedName~PackMLMonitoredItemSupportsQueueSizeOfAtLeastFive"
```

Keep the passing result with the tested SDK revision and server configuration.
The test source alone is not a passing result, and the 102-item configuration
does not establish the capacity of a larger deployment.

For application certification, configure the inherited
[Security Time Synchronization facet](https://reference.opcfoundation.org/specs/OPC-10000-7/v1.04/6.6.151):
support `Security Time Synch - Configuration` and at least one of
`Security Time Synch - NTP / OS Based support` or
`Security Time Synch - UA based support`. The application and operating
environment must maintain the selected mechanism. Verify clock synchronization
in deployment; monotonic state timers do not synchronize the system clock.
This application requirement does not reduce the SDK support rating in the
[published specification support matrix](Profiles.md#published-specification-support-matrix).
