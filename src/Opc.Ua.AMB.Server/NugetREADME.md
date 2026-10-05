# Opc.Ua.AMB.Server

Server hosting for **OPC 10000-110 — Asset Management Basics (AMB)**.

The Asset Management Basics node manager is a sidecar: it loads the AMB model
next to whatever node manager owns the Device Integration address space
(`AddOpcUaDi`, Machinery, Pumps, Scales) and serves the assets those managers
register with the shared `IAssetManagement` registry.

```csharp
services.AddOpcUa()
    .AddServer<StandardServer>(ConfigureServer)
    .AddOpcUaDi()
    .AddAssetManagement(options => options.UseFileSystemStores("amb-state"))
    .ConfigureDevicesFor<DiNodeManager>(async context =>
    {
        IDeviceBuilder<DeviceState> device = await context.CreateDeviceAsync(
            new QualifiedName("Sensor_1", context.Manager.InstanceNamespaceIndex));
        device.WithIdentification(i => i.ProductInstanceUri = "urn:acme:sensor:4711");

        await device.RegisterAsAssetAsync(
            context.GetRequiredService<IAssetManagement>(),
            asset => asset.WithConfigurableAssetId(),
            context.CancellationToken);
    });
```

Registering an asset checks its identification (a non-empty
`ProductInstanceUri`, on the asset or in its `Identification` group).
`WithConfigurableAssetId` makes `AssetId` writable for clients and persists
what they write through the `IAssetConfigurationStore`
(`MemoryAssetConfigurationStore` or `FileSystemAssetConfigurationStore`).

Every asset is listed in the alias categories `AssetsByProductInstanceUri` and
- when it has a `2:AssetId` - `AssetsByAssetId` below `0:Aliases/Assets`, so
clients discover it with `FindAlias` (OPC 10000-17); an asset whose `AssetId`
is empty is listed as `NoAssetIdAssigned`, and writing the `AssetId` moves it.
Assets that share a name share its alias object. The categories keep
their `NodeVersion` current and announce changes with a
`GeneralModelChangeEvent`.

`WithDeviceHealth` gives an asset the OPC 10000-100 `DeviceHealth`, set by the
application or derived from the active alarms; an asset with health alarms gets
it in any case, following the alarms. `WithHealthAlarm` adds an
OPC 10000-100 health alarm with the asset as its source, an AMB condition class
and the `PotentialRootCauses` of `IRootCauseIndicationType`; by default the
alarm is an instance of a server-specific subtype of the Device Integration
alarm type that implements the interface. The application drives it through
`IAssetHandle.Health` (`RaiseAsync` with a severity of Table 15, `ClearAsync`).

`WithMaintenance` adds a current or future maintenance activity: a condition
that implements `IMaintenanceEventType`, carries a maintenance condition class
and the optional details (planned date, estimated downtime, supplier, parts,
method), and moves through the `MaintenanceEventStateMachineType`
(`StartAsync`, `FinishAsync`, `ReplanAsync` for recurring activities). Every
transition and update is reported as an event. Health alarms and maintenance
activities are created while the node manager that owns the asset builds its
address space, so register such assets from its setup (`ConfigureDevicesFor`,
an `IMachineryConfigurator`, `ConfigurePumps`, `ConfigureScales`).

`WithDocumentationLinks` adds the `DocumentationLinks` AddIn with links of the
manufacturer, links users can change, and - with `AllowUserLinks` - the
`AddLink` and `RemoveLink` methods, which need a persistent store (§10.5.3).
What users change or add is persisted in the `IAssetConfigurationStore` and
comes back after a restart with the same NodeIds; `AmbServerOptions.AuthorizeLinkEdit` decides who may change links
(by default every authenticated user, no anonymous one).

`WithVersionInformation` sets the hardware and software revisions and the
`RevisionCounter` (`IAssetHandle.IncrementRevisionCounterAsync`). `LocatedIn`
puts the asset into a location below `HierarchicalLocations` or
`OperationalLocations`, with the `*Contains` reference in both directions;
`WithLocation` and `WithLocalTime` add the location Properties and
`0:LocalTime`, optionally writable and persisted. `ClassifiedAs`,
`WithRequirements` and `WithCapabilities` add dictionary references and the
two folders, `RelatesTo` sub-assets and relations to other assets. The
conformance units of these sections are evaluated from what the assets
publish, whoever added it.

The manager advertises the AMB conformance units the registered assets meet,
and the *AMB Base Asset Management Server Facet* exactly when its mandatory
unit, *AMB Asset Identification*, is met. Registering or unregistering an asset
publishes them again, so a unit the assets no longer meet is withdrawn.

## Target frameworks

net48, net8.0, net9.0, net10.0
