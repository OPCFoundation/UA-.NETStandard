# OPCFoundation.NetStandard.Opc.Ua.Machinery.Client

Client-side helpers for the OPC 40001 *OPC UA for Machinery* specification
series.

A Machinery server publishes its machines in one well-known place — the
`Machines` folder that OPC 40001-1 organizes from `Objects`, **not** from the
Device Integration `DeviceSet`. This package starts there and hands back typed
access to what a machine exposes.

```csharp
MachineryClient machinery = session.Machinery(telemetry);

await foreach (MachineEntry machine in machinery.EnumerateMachinesAsync(ct))
{
    MachineIdentification id = await machinery.ReadIdentificationAsync(machine.NodeId, ct);
    Console.WriteLine($"{id.Manufacturer} {id.Model} — {id.SerialNumber}");

    FiniteStateSnapshot? state = await machinery.GetItemStateAsync(machine.NodeId, ct);
    Console.WriteLine($"  state: {state?.CurrentState}");
}
```

| Concern | API |
| --- | --- |
| Discovery | `EnumerateMachinesAsync`, `DiscoverMachinesAsync`, `FindComponentsAsync` |
| Identification | `ReadIdentificationAsync` |
| State | `GetItemStateAsync`, `ObserveItemStateAsync`, `GetOperationModeAsync`, `ObserveOperationModeAsync` |
| Process values | `ReadProcessValueAsync` |
| Jobs | `JobManagement(machine)` → `Isa95JobControlV2Client` |
| Results | `ResultManagement(machine)`, `DownloadResultAsync` |

State observation rides the generated state-machine proxies, so it works
against any server that publishes the OPC 40001-1 state machines — including
vendor subtypes.

For bounded inspection with explicit source, quality and timestamp evidence,
opt in without changing legacy callers:

```csharp
MachineryClient strict = session.Machinery(telemetry, new MachineryClientOptions
{
    MaximumBrowseReferences = 1024,
    MaximumBrowsePages = 128,
    MaximumReadArrayItems = 1024
});

MachineIdentification? id = await strict.ReadIdentificationAsync(selectedMachineId, ct);
MachineryStateSnapshot? state = await strict.ReadItemStateAsync(selectedMachineId, ct);
MachineryReadValue health = await strict.ReadDeviceHealthValueAsync(selectedMachineId, ct);
```

Strict detail records expose `ArrayOf<MachineryReadValue> Values`. Each field
retains its local source NodeId, qualified path and original DataValue. Good-null
is present; only genuine optional absence sets `IsAbsent`. Denied/Uncertain
reads, wrong types, incomplete responses and ambiguous/foreign translations
throw `MachineryReadException`, preserving status and offending evidence.
Browse budgets are enforced while following pages, including progressing pages
that reuse opaque continuation tokens. Continuation cleanup accepts standard
empty release responses and has an independently bounded five-second deadline.
Decoding and opaque vendor payload sizes still
depend on session quotas.

Strict lifetime and equipment-life bounds are mandatory Number fields.
Scalar and one-dimensional numeric warning thresholds remain in their original
evidence when they are not Double arrays. Strict meters require ApplicationTag,
keep StartTime and other properties as metadata, and validate measurements
against their declared data type and rank, including ECM three-phase structures.

Use `EnumeratePublishedResultsAsync` for result identities and
`ReadPublishedResultAsync` for an explicitly selected variable. Job-list and
state snapshot APIs retain evidence without mutable client-wide last-read
state. Options also flow through `AddMachineryClient(options)` and
`MachineryClientFactory`. Generated proxies, event observation, job commands
and file transfers keep their own contracts. See the developer guide for
strict job-response-list semantics and array limits.

For bounded result exports, pass a writable, caller-owned stream and an explicit
byte limit. This overload downloads only the selected `ResultId`; it never
enumerates results or retrieves a result URI.

```csharp
long bytesWritten = await machinery.DownloadResultAsync(
    machineId, selectedResultId, destination, maxBytes: 1024 * 1024, cancellationToken: ct);
```

The limit is enforced during streaming, without trusting a declared file size.
Reads use at most 64 KiB per request and at most 1024 requests, including the
end-of-file probe. A zero limit accepts only an empty payload. The server handle
is closed with an independent five-second deadline, even when the download is
cancelled. If downloading and closing both fail, `AggregateException` preserves
the download failure first and the close failure second. The session and
destination stay open; discard any partial destination after failure.
On a ManagedSession, the result path and transfer share one captured session
generation. Bounded browse continuations use the same ownership rule; neither
operation forwards owned state to a replacement session after recovery.

The existing `DownloadResultAsync(machineId, resultId, ct)` overload still returns
a buffered `ByteString` with its original behavior. Prefer the streaming overload
for exports that must remain bounded.

See the [Machinery developer guide](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/docs/Machinery.md).
