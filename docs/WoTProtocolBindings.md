# WoT Connectivity Protocol Bindings

The WoT Connectivity 1.1 runtime materializes Thing Descriptions and Thing Models
into the OPC UA AddressSpace. Each interaction-affordance **form** in a document
describes how to reach a value over a concrete protocol (HTTP, MQTT, Modbus,
OPC UA, …). The **protocol binder** subsystem turns those forms into validated,
immutable **binding plans** and, when an executor is present, drives the live
transport operations.

The subsystem is deliberately layered so the core model and server assemblies
carry **no transport dependencies**:

| Assembly | Contents | Dependencies |
| --- | --- | --- |
| `Opc.Ua.WotCon.Binding` | Stable interfaces, plan model, codecs, the eight planner/validator binders, the sample binder | model only (dependency-light, all TFMs) |
| `Opc.Ua.WotCon.Binding.Http` | HTTP executor | `HttpClient` (net8.0+) |
| `Opc.Ua.WotCon.Binding.Mqtt` | MQTT executor | MQTTnet (net8.0+) |
| `Opc.Ua.WotCon.Binding.Modbus` | Modbus TCP client + executor | sockets only (net8.0+) |
| `Opc.Ua.WotCon.Binding.OpcUa` | OPC UA executor (OPC UA-to-OPC UA) | `Opc.Ua.Client` (net8.0+) |
| `Opc.Ua.WotCon.Server` | Materialization coordinator integration | references `Opc.Ua.WotCon.Binding` only |

> The concrete executors target **net8.0/net9.0/net10.0**. The dependency-light
> planner assembly targets the full `net472;net48;netstandard2.1;net8.0;net9.0;net10.0`
> matrix so unsupported runtime protocols can still validate and compile plans on
> every framework.

## Stable public interfaces

All contracts live in the `Opc.Ua.WotCon.Binding` namespace.

* **Identification, version and capability**
  * `WotBindingIdentity` — a binder's stable `Id` + `Version` (`id@version` key).
    Multiple versions of a binding coexist.
  * `WotBindingSource` / `WotBindingMaturity` — the version-pinned specification a
    binder implements (URL, version/date, commit, standards maturity).
  * `WotBindingCapability` — supported operations, content types, executable flag;
    projects onto the generated `WoTBindingCapabilityDataType`.
  * `IWotBindingIdentification` — deterministic selection. A binder returns a
    `WotBindingMatch` (kind + priority) so selection uses pinned rules
    (explicit pin > vocabulary > subprotocol > scheme), **not the URI scheme
    alone**.
* **Form validation and compilation**
  * `WotFormExtractor` / `WotAffordanceForm` — reflection-free extraction of forms
    (with resolved `op` defaults, security scheme references and JSON Pointers).
  * `IWotBindingPlanner` — validates a form and compiles it into a
    `WotBindingCompilation` of immutable `WotCompiledForm` entries carrying
    `WotEndpointDescriptor` / `WotAddressingDescriptor` / `WotOperationDescriptor`
    / `WotPayloadDescriptor` metadata.
* **Payload codec selection**
  * `IWotPayloadCodec` / `IWotCodecRegistry` — reflection-free JSON, text and
    octet-stream codecs; protocol executors may register more.
* **Credential / trust reference lookup (no secrets in TD / registry nodes)**
  * `WotSecurityDefinition` / `WotCredentialReference` — secret-free scheme
    references parsed from `securityDefinitions`.
  * `IWotCredentialProvider` — resolves a reference into short-lived
    `WotCredential` material at runtime, out-of-band. No secret ever appears in a
    Thing Description or on a registry node.
* **Lifecycle and operations**
  * `IWotBindingExecutor` — `ActivateAsync` opens a per-form `IWotBindingChannel`.
  * `IWotBindingChannel` — `ReadAsync` / `WriteAsync` / `InvokeAsync` /
    `ObserveAsync` / `SubscribeEventAsync`, returning `WotReadResult` /
    `WotWriteResult` / `WotInvokeResult` with mapped `StatusCode`s.
* **Registry and structured diagnostics**
  * `IWotBinderRegistry` / `WotProtocolBinderRegistry` — the Prepare / Activate /
    Deactivate seam the coordinator uses.
  * `WotBindingDiagnostic` — severity + stable code + **RFC 6901 JSON Pointer**.

## Protocol coverage

