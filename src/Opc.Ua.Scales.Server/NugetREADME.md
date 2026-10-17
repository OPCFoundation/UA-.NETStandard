# Opc.Ua.Scales.Server

Server hosting for the source-generated **OPC 40200 Weighing Technology
(Scales)** model, version 2.00.

- `AddScales()` registers a DI-based node manager that loads the DI, IA,
  Machinery, PackML and Scales models; `ConfigureScales(...)` creates scales
  once the address space is ready.
- `AddScalesToMachinery()` and `ConfigureScalesFor<MachineryNodeManager>()`
  add the same complete runtime to an existing Machinery owner, without
  another DI manager. The owner must include `MachineryParts.BuildingBlocks`.
- `CreateScaleAsync(name, ScaleKind, builder => ...)` materialises any of the
  fourteen concrete scale types into the `DeviceSet` and the Machinery
  `Machines` folder; `CreateScaleSystemAsync` builds a scale system with its
  scales.
- The returned `ScaleHandle` runs the scale: `PublishLoad` feeds the raw load,
  and the runtime derives gross, net and tare, rounds to the weighing range,
  drives the overload/underload alarms and implements `SetZero`, `SetTare`,
  `ClearTare`, `SetPresetTare` and `RegisterWeight`.
- Production presets with product selection, management and DI locking; the
  PackML state machine; scale events and alarms with the Annex C notification
  ids; and the methods of every scale type - loss-in-weight discharge/refill,
  piece-counting reference weighing, laboratory draft shields and calibration,
  vehicle inbound/outbound/one-pass weighing, feeder speed, totalizers and
  recipe management and processing.

```csharp
builder.Services.AddOpcUa()
    .AddServer(...)
    .AddScales()
    .ConfigureScales(async context =>
    {
        ScaleHandle scale = await context.Manager.CreateScaleAsync(
            new QualifiedName("Scale1"),
            ScaleKind.Simple,
            b => b
                .WithIdentification(new ScaleIdentification
                {
                    Manufacturer = new LocalizedText("Contoso"),
                    SerialNumber = "0001",
                    ProductInstanceUri = "urn:contoso:scale:0001"
                })
                .WithWeighingRange(new WeighingRangeDefinition(0, 30, 0.005, 0.01))
                .WithWeightDetails()
                .WithZeroAndTare()
                .WithRegisterWeight(),
            context.CancellationToken);
        scale.PublishLoad(1.234);
    });
```

## Shared Machinery owner

Register `AddMachinery()` followed by `AddScalesToMachinery()`. Configure with:

```csharp
serverBuilder.ConfigureScalesFor<MachineryNodeManager>(async (context, ct) =>
{
    ScaleHandle scale = await context.Runtime.CreateScaleAsync(
        new QualifiedName("Scale1", context.Runtime.InstanceNamespaceIndex),
        ScaleKind.Simple,
        b => b.WithIdentification(identification)
            .WithWeighingRange(new WeighingRangeDefinition(0, 30, 0.005, 0.01))
            .WithPackMLState(),
        ct);
    scale.PublishLoad(1.234);
});
```

`ScalesRuntime` shares all scale kinds, systems, subdevices and controllers with
the standalone manager. Instances and AMB asset additions stay on the same owner,
including when Pumps is added with `AddPumpsToMachinery()`. Nested events reach
their system and server subscribers. Shutdown releases file buffers, handles and
session lock subscriptions.

For direct construction, pass `ScalesMachineryModelProvider` to the stock
`MachineryNodeManager` alongside its building-block provider, then call
`owner.CreateScalesRuntime()` after initialization. No inheritance changes are
required, and the stock owner manages disposal.

## Target frameworks

net48, net8.0, net9.0, net10.0

## Additional documentation

See the [Scales developer guide](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/docs/Scales.md).
