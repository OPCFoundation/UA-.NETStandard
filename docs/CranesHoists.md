# Cranes and Hoists

The CranesHoists family implements the released **OPC 40020-1 1.00** information
model, native server instances, provider-backed monitoring, typed client
workflows and six bounded MCP tools. All four packages remain **preview-only**.
This is an information-model implementation, not a crane controller, safety
system or external OPC certification.

## Packages, targets and model identity

| Package | Responsibility |
| --- | --- |
| `OPCFoundation.NetStandard.Opc.Ua.CranesHoists` | Generated model, factories, proxies and shared contracts. |
| `OPCFoundation.NetStandard.Opc.Ua.CranesHoists.Server` | Native builders, provider binding and runtime lifetime. |
| `OPCFoundation.NetStandard.Opc.Ua.CranesHoists.Client` | Discovery, snapshots, observations and limit requests. |
| `OPCFoundation.NetStandard.Opc.Ua.Mcp.CranesHoists` | MCP adapter over the typed client and existing sessions. |

The first three use the repository's `LibTargetFrameworks`: `net48`, `net8.0`,
`net9.0` and `net10.0`. The MCP package targets `net8.0`, `net9.0` and `net10.0`;
it is centrally restricted in legacy solution builds. The samples use the same
four application targets. Do not infer current .NET Standard targets from the
historical `NetStandard` package prefix.

Both ordinary and `.Debug` identities are subject to the root preview-package
policies. Use a matching preview family from the configured
[preview feed](DeveloperGuide.md), or project references as the samples do.
No new release version, stable publication or independently versioned crane
protocol is implied.

The [pinned provenance][provenance] records UA-Nodeset revision
`96a08945425d0295edb3607d2a38f6841ef44046`:

- Wire model/namespace URI: `http://opcfoundation.org/UA/CranesHoists/`.
- XML model version: `1.00`; model publication date: **2023-09-01**.
- The source's namespace-metadata variable says **2023-05-01**. Both original
  dates are retained; the runtime does not rewrite the published NodeSet.
- The specification's `Robotics/CranesHoists` profile prose is not a replacement
  wire namespace. Namespace indexes are resolved dynamically, never fixed to
  the `ns=4` used in the source XML.
- The native Basic profile URI is
  `http://opcfoundation.org/UA-Profile/CranesHoists/Server/Basic`.
- The 89 canonical declarations include six ObjectTypes, four enum DataTypes
  and `ProtectsAxis` (`i=4001` in the crane namespace). There are no newly
  declared Method nodes. The local NodeIds CSV is a **derived drift guard**,
  not an upstream identifier publication.

The generated runtime types are `CraneMotionDeviceSystemState` (`i=2112`),
`CraneMotionDeviceState` (`i=1392`), `CraneAxisState` (`i=1369`),
`CraneMotorState` (`i=1262`), `BrakeState` (`i=1556`) and
`ProtectiveFunctionState` (`i=1462`). They inherit the existing
[Robotics](Robotics.md) and DI types and reuse
[Machinery](Machinery.md) identification/building blocks. Dependency models are
loaded from their existing assemblies, not copied into a parallel address space.

## Run the actual samples

The [paired walkthrough][samples] includes complete server/client programs,
help, certificate-store options and bounded commands:

```powershell
dotnet build samples\CranesHoists\CranesHoistsServer\CranesHoistsServer.csproj
dotnet build samples\CranesHoists\CranesHoistsClient\CranesHoistsClient.csproj
```

In separate terminals, from the repository root:

```powershell
dotnet run --no-build --framework net10.0 `
  --project samples\CranesHoists\CranesHoistsServer -- `
  --port 62557 --seconds 60 --instances 2 --advanced --demo-trust --allow-limit-requests
```

```powershell
dotnet run --no-build --framework net10.0 `
  --project samples\CranesHoists\CranesHoistsClient -- `
  --endpoint opc.tcp://localhost:62557/CranesHoistsServer `
  --demo-trust --system Crane1 --seconds 3 --count 3 --events `
  --request-limit 40 --prove-refusal
```

