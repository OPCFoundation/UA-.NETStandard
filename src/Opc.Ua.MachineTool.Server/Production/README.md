# Retained MachineTool production

This module implements the legacy production observation model retained by
OPC 40501-1, version 1.02, sections 8.4, 9.2 and 9.4–9.6. Deprecation of the
information-model declarations does not remove their required behavior.
The module does not start jobs, operate equipment, or replace Machinery/ISA95
job execution. A modern job-management adapter can use the same
`IMachineToolProvider` observations.

## Public builder and runtime

Select optional production facets explicitly:

```csharp
var options = new MachineToolProductionOptions
{
    Features = MachineToolFeatures.Production |
        MachineToolFeatures.DynamicProductionPlan |
        MachineToolFeatures.ProductionParts |
        MachineToolFeatures.ProductionStatistics |
        MachineToolFeatures.ProductionInterruptions,
    MaximumJobs = 32,
    MaximumProgramsPerJob = 64,
    MaximumPartSetsPerJob = 16,
    MaximumPartsPerSet = 256,
    MaximumInterruptionsPerJob = 16,
    MaximumElements = 2048
};

await using MachineToolRuntime<MachineToolState> runtime = await context
    .AddMachineTool("Mill")
    .WithProvider(equipmentProvider)
    .WithProduction(options)
    .WithIdentification(identity =>
    {
        identity.Manufacturer = new LocalizedText("Machine builder");
        identity.SerialNumber = "Mill-1";
        identity.ProductInstanceUri = "urn:example:mill:1";
    })
    .BuildAsync(cancellationToken);

await runtime.RefreshAsync(cancellationToken);
```

`WithProduction` adds its feature flags to the existing selection. A subsequent
`WithFeatures` replaces that selection. No flags means mandatory ActiveProgram
monitoring only. Static `Production` requires at least one job; a dynamic plan
can be empty. Parts and interruptions require `Production`. Statistics can be
selected independently. Missing required observations or exceeded bounds fail;
inventories are never truncated.

Providers remain application-owned. The runtime obtains snapshots outside its
commit gate. Monitoring validation and production preparation finish before
either module commits. Publication and factory/disposal callbacks are not
awaited under the runtime's commit gate. Reentrant mutation or disposal from an
observation callback fails rather than waiting for itself.

If publication fails after a coherent commit, the accepted snapshot and its
event identities remain pending. Call `RetryPublicationAsync` before refreshing
to a newer provider revision. This retries publication, not equipment execution.

## Observations, identities and counters

`MachineToolSnapshot.Production` contains the complete current ordered job
inventory and optional statistics. `ActiveProgram` must identify and match a
modeled program whenever a populated plan and selected program are supplied.
Its NumberInList and state history mirror that program; changing selection is
not fabricated as a production transition.

Job and program `Key` values are application slot identities, not additional
wire properties. Empty keys use the job Identifier or program Name. Use stable
keys for static slots that are reused for different jobs/programs. Job
Identifiers and supplied physical part Identifiers must be unique. Every job
has at least one program. Job/program ordering is published using
HasOrderedComponent and sequential UInt16 NumberInList values.

Part sets and individual parts use their standard HasComponent relationships.
They do not acquire invented NumberInList properties. An aggregate-only part set
omits PartsPerRun. Removing the last set or individual part also removes the
corresponding optional mandatory-placeholder container.

Each state machine has a contiguous TransitionSequence. A transition, including
RunningToRunning, advances it by exactly one. Missing transitions are rejected,
not inferred. An initial snapshot can join an already-running machine, but does
not invent prior transition history. A different snapshot requires a newer
provider Revision; re-reading the same immutable snapshot is idempotent.

RunsCompleted advances once for RunningToRunning or RunningToEnded. Ended jobs
have a known RunsPlanned equal to RunsCompleted. Aborted or ended jobs cannot
contain a running program or part. Static job replacement uses the specified
EndedToInitializing or AbortedToInitializing transition.

PartsCompleted counts terminal parts regardless of quality. Good,
CapabilityUnavailable and WillNotBeMeasured are the accepted quality categories;
Bad and NotYetMeasured are not. Part quality may be completed after machining
while the containing job is still open. Final job observations cannot be revised.