Eight planner/validator binders ship in `Opc.Ua.WotCon.Binding`
(`WotBuiltInBinders.CreateAll()`). Each pins its exact source in
`Planners/WotBindingSources.cs`.

| Binding | Id | Pinned source | Maturity | Executable |
| --- | --- | --- | --- | --- |
| HTTP | `w3c.http` | W3C TD 1.1 (normative HTTP mapping) | REC | yes (`.Http`) |
| CoAP | `w3c.coap` | W3C Binding Templates CoAP | Editor's Draft | planner only |
| MQTT | `w3c.mqtt` | W3C Binding Templates MQTT | Editor's Draft | yes (`.Mqtt`) |
| Modbus TCP | `w3c.modbus` | W3C Binding Templates Modbus | Editor's Draft | yes (`.Modbus`) |
| BACnet | `w3c.bacnet` | W3C Binding Templates BACnet | Editor's Draft | planner only |
| PROFINET | `w3c.profinet` | WoT PROFINET contribution | Unofficial Draft | planner only |
| LoRaWAN | `w3c.lorawan` | WoT LoRaWAN contribution | Unofficial Draft | planner only |
| OPC UA | `opc.opcua` | OPC 10101 (OPC UA WoT Connectivity) | OPC specification | yes (`.OpcUa`) |

Notes:

* The **W3C Binding Templates registry is a pilot and currently empty**; no
  binder ever reports `RegistryCurrent`. Drafts expose their Editor's Draft
  maturity; OPC UA exposes the OPC specification maturity.
* BACnet, PROFINET, LoRaWAN and CoAP perform **schema / document-level planning
  only** and are reported as **non-executable** — the runtime materializes their
  nodes but marks the closure degraded so callers know they cannot be driven yet.
* Each planner validates the href scheme and the currently-defined vocabulary
  terms of its pinned document, checks `op` compatibility, `contentType` and
  required fields, produces immutable endpoint/addressing/operation/payload
  metadata and returns precise errors/warnings with JSON Pointers.

## Runtime integration

`WotMaterializationCoordinator` compiles each resource's forms into a
`WotBindingPlan` during **Prepare**, activates the plan only **after** the
projection is committed as the active generation, and deactivates it **before**
the projection is retired or unloaded.

* **Strict mode** (`WotRegistryServerOptions.StrictBindings = true`) fails the
  closure when any required form is unsupported or invalid.
* **Degraded mode** materializes nodes with `BadConfigurationError` and emits a
  `WoTBindingFailureEvent`. Validated-but-non-executable forms also degrade the
  closure so their nodes are visible but flagged.
* Binding capability snapshots populate the registry `SelectedBindings` node
  and contribute to refresh unchanged-detection.
* The legacy 1.02 `IWotAssetProviderFactory` provider model is preserved
  untouched.

## Registering binders and executors

The planner binders are opt-in and replaceable:

```csharp
builder
    .AddWotRegistryServer(o => o.StrictBindings = false)
    .AddHttpWotBinding()                 // planners + HTTP executor
    .AddModbusWotBinding()               // + Modbus TCP executor
    .AddMqttWotBinding()                 // + MQTT executor
    .AddOpcUaWotBinding(o => o.SessionFactory = ConnectSessionAsync);
```

Each `Add<Protocol>WotBinding` registers the eight planner binders (idempotently)
and its executor. Without any executor, `AddWotProtocolBinders()` still validates
and compiles plans, materializing non-executable nodes.

Replace or add binders directly:

```csharp
builder.AddWotBinder(new MyCustomBinder());               // custom planner
builder.AddWotBindingExecutor(new MyCustomExecutor());    // custom executor
builder.AddWotCredentialProvider(new VaultCredentialProvider());
```

Selection is deterministic: the registry evaluates binders in ordinal
`id@version` order and chooses the highest-priority `WotBindingMatch`.

## Writing a custom binder (code-behind)

A third party contributes a binder as ordinary code. See the worked sample
`Opc.Ua.WotCon.Binding.Samples.MemoryWotBinder` (a fictitious `mem://` protocol
bound to an in-process key/value store). The pattern is:

1. Derive from `WotProtocolBinderBase` and provide `Identity`, `Capability` and
   the handled `Schemes`.
2. Override `Match` (usually `MatchStandard(form, context, "yourv:")`) to claim
   forms deterministically.