The **fresh** sample provider accepts the first request, then refuses the second
because its accepted-request history is capped at one. The client rereads the
requested value and the independently acquired active limit. This demonstrates
acceptance/refusal, not a physical change. A second system has its own provider,
root/nameplate and request history. Without the write flags the client is
read-only. Without `--advanced`, the server publishes mandatory-only defaults;
omit client `--events` if no event capability was selected.

The server supports `--port 0` and prints the actual endpoint after startup.
`--seconds` bounds its runtime; the client has both observation count/window
bounds and a 90-second work deadline. The application never waits for console
input. Ctrl+C triggers awaited cleanup.

**Trust is required by default.** `--demo-trust` only permits unknown peer
certificates for local testing; encryption, SHA-2 policy and domain validation
remain in use. The client refuses this flag for non-loopback URLs. A server's
localhost endpoint is not a firewall: do not expose a demo-trust/write-enabled
process. Use the existing [Certificate Manager](CertificateManager.md),
[certificate guidance](Certificates.md) and
[identity providers](IdentityProviders.md) for real deployments. The sample
authorizer is explicitly not a production access-control policy.

## Progressive construction

### One standalone owner

The sample registers its clock, authorizer and application service with DI, then
uses this existing hosting chain:

```csharp
builder.Services.AddOpcUa()
    .AddServer(server =>
    {
        server.ApplicationName = "CranesHoistsServer";
        server.ApplicationUri = "urn:localhost:OPCFoundation:CranesHoistsServer";
        server.PkiRoot = pkiRoot;
        server.EndpointUrls.Add(endpoint);
        // Unknown certificates remain rejected unless explicitly configured otherwise.
    })
    .AddCranesHoists()
    .ConfigureCranesHoists((context, ct) =>
        context.Machinery.GetRequiredService<CraneDemo>().ConfigureAsync(context, ct));
```

`CraneDemo` is the concrete service in [the sample][demo], not a library type.
`AddCranesHoists` selects one Machinery owner with `BuildingBlocks`, then adds
the required crane/Robotics model closure. Configuration is awaited before the OPC UA server's post-start callback.
Generic Host `StartAsync` can return earlier; the sample awaits the existing
`IServerStartupTask` readiness seam before printing `READY` or starting acquisition.
Loading models alone is not a runtime-capability claim.

### Mandatory-only instance

Inside a configuration callback, with a supplied `ICraneDataProvider provider`,
this is the minimal native shape. Keep the returned handle in the application
service, refresh it from the acquisition loop, and dispose it with that service.
The complete ownership implementation is in the sample.

```csharp
CraneSystemBuilder system = context.AddCraneSystem("Crane1")
    .WithProvider(provider, ownsProvider: false)
    .WithIdentification(data =>
    {
        data.Manufacturer = new LocalizedText("Example manufacturer");
        data.SerialNumber = "Crane1";
        data.ProductInstanceUri = "urn:example:crane:1";
        data.Model = new LocalizedText("Crane model");
        data.ProductCode = "Crane model";
    })
    .AddController("Controller", controller =>
    {
        controller.WithIdentification(data =>
        {
            data.Manufacturer = new LocalizedText("Example manufacturer");
            data.Model = new LocalizedText("Controller model");
            data.ProductCode = "Controller model";
            data.SerialNumber = "Controller1";
        });
        controller.WithCurrentUser(user => user.WithLevel("Observer").WithName("Operator"));
        controller.AddSoftware("Runtime", software => software.WithIdentification(data =>
        {
            data.Manufacturer = new LocalizedText("Example manufacturer");
            data.Model = new LocalizedText("Runtime");
            data.SoftwareRevision = "1.0";
        }));
        controller.AddTaskControl("Main");
    })
    .AddSafetyState("Safety")
    .AddDevice("Hoist", CraneMotionDeviceCategoryEnum.HOIST, device =>
    {
        device.WithIdentification(data =>
        {
            data.Manufacturer = new LocalizedText("Example manufacturer");
            data.SerialNumber = "Hoist1";
            data.ProductInstanceUri = "urn:example:hoist:1";
            data.Model = new LocalizedText("Hoist model");
            data.ProductCode = "Hoist model";
        });
        device.AddAxis("Lift", axis => axis.AddMotor("LiftMotor"));
    });

CraneSystemHandle handle = await system.BuildAsync(ct).ConfigureAwait(false);
```

