# WoT Connectivity Protocol Bindings

The WoT Connectivity 1.1 runtime materializes Thing Descriptions and Thing Models into the OPC UA AddressSpace. Each interaction-affordance **form** in a document describes how to reach a value over a concrete protocol (HTTP, MQTT, Modbus, OPC UA, …). The **protocol binder** subsystem turns those forms into validated, immutable **binding plans** and, when an executor is present, drives the live transport operations.

The subsystem is deliberately layered so the model remains transport-neutral while the base Bindings package can bundle the dependency-compatible executors on modern .NET:

| Project, assembly, or namespace | Contents | Availability and dependencies |
| --- | --- | --- |
| `src/Opc.Ua.WotCon.Bindings` / `Opc.Ua.WotCon.Bindings` | Stable interfaces, plan model, codecs, the eight planner/validator binders, registry, and sample binder | Base package `OPCFoundation.NetStandard.Opc.Ua.WotCon.Bindings`; full `net472;net48;netstandard2.1;net8.0;net9.0;net10.0` matrix. |
| `Opc.Ua.WotCon.Bindings.Http` | HTTP executor and options, included in the base Bindings package | `net8.0`, `net9.0`, and `net10.0`; `HttpClient`. |
| `Opc.Ua.WotCon.Bindings.Modbus` | Modbus TCP client, executor, addressing, and conversion, included in the base Bindings package | `net8.0`, `net9.0`, and `net10.0`; sockets only. |
| `Opc.Ua.WotCon.Bindings.OpcUa` | OPC UA-to-OPC UA executor and options, included in the base Bindings package | `net8.0`, `net9.0`, and `net10.0`; `Opc.Ua.Client`. |
| `src/Opc.Ua.WotCon.Bindings.Mqtt` / `Opc.Ua.WotCon.Bindings.Mqtt` | MQTT executor and options | Separate `OPCFoundation.NetStandard.Opc.Ua.WotCon.Bindings.Mqtt` package for `net8.0`, `net9.0`, and `net10.0`; MQTTnet. |
| `Opc.Ua.WotCon.Server` | Materialization coordinator integration | references `Opc.Ua.WotCon.Bindings` only |

The base Bindings package keeps its full TFM matrix, but its concrete HTTP, Modbus, and OPC UA executor namespaces are compiled only for `net8.0`, `net9.0`, and `net10.0`. MQTT remains separate because it carries an optional external transport dependency. Planner-only use therefore remains available on every base-package TFM.

## Stable public interfaces

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

## Protocol coverage

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

## Runtime integration

`WotMaterializationCoordinator` compiles each resource's forms into a `WotBindingPlan` during **Prepare**, activates the plan only **after** the projection is committed as the active generation, and deactivates it **before** the projection is retired or unloaded.

* **Strict mode** (`WotRegistryServerOptions.StrictBindings = true`) fails the closure when any required form is unsupported or invalid.
* **Degraded mode** materializes nodes with `BadConfigurationError` and emits a `WoTBindingFailureEvent`. Validated-but-non-executable forms also degrade the closure so their nodes are visible but flagged.
* Binding capability snapshots populate the registry `SelectedBindings` node and contribute to refresh unchanged-detection.
* The legacy 1.02 `IWotAssetProviderFactory` provider model is preserved untouched.
* The coordinator passes its prepared `WotBindingPlan`s to the host as `WotProjectionDocument.BindingPlans` (an `ArrayOf<WotBindingPlan>`), so the projection host can wire a per-generation OPC UA binding runtime once the closure's NodeSet2 content has been imported.

## OPC UA target-mapping binding runtime

Once a closure's forms are materialized as NodeSet2 content, `LifecycleWotProjectionHost` wires each runtime NodeSet generation's `RuntimeNodeSetOptions.ConfigureAsync` to build a **projection binding runtime** from the document's `BindingPlans`. This implemented generic runtime drives live target-mapped value exchange between the resolved OPC UA variable and the compiled forms of its non-OPC-UA or OPC UA source; it is not limited to a protocol-specific projection.

