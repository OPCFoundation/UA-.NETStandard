# Plastics and rubber common capabilities

OPC 40083 defines capabilities reused by plastics and rubber machinery. It does
not define a universal machine root. A concrete companion model, such as
[Extrusion](Extrusion.md) or [Granulate drying](GranulateDrying.md), composes
these capabilities into its own equipment interface.

## Packages and model provenance

`Opc.Ua.PlasticsRubber` owns the standard namespace
`http://opcfoundation.org/UA/PlasticsRubber/GeneralTypes/`. It supplies generated
states, structured encodings, typed clients and compiled model loading.
`Opc.Ua.PlasticsRubber.Server` adds fluent accessors and provider bindings.
`Opc.Ua.PlasticsRubber.Client` adds managed-session integration and common workflows.

The common model is an independently authored implementation of the 1.03
interface changes on the expressly MIT-licensed 1.02 implementation. It is
**not a redistribution of the official 1.03 XML**, and the presence of this
implementation does not establish a redistribution license for that artifact.
The reproducible authoring input, script and notices are under
`src/Opc.Ua.PlasticsRubber/Model`. Each package includes the companion-model
notice. The model version is distinct from the NuGet package version.

Generated declarations, field/encoding comparisons and tests do not constitute
formal OPC Foundation certification. The device guides identify additional
feature requirements and any upstream conformance-table discrepancies.

## One owner, explicit providers

Use `AddPlasticsRubber()` to start a standalone Machinery-backed owner, or
`AddPlasticsRubberToMachinery()` after an existing `AddMachinery()` registration.
Do not register another DI-owning manager just to add common capabilities.

The direct path is:

```csharp
var runtime = new PlasticsRubberRuntime(owner, accessPolicy);
StartDeviceBinding binding =
    await runtime.BindStartDeviceAsync(startDevice, startProvider);
```

Here `owner` is the existing DI/Machinery manager, `startDevice` is the actual
generated `StartDeviceState` selected by the equipment model, and
`startProvider` is its device authority. The runtime registers the binding with
the existing Machinery resource lifecycle. Direct binding constructors also
exist; their callers must dispose them or register them with that lifecycle.

`ConfigurePlasticsRubber()` and `ConfigurePlasticsRubberFor<TNodeManager>()`
participate in the existing Machinery setup pipeline. They require an
explicitly registered `IPlasticsRubberAccessPolicy`; no allow-all policy or
default hardware authority is installed.

Every provider receives the original `ISystemContext` and target instance
through `PlasticsRubberRequestContext`. Authorization occurs before provider
invocation. A failed provider operation is returned as a failure, not a
successful simulation of the requested action.

## Authority and observed state

Bindings acknowledge a request only after the provider's commit boundary.
They do not equate request acceptance with completion of physical movement,
starting, stopping or self-optimization. The device authority publishes the
actual state and absolute counters.

For a distributed or simulated system, keep production state, watchdogs and
control leases at the authoritative device or coordinator. In particular,
`IProductionControlProvider` must enforce the single controlling MES,
connection-loss behavior and watchdog deadline. A view must not run a second
production clock or turn its process-service identity into an operator identity.
Read-only observers are not subject to an invented one-session server limit.

Common bindings cover start/stop requests and their client interlock,
maintenance reset, parameter monitoring/settings, closed-loop control,
machine configuration/pages, machine sleep requests, MES messages,
production-control requests and active-job operations. Optional members are
selected on the generated instance before binding. A missing optional member
is not fabricated as an executable success-shaped placeholder.

`JobsBinding` and `CyclicJobInformationBinding` support the common list and
configuration exchanges. Receiving a job list does not start a job. Hardware
interlocks, job completion and committed cyclic counters remain with the
explicit provider; the binding does not substitute ISA-95 job semantics.

For common production datasets, see the
[chapter 20 server/client guide](PlasticsRubberDatasets.md). It documents
storage flags, active/preparation layers, metadata, freezing and temporary-file
ownership. These are not interchangeable with similarly named protocols from
other companion specifications.

## Material catalogues and diagnostics

`MaterialListBinding` publishes real generated `MaterialType` children from a
complete `MaterialListSnapshot`. The authority supplies stable slots
`001` through `999`, unique material identifiers, names, density and the
commit's source time. Density uses the list's declared `DensityUnit`.
`NodeVersion` follows the committed catalogue revision. Identical replay is
idempotent; a stale revision or conflicting replay is rejected.

Select the optional generated add/remove methods before binding when remote
mutation is required. `IMaterialListProvider` commits the mutation and returns
the full resulting catalogue. It must reject removal of an in-use material
where appropriate. No implicit privileged read is performed at construction:
refresh uses an explicitly supplied caller, or the host can publish an already
committed snapshot.

`DiagnosticsBinding` implements the common start/stop protocol and emits
generated step/end records from concrete application-owned derivatives of the
abstract standard event types. The actual device interface is the event source.
`IDiagnosticsProvider.RunAsync` returns an active state; later completion is
published as an absolute `DiagnosticsSnapshot`, including the required end
event. Stopping returns `OFF`. Vendor-specific diagnostic algorithms do not
belong in the common package.

The client facade includes `ObserveDiagnosticsStepsAsync`,
`ObserveDiagnosticsCompletionsAsync`, `ObserveLogbookAsync` and
`ObserveAlarmsAsync`. These consume an existing bounded streaming subscription
or a managed session's default stream and use generated record decoders.
Unknown application event subtypes are checked against the declared standard
base type; malformed or unrelated event payloads fail explicitly.

## Monitoring semantics

`MonitoredParameterLimits` uses signed relative offsets: lower tolerances are
nonpositive, upper tolerances are nonnegative, and the second band contains
the first. Absolute minimum/maximum alarms take precedence over relative
warnings. Exact boundaries are inside their corresponding limit.

Missing optional limits remain absent; they are not replaced with zero.
Relative bands need a setpoint. Nonfinite settings, reversed boundaries and
incorrectly signed offsets fail validation. Bad or uncertain measurements have
an unknown monitoring status instead of a fabricated in-tolerance result.

`MonitoredParameterBinding.Publish` preserves the supplied measurement quality
and source timestamp. Alarm suppression does not erase the observed monitoring
status. Device-specific calibration and alarm side effects belong to the
injected authority. A plain `MonitoredParameterType` setpoint is informational;
only `ControlledParameterType` and its subtypes use it to command hardware.

The underlying [OPC 40083 parameter definitions](https://reference.opcfoundation.org/specs/OPC-40083/v1.03/22)
and [temperature-zone definitions](https://reference.opcfoundation.org/specs/OPC-40083/v1.03/17.2.7)
describe these distinctions. Every selected AnalogItem must carry its
engineering units.

## Typed clients

Use the existing managed session rather than creating a second connection:

```csharp
var common = new PlasticsRubberClient(session, telemetry);
StartDeviceTypeClient start = common.StartDevice(startDeviceNodeId);
await start.StartRequestAsync(cancellationToken);

MonitoredParameterTypeClient parameter =
    common.MonitoredParameter(parameterNodeId);
await parameter.ResetMonitoringAsync(cancellationToken);
```

`AddPlasticsRubberClient()` registers `PlasticsRubberClientFactory` with the
existing client builder. The factory acquires the application's managed
session and registers the generated common encodings. It does not change
credentials or own the shared session's disposal.

Resolve capabilities through the concrete companion client's generated child
accessors or namespace-URI-based browsing. OPC 40083 does not prescribe a
global folder containing every common capability. Generated clients for all
remaining common types are available directly from the model assembly.
