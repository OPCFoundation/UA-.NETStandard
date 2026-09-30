# Opc.Ua.Pumps.Client

Client-side access to **OPC 40223 — OPC UA for Pumps and Vacuum Pumps**.

```csharp
PumpsClient pumps = session.Pumps(telemetry);

await foreach (PumpEntry entry in pumps.EnumeratePumpsAsync(ct))
{
    PumpNameplate? nameplate = await pumps.ReadNameplateAsync(entry.NodeId, ct);
    PumpValueSet measurements = await pumps.ReadMeasurementsAsync(entry.NodeId, ct: ct);

    double? flow = measurements.GetDouble(BrowseNames.MassFlow);
    EUInformation? unit = measurements[BrowseNames.MassFlow]?.EngineeringUnits;
}
```

## Discovery

A conforming server publishes a pump twice — as a Device Integration device
below `DeviceSet` and as an OPC 40001-1 machine below `Machines`.
`EnumeratePumpsAsync` looks in both and de-duplicates. Matching is by type
hierarchy, so a vendor subtype of `PumpType` is found as well.

## Reading

Each of the seven `PumpType` groups has its own accessor, and
`ReadPumpAsync` takes the lot in one `PumpSnapshot`:

| Call | OPC 40223 group |
| --- | --- |
| `ReadNameplateAsync` | `Identification` |
| `ReadConfigurationAsync` | `Configuration` — design, implementation, system requirements |
| `ReadOperationalAsync` | `Operational` — measurements, signals, control, actuation, multi-pump |
| `ReadSupervisionAsync` | `Events` — the seven supervision categories |
| `ReadMaintenanceAsync` | `Maintenance` — general, preventive, condition-based, breakdown |
| `ReadDocumentationAsync` | `Documentation` |
| `ReadPortsAsync` | `Ports` — drive, inlet and outlet connection ports |

The open groups come back as a `PumpValueSet` keyed by browse name, filled
from what the server actually publishes rather than from a fixed list — so a
pump that publishes three of the 78 `DesignType` parameters yields three
entries, and one that publishes all 78 yields all 78. Index it with the
generated `Opc.Ua.Pumps.BrowseNames` constants.

Each `PumpValue` carries the engineering unit and instrument range alongside
the reading, because an OPC 40223 measurement without its unit is not
interpretable. That costs one extra round trip per group; pass
`PumpReadOptions.ValuesOnly` when polling, since the metadata does not change.

## Supervision

`PumpSupervisionStatus.ActiveSignals` collapses the roughly 150 boolean fault
signals across the seven categories into just the ones currently raised —
which is normally the question being asked.

## Hosting

```csharp
services.AddClient(...).AddPumpsClient();
```

registers a `PumpsClientFactory` and a `Func<CancellationToken, Task<PumpsClient>>`.

For the server side use `Opc.Ua.Pumps.Server`; the model, the generated types
and the shared snapshot contracts live in `Opc.Ua.Pumps`.
