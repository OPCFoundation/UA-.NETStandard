# Shared reference job source

`MachineToolJobSource` is a bounded, **single-host, non-durable simulator** for
applications exposing both retained MachineTool production and Machinery
JobManagement. It owns one `InMemoryIsa95JobControlProvider`; that existing engine
decides whether ISA-95 operations and execution transitions are accepted. This
adapter does not implement a second job-control protocol or command CNC hardware.

## Bind the same instance twice

Supply explicit job/work-master/program identities. The intentionally narrow
reference projection models one program and, optionally, one aggregate part set
per job. It never guesses program names, physical part identities, equipment
measurements, or production quantities from arbitrary ISA-95 parameters.

```csharp
await using var source = new MachineToolJobSource(new MachineToolJobSourceOptions
{
    Jobs =
    [
        new MachineToolJobSourceDescriptor
        {
            JobOrderId = "Order-42",
            WorkMasterId = "WorkMaster-7",
            ProgramName = "ApplicationSupplied.nc",
            RunsPlanned = 2,
            PartSetKey = "Batch",
            PartSetName = "ApplicationSuppliedParts",
            PartsPlannedPerRun = 10
        }
    ]
});

await using var machine = await context.AddMachineTool("ReferenceMachine")
    .WithIdentification(identity =>
    {
        identity.Manufacturer = new LocalizedText("Reference application");
        identity.SerialNumber = "Simulation-1";
        identity.ProductInstanceUri = "urn:example:reference-machine";
    })
    .WithProvider(source)
    .WithProduction(new MachineToolProductionOptions
    {
        Features = MachineToolFeatures.Production |
            MachineToolFeatures.DynamicProductionPlan |
            MachineToolFeatures.ProductionParts
    })
    .WithJobManagement(new MachineToolJobManagementOptions
    {
        Receiver = source,
        ResponseProvider = source,
        Catalog = source
    })
    .BuildAsync();
```

Build the runtime before accepting orders, then use
`await source.DrainAsync(machine.RefreshAsync)` to publish **every** accepted
revision in order. A direct `machine.RefreshAsync()` reads only the latest
snapshot and is unsuitable when intermediate transitions have accumulated.
The drain callback has a pinned `ReadAsync`/`CaptureAsync` view and runs outside
source gates. Failed or cancelled delivery retains its exact head for retry.
The existing production controller validates transitions, stages nodes, retains
events for retry, and provides its real final-observation deletion barrier.
Do not attach a second independent legacy publisher to the same drain cursor.

DI registration is optional:
`services.AddMachineToolJobSource(options, initialMonitoring)`. Each asynchronous
DI scope owns one source and resolves all existing provider facets to that same
instance. Use one scope per machine. Direct machine bindings borrow the source;
the application disposes it after its runtimes. Clocks and telemetry are borrowed.

## Execution and projection semantics

* `Store`, `StoreAndStart`, `Start`, `RevokeStart`, and `Update` retain the
  legacy **Initializing** phase. They do not select an active program or execute
  anything physically.
* Explicit `TransitionAsync(id, BeginExecution)` begins simulated execution and
  selects that declared program. This single-machine reference source admits
  only one executing/interrupted order at a time.
* `Pause`/`Hold` project to **Interrupted**; `Resume` projects back to **Running**.
* `RecordPartsAsync` accepts explicit current-run completed/good counts and their
  original acquisition metadata. Counts cannot decrease or exceed the declared
  plan. Good means the standard Good, CapabilityUnavailable, or WillNotBeMeasured
  quality categories. No individual part nodes or measurements are fabricated.
* `CompleteRunAsync` is an explicit simulation execution observation, not an OPC
  UA Method invented by this adapter. It requires all declared current-run parts
  to be accounted for. Intermediate runs produce canonical **RunningToRunning**
  transitions and move prior totals into the cumulative baseline. The final run
  invokes the existing engine's `Complete` transition and projects to **Ended**.
* An explicit engine `Complete` while Running also normally ends the final
  planned run when its declared parts have been accounted for. Early completion,
  completion while interrupted, and client `Stop` are incomplete legacy
  production and project to **Aborted**, without inventing a resume or a normally
  completed run. ISA-95 **Ended** and legacy **Ended** are not interchangeable.
* `Abort` projects to **Aborted**. `Clear` removes a terminal job. `Cancel`
  retains a final **InitializingToAborted** observation and a subsequent removal
  observation; the modern engine emits only its actual catalog removal.

Every transition advances the appropriate legacy sequence exactly once.
`CaptureAsync` returns detached modern orders/responses together with the
immutable legacy snapshot at one revision. Separate protocol queries are normal
live engine queries, not a multi-call transaction. State, identifiers, comments,
work masters, and accepted response fields retain the existing engine semantics.

Responses are supplied explicitly through `ReceiveJobResponseAsync`, using the
still-stored order's current complete state path from the catalog. Ingestion
cannot change an order's execution state. Responses remain historical after
later transitions and are queried through the actual response-provider methods.
There is no fabricated JobManagement response-receiver child or automatic result.
Set the generated structures' `EncodingMask` flags for populated optional fields
(for example `ISA95JobOrderDataTypeFields.WorkMasterID`). The source validates a
bounded binary round-trip and refuses inconsistent masks instead of silently
discarding supplied fields. This is also required when calling generated wire
proxies, where unselected optional fields are not transmitted.

## Measurements, limits, and lifetime

Constructor monitoring or `SetMonitoringAsync` supplies base monitoring and
optional lifetime statistics. Its quality and acquisition timestamps are
retained; unavailable values remain unavailable. The source owns
`ActiveProgram` and `Production.Jobs`. Equipment/prognosis projection and
individual-part/interruption modeling are outside this reference adapter.
Derived simulation job/program state and cumulative counters use
`UncertainSubstituteValue`, not fabricated Good hardware quality. Explicit
aggregate part-set observations keep their supplied quality and acquisition time.
Lifetime equipment counters are never incremented from job acknowledgments.

Options bound orders, responses, declarations, full encoded payload sizes,
strings, arrays, nesting, queued revisions, subscribers, and each subscriber's
pending changes. Admission fails **before commit** when a bound is reached; no
transition or subscriber event is silently discarded. The adapter reuses the
engine's independent status, catalog, and response subscriptions, adding admission
accounting and a post-commit delivery barrier rather than competing for a single
shared iterator. Stop consuming and dispose a subscriber when it is no longer
needed, or it will correctly apply backpressure.

Cancellation before acceptance leaves both representations unchanged.
Cancellation after acceptance cannot erase a committed revision. Disposal
prevents new operations, cancels and joins publication, completes the owned
engine's streams, and releases its gates only after admitted operations drain.
Publication callbacks may read, but recursive mutation, drain, or disposal is
rejected promptly.

This module is not a hardware adapter, durable scheduler, HA coordination
mechanism, or a claim of complete MachineTool conformance.