There must be at least one device, with the inherited controllers, software,
task controls, safety state, axes, power trains and motors satisfied. The
runtime validates the selected graph and provider acquisition before
publication. A missing selected measurement is a configuration error, not
permission to publish a generated default as Good.

Default position capability is unavailable: the mandatory position value has
`BadNoData`. Optional load, speed-limit properties, brakes, protective functions,
external-control state, Components, functional groups and event publication
require explicit builder calls. Conformance contribution is based on committed,
selected instances; a minimal instance does not advertise every optional CU
merely because its namespace is loaded.

### Add selected capabilities before BuildAsync

Within the existing axis callback, choose the data the equipment can actually
provide:

```csharp
axis.WithPositionCapability(true, AxisMotionProfileEnumeration.LINEAR)
    .WithAxisFunctions()
    .WithMovingState()
    .WithSpeedLimits()
    .WithActualLoad()
    .AddBackupBrake("BackupBrake");
axis.AddMotor("LiftMotor", motor => motor.WithOverheated().WithServiceBrake());
```

On the system builder, explicitly supply both the limit provider and authorizer:

```csharp
system.WithExternalControl()
    .WithGlobalSpeedLimit()
    .WithComponents()
    .WithFunctionalGroups()
    .WithEvents()
    .WithLimitRequests(limitProvider, writeAuthorizer)
    .AddProtectiveFunction("Overload", ["Lift"], protection => protection
        .WithStateMonitoring()
        .WithDescription(ProtectiveFunctionEnumeration.FORCE_LIMITER, "Overload monitor", true)
        .WithDirections(true, true, false, true));
```

These fragments replace/extend the minimal callbacks **before the single
BuildAsync**, rather than adding the same motor twice. Other explicit choices
include device `WithWorkingPeriod`, axis `WithRatedSpeed(value, units)` and
native Robotics load inertia/center-of-mass children through `WithActualLoad`.
The structured geometry and scalar coordinate projections retain the original
quality and acquisition time.
The released dual-parent DWP/nameplate declaration aliases are materialized as
local instance children, not exposed as prototype values or ambiguous paths.

`WithPhysicalComponent(otherDeviceId)` adds the real `HasPhysicalComponent`
relationship; it does not reparent or duplicate the flat motion-device inventory.
`AddProtectiveFunction(..., protectedAxes)` creates native forward `ProtectsAxis`
references from protection to actual local axes. Invalid, duplicated or unknown
targets fail setup. Removal releases inverse references and discovery bindings.
Enabled/active protection telemetry must be coherent: disabled protection cannot
report an active stop. Direction capability metadata is not an active safety
command and does not change as the monitored state changes.

### Existing Machinery or Robotics owner

Use **one** of these alternatives, never call a second standalone owner to load
the same DI/Robotics namespaces. `ConfigureCraneAsync` below denotes the same
application callback used for standalone construction.

```csharp
serverBuilder
    .AddMachinery(options => options.Parts = MachineryParts.BuildingBlocks)
    .AddCranesHoistsToMachinery()
    .ConfigureCranesHoistsFor<MachineryNodeManager>(ConfigureCraneAsync);
```

```csharp
serverBuilder
    .AddRobotics()
    .AddCranesHoistsToRobotics()
    .ConfigureCranesHoistsFor<RoboticsNodeManager>(ConfigureCraneAsync);
```

`ConfigureCranesHoistsFor<TManager>` supports an existing compatible DI-derived
owner. The direct fallback is
`new CranesHoistsBuildContext(roboticsContext, machineryContext, timeProvider)`;
both contexts must refer to the **same manager and application namespace**, and
Machinery BuildingBlocks must be selected. The same builder/runtime is used in
all cases, including both DeviceSet and Machines discovery.

The shared [Machinery](Machinery.md) / [Robotics](Robotics.md) registration
transaction prepares, binds and commits the root once. Failure/cancellation
releases reservations, owned resources and references without deleting another
instance's conflicting tree. Runtime state belongs to each handle/provider,
not a static family registry. Multiple independent instances may reuse local
component names but must have distinct system identities. Distributed equipment
state and coordination belong in the injected provider, not in the sample's
in-process simulator.

## Provider, authorization and lifetime contract

The public contracts live in [`CraneProviders.cs`][providers]:

