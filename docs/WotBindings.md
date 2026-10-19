# WoT bindings and NodeSet conversion

This guide covers protocol configuration, WoT/NodeSet authoring, and custom
binders. For server/client usage see [WoT Connectivity](WoTConnectivity.md);
for registry ownership and prepared publication see [WoT design](WoT.md).
The [WoT-Con 1.02 asset provider](WoTConnectivity.md#wot-con-102-compatibility)
is separate from the registry's compiled binding runtime.

## Contents

- [Bindings that ship today](#bindings-that-ship-today)
- [Registering binders and executors](#registering-binders-and-executors)
- [Transport security](#transport-security)
- [Authoring target mapping](#authoring-target-mapping)
- [Property operations and observation](#property-operations-and-observation)
- [Action payloads and native Methods](#action-payloads-and-native-methods)
- [Portable browse-path targets](#portable-browse-path-targets)
- [Event field selection](#event-field-selection)
- [Projected Methods, events, and Conditions](#projected-methods-events-and-conditions)
- [Conversion entry points](#conversion-entry-points)
- [Independent readable models](#independent-readable-models)
- [Identity and preservation](#identity-and-preservation)
- [Resolving a type binding: the local context](#resolving-a-type-binding-the-local-context)
- [Resolving a relation: companion ReferenceTypes](#resolving-a-relation-companion-referencetypes)
- [DataTypes, ranks, arguments, and model vocabulary](#datatypes-ranks-arguments-and-model-vocabulary)
- [Engineering units, ranges, and localized text](#engineering-units-ranges-and-localized-text)
- [Conformance and authoring validation](#conformance-and-authoring-validation)
- [Adding your own binding](#adding-your-own-binding)

## Bindings that ship today

The base package is `OPCFoundation.NetStandard.Opc.Ua.WotCon.Bindings`,
with `net48;net8.0;net9.0;net10.0` targets. Planners are available on every target.
Concrete HTTP, Modbus, and OPC UA executor namespaces are included only on
`net8.0`, `net9.0`, and `net10.0`. MQTT has a separate package,
`OPCFoundation.NetStandard.Opc.Ua.WotCon.Bindings.Mqtt`, on those modern targets.
Use the plural `Bindings` artifact/namespace names.

`WotBuiltInBinders.CreateAll()` provides eight version-pinned planners:

| Binding id | Source/maturity | Executor |
| --- | --- | --- |
| `w3c.http` | W3C TD 1.1 normative HTTP mapping, REC | Bundled HTTP |
| `w3c.coap` | W3C Binding Templates, Editor's Draft | Planner only |
| `w3c.mqtt` | W3C Binding Templates, Editor's Draft | Separate MQTT package |
| `w3c.modbus` | W3C Binding Templates, Editor's Draft | Bundled Modbus TCP |
| `w3c.bacnet` | W3C Binding Templates, Editor's Draft | Planner only |
| `w3c.profinet` | WoT contribution, Unofficial Draft | Planner only |
| `w3c.lorawan` | WoT contribution, Unofficial Draft | Planner only |
| `opc.opcua` | OPC 10101 | Bundled OPC UA |

The Binding Templates registry is a pilot and currently empty; these binders
report their pinned maturity, not `RegistryCurrent`.
Planner-only forms are validated but non-executable and degrade a non-strict
projection. Modbus has no action/event operations. MQTT actions publish only;
dedicated-response-topic RPC is not modeled.
HTTP events use JSON polling, not SSE/WebSub.

## Registering binders and executors

```csharp
builder
    .AddWotRegistryServer(options => options.StrictBindings = false)
    .AddHttpWotBinding()
    .AddModbusWotBinding()
    .AddMqttWotBinding()
    .AddOpcUaWotBinding(options => options.SessionFactory = ConnectSessionAsync);
```

Each protocol extension idempotently registers the planners and its executor.
`AddWotProtocolBinders()` alone supports planning, not live I/O.
The server otherwise uses `NullWotBinderRegistry`: strict closures fail;
non-strict closures expose degraded nodes.

`IWotBinderRegistry` is the runtime-neutral Prepare/Activate/Deactivate interface.
`WotProtocolBinderRegistry` also implements `IWotBindingChannelFactory`;
DI aliases both to the same singleton independent of registration order.
Prepare compiles immutable plans without transport I/O. Activation/deactivation
notifications observe committed generations. Channels/subscriptions are owned
by the source generation and disposed when it drains, not when a notification
alone reports deactivation.

`SupportedBindings` is a browseable folder, including when empty. Each descriptor
exposes URI, title, profile version, draft maturity, enabled state, content types,
and capabilities. `Enabled` means effective executable operations, not remote
endpoint connectivity. Optional custom metadata stays absent when not supplied.
Conflicting descriptors for the same URI/version fail configuration.

`SelectedBindings` is a detached deterministic snapshot of published plans,
not every registered binder. Direct callers use
`GetSelectedBindingCapabilitiesAsync`. Register the generated Structure when
decoding it directly:

```csharp
session.MessageContext.Factory.Builder
    .AddEncodeableType<WoTBindingCapabilityDataType>()
    .Commit();
```

## Transport security

TDs/plans contain only security definitions and `WotCredentialReference` values.
Resolve actual credentials, certificates, and trust anchors out of band through
`IWotCredentialProvider`, registered with `AddWotCredentialProvider`.
Scope them to scheme name, binding URI, and endpoint. Missing required credentials
fail closed; never put secrets in documents, plans, registry nodes, or diagnostics.

`WotEndpointPolicy` allows the shipped schemes (`http`, `https`, `modbus+tcp`,
`modbus`, `mqtt`, `mqtts`, `opc.tcp`, `opc.https`, `opc.wss`) and blocks loopback,
RFC1918, CGNAT, link-local/cloud metadata, and IPv6 ULA addresses.
Add only required custom schemes:

```csharp
var endpointPolicy = new WotEndpointPolicy();
endpointPolicy.AllowedSchemes.Add("mem");
```

Host validation and credential scoping use the same IDNA A-label for
internationalized names. URI components are encoded separately; path/query
encoding is not a hostname spelling. `?id=` is decoded once as one query
component, preserving other endpoint query components; duplicate `id` keys fail.

| Transport | Security behavior/configuration |
| --- | --- |
| MQTT | `mqtts` always enables TLS, default port 8883; `mqtt` is explicit plaintext, default port 1883. Plaintext credentials are refused unless `AllowCredentialsOverPlaintext` is deliberately enabled. |
| HTTP | Owned clients disable automatic redirects and use an origin-aware policy. Header/query credentials are stripped across origins; loops and non-HTTP schemes fail. HTTPS downgrade requires `AllowInsecureRedirectDowngrade`; `MaxAutomaticRedirects` defaults to 5. Credential-bearing caller clients require `CallerClientHandlesRedirectSafety`. |
| Modbus | Address and addressed range stay within 0–65535. Functions 1, 2, 3, 4, 5, 6, 15, and 16 must agree with entity/operation; execution rechecks ranges before narrowing. |

### OPC UA endpoint constraints

An `auto` scheme may set a floor:

```json
{
  "scheme": "auto",
  "uav:minimumSecurity": {
    "uav:securityMode": "Sign",
    "uav:securityPolicy": "Basic256Sha256"
  }
}
```

`ConstrainedSessionFactory` receives `OpcUaWotSessionRequest.MinimumSecurity`
and `SecurityRequirements`. Alternatively configure `EndpointDiscovery` plus
`SelectedEndpointSessionFactory`; the executor selects an eligible endpoint
before opening it. Ranking is strongest mode, policy, securityLevel, then
Unicode-code-point endpoint URL and original response position.
No eligible endpoint means `BadSecurityModeRejected`, not a weaker fallback.

The endpoint-blind `SessionFactory` is suitable only for unconstrained forms.
Constrained forms with only that factory fail `BadConfigurationError`.
`uav:channelsec` specifies an exact mode/policy; `uav:authentication` specifies
the actual Anonymous/UserName/Certificate/IssuedToken identity.
`allOf` and `oneOf` preserve whole alternatives without combining incompatible
channels/identities. Invalid references, cycles, or depth/alternative bounds
fail before connection. `uav:issueToken` is a secret-free provider reference.

Custom selection can reuse:

```csharp
EndpointDescription? selected = OpcUaWotEndpointSelector.Select(
    discovered, request.MinimumSecurity, request.SecurityRequirements);
```

The executor verifies the established Session again. Channel mismatch reports
`BadSecurityModeRejected`; identity mismatch reports `BadIdentityTokenRejected`.
Rejected owned Sessions are disposed, borrowed Sessions are not.
Certificate trust and credential/token policy remain application configuration.

## Authoring target mapping

[OPC 10101 §6.5.4](https://reference.opcfoundation.org/specs/OPC-10101/6.5.4)
defines protocol-neutral target mapping on **property affordances**, never forms:

| Term | Meaning |
| --- | --- |
| `uav:mapToNodeId` | Exact local target Variable |
| `uav:mapToType` | Target Variable DataType; without a NodeId requires a unique Variable |
| `uav:mapByFieldPath` | Field of the structured target; requires `mapToType` |

With both NodeId and type, the target must have that DataType. Values must be
nonempty strings; action/event or form-level declarations fail.
The registry validates once and attaches `WotTargetMappingDescriptor` to each
compiled operation.

```json
{
  "properties": {
    "temperature": {
      "type": "number",
      "uav:mapToNodeId": "nsu=urn:example:aggregate;s=Device1.Temperature",
      "forms": [{
        "href": "https://sensor.example.test/temperature",
        "op": "readproperty"
      }]
    }
  }
}
```

For a structured field, use `uav:mapToType` plus a field path such as
`"uav:mapByFieldPath": "VoltageL1N"`. The form may use HTTP, Modbus, MQTT,
OPC UA, or a custom protocol; see the
[Modbus example](https://reference.opcfoundation.org/specs/OPC-10101/8.2).

Ordinary projected properties need no explicit target terms:
`WotConversionOutput.ProjectedAffordances` identifies their actual local
Variables. Upstream form addresses never define local interaction identities.
Missing authored local identities, wrong classes, ambiguous declarations, or
conflicting ownership fail; native partitions are not overlaid with missing Nodes.
Native Variables without forms/target mappings retain their declared local Value.

## Property operations and observation

The runtime selects the first executable form per operation in authored order.
Failure does not retry another source, and writes do not fan out across
alternative forms. Different properties claiming the same target/field/direction
are conflicting declarations.

Direct reads preserve mapped status, usable Uncertain values, and source
timestamps. Structured reads concurrently acquire fields and build registered
`IStructure` values without reflection. A Bad field fails the operation;
otherwise the first non-default status at highest severity and oldest available
source timestamp describe the result. Writes preserve Good subcodes such as
`GoodClamped`. Mixed direct/field mappings, array/non-structure intermediate
paths, and unsupported directions fail.
Structure/path resolution occurs at first use after custom types are registered;
an unresolved use reports `BadConfigurationError` and is not cached forever.

`WotReadResult.Context`, `WotNotification.Context`, and contextual writes carry
source namespace/server/factory authority. Namespace-bearing values translate
by URI, without extending the remote Session table. Context-free channels support
namespace-independent values, not reinterpretation of local indexes.
Opaque values that cannot be safely translated fail explicitly.

`IWotPropertyBindingChannel` supports `WotReadRequest`/`WotWriteRequest` with
IndexRange, encoding, and message context. OPC UA forwards native ranges and
encoding names; other channels apply local read processing and reject indexed
writes before I/O. Structured writes do not apply one range to every field.

```csharp
if (channel is IWotPropertyBindingChannel propertyChannel)
{
    WotReadResult slice = await propertyChannel.ReadAsync(
        new WotReadRequest(callerContext, NumericRange.Parse("1")), ct);
}
```

Distinct/observe-only forms use `ObserveAsync` and native push where available.
A combined read/observe form from the same document retains read sampling.
Active Value items in Sampling/Reporting share one source subscription; Disabled
items and metadata-only items do not. The last Value consumer releases it.
Observe-only local reads use the cache, initially `BadWaitingForInitialData`;
a separately declared read form continues using its own source.
Delivery checks each consumer's permissions, range, and encoding.

`MaxQueuedPropertyValues` defaults to 1024. Overflow reports
`BadResourceUnavailable` and releases the source rather than silently dropping
values. Retry is possible after all Value consumers deactivate and one reactivates.
Graceful shadow reload keeps old consumers on their source until drain.
Compatible immediate handoff reconciles replacement subscriptions without
inventing a Node/cache read as a source notification.

HTTP/Modbus use `PollingWotSubscription`. Both thrown failures and mapped Bad
statuses are visible notifications. `RetryPolicy` defaults to
`ExponentialBackoffChannelReconnectPolicy` (500 ms doubling to 30 s, unlimited
attempts); backoff never polls faster than the interval and a healthy poll resets
it. Modbus `modv:pollingTime` overrides `ObserveInterval`; `modv:timeout` is a
request timeout. HTTP uses `HttpWotBindingOptions.ObserveInterval`.
Modbus reconnects faulted sockets on the next transaction.

## Action payloads and native Methods

Actions distinguish absent schemas (zero arguments), a single whole value,
and named positional arguments. A Structure/Union is one value, not flattened
members. Named layouts require complete `uav:fieldOrder`, not JSON member order:

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

HTTP inputs `[-7, 42]` encode as `{"Minimum":-7,"Maximum":42}`.
An empty named object is `{}` on the wire; an explicit null single value is not
an absent contract. Outputs must have the exact declared count/types/members.
Malformed/missing/extra output fails without a partial Good response.
Explicit `uav:valueRank`/ArrayDimensions retain array semantics.

Planners retain `InputLayout`, `OutputLayout`, and detached `WotPayloadSchema`
facts in `WotPayloadDescriptor`. Capture uses the original owning document/context
before detachment. A relative form href does not retag the affordance's DataTypes.
`ValidateInputs`, `ValidateArgumentLayouts`, and `ValidateMethodSignature`
provide preflight checks. Actual projected Method names/types/ranks must agree;
abstract numeric declarations permit compatible native concrete refinements.
JSON optionality/defaults do not authorize shortening an ordinary native signature.

Use explicit caller context for namespace-bearing inputs:

```csharp
var request = new WotInvokeRequest(
    inputs, messageContext, DiagnosticsMasks.OperationAll);
WotInvokeResult result = await contextualChannel.InvokeAsync(request, ct);
ServiceResult operation = result.OperationResult;
ArrayOf<ServiceResult> inputResults = result.InputArgumentResults;
```

`IWotContextualBindingChannel` accepts this request. Direct activation can use
`WotExecutorContext.WithMessageContext`, registry `MessageContext`, or
`OpenChannelAsync(form, messageContext, ct)`; DI honors `IServiceMessageContext`.
`IWotContextualBindingChannelFactory` lets the projected host supply its actual
namespace/type context.
Native diagnostic text is resolved from the source StringTable before returning;
the receiving server applies its own diagnostic mask/permissions and table.
Invalid counts/indexes/depth fail decoding. The source authorizes additional
diagnostics independently.

The JSON interaction codec uses registered encodeable/Structure metadata.
Int64/UInt64 follow numeric TD schemas; string LocalizedText uses text.
Finite numbers must fit their native Float/Double; explicit special-value strings
require a schema admitting them. Numeric constraint comparison preserves decimal
digits/magnitude rather than narrowing to Double/Decimal.
Unsupported nonzero exponents outside Int32 fail comparison.
`MaxPayloadBytes` counts actual UTF-8 including syntax; `MaxPayloadDepth` counts
root object/array as one, scalars as zero. Message-context `MaxArrayLength`
also bounds Structure-array outer allocation.
Custom `IWotInteractionPayloadCodec` implementations own complete action/event
wire shapes; scalar-only codecs reject unsupported shapes instead of losing inputs.

## Portable browse-path targets

OPC UA source forms may address a Node by portable `uav:id`, an encoded href
`?id=`, or `uav:browsePath`. Paths support read/write/observe, Call, and event
subscription on .NET 8+. This is source addressing, not View placement or
automatic Call-receiver inference.

```json
{
  "@context": {
    "uav": "http://opcfoundation.org/UA/WoT-Binding/",
    "plant": "urn:example:plant"
  },
  "title": "Plant temperature",
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

Configure security for the deployment; this addressing fragment does not
prescribe unsecured access. The endpoint keeps `/UA/Factory/`.
Leading `/` starts at Root (`i=84`). Relative paths use the nearest enclosing
explicit anchor, otherwise the nearest `uav:id`; an outer explicit anchor wins
over a nearer fallback identity. Missing/invalid relative anchors fail.
The closest path declaration (form, affordance, root) retains the context where
it was authored, not a later form-local prefix override.

| Syntax | Interpretation |
| --- | --- |
| `/Objects/plant:Pump` | Hierarchical path from Root |
| `plant:Pump.plant:Temperature` | Hierarchical then Aggregates |
| `<HasComponent>plant:Temperature` | Forward reference including subtypes |
| `<#HasComponent>plant:Temperature` | Exact reference, excluding subtypes |
| `<!HasComponent>plant:Parent` | Inverse reference |
| `plant:Pump&/Primary` | Literal slash in one BrowseName |
| `{https://example.org/model/}Temperature` | URI-qualified BrowseName |
| `nsu=https://example.org/model/;Temperature` | Alternative URI qualification |

Annex A escapes preserve reserved characters; slashes inside URI qualifiers are
not step separators. Numeric namespace prefixes are non-portable.
Named ReferenceTypes must exist in the connected Session's type tree.
Before business I/O the channel translates against current Session tables,
requires one complete local target, verifies simultaneous NodeIds agree,
and checks NodeClass (Variable, Method, or event Object/View).
Missing, partial, remote, or ambiguous targets fail before the operation.
`uav:callObjectId` remains a separate receiver; component placement is not one.

Subscriptions use native monitored items and revalidate after Session changes
and periodically for AddressSpace changes. Replacement items prepare disabled,
enable after context admission, and obtain a fresh sample after recovery.
Known stale addresses emit Bad status rather than apparently valid data.
Disposing `IWotSubscription` stops maintenance/callbacks and removes subscriptions.
`DisposeSession` governs Session ownership, not subscription ownership.

```csharp
services.AddWotProtocolBinders().AddOpcUaWotBinding(options =>
{
    options.SessionFactory = ConnectSessionAsync;
    options.BrowsePathRefreshInterval = TimeSpan.FromSeconds(5);
});
```

The refresh interval defaults to ten seconds; `TimeProvider` is injectable.
`WotBindingBounds.MaxBrowsePathElements` defaults to 64; `MaxUriLength` bounds
text. `WotBrowsePathTarget.FromForm` has separate configurable offline ceilings.
`WotAddressingDescriptor.BrowsePathTarget` retains URI-qualified steps, not
cached Session indexes. Direct forms need the enclosing context explicitly.
NodeId-only forms keep their path; a supplied malformed/inconsistent path
is never ignored.

## Event field selection

Author `tm:ref` and `uav:eventSelectClauses` on **event affordances**, not forms,
property/action schemas, or event payloads. The referenced definition is an
event affordance or TM root with `uav:eventType`, portable `uav:id`, and object
`data`. A bare NodeId is not a definition.

```json
{
  "tm:ref": "./event-types.tm.jsonld#/events/highTemperature",
  "uav:eventSelectClauses": [{
    "tm:ref": "./event-types.tm.jsonld#/events/highTemperature",
    "uav:browsePath": "Severity"
  }]
}
```

References may use document locations/pointers or contextual logical identifiers.
Resolution uses held documents, configured resolvers, then the well-known Core
catalog, detecting conflicting answers. The library does not infer network
authorization from a reference. The definition's ordered leaves form a baseline;
explicit clauses replace matching baseline members and append in authored order.
Without a linked baseline, explicit clauses are the complete selection.
With neither, the eight mandatory BaseEventType fields are selected.

Resolve before synchronous planning through
`WotBindingPlanRequest.FromDocumentAsync` or `ResolveEventSelectionsAsync`, then
pass the resulting catalog to `FromDocument`. A planner receiving unresolved
stated clauses fails `EventSelectClauseInvalid`, not hidden I/O.

Each clause carries exactly `tm:ref` and a relative `uav:browsePath`.
TypeDefinitionId is the linked query anchor, not necessarily the field owner.
Empty path selects the Condition NodeId Attribute; other paths select Value.
`WhereClause`/ContentFilter and clause `uav:typeDefinitionId` are unsupported.
Qualification resolves against original contexts and Session namespace tables.

| Path | Public `WotNotification.Data` member |
| --- | --- |
| Empty | `ConditionId` |
| `Severity` | `Severity` |
| `EnabledState/Id` | Nested `EnabledState.Id` |
| `EnabledState` | Nested `EnabledState.Name` |

`EventFields` is the flat transport index keyed by joined paths, not the public
nested JSON shape. Two clauses targeting one data-member path fail even with
different query anchors/namespace-qualified spellings. URI slashes inside a
qualified element do not add nested members.
HTTP event polling decodes the root data object and populates both representations
with typed values/context. Malformed payloads produce Bad notifications.

The earlier form-level `uav:eventFields` spelling is read but never emitted.
It adds bare browse names to the default; standardized selection wins when both
appear and reports the conflict. This vocabulary compatibility is distinct from
the [WoT-Con 1.02 management interface](WoTConnectivity.md#wot-con-102-compatibility).

## Projected Methods, events, and Conditions

Each `WotBindingPlan.ProjectedAffordances` entry carries local identity, owner,
and JSON Pointer separately from upstream addressing. Local Methods use the
async result hook and return source status/argument errors. They select one
executable form; no success is inferred merely from local invocation.

`uav:conditionType`/`uav:conditionTypeId` identify the event's ConditionType.
`uav:conditionAction` is one of Acknowledge, Confirm, AddComment, Enable, Disable;
`uav:actsOn` identifies the paired event in the same document.
Companion types require verified, bounded, coherent ancestry reaching
`ConditionType`, not merely a numeric pin. Conflicting type forms fail.
Standard Condition, AcknowledgeableCondition, AlarmCondition, and LimitAlarm
types have built-in context.

Condition occurrences require the genuine namespace-zero scalar ByteString
EventId declaration, including through inherited companion types.
Vendor/nested fields called EventId remain data and cannot authorize routing.
Occurrence actions require native EventId/Comment positions, zero outputs, and
a Method actually declared by the ConditionType. Optional WoT Comment becomes
`LocalizedText.Null`; native OPC UA Calls still supply both argument positions.
Enable/Disable take zero inputs/outputs. Unbound inherited Methods are disabled
so they cannot mutate only a local copy. After acknowledgment, use the new
EventId for confirmation.
Shelving, suppression, dialog, and ConditionRefresh transport mapping are out
of scope; local native retained-state refresh is a separate behavior.

The event runtime uses `IWotProjectionEventPublisher` and
`IWotProjectionConditionFactory`; instance-aware custom factories implement
`IWotProjectionConditionInstanceFactory`. Source-compatible consumers share an
upstream subscription; the final consumer releases it while the generation
retains its channel. `IEventSourceReadiness` awaits active subscriptions, not a
first occurrence. Views/groups are not event notifiers.

### Identity modes, provenance, and bounds

`LocalReEmission` is the default `WoTEventIdentityModeEnum`.
`uav:eventIdentityMode` accepts `local-re-emission` or `transparent-forwarding`.
Unknown values fail. Direct callers can use:

```csharp
WotProjectedAffordance declaration = WotProjectedAffordance
    .FromConverted(converted)
    .WithIdentityMode(WoTEventIdentityModeEnum.TransparentForwarding);
```

Transparent mode requires authenticated native occurrence/source binding,
current Session/mapping, EventId/EventType/SourceNode, source times, and compatible
type lineage. Admission retains the channel before publication even with no
subscribers, checks notifier support/host identity admission, and never downgrades
automatically. Core types and custom subtypes without additional instance
declarations are supported; custom payload schemas/unknown or conflicting
lineages, cycles, or depth beyond 64 fail. This is not general schema equivalence.

`IWotCapturedEventChannel` preserves public operands and appends only missing
private Core fields. Conditions additionally capture identity, branches, state,
quality/comment timestamp components, and fields of the advertised Core subtype.
Public Data/EventFields remain the declared selection.
Required fields are translated/validated on a detached prospective occurrence
before changing retained Condition state.
The native publisher capability requires captured source/action admission;
headless publishers do not claim server-wide native identity guarantees.

`IWotCapturedConditionActionChannel` pins authenticated Session, Method, receiver,
and generation. Wrong/unknown/revoked IDs fail instead of dispatching through a
new Session or another source. RolePermissions are checked before route lookup.
Retired routes remain eligible only while their original generation/source lives.
Independent Conditions/branches do not share mutable instances merely because
they use one EventType.

`EventBindings` contains `WoTEventBindingType` descriptors with mode, source
document/pointer, producing generation, source URI, and Availability.
`GetEventProvenance` returns `WoTEventOriginDataType`, not an action capability:

```csharp
session.Factory.Builder.AddOpcUaWotCon().Commit();
var binding = new WoTEventBindingTypeClient(session, bindingNodeId, telemetry);
WoTEventOriginDataType origin = await binding.GetEventProvenanceAsync(eventId, ct);
```

Unauthorized requests fail `BadUserAccessDenied` before identity lookup;
authorized unknown/disposed evidence returns `BadNoData`.
Verified retransmissions refresh receipt provenance without emitting another
occurrence. Core non-state alarm configuration can change on a retained refresh;
identity/state/source changes fault Availability with `BadSecurityChecksFailed`,
retain accepted evidence, and revoke its action route. Namespace lookalikes do
not acquire Core semantics. Local native ReceiveTime uses the server TimeProvider;
source Time/ReceiveTime and contextual timestamps remain source facts.

`WotProjectionBindingRuntimeOptions` configures:

| Bound | Behavior at capacity |
| --- | --- |
| `MaxQueuedEvents` | Delivers queued events, then stops the affected producer with `BadTooManyOperations` and releases source subscriptions |
| `MaxEventRoutes` | Per-declaration/generation occurrence and Condition-instance budget; excess rejected before state change/publication |
| `MaxQueuedPropertyValues` | Per-variable delivery budget, default 1024 |

Accepted occurrence evidence is not FIFO-evicted. Replays consume no new slot;
every distinct ordinary/Condition/branch occurrence does.
Subscription restart/action expiry does not reset capacity.
Recover by installing a replacement generation with the existing shadow-reload
lifecycle, allowing old consumers/evidence to drain.

The server-wide EventManager additionally uses
`EventIdentityAdmissionOptions.MaxEventIdentities` (default 65,536).
Configure `StandardServer.EventIdentityAdmissionOptions` before startup, inject
options, or supply them to EventManager. Native and projected events share this
admission domain; copied fields/URI aliases cannot establish one trusted occurrence.
Queues/retransmission buffers retain admitted identity references until drain.
Native evidence remains owned by live event targets, retained Condition snapshots,
and queued or retransmitted fields, rather than by a server-lifetime table reference.
After those owners drain, the identity slot is reclaimable. Native and projected
reclamation can lag until unowned targets are collected.

Preparing transparent projection/first projected reservation requires strict
identity admission for the remaining server lifetime. Earlier unsupported native
publication permanently prevents claiming it in that lifetime; after admission,
collisions/exhaustion/unsupported shapes fail before delivery.
Only the built-in native publication/subscription paths and admitted field
instances provide the guarantee. Bypassing/custom/durable reconstituted queues
do not establish identity continuity. No restart/HA/persisted replay claim follows.
The [aggregation sample](../samples/WotCon/README.md) exercises source-specific
alarm acknowledgment/confirmation and management actions.

## Conversion entry points

`Opc.Ua.Wot.WotNodeSetConverter` maps NodeSet2 to readable TD/TM and back.
Use async conversion when type declarations, event selections, or document links
need resolution:

```csharp
WotConversionResult<UANodeSet> result = await WotNodeSetConverter.ToNodeSetResultAsync(
    document, options, thingResolver, cancellationToken: ct);
if (!result.Success)
{
    throw new InvalidOperationException(string.Join(Environment.NewLine, result.Diagnostics));
}
UANodeSet nodeSet = result.Value!;
```

The synchronous entry points follow no document links. A stated event selection
without resolution reports `EventSelectionUnresolved`.
`NullWotResolver.Instance` is an explicit no-external-resolution policy for local
references on the async path. Missing required definitions do not become success.
Optional `WotExternalSchemaResolver` compares externally supplied schemas:
no provider is NotEvaluated, no answer Unresolved, conflicting answers Ambiguous,
and disagreement Incompatible. External schema support never rewrites native
DataType authority and is not a full JSON Schema engine.

### Importable output

All conversion paths complete known aliases before returning NodeSet2.
A name such as HasComponent/Double in a NodeId position must have its own
`Aliases` declaration; unknown vendor aliases still fail import.
Completion appends resolved aliases deterministically without rewriting existing
names. The runtime uses conversion, serialization, `UANodeSet.Read`, then Import.
`ResolveAffordanceNodes` proves local Nodes/classes and qualified declaration
ownership; a missing explicit identity does not become a name fallback.

### Verified linked document sets

```csharp
WotConversionResult<WotDocumentSet> exported =
    await WotNodeSetConverter.FromNodeSetDocumentsAsync(
        nodeSet, "pump", nodeResolver: resolver, cancellationToken: ct);
if (!exported.Success)
{
    throw new InvalidOperationException(string.Join(Environment.NewLine, exported.Diagnostics));
}
using WotDocumentSet documents = exported.Value!;
```

The async export reconstructs and compares the whole source before returning
success. Roots own disjoint partitions; Methods/arguments/nested Variables stay
with their owner and nested Objects/EventTypes may have separate roots.
Readable-complete partitions omit preservation; exceptional native partitions
carry their **complete authoritative graph**, not a readable overlay/delta.
Duplicate ownership and conflicting shared headers fail.
Comparison normalizes top-level Node order only; reference direction/order,
fields, arguments, values, metadata, tables, permissions, and extensions matter.
Synchronous export produces a candidate, not this verification guarantee.

`MergeNodeSetPartitions` applies the bounded rules to already converted partitions
without mutating inputs. Shared model ownership can join activation closures;
shared namespace spelling alone does not make unrelated resources atomic.

## Independent readable models

`DocumentSetMode` defaults to `PartitionReconstruction`. Opt in explicitly for
independently authored readable inputs using different local namespace tables:

```csharp
var options = new WotNodeSetConverterOptions
{
    DocumentSetMode = WotDocumentSetMode.IndependentReadableModels,
    ValueEncodingContext = messageContext
};
using var documents = new WotDocumentSet(
    "a", [new("a", modelA), new("b", modelB)]);
WotConversionResult<UANodeSet> imported = await WotNodeSetConverter.ToNodeSetAsync(
    documents, options, nodeResolver: resolver, cancellationToken: ct);
```

`urn:example:a` and `urn:example:b` can each use local `ns1:Root`/`i=1`.
The merged sorted URI table distinguishes them as `ns=1;i=1` and `ns=2;i=1`
regardless of input order. Core remains implicit at zero.
`MergeNodeSetPartitions(documents, partitions, options, ct)` supports the same mode.
No failed reconstruction automatically retries in this mode.

Normalization covers Nodes, BrowseNames, parents, references, aliases, roles,
MethodDeclarationIds, and definition fields. Conflicting model metadata,
RequiredModels, root ownership, shared Nodes, or source hrefs fail.
ServerUris must agree; NodeSet headers declare remote indexes starting at one,
not a fake local entry. Annotation strings/XML extensions are not searched for
apparent identifiers.

Standard namespace-bearing XML values are decoded/re-encoded through the native
codec, including nested Variants/DataValues, arrays, matrices, and Arguments.
Unknown fields/attributes, undeclared indexes, lossy decode, or incomplete
consumption fails `NamespaceRebaseUnsupported`.
Register structured XML codecs in `ValueEncodingContext`; its tables are copied
and limits honored without changing the caller's factory/tables. The basic
Argument codec is available without it. Remote server indexes stay remote even
when a URI equals the local slot.
Opaque XML/binary bodies remain untouched only when rebasing is unnecessary;
supplying a factory is not permission to rewrite opaque binary values.
Typed null and empty Strings remain distinct.

Authoritative native/archive inputs retain strict header/ownership rules.
Independent mode does not relax them. Document/node/byte/depth bounds and
cancellation apply to both direct/preconverted paths and final output.
Registry configuration uses:

```csharp
services.AddOpcUa().AddWotRegistryServer(options =>
    options.DocumentSetMode = WotDocumentSetMode.IndependentReadableModels);
```

The key is `OpcUa:WotConRegistry:Server:DocumentSetMode`.
DI forwards the mode and optional `IServiceMessageContext` to converter options.
It participates in refresh fingerprints. Codec registration changes require an
explicit forced refresh; source grouping/activation scope is not widened.

## Identity and preservation

Author portable `nsu=<NamespaceUri>;<idtype>=<id>` identities; namespace zero
keeps `i=`. Use context-bound or URI-qualified BrowseNames/paths, not numeric
namespace prefixes. These rules cover `uav:id`, components, map targets, refIds,
and generated href `?id=` values. Native/archive local fields keep their own tables.

Unspecified identities follow Annex G.1:
`GeneratedNodeId(U, P) = "nsu=" + U + ";s=" + P`.
The absolute qualified browse path escapes reserved `&/.<>:#!` characters
with Annex A syntax. Base-namespace InputArguments/OutputArguments keep their
bare names beneath the action. Authored identities always win; duplicates fail.
`WotPortableIdentity` provides generation and portability checks.

### Compatibility switch for non-portable identifiers

`AllowNonPortableIdentifiers = true` changes NonPortableIdentity/
NonPortableQualifiedName errors to warnings while interpreting OPC 10101 v1.00
documents. It defaults false and is a rewriting aid, not portable authoring.
This Binding-vocabulary compatibility is not the WoT-Con 1.02 asset interface.

### Preservation modes and digests

| `WotNodeSetPreservationMode` | Behavior |
| --- | --- |
| `WhenRequired` (default) | Readable conversion first; complete structured `uav:nodes` only when needed |
| `Always` | Explicit byte archival in `uav:nodeSet` |
| `Never` | No preservation fallback; useful for completeness/conformance checks |

`NativeProjectionPreserved` and `UsedPreservationEnvelope` distinguish structured
and archival fallback in roundtrip reports. Equivalence is not byte identity.
Supported native/archive content remains authority: asserted readable facts must
agree with actual identities/classes/types/ranks/values/references/metadata.
Missing readable facts do not authorize synthesis over preserved Nodes.
Unsupported future native profiles are retained as opaque residue and diagnosed;
malformed supported profiles and invalid archival digests still fail.

Unknown/unmapped JSON values are pointer-addressed `WoTJsonResidue` members in
NodeSet Extensions, not a copy of the source document. Reverse conversion
regenerates known facts then applies residue, reporting conflicts.
Opaque values preserve original escapes, numeric spellings, order, and whitespace.
Metadata/configuration/literal boundaries do not declare Nodes or fetch requests.
A leading UTF-8 BOM is retained in exact document bytes and counts toward limits,
but is absent from canonical JSON.

Do not confuse three measurements:

- SHA-256 pins **received bytes** for residue, archive envelopes, and source
  digests. Reformatting first changes the pinned content.
- RFC 8785 canonical JSON compares values where supported. Excess precision is
  diagnosed, not rounded into false equality; comparison may retain lexical
  distinctions outside that domain. No preservation digest uses this form.
- Opaque size bounds use compact received JSON: remove insignificant whitespace
  outside strings without changing member order/numbers/escapes.

## Resolving a type binding: the local context

Resolution consults sibling documents before the loaded AddressSpace.
`IWotNodeResolver` supplies types; `WotCompositeNodeResolver` composes the order.
The registry uses `SnapshotWotNodeResolver` and `AddressSpaceWotNodeResolver`;
without context `NullWotNodeResolver` holds nothing.
Compact model names are hints; ExpandedNodeIds are definitive identities.
A `ua:HasTypeDefinition` document IRI uses `IWotThingResolver` with the link's
effective context, not AddressSpace fallback or an arbitrary Web fetch.

Absent TD type information defaults to BaseObjectType. A stated binding must
resolve uniquely to the right class: UnresolvedTypeBinding, AmbiguousTypeBinding,
and InvalidTypeBinding distinguish failure. A held namespace makes a compact
name a binding even if lookup fails; unrelated annotations stay annotations.
Registry Kind determines eligible model declarations; a TD cannot plant a type
by calling itself a TM in its bytes.

`IWotTypeDeclarationResolver` supplies loaded declarations; async conversion
prepares `WotDeclarationCatalog`. Qualified members populate existing declarations,
adopting their ReferenceType, type definition, DataType, rank, dimensions, and
Method declaration instead of creating a second sibling.
An unqualified name requires uniqueness. Incomplete context applies known facts
and reports `DeclarationsUnavailable`. Closed
`uav:additionalProperties: false` then fails; open content reports a warning,
not a claim that unread declarations are absent.
`uav:includeInherited` controls expansion, not verification of stated ancestry.

## Resolving a relation: companion ReferenceTypes

`IWotReferenceTypeResolver` resolves typed link `rel` names and definitive
`uav:refId` against the same context order. BrowseName expresses forward direction,
InverseName expresses inverse, and symmetric references use one forward name.
Ambiguity needs a consistent pin; wrong NodeClass, missing concepts, or
name/pin disagreement produce explicit diagnostics, not HasComponent fallback.
NodeSet output uses local NodeIds/declared aliases for ReferenceType fields.

Containment, supertype, type definition, modelling rules, events, and encodings
already represented structurally are not duplicated as generic links.
Other relations retain their exact name/pin/direction and target name where known.
ReferenceType TM roots carry `uav:inverseName` and `uav:symmetric`;
symmetric roots cannot also declare an inverse name.
`ua:ComponentOf` is an accepted alias for emitted `uav:componentOf`.

Component-template links distinguish the type `href` from its
`uav:declaration` Object/Variable identity, name, and modelling rule.
Using one type twice creates two declarations. This annotation is a converter
extension pending coordinated advertised-vocabulary support, not a strict
1.1 conformance claim.

## DataTypes, ranks, arguments, and model vocabulary

Unannotated property booleans/strings map to Boolean/String; bare integers/numbers
to abstract Integer/Number. Native pins override inference. `base64`, `date-time`,
`uuid`, and `uri` refine to ByteString, DateTime, Guid, and UriString.
Unrecognized type falls back to BaseDataType. This differs from the primitive
[WoT-Con 1.02 asset mapper](WoTConnectivity.md#wot-con-102-compatibility).
Property type defaults to BaseDataVariableType; a PropertyType binding uses
HasProperty. Missing access flags mean read/write; author contradictory flags
explicitly rather than depend on the converter's CurrentRead safety default.
A missing model identity can synthesize `urn:opcua:wot:synthesized`; author a real
namespace for portable models.

`uav:dataTypeDefinitions` holds one full graph definition per identity; later
occurrences are reference-only. Definitions cover Structures, Unions, Enums,
OptionSets, and SimpleDataTypes. Native IDs are explicit or derived from qualified
name as `nsu=<NamespaceUri>;s=DataTypes/<Name>`.
Graph `@id` is not itself a NodeId.

Inference requires enough information: multi-field objects need field order;
required fields determine Structure optionality; Union is explicit; enum branches
need both values and names. Bare integer/number Structure fields and missing
custom bases fail instead of guessing widths/subtyped representation.
OptionSet bases are Byte/UInt16/UInt32/UInt64 wide enough for the highest bit,
not abstract UInteger.
Concrete Structures expose Default Binary/XML/JSON encodings unless an eligible
embedded-only type opts out with `uav:hasDefaultEncoding: false`.
Abstract types cannot author encoding identities.
Inferred schema terms still use residue/preservation where canonical schema
equivalence is not established; inference is not a blanket exact-readable claim.

`uav:valueRank` preserves -3, -2, -1, 0, and positive ranks.
ArrayDimensions must match fixed rank; zero dimension bounds mean unspecified
length. Argument/event/definition fields use the same rules.
Named action arguments have ordered InputArguments/OutputArguments; an explicit
single layout retains one whole Structure value. `GetMethodArgumentLayout`
returns the same captured contract without creating Nodes.
Unavailable argument values retain their source Nodes instead of losing them.

Events use `uav:eventType` and derive from BaseEventType or the verified
ConditionType. Inherited fields are not redeclared; ConditionId is an Attribute,
not a synthesized Variable. A TD's extra event declarations belong on a generated
subtype because Object instances cannot source GeneratesEvent.
Severity is occurrence data: `uav:severity` and `uav:isEvent` are unknown residue,
not synthesized metadata. OptionalPlaceholder is `i=11508`;
MandatoryPlaceholder is `i=11510`, not 11509.

Composition/containment, inheritance, semantic IDs, scaling, and configuration
annotations are validated and retained where no separate native structure maps
them. Malformed known values fail in both modes. Unmapped terms such as
`uav:capability`/`uav:componentModel`/`uav:reference` do not create relations:
use typed HasInterface/HasComponent/NonHierarchicalReferences links.

## Engineering units, ranges, and localized text

EngineeringUnits/EURange/InstrumentRange map in both directions when source
values decode. `uav:engineeringUnits` retains EUInformation authority, integer
unitId, displayName, and description; `uav:unitProperty` points to its sibling
string property and places it under the annotated Variable unless placement is
explicit. `minimum`/`maximum` map EURange only as a valid pair; InstrumentRange
must contain it. DataType machine limits never invent engineering ranges.
Undecodable/missing native values remain ordinary facts with preservation fallback.
`uav:scaleFactor`/`uav:decimalPlaces` are retained static annotations, not derived
from units/ranges or used to fabricate them. These terms belong on property
affordances, not nested payload fields. `unit` is a display unit, not a quantity kind.

Plural `titles`/`descriptions` retain all locales and are authoritative.
The singular member uses the effective default-locale entry, otherwise the
code-point-first language fallback with a neutral term-language override.
Local/property-scoped overrides and explicit null resets remain effective.
Missing or contradictory singular fallback fails InvalidLocalizedText.
Native EUInformation/Argument descriptions hold one translation; additional
authored locales use bounded residue without duplicating the selected translation.
The Binding's versioned context supplies scoped engineering-unit terms;
declaring only the `uav` prefix does not define their short member names.

## Conformance and authoring validation

`WotBindingConformance.CurrentRevision` is `1.1`.
`SupportedRevisions` includes Binding vocabulary `1.0` and `1.1`, independent
of [WoT-Con 1.02 compatibility](WoTConnectivity.md#wot-con-102-compatibility).
The `uav` namespace is `http://opcfoundation.org/UA/WoT-Binding/`.
Compact-term reading also accepts earlier draft prefix bindings; that does not
change the advertised revision.
Generated documents state `uav:bindingVersion: "1.1"`; authored claims survive
roundtrip rather than being overwritten.

`ConformanceMode` defaults Permissive: unknown vocabulary is retained as residue.
Strict reports unknown terms and unmet `RequiredConformance` claims.
`AuthoringValidation` is a separate opt-in: known published revision/unit/profile
names are required for an authored claim, while consumers preserve well-formed
unknown claims/revisions with warnings. Malformed claims fail in every mode.
Profile declarations are document claims, not proof that a deployment implements
all requirements.

Opaque metadata/property/action/event configuration objects always obey 65,536
compact-received UTF-8 bytes, depth 32 (outer container counts), and 256 top-level
keys. Exact limits pass. Keys should be absolute/context-bound IRIs; earlier
unnamespaced keys warn for consumers and fail authoring validation.
`WotUnitMigration.MoveQuantityKinds` is an explicit helper for moving an earlier
quantity-kind `unit` to `qudt:hasQuantityKind`, never inventing an engineering unit.

The `uav:nodes` lower-camel record grammar has its own
`http://opcfoundation.org/UA/WoT-Binding/nodes/` namespace; its tokens are not
readable Binding terms. Strict vocabulary checks do not reinterpret opaque
archive/native grammar fields.
Full TD/TM JSON Schema and compatibility-policy engines are separate unfinished
work. Parsing, native admission, successful conversion, generated profiles,
and pinned examples do not close that work or establish Full conformance.

### How this is checked

The pinned `spec-examples.manifest.json` records **27 published examples**,
source repository/branch/full commit, Binding revision, size, and SHA-256.
Fixtures compare exact vendored bytes and test the published projection/expected
output pair, strict validation, conversion, serialization, reread, and Import.
This inventory is separate from the aggregation sample's 535-document manifest.

`WotSpecFixtureManifestTests` verifies the fixture set and provenance offline.
Optional checkout identity checks use a sibling `spec-drafts` checkout or
`OPCUA_WOT_SPEC_DRAFTS`, and are skipped without one.
`RegenerateFromSpecCheckout` is the explicit fixture regeneration operation.
Statement-ledger checks separately verify pinned source commit/hashes:
`tools\wot-spec\Get-WotStatementDigests.ps1 -SpecRoot <checkout> -Verify`.
Recorded evidence gaps remain gaps, not profile certification.

## Adding your own binding

Use the checked-in
[`MemoryWotBinding.cs`](../samples/WotCon/AggregationServer/Bindings/MemoryWotBinding.cs)
as a complete example, rather than copying a second implementation from this
guide. It is sample-only and deliberately not registered by the sample host.

### Interfaces and lifecycle

| Interface/type | Implementer responsibility |
| --- | --- |
| `IWotProtocolBinder` / `WotProtocolBinderBase` | Stable id/version/source/capability and deterministic identification |
| `IWotBindingIdentification` | Explicit pin, vocabulary, subprotocol, then scheme precedence |
| `IWotBindingPlanner` | Pure validation and immutable compiled forms, exact JSON Pointer diagnostics |
| `IWotBindingExecutor` | `ActivateAsync` opens a channel for a compatible compiled identity |
| `IWotBindingChannel` | Async read/write/invoke/observe/event operations and disposal |
| `IWotPayloadCodec` / optional `IWotInteractionPayloadCodec` | Bounded scalar/complete interaction representation |
| `IWotCredentialProvider` | Out-of-band credential/trust lookup, never plan secrets |

The registry evaluates ordinal `id@version` order for ties; multiple versions
can coexist. Executor lookup uses exact key then id default.
`WotFormExtractor` resolves op/security defaults and captures JSON Pointer,
affordance, form, schema, and original context before detachment.
Use its typed helpers and report form/affordance pointers correctly.
The default runtime owns lazy per-form channel slots: concurrent open occurs
once, failed opens can retry, one caller cannot cancel another's shared open,
and generation cancellation/disposal drains successful channels/subscriptions.
Transparent/native action admission can require earlier capture.

Planner helpers include RequireHref, URI/scheme/endpoint creation,
ResolveOperations, ResolveCodec, and ResolveSecurity.
Return Supported only with nonempty entries/no errors; otherwise Unsupported.
Plans carry endpoint/address/operation/payload/target/security metadata,
not clients, mutable state, secrets, delegates, or disposables.
Protocol planners leave generic target mapping to the registry.

### Direct and DI registration

```csharp
var store = new MemoryWotStore();
var endpointPolicy = new WotEndpointPolicy();
endpointPolicy.AllowedSchemes.Add("mem");
var registry = new WotProtocolBinderRegistry(
    [new MemoryWotBinder()],
    [new MemoryWotBindingExecutor(store)],
    endpointPolicy: endpointPolicy);
```

Equivalent host registration uses `AddWotBinder`, `AddWotBindingExecutor`, and
`AddWotCredentialProvider`; ensure the custom scheme is allowed by the configured
endpoint policy in the hosted registry too.
Custom packages should expose one fluent protocol extension and retain a direct
construction path. Register codecs ahead of built-ins through
`WotPayloadCodecRegistry.Register`, or inject `IWotCodecRegistry`.
Transport factories, `IWotTargetVariableResolver`,
`IWotProjectionBindingRuntimeFactory`, event/Condition factories, and runtime
options are injectable. Direct configuration can set bounded queues:

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

### Status, tests, and packaging

Return deterministic status results for expected transport failures:
BadNotSupported for unavailable operations, BadEncodingError/BadDecodingError
for payloads, BadTimeout for executor timeouts, BadCommunicationError for network
failure, BadUserAccessDenied for authorization, and BadEncodingLimitsExceeded
for bounds. Preserve meaningful native statuses/timestamps and caller cancellation.
Keep detailed exceptions in telemetry, not remote error text.

Test identification/pins/ties, inherited forms/security, bounds/conflicts,
all supported/unsupported operations, codecs, contextual types, credential
isolation, strict/degraded outcomes, cancellation/concurrent opens/disposal,
direct/structured mapping, observe-only sources, subscriber cleanup/overflow,
and failed replacement retaining the old generation.

Use JsonElement, source-generated protocol-envelope serializers, and registered
native codecs instead of reflection/dynamic discovery. Validate modern executor
paths under trimming/NativeAOT; the aggregation samples publish AOT on net10.0.
Keep dependency-free planners on the full base TFM matrix; optional transport
dependencies belong in separate packages. Documentation must distinguish
package availability from executor availability.
