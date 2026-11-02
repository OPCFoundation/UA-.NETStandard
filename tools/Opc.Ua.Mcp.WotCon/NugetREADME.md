# OPC UA WoT Connectivity MCP tools

Embeddable tools for legacy WoT assets, WoT registry materialization, bounded
document processing and independently registered live protocol runtimes.
The `wot`, `wot-documents` and `wot-runtime` profiles remain separate.
`wot-documents` needs no OPC UA connection and never activates a protocol executor.

`AddOpcUaMcpWotCon` / `WithOpcUaWotConTools` retain their management and document
registration. Live execution still requires the distinct
`AddOpcUaMcpWotRuntime` / `WithOpcUaWotRuntimeTools` registration and explicit
host/caller grants. Sharing this package does not opt a host into execution.

```csharp
McpToolProfileSet profiles = McpToolProfileSet.Parse("wot,wot-documents");
services.AddOpcUaMcpCore(options).AddOpcUaMcpWotCon(new WotMcpOptions());
services.AddMcpServer().WithOpcUaMcpFilters()
    .WithOpcUaCoreTools(profiles).WithOpcUaWotConTools(profiles);
```

Compose with `Opc.Ua.Mcp.XRegistry` for generic groups, resources, versions,
labels and content transfers. Mutations are explicit, require encryption, and
preserve concurrency checks and partial outcomes. File transfers use the
existing host-owned transfer root and never overwrite local output.

Document processing uses supplied text/files first. Remote dependencies need
both invocation opt-in and the host's exact HTTPS origin allowlist; the default
is no egress. Redirects, ambient credentials, cookies and proxy bypasses are not
permitted. Requests and decompressed content share bounded count/depth/byte and
deadline policies.

Linked document sets are retained in one ZIP artifact with an href/digest
manifest, not flattened into a single incomplete document. Archive members are
never extracted to arbitrary host paths.