- `ICraneDataProvider.ReadAsync(ct)` returns a complete, bounded
  `ArrayOf<CraneSample>` for one system. Each sample has a system-local
  `CraneSignalAddress` and a `DataValue` preserving value, quality and source time.
  `handle.TelemetrySignals` lists the selected acquisition addresses.
- A provider explicitly reports unavailable values with bad/null samples.
  Unknown/duplicate addresses, missing selected signals, request-field injection
  or invalid wire values reject the acquisition. Validation precedes publication,
  so another signal is not partially updated on failure.
- `ICraneWriteAuthorizer.AuthorizeAsync(caller, request, ct)` uses the **actual
  caller context**. Authentication and permission to request configuration are
  separate from the equipment/provider's decision to accept it.
- `ICraneLimitProvider.RequestAsync(request, ct)` returns the actual acceptance
  status. No provider means no configured write capability. A refusal preserves
  the prior requested and reported state.

`CraneSignal` is an SDK selector over existing variables, not an additional
wire enum or command protocol. The five writable selectors are:

| Selector | Target / native value |
| --- | --- |
| `SpeedLimitDirMinusSetpoint` | Axis / finite Double percentage in `[0, 100]`. |
| `SpeedLimitDirPlusSetpoint` | Axis / finite Double percentage in `[0, 100]`. |
| `SpeedLimitEnabledDirMinus` | Axis / Boolean request. |
| `SpeedLimitEnabledDirPlus` | Axis / Boolean request. |
| `SpeedLimitGlobalEnabled` | System / Boolean request. |

`ConfigureLimitAsync` writes those existing request properties; it does not call
an invented method. Active directional limits, reported external-control state
and protection state remain read-only provider observations. **Good means
request accepted**, not hardware changed. A write's current-user access flag
does not replace the authorizer/provider checks. NaN, infinities, wrong native
types and out-of-range values are errors.

`SimulatedCraneProvider` is a bounded reference adapter, not a hardware adapter:
it returns only supplied samples and separately retains accepted requests.
Its default policy refuses requests. Its configurable sample/request bounds
cannot exceed 1024; the sample selects 32 samples and one accepted request.
The [acquisition source][acquisition] deliberately supplies
`UncertainDataSubNormal` motor temperature and source timestamps from an injected
`TimeProvider`. It never translates accepted requests into active values.

Provider callbacks run outside the publication lock. `RefreshAsync` stages a
complete report. `DisposeAsync` cancels/drains runtime callbacks;
`RemoveAsync` additionally removes the tree through its owner.
`WithProvider(..., ownsProvider: false)` borrows the data provider; the application
must dispose it after the handle. With `ownsProvider: true`, the runtime disposes
the data provider after draining. Separate limit providers and authorizers
remain application-owned. Do not dispose a shared provider under a live handle.

The sample stops and awaits its loop before removal and disposes its providers
even when startup/acquisition fails. The host is stopped and asynchronously
disposed. No object-based synchronization root, reflection-based model loading
or sync-over-async wrapper is required.

## Typed client

Register `AddCranesHoistsClient` on the existing OPC UA client builder:

```csharp
services.AddOpcUa()
    .AddClient(options =>
    {
        options.ApplicationName = "CraneClient";
        options.PkiRoot = clientPkiRoot;
        options.AutoAcceptUntrustedCertificates = false;
        options.Session = new ManagedSessionOptions { CheckDomain = true };
    })
    .AddDiscoveryAndConnect(options =>
    {
        options.DiscoveryUrl = endpoint;
        options.SecurityMode = MessageSecurityMode.SignAndEncrypt;
        options.SecurityPolicyUri = SecurityPolicies.Basic256Sha256;
    })
    .AddCranesHoistsClient();

CranesHoistsClient client = await serviceProvider
    .GetRequiredService<CranesHoistsClientFactory>().CreateAsync(ct).ConfigureAwait(false);
```

The sample supplies application identity and ownership details. The direct
fallback for an already managed session is
`session.CranesHoists(telemetry)` or
`new CranesHoistsClient(session, telemetry, options, timeProvider)`.
The typed client **borrows** the session; the application/DI owner closes it.
The client package does not reference the server package.

