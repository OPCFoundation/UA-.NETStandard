# AggregationServer

This sample hosts the generic WoT registry, runtime document materialization, and protocol-binding projection runtime. It contains no Pump-specific generated code; DI, Machinery, Pumps, and the Pump instance are loaded at runtime from WoT files.

It also includes [`MemoryWotBinding.cs`](Bindings/MemoryWotBinding.cs), a small in-memory custom binding and executor
that demonstrates how third-party protocol bindings plug into the runtime without shipping that demo code in the
`Opc.Ua.WotCon.Bindings` library. It is provided as a reference implementation and is deliberately not registered in
this sample's host, so the aggregation topology exercises only the real HTTP, Modbus and OPC UA bindings. To try it,
register it alongside the other bindings in `AggregationServerHost.Configure`:

```csharp
var memoryStore = new MemoryWotStore();
opcUa.AddWotProtocolBinders()
    .AddWotBinder(new MemoryWotBinder())
    .AddWotBindingExecutor(new MemoryWotBindingExecutor(memoryStore));
```

The executable server targets `net8.0`, `net9.0`, and `net10.0`, where its OPC UA binding executor is available. Legacy `CustomTestTarget` solution builds use a no-op shell and are not runnable sample configurations.

## Endpoint policy

WoT binding executors validate every outbound endpoint against a `WotEndpointPolicy` before opening a channel. The
default policy blocks loopback and private address ranges, so a server cannot be talked into reaching its own
listeners or a cloud metadata service on behalf of a remote caller.

This sample federates `SourceA` and `SourceB`, which run on the same host, so it opts in to loopback explicitly:

```csharp
opcUa.AddWotEndpointPolicy(new WotEndpointPolicy { AllowLoopback = true });
```

Only the loopback gate is opened; the scheme allow-list, blocked-host list, and private-range checks stay at their
secure defaults. A deployment that reaches real assets over the network should leave `AllowLoopback` off and instead
pin the reachable devices with `AllowedHosts`.

See the [WoT aggregation sample guide](../README.md) for topology, commands, monitoring, shadow replacement, troubleshooting, and NativeAOT publishing.