3. Override `Compile` to validate the href/vocabulary and emit `WotCompiledForm`
   entries with endpoint/addressing/operation/payload metadata and JSON-Pointer
   diagnostics.
4. Optionally implement `IWotBindingExecutor` returning an `IWotBindingChannel`
   for read/write/observe/invoke.
5. Register with `builder.AddWotBinder(...)` and
   `builder.AddWotBindingExecutor(...)`.

Because the planner is separate from the executor, a custom binding can ship as a
validator first and gain execution later without any change to the core model,
server or coordinator.

## Intentionally unsupported operations

* CoAP, BACnet, PROFINET and LoRaWAN ship as **planner-only** (non-executable) in
  this build.
* The Modbus binding does not support action invocation or events (Modbus has no
  such concept); those operations return `BadNotSupported`.
* The OPC UA executor implements read/write/invoke and **native** observe /
  event subscription (a `Subscription` / `MonitoredItem` pair per channel, Part
  4 §5.12 / §5.13) — see [Operation coverage](#operation-coverage) below.
* The MQTT executor implements publish/subscribe; request/response RPC with a
  dedicated response topic is not modelled (actions publish only).

## Transport security

The executable bindings fail closed and never downgrade a secure form to an
insecure transport:

* **MQTT** — an `mqtts://` href always enables TLS and defaults to port 8883; an
  `mqtt://` href stays explicit plaintext (port 1883). Username / password,
  the TLS client certificate and TLS trust anchors are resolved through the
  `IWotCredentialProvider`; a form that declares a security scheme is refused
  when the provider resolves no credential. Username / password over plaintext
  `mqtt://` is refused unless `MqttWotBindingOptions.AllowCredentialsOverPlaintext`
  is set.
* **HTTP** — the executor-owned `HttpClient` disables automatic redirects and
  applies a bounded, origin-aware redirect policy: custom header and query
  credentials are stripped across origins, redirect loops and non-`http(s)`
  schemes are refused, an `https`→`http` downgrade is refused unless
  `AllowInsecureRedirectDowngrade` is set, and the hop count is capped by
  `MaxAutomaticRedirects` (default 5). A caller-supplied client used with a
  credential-bearing form fails closed unless
  `HttpWotBindingOptions.CallerClientHandlesRedirectSafety` confirms the client
  handles redirects without leaking credentials.
* **Modbus** — `modv:address` must be 0–65535 and the addressed range
  (`address + quantity - 1`) must stay in the 16-bit space; function-only forms
  map exactly onto function codes 1, 2, 3, 4, 5, 6, 15 and 16, and
  op/function (or entity/function) mismatches are rejected. The executor
  re-validates the range before narrowing to `ushort` / `byte`.

## Operation coverage (OPC UA executor)

| Operation | Mechanism |
| --- | --- |
| `readproperty` | `Read` service (`ISession.ReadValueAsync`). |
| `writeproperty` | `Write` service; the mapped `StatusCode` is preserved. |
| `observeproperty` | A native data-change `MonitoredItem` (`AttributeId = Value`, queue size 1) on a dedicated `Subscription`; no client-side polling. |
| `invokeaction` | `Call` service; the method NodeId is `uav:id` and its owner object is resolved from `uav:componentOf`. |
| `subscribeevent` | A native event `MonitoredItem` (`AttributeId = EventNotifier`) selecting `EventId`, `EventType`, `SourceNode`, `SourceName`, `Time`, `ReceiveTime`, `Message` and `Severity`, plus any `uav:eventFields`-authored extra select clauses. Every selected field is delivered in `WotNotification.EventFields`, keyed by its browse path, with the event's own `Time` / `ReceiveTime` as the source / server timestamp. |

Both subscription kinds share one code path: a dedicated `Subscription` is
created per channel subscription, its `MonitoredItem` is disposed and the
subscription removed from the session (`ISession.RemoveSubscriptionAsync`) when
the returned `IWotSubscription` is disposed, so no session or subscription is
leaked — including when creation fails partway through.

A compiled form's NodeId (`uav:id`, and `uav:componentOf` for actions) is
resolved with `NodeId.Parse` for the plain `ns=` / `i=` / `s=` / `g=` / `b=`
forms; a portable NodeId carrying an `nsu=` namespace URI is parsed as an
`ExpandedNodeId` and resolved against the connected session's namespace table,
since `NodeId.Parse` alone cannot resolve a namespace URI without one.
