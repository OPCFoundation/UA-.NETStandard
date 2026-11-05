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
- `ReadDeviceAsync` with `AutoIdReadOptions` (`Default`, `StatusOnly`), one
  Browse and one Read per call; the snapshot also carries the NodeIds of the
  variables to monitor and the methods the device publishes.
  `RefreshDeviceAsync` re-reads the status, last scan and location of a snapshot
  in one Read without browsing.
- Asynchronous streams of scan, access, presence and logbook entry events.
- `ReadScanParametersAsync` and `WriteScanParametersAsync` explicitly browse
  nested runtime settings and preserve individual statuses. `ReadScanActiveAsync`
  and `SetScanActiveAsync` operate the Data Access control variable.
  `ObserveScanActiveAsync` observes it through a caller-owned subscription.
  `ReadLastScanAsync` checks matching acquisition timestamps, including available
  RFID antenna/RSSI values, with bounded retries; ordinary refreshes stay lightweight.

A browse the server rejects throws `ServiceResultException` instead of yielding an
empty list, cancellation is honoured by every asynchronous member, and the
session is borrowed, never closed. Optical-verifier `ScanAsync` returns
`OpticalVerifierScanResult[]` as specified by OPC 30010 6.4.3.1; the imported
model corrects the published NodeSet's conflicting result type. Asynchronous
scanning and scan events remain available for verifiers.

See the [AutoID guide](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/docs/AutoID.md).