See the [MCP server guide](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/docs/McpServer.md#wot-and-registry-tools)
for profiles, configuration, embedding and workflow details. Draft model
maturity is independent of this package's release channel.

## Document input and artifact contract

The five document tools take root `source.text` **or** `source.filePath`.
Inline text is limited to 64 KiB of UTF-8; files and outputs are confined to the
existing `TransferRoot`. Sources, dependency arrays and refresh selectors are
top-level arguments, with scalar settings in `options`, not an extra `input`
envelope. Direct callers retain `WotDocumentInput` and `WotRefreshInput`.

For example, `wot_from_nodeset` accepts:

```json
{
  "source": { "filePath": "pump.xml" },
  "outputPath": "pump-documents.zip",
  "rootHref": "pump"
}
```

The ZIP preserves every linked document in flat `document-NNNN.json` entries.
Its `manifest.json` contains `format: "opcua-mcp-wot-document-set"`, `version: 1`,
the exact `rootHref`, and `documents` records with `href`, `entry` and `sha256`.
This is an MCP artifact, not new WoT vocabulary. Hrefs are metadata and members
are not extracted. Duplicate, path-bearing, missing, unlisted and
digest-mismatched members fail validation.

Pass the whole artifact back to `wot_to_nodeset`:

```json
{
  "source": { "filePath": "pump-documents.zip" },
  "options": { "documentSetArchive": true },
  "outputPath": "reconstructed.xml"
}
```

Publication is atomic and never overwrites. Results contain byte counts, SHA-256
and bounded diagnostics/provenance, not document bodies. JSON caps at 16 MiB,
XML at 64 MiB, and stricter host/transfer budgets also count manifests,
decompressed members and compressed/decompressed dependencies. Cancellation or
failure leaves no partial destination.

The host maps `MaxDocuments`, `MaxResolverDepth`, `MaxTotalBytes` to the
converter's independent `MaxDocumentSetDocuments`, `MaxDocumentSetDepth`,
`MaxDocumentSetBytes` ceilings. Counts, depth and cumulative bytes are checked
during conversion, including preservation partitions.

Consumer mode preserves unknown vocabulary. `options.strictAuthoring: true`
enables authoring checks; `requiredConformance` requires that choice. An
unannotated W3C TD may have `kind: Unknown`; validation then reports JSON syntax
only, not W3C certification or a verified Binding projection. An explicit
`ThingDescription` / `ThingModel` planning kind does not guarantee conversion.
Plans retain planner versions/maturity and never activate an executor.

`dependencies` entries take `reference` and exactly one text/file source.
`options.baseUri` is a document identity, not a URL to fetch. Relative references
are associated with their owning document; ambiguous aliases fail. Absolute
references remove that ambiguity. Inspection never retrieves dependencies or
executes JSON-LD contexts.

### Remote dependency policy

Remote retrieval needs both `options.resolveRemoteDependencies: true` and the
host's exact HTTPS-origin allowlist:

```json
{
  "McpServer": {
    "ToolProfile": "wot-documents",
    "TransferRoot": "C:\\opcua-mcp-transfer",
    "Wot": {
      "AllowedDependencyOrigins": [
        "https://models.example.org",
        "https://schemas.example.org:8443"
      ],
      "AllowPrivateDependencyAddresses": false,
      "MaxDocumentBytes": 16777216,
      "MaxTotalBytes": 67108864,
      "MaxDocuments": 256,
      "MaxResolverDepth": 16,
      "OperationTimeoutMilliseconds": 30000,
      "MaxConcurrentOperations": 2
    }
  }
}
```

`OPCUA_MCP_WOT_DEPENDENCY_ORIGINS` accepts comma/semicolon-separated origins;
`OPCUA_MCP_WOT_ALLOW_PRIVATE_DEPENDENCIES` takes an explicit Boolean.
Configuration values take precedence. Callers cannot raise host ceilings.

Origins match normalized hostname and effective port, not URL prefixes. Paths,
queries, fragments, credentials and non-HTTPS schemes are invalid allowlist
entries. Redirects, cookies, ambient credentials and proxies are disabled.
Every resolved address is checked before connecting to that IP; private
addresses require host permission and loopback is blocked. An injected HTTP
transport is trusted host code and must preserve these guarantees.

The deadline covers all requests. Declared and actual decompressed sizes,
cycles and resolver depth are bounded. Credential-bearing URLs are unsuitable
for logs. `CreateWotMcpOptions(configuration)` shares the executable's
reflection-free binding; do not add credentials, proxies or redirects to its
dedicated named dependency client.

### Composing registry tools

```csharp
McpToolProfileSet profiles = McpToolProfileSet.Parse("core,wot,xregistry,wot-documents");
builder.Services
    .AddOpcUaMcpCore(new OpcUaMcpOptions { TransferRoot = "C:\\opcua-mcp-transfer" })
    .AddOpcUaMcpWotCon(new WotMcpOptions
    {
        AllowedDependencyOrigins = ["https://models.example.org"]
    })
    .AddOpcUaMcpXRegistry();
builder.Services.AddMcpServer()
    .WithStdioServerTransport()
    .WithOpcUaMcpFilters()
    .WithOpcUaCoreTools(profiles)
    .WithOpcUaWotConTools(profiles)
    .WithOpcUaXRegistryTools(profiles);
```

`wot_refresh` takes explicit selectors or `allResources: true`, returns partial
outcomes and generation, and may truncate detail at the requested item limit.
There is no continuation that would re-run the mutation. Registry operations
share the xRegistry package's Resource/Version and epoch contract.

## Live protocol execution

Ten explicitly authorized tools form the independent `wot-runtime` catalogue.
Inspecting, validating, converting or planning a document never registers or
activates these executors.

### Executors and ownership

| Binding ID | Version | Execution |
| --- | --- | --- |
| `w3c.http` | `1.1` | HTTP/HTTPS using the existing HTTP executor |
| `w3c.mqtt` | `1.0-ed` | MQTT/MQTTS using the existing MQTTnet executor |
| `w3c.modbus` | `1.0-ed` | Modbus TCP using the existing Modbus engine |
| `opc.opcua` | `10101` | An explicitly selected existing named ManagedSession |

CoAP, BACnet, PROFINET and LoRaWAN remain visible as planning-only capabilities.
There is no protocol or form fallback.

Each named runtime pins the original document SHA-256, affordance kind/name,
zero-based form index, binder ID/version and explicitly requested operations.
Opening a runtime prepares the existing binding plan but opens no protocol
channel. Authorized operations open channels lazily.

The shared runtime registry enforces ownership and defaults to eight runtimes per
host, two per caller, five minutes idle expiry and a thirty-minute hard lease.
Closing drains pending operations and releases owned channels. OPC UA uses
`SessionRequestContext`, not a captured `InnerSession`; runtime disposal never
disconnects the caller's named session. Its generation-invalidation signal is
attached to the registry before runtime creation, so recovery or replacement
closes owned channels without waiting for another tool request. Model changes
within an unchanged session require the host's separate model-invalidation policy.

### Embedding live tools

```csharp
services
    .AddOpcUaMcpCore(hostOptions)
    .AddOpcUaMcpWotRuntime(new WotRuntimeOptions
    {
        MaxPayloadBytes = 65_536,
        MaxResultBytes = 262_144,
        MaxNotifications = 64,
        MaxObservationMilliseconds = 10_000
    });

services.AddMcpServer()
    .WithOpcUaMcpFilters()
    .WithOpcUaWotRuntimeTools(McpToolProfile.WotRuntime);
```

Supply `hostOptions.Execution` with explicit host capabilities, trusted caller
grants and network rules. Selecting `wot-runtime` or `full` grants no permission.
Default configuration denies execution. Shared/network hosts must supply trusted
transport identity; tool arguments cannot claim an owner.

The public `IWotRuntimeExecutorFactory` is the host's injectable composition seam.
Its default implementation reuses `IWotBinderRegistry`,
`IWotBindingChannelFactory`, the shipped planners/executors and their codecs.
Direct construction remains available for embedding and tests.

### Exact network and operation rules

`McpNetworkRule` requires the exact scheme, normalized hostname, explicit port,
allowed CIDR networks, capability, operation and resource. Empty rules deny all.
Caller-supplied TD targets are permitted when these host rules match.

| Protocol | Rule operation | Rule resource |
| --- | --- | --- |
| HTTP | Exact HTTP verb, for example `GET` or `PUT` | `PathAndQuery`, for example `/temperature` |
| MQTT | `subscribe` or `publish` | Exact topic, for example `things/temperature` |
| Modbus | Existing planner method, for example `readHoldingRegisters` | `unit=1;function=readHoldingRegisters;address=100;quantity=2` |
| OPC UA read/write/observe | `Read`, `Write`, `Subscribe` or `EventSubscribe` | Namespace-qualified node identifier |
| OPC UA call | `Call` | `object=<namespace-qualified NodeId>;method=<namespace-qualified NodeId>` |
| OPC UA path call | `Call` | Browse-path selector (format below) |

Every call rechecks `WotRuntime`; writes additionally need `WotWrite`, and actions
need `WotInvoke`. OPC UA additionally requires the exact named-session grant.
Generic UA protection is reused for OPC UA writes/actions so a WoT form cannot
bypass typed, secret-safe GDS or identity-administration tools.
Any Call with a browse path needs a preflight resource of
`object=<NodeId>;browsePath=<anchor>|<steps>`, even when it also states a Method
NodeId. The anchor and steps are percent-encoded separately. Steps are a compact
JSON array of ordered tuples:
`[targetNamespaceUri,targetName,referenceNamespaceUri,referenceName,isInverse,includeSubtypes]`.
Names use the captured namespace URIs, not the document's prefix spelling or
Session indexes. `/` uses `HierarchicalReferences`, `.` uses `Aggregates`, and
both reference names belong to `http://opcfoundation.org/UA/`. Flags are JSON
booleans; tuples contain no whitespace and use the default System.Text.Json
string escaping. This grant admits metadata resolution before any UA service
request. The resolved Method/receiver pair still needs its own exact grant before
runtime activation.

HTTP has no redirects, cookies, ambient credentials or proxy. The socket callback
uses the approved IP from a single DNS resolution while TLS retains the original
hostname. MQTT and Modbus use the same grant at each connection/reconnection.
All resolved addresses must match the host CIDRs. MQTT wildcard topics are refused.
OPC UA borrows an existing generation-bound session matching the compiled origin;
its explicit session-name grant fixes the configured endpoint path and identity.

### Inputs and tools

- `wot_runtime_capabilities`
- `wot_runtime_open`
- `wot_runtime_list`
- `wot_runtime_inspect`
- `wot_runtime_close`
- `wot_runtime_read_property`
- `wot_runtime_write_property`
- `wot_runtime_invoke_action`
- `wot_runtime_observe_property`
- `wot_runtime_subscribe_events`

An open request supplies one inline document of at most 64 KiB or a file under
`TransferRoot`. Explicit dependency documents use the same bounded document
preparation policy. Document-set ZIPs require file input and an exact selected
member when more than one document is present. Runtime preparation never fetches
remote TD dependencies.

```json
{
  "name": "room-temperature",
  "document": { "filePath": "devices/room.json" },
  "selection": {
    "sha256": "<64 hexadecimal digits from document inspection>",
    "kind": "Property",
    "affordance": "temperature",
    "formIndex": 0,
    "bindingId": "w3c.http",
    "bindingVersion": "1.1"
  },
  "operations": ["readproperty", "writeproperty"],
  "dependencies": [],
  "credentials": []
}
```

A typed write uses the actual UA built-in type and JSON value:

```json
{
  "handle": "<runtime handle>",
  "value": { "type": "Double", "value": 21.5 }
}
```

Action inputs are actual JSON arrays of these typed values. HTTP/MQTT accept at
most one body value; extra arguments are refused rather than silently discarded.
The current HTTP read/poll executor supports GET forms; a different authored verb
is refused, not executed as GET.

Results preserve the existing executor's UA Variant type, status and timestamps
through the stack's JSON encoder. The existing JSON payload codec represents
object/array bodies as JSON text; this module does not infer a new structured type
or replace that codec. Completed mutations keep their completed outcome even if
cancellation arrives late or oversized outputs must be omitted. No write or action
is automatically retried.

Observations are finite calls with count, duration and complete-batch byte limits.
Their subscriptions are disposed before the call returns. Event output preserves
both the SDK's typed field envelope and its nested WoT `data` shape.

### Credentials

Only `alias:` and caller-owned `issued:` references are accepted. There are no raw
password/token arguments, arbitrary store paths, automatic certificate trust
changes or credential-read tools.

HTTP supports one explicit Basic, bearer/OAuth2 or header API-key scheme over
HTTPS. MQTT supports one username/password scheme over TLS. Basic/MQTT usernames
use a separate authorized reference. OPC UA uses the named session's configured
identity. Alternative/combined security schemes and query credentials are refused.

Host aliases and issued references must match
`WotRuntimeTarget.CredentialResource` exactly, for example
`wot:https://device.example#/temperature`. The gateway also checks the operation's
capability. Issued-reference expiry/revocation is attached to the shared registry
before runtime creation.

Existing executors cache resolved credentials, so credential-bearing channels are
operation-scoped and are recreated for the next call. MCP never caches credential
material between calls. Alias values are re-resolved per operation; the current
alias gateway has no asynchronous revocation signal, which inspection reports
explicitly. An active finite observation cannot claim immediate alias revocation.

Payload and result processing are bounded independently. MQTT 5's maximum packet
size is advertised to the broker, with a separate payload check before copying or
decoding application messages. That advertisement is not a claim that MQTTnet can
prevent every allocation caused by a malicious peer's oversized wire frame.
