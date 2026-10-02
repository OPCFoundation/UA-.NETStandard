# Opc.Ua.Pumps.Server

Server hosting for **OPC 40223 — OPC UA for Pumps and Vacuum Pumps**.

```csharp
builder.Services
    .AddOpcUaServer(configuration)
    .AddPumps()
    .ConfigurePumps(async context =>
    {
        IPumpBuilder pump = await context.Manager.CreatePumpAsync(
            new QualifiedName("Pump_1", context.Manager.InstanceNamespaceIndex),
            context.CancellationToken);

        pump.WithNameplate(new PumpNameplate
        {
            NodeId = NodeId.Null,
            Manufacturer = new LocalizedText("Acme Pumps"),
            SerialNumber = "SN-001",
            Model = new LocalizedText("PumpX-2000"),
            CountryOfOrigin = "DE"
        });

        pump.Measurements
            .SetAnalog(BrowseNames.MassFlow, 12.5, EngineeringUnits.KilogramsPerSecond)
            .SetAnalog(BrowseNames.DifferentialPressure, 350_000, EngineeringUnits.Pascal);

        pump.Supervision(BrowseNames.SupervisionProcessFluid)
            .SetDiscrete(BrowseNames.Cavitation, false);

        pump.AddPort(PumpPortKind.InletConnection, "Suction")
            .SetAnalog(BrowseNames.MaximumAllowableInletPressure, 1_600_000);
    });
```

## What it does

`PumpsNodeManager` derives from `DiNodeManager` — a pump *is* a Device
Integration device — and loads DI, OPC 10000-200 IA, OPC 40001-1 Machinery and
OPC 40223 Pumps into one address space, in that order (Machinery's
`Stacklight` is typed by an IA type, so IA has to be loaded first).
`CreatePumpAsync` materialises a `PumpType` instance below `DeviceSet` and
organizes it into the Machinery `Machines` folder as well, so both a DI client
and a Machinery client find it.

## Building a pump

Nothing is materialised until it is asked for. The full `PumpType` surface is
about 3000 nodes; a pump that publishes a flow rate and a cavitation flag
should publish those, not 3000 mostly empty variables.

`IPumpBuilder` exposes the seven groups and their sub-groups as properties, and
`IPumpGroupBuilder` addresses everything below them by browse name — `Add`,
`Set`, `SetAnalog`, `SetDiscrete`, `With`, `Nested`. Children are created
through the model's own `CreateChild`, so each one gets the type, data type,
access level and default value the specification declares; a name the group
does not declare is rejected rather than quietly creating a non-conforming
node. Pass the generated `Opc.Ua.Pumps.BrowseNames` constants and a rename in
the model breaks the build.

`SetDiscrete` handles both shapes OPC 40223 uses for a boolean: the
`TwoStateDiscreteType` variables of the supervision groups, and the
`DiscreteInput`/`DiscreteOutputObjectType` objects of the signal and actuation
groups whose value sits one level down.

## Typed fluent accessors

This package also supplies the typed `Opc.Ua.Server.Fluent` accessors for the
whole Pumps model, so `builder.Node<PumpState>(id).Components().Events()` and
friends are available to servers that wire per-node callbacks directly.

## Ownership

`AddPumps` claims the Device Integration address space, so it cannot be
combined with `AddOpcUaDi` or another DI-owning registration. A server that
already has a DI manager loads the model into it with
`nodes.AddOpcUaPumps(context)` and configures it through
`ConfigureDevicesFor<TNodeManager>()`.

For the client side use `Opc.Ua.Pumps.Client`; the model and the shared
snapshot contracts live in `Opc.Ua.Pumps`.