Section 8.4.3 defines cumulative job counters, whereas section 8.4.6 resets part
nodes for each run. `PartsCompletedBeforeCurrentRun` and
`PartsGoodBeforeCurrentRun` reconcile these two scopes without adding wire
properties. On a consecutive run, they carry forward the previous run's final
cumulative totals. Current-run individual parts and set counters must account
for the remaining totals. Aggregate-only sets retain provider-supplied good
counts, bounded by completed counts. Later machines must not retrospectively
revise historical quality totals.

The deprecated Statistics counter is UInt32. If the newer UInt64 operation
counter is also supplied, both values must agree; overflow is not truncated.
All observation writes preserve provider SourceTimestamp and StatusCode.

## Transitions, conditions and dynamic removal

CurrentState, LastTransition, their canonical IDs/numbers, and transition times
come from the loaded model and observed transitions. Generated job, program and
part transition events carry the post-transition fields. The abstract standard
event and interruption types receive concrete application-namespace subtypes;
no standard NodeIds are invented.

Interruption observations name registered ConditionClass subtypes, including
the MachineTool classes and standard SafetyConditionClassType. All active
reasons must clear before a job resumes. Standard condition enable/disable,
retention, comments and refresh behavior are reused. Cleared conditions publish
Retain=false before removal. Multiple simultaneous reasons remain independent.

Event browsing uses HasNotifier/HasEventSource references. Delivery follows the
generated nodes' Parent chain exactly once, reaching nested notifiers, the root
machine and its server notifier.

Dynamic removal requires a previously published final observation. The builder
wires `AsyncCustomNodeManager.WaitForValuePublicationAsync` by default. It waits
for final values to be included in every active data subscription's actual
Publish response before deleting any subtree. This is a server publication
boundary, not transport acknowledgement. Its default deadline is 30 seconds.
Unsupported queues, a timeout, cancellation or a failed barrier leave the
observation and its nodes unchanged.

For custom namespace owners, configure
`PublishFinalObservationsAsync(ArrayOf<NodeState>, CancellationToken)` explicitly.
The direct controller refuses a dynamic plan without that capability. No timer
or successful no-op replaces actual publication. Pure reordering and
insertion/removal update NodeVersion and emit GeneralModelChangeEvent through
the shared model-change infrastructure.

Runtime disposal cancels uncommitted work, drains accepted publication and
attempts every owned resource's disposal. It does not fabricate terminal
equipment observations during shutdown.

## GMS and other generated subtypes

The same builder accepts a generated root factory. Production options accept
typed asynchronous job and part factories:

```csharp
var options = new MachineToolProductionOptions
{
    Features = MachineToolFeatures.Production | MachineToolFeatures.ProductionParts,
    CreateJobAsync = (system, parent, name, ct) =>
        new ValueTask<MachineToolProductionNode<ProductionJobState>>(
            new MachineToolProductionNode<ProductionJobState>(
                system.CreateInstanceOfGMSJobType(parent, name))),
    CreatePartAsync = (system, parent, name, ct) =>
        new ValueTask<MachineToolProductionNode<ProductionPartState>>(
            new MachineToolProductionNode<ProductionPartState>(
                system.CreateInstanceOfGMSPartType(parent, name)))
};
```

Factories return unique unpublished generated nodes with the supplied parent
and browse name. Actual derived types and extra GMS members are retained.
An optional IAsyncDisposable in `MachineToolProductionNode<TState>` transfers
to the module and is released on rollback, removal or runtime disposal. A
factory that throws before returning remains responsible for its own resources.
Provider ownership is not transferred.

For direct composition, construct `MachineToolProductionController` with the
existing `IMachineryBuildContext`, generated MachineToolState, production
options and TimeProvider. Initialize before registering the machine, then use
ApplyAsync and DisposeAsync. The integrated runtime additionally coordinates
monitoring with production through the module's internal
prepare/commit/publish transaction. Do not update its production controller
independently when full machine-snapshot coherence is required.
