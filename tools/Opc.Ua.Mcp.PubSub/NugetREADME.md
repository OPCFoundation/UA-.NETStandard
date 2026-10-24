# OPCFoundation.NetStandard.Opc.Ua.Mcp.PubSub

OPC UA Model Context Protocol (MCP) tools for PubSub, packaged so they can be
embedded in any MCP server host rather than only run as the shipped `opcua-mcp`
tool.

Use this package when an application wants to expose OPC UA PubSub publishers
and subscribers to an LLM alongside tools of its own.

## Tools

| Profile | Workflows |
|---|---|
| `pubsub` | Legacy UDP/UADP runtime, action and discovery tools; names and arguments remain unchanged |
| `pubsub-runtime` | Discover backends; validate/create/list/start/stop/remove named applications; typed publication, bounded reads and diagnostics |
| `pubsub-configuration` | Existing remote server configuration inspection, safe export/import, standard component/field mutations and enable/disable |
| `pubsub-bridge` | Explicit client-source/sink/action bindings and named declarative transcoding routes |
| `pubsub-security` | Public group metadata, group creation/removal, rotation/invalidation, protected key pull/push and push-target management |

**Selecting a profile, including `full`, does not grant permission.** Live and
administrative operations require explicit host capability, trusted caller,
target and operation rules. Empty rules deny execution. Legacy live start,
publish, actions and discovery now pass through this intentional compatibility
gate too. Legacy status/stop cannot inspect or replace another caller's runtime.
Its lifetime remains until explicit stop or host disposal; new named-runtime
expiry is not silently imposed on it.

## Usage

```csharp
builder.Services.AddOpcUaMcpCore();
builder.Services.AddOpcUaMcpPubSub();

builder.Services.AddMcpServer()
    .WithStdioServerTransport()
    .WithOpcUaMcpFilters()
    .WithOpcUaCoreTools(McpToolProfile.PubSub)
    .WithOpcUaPubSubTools(McpToolProfile.PubSub)
    .WithTools<MyApplicationTools>();
```

A profile that does not select PubSub contributes no tools rather than failing,
so the same profile value can be passed to every OPC UA tool package a host
references.

The existing zero-argument registration remains available. Hosts can provide
`AddOpcUaMcpPubSub(new McpPubSubOptions { ... })` for bounds, literal-address
UDP/DTLS rules, exact Ethernet MAC/VLAN/interface rules, exact remote model
operations and adapter node/method scopes. Broker connection/topic grants use
`OpcUaMcpOptions.Execution.NetworkRules`. Trusted caller/session grants and
capabilities use `OpcUaMcpOptions.Execution`; registration never enables them.

Use the `McpToolProfileSet` overload to compose new profiles:

```csharp
builder.Services.AddMcpServer()
    .WithStdioServerTransport()
    .WithOpcUaMcpFilters()
    .WithOpcUaPubSubTools(McpToolProfileSet.Parse(
        "pubsub-runtime,pubsub-configuration,pubsub-bridge,pubsub-security"));
```

Remote profiles add the shared connection tools only once. They operate on an
existing encrypted named UA session; they do not start or control a server
process. Public constructors on `McpPubSubRuntimeManager`, the transport
provider and tool classes support direct construction without the executable.
`IMcpPubSubTransportProvider`, DTLS context/certificate infrastructure and
`IEthernetFrameChannelFactory` are injectable host services.

Transport control services use programmatic methods, not configuration
properties: `MqttConnectionOptions.WithStreamProvider` / `GetStreamProvider`,
`WithTopicPolicy` / `GetTopicPolicy`, and
`UdpTransportOptions.WithDatagramPolicy` / `GetDatagramPolicy`,
`WithLocalAddress` / `GetLocalAddress`. The backing fields remain private so
neither configuration binding nor configuration serialization can traverse
these host-owned services. The normal scalar transport options retain their
existing defaults and binding behavior.

## Named runtime contract

`pubsub_application_create` accepts three typed root arguments: `input`
(`PubSubApplicationSettingsInput`), `connection` (`PubSubConnectionInput`), and
the native JSON `fields` array. It returns an opaque owner-bound handle and
creates a **stopped** application. For example:

```json
{
  "input": {
    "name": "press-values",
    "mode": "Publisher",
    "publisherId": { "type": "UInt16", "value": 42 },
    "writerGroupId": 10,
    "dataSetWriterId": 1
  },
  "connection": {
    "transport": "Mqtt",
    "endpoint": "mqtts://broker.example:8883",
    "encoding": "Json",
    "topic": "plant/press/data",
    "metadataTopic": "plant/press/metadata"
  },
  "fields": [
    { "name": "Temperature", "type": "Double", "initialValue": 21.5 }
  ]
}
```

The host must independently allow that broker, resolved network, exact data and
metadata topics, and operations. The existing MQTT backend also configures a
standard status last-will topic, which requires its own explicit `publish`
grant (including on subscriber connections). Wildcard discovery subscriptions
are disabled; additional topics require explicit `auxiliaryTopics` and grants.
The example does not itself authorize a connection.

Use `pubsub_application_start(handle)` to activate and
`pubsub_application_publish` with
`{"handle":"<created handle>","fields":[{"name":"Temperature","value":22.0}]}`
to update a sample atomically. A successful sample update is not a delivery
acknowledgement. PublisherId widths, numeric/string distinctions and UADP versus
JSON wire encoding are preserved. Arrays require explicit rank `1`; default
rank `-1` is scalar.

Omitted optional JSON members retain the same defaults as direct construction:
writer/group identifiers `1`, dataset name `McpDataSet`, publishing interval
`100` milliseconds, and scalar field rank `-1`. Missing required members and
empty required objects remain errors; explicit zero or empty values are never
silently replaced by defaults. The production source-generated context owns
this behavior and does not depend on reflection.

