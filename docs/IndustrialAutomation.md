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
optional members for intensities, control channels, RGBW values, health, and
audio samples.

```csharp
BasicStacklightState state = context.CreateInstanceOfBasicStacklightType(
    parent, new QualifiedName("Stacklight", instanceNamespace));
// Type-level factories leave the parent reference unset; browse needs a hierarchical one.
state.ReferenceTypeId = ReferenceTypeIds.HasComponent;

IaStacklight light = new IaStacklightBuilder(context, state)
    .WithModes(0, 100)
    .AddLight(new QualifiedName("Lamp", instanceNamespace))
    .AddAcoustic(new QualifiedName("Sound", instanceNamespace),
        new QualifiedName("Warning", instanceNamespace),
        configureSignal: signal => signal.AddAudioSample(context))
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
`AcousticSignalType.AudioSample` is optional. Configure it through the
`AddAcoustic` signal callback; the provider returns one `IaStacklightAudioSample`
for each configured audio variable, in signal order. The binding copies the
`ByteString` before publication and preserves its quality and source timestamp.
The byte contents remain application-defined audio data: the stack does not
select a codec or operate a speaker. When the DI `DeviceHealthAlarms` folder is
present, the binding leaves that subtree and its access levels to the host's
alarm implementation.

This observation binding exposes variables as read-only. A generated client can
represent a model-permitted write, but the server rejects writes to this binding
with `Bad_NotWritable`. It does not interpret a UA write as permission to operate
physical equipment.

Refresh holds the stacklight's acquisition gate through asynchronous state-change
dispatch. A sink must not await or synchronously block on a reentrant refresh
through this binding. Caller cancellation after publication does not skip
committed notifications; a notification error reaches the caller without
reverting values, and the gate is released after dispatch ends.
The gate serializes binding refreshes only. Because `State` exposes live
`NodeState` instances, callers must coordinate direct in-process mutations of
those nodes with refresh; the binding cannot serialize external setters.

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

The binding publishes a detached copy of each supported provider value, so a
provider can reuse its buffers after `RefreshAsync` returns without changing the
address space. Arrays, matrices, byte strings, opaque identifiers, nested
variants, data values and extension object type identifiers are copied into new
storage with their type and dimensions unchanged. OPC UA Part 6 §5.1.6 requires
abstract `Number`, `Integer` and `UInteger` structure fields to be encoded as
`Variant` values. Providers therefore supply the concrete built-in value inside
the `Variant`; nonempty arrays and matrices explicitly tagged with one of those
abstract types are rejected with `Bad_NotSupported`.
For scalar byte strings, node identifiers, expanded node identifiers, localized
text, data values and extension objects, a payload that cannot be read as its
declared type is rejected with `Bad_NotSupported`; it is not returned unchanged.
Supported typed null values retain their existing behavior.

Nested variants and data values are copied with a nesting check at each recursive
entry. A `ServerSystemContext` supplies the server's configured
`MessageContext.MaxEncodingNestingLevels`; other system contexts use
`DefaultEncodingLimits.MaxEncodingNestingLevels`. The copy also checks remaining
stack space, as the codecs do. A cyclic graph or an acyclic graph exceeding the
nesting or stack limit rejects the snapshot with `Bad_EncodingLimitsExceeded`
before publication, retaining the previous values and collection metadata.
Shared acyclic branches remain supported and are copied independently.

A decoded
`IEncodeable` extension object body in the ordinary IA path is copied only when it is an exact
`EnumValueType` or `Argument`, using a field-wise copy rather than its codec,
equality implementation or clone. Other decoded structures, including
subclasses and custom `IEncodeable` implementations, are rejected with
`Bad_NotSupported` before calling `Encode`, `Decode`, `IsEqual`, `Clone` or an
encodeable-factory activator. Registering a structure does not make it safe to
copy. Application-defined structures use an encoded `ExtensionObject` value:

```csharp
ExtensionObject encoded = EncodeableObject.Encode(
    context.AsMessageContext(), applicationValue, useXml: false);
