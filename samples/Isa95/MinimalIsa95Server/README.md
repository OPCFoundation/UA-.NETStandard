# Minimal ISA-95 Server

This sample hosts the OPC-10030 ISA-95 common model together with OPC-10031-4 Job Control V1 and V2.

It uses the typed common-model builder, a provider-backed `GeoSpatialLocationType`, and the reusable in-memory Job Control provider. The seeded V2 job demonstrates the `StoreAndStart` and automatic `AllowedToStart` to `Running` transition.

## Running

```powershell
dotnet run --project samples\Isa95\MinimalIsa95Server\MinimalIsa95Server.csproj
```

The server listens on `opc.tcp://localhost:62545/MinimalIsa95Server` by default (override with `--port`).

## What this sample demonstrates

- Server registration via `services.AddOpcUa().AddServer(...).AddIsa95Server().ConfigureModel(...)`.
- Populating the OPC-10030 common model: Personnel, Equipment, PhysicalAsset, and the full Material class/definition/lot/sublot hierarchy, including `DefinedByMaterialClass` and `MadeUpOfMaterialSublot`.
- Binding a `GeoSpatialLocationType` instance to the shared `InMemoryGeoLocationProvider`, seeded with a fixed WGS84 position that is published as WKT.
- Seeding a Job Control V2 job order (`DemoJobSeeder`) with `StoreAndStart` followed by the automatic execution-controller `BeginExecution` transition to `Running`.

See the [ISA-95 developer guide](../../../docs/ISA95.md) for the full hosting model, the Job Control V1/V2 state engine, the two documented normative NodeSet repairs, and a conformance matrix.
