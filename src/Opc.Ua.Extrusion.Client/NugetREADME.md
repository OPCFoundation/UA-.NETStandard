# OPC UA Extrusion Client

Typed clients for OPC 40084 parts 1–12, version 2.00, on the application's
existing session or `ManagedSession`.

All twelve wire models are supplied by the single `Opc.Ua.Extrusion` model
package. Client entrypoints are organized in per-part source folders without
changing their public namespaces. The client project opts into Roslyn
`ModelSourceGeneratorClientAccessorsOnly` generation for every part; generated
accessors are build output, not checked-in C# or Python output.

```csharp
services.AddOpcUa().AddClient(clientOptions).AddExtrusionClient();
ExtrusionClient client = await services
    .GetRequiredService<Func<CancellationToken, Task<ExtrusionClient>>>()(ct);

var extruder = client.Extruder(extruderNodeId);
var present = await extruder.IsPresentVariable().ReadAsync(ct);
await extruder.LineIdVariable().WriteAsync("Line-1", ct);
```

The facade returns each device's actual generated proxy, not an interchangeable
extruder interface. Model-derived extensions add 141 typed variable accessors
and 17 typed repeated-child enumerators with statically selected codecs, backed
by the reusable `ClientVariable<T>` and `ClientRead<T>` in `Opc.Ua.Client`.
Reads retain the native `DataValue`, including bad quality and source/server
timestamps. No reflected or boxed-value conversion is required.

Discovery handles real vendor subtypes and deduplicates Machinery/DI/component
references. State observation, generated event decoding, job methods, common
maintenance and the complete common production-dataset/file workflow share the
same authenticated session. Observation disposal removes only its monitored
item, not another consumer's session.

See `docs/Extrusion.md` in the repository for all feature selections, usage and
the published Filter-profile discrepancy. No formal certification is claimed.
The package includes `licenses/CompanionModels.NOTICE.txt`.
