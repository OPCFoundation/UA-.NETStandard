# Programs (OPC UA Part 10)

Programs expose long-running, controllable work through the four base states
Halted, Ready, Running and Suspended. They are not arbitrary code-execution
endpoints. An application registers an execution provider and its information
model; clients invoke the published methods and monitor transitions and results.

The SDK builds on [Part 16 state machines](StateMachines.md), generated node
states and client proxies, [node management](NodeManagement.md), and
[streaming subscriptions](Subscriptions.md). No separate Programs package is
required: shared contracts/model are in Core, execution/hosting in Server, and
discovery/observation in Client.

## Contents

- [Programs (OPC UA Part 10)](#programs-opc-ua-part-10)
  - [Register a Program](#register-a-program)
  - [Execution and lifetime](#execution-and-lifetime)
  - [Typed client use](#typed-client-use)
  - [Results, diagnostics and security](#results-diagnostics-and-security)
  - [Specification compatibility](#specification-compatibility)

## Register a Program

Select the lifetime policy and supported controls explicitly:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Opc.Ua;
using Opc.Ua.Programs;
using Opc.Ua.Server.Programs;
using ProgramTypes = Opc.Ua.Programs.ObjectTypeIds;

IProgramProvider provider = new DelegateProgramProvider(CreateExecutionAsync);
var definition = new ProgramDefinition(
    ProgramTypes.ManagedProgramType,
    static parent => new ManagedProgramState(parent),
    provider,
    [ProgramControlMethod.Start, ProgramControlMethod.Suspend,
        ProgramControlMethod.Resume, ProgramControlMethod.Halt,
        ProgramControlMethod.Reset],
    ProgramState.Ready,
    maxInstanceCount: 4,
    maxRecycleCount: 10)
{
    Creatable = true,
    Deletable = true,
    AutoDelete = false
};

services.AddOpcUa()
    .AddServer(options => { /* endpoint and authentication configuration */ })
    .AddPrograms([definition]);
```

`CreateExecutionAsync` is application code returning a fresh `IProgramExecution`
for an invocation. Its `ControlAsync` accepts or rejects a control and returns
the method's outputs; it must not wait for the entire workload. Its `ObserveAsync`
stream reports internal progress/completion, tagged with the `RunId` received
when Start was accepted. `DisposeAsync` releases its resources.
Control handlers must not await consumption of their own update stream:
return acceptance first, then report subsequent internal changes. Unexpected
observation failures halt the Program; an accepted Reset restarts observation.

Registration alone does not create or start work. `ProgramNodeManagerFactory`
provides the non-DI path. `ProgramNodeManager.CreateInvocationAsync` creates
server-owned invocations; `AdoptInvocationAsync` accepts an exclusively owned,
unindexed generated node. Both use the same capacity accounting as client
creation. `ProgramRuntime.AttachAsync` is the direct per-invocation path; its
caller owns indexing newly materialized children and disposing the runtime.
For address-space attachment, pass `startObserving: false`, index the children,
then call `ActivateAsync`. The supplied node manager performs this sequence
automatically; failed registration cannot start an observation loop.
`ActivateAsync` waits until the provider's first enumeration request has begun
before enabling controls. A hot-source provider must install its observer before
that request yields; providers with asynchronous setup must buffer updates until
setup completes. Reset uses the same readiness ordering when restarting a failed
observation stream.

For a custom Program, pass its generated node factory and type identifier.
The optional model loader on `AddPrograms` supplies its generated node
collection. Different workloads should register distinct concrete Program
types, not overwrite the policy on a shared namespace-zero type.
An `IProgramCreationAdapter` adapts a declared application-specific Create
signature to the same checked lifecycle. `ParseAsync` returns the parent,
BrowseName and provider arguments; `CompleteAsync` constructs the declared
outputs. Arguments reach the provider through `ProgramInvocationContext.CreationArguments`.
Invalid outputs roll back the new invocation rather than leaking capacity.

## Execution and lifetime

`ProgramDiagnostic2` and its diagnostic value descendants are readable only by
the creating session or a SecurityAdmin, over a confidential channel.
Monitoring applies the same authorization as direct reads. Diagnostic
value-read authorizers are registered with the owning node manager and released
with the Program runtime; unrelated subscribers cannot retain access through a
previously authorized subscription.

| Control | Allowed transition |
| --- | --- |
| Start | Ready to Running |
| Suspend | Running to Suspended |
| Resume | Suspended to Running |
| Halt | Ready, Running or Suspended to Halted |
| Reset | Halted to Ready |

Internal updates follow the same nine-transition graph. Initial resource
availability can move Halted to Ready using RunId zero. An autonomous
Ready-to-Running transition uses the next RunId and consumes the same restart
allowance as Start; subsequent updates use that generation. Submachine updates identify a
descendant state machine and its transition. They do not count as a new run.
Unsupported controls need not be materialized; the remaining controls expose
state-sensitive Executable/UserExecutable attributes.

The runtime validates and prepares transitions before awaiting control
acceptance, then commits only if the captured state revision is still current.
Guards run once, before provider side effects. A stale report from an earlier run
cannot terminate a subsequent run. Do not mutate a runtime-owned node through
the legacy FSM methods or another independently attached behavior.
For a model-declared nested machine, `StateMachineBuilder.BindSubStateMachine`
reuses the Part 16 activation machinery. Preserve state on reentry for
Suspend/Resume; a new Program run resets its nested machines to their initial
states. Inactive machines report BadStateNotActive and reject transitions.

Request cancellation and workload lifetime are different. Cancellation or a
lost response after acceptance does **not** mean that the Program stopped.
Controls are not automatically replayed. Providers must handle external
side effects and ambiguous outcomes; local state management cannot roll back
physical equipment or guarantee exactly-once execution across a network.

RecycleCount starts at zero and counts successful restarts after the first
Start, not Reset or Resume. Rejected starts do not consume the allowance or
clear retained results. After the allowance is exhausted, a completed Program
remains Halted: Reset and subsequent Start are not executable, and an internal
completion must target Halted rather than Ready.
InstanceCount counts committed live invocations of the
concrete type. Capacity is reserved while asynchronous creation is in progress.

Client deletion requires Deletable, authorization and Halted. AutoDelete acts
after an execution terminates, not merely because an invocation was initialized
in Halted. Terminal results/events are captured before removal. Auto-deleted
results cannot subsequently be read.

The default state is in memory. Shutdown releases owned resources; there is no
durable restart recovery, failover execution, or distributed scheduling in this
feature. An injectable provider may manage external resources, but that does not
turn the in-memory invocation registry into a replicated store.

## Typed client use

```csharp
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Client.Programs;
using Opc.Ua.Programs;

var programs = new ProgramClient(session, telemetry);
NodeId invocationId = await programs.CreateAsync(
    programTypeId, programsFolderId, new QualifiedName("Job1", instanceNamespace), ct);
ProgramStateMachineTypeClient program = programs.GetProgram(invocationId);

await program.WaitForProgramTransitionAsync(
    streaming,
    notifierId: ObjectIds.Server,
    targetState: ProgramState.Running,
    onReady: token => program.StartAsync(token),
    ct: ct);

var capabilities = await programs.ReadCapabilitiesAsync(invocationId, ct);
var diagnostics = await programs.ReadDiagnosticsAsync(invocationId, ct);
var finalResults = await programs.ReadFinalResultsAsync(invocationId, ct);

await program.HaltAsync(ct);
await programs.DeleteAsync(invocationId, ct);
```

The Create convenience targets the SDK model's declared type-level signature:
ParentId and BrowseName in, InvocationId out. Part 3 does not define universal
Create arguments. Use an application's generated proxy for a vendor-specific
Create or argument-bearing Start; do not infer the returned NodeId from an
arbitrary output position.

Program client factories are available through the client DI builder.
Discovery is scoped to a supplied address-space root and recognizes Program
subtypes. Optional components are distinguished from bad service/value results.
Always inspect the status-bearing capability, diagnostic and final-result values.

Use `ObserveProgramTransitionsAsync` for intermediate results and
`ObserveProgramAuditsAsync` for transition audit events. Their generated filters
select the invocation's SourceNode even when subscribing to an owning Object
or Server notifier. The readiness callback subscribes before issuing a control.

Events carry captured results and state identifiers. Watching CurrentState.Id
and rereading Variables is not equivalent: it can miss fast transitions,
submachine self-transitions, and the intermediate values that caused them.
Multi-node reads are not transactional historical snapshots. Streaming is not
a durable event journal; account for overflow, disconnects and cancellation.

## Results, diagnostics and security

`ProgramTransitionEventType.IntermediateResult` is a BaseDataType Variable:
use a Variant, including a generated structure when appropriate. The default
SDK event is a concrete subtype. Applications can select concrete event
factories by namespace-aware transition NodeId. IntermediateResult is validated
against the selected event's declared DataType and ValueRank.

Final results address declared Variables under FinalResultData. The runtime
checks their type, captures their values, and retains them until an accepted
rerun or deletion; Reset does not discard them.

ProgramDiagnostic2 contains creation identity/time, last transition time,
last method/session/time, argument metadata and values, and return status.
Its structured value and children are updated even when nobody subscribes.
For rejected malformed calls, value arrays retain declaration positions,
with null values for missing positions; excess inputs are not invented as
declared arguments.

Diagnostic identity and argument reads require an encrypted channel and the
creating session or SecurityAdmin. Service authorization still applies.
Do not use arbitrary Program arguments as a secret store. Audit events follow
the server's auditing/access policy; a failed method is not a successful Program
transition. Internal transitions are not attributed to the client that
originally created the invocation.

## Specification compatibility

The linked [Part 10 document](https://reference.opcfoundation.org/specs/OPC-10000-10/full)
is v1.05.00; the namespace-zero model shipped by this repository is 1.05.07.
The SDK preserves the model's wire types and records these inconsistencies:

| Issue | SDK behavior |
| --- | --- |
| Maximum limits are UInt32, but prose describes negative unlimited values | `UInt32.MaxValue` means unlimited by documented SDK convention, not a normative assertion. |
| RecycleCount is Int32 while the prose calls it unsigned | Preserve Int32. After overflow the Variable reports BadOutOfRange; unlimited execution continues. |
| Reset HasCause links conflict with method definitions | Managed runtime uses Halted-to-Ready only. Legacy low-level Reset-from-Suspended remains for compatibility. |
| IntermediateResult prose describes an Object | Preserve the formal Variable/BaseDataType definition. |
| Program transition/audit types are abstract | Use concrete SDK or application subtypes without changing namespace zero. |
| Old runtime state numbers were 1-4 | Correct to Halted=11, Ready=12, Running=13, Suspended=14. Standard NodeIds are unchanged. |
| Generated subtypes qualified inherited IDs with their own namespace | Inherited base states/transitions resolve to their standard namespace-zero declarations. |

The SDK model namespace is `http://opcfoundation.org/UA/SDK/Programs/`.
It defines concrete events and a default Program type, not an Annex A production
download engine. Annex A is an illustrative interoperability fixture.
These implementation tests are not an OPC Foundation certification.