DataValue reading = new(new Variant(encoded), StatusCodes.Good, acquiredAt);
```

The server copies the encoded type identifier and body without decoding or
invoking application code. A client with the corresponding model and encodeable
factory can decode the value. `Argument.Value` is runtime-only and is not part
of the wire representation; the ordinary IA path rejects an `Argument` with a
non-null runtime value rather than retaining that state.

Localized text uses the shared `LocalizedText.Copy()` operation. It retains
all translations, the selected locale and template, and the translation key
and fallback without formatting them during copying. Translation dictionaries
and argument arrays are detached. Deferred arguments may be null, strings,
Boolean, Char, integral and floating-point primitives, Decimal, DateTime,
DateTimeOffset, TimeSpan or Guid. Other argument types, including mutable
formatting objects, reject the refresh with `BadNotSupported` without replacing
published state. Copying never invokes an argument's `Clone` or `ToString`.
Providers must not mutate inputs concurrently with a refresh. This applies to
scalar text, array and matrix elements, and text nested in a `DataValue`;
it does not change calibration's fixed configuration contract.
Caller-supplied translation dictionaries must be concrete
`Dictionary<string, string>` instances using the default, ordinal or
ordinal-ignore-case comparer. The copy retains
that comparer and entry order, including the requested locale spelling
returned by a direct case-insensitive lookup. Unknown dictionary
implementations and comparers are rejected with `BadNotSupported` because
their lookup behavior cannot be preserved without retaining provider-owned
state. SDK-decoded
multi-language dictionaries remain supported, including `TranslationInfo`
templates with deferred arguments; copying detaches their argument arrays.
This exception follows internal decoding provenance, not the dictionary's
read-only wrapper type. Encoded multi-language text remains supported without
borrowing caller-owned storage.

An `ExtensionObject` whose body is exactly `EnumValueType` copies its numeric
value, `DisplayName` and `Description` field by field into a new instance. Both
text fields use `LocalizedText.Copy()` before any wire encoding or formatting.
This preserves deferred translations and rejects
unsupported arguments without changing publication. An exact `Argument` also
copies its name, data type, value rank, dimensions, and description field by
field, preserving null dimensions separately from an explicitly empty array.
A non-null runtime-only `Value` is not copied. These field-wise rules
apply to each `ExtensionObject` body reached through the supported `Variant`,
array, matrix and `DataValue` traversal; they do not traverse fields in custom
decoded structures.
Other decoded structures are not accepted by the ordinary IA path; encode them
as `ExtensionObject` values before returning them from the provider. This keeps
application-defined statistic types available without retaining provider-owned
buffers or invoking custom copy code.
Opt-in composition additionally supports the two ECM structures described below.
Calibration feature and operational-condition readings use the same copy, with
one additional calibration-only case: an exact decoded `Range` is copied into a
new instance through its `Low` and `High` fields. Derived ranges reject before
copying; statistics do not gain support for `Range`.

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

Statistics notification dispatch awaits child nodes before their parent, then
any statistic components outside the owner's subtree. Synchronous sinks run
before asynchronous sinks; asynchronous event sinks are awaited in registration
order. The publication gate remains held through dispatch, and reset also holds
the reset gate. A sink must not await or synchronously block on a reentrant
refresh or reset through this binding. A later refresh or reset cannot publish
while an earlier operation is still notifying its nodes, so each setter's live
node value remains from the same snapshot throughout that operation's dispatch.
Caller cancellation after publication does not cancel committed notifications.
A sink error reaches the caller without reverting the published collection or
provider commit; the failing node retains its change masks for a subsequent
notification attempt. Both gates are released when dispatch completes or throws.
These gates serialize operations made through the binding. Callers must
coordinate direct in-process mutations through the exposed `NodeState`
instances with refresh and reset; those setters do not acquire the binding's
gates.

### Publish companion readings with statistics

`WithCombinedPublication` opts into one publication containing the statistic
components, `StartTime`, and an ordered set of companion variables or properties.
Use it when an application tag or instantaneous reading must already reflect the
same provider snapshot when statistics notifications begin.

```csharp
IaStatistics statistics = new IaStatisticsBuilder()
    .AddComponent(totalVariable)
    .WithAggregate()
    .WithCombinedPublication([instantaneousVariable, applicationTagProperty])
    .Build(context, statisticsObject, statisticsProvider);

