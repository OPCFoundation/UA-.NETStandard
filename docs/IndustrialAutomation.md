# Industrial Automation

The Industrial Automation packages compose the three information-model concepts
in [OPC 10000-200, version 1.01.4](https://reference.opcfoundation.org/IA/v101/):
stacklights, statistical data, and calibration targets. An existing Device
Integration, Machinery, or Robotics node manager owns their address space.
Applications supply observations, calculations, authorization, and storage through
providers; the bindings publish those values through the shared generated model.

| Package | Responsibility |
|---|---|
| `Opc.Ua.IA` | Model identifiers, node states, structures, enums, client proxies, and the `AddOpcUaIA` loader |
| `Opc.Ua.IA.Server` | Model registration, generated server accessors, composition builders, and provider bindings |
| `Opc.Ua.IA.Client` | Generated variable accessors, bounded discovery, category identification, and feature inspection |

Physical light drivers, alarm-to-light policies, analytics quantities, and physical
calibration remain application responsibilities. Loading declarations does not
establish conformance to an IA profile. The bindings do not advertise profiles
from their presence in the address space.

## Host IA on an existing companion

Register IA on the server builder after selecting its DI-owning manager:

```csharp
services.AddSingleton<MyIaApplication>();
services.AddOpcUa().AddServer(options =>
{
    options.ApplicationName = "InspectionCell";
    options.PkiRoot = certificateStoreRoot;
    options.EndpointUrls.Add(endpoint);
})
.AddMachinery()
.AddIndustrialAutomation()
.ConfigureIndustrialAutomationFor<MachineryNodeManager>(setup =>
    setup.GetRequiredService<MyIaApplication>().BuildAsync(setup));
```

`MyIaApplication` is the application's composition service. The callback runs
after model loading and receives the owning manager, services, and cancellation
token through `IDiPostSetupContext`. Construct bindings before registering their
subtrees with that manager. The [IA sample](../samples/IA/README.md) shows the
complete callback and registration sequence below one Machinery-owned DI device.

For direct construction, create initialized generated states with the owning
`ISystemContext`, instantiate the bindings with their providers, and register the
resulting nodes using the existing node manager. `IaModelProvider` can load the
same compiled model outside DI. `Opc.Ua.IA` remains its only producer.

## Stacklight observations

`IaStacklightBuilder` composes ordered light and acoustic elements on an existing
`BasicStacklightState` or `StacklightState`. Callbacks retain access to generated
optional members for intensities, control channels, RGBW values, and health.

```csharp
BasicStacklightState state = context.CreateInstanceOfBasicStacklightType(
    parent, new QualifiedName("Stacklight", instanceNamespace));
// Type-level factories leave the parent reference unset; browse needs a hierarchical one.
state.ReferenceTypeId = ReferenceTypeIds.HasComponent;

IaStacklight light = new IaStacklightBuilder(context, state)
    .WithModes(0, 100)
    .AddLight(new QualifiedName("Lamp", instanceNamespace))
    .AddAcoustic(new QualifiedName("Sound", instanceNamespace),
        new QualifiedName("Warning", instanceNamespace))
    .Build(stacklightProvider);

await light.RefreshAsync(cancellationToken);
```

The provider returns a complete `IaStacklightSnapshot` in element order.
The binding validates its topology, enum values, required timestamps, and
configured engineering ranges before publishing any value. Each value retains
its source quality and acquisition time. Invalid snapshots leave the previous
cache unchanged and return an error to the caller.

Segmented mode exposes individual element observations. Level-meter mode requires
`StackLevel`, its engineering range, a level value, and a display mode; a lamp's
`SignalOn` observation does not determine the level. Running-light mode requires
`StackRunning`. Separate control channels cannot coexist with direct light
controls on one element. Acoustic `OperationMode` selects an actual signal's
`NumberInList`; elements belonging to the mounting base precede other elements.

This observation binding exposes variables as read-only. A generated client can
represent a model-permitted write, but the server rejects writes to this binding
with `Bad_NotWritable`. It does not interpret a UA write as permission to operate
physical equipment.

## Application-owned statistics

The specification defines interfaces and collection behavior, not fixed
statistical quantities. Create the application's variables, then bind their
ordered snapshots to an aggregate or rolling interface:

```csharp
IaStatistics statistics = new IaStatisticsBuilder()
    .AddComponent(sumVariable)
    .WithAggregate("Application-controlled collection restart")
    .Build(context, statisticsObject, statisticsProvider);

await statistics.RefreshAsync(cancellationToken);
```

Use `.WithDurationWindow(milliseconds)` or `.WithCountWindow(count)` for a
rolling collection. Duration and count windows are mutually exclusive.
`IaStatisticsOptions { Rolling = true }` also permits an application-defined
rolling collection without either optional window property. Aggregate collections
do not roll over; their optional `ResetCondition` describes an application reset.

`IIaStatisticsProvider` owns calculation and persistence. It returns coherent
snapshots containing all quantities, their quality and source times, and the
collection `StartTime`. The binding adds `HasInterface` and
`HasStatisticComponent` references and publishes read-only collection metadata.
Use `.AddGroup(group)` alongside `.AddComponent(...)` for indirect quantities
under application-owned grouping objects. Every grouped quantity needs a provider
snapshot slot; a group cannot introduce another statistics owner.

The binding publishes a copy of each provider value, so a provider can reuse its
buffers after `RefreshAsync` returns without changing the address space. Arrays,
matrices, byte strings, opaque identifiers, nested variants and extension object
type identifiers are copied into new storage with their type and dimensions
unchanged. A decoded structure is binary encoded and decoded into a new
instance, because its generated clone shares array, byte string and variant
fields. A structure whose decoded copy differs from the original, for example
because a nested type is not registered with the encodeable factory, fails the
refresh with `BadNotSupported` and is not published. Calibration feature and
operational-condition readings use the same copy.

For vendor-defined grouping ObjectTypes, supply
`.WithGroupTypeResolver(nodeId => manager.FindPredefinedNode<BaseObjectTypeState>(nodeId))`.
The lookup must return the actual type with the requested identity, including
types owned by other managers when the ancestry crosses a manager boundary.
The binding checks the type and its supertypes for statistics interfaces before
changing the owner. Missing or inconsistent type metadata rejects construction.
The `Build(INodeAttachContext, ...)` overload uses the attachment's indexed
manager automatically unless an explicit resolver was supplied. Direct
construction accepts the same lookup through `IaStatisticsOptions.ResolveGroupType`.

`ResetStatistics` first calls `AuthorizeResetAsync` with the requesting system
context. A denial returns the provider's status without changing values or
`StartTime`. A successful `ResetAsync` must commit the entire collection and its
new start time before returning its snapshot. Refresh and reset operations are
serialized per binding. Repeated resets are separate authorized operations.

Cancellation or failure before a provider commit leaves its previous state
intact. Once committed, a provider returns that committed snapshot even if
cancellation arrives subsequently. A provider returning invalid data has violated
its contract: the binding reports the error and retains its previous cache, but
cannot roll back an external store. Restore persisted values and `StartTime`
through the provider after restart; the binding does not create its own store.

## Calibration-target information

`IaCalibrationTargetBuilder` selects a concrete category, DI identification,
typed features, and optional target properties. The resulting configuration is
the provider's fixed topology for the binding's lifetime.

```csharp
var units = new IaCalibrationUnit(
    "http://www.opcfoundation.org/UA/units/un/cefact",
    5066068,
    new LocalizedText("mm"));

IaCalibrationTargetConfiguration configuration =
    new IaCalibrationTargetBuilder(IaCalibrationTargetCategory.ReusableDevice,
        new IaCalibrationIdentification
        {
            Manufacturer = new LocalizedText("Example Manufacturer"),
            Model = new LocalizedText("Reference scale"),
            SerialNumber = "REFERENCE-001"
        })
    .WithFeature(new IaCalibrationFeature(
        new QualifiedName("Capacity", instanceNamespace),
        IaCalibrationFeatureKind.CapacityRange,
        units,
        Resolution: 0.01))
    .WithOptionalFields(IaCalibrationTargetFields.Quality |
        IaCalibrationTargetFields.CertificateUri)
    .Build();

// targetProvider.Configuration supplies this configuration.
CalibrationTargetState state = context.CreateInstanceOfCalibrationTargetType(
    parent, new QualifiedName("ReferenceTarget", instanceNamespace));
state.ReferenceTypeId = ReferenceTypeIds.HasComponent;
var target = new IaCalibrationTarget(context, state, targetProvider);
await target.RefreshAsync(cancellationToken);
```

Dynamic and one-time targets require at least one feature. Reusable and
reusable-device targets additionally require manufacturer, model, serial number,
an `OperationalConditions` object, and `LastValidationDate`. Applications can
publish numeric calibration values or capacity ranges with units and positive
resolution. Numeric values retain their actual scalar, array, or matrix type.
Operational conditions use the same typed metadata. Dynamic categories can
reference the instruments that measured the individual target.

Each refresh supplies exactly the configured features and optional fields.
The binding validates native type, rank, ranges, acquisition time, validation
dates, and quality from 0 through 100 before publishing. A failed or cancelled
refresh retains the previous cache; simultaneous refresh attempts are rejected
to avoid publishing provider snapshots in a different order.

All target variables are read-only. External applications authorize and commit
changes in the provider's authoritative store, then refresh the binding.
`CertificateUri` is published as information and is never fetched. None of these
operations performs, validates, or certifies a physical calibration.

## Typed clients and subscriptions

Register the client on an existing managed connection:

```csharp
services.AddOpcUa().AddClient(options =>
{
    options.ApplicationName = "InspectionClient";
    options.PkiRoot = certificateStoreRoot;
})
.AddDiscoveryAndConnect(options =>
{
    options.DiscoveryUrl = endpoint;
    options.SecurityMode = MessageSecurityMode.SignAndEncrypt;
    options.SecurityPolicyUri = SecurityPolicies.Basic256Sha256;
})
.AddIndustrialAutomationClient();

IndustrialAutomationClient client = await servicesProvider
    .GetRequiredService<IndustrialAutomationClientFactory>()
    .CreateAsync(cancellationToken);
```

The factory borrows the DI container's cached session. Do not close or dispose
`client.Session`; the container owns it. Direct construction accepts an existing
`ISession` and `ITelemetryContext` without opening another connection.

```csharp
await foreach (IaEntry entry in client.DiscoverAsync(
    applicationRoot, cancellationToken: cancellationToken))
{
    if (entry.Concept == IaConcept.Stacklight)
    {
        BasicStacklightTypeClient stack = client.Stacklight(entry.NodeId);
        ClientRead<StacklightOperationMode> read =
            await stack.StacklightModeVariable().ReadAsync(cancellationToken);

        await foreach (ClientRead<StacklightOperationMode> change in
            stack.StacklightModeVariable().ObserveAsync(
                cancellationToken: cancellationToken))
        {
            // change.DataValue retains the source quality and timestamp.
        }
    }
}

await client.AggregateStatistics(statisticsNode).ResetStatisticsAsync(cancellationToken);
IaTargetCategory category =
    await client.ReadCalibrationCategoryAsync(targetNode, cancellationToken);
ArrayOf<Opc.Ua.IA.Client.IaCalibrationFeature> features =
    await client.ReadCalibrationFeaturesAsync(targetNode,
        cancellationToken: cancellationToken);
```

Discovery follows hierarchical references with cycle detection and a node limit.
It resolves type identities through namespace URIs, recognizes vendor subtypes,
and identifies statistics through `HasInterface` on an instance, its type, or
its supertypes. Type traversal also detects cycles and applies the configured
limit independently to each type chain. Generated accessors resolve
optional children by qualified browse name; an absent optional member produces
`Bad_NotFound`. Subscriptions use the existing managed streaming infrastructure.
Disposing an observation removes its monitored item, not the shared session.

Category handling distinguishes reusable devices from their reusable supertype.
Feature inspection returns the original `DataValue`, units, resolution, type
definition, and display name. Custom object features remain visible with their
display name and `DataValue.Null`; the client does not invent a numeric
interpretation for them.

## Ownership and support evidence

Applications own providers, schedule refreshes, and register their nodes.
Stop new requests and drain outstanding operations before disposing
`IaStacklight` or `IaStatistics`. A calibration binding has no independently
owned background resource. Existing Machinery, MachineTool, and Robotics
integrations continue to consume the same generated IA model.

Support remains bounded by executed evidence rather than loaded declarations.
The IA test project separates provider/topology tests from the encrypted
client/server scenario. NativeAOT, supported-framework solution results, and
companion compatibility results must be recorded before changing a support
rating to Full.
