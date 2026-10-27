# AutoID composition with Machinery

`OPCFoundation.NetStandard.Opc.Ua.AutoID.Machinery.Server` adds AutoID readers
to a Machinery server without creating a second Device Integration node manager.
Install this package when using `AddAutoIdToMachinery`; reader-only servers need
only `OPCFoundation.NetStandard.Opc.Ua.AutoID.Server`.

```csharp
services.AddOpcUa()
    .AddServer(options => { /* application name, endpoints, PKI */ })
    .AddMachinery(options => options.Parts = MachineryParts.BuildingBlocks)
    .AddAutoIdToMachinery()
    .ConfigureMachinery(async (machines, ct) =>
    {
        await machines.AddMachine(new QualifiedName("Press1", machines.InstanceNamespaceIndex))
            .WithIdentification(id =>
            {
                id.Manufacturer = new LocalizedText("Acme");
                id.SerialNumber = "PR-1";
                id.ProductInstanceUri = "urn:acme:press:1";
            })
            .BuildAsync(ct);
    })
    .ConfigureAutoIdFor<MachineryNodeManager>(async (context, ct) =>
    {
        await context.Runtime
            .Reader(new QualifiedName("DockDoor1", context.Runtime.InstanceNamespaceIndex), hardware)
            .BuildAsync(ct);
    });
```

`AddMachinery` must precede `AddAutoIdToMachinery`. Machinery configuration runs
before the AutoID callbacks, which share the owner's AutoID runtime. Readers
remain reachable from DeviceSet; they do not enter the Machines folder.
The owner releases the runtime and its readers when the server stops.

See the [AutoID guide](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/docs/AutoID.md)
for reader providers, placement and component identification.
