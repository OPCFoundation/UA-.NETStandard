# WoT Connectivity protocol bindings

The WoT Connectivity 1.1 runtime materializes Thing Descriptions and Thing Models into the OPC UA AddressSpace. Each interaction-affordance **form** in a document describes how to reach a value over a concrete protocol (HTTP, MQTT, Modbus, OPC UA, …). The **protocol binder** subsystem turns those forms into validated, immutable **binding plans** and, when an executor is present, drives the live transport operations.

The subsystem is deliberately layered so the model remains transport-neutral while the base Bindings package can bundle the dependency-compatible executors on modern .NET.

This document starts with the bindings that ship today and how to register them, then describes the contributor workflow for adding your own binding.

## Table of contents

- [Bindings that ship today](#bindings-that-ship-today)
  - [Package and assembly layout](#package-and-assembly-layout)
  - [Stable public interfaces](#stable-public-interfaces)
  - [Polling, retry and backoff](#polling-retry-and-backoff)
  - [Runtime integration](#runtime-integration)
  - [OPC UA target-mapping binding runtime](#opc-ua-target-mapping-binding-runtime)
  - [Projected Methods, events, and Conditions](#projected-methods-events-and-conditions)
  - [Identity modes, provenance, and bounds](#identity-modes-provenance-and-bounds)
  - [Property observation and contextual values](#property-observation-and-contextual-values)
  - [Action payload contracts](#action-payload-contracts)
  - [Portable browse-path targets](#portable-browse-path-targets)
  - [Registering binders and executors](#registering-binders-and-executors)
  - [Intentionally unsupported operations](#intentionally-unsupported-operations)
  - [Transport security](#transport-security)
  - [Operation coverage (OPC UA executor)](#operation-coverage-opc-ua-executor)
  - [Event field selection (`tm:ref` and `uav:eventSelectClauses`)](#event-field-selection-tmref-and-uaveventselectclauses)
  - [Constraining an `auto` endpoint selection (`uav:minimumSecurity`)](#constraining-an-auto-endpoint-selection-uavminimumsecurity)
- [Adding your own binding](#adding-your-own-binding)
  - [Architecture and lifecycle](#architecture-and-lifecycle)
  - [Identification and capability](#identification-and-capability)
  - [Form extraction and vocabulary terms](#form-extraction-and-vocabulary-terms)
  - [Authoring OPC 10101 target mapping](#authoring-opc-10101-target-mapping)
  - [Planner validation and compiled forms](#planner-validation-and-compiled-forms)
  - [Executors, channels, and disposal](#executors-channels-and-disposal)
  - [Payload codecs](#payload-codecs)
  - [Credentials and trust](#credentials-and-trust)
  - [Endpoint policy and custom schemes](#endpoint-policy-and-custom-schemes)
  - [Registration](#registration)
  - [Monitoring and local sampling](#monitoring-and-local-sampling)
  - [Structured target mapping](#structured-target-mapping)
  - [Status and error mapping](#status-and-error-mapping)
  - [Memory-binding implementation](#memory-binding-implementation)
  - [Memory-binding tests](#memory-binding-tests)
  - [NativeAOT and trimming](#nativeaot-and-trimming)
  - [Packaging and TFM decisions](#packaging-and-tfm-decisions)
  - [Contributor checklist](#contributor-checklist)
  - [Testing matrix](#testing-matrix)
- [Related documentation](#related-documentation)
- [Conformance to OPC 10101 (Binding and Projections)](#conformance-to-opc-10101-binding-and-projections)
  - [OPC 10101-1 (WoT Binding)](#opc-10101-1-wot-binding)
  - [OPC 10101-2 (WoT Projections)](#opc-10101-2-wot-projections)
  - [Conversion entry points](#conversion-entry-points)
  - [Independent readable models](#independent-readable-models)
  - [Identity and preservation](#identity-and-preservation)
  - [DataTypes, ranks, arguments, and model vocabulary](#datatypes-ranks-arguments-and-model-vocabulary)
  - [Engineering units, ranges, and localized text](#engineering-units-ranges-and-localized-text)
  - [What the readable mapping does not yet carry](#what-the-readable-mapping-does-not-yet-carry)
  - [How this is checked](#how-this-is-checked)
  - [Resolving a type binding: the local context](#resolving-a-type-binding-the-local-context)
  - [Resolving a relation: companion ReferenceTypes](#resolving-a-relation-companion-referencetypes)
  - [Alarms and Conditions](#alarms-and-conditions)
  - [Compatibility switch for non-portable identifiers](#compatibility-switch-for-non-portable-identifiers)

## Bindings that ship today

### Package and assembly layout

| Project, assembly, or namespace | Contents | Availability and dependencies |
| --- | --- | --- |
| `src/Opc.Ua.WotCon.Bindings` / `Opc.Ua.WotCon.Bindings` | Stable interfaces, plan model, codecs, the eight planner/validator binders, and registry. No sample binding ships in this library. | Base package `OPCFoundation.NetStandard.Opc.Ua.WotCon.Bindings`; full `net48;net8.0;net9.0;net10.0` matrix. |
| `Opc.Ua.WotCon.Bindings.Http` | HTTP executor and options, included in the base Bindings package | `net8.0`, `net9.0`, and `net10.0`; `HttpClient`. |
| `Opc.Ua.WotCon.Bindings.Modbus` | Modbus TCP client, executor, addressing, and conversion, included in the base Bindings package | `net8.0`, `net9.0`, and `net10.0`; sockets only. |
| `Opc.Ua.WotCon.Bindings.OpcUa` | OPC UA-to-OPC UA executor and options, included in the base Bindings package | `net8.0`, `net9.0`, and `net10.0`; `Opc.Ua.Client`. |
| `src/Opc.Ua.WotCon.Bindings.Mqtt` / `Opc.Ua.WotCon.Bindings.Mqtt` | MQTT executor and options | Separate `OPCFoundation.NetStandard.Opc.Ua.WotCon.Bindings.Mqtt` package for `net8.0`, `net9.0`, and `net10.0`; MQTTnet. |
| `Opc.Ua.WotCon.Server` | Materialization coordinator integration | References `Opc.Ua.WotCon.Bindings` only. |
| `samples/WotCon` | Runnable sample guide plus `AggregationClient`, `AggregationServer`, and `FlatTagServer` projects. `AggregationServer/Bindings/MemoryWotBinding.cs` is a reference custom binding only. | Sample applications; the memory binding is deliberately not registered in the sample host. |

The base Bindings package keeps its full TFM matrix, but its concrete HTTP, Modbus, and OPC UA executor namespaces are compiled only for `net8.0`, `net9.0`, and `net10.0`. MQTT remains separate because it carries an optional external transport dependency. Planner-only use therefore remains available on every base-package TFM. The WoT samples now live under `samples/WotCon`; their project, assembly, and namespace names are `AggregationClient`, `AggregationServer`, and `FlatTagServer` without a `Wot` prefix. The sample guide is [`samples/WotCon/README.md`](../samples/WotCon/README.md).

The plural `Bindings` name is part of every current artifact and namespace. Do not add new references to the retired singular `Opc.Ua.WotCon.Binding*` names.

### Stable public interfaces

All contracts live in the `Opc.Ua.WotCon.Bindings` namespace.

* **Identification, version and capability**
  * `WotBindingIdentity` — a binder's stable `Id` + `Version` (`id@version` key). Multiple versions of a binding coexist.
  * `WotBindingSource` / `WotBindingMaturity` — the version-pinned specification a binder implements (URL, version/date, commit, standards maturity).
  * `WotBindingCapability` — supported operations, content types, executable flag; projects onto the generated `WoTBindingCapabilityDataType`.
  * `IWotBindingIdentification` — deterministic selection. A binder returns a `WotBindingMatch` (kind + priority) so selection uses pinned rules (explicit pin > vocabulary > subprotocol > scheme), **not the URI scheme alone**.
* **Form validation and compilation**
  * `WotFormExtractor` / `WotAffordanceForm` — reflection-free extraction of forms (with resolved `op` defaults, security scheme references and JSON Pointers).
  * `IWotBindingPlanner` — validates a form and compiles it into a `WotBindingCompilation` of immutable `WotCompiledForm` entries carrying `WotEndpointDescriptor` / `WotAddressingDescriptor` / `WotOperationDescriptor` / `WotPayloadDescriptor` / `WotTargetMappingDescriptor` metadata.
* **Target mapping ([OPC 10101 §6.5.4](https://reference.opcfoundation.org/specs/OPC-10101/6.5.4), with the protocol-neutral example in [§8.2](https://reference.opcfoundation.org/specs/OPC-10101/8.2))**
  * `WotTargetMappingDescriptor` — the protocol-neutral `uav:mapToNodeId` / `uav:mapToType` / `uav:mapByFieldPath` terms authored on a **property affordance** (never on a form), letting a non-OPC-UA source (Modbus, HTTP, …) be projected onto an OPC UA target NodeId or a field of a structured target type. `WotAffordanceForm.TargetMapping` parses it from the owning affordance; `WotProtocolBinderRegistry.Prepare` validates it once for every protocol (property-only, `mapByFieldPath` requires `mapToType`, non-empty values, never authored on a form) and attaches it to every `WotCompiledForm` it produces, so individual planners never parse or duplicate it.
* **Payload codec selection**
  * `IWotPayloadCodec` / `IWotCodecRegistry` — reflection-free JSON, text and octet-stream codecs; protocol executors may register more.
* **Credential / trust reference lookup (no secrets in TD / registry nodes)**
  * `WotSecurityDefinition` / `WotCredentialReference` — secret-free scheme references parsed from `securityDefinitions`.
  * `IWotCredentialProvider` — resolves a reference into short-lived `WotCredential` material at runtime, out-of-band. No secret ever appears in a Thing Description or on a registry node.
* **Lifecycle and operations**
  * `IWotBindingExecutor` — `ActivateAsync` opens a per-form `IWotBindingChannel`.
  * `IWotBindingChannel` — `ReadAsync` / `WriteAsync` / `InvokeAsync` / `ObserveAsync` / `SubscribeEventAsync`, returning `WotReadResult` / `WotWriteResult` / `WotInvokeResult` with mapped `StatusCode`s.
* **Registry and structured diagnostics**
  * `IWotBinderRegistry` / `WotProtocolBinderRegistry` — the Prepare / Activate / Deactivate seam the coordinator uses.
  * `WotBindingDiagnostic` — severity + stable code + **RFC 6901 JSON Pointer**.

### Polling, retry and backoff

A transport with no native push channel (HTTP, Modbus) implements `ObserveAsync` with the shared `PollingWotSubscription`, so a poll-only driver does not write its own timer loop. The poll callback reports **health**: it returns `false` when the source failed without throwing, which is how a binding that maps a failure onto a bad `StatusCode` reports it. Both the mapped bad status and a thrown fault are surfaced as a notification, so a variable never silently keeps its last good value while the asset is down.

Consecutive unhealthy polls back off through an `IChannelReconnectPolicy` — the same abstraction the stack already uses for channel reconnects — so an offline device is not hammered once per poll cycle. The default is `ExponentialBackoffChannelReconnectPolicy` (500 ms doubling to 30 s, unlimited attempts); set `RetryPolicy` on `HttpWotBindingOptions` / `ModbusWotBindingOptions` to change it. Backing off never polls *faster* than the configured interval, the first healthy poll resets it, and a policy that reports "stop retrying" ends the loop rather than spinning.

The interval itself comes from the form where the protocol binding defines a standard term for it. Modbus does: **`modv:pollingTime`** (milliseconds, per the W3C Modbus binding — distinct from `modv:timeout`, which is a request timeout) is compiled onto `WotOperationDescriptor.PollInterval` and wins over the executor's configured `ObserveInterval`. HTTP has no standard polling term, so it uses `HttpWotBindingOptions.ObserveInterval`. No vendor-specific `uav:` term is introduced for this.

For Modbus TCP, `ModbusTcpClient` treats a faulted socket as disposable state. The next read or write transaction reconnects before sending the MBAP request, so polling backoff controls retry rate while ordinary operations can recover without recreating the binding channel.



Eight planner/validator binders ship in `Opc.Ua.WotCon.Bindings` (`WotBuiltInBinders.CreateAll()`). Each pins its exact source in `Planners/WotBindingSources.cs`.

| Binding | Id | Pinned source | Maturity | Executable |
| --- | --- | --- | --- | --- |
| HTTP | `w3c.http` | W3C TD 1.1 (normative HTTP mapping) | REC | yes (`Opc.Ua.WotCon.Bindings.Http`, bundled on net8+) |
| CoAP | `w3c.coap` | W3C Binding Templates CoAP | Editor's Draft | planner only |
| MQTT | `w3c.mqtt` | W3C Binding Templates MQTT | Editor's Draft | yes (`Opc.Ua.WotCon.Bindings.Mqtt`, separate package) |
| Modbus TCP | `w3c.modbus` | W3C Binding Templates Modbus | Editor's Draft | yes (`Opc.Ua.WotCon.Bindings.Modbus`, bundled on net8+) |
| BACnet | `w3c.bacnet` | W3C Binding Templates BACnet | Editor's Draft | planner only |
| PROFINET | `w3c.profinet` | WoT PROFINET contribution | Unofficial Draft | planner only |
| LoRaWAN | `w3c.lorawan` | WoT LoRaWAN contribution | Unofficial Draft | planner only |
| OPC UA | `opc.opcua` | OPC 10101 (OPC UA for WoT Binding) | OPC specification | yes (`Opc.Ua.WotCon.Bindings.OpcUa`, bundled on net8+) |

Notes:

* The **W3C Binding Templates registry is a pilot and currently empty**; no binder ever reports `RegistryCurrent`. Drafts expose their Editor's Draft maturity; OPC UA exposes the OPC specification maturity.
* BACnet, PROFINET, LoRaWAN and CoAP perform **schema / document-level planning only** and are reported as **non-executable** — the runtime materializes their nodes but marks the closure degraded so callers know they cannot be driven yet.
* Each planner validates the href scheme and the currently-defined vocabulary terms of its pinned document, checks `op` compatibility, `contentType` and required fields, produces immutable endpoint/addressing/operation/payload metadata and returns precise errors/warnings with JSON Pointers.
* OPC UA forms default to `application/octet-stream`. The planner advertises only media types
  supported by its registered payload codecs; it does not advertise OPC UA Binary until a matching
  codec is available.

### Runtime integration

`WotMaterializationCoordinator` compiles each resource's forms into a `WotBindingPlan` during **Prepare**. Activation/deactivation notifications describe committed generations; the source generation owns its channels and subscriptions until it drains. A deactivation notification alone does not dispose them. See [publication contracts](WotCon.md#ownership-and-publication).

* **Strict mode** (`WotRegistryServerOptions.StrictBindings = true`) fails the closure when any required form is unsupported or invalid.
* **Degraded mode** materializes nodes with `BadConfigurationError` and emits a `WoTBindingFailureEvent`. Validated-but-non-executable forms also degrade the closure so their nodes are visible but flagged.
* Binding capability snapshots populate the registry `SelectedBindings` node and contribute to refresh unchanged-detection.
* The legacy 1.02 `IWotAssetProviderFactory` provider model remains a separate interface.
* The coordinator passes its prepared `WotBindingPlan`s to the host as `WotProjectionDocument.BindingPlans` (an `ArrayOf<WotBindingPlan>`), so the projection host can wire a per-generation OPC UA binding runtime once the closure's NodeSet2 content has been imported.

### OPC UA target-mapping binding runtime

Once a closure's forms are materialized as NodeSet2 content, `LifecycleWotProjectionHost` wires each runtime NodeSet generation's `RuntimeNodeSetOptions.ConfigureAsync` to build a **projection binding runtime** from the document's `BindingPlans`. This implemented generic runtime drives live target-mapped value exchange between the resolved OPC UA variable and the compiled forms of its non-OPC-UA or OPC UA source; it is not limited to a protocol-specific projection.

* `IWotBindingChannelFactory` (implemented by `WotProtocolBinderRegistry` alongside `IWotBinderRegistry`) opens a live `IWotBindingChannel` for a compiled, executable form. Dependency injection registers the **same** `WotProtocolBinderRegistry` singleton for both interfaces regardless of whether `AddWotRegistryServer` or `AddWotProtocolBinders`/`Add<Protocol>WotBinding` is called first.
* `IWotTargetVariableResolver` (default `WotTargetVariableResolver`) resolves the target `BaseVariableState` a `WotTargetMappingDescriptor` declares against the freshly imported predefined nodes:
  * `uav:mapToNodeId` alone resolves that exact portable NodeId (parsed including `nsu=` forms against `INodeManagerBuilder.Context.NamespaceUris`) and requires a `BaseVariableState`.
  * `uav:mapToType` alone resolves the unique variable whose `DataType` equals the target type.
  * Both terms resolve the exact node and validate its `DataType` equals the declared target type.
  * Missing, malformed, ambiguous, wrong-node-class or type-mismatch mappings fail activation with a deterministic `ServiceResultException` status (`BadNodeIdInvalid` / `BadNodeIdUnknown` / `BadBrowseNameDuplicated` / `BadTypeMismatch`); every portable NodeId parse failure — including one the parser itself raises as a `ServiceResultException` — is wrapped as `BadNodeIdInvalid` naming the offending term (`uav:mapToNodeId` / `uav:mapToType`) rather than surfacing the parser's own exception shape.
* `IWotProjectionBindingRuntimeFactory` (default `WotProjectionBindingRuntimeFactory`) groups the closure's target-mapped, executable compiled forms by resolved target variable and returns a `WotProjectionBindingRuntime` — the `IAsyncDisposable` the NodeSet generation owns:
  * A **direct** target (`uav:mapToNodeId` and/or `uav:mapToType` alone) wires executable `readproperty`/`writeproperty` forms as async `OnRead`/`OnWrite` handlers preserving source status/timestamps. A combined read/observe form retains local sampling; distinct or observe-only forms use the subscription bridge described below.
  * A **structured** target (`uav:mapToType` + `uav:mapByFieldPath`) reads fields concurrently and builds registered structures through `IEncodeableFactory` / `IStructure` / `IDataTypeDefinitionSource`, without reflection. Writes extract and write mapped fields concurrently. A Bad field fails the operation; usable Uncertain values remain usable. The result retains the first non-default status at the highest severity and the oldest available source timestamp; writes preserve Good subcodes such as `GoodClamped`.
  * Conflicting direct-vs-field mappings, duplicate read/write mappings for the same target/field, and unsupported target operations all fail activation deterministically. Everything else about a structured target that depends on its structure type being registered — the encodeable type lookup, root instance validation, and `uav:mapByFieldPath` path resolution (empty segments, unknown fields, array-valued or non-structure intermediate fields) — is deferred to the first structured read or write instead of failing activation, because `RuntimeNodeSetOptions.ConfigureAsync` runs before `NodeManagerLifecycle.RefreshComplexTypesAsync` registers the server's custom structure types. Resolution is retried, uncached, on every first use until it succeeds against the (by-then-populated) `IEncodeableFactory` instance; a still-unresolved first use returns a deterministic `BadConfigurationError` read/write status instead of throwing out of the request pipeline.
  * Channels are opened lazily, except where transparent event forwarding requires preparation before publication, and cached one-per-compiled-form for the generation; concurrent first use opens once, and a failed open is evicted so a later call can retry. Every successfully opened channel is disposed with the generation; disposal failures are aggregated. A channel open racing with, or started after, generation disposal never leaks: disposal marks the slot disposed under its lock so no later open can start, and still awaits and disposes a channel whose open was already in flight.
* Both abstractions are always available via direct construction (no DI container required) and are registered through `AddWotRegistryServer` using `TryAdd*` so a host application can supply its own implementation.

### Projected Methods, events, and Conditions

`WotBindingPlan.ProjectedAffordances` carries each declaration's local identity,
owning resource, and JSON Pointer separately from its upstream form address.
An action's `uav:id` identifies the local Method; its selected form supplies the
upstream Method and Object addresses. The runtime wires the local Method through
the existing asynchronous fluent `OnCall` hook. When an action offers several
forms, the runtime selects one supported, executable form and sends the request
only to that form's upstream source. It returns the upstream call's status and
any argument errors to the caller; invoking the local Method does not by itself
count as success.

Type-owned declarations need not have executable forms. Their declaration
context follows document containment or authoritative native ownership, not
`HasTypeDefinition`: a real instance of a Thing Model still needs executable
bindings for the actions and events it exposes. Native partitions remain
authoritative; routing metadata cannot introduce missing local Nodes.

A property backed by a native Variable needs no transport when it has no form
and no explicit target mapping. Its local Value remains exactly as the NodeSet
declares it, including an unspecified initial Value. This covers companion-model
metadata as well as local constants; it does not invent a data source for an
unbacked property or excuse a target mapping without a form.

`IWotProjectionEventPublisher` registers generation-owned streams of events with the
existing monitored-source lifecycle. Compatible local consumers share an
upstream subscription; the last consumer releases that subscription, while the
generation retains its channel until disposal. Events are reported through the local
notifier hierarchy. Organizational projection Views do not become event sources.
The stream implements `IEventSourceReadiness`, so creating an event monitored
item waits until its upstream subscriptions are active, not until the first
notification arrives. A startup failure is returned to the subscriber.

An event declaration identifies an EventType, not mutable Condition state.
`IWotProjectionConditionFactory` creates separate local Condition instances.
Selected fields are translated through their message-context namespace tables,
and a bounded occurrence-route table maps local EventIds back to the selected
source, Condition, and branch. Wrong-source, unknown, or revoked IDs fail rather
than falling back to another source. Retired-generation routes remain usable
only while that generation is alive and the declaration and source still match.

Condition-management actions use `uav:conditionAction` and same-document `uav:actsOn`.
For a WoT invocation with an optional Comment, the OPC UA adapter supplies
`LocalizedText.Null` when the caller provides only EventId. This does not change
a native two-argument Method's signature: OPC UA callers supply both arguments.
After acknowledgement changes the occurrence, confirmation uses the updated
EventId. Unbound standard Methods on a Condition proxy are disabled, so they
cannot change only the local copy.

Remote calls to standard Condition aliases are dispatched through the original
action's ObjectId and MethodId using the current caller's OperationContext.
Both the alias and the original receiver/Method must therefore pass effective
Call permissions, AccessRestrictions and live Executable/UserExecutable checks.
Removing the original receiver or Method makes the alias fail closed before
opening its binding channel. This also applies to Enable and Disable, which
do not take an EventId; occurrence actions retain their source EventId routing.
Trusted in-process calls without an OperationContext retain their direct path.

`WotProjectionBindingRuntimeOptions` limits pending notifications per local
notifier (`MaxQueuedEvents`) and retained occurrence routes per event declaration
(`MaxEventRoutes`). If the notification queue fills, the producer finishes
delivering already queued events, then stops with a `BadTooManyOperations`
error and releases its upstream subscription leases. The monitored-source
lifecycle logs the failure; other event sources continue running. This is a
server-side producer failure, not an `EventQueueOverflowEventType` notification
or termination of the client's entire subscription. Accepted occurrence routes
are not FIFO-evicted: route capacity rejects a new occurrence before state change
or publication. A later action with an unknown/revoked EventId fails with
`BadEventIdUnknown`. The event publisher, Condition factory, runtime factory,
and options are injectable as well as available for direct construction. The
[two-pump aggregation sample](../samples/WotCon/README.md) demonstrates
source-specific management actions and acknowledgement/confirmation without
introducing shelving, suppression, dialog, or ConditionRefresh transport mappings.

### Identity modes, provenance, and bounds

`LocalReEmission` is the default `WoTEventIdentityModeEnum`.
`uav:eventIdentityMode` accepts `local-re-emission` or `transparent-forwarding`;
unknown values fail. Direct declarations can use
`WotProjectedAffordance.FromConverted(converted).WithIdentityMode(...)`.
Transparent mode requires authenticated native occurrence/source binding,
current Session/mapping, EventId/EventType/SourceNode/timestamps, compatible
type lineage, and server-wide identity admission. It retains its source channel
before publication even without subscribers and never downgrades automatically.
Core types and custom subtypes without additional instance declarations are
supported; unknown/conflicting lineage, custom payload schemas, cycles, and
ancestry depth beyond 64 fail, rather than claim general schema equivalence.

`IWotCapturedEventChannel` retains declared operands and privately captures missing
Core identity/state fields. Public `Data`/`EventFields` remain the declared
selection. Detached prospective occurrences are validated before retained
Condition state changes. `IWotCapturedConditionActionChannel` pins the authenticated
Session, Method, receiver, and generation; permissions are checked before lookup,
and a new Session/source cannot inherit an old action route. Independent
Conditions/branches retain separate instances.

`EventBindings` exposes mode, document/pointer, generation, source URI, and
Availability. Provenance is evidence, not an action capability:

```csharp
session.Factory.Builder.AddOpcUaWotCon().Commit();
var binding = new WoTEventBindingTypeClient(session, bindingNodeId, telemetry);
WoTEventOriginDataType origin = await binding.GetEventProvenanceAsync(eventId, ct);
```

Unauthorized lookup fails `BadUserAccessDenied`; authorized unknown/disposed
evidence returns `BadNoData`. Verified retransmissions refresh receipt provenance
without another occurrence. Identity/state/source conflicts fault Availability
with `BadSecurityChecksFailed`, retain accepted evidence, and revoke its route.
Native ReceiveTime uses the local TimeProvider; source times remain source facts.

`MaxEventRoutes` counts each distinct ordinary/Condition/branch occurrence;
replay consumes no slot. Within a generation, restart/action expiry does not
reset it. Install a replacement generation through shadow reload to let old evidence drain.
The server-wide `EventIdentityAdmissionOptions.MaxEventIdentities` defaults to
65,536 and is configurable before server startup or by injection. Native/projected
events share that domain. Live event targets, retained Condition snapshots, queues,
and retransmission fields own native claims; after drain they are reclaimable,
possibly after collection. No server-lifetime strong table reference pins them.

Preparing transparent projection requires strict identity admission for the
remaining server lifetime. Earlier unsupported native publication prevents that
claim; subsequent collisions/exhaustion/unsupported shapes fail before delivery.
Copied fields, URI aliases, custom bypass queues, or reconstituted durable queues
do not establish continuity. No restart/HA/persisted-replay guarantee follows.

### Property observation and contextual values

The first executable form in authored order owns each operation. Failures do
not retry another form/source and writes do not fan out. Conflicting declarations
for one target/field/direction fail activation.

`WotReadResult.Context`, `WotNotification.Context`, and contextual writes retain
namespace/server/factory authority. Translation uses namespace URIs without
extending a remote Session's table; context-free channels support only
namespace-independent values. `IWotPropertyBindingChannel` accepts
`WotReadRequest`/`WotWriteRequest` with range, encoding, and message context.
OPC UA forwards native ranges/encoding; other channels process local read ranges
and reject indexed writes before I/O. One range is not applied to every field
of a structured write.

Distinct/observe-only forms use `ObserveAsync`. Sampling/Reporting Value items
share a source subscription; Disabled and metadata-only items do not.
The last Value consumer releases it. Observe-only reads use the cache, initially
`BadWaitingForInitialData`; a separately declared read form keeps its own source.
Delivery checks each consumer's permissions/range/encoding.
`MaxQueuedPropertyValues` defaults to 1024; overflow reports
`BadResourceUnavailable` and releases the source rather than dropping values.
Retry requires all Value consumers to deactivate before reactivation. Graceful
reload preserves old consumers; immediate handoff reconciles subscriptions
without fabricating a source notification from a local cache read.

### Action payload contracts

Actions distinguish absent schemas (zero arguments), one whole value, and named
positional arguments. Structures/Unions are not flattened. Named layouts require
complete `uav:fieldOrder`, not JSON member order:

```json
{
  "input": {
    "type": "object",
    "uav:argumentLayout": "named",
    "uav:fieldOrder": ["Minimum", "Maximum"],
    "properties": {
      "Maximum": { "type": "integer" },
      "Minimum": { "type": "integer" }
    }
  }
}
```

HTTP inputs `[-7, 42]` encode as `{"Minimum":-7,"Maximum":42}`. Empty named
objects and explicit null single values are distinct from absent contracts.
Outputs must have the exact count/types/members; malformed output is not partial
success. Compiled `WotPayloadDescriptor` retains detached layouts/schema facts
from the owning context. Projected native names/types/ranks must agree; optional
JSON members do not shorten a native signature.

`IWotContextualBindingChannel.InvokeAsync` accepts
`new WotInvokeRequest(inputs, messageContext, DiagnosticsMasks.OperationAll)`
and returns operation/input argument results. Direct activation can supply
`WotExecutorContext.WithMessageContext` or `OpenChannelAsync(form, messageContext, ct)`;
DI honors `IServiceMessageContext`. Source diagnostics resolve against the source
StringTable; the receiving server applies its own mask/permissions/table.
Invalid diagnostic indexes/counts/depth fail decoding.

`IWotInteractionPayloadCodec` owns whole action/event wire shapes; scalar-only
codecs reject unsupported shapes. JSON uses registered Structure/encodeable
metadata, preserves numeric TD semantics for Int64/UInt64, and rejects non-fitting
finite numbers. `MaxPayloadBytes` counts actual UTF-8; `MaxPayloadDepth` counts
root containers as one and scalars as zero; context `MaxArrayLength` also bounds
Structure-array allocation.

### Portable browse-path targets

On .NET 8+, OPC UA forms can address read/write/observe, Call, and event
subscription by portable `uav:id`, encoded `?id=`, or `uav:browsePath`:

```json
{
  "@context": { "plant": "urn:example:plant" },
  "base": "opc.tcp://opc.example:4840/UA/Factory/",
  "uav:browsePathAnchor": "i=85",
  "properties": {
    "temperature": {
      "type": "number",
      "forms": [{
        "href": "",
        "op": ["readproperty", "observeproperty"],
        "uav:browsePath": "plant:Pump/plant:Temperature"
      }]
    }
  }
}
```

This addressing fragment does not prescribe unsecured access. Leading `/` starts
at Root (`i=84`); relative paths use the nearest explicit anchor, otherwise the
nearest `uav:id`. An outer explicit anchor wins over a nearer fallback identity.
The path retains its declaration's context, not a form-local prefix override.
Annex A supports hierarchical `/`, Aggregates `.`, explicit `<HasComponent>`,
exact `<#HasComponent>`, inverse `<!HasComponent>`, escaped `&/`, and URI-qualified
BrowseNames. Numeric namespace prefixes are non-portable.

Before business I/O, translation against current Session tables requires one
complete local target with the right NodeClass; simultaneous NodeIds must agree.
Missing/partial/remote/ambiguous targets fail. `uav:callObjectId` remains a
separate receiver, not inferred from View placement.
Subscriptions revalidate on Session changes and periodically; replacement items
prepare disabled and obtain a fresh sample after admission. Known stale targets
emit Bad status. Subscription disposal stops maintenance/removes items regardless
of Session ownership. `BrowsePathRefreshInterval` defaults to ten seconds;
TimeProvider is injectable. `MaxBrowsePathElements` defaults to 64 and `MaxUriLength`
bounds text. Malformed paths are never ignored in favor of NodeId-only addressing.

### Registering binders and executors

`SupportedBindings` is browseable even when empty; `Enabled` means effective
executable operations, not remote connectivity. URI/version descriptor conflicts
fail configuration and omitted custom metadata stays absent. `SelectedBindings`
is a detached deterministic snapshot of published plans. Direct consumers use
`GetSelectedBindingCapabilitiesAsync`; register `WoTBindingCapabilityDataType`
with the Session's encodeable factory before decoding it.

The planner binders are opt-in and replaceable. `AddHttpWotBinding`, `AddModbusWotBinding`, and `AddOpcUaWotBinding` come from the base Bindings package on `net8.0+`; `AddMqttWotBinding` requires the separate MQTT package:

```csharp
builder
    .AddWotRegistryServer(o => o.StrictBindings = false)
    .AddHttpWotBinding()                 // planners + HTTP executor
    .AddModbusWotBinding()               // + Modbus TCP executor
    .AddMqttWotBinding()                 // + MQTT executor
    .AddOpcUaWotBinding(o => o.SessionFactory = ConnectSessionAsync);
```

Each `Add<Protocol>WotBinding` registers the eight planner binders (idempotently) and its executor. Without any executor, `AddWotProtocolBinders()` still validates and compiles plans, materializing non-executable nodes.

Replace or add binders directly:

```csharp
builder.AddWotBinder(new MyCustomBinder());               // custom planner
builder.AddWotBindingExecutor(new MyCustomExecutor());    // custom executor
builder.AddWotCredentialProvider(new VaultCredentialProvider());
```

Selection is deterministic: the registry evaluates binders in ordinal `id@version` order and chooses the highest-priority `WotBindingMatch`.

To write `MyCustomBinder` and `MyCustomExecutor` see [Adding your own binding](#adding-your-own-binding). The worked
`AggregationServer.MemoryWotBinder` implementation in the WotCon aggregation sample binds a fictitious `mem://` protocol
to an in-process key/value store.

### Intentionally unsupported operations

* CoAP, BACnet, PROFINET and LoRaWAN ship as **planner-only** (non-executable) in this build.
* The Modbus binding does not support action invocation or events (Modbus has no such concept); those operations return `BadNotSupported`.
* The OPC UA executor implements read/write/invoke and **native** observe / event subscription (a `Subscription` / `MonitoredItem` pair per channel, Part 4 §5.12 / §5.13) — see [Operation coverage](#operation-coverage-opc-ua-executor) below.
* The MQTT executor implements publish/subscribe; request/response RPC with a dedicated response topic is not modelled (actions publish only).

### Transport security

The executable bindings fail closed and never downgrade a secure form to an insecure transport:

* **MQTT** — an `mqtts://` href always enables TLS and defaults to port 8883; an `mqtt://` href stays explicit plaintext (port 1883). Username / password, the TLS client certificate and TLS trust anchors are resolved through the `IWotCredentialProvider`; a form that declares a security scheme is refused when the provider resolves no credential. Username / password over plaintext `mqtt://` is refused unless `MqttWotBindingOptions.AllowCredentialsOverPlaintext` is set.
* **HTTP** — the executor-owned `HttpClient` disables automatic redirects and applies a bounded, origin-aware redirect policy: custom header and query credentials are stripped across origins, redirect loops and non-`http(s)` schemes are refused, an `https`→`http` downgrade is refused unless `AllowInsecureRedirectDowngrade` is set, and the hop count is capped by `MaxAutomaticRedirects` (default 5). A caller-supplied client used with a credential-bearing form fails closed unless `HttpWotBindingOptions.CallerClientHandlesRedirectSafety` confirms the client handles redirects without leaking credentials.
* **Modbus** — `modv:address` must be 0–65535 and the addressed range (`address + quantity - 1`) must stay in the 16-bit space; function-only forms map exactly onto function codes 1, 2, 3, 4, 5, 6, 15 and 16, and op/function (or entity/function) mismatches are rejected. The executor re-validates the range before narrowing to `ushort` / `byte`.

### Operation coverage (OPC UA executor)

| Operation | Mechanism |
| --- | --- |
| `readproperty` | `Read` service (`ISession.ReadValueAsync`). |
| `writeproperty` | `Write` service; the mapped `StatusCode` is preserved. |
| `observeproperty` | A native data-change `MonitoredItem` (`AttributeId = Value`, queue size 1) on a dedicated `Subscription`; no client-side polling. |
| `invokeaction` | `Call` service; the selected form identifies the Method by `uav:id`, encoded `?id=`, or `uav:browsePath`. A form-scoped `uav:callObjectId` takes precedence; otherwise the planner can capture the explicitly identified original source Thing Object as described below. Local containment is not receiver authority. |
| `subscribeevent` | A native event `MonitoredItem` (`AttributeId = EventNotifier`) whose `EventFilter` select clauses are the compiled `WotEventSelection` of WoT Binding Section 6.1: the eight mandatory `BaseEventType` fields (`EventId`, `EventType`, `SourceNode`, `SourceName`, `Time`, `ReceiveTime`, `Message`, `Severity`) when the affordance states no selection, and otherwise the selection resolved from the EventType definition it links to with `tm:ref`, overlaid by the `uav:eventSelectClauses` it states. Every selected field is delivered in `WotNotification.EventFields`, keyed by its browse path — an empty path supplies `ConditionId` — with the event's own `Time` / `ReceiveTime` as the source / server timestamp. |

Both subscription kinds share one code path: a dedicated `Subscription` is created per channel subscription, its `MonitoredItem` is disposed and the subscription removed from the session (`ISession.RemoveSubscriptionAsync`) when the returned `IWotSubscription` is disposed, so no session or subscription is leaked — including when creation fails partway through.

A compiled form's NodeId (`uav:id`, and `uav:callObjectId` for action receivers) is resolved with `NodeId.Parse` for the plain `ns=` / `i=` / `s=` / `g=` / `b=` forms; a portable NodeId carrying an `nsu=` namespace URI is parsed as an `ExpandedNodeId` and resolved against the connected session's namespace table, since `NodeId.Parse` alone cannot resolve a namespace URI without one.

### Source-Thing Call receiver selection

A present form-scoped `uav:callObjectId` must be a portable, non-null NodeId.
An invalid explicit value fails planning; it never falls back to the Thing.
The aggregation sample retains this explicit Binding 1.1 contract.

When the term is omitted, extraction can capture the original source Thing's
explicit `uav:id`. The document must describe an Object, not a Thing Model,
Variable, Method, View or projection envelope. Its OPC UA base and forms must
identify one endpoint, matching the selected action endpoint. Missing identity,
multiple endpoints and relocated actions without captured receiver authority
fail closed. Scenario/projection envelopes cannot supply their local root as
an upstream receiver.

For source-routed projections, resolution retains original Object receiver facts
on the live `WotDocument` result without adding or replacing JSON form terms.
Pass that result to `WotBindingPlanRequest.FromDocument(resourceXid, kind, document)`
to carry the facts into the immutable plan. Nested imports retain these facts.
An existing explicit value, including an invalid one, remains unchanged and takes
precedence. Explicit imported ObjectType receivers, including the standard
ConditionRefresh receiver, retain their existing explicit-Call behavior.
Neither `uav:resolvedFrom` alone nor local `uav:componentOf` arrays supply authority.

**Persisted resolved-only JSON does not retain these source facts.** Re-parsing it
therefore rejects an omitted receiver, rather than inferring from the projection
root or treating the provenance locator as proof. Automatic recovery of trusted
original-source facts from that persisted result is not implemented. Retain the
live resolution result, resolve the original projection again with its configured
source acquisition, or author an explicit current-format receiver. Explicit
`uav:callObjectId` imports are unaffected by this limitation.

Before dispatching an inferred source-Thing Call, the executor checks the connected
endpoint, reads the receiver and Method NodeClasses, and checks `HasComponent`
ownership on the Object or its ObjectType/supertype hierarchy. The traversal is
bounded to 100 types and 10,000 references, releases continuation points, and
rejects ambiguous/remote type ancestry. Session generation and namespace context
are checked through dispatch. Denied inspection, wrong classes or failed
ownership produce an error without a speculative Call. Standard Condition instance
actions additionally require the appropriate Condition type ancestry;
ConditionRefresh/ConditionRefresh2 cannot use an inferred Object receiver.
The Server remains authoritative for execution permissions and explicit-Call
Object/ObjectType ownership. MCP uses the captured `callObjectId`
for both its exact Method/receiver grant and mutation guards.

For example, this no-argument action omits the receiver because the original
Thing identifies it:

```json
{
  "@context": [
    "https://www.w3.org/2022/wot/td/v1.1",
    { "uav": "http://opcfoundation.org/UA/WoT-Binding/" }
  ],
  "@type": "Thing",
  "title": "Source pump",
  "securityDefinitions": { "none": { "scheme": "nosec" } },
  "security": ["none"],
  "base": "opc.tcp://source.example:4840",
  "uav:id": "nsu=urn:example:pump;s=Pump",
  "actions": {
    "reset": {
      "forms": [{
        "href": "opc.tcp://source.example:4840",
        "uav:id": "nsu=urn:example:pump;s=Reset",
        "op": "invokeaction"
      }]
    }
  }
}
```

This is not a way to infer a Condition receiver from an event's `SourceNode`,
a containing Object, or a local Method alias. A source-selected Condition
receiver must pass the same Object/type ownership checks. Existing explicit
Condition occurrence bindings still require their captured source correlation;
omission does not make an unexposed Condition identifiable or authorize an
unrelated Object.

**Published Binding v1.00 interoperability remains incomplete.** Published
[OPC 10101 v1.00 Section 7.1.3](https://reference.opcfoundation.org/specs/OPC-10101/v1.00/t63916693358/download/markdown)
requires an **action-level `uav:componentOf` array** declaring the owning Object.
Section 6.5.3's lack of mandatory *form* terms does not remove that requirement.
The published array is distinct from the obsolete scalar form-level fallback
and from local projection containment. This runtime does not select a receiver
from that published action-level owner array, nor reconstruct native argument
ordering from server metadata for schemas lacking the current explicit layout.
Source-Thing selection therefore does not establish full published v1.00
interoperability. These limits are separate from
[Connectivity 1.02 asset compatibility](WotCon.md#wot-con-102-compatibility) and
do not close the mandatory schema-validation gap documented below.

<a id="event-field-selection"></a>

### Event field selection (`tm:ref` and `uav:eventSelectClauses`)

WoT Binding Section 6.1 states an event's `EventFilter` select clauses on the **event
affordance** — never on a form — and states them by **linking the EventType definition**
the fields are selected from. An `events` map is an affordance map wherever it appears,
not only at the document root, so a clause on a member of a nested one — the event
collection a link carries, for instance — is equally legal, and a clause one level away
from any such member (at the root, on a property affordance, on an action's `input`, on
an event's `data`, on a form) selects nothing and is rejected. The permission and the
prohibition route at the same places on purpose: a rule that admitted a nested map while
forbidding a clause inside it would make one document simultaneously valid and invalid.
The link is a `tm:ref`, and it names the definition in one
of three shapes: a document URI optionally followed by an RFC 6901 JSON Pointer, the
logical identifier of a document whose root *is* an EventType Thing Model, or the logical
identifier a nested event affordance carries in `@id`. A logical identifier is a JSON-LD
term, so a compact IRI such as `evt:highTemperatureAlarm` is expanded in the active
context of the node that **wrote** it — the same short form written in two documents that
bind the prefix differently names two different definitions. It resolves to an *EventType
definition* — an event affordance, or a Thing Model root, that carries
`@type: uav:eventType`, the portable `uav:id` of the OPC UA EventType, and the
object-valued `data` schema of its fields. `uav:id` alone does not make an affordance a
definition: it identifies the Node the affordance projects, which every event affordance
has. `uav:eventSelectClauses` is the **refinement** of that baseline: each clause
carries exactly `tm:ref` and `uav:browsePath` (relative, because the definition the
clause names anchors it).

```jsonc
"events": { "highTemperature": {
  "@type": "uav:eventType",
  "tm:ref": "./event-types.tm.jsonld#/events/highTemperatureAlarm",
  "uav:eventSelectClauses": [
    { "tm:ref": "./event-types.tm.jsonld#/events/limitAlarm",
      "uav:browsePath": "HighHighLimit" },
    { "tm:ref": "./event-types.tm.jsonld#/events/highTemperatureAlarm",
      "uav:browsePath": "Severity" }
  ],
  "forms": [{ "href": "opc.tcp://server:4840", "uav:id": "i=2253",
              "op": ["subscribeevent"] }] } }
```

What the runtime does with it:

* **Resolution happens before planning.** `Opc.Ua.Wot.WotEventSelectionResolver` resolves
  each `tm:ref` through an `IWotThingResolver` — the sibling documents a caller already
  holds — and never dereferences a URI over the network. Sources are consulted in a fixed
  order: the documents held together with the referring one (matched by logical
  identifier), then that reference as a location through each configured resolver in the
  order it was given, then the small well-known catalog this library carries for the OPC
  UA base types. The order is total and each stage yields a *set* rather than a first
  match, so a reference two held documents answer differently is reported as ambiguous
  rather than resolved by whichever was read first, and the built-in catalog is last so a
  definition this library carries can never shadow one an author shipped. That catalog's
  `BaseEventType` declares `LocalTime` in addition to the eight mandatory fields, because
  a definition states what a type *has* while the implicit selection states what a
  consumer subscribes to when the document says nothing. It walks the linked definition's
  `data` once and turns each **leaf** into one clause: the members of an object are walked
  in the order its `uav:fieldOrder` states, a member's `uav:browseName` supplies the exact
  QualifiedName (a bare member name stands for it only where that name is a legal
  unqualified BrowseName), the `ConditionId` member yields the empty path, and a state
  Variable's trailing `Name` is dropped because the clause naming the Variable supplies
  that object's `Name`. Every derived clause carries the definition's `uav:id` as its
  `TypeDefinitionId`. Derivation is total: a definition the resolver cannot walk — a
  `data` that is not an object, a walked object with no field order, a member name that is
  neither legal nor annotated — is reported, and no partial selection is produced.
* **The explicit clauses overlay a linked baseline, and replace a missing one.** Where the
  affordance carries a `tm:ref`, the materialized member paths are computed over the
  derived baseline and the explicit clauses together, every baseline clause an explicit
  clause names is removed, and the explicit clauses are appended in the order they are
  written. There is no *remove* operation: an author who needs a narrower selection links
  to a definition that declares the narrower field set. Where the affordance carries no
  `tm:ref`, the clauses it writes are the **complete** selection — the baseline is empty.
  The eight mandatory `BaseEventType` fields (`EventId`, `EventType`, `SourceNode`,
  `SourceName`, `Time`, `ReceiveTime`, `Message`, `Severity`), stated once in
  `Opc.Ua.Wot.WotEventSelectClauses.Default`, are what an affordance that states *no*
  selection at all falls back to; they are not a floor under an authored one, because a
  document that deliberately selects one field must not subscribe to nine.
* **Planning stays synchronous.** `IWotBinderRegistry.Prepare` is side-effect free, so the
  resolved selections are carried into it: build the request with
  `WotBindingPlanRequest.FromDocumentAsync(..., IWotThingResolver, ...)`, or resolve once
  with `WotBindingPlanRequest.ResolveEventSelectionsAsync` and pass the resulting
  `WotEventSelectionCatalog` to `WotBindingPlanRequest.FromDocument`. Inside a server the
  materialization coordinator does this for every closure member, using the same snapshot
  resolver the conversion uses. An affordance that states a selection and reaches a
  planner with no resolved selection fails the form with `EventSelectClauseInvalid`
  rather than performing I/O during planning.
* `OpcUaBindingPlanner` compiles the **effective** selection onto
  `WotCompiledForm.EventSelection` as an ordered list of
  `Opc.Ua.Wot.WotResolvedEventSelectClause`, each carrying the portable
  `TypeDefinitionId` its definition declared.
* A compact path element such as `pump:Temperature` is rewritten to the portable
  `nsu=<NamespaceUri>;Temperature` form using the prefixes the document's `@context`
  binds (`WotBindingPlanContext.NamespacePrefixes`). An unbound prefix fails the form
  with `UnboundNamespacePrefix` rather than guessing a namespace.
* A browse path is parsed into **elements** once — `PathElements`, produced by
  `WotEventSelectClauses.SplitBrowsePath` — and every rule below is stated
  over those elements rather than over the joined string. A NamespaceUri routinely
  contains `/`, which is also the path separator, so only the separators that follow
  the delimiter ending a NamespaceUri (`;` for the OPC 10000-6 `nsu=` form, `}` for the
  OPC 10000-4 `{...}` form) separate elements. `nsu=http://example.org/pump/;Temperature`
  is therefore **one** element whose member is `data.Temperature`, and not five elements
  nesting the field under `nsu=http:`, an empty member and `example.org`. Escaping does
  not solve this — OPC 10000-6 §5.3.1.11 escapes only `;` and `%` — so the elements, and
  never the joined string, are what the member path, the field name, the collision check,
  the `SimpleAttributeOperand` browse path and the nested `data` object are all built
  from. `WotEventSelectClauses.JoinBrowsePath` is the exact inverse.
* `OpcUaWotBindingChannel` materializes each clause into a `SimpleAttributeOperand`
  against the connected session's namespace table, using the resolved portable
  `TypeDefinitionId`. The **empty** browse path selects the `NodeId` Attribute — the
  OPC 10000-9 `ConditionId` idiom — and every other clause selects `Value`.
* Two clauses **shall not** materialize the same `data` member, even where they reference
  different EventTypes and even where their normalized browse paths differ: the
  **materialized member path** — the sequence of `data` member names the clause fills —
  determines the output, so two clauses that reach it would compete for it and
  nothing in the document would say which of them filled it. Normalization resolves each
  element's prefix to the NamespaceUri the document binds it to, so two prefixes for one
  namespace name one path; but the member name drops the qualification altogether and a
  state Variable appends `Name`, so an unqualified `Severity` beside a
  namespace-qualified `Severity`, and `EnabledState` beside `EnabledState/Name`, are each
  two paths and one member. A collision is an `EventSelectClauseInvalid` error in
  `WotEventSelectClauses.TryParse`, in the resolver's overlay, and again in the planner,
  which re-checks the list it rewrote into portable form.
* An `EventFilter` `WhereClause` / `ContentFilter` is out of scope of the Binding; a
  clause carrying one is rejected with `EventSelectClauseInvalid` instead of being
  reinterpreted. The same holds for the NodeId clause form: a clause names its EventType
  by reference, so `uav:typeDefinitionId` is rejected as an unexpected member.

#### What a notification carries: the nested `data` object and the transport index

A clause materializes into exactly one member of the event affordance's `data`
object, by a rule that is a function of its browse path and the list the clause
sits in:

| Clause path | `data` member |
|---|---|
| `""` (empty) | `data.ConditionId` |
| `Severity` | `data.Severity` |
| `EnabledState/Id` | `data.EnabledState.Id` |
| `EnabledState` | `data.EnabledState.Name` |

A `data` member name therefore **never** contains the path separator:
`EnabledState/Id` is two nested members and not one member called
`EnabledState/Id`. Where the selected Node is an OPC UA state Variable — whose
own value is the state's localized display text and whose `Id` sub-Variable
carries the Boolean — the clause naming the field supplies that object's `Name`
member. `Opc.Ua.Wot.WotEventSelectClauses.StateVariableFieldNames` names the
states this Binding declares (`EnabledState`, `AckedState`, `ConfirmedState`,
`ActiveState`), which is exactly the set the Condition `data` schema of
Section 13.3 writes as an `{ Id, Name }` object; a companion state is recognized
from the selection itself, because a field another clause of the same list nests
through is an object whose `Name` member carries the field's own value.

`WotNotification` carries both representations, built together from one
selection so they cannot disagree:

* **`WotNotification.Data`** is the nested `WotEventData` object above — the
  shape the Binding describes, and the one to read.
* **`WotNotification.EventFields`** is the flat index keyed by the *joined*
  browse path the document authored (`EnabledState/Id`, and `ConditionId` for
  the empty path). Section 6.1 names this what it is: a transport-side artifact
  of one implementation, because a `MonitoredItem` returns field values
  positionally and a runtime naturally keys them by the clause that asked for
  them. It is kept for compatibility; a document never names a `data` member
  with a joined browse path.

Where two clauses would fill one `data` member the plan is rejected before a
subscription exists, so the runtime never has to choose. If a collision still
reaches `WotEventDataBuilder` — a plan assembled around the planner, or a Server
field list the selection does not describe — the first stated clause keeps the
member and the collision is logged as an error rather than silently dropped.

An event affordance links its EventType definition with `tm:ref` and states the
clauses that refine it with `uav:eventSelectClauses`, as described above.

### Constraining an `auto` endpoint selection (`uav:minimumSecurity`)

WoT Binding Section 5.7.1 lets an `auto` security scheme state a floor:

```jsonc
"securityDefinitions": {
  "opcua_auto_sc": {
    "scheme": "auto",
    "uav:minimumSecurity": {
      "uav:securityMode": "Sign",
      "uav:securityPolicy": "Basic256Sha256"
    }
  }
}
```

The planner compiles it onto `WotCompiledForm.SecurityFloor`. A floor the Binding cannot
read — one carried by a scheme other than `auto`, or naming a mode or policy Section 5.7
does not — fails the form (`InvalidSecurityFloor`) instead of compiling without the
constraint.

Because endpoint selection needs an application configuration, a certificate store and a
transport this library is deliberately not given, the choice is made by a delegate — but
the *rules* are made here, and the executor never opens a session it could not have
chosen:

* `OpcUaWotBindingOptions.ConstrainedSessionFactory` receives an
  `OpcUaWotSessionRequest` carrying the floor, so a caller's own factory can discard
  endpoints before opening a channel.
* `OpcUaWotBindingOptions.EndpointDiscovery` together with
  `SelectedEndpointSessionFactory` is the **built-in** path: the executor calls
  discovery, applies `OpcUaWotEndpointSelector.Select`, and hands the chosen
  `EndpointDescription` to the factory. The selection is the clause's own — discard
  everything below the floor, then take the strongest mode, then the strongest policy
  (ranking a policy the Binding does not name below every policy it does), then the
  highest `securityLevel`, then the smallest `endpointUrl` in ascending Unicode
  code-point order (the shared `WotCodePointComparer` of Annex G.3), then the earliest
  position in the response. Where no endpoint is eligible the activation fails with
  `BadSecurityModeRejected` and no session is opened: a client **shall** fail and report
  rather than fall back below a stated floor.
  Before ranking, every advertised destination (and any proxy URL) is validated
  against the registry's `WotEndpointPolicy` and its endpoint URL is normalized.
  Denied or malformed candidates are discarded even when no floor is stated.
  If discovery returned candidates but none pass destination policy, activation
  fails with `BadUserAccessDenied` without invoking the selected-endpoint factory.
* The endpoint-blind `SessionFactory` stays exactly as it was where the form states **no**
  floor. A form that states one and finds only that factory configured fails with
  `BadConfigurationError` naming what to configure, rather than opening a session through
  a factory that could not honour the floor and rejecting whatever endpoint it happened
  to pick — a false negative that reads as "no endpoint is strong enough" even when the
  Server offers one.
* Whichever path is used, `OpcUaWotBindingExecutor` verifies the endpoint the returned
  session reports and **fails closed** (`BadSecurityModeRejected`, session disposed) when
  it is below the floor, or when the session cannot state its endpoint at all. A floor
  whose enforcement was merely assumed would be a claim rather than a guarantee.

The clause constrains a choice among the endpoints a Server already offers and nothing
else: certificate trust, trust-list policy, filtering on any other endpoint attribute and
transport-profile negotiation stay with the application's own security configuration.

`OpcUaWotSessionRequest.EndpointPolicy` carries the same outbound policy to both
constrained and selected-endpoint factories. A factory that performs further
discovery or rewrites a destination must call `request.ValidateEndpoint(url)`
**before** opening that connection, including any proxy connection. The helper
returns a normalized URI or throws the policy's validation error. The executor
also validates the endpoint reported by the returned session before exposing a
binding channel; this check cannot undo a connection a custom factory already
opened. The legacy endpoint-only factory cannot receive this context: applications
using it must independently enforce the same policy on any destination changes,
or migrate to the constrained factory. This does not change certificate trust
requirements or the endpoint policy's documented DNS-resolution limitations.

For example, the selected-endpoint factory can enforce the boundary again
immediately before handing the description to its application-specific connector:

```csharp
options.SelectedEndpointSessionFactory = async (description, request, ct) =>
{
    description.EndpointUrl = request.ValidateEndpoint(description.EndpointUrl).AbsoluteUri;
    if (description.ProxyUrl is { } proxy)
    {
        description.ProxyUrl = request.ValidateEndpoint(proxy.OriginalString);
    }
    return await ConnectSelectedEndpointAsync(description, ct).ConfigureAwait(false);
};
```

`ConnectSelectedEndpointAsync` is the application's connector; it must not
silently replace either validated destination with an unvalidated discovery result.

The same selection paths also enforce exact `uav:channelsec` and actual
`uav:authentication` (Anonymous/UserName/Certificate/IssuedToken) requirements.
`OpcUaWotSessionRequest.SecurityRequirements` preserves complete `allOf`/`oneOf`
alternatives; channel and identity cannot be mixed across alternatives.
Invalid references/cycles/depth/alternative limits fail before connection.
`uav:issueToken` is a secret-free provider reference. Constrained forms with only
an endpoint-blind `SessionFactory` fail `BadConfigurationError`.
Established identity mismatch reports `BadIdentityTokenRejected`; rejected owned
Sessions are disposed, borrowed Sessions are not.

## Adding your own binding

This guide explains how to add a protocol binding to the WoT Connectivity runtime from form identification through live
value exchange, registration, diagnostics, tests, packaging, and NativeAOT validation. The current worked implementation
is [`MemoryWotBinding.cs`](../samples/WotCon/AggregationServer/Bindings/MemoryWotBinding.cs) in the WotCon aggregation
sample, so it demonstrates the extension pattern without shipping in the `Opc.Ua.WotCon.Bindings` package or being
registered by the sample host. A test-only copy lives in
[`tests/Opc.Ua.WotCon.Tests/Support/MemoryWotBinding.cs`](../tests/Opc.Ua.WotCon.Tests/Support/MemoryWotBinding.cs).
The production HTTP, Modbus TCP, OPC UA, and MQTT implementations provide protocol-specific examples.

### Architecture and lifecycle

The binding pipeline separates pure document processing from transport I/O:

1. `WotFormExtractor` parses property, action, and event forms into immutable `WotAffordanceForm` values. It applies default WoT `op` values, inherits Thing-level security when a form has no override, clones the form and affordance JSON, and records RFC 6901 JSON Pointers.
2. Every `IWotProtocolBinder` exposes a stable `WotBindingIdentity`, a version-pinned `WotBindingCapability`, deterministic `IWotBindingIdentification`, and an `IWotBindingPlanner`.
3. `WotProtocolBinderRegistry.Prepare` validates protocol-neutral target mapping, selects one binder for each form, and calls its planner without performing transport I/O.
4. The planner validates protocol vocabulary and addressing, then emits one immutable `WotCompiledForm` per supported operation. A compiled form carries endpoint, addressing, operation, payload, secret-free credential references, target mapping, and executability.
5. The materialization coordinator converts a dependency closure to runtime NodeSet2 content and passes its plans in `WotProjectionDocument.BindingPlans`.
6. After the NodeSet is imported, `WotProjectionBindingRuntimeFactory` wires the compiled forms to local Variables, Methods, and event declarations. Variable wiring is synchronous; transparent event preparation acquires its authenticated source channel before publication.
7. On first read or write, `WotBindingChannelSlot` asks `IWotBindingChannelFactory.OpenChannelAsync` for a live channel. The registry resolves the matching `IWotBindingExecutor` and creates a `WotExecutorContext` containing credentials, codecs, and bounds.
8. The runtime NodeSet generation owns the resulting `IAsyncDisposable` binding runtime. The runtime owns every lazily opened channel and disposes them when that generation drains and is removed.

`IWotBinderRegistry.ActivateAsync` is called only after the new projection becomes active. On replacement, the shadow switch succeeds before the coordinator deactivates the old plans and activates the new plans. The old runtime NodeSet generation can continue serving its existing monitored items until they drain; its generation-owned channels are not disposed until that old generation is removed. If conversion, wiring, or shadow activation fails, the previous active generation remains available.

### Identification and capability

Use a stable binder id and a version that identifies the planner behavior. `WotBindingIdentity.Key` is `id@version`, and multiple versions can coexist. Executor lookup first uses the exact key and then the id-level default.

`WotBindingCapability` must accurately describe the version-pinned source document, operations, content types, and whether the binding has a runtime implementation. The capability is projected to `WoTBindingCapabilityDataType`, advertised by the registry, and included in unchanged-generation decisions.

Identification must be deterministic. `WotProtocolBinderBase.MatchStandard` implements the normal precedence: an explicit resource pin is stronger than a vocabulary match, which is stronger than a URI-scheme match. The registry evaluates binders in ordinal `id@version` order and uses that order to break equal-priority matches. Override `Match` directly when the protocol also requires a subprotocol or a pinned shape rule.

Do not claim a form merely because its URI scheme is vaguely related to the protocol. A false positive prevents a better binder from compiling the form and turns a protocol-selection problem into misleading planner diagnostics.

### Form extraction and vocabulary terms

`WotAffordanceForm.FormElement` contains the form object and is where protocol-specific form vocabulary normally belongs. `AffordanceElement` contains the owning property, action, or event. Use `TryGetString`, `TryGetBoolean`, `TryGetInt32`, and `TryGetStringArray` instead of deserializing arbitrary objects or using reflection.

The planner should validate every term it consumes, reject contradictory terms, enforce `WotBindingBounds`, and report diagnostics at `form.Pointer("term")`. Use `form.AffordancePointer("term")` only for terms defined on the owning affordance. Unknown terms from a pinned vocabulary should produce `UnknownVocabularyTerm` when accepting them could change behavior.

`WotFormExtractor` emits a formless descriptor for an affordance with no `forms` array. Strict materialization rejects an instance affordance that needs an executable route but has none. Type-owned declarations and native Variables without forms or explicit target mappings are exempt, as described under [Projected Methods, events, and Conditions](#projected-methods-events-and-conditions).

### Authoring OPC 10101 target mapping

[OPC 10101 section 6.5.4](https://reference.opcfoundation.org/specs/OPC-10101/6.5.4) defines generic OPC UA vocabulary terms for annotating Thing Descriptions. [Section 8.2](https://reference.opcfoundation.org/specs/OPC-10101/8.2) demonstrates that the mapping vocabulary is not limited to OPC UA source forms: its example maps properties from a Modbus energy meter into an OPC UA data model.

The runtime implements the following affordance-level semantics:

* `uav:mapToNodeId` identifies the exact OPC UA target variable.
* `uav:mapToType` identifies the target variable by its OPC UA `DataType`; resolution requires a unique variable of that type.
* When both are present, the exact node is resolved and its `DataType` must equal `uav:mapToType`.
* `uav:mapByFieldPath` maps a property to a field within a structured target and is valid only together with `uav:mapToType`.
* All three terms belong on a property affordance. Authoring them inside an individual form is invalid, and authoring them on an action or event is invalid.
* Values must be non-empty strings. The registry validates these rules before any protocol planner runs and copies one `WotTargetMappingDescriptor` to every compiled operation for that property.

This direct mapping is valid because the target term is a sibling of `forms` on the property affordance:

```json
{
  "properties": {
    "temperature": {
      "type": "number",
      "uav:mapToNodeId": "nsu=urn:example:aggregate;s=Device1.Temperature",
      "forms": [
        {
          "href": "https://sensor.example.test/temperature",
          "op": "readproperty"
        }
      ]
    }
  }
}
```

A structured mapping puts both type and field path on the property:

```json
{
  "properties": {
    "lineVoltage": {
      "type": "number",
      "uav:mapToType": "nsu=urn:example:types;s=EnergyMeasurementsType",
      "uav:mapByFieldPath": "VoltageL1N",
      "forms": [
        {
          "href": "modbus+tcp://meter.example.test",
          "op": "readproperty",
          "modv:entity": "holdingregister",
          "modv:address": 100
        }
      ]
    }
  }
}
```

Moving any `uav:mapTo*` or `uav:mapByFieldPath` member inside the form object is invalid even if the form uses the OPC UA protocol.

Use portable `nsu=` NodeIds whenever documents can move between servers whose namespace indexes differ. `WotTargetVariableResolver` parses `uav:mapToNodeId` and `uav:mapToType` with `ExpandedNodeId.Parse(text, builder.Context.NamespaceUris)`, so `nsu=urn:vendor:model;s=Device1.Value` resolves against the materialized generation's namespace table. A numeric `ns=` identifier is valid only when the author controls the target server's namespace-index assignment.

Target mapping is protocol-neutral. The form can address HTTP, Modbus, MQTT, OPC UA, or a custom protocol while the affordance maps the resulting value to an OPC UA variable. Protocol planners must not parse, reinterpret, or discard `uav:mapToNodeId`, `uav:mapToType`, or `uav:mapByFieldPath`.

### Planner validation and compiled forms

Deriving from `WotProtocolBinderBase` provides helpers for common work:

* `RequireHref` validates presence and `MaxUriLength`.
* `TryParseUri`, `SchemeOf`, `MakeEndpoint`, and `MakeEndpointOrSynthetic` normalize endpoint metadata.
* `ResolveOperations` validates affordance/operation compatibility, filters unsupported operations, and avoids duplicate teardown entries.
* `ResolveCodec` selects a codec and creates `WotPayloadDescriptor`.
* `ResolveSecurity` converts document security definitions into secret-free `WotCredentialReference` values.

Return `WotBindingCompilation.Unsupported(...)` when the binder cannot produce any valid entry. Return `Supported(entries, diagnostics)` only when entries are non-empty and there are no error diagnostics. The registry treats a compilation with errors as unsupported even if entries were returned.

Keep `WotCompiledForm` immutable and transport-neutral. Put protocol additions in the `Metadata` dictionaries of `WotEndpointDescriptor`, `WotAddressingDescriptor`, `WotOperationDescriptor`, or `WotPayloadDescriptor`. Do not store open clients, mutable protocol state, credentials, delegates, or disposable resources in a plan.

A planner can ship without an executor. The registry still validates and compiles its forms but marks its entries non-executable and the non-strict projection degraded. This is the preferred path for landing a validator before the transport runtime is ready.

### Executors, channels, and disposal

`IWotBindingExecutor.CanExecute` should reject compiled forms for another identity. `ActivateAsync` receives one immutable compiled form and a `WotExecutorContext`; it returns a live `IWotBindingChannel`.

The channel implements read, write, invoke, property observation, event subscription, and asynchronous disposal. Unsupported operations return `BadNotSupported` instead of throwing. Transport failures should be translated into deterministic `StatusCode` results; cancellation requested by the caller should normally remain cancellation, while an executor-owned timeout should become `BadTimeout`.

The projection runtime opens channels lazily, except for the source channel required during transparent event preparation. One `WotBindingChannelSlot` is shared for each compiled-form object within a generation, concurrent first use opens exactly once, a failed open is evicted for retry, and one caller's cancellation does not cancel the generation-scoped open for other callers. Disposal marks the slot closed before awaiting an in-flight open, then disposes any successfully created channel. Channel disposal must be idempotent, and subscription disposal must stop delivery and release its transport resources.

Do not create transport connections in the planner, binder constructor, or DI registration callback unless the executor itself explicitly owns a long-lived pooled client. Prefer an injectable client/session factory in options, as the built-in executors do.

### Payload codecs

The default `WotPayloadCodecRegistry` contains reflection-free JSON, text, and octet-stream codecs. A planner records only the codec id and payload metadata; a channel selects the codec from `WotExecutorContext.Codecs` when it encodes or decodes.

Custom codecs implement `IWotPayloadCodec` and return `WotEncodeResult` or `WotDecodeResult` rather than throwing for expected malformed input. Register custom codecs ahead of the built-ins with `WotPayloadCodecRegistry.Register`, or provide an `IWotCodecRegistry` through DI. Keep codecs deterministic, bounded, culture-invariant, and free of runtime type discovery.

### Credentials and trust

Thing Descriptions and registry nodes contain only `WotSecurityDefinition` and `WotCredentialReference` data. Actual headers, query values, usernames, passwords, certificates, and trust anchors are resolved at channel activation or request time through `IWotCredentialProvider`.

Register a provider with `AddWotCredentialProvider`. Scope credentials by the reference's scheme name, binding URI, and endpoint. Fail closed when a form declares security but the provider cannot resolve the required material. Never serialize `WotCredential`, cache secret text in `WotCompiledForm`, or include secrets in diagnostics.

### Endpoint policy and custom schemes

`WotEndpointPolicy` is an allow-list that decides which endpoint URIs an executor may reach. It fails closed: the default set covers only the schemes the shipped bindings use (`http`, `https`, `modbus+tcp`, `modbus`, `mqtt`, `mqtts`, `opc.tcp`, `opc.https`, `opc.wss`), and it blocks loopback, RFC1918, CGNAT, link-local (including the cloud metadata address `169.254.169.254`) and IPv6 ULA ranges.

Executable MQTT and Modbus network forms require an explicit, nonempty host.
Hostless forms do not imply localhost. Their executors validate the effective
destination against the operation's endpoint policy before resolving credentials
or creating a connection, including when invoked directly without registry
activation.

A custom binding almost always introduces a scheme the default set does not know about, so opening a channel fails with `BadSecurityChecksFailed` and `Endpoint scheme '<scheme>' is not in the policy's AllowedSchemes set` until the scheme is opted in:

```csharp
var endpointPolicy = new WotEndpointPolicy();
endpointPolicy.AllowedSchemes.Add("mem");
```

Add only the scheme your binding needs, and leave the address-range restrictions alone unless the deployment requires them relaxed — those blocks stop a Thing Description from steering an executor at the host's own listeners or at a cloud metadata endpoint.

#### Internationalized hosts

A `href` may name an internationalized host (`http://ü.example/x`). Percent-encoding is defined for a path, a query and a fragment and is **not** a spelling of a host, so the transmitted URI is rebuilt from its components rather than encoded as one string: the host becomes its IDNA A-label (`http://xn--tda.example/x`), and userinfo, an explicit port and an IPv6 literal are carried through unchanged. `WotProtocolBinderBase.ToTransmittedUri` produces the URI on the wire and `ToTransmittedAuthority` the authority the plan is scoped to, so `WotCompiledForm.Endpoint.Host`, `Endpoint.BaseUri`, `Addressing.Target` and every `WotCredentialReference.Endpoint` name one host.

`WotEndpointPolicy` is evaluated against the same A-label — `WotEndpointValidator.ToAsciiHost` exposes it. An allow list accepts either spelling of one name; a block list refuses either, because a policy that blocks `xn--tda.example` while the plan carries `ü.example` would block nothing.

### Registration

The direct-construction path is useful in focused tests. The policy passed alongside the binder and executor lets the sample's `mem://` endpoints resolve:

```csharp
var store = new MemoryWotStore();

var endpointPolicy = new WotEndpointPolicy();
endpointPolicy.AllowedSchemes.Add("mem");

var registry = new WotProtocolBinderRegistry(
    [new MemoryWotBinder()],
    [new MemoryWotBindingExecutor(store)],
    endpointPolicy: endpointPolicy);
```

The normal host path uses `IOpcUaBuilder` extensions:

```csharp
MemoryWotStore store = new();

IOpcUaBuilder opcUa = services
    .AddOpcUa()
    .AddServer(server => { /* server configuration */ })
    .AddWotRegistryServer(options => options.StrictBindings = false);

opcUa
    .AddWotBinder(new MemoryWotBinder())
    .AddWotBindingExecutor(new MemoryWotBindingExecutor(store))
    .AddWotCredentialProvider(NullWotCredentialProvider.Instance);
```

`EnsureWotBinderRegistry` registers one `WotProtocolBinderRegistry` singleton and exposes that same instance as both `IWotBinderRegistry` and `IWotBindingChannelFactory`, independent of registration order. A custom binding package should expose one fluent `Add<Protocol>WotBinding` method that creates options, calls `AddWotProtocolBinders` or `AddWotBinder`, and registers its executor.

### Monitoring and local sampling

For a target-mapped variable, the generic projection runtime wires executable `readproperty` and `writeproperty` forms to async `OnRead` and `OnWrite` handlers. When the same authored form provides `readproperty` and `observeproperty`, local OPC UA monitored items sample the read handler without a second upstream subscription. Distinct or observe-only forms use the subscription bridge and cache described under [Property observation and contextual values](#property-observation-and-contextual-values).

Outside target mapping, callers can use `IWotBindingChannel.ObserveAsync` or `SubscribeEventAsync` directly. The returned `IWotSubscription` owns the native subscription or polling loop and must stop it in `DisposeAsync`.

### Structured target mapping

Direct mapping reads or writes the whole target value. Structured mapping groups forms by target variable and field path. Reads run all mapped field reads concurrently, build nested `IStructure` instances without reflection, and return one `ExtensionObject`. Writes extract each mapped field and run the field writes concurrently.

The runtime rejects a target that mixes direct and field mappings, duplicate read mappings for the same field, duplicate write mappings for the same field, and target-mapped operations other than read, write, or observe. A failed field fails the entire structured operation. A successful structured read preserves a non-default Good status when present and uses the oldest available source timestamp.

Structure type and field-path resolution is delayed until first structured use because runtime NodeSet configuration completes before custom encodeable types are registered in the shared factory. Failed resolution is not cached; later operations retry. Until resolution succeeds, the read or write returns `BadConfigurationError`.

### Status and error mapping

Return a `WotReadResult`, `WotWriteResult`, or `WotInvokeResult` for expected protocol outcomes. Reserve exceptions for invalid API use, cancellation, resource construction failures, and conditions that prevent a channel from being opened.

| Condition | Recommended status |
| --- | --- |
| Unsupported channel operation | `BadNotSupported` |
| Payload encode/decode failure | `BadEncodingError` / `BadDecodingError` |
| Executor-owned timeout | `BadTimeout` |
| Network or broker failure | `BadCommunicationError` |
| Missing protocol target | `BadNodeIdUnknown` or a protocol-specific mapped status |
| Invalid compiled address | `BadNodeIdInvalid` or `BadInvalidArgument` |
| Authentication or authorization rejection | `BadUserAccessDenied` |
| Response exceeds configured bounds | `BadEncodingLimitsExceeded` |
| Invalid runtime mapping or structured configuration | `BadConfigurationError` |

Preserve a source protocol's meaningful OPC UA status and timestamps when the source is OPC UA. Do not expose credentials or stack traces through `Error`; use concise operator-safe text and server-side telemetry for detailed exceptions.

### Memory-binding implementation

The following excerpt is the checked-in sample implementation pattern. It supports `mem://` property read, write, and polling-based observation. Use the linked source file as the authoritative copy if this excerpt is trimmed in rendered documentation.

```csharp
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.WotCon;
using Opc.Ua.WotCon.Bindings;

namespace AggregationServer
{
    /// <summary>
    /// A worked sample showing how a third party contributes a replaceable
    /// protocol binder as pure code-behind. The fictitious <c>mem</c> protocol
    /// binds property affordances to an in-process key/value store, demonstrating
    /// the full extension surface: identity, capability, deterministic
    /// identification, a planner and an executor with a live channel. Register it
    /// with <c>builder.AddWotBinder(new MemoryWotBinder())</c> and
    /// <c>builder.AddWotBindingExecutor(new MemoryWotBindingExecutor(store))</c>.
    /// </summary>
    public sealed class MemoryWotBinder : WotProtocolBinderBase
    {
        /// <summary>
        /// The sample binding vocabulary URI.
        /// </summary>
        public const string BindingUri = "urn:example:wot:mem";

        private static readonly string[] s_schemes = ["mem"];

        /// <inheritdoc/>
        public override WotBindingIdentity Identity { get; } =
            new WotBindingIdentity("example.mem", "1.0", BindingUri, "Sample In-Memory Binding");

        /// <inheritdoc/>
        public override WotBindingCapability Capability { get; } = new WotBindingCapability(
            BindingUri,
            "Sample In-Memory Binding",
            new WotBindingSource("urn:example:wot:mem", "1.0", WotBindingMaturity.UnofficialDraft,
                note: "A sample custom binding for documentation and tests."),
            [
                WoTBindingCapabilityEnum.ReadProperty,
                WoTBindingCapabilityEnum.WriteProperty,
                WoTBindingCapabilityEnum.ObserveProperty
            ],
            ["application/json", "text/plain"],
            isExecutable: true);

        /// <inheritdoc/>
        protected override IReadOnlyCollection<string> Schemes => s_schemes;

        /// <inheritdoc/>
        public override WotBindingMatch Match(WotAffordanceForm form, WotBindingSelectionContext context)
        {
            return MatchStandard(form, context, "memv:");
        }

        /// <inheritdoc/>
        public override WotBindingCompilation Compile(WotAffordanceForm form, WotBindingPlanContext context)
        {
            var diagnostics = new List<WotBindingDiagnostic>();
            if (!RequireHref(form, context, diagnostics, out string href) ||
                !TryParseUri(href, out Uri uri) ||
                !string.Equals(uri.Scheme, "mem", StringComparison.OrdinalIgnoreCase))
            {
                diagnostics.Add(WotBindingDiagnostic.Error(
                    WotBindingDiagnosticCode.InvalidHref,
                    "The href is not a valid mem:// URI.", form.Pointer("href")));
                return WotBindingCompilation.Unsupported([.. diagnostics]);
            }

            string key = uri.AbsolutePath.Trim('/');
            ResolveCodec(form, context, out WotPayloadDescriptor payload);
            WotEndpointDescriptor endpoint = MakeEndpoint(uri);
            var addressing = new WotAddressingDescriptor(key);

            ImmutableArray<WotCompiledForm>.Builder entries = ImmutableArray.CreateBuilder<WotCompiledForm>();
            foreach ((string op, WoTBindingCapabilityEnum capability) in ResolveOperations(form, diagnostics))
            {
                var operation = new WotOperationDescriptor(capability, op, capability.ToString());
                entries.Add(new WotCompiledForm(
                    Identity, form.Kind, form.AffordanceName, form.JsonPointer, capability, op,
                    endpoint, addressing, operation, payload,
                    [], Capability.IsExecutable));
            }

            return entries.Count == 0
                ? WotBindingCompilation.Unsupported([.. diagnostics])
                : WotBindingCompilation.Supported(entries.ToImmutable(), [.. diagnostics]);
        }
    }

    /// <summary>
    /// The in-process key/value store the sample binding reads and writes.
    /// </summary>
    public sealed class MemoryWotStore
    {
        /// <summary>
        /// Gets the value stored under a key.
        /// </summary>
        public DataValue Get(string key)
        {
            return m_values.TryGetValue(key, out DataValue value) ? value : new DataValue(Variant.Null);
        }

        /// <summary>
        /// Sets the value stored under a key.
        /// </summary>
        public void Set(string key, DataValue value)
        {
            m_values[key] = value;
        }

        private readonly ConcurrentDictionary<string, DataValue> m_values =
            new(StringComparer.Ordinal);
    }

    /// <summary>
    /// The executor for the sample in-memory binding.
    /// </summary>
    public sealed class MemoryWotBindingExecutor : IWotBindingExecutor
    {
        /// <summary>
        /// Initializes a new sample executor over the supplied store.
        /// </summary>
        public MemoryWotBindingExecutor(MemoryWotStore store)
        {
            m_store = store ?? throw new ArgumentNullException(nameof(store));
        }

        /// <inheritdoc/>
        public WotBindingIdentity Identity { get; } =
            new WotBindingIdentity("example.mem", "1.0", MemoryWotBinder.BindingUri, "Sample In-Memory Executor");

        /// <inheritdoc/>
        public bool CanExecute(WotCompiledForm form)
        {
            return form is not null && string.Equals(form.Binding.Id, Identity.Id, StringComparison.Ordinal);
        }

        /// <inheritdoc/>
        [System.Diagnostics.CodeAnalysis.SuppressMessage(
            "Reliability", "CA2000:Dispose objects before losing scope",
            Justification = "The channel is owned by the caller, who disposes it via DisposeAsync.")]
        public ValueTask<IWotBindingChannel> ActivateAsync(
            WotCompiledForm form, WotExecutorContext context, CancellationToken cancellationToken = default)
        {
            if (form is null)
            {
                throw new ArgumentNullException(nameof(form));
            }
            IWotBindingChannel channel = new MemoryWotBindingChannel(m_store, form);
            return new ValueTask<IWotBindingChannel>(channel);
        }

        private readonly MemoryWotStore m_store;
    }

    /// <summary>
    /// The live channel for the sample in-memory binding.
    /// </summary>
    internal sealed class MemoryWotBindingChannel : IWotBindingChannel
    {
        public MemoryWotBindingChannel(MemoryWotStore store, WotCompiledForm form)
        {
            m_store = store;
            Form = form;
            m_key = form.Addressing.Target;
        }

        public WotCompiledForm Form { get; }

        public ValueTask<WotReadResult> ReadAsync(CancellationToken cancellationToken = default)
        {
            return new ValueTask<WotReadResult>(new WotReadResult(StatusCodes.Good, m_store.Get(m_key)));
        }

        public ValueTask<WotWriteResult> WriteAsync(DataValue value, CancellationToken cancellationToken = default)
        {
            m_store.Set(m_key, value);
            return new ValueTask<WotWriteResult>(new WotWriteResult(StatusCodes.Good));
        }

        public ValueTask<WotInvokeResult> InvokeAsync(
            IReadOnlyList<Variant> inputs, CancellationToken cancellationToken = default)
        {
            return new ValueTask<WotInvokeResult>(new WotInvokeResult(
                        StatusCodes.BadNotSupported, null, "The sample binding has no actions."));
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage(
            "Reliability", "CA2000:Dispose objects before losing scope",
            Justification = "Ownership of the subscription is transferred to the caller, who disposes it.")]
        public ValueTask<IWotSubscription> ObserveAsync(
            Action<WotNotification> onNotification, CancellationToken cancellationToken = default)
        {
            if (onNotification is null)
            {
                throw new ArgumentNullException(nameof(onNotification));
            }
            var subscription = new PollingWotSubscription(Form, token =>
            {
                onNotification(new WotNotification(m_store.Get(m_key)));
                return new ValueTask<bool>(true);
            }, TimeSpan.FromMilliseconds(200));
            return new ValueTask<IWotSubscription>(subscription);
        }

        public ValueTask<IWotSubscription> SubscribeEventAsync(
            Action<WotNotification> onEvent, CancellationToken cancellationToken = default)
        {
            return ObserveAsync(onEvent, cancellationToken);
        }

        public ValueTask DisposeAsync()
        {
            return default;
        }

        private readonly MemoryWotStore m_store;
        private readonly string m_key;
    }
}
```

### Memory-binding tests

The positive test compiles a TD, selects the generated read and write entries, opens channels through the registry, and verifies round-trip behavior:

```csharp
[Test]
public async Task SampleBinderCompilesAndExecutesReadWrite()
{
    var store = new MemoryWotStore();
    var registry = new WotProtocolBinderRegistry(
        [new MemoryWotBinder()],
        [new MemoryWotBindingExecutor(store)]);

    const string td =
        """
        {
          "@context": "https://www.w3.org/2022/wot/td/v1.1",
          "title": "Memory device",
          "properties": {
            "setpoint": {
              "type": "number",
              "forms": [{ "href": "mem://store/setpoint" }]
            }
          }
        }
        """;

    WotBindingPlan plan = registry.Prepare(
        WotBindingPlanRequest.FromDocument(
            "memory-device",
            WoTDocumentKindEnum.ThingDescription,
            Encoding.UTF8.GetBytes(td)));

    Assert.That(plan.FullySupported, Is.True);
    Assert.That(plan.HasExecutableForms, Is.True);

    WotCompiledForm write = plan.CompiledForms.Single(
        form => form.Operation == WoTBindingCapabilityEnum.WriteProperty);
    WotCompiledForm read = plan.CompiledForms.Single(
        form => form.Operation == WoTBindingCapabilityEnum.ReadProperty);

    IWotBindingChannel writeChannel = await registry.OpenChannelAsync(write);
    await using (writeChannel.ConfigureAwait(false))
    {
        WotWriteResult result = await writeChannel.WriteAsync(
            new DataValue(new Variant(42.5)));
        Assert.That(result.Success, Is.True);
    }

    IWotBindingChannel readChannel = await registry.OpenChannelAsync(read);
    await using (readChannel.ConfigureAwait(false))
    {
        WotReadResult result = await readChannel.ReadAsync();
        Assert.That(result.Success, Is.True);
        Assert.That(result.Value.WrappedValue.AsBoxedObject(), Is.EqualTo(42.5));
    }
}
```

Add a diagnostic test so malformed input remains actionable:

```csharp
[Test]
public void SampleBinderReportsInvalidHrefAtTheFormPointer()
{
    var registry = new WotProtocolBinderRegistry(
        [new MemoryWotBinder()]);

    const string td =
        """
        {
          "title": "Invalid memory device",
          "properties": {
            "setpoint": {
              "forms": [{ "href": "mem://[invalid" }]
            }
          }
        }
        """;

    WotBindingPlan plan = registry.Prepare(
        WotBindingPlanRequest.FromDocument(
            "invalid-memory-device",
            WoTDocumentKindEnum.ThingDescription,
            Encoding.UTF8.GetBytes(td)));

    Assert.That(plan.FullySupported, Is.False);
    Assert.That(
        plan.Diagnostics.Any(d =>
            d.Code == WotBindingDiagnosticCode.InvalidHref &&
            d.JsonPointer == "/properties/setpoint/forms/0/href"),
        Is.True);
}
```

The checked-in equivalent is [`WotCustomBinderSampleTests.cs`](../tests/Opc.Ua.WotCon.Tests/Binding/WotCustomBinderSampleTests.cs). Protocol executor tests belong in [`tests/Opc.Ua.WotCon.Bindings.Tests`](../tests/Opc.Ua.WotCon.Bindings.Tests), while planner, registry, target-mapping, and materialization tests belong in [`tests/Opc.Ua.WotCon.Tests`](../tests/Opc.Ua.WotCon.Tests).

### NativeAOT and trimming

Binding code must remain compatible with trimming and NativeAOT. Parse form vocabulary with `JsonElement`; do not use runtime assembly scanning, unbounded reflection, `Type.GetType`, dynamic code generation, or serializer overloads that require runtime metadata. Use source-generated JSON contexts when a protocol needs typed JSON beyond the built-in scalar codec.

Keep plan objects data-only and immutable. Inject transport factories and credential providers instead of locating services dynamically. Ensure asynchronous cleanup does not depend on finalizers. If a dependency is not annotated as AOT-compatible, add a NativeAOT smoke path that exercises every used feature.

The base Bindings project sets `IsAotCompatible` for compatible `net10.0` builds, and the aggregation samples publish with `PublishAot` on `net10.0`. Validate a new concrete executor with a `net10.0` build and, when it participates in a sample or app, a real `dotnet publish -f net10.0 -r <rid>`.

### Packaging and TFM decisions

Keep protocol abstractions and planners in the base Bindings project when they can compile across the full library matrix without a transport dependency. Place a concrete executor in the base project only when its dependencies are already suitable for the bundled `net8.0+` build, as with HTTP, Modbus TCP, and OPC UA. Use a separate package when the executor introduces an optional external dependency, as MQTT does.

Conditionally exclude executor source on older TFMs rather than reducing the base package's TFM matrix. Public documentation and package README files must state both facts: the package is available on all library TFMs, and the concrete executor namespaces exist only on `net8.0+`.

### Contributor checklist

- [ ] Use plural `Opc.Ua.WotCon.Bindings` project, package, and namespace names.
- [ ] Pin an authoritative binding source and version in `WotBindingSource`.
- [ ] Choose a stable binder id, version, binding URI, display name, and capability set.
- [ ] Implement deterministic identification and verify tie/pin behavior.
- [ ] Validate required vocabulary, conflicts, bounds, operations, content types, and security references without transport I/O.
- [ ] Emit immutable compiled forms with precise endpoint, address, operation, payload, credential, and JSON Pointer data.
- [ ] Leave OPC 10101 target mapping to the protocol-neutral registry/runtime.
- [ ] Implement an executor only for operations the transport can actually perform.
- [ ] Map expected failures to OPC UA status codes and keep caller cancellation distinct from executor timeout.
- [ ] Resolve credentials out of band and verify that diagnostics never contain secrets.
- [ ] Opt the binding's URI scheme into `WotEndpointPolicy.AllowedSchemes` and leave the address-range blocks intact.
- [ ] Make channels, subscriptions, and in-flight activation safe under asynchronous disposal.
- [ ] Register direct-construction and DI/fluent paths.
- [ ] Add planner, diagnostics, executor, concurrency, disposal, and security tests.
- [ ] Test local monitored-item sampling when the binding is used through target mapping.
- [ ] Test direct and structured mappings when the protocol is intended for aggregation.
- [ ] Verify all supported TFMs, `net10.0` trimming/AOT behavior, package contents, and README accuracy.

### Testing matrix

| Area | Required cases |
| --- | --- |
| Identification | Scheme match, vocabulary match, explicit pin, no match, deterministic tie, multiple binder versions. |
| Form extraction | Default operations, form operation override, Thing-level security fallback, relative/base URI behavior if supported, formless affordance. |
| Planner validation | Valid form, missing/invalid href, incompatible operation, unsupported content type, missing term, invalid term shape/range, conflicting terms, configured bounds. |
| Diagnostics | Stable code, severity, offending term, exact RFC 6901 form or affordance pointer, no secret leakage. |
| Compiled plan | Endpoint, addressing, operation, payload, security references, target mapping, executable/non-executable state. |
| Codec | Encode/decode round trip, malformed payload, empty payload, maximum payload, culture independence. |
| Credentials | No-security path, missing required credential, correct endpoint scoping, secure transport, explicit rejection of unsafe downgrade. |
| Executor | Read, write, invoke, observe, event, every intentionally unsupported operation, source status/timestamp preservation. |
| Failure mapping | Timeout, cancellation, connection loss, protocol error, encode/decode failure, authentication failure, oversized response. |
| Concurrency | Concurrent first channel use opens once, failed open retries, parallel operations obey transport rules. |
| Disposal | Never-opened channel, successfully opened channel, failed open, in-flight open racing disposal, subscription partial-construction failure, repeated disposal. |
| Target mapping | Affordance-level direct mapping, `nsu=` mapping, forms-level rejection, action/event rejection, field path requires type, direct/field conflict, duplicate field direction. |
| Structured mapping | Nested fields, unknown field, non-structure intermediate, array-valued intermediate, one failed field, status/timestamp aggregation. |
| Materialization | Strict rejection, non-strict degradation, successful activation, failed shadow replacement retaining old generation, old monitored-item drain. |
| Packaging | Full base TFM matrix, executor source absent before `net8.0`, MQTT separate package, package README and dependency graph. |
| AOT/trimming | `net10.0` analyzer-clean build and NativeAOT publish/run smoke test for the concrete executor path. |

## Related documentation

* [WoT Connectivity model, server, registry, and client](WotCon.md)
* [WoT aggregation sample](../samples/WotCon/README.md) - exercises the complete generic projection runtime with two OPC UA source servers, runtime-loaded DI/Machinery/Pumps models, local monitored items, and shadow-generation replacement.
* [Dependency injection](DependencyInjection.md)
* [Runtime NodeSets](RuntimeNodeSets.md)

<a id="conformance-and-authoring-validation"></a>

<a id="conformance-to-opc-10101-1-wot-binding"></a>

## Conformance to OPC 10101 (Binding and Projections)

### OPC 10101-1 (WoT Binding)

The specification defines eleven conformance units and four recommended profiles
(Section 11). This is where the implementation stands against them.

| Unit | Status | Where |
|---|---|---|
| **WoT-ProtocolBinding** | covered | URI/base/href handling, the four service mappings, access levels, the security schemes and the `auto` endpoint-selection constraint of Section 5.7.1, in `Opc.Ua.WotCon.Bindings` and its planners |
| **WoT-NativeMapping** | covered | `WotNodeSetConverter`, including the proof that `uav:nodes` is omitted when the readable mapping is complete. It descends the whole composition tree (`FromNodeSetDocuments`, §9.1's "Thing / nested Thing"), seeds namespaces from `@context`, and keeps type definitions, DataTypes and scalar values. See *What the readable mapping cannot express* below |
| **WoT-StructuredFallback** | covered | the structured `uav:nodes` projection in `WotNativeProjection` |
| **WoT-JsonResidue** | covered | `WotJsonResidue`, pointer-addressed preservation through the NodeSet Extension |
| **WoT-NodeSetPreservation** | covered | the byte-exact `uav:nodeSet` envelope with digest verification |
| **WoT-ExactRoundtrip** | covered | the envelope-free roundtrip invariants, including residue |
| **WoT-EventMapping** | covered | `subscribeevent` / `unsubscribeevent` mapped to event MonitoredItems, including the EventType `tm:ref` fast path, the `uav:eventSelectClauses` overlay and the implicit `BaseEventType` default (Section 6.1) |
| **WoT-ConditionMapping** | covered | Section 13 (`uav:conditionType`, `uav:conditionTypeId`, `uav:conditionAction`, `uav:actsOn`) in `WotNodeSetConverter.Conditions`, with the Condition supertype resolution and the Section 13.3/13.4 conformance rules |
| **WoT-ModelVocabulary** | covered | `WotNodeSetConverter.ModelVocabulary` and `WotNodeSetConverter.Conformance`, all Section 6 terms with their validation rules |
| **WoT-DataTypeDefinition** | covered | `WotNodeSetConverter.DataTypes`, the explicit and inferred DataType definitions of Section 6.11 |
| **WoT-ExternalResolver** | covered | `WotResolver` for `uav:externalSchema`, `uav:mapToType`, `uav:mapToNodeId` and cross-document links |

The table identifies implementation paths, not certification of every requirement
in **WoT-Reader**, **WoT-Modeller**, **WoT-Converter**, or
**WoT-ArchivalConverter**. Full TD/TM JSON Schema and compatibility-policy validation
remain separate unfinished work; successful conversion does not close those gaps.

The three profiles beyond **WoT-Reader** include the part-qualified
`2:WoT-Projection` dependency from OPC 10101-2. Their projection dependency is
subject to the mandatory validation gap described below.

The WoT Binding unit and profile names are stated once in
`Opc.Ua.Wot.WotBindingConformance`, together with the vocabulary revision this
library implements (`CurrentRevision`, `1.1`) and the profile nesting Section 11
defines. The cross-part projection dependency is kept separately qualified in
those profile closures. A document declares what it claims with `uav:profile` and
the revision it was authored against with `uav:bindingVersion` (Section 4.1);
both are validated, neither becomes a Node, and both are restated verbatim on a
round trip.

`ConformanceMode` defaults Permissive; Strict reports unknown terms and unmet
required claims. `AuthoringValidation` separately requires known published
revision/unit/profile names for authored claims. Consumers preserve well-formed
unknown claims/revisions with warnings; malformed claims fail in every mode.
Opaque metadata/configuration objects retain their own 65,536 compact-received
UTF-8-byte, depth-32, and 256-top-level-key bounds. The `uav:nodes` record grammar
has a separate namespace and is not readable Binding vocabulary.

### OPC 10101-2 (WoT Projections)

The current official draft defines one conformance unit, **WoT-Projection**.
It is qualified as `2:WoT-Projection` in Binding profile closures, not counted
among Part 1's eleven units. The stack has the following implementation and
regression-test paths; these are partial evidence, not a complete requirement
audit or executable CTT result.

| Contract | Implementation | Existing evidence |
| --- | --- | --- |
| Ordinary TD/TM plan envelopes and result roles | `WotProjection`, registry admission | `WotProjectionPlanFormatTests.ResolvedProjectionHasTheDeclaredOutputKindAndNoPlanControl` |
| Ordered selection, enumerated-over-bulk precedence and local overrides | `WotProjectionResolver` | `WotProjectionResolverTests.EnumeratedSelectionWinsOverBulkAndReportsDrop`, `LocalAffordancesOverrideSelectionsAndRetainOrdinaryReferencesAsync` |
| Source routing, provenance, effective context and security ownership | `WotProjectionResolver` and its dependency closure | `WotProjectionResolverTests.SourceRoutingAbsolutizesFormAndCopiesSecurityClosure`, `BulkProvenanceIsHrefPlusPointer`, `WotProjectionSecurityTests.LocalAffordanceSecurityRetainsProjectionOwnershipAsync` |
| Canonical Views over existing local/imported Nodes, stable identities and owned-node counts | `WotProjectionViewBuilder`, `LifecycleWotViewProjectionHost`, materialization coordinator | `WotProjectionViewCanonicalLiveTests.ProjectionLocalAffordancesJoinImportedMembersAndOverrideCollisionsAsync`, `ProjectionGeneratedLocalIdentitiesAndCountsRemainResourceScopedAsync` |
| Distinct organizational occurrences, shared canonical roots and cycle rejection | View host and projection dependency resolver | `WotProjectionViewCanonicalLiveTests.CanonicalCandidateRendersSharedChildAndTypedWrappersOverTransportAsync`, `WotProjectionResolverTests.AGroupThatOrganizesItsOwnOrganizerIsACycleAsync` |
| Complete base TD/TM and Binding validation of the resolved result | **Missing mandatory behavior** | No complete validator is integrated; parsing, conversion and projection regression tests do not satisfy this requirement |

The resolver-side test names are also recorded under part-qualified clause
anchors in the stack-side
[traceability locator](../tests/Opc.Ua.Types.Tests/Wot/Assets/wot-binding-traceability.json).
The View tests are in
[`WotProjectionViewCanonicalLiveTests`](../tests/Opc.Ua.WotCon.Tests/Materialization/WotProjectionViewCanonicalLiveTests.cs).
The locator checks that named tests exist and run; it does not establish that
every requirement or conformance-unit assertion is covered.

Full **WoT-Projection** conformance is **not established**. Complete result
validation remains a mandatory implementation blocker pending runtime dependency
approval, license/distribution review and NativeAOT-compatible integration.
This also prevents claiming the Part 1 **WoT-Modeller**, **WoT-Converter** and
**WoT-ArchivalConverter** profile closures that require `2:WoT-Projection`.
View and recovery tests do not close that validation gap.

The separate Connectivity **WOTC-ProjectionMaterialization** unit adds the
registry/server lifecycle contract. Neither that unit nor the **WoT-Con Full**
profile is established by the Part 2 resolver and View evidence alone; see
[Connectivity conformance](WotCon.md#12-conformance-to-wot-connectivity-11)
for its independent gaps and claim boundaries.

### Conversion entry points

Use async conversion for linked declarations, event selections, and documents:

```csharp
WotConversionResult<UANodeSet> result = await WotNodeSetConverter.ToNodeSetResultAsync(
    document, options, thingResolver, cancellationToken: ct);
if (!result.Success)
{
    throw new InvalidOperationException(string.Join(Environment.NewLine, result.Diagnostics));
}
UANodeSet nodeSet = result.Value!;
```

Synchronous entry points perform no linked resolution; unresolved stated event
selection reports `EventSelectionUnresolved`. Resolution requires configured
providers and budgets, not permission inferred from an HTTP-shaped identifier.
`ResolveAffordanceNodes` checks actual local identity/class/declaration ownership;
a missing explicit identity is not a name fallback.

`FromNodeSetDocumentsAsync` reconstructs and verifies the whole source before
returning a `WotDocumentSet`; synchronous export returns a candidate, not that
guarantee. Partition roots own disjoint Nodes and complete authoritative native
graphs, not readable overlays. Duplicate ownership/conflicting headers fail.
`MergeNodeSetPartitions` applies bounded rules without mutating inputs; shared
declarations can join activation closures, but a namespace spelling alone cannot.

### Independent readable models

`DocumentSetMode` defaults to `PartitionReconstruction`. Explicitly opt in when
independent readable documents have different local namespace tables:

```csharp
var options = new WotNodeSetConverterOptions
{
    DocumentSetMode = WotDocumentSetMode.IndependentReadableModels,
    ValueEncodingContext = messageContext
};
WotConversionResult<UANodeSet> result = await WotNodeSetConverter.ToNodeSetAsync(
    documents, options, nodeResolver: resolver, cancellationToken: ct);
```

The merged URI table distinguishes equal local IDs from different models and
normalizes Nodes, BrowseNames, references, aliases, permissions, declarations,
and definition fields. Conflicting model/RequiredModel facts, ownership, or
server tables fail. Native/archive inputs retain strict reconstruction rules;
failure never retries automatically in independent mode.
Namespace-bearing XML values decode/re-encode through registered codecs.
Unknown fields/indexes, lossy/incomplete decoding, and necessary rebasing of
opaque bodies fail `NamespaceRebaseUnsupported`. Caller tables/factory remain
unchanged; typed null/empty values stay distinct. Bounds/cancellation apply to
preconverted inputs and final output.

Registry options expose the same mode at
`OpcUa:WotConRegistry:Server:DocumentSetMode`; DI forwards optional
`IServiceMessageContext`. The mode participates in refresh fingerprints.
Codec-registration changes require an explicit forced refresh, not wider activation.

### Identity and preservation

Annex G.1 generates unspecified IDs as `nsu=<NamespaceUri>;s=<qualified-path>`,
with Annex A escaping. Authored portable identities win; duplicates fail.
`WotNodeSetPreservationMode` defaults `WhenRequired` (readable first, then complete
structured `uav:nodes`); `Always` archives bytes in `uav:nodeSet`; `Never` forbids
fallback. Native/archive facts remain authority: readable assertions must agree,
and missing assertions do not authorize synthesis over preserved Nodes.

Unmapped JSON is pointer-addressed `WoTJsonResidue`, not a duplicate document.
Opaque values retain received number spellings/escapes/order/whitespace; conflicts
are diagnosed on reverse conversion. Metadata/literal values are not definition
indexes or fetch requests. A received BOM stays in exact bytes/digests/limits,
not canonical JSON. SHA-256 preservation pins received bytes; RFC 8785 compares
supported values without rounding excess precision into false equality.
Opaque-size bounds compact whitespace outside strings only.

### DataTypes, ranks, arguments, and model vocabulary

Unannotated booleans/strings map to Boolean/String; integer/number to abstract
Integer/Number. Native pins override inference. Content encoding `base64`
refines to ByteString; formats `date-time`, `uuid`, and `uri` refine to DateTime,
Guid, and UriString.
`uav:dataTypeDefinitions` carries one complete definition per graph identity;
later occurrences are references. Multi-field inference requires field order,
Unions require explicit intent, and missing concrete widths/custom bases fail.
Concrete Structures expose Binary/XML/JSON encodings unless an eligible embedded
type opts out. Abstract types cannot author encoding identities.

`uav:valueRank` retains -3, -2, -1, 0, and positive ranks; fixed ArrayDimensions
must match. Named argument order and whole-value layouts follow the action
contract above. Unavailable native argument values retain their source Nodes.
Inherited event fields are not redeclared; ConditionId is an Attribute, and extra
TD event declarations need a subtype. Severity is occurrence data, not synthesized
`uav:severity` metadata. OptionalPlaceholder is `i=11508`;
MandatoryPlaceholder is `i=11510`. Unmapped capability/component/reference terms
stay residue rather than fabricating relationships.

### Engineering units, ranges, and localized text

EngineeringUnits/EURange/InstrumentRange map when values decode.
`uav:engineeringUnits` preserves EUInformation authority/unitId/display text;
`uav:unitProperty` identifies its sibling unit property. Only a valid min/max pair
maps EURange; InstrumentRange must contain it. Machine limits invent no range.
`uav:scaleFactor`/`uav:decimalPlaces` stay annotations, not derived values;
these terms belong on affordances, not payload fields. `unit` is a display unit,
not a quantity kind.

Plural `titles`/`descriptions` retain all locales; singular fallback uses the
effective default locale or code-point-first language. Local/scoped/null-reset
contexts apply. Missing/contradictory fallback fails `InvalidLocalizedText`;
extra locales beyond native single-translation fields use bounded residue.
Use the versioned Binding context: a `uav` prefix alone does not define short
engineering-unit members.

### What the readable mapping does not yet carry

Readable mappings and their fallback conditions are maintained in
[WoT / NodeSet conversion](WotNodeSetConversion.md). In particular,
[engineering units, ranges and scaling](WotNodeSetConversion.md#engineering-units-ranges-and-scaling-sections-64-and-641)
are mapped in both directions when their values can be decoded. Missing or
unsupported values use the documented preservation projection rather than
invented defaults.

Use this guide for protocol planners, executors, and binding registration;
use [WoT Connectivity](WotCon.md) for registry hosting and projection
lifecycle. Keeping conversion rules in one place avoids binding implementations
depending on an outdated list of mapping limitations.

Equivalence and byte identity are different contracts. `NodeSetComparer.CompareEquivalent`
resolves each NodeSet's aliases, while `NodeSetComparer.Compare` retains stricter
text comparison. See the conversion guide for readable, preservation, and exact
round-trip guarantees.

### How this is checked

The `.jsonld` examples under `tests/Opc.Ua.Types.Tests/Wot/Assets` are mirrored byte-for-byte from the official [OPCF-Members/OPC10100-WoT](https://github.com/OPCF-Members/OPC10100-WoT) Binding specification at commit `0b06c3e3b5d6a427193a17321abb6d1da4a2bae0`. UA-only regression assets are retained alongside them. `spec-examples.manifest.json` records the upstream coordinates and the SHA-256 and length of every numbered example; tests verify those bytes offline. `.gitattributes` preserves LF on checkout. To synchronize, copy every `*.jsonld` recursively from `source/WoT-Binding` by filename without transforming it, update the baseline and hashes in the manifest, and run the WoT tests.

The shared reference-resolution corpus at xRegistry spec commit `cc2d4c4137a2745dc91beb416578ee60a8b1d555` is mirrored without changes into `tests/Opc.Ua.Types.Tests/Wot/Resolution`. Its `MANIFEST.sha256` covers the vectors, schema, README and Python reference checker. `WotVersionResolverTests.CanonicalManifestMatchesEveryFile` verifies complete manifest coverage, hashes and LF, and `CanonicalVectorMatches` executes every vector through the C# resolver and publisher.

### Versioned document references

`WotVersionResolver` selects a Thing Model version from caller-supplied resource snapshots. A digest or version-id pin overrides floors and default selection. Without a pin, an operator's sticky default is authoritative; otherwise the highest eligible SemVer wins. Prereleases (including generated `0.0.0-N` ids) require opt-in or a prerelease floor. A selected version below the floor fails as `unsatisfied`; resolution never downgrades. The returned identity includes the registry origin and content digest.

A single TD `rel:type` link uses the greater of its explicit `tm:minVersion` and the TD's implicit `version.model` by SemVer precedence, or the only floor present. With several type links and `version.model`, every link requires an explicit floor or unpinned resolution fails as `ambiguous-floor`; when all links have floors, each uses the greater of its own floor and `version.model`. Without `version.model`, each link uses only its own explicit floor if present, and otherwise has no floor without error.

`AssignPublishedVersion` derives authored ids from `version.model`, rejects build metadata and mismatches, and assigns the lowest free `0.0.0-N` for absent or non-SemVer model versions. TD instance revision ids remain manual. Native FileType writers allocate an authored TM Version using its `version.model` before opening the file; content-less unversioned TM Versions use generated ids. Callers can explicitly select a generated Version as the sticky default with `SetDefaultVersionAsync` when deploying unversioned models.

`IWotVersionedThingResolver` extends `IWotThingResolver` additively for stores holding multiple versions. The converter passes `tm:minVersion` on extends/submodel links and beside `tm:ref`, and derives the implicit TD type-link floor from `version.model`. `WotVersionedReference` checks floors on legacy providers' returned documents; those providers cannot silently satisfy a newer floor with older bytes. Registry dependency metadata retains each floor and projects it to `WoTDependencyDataType.RequiredVersion`. The model source is OPC 10100 WoT Connectivity 1.1.1 (2026-10-24), consumed by the existing Roslyn model source generator during build.

### Resolving a type binding: the local context

Section 5.2.1 lets a document bind the node it projects to a type that already
exists rather than to `BaseObjectType`. Section 5.1.5 defines where that name is
looked up — the *local context*, which has two parts consulted in order:

1. the other WoT documents being converted alongside this one, and
2. a loaded AddressSpace.

The order matters. A set of documents authored together resolves to itself, so
loading an unrelated companion model can never change what an existing document
projects to.

`IWotNodeResolver` (in `Opc.Ua.Types`) is one part of that context.
`WotCompositeNodeResolver` composes parts in the specified order and is passed to the
converter. A compact model name is a hint and may match none, one or
several nodes; an `ExpandedNodeId` is definitive and matches one or none.

| Implementation | Part of the context | Assembly |
| --- | --- | --- |
| `SnapshotWotNodeResolver` | the sibling documents of the conversion | `Opc.Ua.WotCon.Server` |
| `AddressSpaceWotNodeResolver` | the types the Server has loaded | `Opc.Ua.WotCon.Server` |
| `NullWotNodeResolver` | holds nothing; the default | `Opc.Ua.Types` |

Both halves are composed with `WotCompositeNodeResolver` in the specified
order. The AddressSpace half lets a document bind to a type a companion
model defines — the primary use of §5.2.1 — and it is wired in by
`WotRegistryNodeManager` as soon as an `IServerInternal` exists. Without it a
document could only bind to a type a sibling projects, and because §5.2.1
forbids falling back to `BaseObjectType` a companion-model binding would fail
the projection instead of resolving.

`SnapshotWotNodeResolver` indexes the registry snapshot being converted. Only
Thing Models are indexed, and the decision uses the *registry's* `Kind` rather
than the document's own content: a Thing Model projects its root as a
`UAObjectType` and can therefore be named by a type binding, whereas a Thing
Description projects an instance and is never a type-binding target. Trusting
the registry Kind means a party who can only submit Thing Descriptions cannot
plant a type for another document to bind to. The identity it indexes by is
derived through `WotNodeSetConverter.TryDescribeProjectedType`, the same rules
the conversion itself uses, so an index entry and the projected node cannot
disagree.

The index is built once per snapshot, not once per conversion — a refresh
converts every resource of one immutable snapshot in turn, so rebuilding it per
document would make a refresh parse the registry once per document. It is also
bounded by the same `MaxResolverDocuments` / `MaxResolverTotalBytes` budget the
rest of a conversion runs under, so a large registry cannot turn one conversion
into unbounded parsing work.

The Section 5.2.1 declaration rule is implemented: `IWotTypeDeclarationResolver`
reports a resolved type's instance declarations, the asynchronous entry point
pre-resolves them into a `WotDeclarationCatalog`, and a document member whose
NamespaceUri-qualified BrowseName is exactly a declaration's **populates** that
declaration — adopting its ReferenceType, type definition, DataType, ValueRank,
ArrayDimensions and, for a Method, the declaration it is an instance of —
instead of becoming a second, differently-reached Node under a name the type has
already spoken for. Each populated member reports `DeclarationPopulated`.

A closure that is only partly known is treated as partly known rather than as
empty:

* Every declaration that *was* read is applied. A declaration the local context
  answered for is a fact about the bound type, and skipping it because some
  other part of the closure could not be read produces exactly the duplicate
  sibling the clause forbids.
* The gap is always reported, as `DeclarationsUnavailable`. A document stating
  `uav:additionalProperties: false` **fails** — Section 6.8 is a closed-content
  statement and it cannot be evaluated against a closure that is not whole. An
  open document states no such rule, so its populated members stand and the gap
  is a **warning**; it is never silence, because silence is indistinguishable
  from a type that declares nothing.
* A member the known part does not declare is **not** reported as
  `UndeclaredMember` while the closure is incomplete: whether the unread part
  declares it was never established.

`AddressSpaceWotNodeResolver` draws the same distinction at the source. A bad
`BrowseResult.StatusCode`, a browse or read the node manager refuses, a
`BrowseName` naming a namespace index the Server does not hold, and a
`ModellingRule` that cannot be read each mark the returned
`WotTypeDeclarationSet` incomplete and name the cause in `Detail`, rather than
contributing "declares nothing".

Two behaviours are deliberate and worth knowing:

* A binding is told apart from an ordinary `@type` annotation **by namespace,
  not by whether the lookup succeeds**. A name in a namespace the local context
  holds is a binding, so failing to resolve it is an error rather than a reason
  to quietly treat it as an annotation.
* An unresolved or ambiguous binding **fails the projection**. It never falls
  back to `BaseObjectType`, because silently mistyping a node is worse than
  refusing to project it.

A host that supplies no resolver gets `NullWotNodeResolver`, which holds
nothing. A document that names no existing type still converts; one that does is
reported as unresolved rather than mistyped.

Both forms resolve through the local context, including the definitive
`ua:HasTypeDefinition` link: §5.2.1's outcome table fails the projection for a
link that "resolves to nothing" exactly as it does for an unresolved name.
Emitting an unverified identifier would leave a dangling `HasTypeDefinition`,
which is the silently mistyped node the clause exists to prevent — so the
synchronous and asynchronous entry points agree on every document, and a caller
with no local context fails such a document rather than trusting the author.

An ambiguous name and an otherwise invalid document are separate outcomes in
§5.2.1 and carry separate diagnostics: `AmbiguousTypeBinding` for a name that
matches more than one node with nothing to settle it, and `InvalidTypeBinding`
for the rest — a resolved type of the wrong NodeClass, or a name and a link that
disagree.

### Resolving a relation: companion ReferenceTypes

A link `rel` names the ReferenceType of the relation it states (§5.1.2), and
`uav:refId` carries that ReferenceType's definitive `ExpandedNodeId` (§6.2).
Neither is limited to the handful of base-namespace names the library knows:
any ReferenceType the §5.1.5 local context holds resolves by the same rules.

`IWotReferenceTypeResolver` (in `Opc.Ua.Types`) is the capability that supplies
them. It is a separate interface rather than a member of `IWotNodeResolver`
because a local context describing no ReferenceType has none to offer, and the
library targets frameworks without default interface implementations. The
converter probes for it, and a part that does not offer it contributes nothing
rather than ending the walk.

| Implementation | Where the names come from | Assembly |
| --- | --- | --- |
| `WotDocumentNodeResolver` | the sibling documents being converted | `Opc.Ua.Types` |
| `SnapshotWotNodeResolver` | the registry snapshot's ReferenceType documents | `Opc.Ua.WotCon.Server` |
| `AddressSpaceWotNodeResolver` | the ReferenceTypes the Server has loaded | `Opc.Ua.WotCon.Server` |

`WotCompositeNodeResolver` keeps the §5.1.5 order here too: the first part that
matches a name settles it, so a set of documents authored together resolves to
itself and loading an unrelated companion model can never change what an
existing document projects to.

OPC 10000-3 gives a ReferenceType two names, so the lookup resolves both:

* a match on the **BrowseName** reads the reference forward;
* a match on the **InverseName** reads the same reference backwards, and the
  emitted `Reference` has its `IsForward` flag cleared;
* a **symmetric** ReferenceType has one name for both directions and is
  therefore offered once, forward. Indexing it under both names would make
  every use of the name ambiguous.

`ResolveReferenceTypesAsync` returns *every* match rather than one, because one
namespace may hold a ReferenceType whose BrowseName is the name and another
whose InverseName is. Each match carries the ReferenceType's canonical NodeId,
the name that matched it and the direction that name expressed.

Four outcomes are diagnosed rather than guessed at:

| Outcome | Diagnostic |
| --- | --- |
| The name resolves to nothing and the link carries no `uav:refId` | `ModelConceptUnresolved` |
| The name matches more than one ReferenceType and the link carries no `uav:refId` to settle it (§6.2 requires one exactly here) | `ReferenceTypeAmbiguous` |
| The name, or the `uav:refId`, names a Node the local context holds that is not a ReferenceType | `ReferenceTypeNodeClassInvalid` |
| The name and the `uav:refId` name different ReferenceTypes, or the `uav:refId` names none of the candidates | `ModelConceptConflict` |

Where the name and the identifier agree, the identifier settles which candidate
was meant and the candidate carries the direction — so `uav:refId` fixes an
ambiguous relation without the author having to restate the direction.

A document describing a ReferenceType carries both names, so a local context
built from documents alone can answer an inverse relation: `uav:inverseName`
holds the InverseName and `uav:symmetric` the Symmetric flag. Both map onto the
projected Node's own Attributes and are restored on the reverse conversion.

A resolved relation is written into the NodeSet as a NodeSet-local NodeId, never
as the portable `ExpandedNodeId` it resolved to: a NodeSet2 document may only
state a ReferenceType as a local NodeId or as a name it declares in
`<Aliases>`, and the importer rejects anything else.

### Alarms and Conditions

Section 13 maps an OPC 10000-9 Condition to a WoT event affordance for the
notification and action affordances for the Condition Methods. Four terms carry
it:

| Term | Domain | Meaning |
| --- | --- | --- |
| `uav:conditionType` | event affordance | The compact model name of the ConditionType the event projects, e.g. `ua:LimitAlarmType` |
| `uav:conditionTypeId` | event affordance | The definitive ExpandedNodeId of the same ConditionType |
| `uav:conditionAction` | action affordance | The Condition Method invoked. Closed set: `Acknowledge`, `Confirm`, `AddComment`, `Enable`, `Disable` |
| `uav:actsOn` | action affordance | The event affordance, in the same document, whose Condition the action acts on |

A projected Condition event derives from the ConditionType it names rather than
from `BaseEventType`. A Client browsing
a type that fell back to `BaseEventType` would see none of the Condition state
and could not tell an alarm from an ordinary event.

The runtime projection follows the same rule. An event affordance that carries
`uav:conditionType` or `uav:conditionTypeId` materializes under the named
ConditionType, so an OPC UA event filter for that ConditionType, or for one of
its supertypes, can match the event. An action that carries
`uav:conditionAction` and `uav:actsOn` is routed to the corresponding OPC
10000-9 Condition Method on the Condition identified by the event affordance.

The two forms follow the hint-plus-pin pattern of Section 5.3.
`uav:conditionTypeId` is definitive and wins. `uav:conditionType` is a readable
hint, resolved for the four ConditionTypes Section 13.1 scopes —
`ConditionType`, `AcknowledgeableConditionType`, `AlarmConditionType` and
`LimitAlarmType`. A name outside that set must be pinned; an unpinned one is
reported rather than guessed. Where a document states both and they name
different types, that is a contradiction rather than a precedence question —
the pin is the definitive identity of *the same* type the compact name reads —
and it is reported as `ConditionTypeConflict`.

The converter enforces the four Section 13.3/13.4 conformance rules, each
because breaking it yields a document a consumer can read but cannot act on, and
also rejects an unresolvable readable ConditionType name:

| Rule | Section | Diagnostic |
| --- | --- | --- |
| A Condition event declares `EventId` in its `data` | 13.3 | `ConditionEventIdMissing` |
| `uav:conditionAction` is in the closed set | 13.2 | `InvalidConditionAction` |
| `uav:actsOn` names a Condition event in the same document | 13.4 | `InvalidConditionTarget` |
| `Acknowledge` / `Confirm` / `AddComment` declare an `EventId` input | 13.4 | `ConditionActionInputMissing` |
| `uav:conditionType` names a ConditionType this Binding resolves | 13.2 | `UnresolvedConditionType` |
| `uav:conditionType` and `uav:conditionTypeId` name the same type | 13.2 | `ConditionTypeConflict` |
| The ConditionType declares the Method `uav:conditionAction` names | 13.1, 13.4 | `ConditionActionNotDeclared` |
| A `data` member is a DataSchema naming one field | 13.3 | `EventFieldInvalid` |

#### Condition event data and Condition Methods

The notification's `data` object carries the Condition state (Section 13.3).
Both directions read one table of the fields OPC 10000-9 declares, so a NodeSet
that does not itself contain `ConditionType` still projects the complete field
list and a document that authors it still materializes only the fields its own
type adds:

- **NodeSet → WoT.** The `data` object is the fields the projected EventType
  effectively has: the eight mandatory `BaseEventType` fields, then the
  Condition identity and state fields, then the state each ConditionType
  subtype adds, then the Variables the projected type declares itself, in the
  order its References state them. The mandatory base fields and the Condition
  identity and state fields are `required`; subtype state is present but not
  required, which is the shape Section 13.5 states. A field the type declares
  itself also carries `uav:mapToType`, `uav:valueRank`, `uav:arrayDimensions`,
  `uav:browseName` and `uav:modellingRule`, because nothing outside the
  document says what it is. `Severity` is a per-occurrence member of that
  schema and nothing else: WoT Binding 1.1 mints no term that states a default
  severity, so none is emitted.
- **WoT → NodeSet.** Only the members the projected type *adds* become Nodes:
  a member naming an inherited field is already declared by the type it comes
  from, and re-declaring it would leave a Server holding two declarations of one
  field. `ConditionId` is never materialized at all — it is the NodeId Attribute
  of the Condition, which is why Section 6.1 selects it with the empty browse
  path. A member the schema lists in `required` gets the `Mandatory` modelling
  rule and every other one gets `Optional`.

A Condition Method is the standard Method OPC 10000-9 declares, so an action
carrying `uav:conditionAction` materializes with that declaration as its
`MethodDeclarationId` (`Acknowledge` `i=9111`, `Confirm` `i=9113`, `AddComment`
`i=9029`, `Enable` `i=9027`, `Disable` `i=9028`), takes the base-namespace
BrowseName the declaration has, and becomes a **component of the EventType**
the pairing names. This records the pairing structurally, so the
forward direction reads `uav:conditionAction` and `uav:actsOn` back from the
model rather than guessing at them. A pairing OPC 10000-9 does not admit — an
`Acknowledge` acting on a plain `ua:ConditionType`, which declares no such
Method — is reported as `ConditionActionNotDeclared` instead of being
materialized against a Method that is not there.

Going the other way, a Method whose base-namespace BrowseName is one of the
five is annotated when the event it acts on can be named without guessing:
either the EventType holds the Method, or the document projects exactly one
Condition event. With several candidates and no owning type the annotation is
left out and `ConditionActionTargetUnresolved` is reported — an `uav:actsOn`
that names the wrong Condition is worse than one that is absent, because a
consumer would acknowledge the wrong alarm. The same diagnostic covers an
occurrence-level Method that neither holds its `EventId` argument nor states
the standard `MethodDeclarationId`, because a pairing without an `EventId`
input is one Section 13.4 rejects.

The ConditionType name is a compact model name, so its prefix is resolved
through the document's `@context` rather than matched literally: an author may
bind a second prefix to the OPC UA namespace and `uav:conditionType` still
resolves.

`EventId` names the Event occurrence, so without it a consumer can receive a
notification but can never identify the occurrence to acknowledge, confirm or
comment on. It is the **one** hard requirement of Section 13.3: an affordance
carrying `uav:conditionType` shall declare `EventId` in its `data` object and,
where it states a selection, shall select it — a
MonitoredItem uses the resolved selection, so one that omits the field
describes a notification that never carries it. Every other Condition field is
present in `data` *where the affordance selects it* and is not otherwise
required; both are `ConditionEventIdMissing`. `Enable` and `Disable` act on the
Condition instance rather than one occurrence and are deliberately exempt from
the input rule.

Shelving, suppression, dialog conditions and `ConditionRefresh` are outside the
mapping, as Section 13.1 scopes it.

For the converter-default compatibility note, see
[Condition events derive from their ConditionType](WotNodeSetConversion.md#condition-events-derive-from-their-conditiontype).

The [two-pump aggregation sample](../samples/WotCon/README.md) includes supervision
EventTypes and Condition-action Methods. With `--exerciseControls true`, its client
acknowledges and confirms occurrences through the aggregate and checks the upstream
Condition state. These controls are opt-in because they modify the source servers.
Views remain organizational rather than event notifiers; subscribe to the source notifier.
Shelving, suppression, dialog and cross-WoT ConditionRefresh mappings are outside
this demonstration.

### Compatibility switch for non-portable identifiers

Release 1.1 rejects two identifier forms that OPC 10101 v1.00 permitted. Both are
session-local: a document carrying either binds to the wrong namespace as soon as
the server's namespace table is reordered, which is exactly what a document meant
to be stored and re-read must not do.

| Rejected in release 1.1 | Permitted in v1.00 | Portable form to use instead |
| --- | --- | --- |
| `ns=<index>` in any NodeId-valued term | § 6.2 | `nsu=<NamespaceUri>;<idtype>=<id>` |
| a numeric namespace prefix in `uav:browseName` / `uav:browsePath` | § 6.5.3 | a context-bound non-numeric prefix, or `nsu=<NamespaceUri>;<Name>` |

The NodeId rule applies to every NodeId-valued term: `uav:id`, `uav:hasComponent`,
`uav:componentOf`, `uav:mapToNodeId`, `uav:mapToType`, `uav:refId`, and the `href`
of a form.

So a v1.00 document written like this:

```jsonc
{
  "uav:id": "ns=3;i=1005",
  "uav:browseName": "3:Identification",
  "forms": [{ "href": "/?id=ns=3;s=Pump1.Temperature" }]
}
```

is rewritten for 1.1 as:

```jsonc
{
  "uav:id": "nsu=http://example.com/UA/Pumps/;i=1005",
  "uav:browseName": "nsu=http://opcfoundation.org/UA/DI/;Identification",
  "forms": [{ "href": "/?id=nsu=http://example.com/UA/Pumps/;s=Pump1.Temperature" }]
}
```

The namespace URI is written out, so the meaning no longer depends on the order of
the table the reader happens to hold.

A document carrying either form fails to convert, reporting `NonPortableIdentity`
or `NonPortableQualifiedName` as an error. Rewriting the document is the fix. While
that is in progress, `WotNodeSetConverterOptions.AllowNonPortableIdentifiers`
downgrades both errors to warnings, so the non-portable values stay visible rather
than being silently accepted, and each is interpreted exactly as v1.00 defined it:

```csharp
var options = new WotNodeSetConverterOptions
{
    AllowNonPortableIdentifiers = true
};

WotConversionResult<UANodeSet> result =
    WotNodeSetConverter.ToNodeSetResult(document, options);

foreach (WotDiagnostic diagnostic in result.Diagnostics)
{
    // NonPortableIdentity / NonPortableQualifiedName arrive as warnings here
    // instead of errors, naming the term and the value that has to be rewritten.
    Console.WriteLine($"{diagnostic.Severity}: {diagnostic.Code} {diagnostic.Message}");
}
```

The option defaults to `false`, which matches the release 1.1 validator. Leave it at
the default once the documents are rewritten; it exists to keep a v1.00 corpus
readable during migration, not as a supported long-term mode.
