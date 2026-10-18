# OPC UA MachineTool and wireless-peripheral server

Provider-backed support for released OPC 40501-1 MachineTool 1.02.0 and
OPC 40505 Wireless Machine Tool Peripherals (WMTP) 1.0.0. Both models share
this package and `Opc.Ua.MachineTool`, but retain independent wire namespaces
and runtime selection.

## Hosting

`AddMachineTool` / `ConfigureMachineTool` provide standalone hosting.
Use `AddMachineToolToMachinery` / `ConfigureMachineToolFor<TNodeManager>`
with an existing compatible DI/Machinery owner; do not duplicate that owner.

`MachineToolServerOptions.Parts` selects MachineTool, WMTP, or both.
MachineTool alone is the default. WMTP-only hosting creates no fictitious
machine. Inside an awaited configuration callback:

```csharp
using Opc.Ua;
using Opc.Ua.MachineTool;
using Opc.Ua.MachineTool.Server;

var provider = new InMemoryMachineToolProvider(new MachineToolSnapshot());
MachineToolRuntime<MachineToolState> machine = await context
    .AddMachineTool("Mill")
    .WithProvider(provider)
    .WithIdentification(identity =>
    {
        identity.Manufacturer = new LocalizedText("Example manufacturer");
        identity.SerialNumber = "Mill-001";
        identity.ProductInstanceUri = "urn:example:mill:001";
    })
    .BuildAsync(cancellationToken);
```

Keep the handle for acquisition rather than disposing it at callback exit.
Initial observations are unavailable, not successful zeros. Supply immutable
equipment observations and call `RefreshAsync`.

## Features and providers

Mandatory structure is the default. Explicit options select monitoring,
equipment/tool life, retained production, prognoses, notifications,
Machinery/ISA95 jobs and machine files. Missing required providers or data
fail configuration rather than advertising non-working features.

The older Production model remains implemented because MachineTool and GMS
still require it. It includes states, transitions, parts and interruption
conditions. `WithJobManagement` requires both existing ISA95 receiver and
response-provider interfaces. `MachineToolJobSource` is a bounded,
non-durable consistent reference source, not a CNC executor.

Tools/forecasts use native types, NodeVersion/model-change events and shared
final-value publication/node lifetime. Modules validate before coherent
commit. `RetryPublicationAsync` retries accepted notifications, never
equipment effects.

## Wireless peripherals

Use `context.AddWirelessPeripheral` with explicitly selected per-instance
providers, identification and measurement definitions. The eight standard
kinds reuse Machinery ProcessValues/PA-DIM with original numeric types,
units, status and acquisition time.

Trigger and delta cannot both be zero. Before acceptance, refusal/cancellation
leaves settings unchanged. After provider acceptance, later caller cancellation
does not discard the accepted configuration.

Work and service stores are independent. Reports copy rather than consume
records. Complete reports exceeding bounds fail; supported index/time ranges
select smaller data. Delete and peripheral-scoped Abort are explicit,
authorized operations.

Calibration, device time and firmware/licence transfer use configured
adapters. Successful transfer is not firmware installation, licence
activation or physical calibration. No Bluetooth/pairing/flash driver is
included.

## Ownership and conformance

Providers remain application owned unless explicitly transferred.
Intentional sharing must coordinate the shared equipment; implicit unkeyed
hardware-provider selection is not supported. Failed construction preserves
caller resources; successful runtimes drain their own registrations.

Capabilities reflect committed instances and deployment conditions, not
namespace presence. Security claims require actual endpoint policies,
certificates and user-token support. The library neither replaces equipment
safety nor independently certifies a device.

See the [MachineTool/WMTP guide](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/docs/MachineTool.md)
for clients, MCP, samples, bounds and conformance evidence.
