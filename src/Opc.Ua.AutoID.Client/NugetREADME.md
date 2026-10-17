# OPC UA AutoID client

`OPCFoundation.NetStandard.Opc.Ua.AutoID.Client` discovers and operates OPC 30010
AutoID readers over a connected session.

```csharp
AutoIdClient autoId = session.AutoId(telemetry);   // or services.AddAutoIdClient()

await foreach (AutoIdReaderEntry reader in autoId.EnumerateReadersAsync())
{
    AutoIdDeviceSnapshot device = await autoId.ReadDeviceAsync(reader);
    AutoIdScanResponse scan = await autoId.ScanAsync(
        reader, new ScanSettings { Duration = 1000, DataAvailable = true });
}

await foreach (AutoIdScanEventTypeRecord e in
    autoId.ObserveScanEventsAsync(session.DefaultStreaming, reader, ct))
{
    // e.ScanResult holds RfidScanResult, OcrScanResult, ... instances
}
```

## What it covers

- Discovery of RFID, OCR, optical, optical verifier and RTLS readers (vendor
  subtypes included) below the DI `DeviceSet` or any folder.
- `ScanAsync`, `ScanStartAsync`, `ScanStopAsync`, `GetDeviceLocationAsync`.
- RFID `ReadTagAsync`, `WriteTagAsync`, `WriteTagIdAsync`, `KillTagAsync`,
  `LockTagAsync`, `SetTagPasswordAsync`; the status a device reports for a tag is
  returned, not thrown.
- RTLS `GetLocationAsync` and `GetSupportedLocationTypesAsync`.
- `ReadDeviceAsync` with `AutoIdReadOptions` (`Default`, `StatusOnly`).
- Asynchronous streams of scan, access, presence and logbook entry events.

A browse the server rejects throws `ServiceResultException` instead of yielding an
empty list, cancellation is honoured by every asynchronous member, and the
session is borrowed, never closed. `ScanAsync` refuses an optical verifier (the
NodeSet types its `Scan` results as `WGS84Coordinate`); use `ScanStartAsync` and
the scan events.

See the [AutoID guide](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/docs/AutoID.md).
