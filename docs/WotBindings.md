# WoT Connectivity protocol bindings

The WoT Connectivity 1.1 runtime materializes Thing Descriptions and Thing Models into the OPC UA AddressSpace. Each interaction-affordance **form** in a document describes how to reach a value over a concrete protocol (HTTP, MQTT, Modbus, OPC UA, …). The **protocol binder** subsystem turns those forms into validated, immutable **binding plans** and, when an executor is present, drives the live transport operations.

The subsystem is deliberately layered so the model remains transport-neutral while the base Bindings package can bundle the dependency-compatible executors on modern .NET.

This document starts with the bindings that ship today and how to register them, then describes the contributor workflow for adding your own binding.

## Table of contents

- [Bindings that ship today](#bindings-that-ship-today)
  - [Package and assembly layout](#package-and-assembly-layout)
  - [Stable public interfaces](#stable-public-interfaces)
  - [HTTP action and event payloads](#http-action-and-event-payloads)
  - [Polling, retry and backoff](#polling-retry-and-backoff)
  - [Runtime integration](#runtime-integration)
  - [OPC UA target-mapping binding runtime](#opc-ua-target-mapping-binding-runtime)
  - [Registering binders and executors](#registering-binders-and-executors)
  - [Intentionally unsupported operations](#intentionally-unsupported-operations)
  - [Transport security](#transport-security)
  - [Operation coverage (OPC UA executor)](#operation-coverage-opc-ua-executor)
  - [Projected event identity, provenance, and Conditions](#projected-event-identity-provenance-and-conditions)
  - [Portable browse-path targets](#portable-browse-path-targets)
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
- [Conformance to WoT Binding 1.1](#conformance-to-wot-binding-11)
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
| `src/Opc.Ua.WotCon.Bindings` / `Opc.Ua.WotCon.Bindings` | Stable interfaces, plan model, codecs, the eight planner/validator binders, and registry. No sample binding ships in this library. | Base package `OPCFoundation.NetStandard.Opc.Ua.WotCon.Bindings`; full `net472;net48;netstandard2.1;net8.0;net9.0;net10.0` matrix. |
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
  * `IWotInteractionPayloadCodec` — optional complete-action and selected-event capability, with ordered arguments, declared schemas, source message contexts, and payload bounds. Legacy scalar codecs do not need to implement it.
* **Credential / trust reference lookup (no secrets in TD / registry nodes)**
  * `WotSecurityDefinition` / `WotCredentialReference` — secret-free scheme references parsed from `securityDefinitions`.
  * `IWotCredentialProvider` — resolves a reference into short-lived `WotCredential` material at runtime, out-of-band. No secret ever appears in a Thing Description or on a registry node.
* **Lifecycle and operations**
  * `IWotBindingExecutor` — `ActivateAsync` opens a per-form `IWotBindingChannel`.
  * `IWotBindingChannel` — `ReadAsync` / `WriteAsync` / `InvokeAsync` / `ObserveAsync` / `SubscribeEventAsync`, returning `WotReadResult` / `WotWriteResult` / `WotInvokeResult` with mapped `StatusCode`s.
  * `IWotPropertyBindingChannel` — optional contextual property capability. `WotReadRequest` carries `IndexRange`, `DataEncoding`, and the caller's message context; `WotWriteRequest` carries a value, its context, and a native index range. Existing channels do not need to implement this interface. `WotReadResult.WithContext` attaches the source context of returned values.
  * `IWotContextualBindingChannel` — optional invocation capability using `WotInvokeRequest`. The HTTP and OPC UA channels preserve this request's source context rather than interpreting local namespace indexes in an unrelated table.
* **Registry and structured diagnostics**
  * `IWotBinderRegistry` / `WotProtocolBinderRegistry` — the Prepare / Activate / Deactivate seam the coordinator uses.
  * `WotBindingDiagnostic` — severity + stable code + **RFC 6901 JSON Pointer**.

### HTTP action and event payloads

On .NET 8 and later, the HTTP JSON channel executes the complete compiled action
contract. `WotPayloadDescriptor.InputLayout` and `OutputLayout` distinguish a
single value from named positional arguments; a typed Structure remains **one**
argument and is sent as a JSON object, not flattened arguments or a quoted JSON
string. Named arguments use `uav:fieldOrder`, never JSON property order:

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
  },
  "output": {
    "type": "object",
    "uav:argumentLayout": "named",
    "uav:fieldOrder": ["Maximum", "Minimum"],
    "properties": {
      "Minimum": { "type": "integer" },
      "Maximum": { "type": "integer" }
    }
  },
  "forms": [{ "href": "https://device.example/limits", "op": "invokeaction" }]
}
```

Inputs `[-7, 42]` produce `{"Minimum":-7,"Maximum":42}`. A response
`{"Minimum":-8,"Maximum":43}` produces native outputs `[43, -8]`.
Missing schemas declare zero arguments; an explicitly empty named object remains
`{}` on the wire, including the response. A declared single null value is not an
absent contract. Keep the native `uav:valueRank` declaration for arrays (for
example `1` for a one-dimensional array); an omitted native rank remains scalar.
Missing, duplicate, extra, malformed, or incorrectly typed output members fail
the operation instead of returning a Good envelope containing a bad value.
Encoding and input-count failures are reported before sending. Invocation does
not implicitly retry or invoke another form.

`WotPayloadDescriptor.Schema` carries a detached `WotPayloadSchema`: the complete
authored schemas plus native type and BrowseName facts resolved while their
owning document and scoped contexts are still available. Conversion-resolved
external and inferred types are retained through `WotConvertedAffordance` and
`WotProjectedAffordance`. Capture requires original elements of the live owning
document, not foreign or already-cloned elements whose scoped context is lost.
Payload and browse-path captures keep their separate original scopes when a
relative href is resolved; a form's local prefixes do not redefine the owning
affordance's data types.
Numeric declarations remain abstract Integer/Number
unless annotated; concrete native widths are not invented in the schema.
The JSON codec adapts the TD representation to the existing native codecs:
Int64/UInt64 use JSON numbers when the TD requires numbers, and a string-valued
LocalizedText schema uses text rather than the UA JSON object envelope.
Finite JSON numbers must fit their native Float/Double representation; overflow
is an explicit decoding failure, including inside arrays and Structures.
Explicit UA IEEE special-value strings remain available when the DataSchema
admits them. Numeric `minimum`/`maximum` comparisons retain the JSON numbers'
significant digits and magnitude rather than narrowing them to Decimal or Double.
The shared `WotJsonNumberComparer` uses decimal digits and exponent positions,
without allocating powers of ten. A nonzero number whose decimal exponent is
outside Int32 is an explicit unsupported comparison, not a satisfied constraint.
Positive message-context `MaxArrayLength` limits also apply to the outer array
of a Structure-array payload, before native allocation or element decoding.

For namespace-bearing inputs, use the established contextual invocation:

```csharp
await using IWotBindingChannel channel =
    await registry.OpenChannelAsync(form, cancellationToken);
if (channel is not IWotContextualBindingChannel contextual)
{
    throw new ServiceResultException(StatusCodes.BadNotSupported);
}

var request = new WotInvokeRequest(
    [new Variant(-7L), new Variant(42L)], messageContext);
WotInvokeResult result = await contextual.InvokeAsync(request, cancellationToken);
if (!result.Success)
{
    throw new ServiceResultException(result.Status, result.Error);
}
```

Custom Structure values use registered `IEncodeableFactory` / `IStructure`
metadata, including nested fields and arrays, without reflection or dynamic
code generation. An unavailable factory or opaque value that cannot preserve
its declared meaning fails explicitly. Direct activation can supply
`WotExecutorContext.WithMessageContext` or
`WotProtocolBinderRegistry.MessageContext`. DI honors a registered
`IServiceMessageContext`; the optional `IWotContextualBindingChannelFactory`
also lets the projected consumer supply its actual materialized namespace and
type-factory context at activation.

HTTP event polling decodes the event's data object at the response root into
both `WotNotification.Data` and its selected-field index. All authored or
implicit-default clauses are populated with typed values and
`WotNotification.Context`; linked EventType schemas retain their own context,
not the referring TD's prefixes. This is JSON event polling, not a property
observation masquerading as an event, and does not add SSE or WebSub framing.
Resolved selections retain their payload facts alongside independently verified
native declaration evidence; neither is discarded when the selection is copied.
Malformed event payloads produce observable bad-status notifications.

`WotBindingBounds.MaxPayloadBytes` counts actual UTF-8 payload bytes, including
JSON syntax. `MaxPayloadDepth` counts the root object/array as depth one and
scalars as depth zero. HTTP event and property polling share
`PollingWotSubscription` and its retry policy. Cancellation ends the returned
subscription, subscription disposal awaits in-flight polling, and channel
disposal stops its subscriptions while leaving a caller-owned `HttpClient`
owned by its caller. The subscription is registered before its first callback,
including when an in-memory transport completes synchronously. Projected event
sources share a subscription only when the resolved payload and clause type
contracts agree; matching URLs and field names alone are insufficient.
Shared acquisition has a source-owned lifetime: cancelling one projected
listener does not stop another listener's poll. Removing the last listener or
disposing the source/runtime cancels and drains that acquisition. Direct HTTP
subscriptions still follow their own cancellation tokens.

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

### Runtime integration

`WotMaterializationCoordinator` compiles each resource's forms into a `WotBindingPlan` during **Prepare**, activates the plan only **after** the projection is committed as the active generation, and deactivates it **before** the projection is retired or unloaded.

* **Strict mode** (`WotRegistryServerOptions.StrictBindings = true`) fails the closure when any required form is unsupported or invalid.
* **Degraded mode** materializes nodes with `BadConfigurationError` and emits a `WoTBindingFailureEvent`. Validated-but-non-executable forms also degrade the closure so their nodes are visible but flagged.
* Registered capabilities populate browseable `SupportedBindings` descriptors and
  contribute to refresh unchanged-detection. The read-only `SelectedBindings`
  array contains detached snapshots from published plans, not unused registered
  binders. See [binding discovery](WoTConnectivity.md#114-binder-integration-seam)
  for optional metadata, effective runtime policy and direct client decoding.
* The legacy 1.02 `IWotAssetProviderFactory` provider model is preserved untouched.
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
  * A **direct** target (`uav:mapToNodeId` and/or `uav:mapToType` alone) wires the executable `readproperty`/`writeproperty` forms as full async `OnRead`/`OnWrite` handlers that preserve the source `StatusCode` and `SourceTimestamp`. An observe-only property or a separately authored `observeproperty` form uses a shared source subscription, not the read handler. Only a combined read/observe form from the same source document retains read-sampling compatibility.
  * A **structured** target (`uav:mapToType` + `uav:mapByFieldPath`) composes the value by reading every mapped field concurrently, building nested structures via `IEncodeableFactory` / `IStructure` / `IDataTypeDefinitionSource` (no reflection); writes extract and write each mapped field concurrently from the incoming structure. A Bad field fails the whole operation; Uncertain reads retain their usable values. The composed result preserves the first non-default status at the highest severity and uses the oldest non-`MinValue` `SourceTimestamp` across the fields. Direct and structured writes preserve successful subcodes such as `GoodClamped`.
  * Alternative forms on one property select the first executable form per operation in authored order, before grouping direct or structured targets. A failed operation is not retried against another source and writes do not fan out. Distinct properties independently claiming the same target/field for the same operation remain conflicting declarations, including duplicate observation mappings. Mixed direct/field mappings and unsupported target operations also fail. Everything else about a structured target that depends on its structure type being registered — the encodeable type lookup, root instance validation, and `uav:mapByFieldPath` path resolution (empty segments, unknown fields, array-valued or non-structure intermediate fields) — is deferred to the first structured read, write, or observation startup, because `RuntimeNodeSetOptions.ConfigureAsync` runs before `NodeManagerLifecycle.RefreshComplexTypesAsync` registers the server's custom structure types. Failed resolution is not cached; an unresolved use reports `BadConfigurationError`.
  * Channels are opened lazily and cached one-per-compiled-form for the generation; concurrent first use opens once, and a failed open is evicted so a later call can retry. Cancelling one reader does not cancel a shared open. Cancelling the generation does cancel that open and pending observation startup. Every successfully acquired channel or subscription remains owned until its asynchronous cleanup completes; disposal failures are aggregated.
* Both abstractions are always available via direct construction (no DI container required) and are registered through `AddWotRegistryServer` using `TryAdd*` so a host application can supply its own implementation.

Property reads translate namespace-bearing values using `WotReadResult.Context`, including individual structured fields, before exposing them in the local AddressSpace. Observations use `WotNotification.Context`, or its `NamespaceUris` table for older channels that provide only namespace authority. The OPC UA adapter supplies a complete source-context snapshot for data changes as well as events. Contextual property writes use the same URI-based mapper in the opposite direction. Unknown remote namespaces fail without extending a Server's session table. A channel without contextual support can still exchange namespace-independent values, but cannot silently accept local namespace indexes; opaque ExtensionObjects that cannot be translated as decoded structures also fail explicitly.

The OPC UA property channel forwards native `IndexRange` values on Read and Write and resolves requested data-encoding QualifiedNames in the source namespace table. The runtime does not apply a native range twice. For channels without this capability, it applies the Core read range/data-encoding helper locally and rejects indexed writes with `BadWriteNotSupported` before calling Write. It does not emulate an indexed write with an unsafe read-modify-write. An index range on a composed structured write is likewise rejected before writing its fields.

Direct channel consumers can opt into the additive capability:

```csharp
if (channel is IWotPropertyBindingChannel propertyChannel)
{
    WotReadResult slice = await propertyChannel.ReadAsync(
        new WotReadRequest(callerContext, NumericRange.Parse("1")),
        cancellationToken);
    WotWriteResult result = await propertyChannel.WriteAsync(
        new WotWriteRequest(replacementSlice, callerContext, NumericRange.Parse("1")),
        cancellationToken);
}
```

### Projected Methods, events, and Conditions

`WotBindingPlan.ProjectedAffordances` carries each declaration's local identity,
owning resource, and JSON Pointer separately from its upstream form address.
It includes ordinary properties as well as Methods and EventTypes. The production
document converter matches interactions to actual converted Nodes through
`WotNodeSetConverter.ResolveAffordanceNodes`, retaining generated identities and
the captured interaction schemas. `WotConversionOutput.ProjectedAffordances`
passes those facts through the coordinator into each binding plan; the runtime
does not derive local identities from upstream form targets.

An ordinary property form therefore binds to its local Variable without requiring
`uav:mapToNodeId`, `uav:mapToType`, or `uav:mapByFieldPath`. Explicit target mappings
keep their existing purpose and take precedence. Local properties without forms
remain local. Native/archive identities can be matched by an unambiguous qualified,
root-owned declaration, but an explicit missing identity, wrong NodeClass or
ambiguous match is an error rather than an arbitrary fallback.

An action's `uav:id` identifies the local Method; its selected form supplies the
upstream Method and Object addresses. The runtime wires the local Method through
the asynchronous fluent `OnCallWithResult` hook. When an action offers several
forms, the runtime selects one supported, executable form and sends the request
only to that form's upstream source. It returns the upstream call's status and
any argument errors to the caller; invoking the local Method does not by itself
count as success.

The compiled input and output layouts retain every native position, including
zero arguments and one whole Structure or Union. A named layout uses its complete
`uav:fieldOrder`; JSON member order is not native argument order.
`WotMethodArgumentLayout.GetArgumentName` also exposes the converter's effective
single-argument name, including its `Input` or `Output` default. Before installing
a projected handler, the runtime checks the actual Method argument names,
DataTypes and ranks against those captured declarations. A mismatched signature
fails activation rather than opening an upstream channel for a different contract.
Abstract numeric declarations can be refined by compatible concrete native
argument types; they do not require an existing explicit consumer to replace an
Int64 signature with the abstract Integer DataType.

The native adapter validates input counts, native types, nullability and ranks
before any target browse-path service or Call. It validates every successful
output position as well: missing, extra or mistyped outputs fail with
`BadDecodingError`, without a partial successful response or a retry.
JSON `required` must be a unique subset of the named arguments. JSON optionality,
nullability and `default` annotations alone do not authorize shortening a native
signature; a nullable value still occupies its argument position. In particular,
a JSON default is not substituted for a supplied native null or a missing
mandatory argument.

Direct and DI consumers can supply an `IServiceMessageContext` to the registry
or per channel activation. Native calls through the original context-free channel
interface then use that explicitly configured input context. Without an explicit
activation context, the legacy native interface retains its Session-relative
interpretation; `WotInvokeRequest` always supplies an explicit caller context.
Decoded native encodeables use registered Structure metadata when they do not
implement `IStructure`; their nested namespace references are checked without
reflection or modification of the source namespace table. Opaque bodies or
missing required type metadata fail before the native Call.
Public `WotFormExtractor.Extract` and `WotBindingPlanRequest.FromDocument` retain
the captured schema facts even after the source document is disposed. Explicit
requests built from extracted forms do not need internal property setters.

An explicit consumer can preflight complete native inputs before opening a channel:

```csharp
ArrayOf<Variant> nativeInputs = form.ConditionInvocation is { } condition
    ? condition.NormalizeInputs(inputs) : inputs;
form.Payload.ValidateInputs(nativeInputs, messageContext);
await using IWotBindingChannel channel =
    await registry.OpenChannelAsync(form, messageContext, cancellationToken);
WotInvokeResult reply = await channel.InvokeAsync(nativeInputs.Span.ToArray(), cancellationToken);
```

`ValidateArgumentLayouts` rejects inconsistent schema/layout combinations, and
`ValidateMethodSignature` is available to other projected consumers. These checks
use captured declarations, not the shape of the current value. Transport-only
descriptors remain supported for existing explicit channel implementations.

`WotInvokeResult.OperationResult` and `InputArgumentResults` carry resolved
diagnostic text, not indexes into the source server's response StringTable.
Argument results preserve their order and optional absence. The receiving
server encodes diagnostic indexes into its own response StringTable and applies
the caller's diagnostic mask and its own authorization for additional details.
Malformed native input-result counts, invalid diagnostic indexes and excessive inner
diagnostic depth fail with `BadDecodingError`, without retrying the action.

Contextual channels receive the requested diagnostics through
`WotInvokeRequest.DiagnosticsMask`. This mask excludes local server permission
flags; the upstream Session authorizes additional diagnostic information
independently. The existing two-argument request constructor and context-free
channel interface remain available. With no mask, native invocation retains the
Session's configured diagnostic defaults; receiving-server filtering still applies.

```csharp
var request = new WotInvokeRequest(
    inputs, messageContext, DiagnosticsMasks.OperationAll);
WotInvokeResult result = await contextualChannel.InvokeAsync(request, cancellationToken);
ServiceResult operation = result.OperationResult;
ArrayOf<ServiceResult> inputResults = result.InputArgumentResults;
```

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
and a bounded occurrence table maps local EventIds back to the selected
source, Condition, and branch. Wrong-source, unknown, or revoked IDs fail rather
than falling back to another source. Retired-generation routes remain usable
only while that generation is alive and the declaration and source still match.

Native occurrence capture obtains the namespace-zero `EventId` independently
of the public field selection. Without native capture, occurrence identity comes
only from a selected one-element namespace-zero `EventId` path, read at that
selection's materialized data-member path.
A vendor or nested field named `EventId` remains business data regardless of
its value type; an unselected payload member cannot drive deduplication or
Condition routing. Events without a captured or selected occurrence identity
still receive distinct local EventIds.

Condition-management actions use `uav:conditionAction` and same-document `uav:actsOn`.
For a WoT invocation with an optional Comment, the OPC UA adapter supplies
`LocalizedText.Null` when the caller provides only EventId. This does not change
a native two-argument Method's signature: OPC UA callers supply both arguments.
The planner checks the canonical scalar ByteString/LocalizedText input order and
zero outputs for occurrence actions. Enable and Disable retain zero inputs and
zero outputs. A shortened, reordered or mistyped Condition signature is not
accepted as a new meaning for an inherited Core Method.
After acknowledgement changes the occurrence, confirmation uses the updated
EventId. Unbound standard Methods on a Condition proxy are disabled, so they
cannot change only the local copy.
The default Condition factory detaches its own Core Enable/Disable transitions
before proxy wiring. Application-installed handlers are not cleared: a conflicting
handler still prevents activation rather than being silently replaced.

`WotProjectionBindingRuntimeOptions` limits pending notifications per local
notifier (`MaxQueuedEvents`) and accepted occurrence evidence per event declaration
and generation (`MaxEventRoutes`). If the notification queue fills, the producer finishes
delivering already queued events, then stops with a `BadTooManyOperations`
error, marks the affected bindings unavailable, and releases its upstream
subscription leases. Generated telemetry logs the failure; other event sources continue running. This is a
server-side producer failure, not an `EventQueueOverflowEventType` notification
or termination of the client's entire subscription. Exhausting occurrence
capacity rejects the excess event before it changes Condition state or is
published. Previously accepted identities, provenance, reservations, and
eligible action routes remain owned; no FIFO eviction makes an old ID reusable.
A revoked action route still fails with `BadEventIdUnknown` without releasing
the occurrence's identity evidence. The event publisher, Condition factory, runtime factory,
and options are injectable as well as available for direct construction. The
[two-pump aggregation sample](../samples/WotCon/README.md) demonstrates
source-specific management actions and acknowledgement/confirmation without
introducing shelving, suppression, dialog, or ConditionRefresh transport mappings.

### Registering binders and executors

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
| `invokeaction` | `Call` service; the Method is selected by its source NodeId or browse path, independently of the form's `uav:callObjectId` receiver. Legacy scalar form-scoped `uav:componentOf` remains a compatibility spelling. |
| `subscribeevent` | A native event `MonitoredItem` (`AttributeId = EventNotifier`) whose public `EventFilter` select clauses are the compiled `WotEventSelection` of WoT Binding Section 6.1: the eight mandatory `BaseEventType` fields (`EventId`, `EventType`, `SourceNode`, `SourceName`, `Time`, `ReceiveTime`, `Message`, `Severity`) when the affordance states no selection, and otherwise the selection resolved from the EventType definition it links to with `tm:ref`, overlaid by the `uav:eventSelectClauses` it states. Every public selected field is delivered in `WotNotification.EventFields`, keyed by its browse path — an empty path supplies `ConditionId` — with the event's own `Time` / `ReceiveTime` as the source / server timestamp. Capturing sources append missing private Core operands without exposing additional public members. |

For local native re-emission, the projection runtime stamps the namespace-zero
Core `ReceiveTime` from the local server's `TimeProvider` when it materializes the
occurrence for publication. Native event selection reads this local receipt
stamp, not the upstream `ReceiveTime`. The selected source `Time`, captured
upstream receipt value, and source/server timestamps remain source facts;
stamping the local occurrence does not mutate the upstream `WotNotification`.
Namespace-qualified fields named `ReceiveTime` retain their selected source
values.

Both subscription kinds share one code path: a dedicated `Subscription` is created per channel subscription, its `MonitoredItem` is disposed and the subscription removed from the session (`ISession.RemoveSubscriptionAsync`) when the returned `IWotSubscription` is disposed, so no session or subscription is leaked — including when creation fails partway through.

A compiled form's NodeId and explicit Call receiver are resolved against the connected Session's namespace table. Portable `nsu=` identifiers retain their namespace-URI meaning; legacy plain NodeId forms remain supported where applicable.

### Projected event identity, provenance, and Conditions

`WotProjectedAffordance.IdentityMode` uses the generated
`WoTEventIdentityModeEnum`. `LocalReEmission` is the compatibility default.
An explicit `uav:eventIdentityMode` declaration accepts `local-re-emission` or
`transparent-forwarding`; unknown values fail rather than selecting a fallback.
Direct callers can select the mode without a JSON declaration:

```csharp
WotProjectedAffordance declaration = WotProjectedAffordance
    .FromConverted(converted)
    .WithIdentityMode(WoTEventIdentityModeEnum.TransparentForwarding);
```

Selection is not admission. Transparent forwarding requires typed occurrence
facts captured from an authenticated native source, its current retained Session
binding and namespace mapping, source EventId/EventType/SourceNode, and source
Time/ReceiveTime. A source EventType cannot be replaced by a local overlay.
Source and Condition identities from different authenticated authorities cannot
alias one native NodeId; ambiguous preserved EventIds are rejected. There is no
automatic downgrade to local re-emission.

Native transparent activation opens and retains its source binding before the
generation is published, even when no actions or subscribers exist. It uses
`IWotCapturedEventChannel.CaptureEventSourceAsync` without creating a
subscription. The channel owns the binding. Admission checks authentication,
the current Session and namespace mapping, the selected Object or View's event
subscription capability, and the host's server-wide identity admission status.
It also checks the source and local EventType lineage. A known source Condition
identity is reserved against conflicting authorities before publication.
After these checks succeed, preparation requires strict native admission through
`EventManager.RequireEventIdentityAdmission`. This does not allocate an EventId
or consume identity capacity. Native collisions are rejected from this point,
without waiting for a subscriber or the first forwarded occurrence.

This admission supports Core event types and custom subtypes that add no
instance declarations. Custom types must match their source BrowseName,
abstractness, and parent lineage. Cyclic or missing lineages, lineages deeper
than 64 types, custom instance declarations, and custom payload schemas are
rejected. This is a bounded capability check, not JSON Schema validation or
general schema equivalence.

The runtime checks source validity and host identity status again before
registering its event publishers. Failure or cancellation disposes the candidate
channels and releases its prepared claims. A failed replacement leaves the old
generation active. The admitted descriptor reports `Availability = Good` and
`SourceServerUri` before the first notification. Later notifications must use
that same captured source binding; activation does not waive occurrence checks.
Local-re-emission event channels stay lazy unless an authored action requires
capture. Direct construction and dependency injection use the same checks.

Imported namespaces must really be owned by their NodeSet sources. A multi-model
projection also needs an unambiguous default namespace for its fluent runtime.
For example, a local notifier that has a `GeneratesEvent` reference to an imported
source EventType declares that source model as a `RequiredModel`. Listing a
namespace URI alone does not establish ownership or a model dependency.

The runtime publishes out-of-band `WoTEventBindingType` descriptors under the
notifier's `EventBindings` folder. A descriptor reports its selected mode,
binding pointer, pinned source document, actual published NodeManager generation,
and availability. It is not an extension of the imported EventType. Source facts
are returned by the generated `GetEventProvenance` method as
`WoTEventOriginDataType`; absent optional facts remain absent. Source receipt time
and the receiving server's latest receipt time are separate fields.

Clients using the generated wrapper register the model's encodeables through the
standard factory builder before decoding its structured result:

```csharp
session.Factory.Builder.AddOpcUaWotCon().Commit();
var binding = new WoTEventBindingTypeClient(session, bindingNodeId, telemetry);
WoTEventOriginDataType origin =
    await binding.GetEventProvenanceAsync(eventId, cancellationToken);
```

Normal native RolePermissions apply before the method looks up an occurrence.
Unauthorized callers receive `BadUserAccessDenied`, including for unknown
EventIds; authorized unknown or disposed-generation evidence returns `BadNoData`, never an
empty successful record. The returned provenance is data, not an action-dispatch
capability.

A verified retransmission retains the same occurrence identity and refreshes
receipt provenance without publishing another occurrence. A retained Condition
refresh may also update a non-state Property declared by its Core Condition
representation, such as the namespace-zero `HighLimit` of `LimitAlarmType`.
The declared type must agree with that representation. The update retains
EventId, Time, ConditionId, BranchId, captured Core state, value type and
status/timestamp facts. It updates the retained Property and receipt provenance
without consuming another occurrence slot. Namespace lookalikes do not gain this
permission. Other changes to selected or privately captured state, identity, or
source binding fault that binding's availability with
`BadSecurityChecksFailed`, preserve the last accepted evidence, and revoke its
occurrence action route.

Notification-only Conditions do not need invented actions or a static action
receiver. The existing injectable Condition factory creates independently
registered instances per source Condition; main and retained branches produce
independent snapshots. Local identities are stable within their actual
generation and distinct across Conditions/branches. Transparent instances retain
their admitted source identities. Reusing an EventType for multiple declarations
does not reuse a mutable Condition or its local NodeId. Unbound inherited
Condition methods are not advertised as executable.

Projection uses the optional `IWotCapturedEventChannel` capability of the native
OPC UA channel. It retains the public selection as an unchanged operand prefix,
including authored duplicates, reuses exact equivalent operands for private
capture, and appends only missing Core operands. Base-event projections request
only the eight `BaseEventType` fields. Condition projections additionally request
the common `ConditionType` fields:
ConditionId (the empty-path NodeId Attribute), ConditionClassId/ConditionClassName,
ConditionName, BranchId, Retain, EnabledState and its Id, Quality, LastSeverity,
Comment, ClientUserId, and the mandatory SourceTimestamp subcomponents of
Quality, LastSeverity, and Comment. Non-Condition occurrences do not acquire
Condition semantics merely because these operands were requested.

The Core-type capture overload also requests `AckedState` and its Id for
`AcknowledgeableConditionType`. For `AlarmConditionType` and `LimitAlarmType`,
it adds `ActiveState` and its Id, `InputNode`, and `SuppressedOrShelved`.
The runtime selects these fields from the advertised Core representation,
not from the public selection. A shared source captures the fields needed by
all its declarations; each binding applies only fields valid for its own type.
An ordinary Condition does not require alarm fields. The existing Boolean
capture overload retains its BaseEventType or common ConditionType contract.

`WotCompiledForm.EventSelection`, `WotNotification.EventFields`, and
`WotNotification.Data` still contain only the authored public selection. Native
projected notifications and Condition instances receive their Core fields from
the same captured occurrence, so native clients can select inherited fields even
when the WoT public selection omits them. Private identity also supplies
provenance, branch mapping, and authored action correlation in both modes.
An absent or invalid required source identity fails rather than falling back to
an authored action receiver or a fabricated Condition. Captured Session validity,
namespace mapping, authentication, and generation ownership are unchanged. The
existing `IWotBindingChannel.SubscribeEventAsync` and publisher contracts remain
compatible; ordinary subscriptions do not request additional private operands.

Declared Condition actions are captured at generation wiring time through
`IWotCapturedConditionActionChannel`. The native adapter retains the original
authenticated source Session, resolved Method, receiver, and namespace mapping.
Occurrence actions use the producing generation's captured action, not a newly
opened replacement channel. A wrong Condition is rejected before dispatch.
Retiring generations keep captured actions only while their consumers drain;
expired routes, reconnects, and mapping invalidation cannot dispatch on a
replacement Session. The ordinary context-aware Call path is unchanged.
Both authored action Methods and their inherited Core Condition counterparts
retain native RolePermissions. Unauthorized Calls fail before occurrence-route
lookup, including when the supplied EventId is unknown.
Local-re-emission `Enable`/`Disable` controls on non-capturing channels retain
their existing zero-input, generation-owned channel contract; they do not claim
an authenticated occurrence route. Transparent controls and all native
occurrence actions still require captured source admission. If a channel
advertises capture but capture fails, the failure is not bypassed.

The default `WotProjectionEventPublisher` advertises
`IWotNativeProjectionEventPublisher`: native metadata registration and captured
Condition action admission are mandatory for that path. Injected headless
publishers retain the original callback contract and do not claim native
metadata or authenticated occurrence dispatch. Existing custom Condition
factories can retain a statically scoped instance; factories that support
independent source Conditions implement `IWotProjectionConditionInstanceFactory`.
Non-capturing headless publishers keep their legacy selected-identity
deduplication; this does not exempt them from the generation's evidence budget.

`MaxEventRoutes` is a finite **per-declaration, per-generation occurrence
budget**, not a rolling action-route cache. Every accepted occurrence, including
an ordinary non-Condition event with no source EventId, consumes a slot. A
verified replay refreshes provenance without consuming another slot. Admission
and transparent reservations precede Condition creation and publication;
rejected or cancelled admission releases only its uncommitted reservation.
Failed Condition creation does not leave a failed task consuming an instance slot.

The generation conservatively owns all accepted occurrence evidence until its
existing lifecycle drain and disposal. Native queues, retransmission buffers,
retained main/branch state, provenance requests and in-flight actions therefore
do not depend on an action-route expiry timer or garbage collection to preserve
identity. Capacity exhaustion sets the binding descriptor's `Availability` to
`BadTooManyOperations` and ends that binding's event delivery. Restarting its
subscription does not reset the budget or make the binding available again.
Already accepted action routes remain subject to their original source,
generation and authorization checks.

Recovery is explicit: use the existing host `ShadowReloadAsync` to install a
replacement generation, or remove a drained generation and add a replacement.
The new generation has its own bounded budget; an old generation's subscribed
consumers and bound work retain its original provenance and actual producing
`NodeManagerRegistration.Generation` until they drain. Replacement does not
authorize a collision with a reservation still owned by an older generation.
Applications must size the budget for their intended generation lifetime and
perform this lifecycle transition rather than expecting an unlimited ordinary
event stream from a finite generation.

Native projection also reserves each output EventId with the host's existing
`EventManager`, before Condition creation or delivery. This is one server-scoped
admission domain shared by independently constructed runtime factories, both
projection modes, and ordinary native events. A native publisher cannot acquire
a forwarded or locally projected ID by copying its fields, nor can forwarding
acquire an ID already owned by native publication. Only bilateral trusted
occurrence evidence permits shared reservations. An authenticated source's bytes
alone, or an ApplicationUri alias, cannot establish a shared physical occurrence.
Collisions return `BadSecurityChecksFailed`; projection failures set the existing
binding `Availability` without replacing the original owner or its provenance.

`EventIdentityAdmissionOptions.MaxEventIdentities` separately bounds this shared
domain (default 65,536 identities). Set `StandardServer.EventIdentityAdmissionOptions`
before startup, inject these options through server hosting DI, or pass them to
the `EventManager` constructor. No live identity is evicted at this limit:
new admission fails with `BadTooManyOperations`. Projected reservations follow
their producing generation. Event targets and the original selected
`EventFieldList` additionally keep their admitted identity alive through native
fanout, monitored-item queues and the server's Publish/retransmission buffers.
The existing sent-message queue releases field ownership on acknowledgment,
discard or subscription cleanup, before returning those payloads to their pools.
Disposing a generation does not release these remaining references. Once all
references drain, garbage collection makes the shared entry reclaimable on a
subsequent admission. An unattached, rejected reservation is released immediately.
This conservative reclamation can lag generation disposal; subscription restart
or action-route expiry is not a reset of either budget.

Ordinary native APIs have no explicit producing-generation release contract, so
their bounded identity evidence remains until server disposal. The manager
records this evidence from startup. Preparing a native transparent projection or
making the first projected reservation requires strict admission for the remaining
server lifetime, including subsequent native publication. Aborting or removing
that generation does not restore legacy admission. Before that request, legacy
native publication remains compatible;
an unidentifiable or conflicting native occurrence is reported through warning
telemetry and `EventIdentityAdmissionStatus = BadNotSupported`. It permanently
prevents claiming the optional guarantee in that server lifetime, rather than
silently forgetting earlier output. After the guarantee is requested, native
collisions, unsupported shapes and exhaustion throw before queueing or delivery.
Exact native retransmissions and same retained-Condition refresh preserve their
EventId; changed ReceiveTime or non-state alarm limits do not require a new ID.

The supported host uses the built-in subscription manager and native publication
pipeline without a persisted/replicated retransmission store.
Server `ReportEvent`/`ReportEventAsync`, node-manager notifier sinks,
`MonitoredItem.QueueEvent(IFilterTarget)` (including refresh and the event-manager
report helpers), and the final subscription Publish path all participate.
Custom subscription managers or publishers which override/bypass these paths
are not an admitted native capability. Direct preselected
`QueueEvent(EventFieldList)` is supported only for fields already selected and
admitted by `MonitoredItem`; arbitrary fields, including a forged `Handle`, fail
with `BadNotSupported` once admission is required. Custom/durable queues must
preserve the admitted in-process field instances; reconstituting them does not
establish identity continuity. Headless publishers do not advertise this native
server-wide guarantee. These publication checks complement the bounded
transparent activation checks; neither provides persisted identity continuity
or general schema equivalence.

`MaxEventRoutes` also separately bounds independently materialized source Conditions.
A declared actionable Condition consumes the same instance bound as a
notification-only Condition. Branches do not consume additional **instance**
slots, but each distinct branch occurrence consumes an **occurrence** slot.
Queue limits remain separate. This is an in-process, generation-lifetime
contract, not persisted historical replay, restart/HA continuity, or a durable
publication transaction.
JSON Schema validation is independent of these identity/lifetime guarantees and
is not added by this feature.

### Portable browse-path targets

An OPC UA form may use `uav:browsePath` without a target NodeId. The binder
captures its original scoped namespace context and inherited anchor, preserves
the endpoint resource path, and translates against the actual Session before a
source operation. Missing, partial, remote, ambiguous, wrong-class, or
inconsistent simultaneous targets fail before that operation.

Path-based native subscriptions revalidate addressing after Session configuration
changes and on their configured maintenance interval. They keep native value and
event delivery, and dispose their maintenance alongside the native subscription.
See [OPC UA browse-path targets](WotBrowsePathTargets.md) for syntax, authoring
examples, limits, source-ownership rules, and consumer effects.

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
  `TypeDefinitionId` its definition declared. This is the **query anchor**, not
  necessarily the owner of an inherited field. A Condition occurrence selection
  must resolve the one-element namespace-zero `BaseEventType.EventId` declaration
  (`i=2042`, scalar `ByteString`), including when queried through a companion
  EventType. A vendor `EventId`, a nested lookalike, or a base64 string schema does
  not establish that declaration. Native-backed contexts report the actual
  declaration even when its declaring type is not the document root and only
  the owner's forward `HasProperty` reference establishes ownership. A built-in
  declaration is used only without supplied type context; it never replaces
  an incompatible native DataType, rank, or owner.
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
  is what decides the output, so two clauses that reach it would compete for it and
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

The superseded `uav:eventFields` spelling this implementation minted before the term was
standardized is still **read** — it is authored on a form, carries bare browse names and
*adds* to the default selection — and is never **written**. Where a form carries both,
the standardized term wins and the contradiction is reported (`ConflictingFields`)
rather than silently merged. New documents should author the standardized terms
instead: link the EventType definition with `tm:ref` and state only the clauses that
refine it, as described above.

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

The planner compiles a shared floor onto `WotCompiledForm.SecurityFloor`. When a
`oneOf` combination offers different floors, each remains attached to its own
`WotCompiledForm.OpcUaSecurityRequirements` alternative instead of becoming an
unconditional floor on every alternative. A floor the Binding cannot
read — one carried by a scheme other than `auto`, or naming a mode or policy Section 5.7
does not — fails the form (`InvalidSecurityFloor`) instead of compiling without the
constraint.

Because endpoint selection needs an application configuration, a certificate store and a
transport this library is deliberately not given, the choice is made by a delegate — but
the *rules* are made here, and the executor never opens a session it could not have
chosen:

* `OpcUaWotBindingOptions.ConstrainedSessionFactory` receives an
  `OpcUaWotSessionRequest` carrying `SecurityRequirements` and a shared `MinimumSecurity`,
  so a caller's own factory can discard
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
* The endpoint-blind `SessionFactory` stays exactly as it was where the form states **no**
  floor or exact requirement. A constrained form with only that factory configured fails with
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

The explicit `uav:channelsec` scheme requires an exact mode and complete standard
policy identity, not a minimum strength. `uav:authentication` requires the actual
session's `Anonymous`, `UserName`, `Certificate`, or `IssuedToken` identity kind.
The planner preserves `allOf` conjunctions and `oneOf` alternatives without mixing
the channel of one alternative with the identity of another. Invalid references,
cycles, contradictory requirements, and configured depth/alternative limits are
reported before connection; requirements are not truncated. An optional
`uav:issueToken` remains a secret-free security-scheme reference for the factory's
out-of-band credential provider, not an issuer URL or a token embedded in the TD.

Factories that previously read only `MinimumSecurity` must also honor
`SecurityRequirements`. The built-in discovery path filters both channel constraints
and advertised user-token kinds, retaining the existing deterministic ranking among
eligible endpoints. Custom selection can use the same overload:

```csharp
EndpointDescription? selected = OpcUaWotEndpointSelector.Select(
    discovered, request.MinimumSecurity, request.SecurityRequirements);
```

The executor verifies the established session again: channel mismatches return
`BadSecurityModeRejected`, and a non-matching actual identity returns
`BadIdentityTokenRejected`. Rejected owned sessions are disposed; borrowed sessions
are not. Certificate trust, credential acquisition, and the choice among compatible
token policies remain application-controlled.

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
6. After the NodeSet is imported, `WotProjectionBindingRuntimeFactory` wires the compiled forms to target variables. Wiring is synchronous and performs no transport I/O.
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

`WotFormExtractor` emits a formless descriptor for an affordance with no `forms` array. This intentionally makes strict materialization reject an affordance that has no executable route instead of silently ignoring it.

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

The projection runtime opens channels lazily. One `WotBindingChannelSlot` is shared for each compiled-form object within a generation, concurrent first use opens exactly once, a failed open is evicted for retry, and one caller's cancellation does not cancel the generation-scoped open for other callers. Disposal marks the slot closed before awaiting an in-flight open, then disposes any successfully created channel. Channel disposal must be idempotent, and subscription disposal must stop delivery and release its transport resources.

Do not create transport connections in the planner, binder constructor, or DI registration callback unless the executor itself explicitly owns a long-lived pooled client. Prefer an injectable client/session factory in options, as the built-in executors do.

### Payload codecs

The default `WotPayloadCodecRegistry` contains reflection-free JSON, text, and octet-stream codecs. A planner records the codec id, payload metadata, and resolved interaction schemas. Executors obtain codecs from `WotExecutorContext.Codecs`; HTTP validates the compiled action/event codec id and retains the selected codec instance at activation.

Custom codecs implement `IWotPayloadCodec` and return `WotEncodeResult` or `WotDecodeResult` rather than throwing for expected malformed input. Register custom codecs ahead of the built-ins with `WotPayloadCodecRegistry.Register`, or provide an `IWotCodecRegistry` through DI. Keep codecs deterministic, bounded, culture-invariant, and free of runtime type discovery.

To support complete actions or events, additionally implement
`IWotInteractionPayloadCodec`. Its methods own the complete wire representation;
the HTTP channel does not concatenate scalar codec bytes, bypass a custom codec
with JSON, or discard arguments and context. Scalar-only codecs retain their
existing scalar meaning and explicitly reject shapes they cannot represent.
The original JSON scalar `Encode`/`Decode` entry points remain compatible,
including legacy raw-text object/array decoding; schema-aware interaction
methods are separate optional capabilities on that same codec.
Successful HTTP action outputs and selected event values are checked against
the compiled native DataType, ValueRank and Structure ancestry using the native
type/factory infrastructure. Namespace and server indexes must resolve in the
returned value context; merely supplying a context is insufficient. A mismatch
fails with no partial action outputs or selected event fields. Custom codecs
still own their non-JSON wire representation, and valid values retain their
statuses, timestamps and diagnostic context without a replacement decode.

### Credentials and trust

Thing Descriptions and registry nodes contain only `WotSecurityDefinition` and `WotCredentialReference` data. Actual headers, query values, usernames, passwords, certificates, and trust anchors are resolved at channel activation or request time through `IWotCredentialProvider`.

Register a provider with `AddWotCredentialProvider`. Scope credentials by the reference's scheme name, binding URI, and endpoint. Fail closed when a form declares security but the provider cannot resolve the required material. Never serialize `WotCredential`, cache secret text in `WotCompiledForm`, or include secrets in diagnostics.

### Endpoint policy and custom schemes

`WotEndpointPolicy` is an allow-list that decides which endpoint URIs an executor may reach. It fails closed: the default set covers only the schemes the shipped bindings use (`http`, `https`, `modbus+tcp`, `modbus`, `mqtt`, `mqtts`, `opc.tcp`, `opc.https`, `opc.wss`), and it blocks loopback, RFC1918, CGNAT, link-local (including the cloud metadata address `169.254.169.254`) and IPv6 ULA ranges.

A custom binding almost always introduces a scheme the default set does not know about, so opening a channel fails with `BadSecurityChecksFailed` and `Endpoint scheme '<scheme>' is not in the policy's AllowedSchemes set` until the scheme is opted in:

```csharp
var endpointPolicy = new WotEndpointPolicy();
endpointPolicy.AllowedSchemes.Add("mem");
```

Add only the scheme your binding needs, and leave the address-range restrictions alone unless the deployment genuinely requires them relaxed — those blocks are what stop a Thing Description from steering an executor at the host's own listeners or at a cloud metadata endpoint.

#### Internationalized hosts

A `href` may name an internationalized host (`http://ü.example/x`). Percent-encoding is defined for a path, a query and a fragment and is **not** a spelling of a host, so the transmitted URI is rebuilt from its components rather than encoded as one string: the host becomes its IDNA A-label (`http://xn--tda.example/x`), and userinfo, an explicit port and an IPv6 literal are carried through unchanged. `WotProtocolBinderBase.ToTransmittedUri` produces the URI on the wire and `ToTransmittedAuthority` the authority the plan is scoped to, so `WotCompiledForm.Endpoint.Host`, `Endpoint.BaseUri`, `Addressing.Target` and every `WotCredentialReference.Endpoint` name one host.

`WotEndpointPolicy` is evaluated against the same A-label — `WotEndpointValidator.ToAsciiHost` exposes it. An allow list accepts either spelling of one name; a block list refuses either, because a policy that blocks `xn--tda.example` while the plan carries `ü.example` would block nothing.

### Registration

The direct-construction path is useful in focused tests. Note the policy passed alongside the binder and executor, which is what lets the sample's `mem://` endpoints resolve:

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

For a target-mapped or ordinary projected variable, `readproperty` and
`writeproperty` retain their own async handlers. An observe-only property, or an
`observeproperty` form distinct from its read form, uses `IWotBindingChannel.ObserveAsync`.
It does not require or fabricate an upstream read operation. A combined
read/observe form in the same document keeps the existing read-sampling path;
equal JSON Pointers in different documents do not establish that equivalence.

Active Value monitored items share one observation subscription per selected
source. `Sampling` and `Reporting` modes keep it active; `Disabled` does not.
Monitoring metadata such as DisplayName does not start it or prevent its release.
The first active Value subscriber starts the source and the last releases it.
New or re-enabled subscribers can receive its cached observation. Callbacks from
a stopped source cannot publish into its successor.

Compatible Core `ReloadRuntimeNodeSetAsync` handoff retains monitored-item
identity and registers those items with the replacement observation source
before reconciling subscriber counts. It does not inject a Node/cache read as
the resumed value. Graceful `ShadowReloadAsync` intentionally keeps existing
subscribers on the retiring source until they drain; replacement preparation
failure leaves the current source active.

Delivery checks each subscriber's current Read permissions and applies its own
IndexRange and data encoding without changing the shared complete value.
Usable Uncertain values, source timestamps and successful subcodes are retained.
Where no read form exists, a local Read returns the observation cache, initially
`BadWaitingForInitialData`; where a read form exists, that Read still invokes
the separately selected read source.

Startup failures produce a Bad observation status rather than switching to
polling. `WotProjectionBindingRuntimeOptions.MaxQueuedPropertyValues` bounds
pending delivery per variable (default 1024). Overflow reports
`BadResourceUnavailable` and releases the source instead of silently dropping
values. A source can be retried after all Value subscribers deactivate and one
reactivates. Last-subscriber shutdown cancels a pending source startup before
waiting for its release. It does not cancel a generation-wide shared channel
open; generation cancellation still does. The generation owns pending opens, subscriptions and delivery work
through cancellation and asynchronous disposal.

The bound is configurable through the same direct-construction or injected
runtime factory:

```csharp
var runtimeFactory = new WotProjectionBindingRuntimeFactory(
    channelFactory,
    resolver: null,
    new WotProjectionEventPublisher(),
    new WotProjectionConditionFactory(),
    new WotProjectionBindingRuntimeOptions
    {
        MaxQueuedPropertyValues = 256
    });
```

Direct channel consumers can also use `IWotBindingChannel.ObserveAsync` or `SubscribeEventAsync`. The returned `IWotSubscription` owns the native subscription or polling loop and must stop it in `DisposeAsync`.

### Structured target mapping

Direct mapping reads or writes the whole target value. Structured mapping groups forms by target variable and field path. Reads run all mapped field reads concurrently, build nested `IStructure` instances without reflection, and return one `ExtensionObject`. Writes extract each mapped field and run the field writes concurrently.

The runtime rejects a target that mixes direct and field mappings, duplicate mappings for the same field and operation, and target-mapped operations other than read, write, or observe. A failed field fails the entire structured operation. Uncertain values remain usable. The composed status is the first non-default status at the highest severity, with the oldest available source timestamp.

When an aggregate has a distinct or observe-only field source, every selected
observation field uses its actual observe channel. The aggregate waits for all
observed fields before reporting a usable complete value, retaining
`BadWaitingForInitialData` while a field is missing. Subsequent updates use the
other fields' latest observations, not unrelated read forms. A partial startup
failure releases all field subscriptions already acquired. A new startup does
not reuse values or callbacks from the failed set.

Structure type and field-path resolution is delayed until first structured use because runtime NodeSet configuration completes before custom encodeable types are registered in the shared factory. Failed resolution is not cached; later operations retry. Until resolution succeeds, the read, write, or observation reports `BadConfigurationError`. The registered type must expose the existing `IStructure` and datatype-definition contracts; merely being an `IEncodeable` is not sufficient for field navigation.

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

Binding code must remain compatible with trimming and NativeAOT. Parse form vocabulary with `JsonElement`; do not use runtime assembly scanning, unbounded reflection, `Type.GetType`, dynamic code generation, or serializer overloads that require runtime metadata. Reuse the schema-aware JSON interaction codec and registered native type factories for WoT payloads. Use source-generated JSON contexts for unrelated protocol-specific envelopes that need additional typed serialization.

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
- [ ] Test same-form read sampling and distinct/observe-only source subscriptions, including last-subscriber cleanup.
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
| Property observation | Shared Value subscribers, metadata-only subscribers, mode changes, stale callbacks, source context, per-item ranges/encoding, permission changes, startup/overflow failures, generation cancellation, complete field aggregation. |
| Materialization | Strict rejection, non-strict degradation, successful activation, failed shadow replacement retaining old generation, old monitored-item drain. |
| Packaging | Full base TFM matrix, executor source absent before `net8.0`, MQTT separate package, package README and dependency graph. |
| AOT/trimming | `net10.0` analyzer-clean build and NativeAOT publish/run smoke test for the concrete executor path. |

## Related documentation

* [WoT Connectivity model, server, registry, and client](WoTConnectivity.md)
* [WoT aggregation sample](../samples/WotCon/README.md) - exercises the complete generic projection runtime with two OPC UA source servers, runtime-loaded DI/Machinery/Pumps models, local monitored items, and shadow-generation replacement.
* [Dependency injection](DependencyInjection.md)
* [Runtime NodeSets](RuntimeNodeSets.md)

## Conformance to WoT Binding 1.1

The specification defines twelve conformance units and four recommended profiles
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
| **WoT-Projection** | covered | `WotProjection`, `WotProjectionResolver` and, for materialization, `WotProjectionViewBuilder` with `LifecycleWotViewProjectionHost` |

All four profiles - **WoT-Reader**, **WoT-Modeller**, **WoT-Converter** and
**WoT-ArchivalConverter** - are therefore satisfied by the units above.

The unit and profile names themselves are stated once, in
`Opc.Ua.Wot.WotBindingConformance`, together with the vocabulary revision this
library implements (`CurrentRevision`, `1.1`) and the profile nesting Section 11
defines. A document declares what it claims with `uav:profile` and the revision it
was authored against with `uav:bindingVersion` (Section 4.1); both are validated,
neither becomes a Node, and both are restated verbatim on a round trip.

### What the readable mapping does not yet carry

Section 9.2 emits the exceptional `uav:nodes` projection where converting the readable
document back would not reproduce an equivalent NodeSet. Two gaps in this
implementation still trigger it, both ordinary work rather than limits of the
vocabulary.

A Variable's own Variable children - the `EURange` and `EngineeringUnits` Properties of
an `AnalogUnitType` - sit one level deeper than the conversion descends, so they are
not emitted. And a Variable's `Value` is carried only where it is a scalar the
conversion special-cases; a structure is not carried at all.

Neither needs new vocabulary. A structure's value is self-describing: the
`ExtensionObject` states the identifier of the type it holds, `EUInformation` and
`Range` are types this stack already generates from the standard NodeSet, and the
encoder stack in `Opc.Ua.Types/Encoders` maps such a value to named JSON fields and
back. Nothing has to infer a unit's identifier from its symbol.

One convention is worth knowing when reading a generated document: completeness is
tested with `NodeSetComparer.CompareEquivalent`, which reads each side through its own
`Aliases` table, because Section 9.2 asks for an equivalent NodeSet and not an
identically spelled one. A name neither side declares is read through the
`INodeSetAliasResolver` the caller injects — here `WotNodeSetAliases`, which states
that the Binding writes the standard base-namespace names — so the comparison itself
states no policy of its own. `NodeSetComparer.Compare` keeps the stricter text
comparison for callers that need to know a document was reproduced as written.

### How this is checked

The specification publishes twenty-six worked examples, and two of them are a
golden pair: a projection document and the resolved view it is defined to
resolve to. `WotSpecExampleTests` embeds all twenty-six and runs the pair
through the resolver, asserting against the specification's own expected output
rather than against our reading of the prose. That covers, in one document, all
three selection forms, the bulk naming rule, the security closure naming and the
provenance term. Example 22 is additionally converted to check that a document
binds the node it projects to an existing type (Section 5.2.1) and constrains
its `auto` endpoint selection with a Section 5.7.1 floor. Three examples were
added by revision 1.1 and pin the corrections it made: a document whose texts
are authored in German and French while the default locale is `en`
(example 24, the code-point-first display fallback of Section 9.1.1), a Thing
Model that projects a ReferenceType Node with `uav:inverseName` and
`uav:symmetric` (example 25, Section 6.2.1), and one that projects a DataType
Node (example 26, Sections 5.2 and 6.11).

`WotSpecExampleTests.EveryPublishedExamplePassesStrictConformanceAndImports`
runs every one of the twenty-six through the whole reading pipeline — parse,
**strict** conformance validation, conversion, then serialize, re-read and
`Import`. A document that claims a profile covering `WoT-Modeller` has to
convert: the claim is what makes the conversion mandatory rather than optional.

#### Keeping the vendored examples honest

The examples are vendored byte-for-byte from the specification repository into
`tests/Opc.Ua.Types.Tests/Wot/Assets`, and `.gitattributes` marks `*.jsonld`
`eol=lf` so the checked-out bytes are the upstream bytes on every platform.
They used to be copied by hand with no record of the source, and they drifted:
one example gained a security floor upstream while the copy here kept the
superseded text, and a later example never arrived at all.

`spec-examples.manifest.json` beside them now records the source repository,
branch and commit, the vocabulary revision, and the size and SHA-256 of every
file. `WotSpecFixtureManifestTests` enforces it in three layers:

| Check | Needs | What it catches |
| --- | --- | --- |
| manifest set, count, numbering and per-file SHA-256 | nothing — offline, from embedded resources | an edited, replaced, added or dropped example, and a gap in the `NN-` numbering that would hide a missing tail |
| manifest provenance | nothing | a manifest that names no full source commit, or that records a revision this library does not implement |
| byte identity against the specification checkout | a sibling `spec-drafts` checkout, or `OPCUA_WOT_SPEC_DRAFTS` | a regeneration made from the wrong source, which would record wrong hashes just as consistently |

The third check is skipped, not failed, where no checkout is present, so CI needs
neither the network nor a second repository. Re-vendoring is the explicit
developer step `WotSpecFixtureManifestTests.RegenerateFromSpecCheckout`, which
copies the published examples over the vendored ones and rewrites the manifest
from `git` in the source checkout — so the diff a reviewer sees is the
specification's diff.

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
`WotCompositeNodeResolver` composes parts in the specified order and is what a
converter is handed. A compact model name is a hint and may match none, one or
several nodes; an `ExpandedNodeId` is definitive and matches one or none.

| Implementation | Part of the context | Assembly |
| --- | --- | --- |
| `SnapshotWotNodeResolver` | the sibling documents of the conversion | `Opc.Ua.WotCon.Server` |
| `AddressSpaceWotNodeResolver` | the types the Server has loaded | `Opc.Ua.WotCon.Server` |
| `NullWotNodeResolver` | holds nothing; the default | `Opc.Ua.Types` |

Both halves are composed with `WotCompositeNodeResolver` in the specified
order. The AddressSpace half is what lets a document bind to a type a companion
model defines — the primary use of §5.2.1 — and it is wired in by
`WotRegistryNodeManager` as soon as an `IServerInternal` exists. Without it a
document could only bind to a type a sibling projects, and because §5.2.1
forbids falling back to `BaseObjectType` a companion-model binding would fail
the projection instead of resolving.

`SnapshotWotNodeResolver` indexes the registry snapshot being converted. Only
Thing Models are indexed, and the decision uses the *registry's* `Kind` rather
than the document's own content: a Thing Model projects its root as a
`UAObjectType` and so is what a type binding can name, whereas a Thing
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

The shared `WotDocumentDeclarationIndex` also indexes authoritative native types
and their declarations. `SnapshotWotNodeResolver` exposes those non-root types
through the same snapshot index used for readable sibling models.
`WotResolvedNode.DirectSupertypeNodeIds` preserves direct source ancestry
separately from the nearest-first summary. `uav:includeInherited` controls
declaration expansion, not whether stated supertype references are checked.

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
from `BaseEventType`. That is the whole point of the mapping: a Client browsing
a type that fell back to `BaseEventType` would see none of the Condition state
and could not tell an alarm from an ordinary event.

The runtime projection follows the same rule. An event affordance that carries
`uav:conditionType` or `uav:conditionTypeId` materializes under the named
ConditionType, so an OPC UA event filter for that ConditionType, or for one of
its supertypes, can match the event. An action that carries
`uav:conditionAction` and `uav:actsOn` is routed to the corresponding OPC
10000-9 Condition Method on the Condition identified by the event affordance.

The two forms follow the hint-plus-pin pattern of Section 5.3.
`uav:conditionTypeId` supplies the definitive identity, not proof that the Node
is a Condition. The converter verifies that exact ObjectType and a bounded,
cycle-free ancestry reaching `ConditionType` (`i=2782`). `BaseObjectType`
(`i=58`) and its non-Condition subtypes cannot acquire Condition semantics by
being pinned. The four standard ConditionTypes resolve without external context:
`ConditionType`, `AcknowledgeableConditionType`, `AlarmConditionType` and
`LimitAlarmType`.

Supplied ancestry must be a single coherent chain: conflicting parents are
rejected in either link order, and a known standard identity does not hide
contradictory or cyclic references in the supplied context. Canonical exported
Thing Models annotated with `uav:eventType` resolve as ObjectTypes, just like
readable `uav:objectType` declarations.

For companion types, use the asynchronous converter with the existing local
node context. A unique `uav:conditionType` hint can resolve without a pin; a pin
can settle an otherwise unresolved hint only after its ancestry is verified.
Where both forms resolve to different types, conversion reports
`ConditionTypeConflict`. The verified binding retains the companion identity
for the emitted `HasSubtype` and governs inherited Condition action declarations
as well; it is not replaced by the standard ancestor used to verify it.

```csharp
WotConversionResult<UANodeSet> result = await WotNodeSetConverter.ToNodeSetResultAsync(
    document,
    options: null,
    thingResolver: siblingDocuments,
    resolutionContext: null,
    nodeResolver: localTypes,
    cancellationToken: cancellationToken);
```

`localTypes` is the supplied `IWotNodeResolver`, for example a
`WotDocumentNodeResolver` over explicit type declarations and their `tm:extends`
links. For a companion query anchor selecting the occurrence `EventId`, it also
supplies `IWotTypeDeclarationResolver`: a complete effective declaration set
identifies the field's owner, qualified name, native identity, DataType and rank.
The asynchronous conversion carries that evidence in the existing event selection
catalog. A linked effective DataSchema remains usable, but its shape or numeric
EventType pin is not a substitute for type/declaration context. Synchronous
conversion cannot verify an otherwise unknown companion pin.

Native and archive restoration retain their authoritative Nodes. Readable
Condition claims are checked against those actual ObjectTypes and their ancestry,
not merely against regenerated readable hints. Missing readable `data` or other
unasserted Condition facts do not demand synthesis of additional native Nodes.
A pin may name a companion or standard Condition ancestor rather than the
concrete event type, but it must remain within the Condition portion of that
ancestry. `BaseEventType` (`i=2041`) is not an eligible Condition pin.

The converter enforces the four Section 13.3/13.4 conformance rules, each
because breaking it yields a document a consumer can read but cannot act on, and
also rejects an unresolvable readable ConditionType name:

| Rule | Section | Diagnostic |
| --- | --- | --- |
| A Condition event declares the occurrence `EventId` in its own or linked `data` and any stated selection reaches its verified declaration | 6.1, 13.3 | `ConditionEventIdMissing` |
| `uav:conditionAction` is in the closed set | 13.2 | `InvalidConditionAction` |
| `uav:actsOn` names a Condition event in the same document | 13.4 | `InvalidConditionTarget` |
| `Acknowledge` / `Confirm` / `AddComment` declare an `EventId` input | 13.4 | `ConditionActionInputMissing` |
| `uav:conditionType` names a ConditionType this Binding resolves | 13.2 | `UnresolvedConditionType` |
| `uav:conditionType` and `uav:conditionTypeId` name the same type | 13.2 | `ConditionTypeConflict` |
| The ConditionType declares the Method `uav:conditionAction` names | 13.1, 13.4 | `ConditionActionNotDeclared` |
| A `data` member is a DataSchema naming one field | 13.3 | `EventFieldInvalid` |

Explicit vendor-qualified event fields remain ordinary fields even when their
local name is `EventId` or `Severity`. Materialization preserves the qualified
identity and the linked definition's namespace context. Namespace aliases for
one field and ambiguous unqualified duplicate declarations remain errors, as do
select clauses competing for the same output member.

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
the pairing names. That is what records the pairing structurally, so the
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
where it states a selection, shall select it — the resolved selection is what a
MonitoredItem is created with, so one that omits the field
describes a notification that never carries it. Every other Condition field is
present in `data` *where the affordance selects it* and is not otherwise
required; both are `ConditionEventIdMissing`. `Enable` and `Disable` act on the
Condition instance rather than one occurrence and are deliberately exempt from
the input rule.

Shelving, suppression, dialog conditions and `ConditionRefresh` are outside the
mapping, as Section 13.1 scopes it.

For the converter-default compatibility note, see
[Condition events derive from their ConditionType](WoTNodeSetConversion.md#condition-events-derive-from-their-conditiontype).

Current sample limitation: the upstream cavitation signal is proven to raise the
upstream alarm and leave it unacknowledged, but the Pump1 Asset's `Supervision`
view currently organizes no event affordance, Pump1 carries no `GeneratesEvent`
reference for its cavitation alarm, and acknowledgement does not round-trip
because the projected pump actions are Start, Stop and Reset rather than
Condition Methods carrying `uav:conditionAction` / `uav:actsOn`.

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
