# OPC UA plastics and rubber common clients

Managed-session integration and typed workflows for OPC 40083 common
capabilities, including production dataset metadata and file transfer.
Generated proxies and structured data types come from `Opc.Ua.PlasticsRubber`.

```csharp
var common = new PlasticsRubberClient(session, telemetry);
await common.StartDevice(startDeviceNodeId).StartRequestAsync(cancellationToken);
await common.MonitoredParameter(parameterNodeId).ResetMonitoringAsync(cancellationToken);
```

`AddPlasticsRubberClient()` registers `PlasticsRubberClientFactory` on the
existing client builder. The factory uses the application's managed session;
it does not create another connection, change credentials or dispose the shared
session.

Resolve capability NodeIds through the concrete companion model's generated
child accessors or namespace-aware browsing. OPC 40083 does not define a
universal machine root or a global folder for every common capability.

`ProductionDatasetClient` provides bounded upload/download, typed metadata and
storage-layer operations. Temporary-file metadata uses the server's existing
session-owned handle; final commit and rollback remain owned by the stack's
temporary-file wrapper.

See the [common capability guide](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/docs/PlasticsRubber.md)
and [production dataset guide](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/docs/PlasticsRubberDatasets.md).
The included `licenses/CompanionModels.NOTICE.txt` records the common model's
independently authored, MIT-basis implementation provenance. Package use does
not constitute formal OPC Foundation certification.