* `IWotBindingChannelFactory` (implemented by `WotProtocolBinderRegistry` alongside `IWotBinderRegistry`) opens a live `IWotBindingChannel` for a compiled, executable form. Dependency injection registers the **same** `WotProtocolBinderRegistry` singleton for both interfaces regardless of whether `AddWotRegistryServer` or `AddWotProtocolBinders`/`Add<Protocol>WotBinding` is called first.
* `IWotTargetVariableResolver` (default `WotTargetVariableResolver`) resolves the target `BaseVariableState` a `WotTargetMappingDescriptor` declares against the freshly imported predefined nodes:
  * `uav:mapToNodeId` alone resolves that exact portable NodeId (parsed including `nsu=` forms against `INodeManagerBuilder.Context.NamespaceUris`) and requires a `BaseVariableState`.
  * `uav:mapToType` alone resolves the unique variable whose `DataType` equals the target type.
  * Both terms resolve the exact node and validate its `DataType` equals the declared target type.
  * Missing, malformed, ambiguous, wrong-node-class or type-mismatch mappings fail activation with a deterministic `ServiceResultException` status (`BadNodeIdInvalid` / `BadNodeIdUnknown` / `BadBrowseNameDuplicated` / `BadTypeMismatch`); every portable NodeId parse failure — including one the parser itself raises as a `ServiceResultException` — is wrapped as `BadNodeIdInvalid` naming the offending term (`uav:mapToNodeId` / `uav:mapToType`) rather than surfacing the parser's own exception shape.
* `IWotProjectionBindingRuntimeFactory` (default `WotProjectionBindingRuntimeFactory`) groups the closure's target-mapped, executable compiled forms by resolved target variable and returns a `WotProjectionBindingRuntime` — the `IAsyncDisposable` the NodeSet generation owns:
  * A **direct** target (`uav:mapToNodeId` and/or `uav:mapToType` alone) wires the executable `readproperty`/`writeproperty` forms as full async `OnRead`/`OnWrite` handlers that preserve the source `StatusCode` and `SourceTimestamp`; local monitored items sample the same read handler, so no second observe bridge is created for an `observeproperty` form on the same target.
  * A **structured** target (`uav:mapToType` + `uav:mapByFieldPath`) composes the value by reading every mapped field concurrently, building nested structures via `IEncodeableFactory` / `IStructure` / `IDataTypeDefinitionSource` (no reflection); writes extract and write each mapped field concurrently from the incoming structure. A single failing field fails the whole read or write; a successful read preserves a non-default `Good` status if any field reported one and uses the oldest non-`MinValue` `SourceTimestamp` across the fields, rather than always reporting plain `Good`/now.
  * Conflicting direct-vs-field mappings, duplicate read/write mappings for the same target/field, and unsupported target operations all fail activation deterministically. Everything else about a structured target that depends on its structure type being registered — the encodeable type lookup, root instance validation, and `uav:mapByFieldPath` path resolution (empty segments, unknown fields, array-valued or non-structure intermediate fields) — is deferred to the first structured read or write instead of failing activation, because `RuntimeNodeSetOptions.ConfigureAsync` runs before `NodeManagerLifecycle.RefreshComplexTypesAsync` registers the server's custom structure types. Resolution is retried, uncached, on every first use until it succeeds against the (by-then-populated) `IEncodeableFactory` instance; a still-unresolved first use returns a deterministic `BadConfigurationError` read/write status instead of throwing out of the request pipeline.
  * Channels are opened lazily and cached one-per-compiled-form for the generation; concurrent first use opens once, and a failed open is evicted so a later call can retry. Every successfully opened channel is disposed with the generation; disposal failures are aggregated. A channel open racing with, or started after, generation disposal never leaks: disposal marks the slot disposed under its lock so no later open can start, and still awaits and disposes a channel whose open was already in flight.
* Both abstractions are always available via direct construction (no DI container required) and are registered through `AddWotRegistryServer` using `TryAdd*` so a host application can supply its own implementation.

## Registering binders and executors

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

## Writing a custom binder (code-behind)

A third party contributes a binder as ordinary code. See the complete [binding-authoring guide](WoTBindingDevelopment.md) and the worked `Opc.Ua.WotCon.Bindings.Samples.MemoryWotBinder` implementation, which binds a fictitious `mem://` protocol to an in-process key/value store. The pattern is:

1. Derive from `WotProtocolBinderBase` and provide `Identity`, `Capability` and the handled `Schemes`.
2. Override `Match` (usually `MatchStandard(form, context, "yourv:")`) to claim forms deterministically.
3. Override `Compile` to validate the href/vocabulary and emit `WotCompiledForm` entries with endpoint/addressing/operation/payload metadata and JSON-Pointer diagnostics.
4. Optionally implement `IWotBindingExecutor` returning an `IWotBindingChannel` for read/write/observe/invoke.
5. Register with `builder.AddWotBinder(...)` and `builder.AddWotBindingExecutor(...)`.

