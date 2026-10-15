# OPCFoundation.NetStandard.Opc.Ua.Mcp.Core

OPC UA Model Context Protocol (MCP) tools for the Part 4 services, packaged so
they can be embedded in any MCP server host rather than only run as the shipped
`opcua-mcp` tool.

Use this package when an application wants to offer OPC UA tools to an LLM
*and* add tools of its own.

## Tools

Connect and session management, browse and view services, read and write
attributes, method calls, subscriptions and monitored items, node management,
NodeSet2 export, and PKI/certificate administration.

## Usage

```csharp
builder.Services.AddOpcUaMcpCore();

builder.Services.AddMcpServer()
    .WithStdioServerTransport()
    .WithOpcUaMcpFilters()
    .WithOpcUaCoreTools(McpToolProfile.Services)
    .WithTools<MyApplicationTools>();
```

`WithOpcUaMcpFilters` registers the request and schema filters that make tool
errors actionable and tool schemas explicit; call it once per server even when
several OPC UA tool packages are composed.

## Profile composition

Each package has a service registration and a catalogue registration. Pass the
same `McpToolProfileSet` to every selected package; unselected profiles contribute
no tools. Core deduplicates connection tools across a composed host:

```csharp
McpToolProfileSet profiles = McpToolProfileSet.Parse("machinery,isa95");
builder.Services.AddOpcUaMcpCore();
builder.Services.AddOpcUaMcpMachinery();
builder.Services.AddOpcUaMcpIsa95();
builder.Services.AddMcpServer()
    .WithStdioServerTransport()
    .WithOpcUaMcpFilters()
    .WithOpcUaCoreTools(profiles)
    .WithOpcUaMachineryTools(profiles)
    .WithOpcUaIsa95Tools(profiles);
```

Model/package dependencies do not silently register their tool catalogues.
Accessors may also be constructed with an existing `OpcUaSessionManager`;
they do not own its sessions. Profile selection, including `Full`, grants no
authority.

## Arguments and JSON results

`Connect` returns a session name. Pass `sessionName` on later calls; omission is
valid only when one session is connected. `Disconnect` closes only the selected
session. For example, `ReadValue` accepts:

```json
{ "nodeId": "ns=2;s=Temperature", "sessionName": "plant" }
```

Certificate authentication uses a non-secret `certificateIdentity` name configured
by the host, alongside `authType: "Certificate"`. The executable reads
`McpServer:CertificateIdentities:<name>` with `StoreType`, `StorePath`, and exactly
one of `SubjectName` or `Thumbprint`. Embedders populate
`OpcUaMcpOptions.CertificateIdentities` with `McpCertificateIdentity` entries and
can inject `ICertificatePasswordProvider` for protected keys. The existing
certificate store/provider and user-token policy selection perform authentication;
tool callers cannot supply private keys or arbitrary certificate-store locations.
Unknown references fail closed. Expose these identities only to trusted MCP clients.

NodeIds use OPC UA string notation:

| Kind | Example |
| --- | --- |
| Numeric | `i=2258` |
| Namespace index and number | `ns=2;i=1001` |
| String | `ns=2;s=MyVariable` |
| GUID | `ns=2;g=12345678-1234-1234-1234-123456789abc` |
| ByteString | `ns=2;b=AQID` |

Useful namespace-zero IDs include Root `i=84`, Objects `i=85`, Types `i=86`,
Views `i=87`, Server `i=2253` and ServerStatus.CurrentTime `i=2258`.
`WriteValue` takes explicit `dataType` plus the encoded `value`; `CallMethod`
uses `objectId`, `methodId` and ordered `inputArguments`. Use the running
catalogue's schema for each service's typed request shape.

`Call` and `CallMethod` additionally accept `inputArgumentsJson` instead of the
legacy string array. It is an OPC UA JSON Variant array, for example
`[{"UaType":8,"Value":"0"},{"UaType":6,"Value":1}]` for Int64/Int32 arguments.
Structure values use `UaType: 22` and a `Value` object containing `UaTypeId` and
the structure's fields. Load custom types with `schema_load_types` first.
Conflicting argument forms and invalid JSON are rejected before dispatch.
Typed input is limited to 4 MiB of UTF-8 JSON and 1,024 arguments. A non-null
`Value` requires an explicit `UaType`; a missing or misspelled type tag is not
silently interpreted as a null argument. Null encodings and typed defaults remain supported.
Existing `inputArguments` strings are never implicitly reinterpreted as JSON.

Generic read/call results retain an ExtensionObject's `typeId` and decoded `body`
fields through UA codecs. Unregistered encoded bodies remain explicitly encoded;
they are not reduced to a type name or silently omitted.

`Publish` pairs `ackSubscriptionIds` and `ackSequenceNumbers` by index. Their
lengths must match; omitted and empty arrays both mean no acknowledgements.
Its `results` array preserves each acknowledgement's status in request order,
including failures such as `BadSequenceNumberUnknown` even when the service
response itself is `Good`.

UA JSON preserves types, quality and timestamps. Variant serialization uses
`BuiltInType`, so Boolean `false` and numeric zero do not become Variant.Null.
Non-finite companion measurements are `"NaN"`, `"Infinity"` or `"-Infinity"`.
Companion tools return matching textual JSON and MCP structured content.

Service failures retain their UA status, for example:

```json
{
  "error": true,
  "statusCode": "BadNodeIdUnknown",
  "message": "The node id refers to a node that does not exist in the server address space."
}
```

Shared companion pages contain `items`, `offset`, `nextOffset`, `hasMore` and
`consistency: "live"`. Page sizes default to 100 and cap at 500; an offset is not
a retained snapshot. Observations are finite, report the completion/item/time
limit, and dispose only their own enumeration. Individual package contracts may
use different limits or continuations.

