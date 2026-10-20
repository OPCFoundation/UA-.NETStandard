# Opc.Ua.AMB.Client

Client-side access to **OPC 10000-110 — Asset Management Basics (AMB)**.

```csharp
AmbClient amb = session.AssetManagement(telemetry);

foreach (NodeId asset in await amb.DiscoverAssetsAsync(ct))
{
    AssetSnapshot snapshot = await amb.ReadAssetAsync(asset, ct);
    Console.WriteLine($"{snapshot.Identification.ProductInstanceUri}: {snapshot.DeviceHealth}");
}
```

## Discovery

`FindAssetsAsync` calls `FindAlias` on the alias categories `Assets`,
`AssetsByProductInstanceUri` and `AssetsByAssetId`; `EnumerateAssetsAsync`
browses the alias objects instead. `DiscoverAssetsAsync` returns every asset of
the server once. `ReadNodeVersionAsync` and `ObserveAssetSetChangesAsync`
follow changes of the categories.

## Reading

| Call | OPC 10000-110 |
| --- | --- |
| `ReadIdentificationAsync`, `WriteAssetIdAsync` | §7, on the asset or in `2:Identification` |
| `ReadDeviceHealthAsync`, `ReadHealthAlarmsAsync` | §9 |
| `ReadMaintenanceActivitiesAsync` | §12 |
| `ReadDocumentationLinksAsync`, `AddDocumentationLinkAsync`, `RemoveDocumentationLinkAsync` | §10.5 |
| `ReadEntriesAsync` | §10.6, §10.7 |
| `ReadContextAsync`, `ReadLocationsOfAssetAsync`, `BrowseLocationsAsync` | §11, §13 |
| `EnumerateSubAssetsAsync`, `ReadRelationsAsync` | §14 |
| `ReadAssetAsync` | all of the above for one asset |

Pass `AmbClientOptions` to the client constructor to opt into bounded traversal
and strict response validation. Read records then retain original source nodes,
quality and timestamps in their `Values` evidence. A present Good-null structure
is not an absent optional path; scalar type mismatches are still rejected.

## Events

`ObserveHealthAlarmsAsync` and `ObserveMaintenanceAsync` subscribe to the
condition events and return `AssetAlarmRecord` and `MaintenanceActivityRecord`
values, including the potential root causes, the maintenance state and the
comment of an acknowledgement. A maintenance activity is told apart by its
condition class (`MaintenanceConditionClassType` or a subtype), and the two
filters select disjoint events. The fields of the AMB interfaces are selected
with `BaseEventType` as the type definition, since a server checks select
clauses against the type hierarchy of an event, which interfaces are not part
of. Tolerant read calls ask for a condition refresh when there is no
`2:DeviceHealthAlarms` folder; strict reads require the folder and do not start
an implicit refresh. Event observation still falls back to known maintenance
classes if subtype browsing fails or reaches a traversal limit, without caching
an incomplete class set. `AcknowledgeAsync` acknowledges a condition.

## Hosting

```csharp
services.AddOpcUa().AddClient(...).AddAssetManagementClient();
```

registers an `AmbClientFactory` and a `Func<CancellationToken, Task<AmbClient>>`.

Use `AddAssetManagementClient(new AmbClientOptions { MaximumBrowseReferences = 1024,
MaximumBrowsePages = 128 })` to apply strict limits to both factories. An
`AmbClientOptions` singleton registered separately is also honored. Without
options, both retain tolerant behavior. Direct construction supports
`new AmbClientFactory(openSession, telemetry, options)` as well.

For the server side use `Opc.Ua.AMB.Server`; the model and the shared
contracts live in `Opc.Ua.AMB`.