Because the planner is separate from the executor, a custom binding can ship as a validator first and gain execution later without any change to the core model, server or coordinator.

The [WoT aggregation sample](WoTAggregationSample.md) exercises the complete generic projection runtime with two OPC UA source servers, runtime-loaded DI/Machinery/Pumps models, local monitored items, and shadow-generation replacement.

## Intentionally unsupported operations

* CoAP, BACnet, PROFINET and LoRaWAN ship as **planner-only** (non-executable) in this build.
* The Modbus binding does not support action invocation or events (Modbus has no such concept); those operations return `BadNotSupported`.
* The OPC UA executor implements read/write/invoke and **native** observe / event subscription (a `Subscription` / `MonitoredItem` pair per channel, Part 4 §5.12 / §5.13) — see [Operation coverage](#operation-coverage) below.
* The MQTT executor implements publish/subscribe; request/response RPC with a dedicated response topic is not modelled (actions publish only).

## Transport security

The executable bindings fail closed and never downgrade a secure form to an insecure transport:

* **MQTT** — an `mqtts://` href always enables TLS and defaults to port 8883; an `mqtt://` href stays explicit plaintext (port 1883). Username / password, the TLS client certificate and TLS trust anchors are resolved through the `IWotCredentialProvider`; a form that declares a security scheme is refused when the provider resolves no credential. Username / password over plaintext `mqtt://` is refused unless `MqttWotBindingOptions.AllowCredentialsOverPlaintext` is set.
* **HTTP** — the executor-owned `HttpClient` disables automatic redirects and applies a bounded, origin-aware redirect policy: custom header and query credentials are stripped across origins, redirect loops and non-`http(s)` schemes are refused, an `https`→`http` downgrade is refused unless `AllowInsecureRedirectDowngrade` is set, and the hop count is capped by `MaxAutomaticRedirects` (default 5). A caller-supplied client used with a credential-bearing form fails closed unless `HttpWotBindingOptions.CallerClientHandlesRedirectSafety` confirms the client handles redirects without leaking credentials.
* **Modbus** — `modv:address` must be 0–65535 and the addressed range (`address + quantity - 1`) must stay in the 16-bit space; function-only forms map exactly onto function codes 1, 2, 3, 4, 5, 6, 15 and 16, and op/function (or entity/function) mismatches are rejected. The executor re-validates the range before narrowing to `ushort` / `byte`.

## Operation coverage (OPC UA executor)

| Operation | Mechanism |
| --- | --- |
| `readproperty` | `Read` service (`ISession.ReadValueAsync`). |
| `writeproperty` | `Write` service; the mapped `StatusCode` is preserved. |
| `observeproperty` | A native data-change `MonitoredItem` (`AttributeId = Value`, queue size 1) on a dedicated `Subscription`; no client-side polling. |
| `invokeaction` | `Call` service; the method NodeId is `uav:id` and its owner object is resolved from `uav:componentOf`. |
| `subscribeevent` | A native event `MonitoredItem` (`AttributeId = EventNotifier`) selecting `EventId`, `EventType`, `SourceNode`, `SourceName`, `Time`, `ReceiveTime`, `Message` and `Severity`, plus any `uav:eventFields`-authored extra select clauses. Every selected field is delivered in `WotNotification.EventFields`, keyed by its browse path, with the event's own `Time` / `ReceiveTime` as the source / server timestamp. |

Both subscription kinds share one code path: a dedicated `Subscription` is created per channel subscription, its `MonitoredItem` is disposed and the subscription removed from the session (`ISession.RemoveSubscriptionAsync`) when the returned `IWotSubscription` is disposed, so no session or subscription is leaked — including when creation fails partway through.

A compiled form's NodeId (`uav:id`, and `uav:componentOf` for actions) is resolved with `NodeId.Parse` for the plain `ns=` / `i=` / `s=` / `g=` / `b=` forms; a portable NodeId carrying an `nsu=` namespace URI is parsed as an `ExpandedNodeId` and resolved against the connected session's namespace table, since `NodeId.Parse` alone cannot resolve a namespace URI without one.