Connected sessions are exposed through `resources/list` and `resources/read`:
`opcua://sessions`, `opcua://sessions/{name}`, and
`opcua://sessions/{name}/namespaces`.

## Host policy and configuration

The executable binds `McpServer:Execution`; embedding hosts set
`OpcUaMcpOptions.Execution`. Independent capabilities and exact caller/session
grants are required. This example grants only local role/user administration:

```json
{
  "McpServer": {
    "ToolProfile": "roles,users",
    "Execution": {
      "EnabledCapabilities": [ "IdentityAdministration" ],
      "CallerGrants": [
        {
          "Caller": {
            "Subject": "stdio",
            "Issuer": "urn:opcfoundation:mcp:local-host",
            "IsLocal": true
          },
          "Capabilities": [ "IdentityAdministration" ],
          "SessionNames": [ "maintenance" ],
          "SecretAliases": []
        }
      ]
    }
  }
}
```

Stdio uses this explicit local identity. HTTP must authenticate requests and
provide a stable subject/issuer; it rejects `LocalCallerId` as an authority
source. Caller-supplied tool arguments never choose the trusted principal.
Remote permissions and security mode remain additional requirements.

Live network rules identify an exact scheme, host, port, operation, resource
and permitted CIDRs. Empty grants deny access. DNS is checked at actual
connection/reconnection, and sockets use approved IPs while TLS retains the
original hostname. UDP/Ethernet add interface/peer/frame rules. See the PubSub
and WoT package READMEs for protocol selectors.

An issuance host registers `ISecretRegistry`, resource-bound `McpSecretAlias`
bindings and an explicit writable `ISecretStore` through `AddOpcUaMcpSecrets`
before registering domain services. No writable store is chosen implicitly.
References reserve capacity before remote issuance; a later persistence failure
is a partial outcome, not rollback. Private material never enters a transfer
artifact.

### Transfer paths

Set `OpcUaMcpOptions.TransferRoot` and `MaxTransferBytes`, or executable keys
`McpServer:TransferRoot` / `McpServer:MaxTransferBytes`. Equivalent environment
variables are `OPCUA_MCP_TRANSFER_ROOT` / `OPCUA_MCP_MAX_TRANSFER_BYTES`.
The directory must exist; the default limit is 64 MiB. No configured root means
transfers are disabled.

Paths stay beneath the root. Symlinks, junctions, traversal and Windows alternate
data streams are refused. Keep the root and ancestors unwritable by untrusted
local users; portable checks do not prevent concurrent privileged replacement.
Uploads enforce length before and during reads. Downloads use bounded temporary
files and publish without overwrite; failures remove partial output. Parent
directories must exist.

See the [MCP catalogue](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/docs/McpServer.md)
for individual tool behavior.

## Generic mutations and session lifetime

Generic Call/CallMethod, Write/WriteValue, HistoryUpdate and node/reference
mutations capture a `SessionRequestContext` for stack `ManagedSession` instances
before classification. The same borrowed session performs classification and
dispatch. Recovery cannot turn an ordinary preflight into a write to a different
session or remapped protected namespace. The host's registered session name,
not the server-provided name, remains the policy authority. An acknowledged
mutation result is not discarded by a later cancellation or session change.

**Embedding compatibility:** a custom `ISession` registered with
`RegisterExistingSessionAsync` must have an explicit host-owned
`IMcpFixedSessionContract` in DI before generic mutations are accepted.
Undeclared custom sessions fail closed with `BadNotSupported`; read-only tools
remain unchanged. `IsFixed(registration)` may return true only when the host
guarantees no reconnect, retarget, authentication change, or namespace/type-cache
change between classification and dispatch. This grants no operation
capability and is not a synthetic SDK generation gate. Stack managed sessions
always use the SDK context, regardless of this contract.

The fixed contract and observable metadata are rechecked before dispatch, not
after a response. These checks cannot make an independently mutable custom
implementation safe; use a stack managed session for that case. Server-side
authorization remains authoritative, and remote model changes within the same
session are not made into an atomic server transaction.

## Trimming and Native AOT

The assembly is marked `IsAotCompatible` on `net10.0`: its own IL is free of trim
and AOT warnings, and tool results are written with `Utf8JsonWriter` rather than
the reflection-based `JsonSerializer`.

That is not yet the same as working in an ahead-of-time published host. Registering
the tools still requires JSON reflection, because the MCP SDK builds each tool's
input schema by asking `JsonSchemaExporter` for a `JsonTypeInfo` of every parameter
type; without a source-generated context covering the tool signatures this throws
`NotSupportedException`. `Opc.Ua.Aot.Tests.McpAotTests` pins that limitation so it
is stated rather than discovered at run time.

`OPCFoundation.NetStandard.Opc.Ua.Mcp.Diagnostics` is deliberately *not* marked
AOT-compatible: it depends on SharpPcap and on reflective service-call dissection.

## Related packages

| Package | Adds |
|---|---|
| `OPCFoundation.NetStandard.Opc.Ua.Mcp.Diagnostics` | packet capture, decode and replay, including key logging |
| `OPCFoundation.NetStandard.Opc.Ua.Mcp.PubSub` | PubSub runtime, actions and discovery |
| `OPCFoundation.NetStandard.Opc.Ua.Mcp.PubSub.Diagnostics` | PubSub packet capture and decode |
| `OPCFoundation.NetStandard.Opc.Ua.Mcp` | the ready-to-run `opcua-mcp` server composing all of the above |

## License

OPC Foundation MIT License 1.00 — <http://opcfoundation.org/License/MIT/1.00/>
