# OPC UA LADS client

`OPCFoundation.NetStandard.Opc.Ua.LADS.Client` is the typed client for OPC
30500-1 Laboratory and Analytical Device Standard (LADS). The client discovers
LADS devices, reads functional units and their functions, starts programs and
downloads the result files they produce. Every
method call goes through the source-generated LADS ObjectType proxies, so a
client does not depend on the method NodeIds of a particular server.

```csharp
LadsClient lads = session.GetLadsClient(telemetry);

LadsDevice device = await lads.GetFirstDeviceAsync(ct);
LadsFunctionalUnit unit = await device.GetFirstFunctionalUnitAsync(ct);

string runId = await unit.StartProgramAsync(
    LadsProgramRequest.For("Calibration")
        .WithProperty("Operator", "lab-1")
        .WithSample(containerId: "Plate-7", sampleId: "S-0042"),
    ct);

await unit.WaitForStateAsync("Stopped", TimeSpan.FromMinutes(5), cancellationToken: ct);

LadsResult? result = await unit.FindResultAsync(runId, ct);
ByteString content = await lads.ReadResultFileAsync(result!.Files[0], ct);
```

## Handles and snapshots

The client separates navigation from data. `LadsDevice`, `LadsFunctionalUnit`
and `LadsFunction` are handles: they carry a NodeId and a browse name, and
every operation on them reads or calls the server. `ReadAsync` on a handle
returns an immutable snapshot record (`LadsDeviceSnapshot`,
`LadsFunctionalUnitSnapshot`, `LadsFunctionSnapshot`) that holds the values
read at that moment. A snapshot is not updated afterwards; read again or
observe the values to follow changes.

Each handle operation is also available on `LadsClient` with the NodeId as the
first argument, for callers that already know the node, such as
`lads.StopAsync(functionalUnitId)` or `lads.OpenCoverAsync(coverId)`.
`lads.Device(id)`, `lads.FunctionalUnit(id)` and `lads.Function(id)` create a
handle for a known NodeId without browsing.

## Discovery

`GetDevicesAsync` browses `DeviceSet` (OPC 10000-100) and `Machines`
(OPC 40001-1) and keeps the objects whose type is `LADSDeviceType` or a
subtype. A device that both folders reference is returned once.
`FindDeviceAsync` and `GetDeviceAsync` select a device by browse name;
`GetDeviceAsync` throws when no device matches.

`GetFunctionalUnitsAsync` follows `FunctionalUnitSet`, and `GetFunctionsAsync`
follows `FunctionSet`, including the function sets nested in a function up to
`LadsClientOptions.MaximumFunctionDepth`. Each `LadsFunction` reports the most
specific of the 20 standard function types it implements as a
`LadsFunctionKind`, and its `Path` relative to the functional unit, for
example `Heater/Temperature`. `LadsFunctionalUnit.GetFunctionAsync` accepts
either the path or the browse name.

## Reading

| Call | Reads |
| --- | --- |
| `ReadDeviceAsync`, `LadsDevice.ReadAsync` | identification, device state, operation mode, device health |
| `ReadFunctionalUnitAsync`, `LadsFunctionalUnit.ReadAsync` | functional unit state, running state, active program, function snapshots |
| `ReadFunctionAsync`, `LadsFunction.ReadAsync` | sensor values, target and current values, function state |
| `GetProgramTemplatesAsync` | the templates in `ProgramTemplateSet` |
| `ReadActiveProgramAsync` | the progress of the running program |
| `GetResultsAsync`, `ReadResultAsync` | the results in `ResultSet` with their files |
| `GetMaintenanceTasksAsync` | the maintenance tasks of a device |
| `ReadOperationCountersAsync` | the operation counters of a device |

A function snapshot contains the variables its type defines.
`LadsFunctionSnapshot.ValueOf("SensorValue")` returns the value of a member,
and a missing optional member returns a null `Variant` instead of an error.

## Commands

`LadsFunctionalUnit` exposes the state machine methods of the functional unit:
`StartAsync`, `StartProgramAsync`, `StopAsync`, `AbortAsync`, `ClearAsync`,
`HoldAsync`, `UnholdAsync`, `SuspendAsync`, `UnsuspendAsync`, `ResetAsync` and
`ToCompleteAsync`. `StartProgramAsync` returns the device program run id,
which `FindResultAsync` uses to locate the result after the run.

`LadsFunction` exposes the methods of control functions (`StartAsync`,
`StartAsync(targetValue)`, `StopAsync`, `AbortAsync`,
`ModifyTargetValueByAsync`, `ResetTotalizerAsync`) and of covers
(`OpenAsync`, `CloseAsync`, `LockAsync`, `UnlockAsync`, `ResetAsync`).
`LadsDevice` calls the device state and operation mode transitions
(`GotoOperateAsync`, `GotoProcessingAsync` and so on). The client calls
`StartMaintenanceTaskAsync`, `StopMaintenanceTaskAsync` and
`ResetMaintenanceTaskAsync` on a maintenance task.

A call on a node that lacks the required state machine fails with
`BadNotSupported`. A transition that the server rejects in the current state
fails with the status code the server returns.

## Observing

`LadsFunction.ObserveAsync` and `LadsClient.ObserveFunctionAsync` monitor the
sensor, control and state variables of a function and return an
`IAsyncEnumerable<LadsValueChange>`, starting with the current values.
`LadsFunctionalUnit.ObserveStateAsync` returns the state changes of the
functional unit, and `WaitForStateAsync` completes when the state machine
reaches the named state or throws `TimeoutException`.

```csharp
LadsFunction temperature = await unit.GetFunctionAsync("Temperature", ct);
await foreach (LadsValueChange change in temperature.ObserveAsync(cancellationToken: ct))
{
    Console.WriteLine($"{change.Name}: {change.Value.WrappedValue}");
}
```

Observation uses the default streaming subscription of a `ManagedSession`.
On any other session, pass an `IStreamingSubscription` explicitly; without
one, observation fails with `BadInvalidState`.

## Result files

`DownloadResultFileAsync` writes a result file into a stream and
`ReadResultFileAsync` returns its content as a `ByteString`. Both read the
`FileType` object of the result file in chunks of
`LadsClientOptions.FileChunkSize` and limit the download to
`LadsClientOptions.MaximumFileSize` bytes. A result file that only has a URL has no
`FileType` object; `LadsResultFile.CanDownload` is then `false` and the
download fails with `BadNotSupported`.

## Hosting

```csharp
services.AddOpcUa()
    .AddClient(...)
    .AddLadsClient(options => options with { SamplingInterval = TimeSpan.FromSeconds(1) });

Func<CancellationToken, Task<LadsClient>> createLads = provider
    .GetRequiredService<Func<CancellationToken, Task<LadsClient>>>();
LadsClient lads = await createLads(ct);
```

`AddLadsClient` registers a `LadsClientFactory` and a
`Func<CancellationToken, Task<LadsClient>>` together with the Device
Integration client. A `LadsClientOptions` singleton registered separately is
also honored. Outside dependency injection, use
`new LadsClientFactory(openSession, telemetry, options)` or
`session.GetLadsClient(telemetry, options)`.

For the server side use `Opc.Ua.LADS.Server`; the model lives in
`Opc.Ua.LADS`.