Direct C# callers can continue using complete `PubSubApplicationInput` and
`PubSubBridgeInput` plans. Tool overloads split those plans into shallow,
native typed arguments; they do not accept serialized JSON strings as an escape
hatch. Configuration tools likewise accept root `fields` or `variables` arrays,
and security group association tools accept a root `nodes` array.

The shared defaults admit eight leases per host, two per trusted caller,
five-minute idle expiry, thirty-minute hard expiry and thirty-second operations.
Per-runtime defaults bound the decoded buffer to 64 messages/1 MiB, with a
64 KiB per-message budget, 64 fields, 1,024 array elements and 100 frames/second.
Overflows, invalid snapshots and rate drops have distinct counters. Read with
`clear=true` removes only the returned page. Stop retains the lease, field schema
and buffered data; remove releases the quota slot.

## Transport and credential boundaries

- MQTT uses the shipped MQTTnet adapter. Its supported stream-provider seam
  resolves/checks every reconnect through the immutable host network grant;
  TLS still uses the original broker hostname. Topic and payload checks run at
  the actual subscribe/publish/receive boundary before the MCP receive buffer.
- UDP/DTLS requires IP literals, explicit ports, local interface/address and
  source/reply scopes. The send policy also covers discovery and DTLS replies.
  Standard multicast discovery is not implied by permission for a data endpoint.
- Ethernet checks full frames against exact source/destination MAC, VLAN,
  priority, interface and frame limits. No driver is installed and no privilege
  is elevated. Native Windows activation requires a host-provided backend.
  An in-memory backend proves the integration seam, not hardware interoperability.
- **Built-in live Kafka is blocked.** The pinned managed Dekaf 1.21.1 backend
  lacks a supported public per-broker DNS/connect hook, so bootstrap allowlisting
  cannot authorize metadata-discovered broker egress. Kafka configuration,
  profile and schema selection remain available and capability discovery reports
  this limitation explicitly. There is no native-Confluent fallback.
- Passwords and SKS keys are authorized `alias:`/`issued:` references through
  `AddOpcUaMcpSecrets`, never plaintext tool inputs. Issued-reference expiry or
  invalidation closes dependent leases. Host aliases are re-resolved at each
  start and bounded by the runtime lease; aliases currently have no live
  revocation notification, which status reports explicitly.
- SKS pull persists keys before returning references. Store failures report
  remote retrieval separately from local persistence. Key invalidation through
  these tools invalidates tracked uses of that exact remote group. Rotation is
  separate from invalidation and does not silently redeploy keys. Secured
  runtimes retain the actual encrypted issuing endpoint as standard
  `SecurityKeyServices` metadata while obtaining key material from the protected
  local reference. This metadata neither triggers an automatic key pull nor
  grants new outbound authority.

## Remote configuration and adapters

Configuration uses generated standard clients and bounded standard file
transfers under `TransferRoot`. Export removes credential-bearing property bags,
opaque transport settings and substitution values and reports the number
removed. Such an export is explicitly not a complete round trip of private
configuration. Import rejects those fields rather than silently dropping them.
`CloseAndUpdate` reports the server's applied flag and per-item statuses; it is
not a client-emulated atomic transaction. Unknown outcomes must not be blindly
retried.

Every imported connection needs an explicit endpoint `configure-connect` grant
with an empty resource selector, even if it contains no writer or reader groups.
Authentication defaults and implicit discovery/status traffic can activate such
a connection. Direction-specific topic grants must also cover writer-group,
per-writer and metadata queues, including aggregate-message fallbacks. Mutations
check their actual direction and existing parent address/transport settings;
unreadable or incompatible parent settings are security errors, not permission
to substitute a caller-provided plan.

Source/sink bindings require exact declared field names/order and authorized
Value node scopes. They reuse the Adapter package's cyclic reads and
TargetVariables sink. Metadata is an explicit immutable schema, not an implicit
model subscription. Replaced sessions or namespace mappings fail closed instead
of binding a new server under the old name. Sink diagnostics preserve actual
per-item write results; a batch is not advertised as an atomic server write.
Method bindings also pass the shared protected-target guard.

A declarative bridge owns both endpoint applications; it never silently borrows
or replaces an existing application. Supported transforms are identifier
remapping, field rename/projection/exclusion/promotion and metadata-version
preservation. The target's cyclic writer is disabled, so it emits only forwarded
records. Lowering UADP security requires both explicit host permission and
explicit route consent. No scripts, expressions or tool-name callbacks are
accepted.

The bridge tool interface has eight root arguments: route `input`
(`PubSubBridgeSettingsInput`), application `source` and `target` settings,
`sourceConnection` and `targetConnection`, native `sourceFields` and
`targetFields` arrays, and an optional native `renameFields` array. Reusable
schema definitions keep the catalogue compact; their resolved object/array
structure remains at most three levels deep.

## Related packages

| Package | Adds |
|---|---|
| `OPCFoundation.NetStandard.Opc.Ua.Mcp.Core` | Part 4 service tools, session management, filters (required) |
| `OPCFoundation.NetStandard.Opc.Ua.Mcp.Diagnostics` | packet capture, decode and replay, including key logging |
| `OPCFoundation.NetStandard.Opc.Ua.Mcp.PubSub.Diagnostics` | PubSub packet capture and decode |
| `OPCFoundation.NetStandard.Opc.Ua.Mcp` | the ready-to-run `opcua-mcp` server composing all of the above |

## License

OPC Foundation MIT License 1.00 — <http://opcfoundation.org/License/MIT/1.00/>