`DiscoverAsync()` starts at DeviceSet. Its root overload also accepts the actual
Machines folder or application organizer. Discovery follows vendor subtypes and
continuation points, retains actual type identities, enforces bounds and releases
owned continuation points. Namespace presence alone is not selected capability.

`ReadSystemAsync` / `ReadElementAsync` return native node identities, current-user
write flags, nameplates, nine axis-function metadata entries when selected,
original DataValues, loads and real directed relationships. A null NodeId means
optional absence; a present bad or uncertain value is **not absence**.
Use `NodeId.IsNull`, `DataValue.IsNull` and `Variant.TryGetValue`, not nullable
wrappers for UA value structs or boxed union access.
`client.System(id)` and `client.Axis(id)` expose the generated proxies for
advanced namespace-correct access; the sample resolves the actual axis
ParameterSet through that proxy.

```csharp
CraneObservation observed = await client.ObserveValuesAsync(
    selectedNodeIds,
    new CraneObservationOptions
    {
        Window = TimeSpan.FromSeconds(3),
        MaximumNotifications = 10,
        MaximumPayloadBytes = 128 * 1024
    },
    ct).ConfigureAwait(false);
```

The typed client permits at most 64 distinct data nodes, 60 seconds, 100 retained
notifications and 1 MiB decoded payload. Default inventory bounds are 256 objects,
32 references per page and 64 browse pages. Overflow is an explicit error rather
than an apparently complete truncated snapshot.

`ObserveEventsAsync(notifier, options, ct)` retains event IDs/types and the
namespace-qualified selected field paths. The sample uses the Server notifier
and standard `SystemEventType` acquisition diagnostics from selected Machinery
Notifications add-ins. Events from all sources can appear there; the event's
SourceNode/SourceName identify the actual crane. No crane-specific motion event
or acknowledgement method is invented.

Each observation owns only its newly created subscription, has setup and
collection bounds, reports count-limit completion and dropped notifications,
and disposes on success, error or delivered cancellation. Empty windows are
reported as empty. Unrelated subscriptions and the borrowed session survive.
The sample prints the before/after subscription count before closing its own
managed session.

## MCP adapter and cancellation delivery

Use the CranesHoists profile of the existing [MCP server](McpServer.md), or embed
the adapter with `AddOpcUaMcpCranesHoists()` and
`WithOpcUaCranesHoistsTools(...)`. The latter has both single-profile and composed
`McpToolProfileSet` overloads. Shared connection tools are deduplicated; the
adapter borrows the selected existing session, not another application host.

| Tool | Actual operation |
| --- | --- |
| `craneshoists_list_systems` | Bounded subtype-aware inventory from DeviceSet or an explicit root. |
| `craneshoists_read_system` | Selected native system, nameplates, topology and values. |
| `craneshoists_read_element` | One actual element with optional presence, access and original DataValues. |
| `craneshoists_observe_values` | Bounded native-array node selection and owned data subscription. |
| `craneshoists_observe_events` | Bounded event window, IDs/types and qualified field order. |
| `craneshoists_configure_limit` | Explicit consent plus server authorization and provider acceptance. |

Specify `sessionName` when more than one session exists. Inventory is capped at
100 systems; observations accept 1–64 distinct NodeIds, 1–60 seconds and 1–100
notifications. Response budgets are bounded to 1 MiB. NodeId arguments are
limited to 1024 characters; use the real IDs from inventory/snapshots.
The [tool schemas and adapter][mcp-tools] specify each tool's narrower defaults.

Configuration requires `confirmConfiguration=true`, one of the five exact
selectors above, and its native Double/Boolean value. It is marked non-read-only,
destructive and non-idempotent. Responses distinguish `requestAccepted` from
`activeValueVerified=false`; they do not claim hardware verification.
Native readings use the stack's UA JSON DataValue encoding, not an alternative
measurement codec. Service errors/refusals retain their UA status.

**SDK 2.2 local-caller cancellation caveat:** the adapter's integration report
observed that canceling the local MCP client's `CallToolAsync` token could stop
that caller's wait without delivering cancellation to the server request.
This is distinct from the adapter receiving cancellation. A delivered standard
MCP `CancelledNotificationParams` notification cancels the in-flight operation
and releases its owned subscription immediately; direct adapter cancellation
also cleans up and preserves the caller token. When only the local wait stops,
the server-side bounded observation window still applies. No SDK workaround or
claim that local-token delivery was fixed is made here.