// IIaStatisticsProvider.ReadAsync returns this complete snapshot.
var snapshot = new IaStatisticsSnapshot
{
    StartTime = collectionStart,
    Values = [totalReading],
    CompanionValues = [instantaneousReading, tagReading]
};
```

Direct construction sets
`IaStatisticsOptions.Composition = new IaStatisticsComposition { Nodes = [...] }`.
The binding copies the configured node array. Companion identities must be
assigned and distinct from the statistics, owner, collection metadata and each
other; companion properties are permitted. The composition does not change
their access levels or make them statistic components.

Both `ReadAsync` and `ResetAsync` return a value for every configured companion
in `CompanionValues`. An application reset that changes only statistics supplies
the retained companion values, quality and timestamps from its serialized
application snapshot; the binding does not acquire them from another provider.
There is no second acquisition or application commit callback inside publication.

The binding validates and detaches all statistics and companion readings before
assigning any live value. Every reading needs explicit quality and a source
timestamp. In this opt-in path, a null value with Uncertain or Bad quality is
accepted without changing its quality; Good/null still rejects with
`Bad_TypeMismatch`. Without composition, statistics retain their existing rule
that null requires Bad quality. The same selected rule applies to refresh and
reset. Unsupported decoded structures and deferred formatting arguments retain
the rejection rules above; composition is not a trusted-copier or pre-owned-value
bypass. Exact decoded ECM `AcPeDataType` and `AcPpDataType` bodies are copied
through generated assignments for their three scalar Float fields. Their runtime
type must match the first-party model type before any member is read; subclasses
and custom encodeables remain rejected. The copy preserves the decoded in-process
representation, including context-free `TryGetStructure`, without calling
`Clone`, `Encode`, `Decode` or an application copy callback.

`Opc.Ua.IA.Server` depends directly on the `Opc.Ua.ECM` model package for these
closed copy paths. ECM model depends on the IA and DI models, so this does not
introduce an IA.Server/ECM.Server dependency cycle or load an ECM server runtime.
Both libraries use the same net48/net8.0/net9.0/net10.0 target selection. The
generated copies use direct constructors and field assignments, with no
reflection or dynamic activation required by NativeAOT.

The ECM model enables `ModelSourceGeneratorEcmFieldCopies`. Generation checks
the published AcPe/AcPp definitions for their exact required scalar Float
fields. A final-compilation analyzer checks the generated types after all
generators have run: added source or peer-generated partial declarations,
extra backing fields, inherited state and nonprimitive properties reject the
model build with `MODELGEN039`. The generated data declaration and its fixed
pooling partial are permitted together. The pooling partial contains only its
integer reuse sentinel and four lifecycle methods; that sentinel is not a
measurement field and starts fresh in a copied instance. Additional pooling
storage is rejected. Each field property must read and assign its
own distinct primitive backing field directly; a custom accessor call also
rejects the build. This is a bounded first-party path for these
two types, not an acceptance rule inferred from a public interface, attribute
or factory registration. Other nested or mutable structure shapes are not
eligible.

After staging succeeds, the binding assigns statistics, `StartTime` and companion
values before dispatching any notification. Statistics and companion setters
compare with their current values, so composition also rejects unsafe existing
statistic or companion payloads before assignment. A cancelled refresh before
assignment, invalid snapshot or provider
failure leaves the entire previous publication unchanged. When a reset provider
returns a successful committed snapshot, subsequent caller cancellation does
not prevent staging, publication or notification. A rejected reset snapshot
still leaves the address-space cache unchanged; the binding cannot undo the
provider's reset.

Notifications await the owner's subtree and any remaining statistics and
companions using `CancellationToken.None`, while the existing binding
serialization remains held. A notification exception propagates after the
complete publication; it does not roll back values or provider side effects.
The serialization is released in `finally`, including after notification failure.
Sinks must not await reentrant operations on the same binding.

This ordering applies to binding-owned operations and notifications. It does
not make multiple node assignments atomic to concurrent raw node reads, and
callers must still coordinate direct in-process node mutations. The binding
does not supply a public or internal lock or lease for external coordination.

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

Construction detaches identification and engineering-unit translation dictionaries
and formatting arguments, including operational-condition units, and copies
opaque reference-instrument identifier bytes. Reusing the provider's metadata
buffers does not change the configured nodes. Unsupported translation state
rejects construction rather than remaining shared with the provider.

Each refresh supplies exactly the configured features and optional fields.
The binding validates native type, rank, ranges, acquisition time, validation
dates, and quality from 0 through 100 before publishing. A failed or cancelled
acquisition or validation retains the previous cache; simultaneous refresh attempts are rejected
to avoid publishing provider snapshots in a different order.
Committed notifications await asynchronous sinks without caller cancellation.
The overlap guard remains active until notification dispatch finishes, so a
calibration sink cannot re-enter refresh. A notification error reaches the caller
without reverting values; the guard is released even when dispatch throws.

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

Calibration feature inspection retains each returned `DataValue`, including Bad
quality and source timestamps, for feature values, engineering units and range
resolution. An absent optional property is `DataValue.Null`; service and
transport failures still propagate to the caller.

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
The client browses each feature once to resolve both metadata properties, then
reads feature values and present metadata in one batch. Managed browsing and the
session's read batching apply server operation limits; returned values retain
their individual status codes and timestamps. An empty feature folder does not
issue a value read.

## Ownership and support evidence

Applications own providers, schedule refreshes, and register their nodes.
Stop new requests and drain outstanding operations before disposing
`IaStacklight` or `IaStatistics`. A calibration binding has no independently
owned background resource. Existing Machinery, MachineTool, and Robotics
integrations continue to consume the same generated IA model.

Support depends on implemented behavior, not on model declarations alone.
IA support is **Full** for the released information-model capabilities described
above. Application-defined statistics use encoded `ExtensionObject` values;
the binding copies their type identifiers and bodies without decoding or
invoking application code. The provider API deliberately rejects arbitrary
decoded custom structures with `Bad_NotSupported`, since it cannot safely copy
their mutable state without application code. NativeAOT, supported-framework
solution results, and companion compatibility evidence remain part of
validation, but pending test execution does not itself reduce the support
rating. The
[published-specification matrix](Profiles.md#published-specification-support-matrix)
records this rating.
