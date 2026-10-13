# MachineTool client

Typed client access to OPC-40501-1 MachineTool and the independently modeled
OPC-40505 wireless-peripheral namespace. This package references generated
models and shared client services, not a server runtime. Applications can use
generated ObjectType proxies directly for advanced workflows.

`MachineToolClient.DiscoverAsync` uses the shared continuation-owning browser,
recognizes vendor subtypes, and deduplicates DI/Machinery discovery references.
`ReadSnapshotAsync` navigates generated proxies and preserves sample quality
and acquisition time; optional absence is distinct from a failed read.
`GetPeripheralProxy` works on WMTP-only endpoints without requiring a machine.

`AddMachineToolClient` registers an injectable managed-session factory.
`CreateMachineToolClient` wraps an existing session without taking ownership.
Clients created by the factory close their own sessions on asynchronous
disposal; borrowed sessions remain application-owned.

`ObserveDataAsync` creates a private options-based subscription for the same
monitoring and active-program variables returned by `ReadSnapshotAsync`. The
window includes setup and is limited to 60 seconds; at most 100 notifications
and one MiB of decoded sample payload are retained. Window expiry after setup
returns a bounded result, while caller cancellation, setup failures and
communication errors remain failures. Results expose the count limit and known
notification losses. Observation never writes equipment settings or controls.
Disposing the facade cancels and drains its observations, without deleting
subscriptions created by the application.

`ObserveValuesAsync` accepts 1-256 distinct non-null NodeIds for native companion
fields such as GMS qualification or calibration. It reuses the same collector,
limits, caller/disposal cancellation and subscription cleanup; it is not a
second subscription engine. The calling facade remains responsible for
resolving the intended instance's variables.

```csharp
await using MachineToolClient client = session.CreateMachineToolClient(telemetry);
MachineToolDataObservation observed = await client.ObserveDataAsync(
    machineId,
    new MachineToolObservationOptions
    {
        Window = TimeSpan.FromSeconds(5),
        MaximumNotifications = 50
    },
    cancellationToken);
```