For canceled/disconnected **writes**, the outcome can be unknown. Reread both
the requested property and provider-reported active state; do not blindly retry
or infer a physical effect. See the retained protocol-cancellation and
direct-adapter-cancellation regressions in [MCP integration tests][mcp-tests].

## Eleven conformance units and concrete evidence

Names below preserve the singular/plural spelling of
[OPC 40020-1 §10.1](https://reference.opcfoundation.org/specs/OPC-40020-1/10).
Model declarations are generated from the canonical XML. Runtime support is
implemented by the named files and exercised by actual behavior tests, not
merely by finding a node in the model.

1. **Cranes Base Info CraneMotionDeviceSystemType** — mandatory root and at least
   one device. Implementation: [CraneSystemBuilder][system-builder],
   [CraneGraph][graph], [hosting][hosting].
   Positive: [CraneRuntimeTests][runtime-tests].
   `MandatoryTopologyPreservesNativeTypesAndUnavailablePosition`;
   [CraneEndToEndTests][tcp-tests].`AllCraneUnitsHaveRealSessionBehaviorWithReorderedNamespaces`.
   Negative: [CraneLifetimeAndTopologyTests][topology-tests].
   `MissingMandatoryObligationFailsAndReleasesReservations`.
2. **Cranes Base Info CraneMotionDeviceType** — native devices, category and
   physical composition. Implementation: [CraneDeviceBuilder][device-builder],
   [CraneGraph][graph].
   Positive: [CraneEndToEndTests][tcp-tests].`PhysicalCompositionIsReportedWithoutDuplicateDevicesOverTcp`.
   Negative: [CraneLifetimeAndTopologyTests][topology-tests].`InvalidPhysicalCompositionFailsSetup`.
3. **Cranes Base Info CraneAxisType** — native axis, unavailable/available position,
   selected load/speed/functions and all nine EnumValues.
   Implementation: [CraneAxisBuilder][axis-builder], [CraneGraph][graph], [client][client].
   Positive: [CraneRuntimeTests][runtime-tests].`SelectedFacetsImplementAllElevenUnitsWithActualValuesAndReferences`;
   [CraneEndToEndTests][tcp-tests].`OptionalLoadGeometryUsesTheNativeRoboticsStructures`.
   Negative: [CraneEndToEndTests][tcp-tests].`InvalidLimitsReturnExplicitWireErrors`.
4. **Cranes Base Info BrakeType** — actual service/backup brake monitoring.
   Implementation: [CraneAxisBuilder][axis-builder], [CraneMotorBuilder][motor-builder], [CraneGraph][graph].
   Positive: [CraneEndToEndTests][tcp-tests].`AllCraneUnitsHaveRealSessionBehaviorWithReorderedNamespaces`.
   Negative: [CraneLifetimeAndTopologyTests][topology-tests].`InvalidReportCannotPartiallyUpdateSystemState`.
5. **Cranes Base Info CraneMotorType** — native power-train motor and acquired
   temperature, with explicit optional overheat/brake monitoring.
   Implementation: [CraneMotorBuilder][motor-builder], [CraneSystemHandle][handle].
   Positive: [CraneRuntimeTests][runtime-tests].`ReportedQualityAndAcquisitionTimeSurviveRefresh`.
   Negative: [CraneContractTests][contract-tests].`NumericMeasurementsRetainTheirExactWireTypes`.
6. **Cranes Base Info ProtectiveFunctionType** — protection state and independent
   direction/stop/slowdown capabilities.
   Implementation: [CraneProtectionBuilder][protection-builder], [CraneSystemHandle][handle].
   Positive: [CraneLifetimeAndTopologyTests][topology-tests].
   `DirectionCapabilitiesAreIndependentFromActiveProtectionState`.
   Negative: [CraneRuntimeTests][runtime-tests].`DisabledProtectionCannotBecomeActiveOrPartiallyPublishAnotherSignal`.
7. **Cranes Base Info CraneOperationalModeEnum** — native operational-mode values.
   Implementation: [canonical XML][model-xml], [CraneSystemHandle][handle].
   Positive: [CraneEndToEndTests][tcp-tests].`EveryModeAndControlEnumerationValueRoundtripsOverTcp`.
   Negative: [CraneContractTests][contract-tests].`EnumerationValidationCoversEveryPublishedValue`.
8. **Crane Base Info ExternalControlRequestEnum** — optional **reported** external
   control state, never inferred from acceptance.
   Implementation: [CraneGraph][graph], [CraneSystemHandle][handle].
   Positive: [CraneEndToEndTests][tcp-tests].`EveryModeAndControlEnumerationValueRoundtripsOverTcp`.
   Negative: [CraneEndToEndTests][tcp-tests].`MinimalInstancePreservesOptionalAbsenceOverTcp`.
9. **Crane Base Info CraneMotionDeviceCategoryEnum** — native category metadata.
   Implementation: [CraneDeviceBuilder][device-builder], [canonical XML][model-xml].
   Positive: [CraneEndToEndTests][tcp-tests].`CategoryAndProtectionEnumValuesRoundtripOverTcp`.
   Negative: [CraneLifetimeAndTopologyTests][topology-tests].`BuilderRejectsUnknownEnumsDuplicateNamesAndInvalidBounds`.
10. **Crane Base Info ProtectiveFunctionEnum** — optional native protection-mode metadata.
    Implementation: [CraneProtectionBuilder][protection-builder], [canonical XML][model-xml].
    Positive: [CraneEndToEndTests][tcp-tests].`CategoryAndProtectionEnumValuesRoundtripOverTcp`.
    Negative: [CraneRuntimeTests][runtime-tests].`DisabledProtectionCannotBecomeActiveOrPartiallyPublishAnotherSignal`.
11. **Crane Base Info ProtectsAxis** — the original directed reference and its lifetime.
    Implementation: [CraneGraph][graph], [CraneSystemHandle][handle], [client][client].
    Positive: [CranesHoistsModelTests][model-tests].`ProtectsAxisRetainsItsReferenceSemantics`;
    [CraneEndToEndTests][tcp-tests].`AllCraneUnitsHaveRealSessionBehaviorWithReorderedNamespaces`.
    Negative: [CraneLifetimeAndTopologyTests][topology-tests].`InvalidProtectionReferencesFailSetup`;
    [CraneBindingTransactionTests][binding-tests].`FailedBindingRollsBackReferencesReservationsAndFacets`.

Cross-cutting tests include
[CraneRuntimeTests][runtime-tests].`AuthorizationAndProviderAcceptanceAreBothRequired`,
[CraneEndToEndTests][tcp-tests].`ProviderRefusalRetainsRequestAndReportedStateOverTcp`,
[CraneClientTests][client-tests].`DiscoveryFollowsContinuationsAndVendorSubtypes`,
[CraneObservationTests][observation-tests].`CleanupFailureIsNotReportedAsSuccessfulObservation`,
and [CraneHostingTests][hosting-tests].`HostingUsesOneOwnerAndTheSameRuntime`.
The [MCP integration fixture][mcp-tests] sends real MCP requests through the typed
client over `opc.tcp`, including denied/refused writes, original quality/time,
native geometry, ownership, bounds and delivered cancellation.

These are implementation/test evidence, not external certification. The retained
family acceptance reports cover the unchanged 45 model tests plus runtime/client
behavior (211 cases on each of net10 and net48) and 118 MCP cases on net10,
including 36 actual MCP-to-TCP cases. These bounded family results do **not** mean
that every solution, other family, native binary or package publication passed.

## Validation scope and NativeAOT

The existing [family tests][test-project] exercise standalone and both shared
owners, multiple systems, rollback and shutdown. Targeted commands use the
repository's documented runner, for example:

```powershell
dotnet test tests\Opc.Ua.CranesHoists.Tests\Opc.Ua.CranesHoists.Tests.csproj -p:CustomTestTarget=net10.0
dotnet test tests\Opc.Ua.CranesHoists.Tests\Opc.Ua.CranesHoists.Tests.csproj -p:CustomTestTarget=net48
```

The sample walkthrough additionally exercises real application binaries, not
just compilation. It prints completed subscription/session/provider/host
cleanup and explicit request/refusal readback evidence. Certificates remain
in the selected PKI roots for trust administration; remove only the owned
demo roots after processes stop. No helper is intended to outlive a bounded run.

Generated model factories/proxies and closed JSON contexts avoid adding
reflection-based model activation to these examples. The net10 sample projects
select `PublishAot` locally. Both samples have been published for `win-x64` and
executed as native binaries with no CLR header. The native client discovered
two systems, read native values, received two data and two event notifications,
verified an accepted limit request and a subsequent provider refusal, and
closed its session and subscriptions. Both processes exited normally and the
listener was removed.

```powershell
dotnet publish samples\CranesHoists\CranesHoistsServer\CranesHoistsServer.csproj `
    -c Release -f net10.0 -r win-x64
dotnet publish samples\CranesHoists\CranesHoistsClient\CranesHoistsClient.csproj `
    -c Release -f net10.0 -r win-x64
```

Do not force `PublishAot` globally onto source-generator project references:
the sample's conditional property already selects it for the executable.
On Windows, the installed Visual Studio C++ tools and `vswhere.exe` must be
discoverable. This sample evidence does not replace the full repository,
MCP-native or package matrix. A managed run is not native evidence, and net48
is not a NativeAOT target. No release or external certification is claimed.

[samples]: ../samples/CranesHoists/README.md
[demo]: ../samples/CranesHoists/CranesHoistsServer/CraneDemo.cs
[acquisition]: ../samples/CranesHoists/CranesHoistsServer/CraneAcquisition.cs
[provenance]: ../src/Opc.Ua.CranesHoists/Model/provenance.json
[model-xml]: ../src/Opc.Ua.CranesHoists/Model/Opc.Ua.CranesHoists.NodeSet2.xml
[providers]: ../src/Opc.Ua.CranesHoists/Contracts/CraneProviders.cs
[system-builder]: ../src/Opc.Ua.CranesHoists.Server/CraneSystemBuilder.cs
[device-builder]: ../src/Opc.Ua.CranesHoists.Server/CraneDeviceBuilder.cs
[axis-builder]: ../src/Opc.Ua.CranesHoists.Server/CraneAxisBuilder.cs
[motor-builder]: ../src/Opc.Ua.CranesHoists.Server/CraneMotorBuilder.cs
[protection-builder]: ../src/Opc.Ua.CranesHoists.Server/CraneProtectionBuilder.cs
[graph]: ../src/Opc.Ua.CranesHoists.Server/CraneGraph.cs
[handle]: ../src/Opc.Ua.CranesHoists.Server/CraneSystemHandle.cs
[hosting]: ../src/Opc.Ua.CranesHoists.Server/Hosting/OpcUaCranesHoistsServerExtensions.cs
[client]: ../src/Opc.Ua.CranesHoists.Client/CranesHoistsClient.cs
[mcp-tools]: ../tools/Opc.Ua.Mcp.CranesHoists/NugetREADME.md
[mcp-tests]: ../tests/Opc.Ua.Tools.Tests/Mcp/CranesHoistsMcpIntegrationTests.cs
[test-project]: ../tests/Opc.Ua.CranesHoists.Tests/Opc.Ua.CranesHoists.Tests.csproj
[runtime-tests]: ../tests/Opc.Ua.CranesHoists.Tests/CraneRuntimeTests.cs
[tcp-tests]: ../tests/Opc.Ua.CranesHoists.Tests/CraneEndToEndTests.cs
[topology-tests]: ../tests/Opc.Ua.CranesHoists.Tests/CraneLifetimeAndTopologyTests.cs
[model-tests]: ../tests/Opc.Ua.CranesHoists.Tests/CranesHoistsModelTests.cs
[contract-tests]: ../tests/Opc.Ua.CranesHoists.Tests/CraneContractTests.cs
[binding-tests]: ../tests/Opc.Ua.CranesHoists.Tests/CraneBindingTransactionTests.cs
[client-tests]: ../tests/Opc.Ua.CranesHoists.Tests/CraneClientTests.cs
[observation-tests]: ../tests/Opc.Ua.CranesHoists.Tests/CraneObservationTests.cs
[hosting-tests]: ../tests/Opc.Ua.CranesHoists.Tests/CraneHostingTests.cs
