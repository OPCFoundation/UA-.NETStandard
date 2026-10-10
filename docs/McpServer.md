# OPC UA MCP Server

The OPC UA MCP Server exposes OPC UA Part 4 service calls as
[Model Context Protocol (MCP)](https://modelcontextprotocol.io) tools. AI
assistants such as Claude, GitHub Copilot, VS Code Copilot, and Cursor can
use these tools to connect to OPC UA servers and interact with industrial
automation systems through natural language.

## Contents

- [What It Does](#what-it-does)
- [Tool Profiles](#tool-profiles)
  - [Vision-guided Robotics](#vision-guided-robotics)
  - [Industrial companion tools](#industrial-companion-tools)
- [Resources](#resources)
  - [Multi-Session Support](#multi-session-support)
- [Installation](#installation)
  - [Option 1: Run without installing (.NET 10 SDK)](#option-1-run-without-installing-net-10-sdk)
  - [Option 2: Install as a .NET global tool](#option-2-install-as-a-net-global-tool)
  - [Option 3: Run from source](#option-3-run-from-source)
  - [Option 4: Install from local build](#option-4-install-from-local-build)
- [Configuration](#configuration)
  - [Run on demand without a global installation](#run-on-demand-without-a-global-installation)
  - [Claude Desktop](#claude-desktop)
  - [VS Code / GitHub Copilot](#vs-code--github-copilot)
  - [Cursor](#cursor)
  - [HTTP Transport (for remote clients)](#http-transport-for-remote-clients)
  - [Companion file transfers](#companion-file-transfers)
- [Usage](#usage)
  - [Companion workflows](#companion-workflows)
  - [Companion results, errors and bounds](#companion-results-errors-and-bounds)
  - [Typical Workflow](#typical-workflow)
  - [NodeId Formats](#nodeid-formats)
  - [Common Well-Known NodeIds](#common-well-known-nodeids)
  - [Error Handling](#error-handling)
- [PubSub Tools](#pubsub-tools)
- [Architecture](#architecture)
  - [Packages](#packages)
  - [Embedding the tools in your own MCP server](#embedding-the-tools-in-your-own-mcp-server)
  - [Embedding industrial companions](#embedding-industrial-companions)
- [Security Notes](#security-notes)
- [Agent-Usability Quality Gate](#agent-usability-quality-gate)
- [Requirements](#requirements)

## What It Does

The MCP server wraps the OPC UA .NET Standard client library, translating between JSON-based MCP tool calls and OPC UA binary protocol operations. The server exposes tools through a [tool profile](#tool-profiles) — a named, bounded catalog selected at startup — rather than a single fixed tool count. The default `full` profile currently registers every tool below; running a narrower profile (`core`, `services`, `administration`, `pubsub`, `diagnostics`, `robotics`, or `vision`) exposes only the subset relevant to that workflow. The tables below list the complete tool surface, organized by OPC UA Part 4 service set:

| Service Set | Tools | Description |
|---|---|---|
| **Connection** | `GetEndpoints`, `Connect`, `Disconnect`, `GetConnectionStatus` | Endpoint discovery and session lifecycle management |
| **Attribute** | `Read`, `Write`, `HistoryRead`, `HistoryUpdate` | Read/write node attributes and historical data |
| **View** | `Browse`, `BrowseNext`, `TranslateBrowsePaths`, `RegisterNodes`, `UnregisterNodes`, `QueryFirst`, `QueryNext` | Navigate and query the address space |
| **Node Management** | `AddNodes`, `AddReferences`, `DeleteNodes`, `DeleteReferences` | Modify the address space |
| **Method** | `Call` | Invoke OPC UA methods |
| **Subscription** | `CreateSubscription`, `ModifySubscription`, `SetPublishingMode`, `Publish`, `Republish`, `DeleteSubscriptions`, `TransferSubscriptions` | Manage notification subscriptions |
| **MonitoredItem** | `CreateMonitoredItems`, `ModifyMonitoredItems`, `SetMonitoringMode`, `SetTriggering`, `DeleteMonitoredItems` | Monitor data changes and events |
| **Discovery** | `FindServers`, `FindServersOnNetwork`, `RegisterServer`, `RegisterServer2` | Discover servers and register |
| **PKI Management** | `ListCertificates`, `TrustCertificate`, `RemoveCertificate`, `GetPkiStorePaths` | Manage certificate trust lists |
| **Configuration** | `GetConfiguration`, `SetTransportConfiguration`, `SetClientConfiguration`, `SetSecurityConfiguration`, `SetConfiguration` | View/modify in-memory client settings; `SetConfiguration` is the `full`-profile compatibility tool |
| **NodeSet Export** | `ExportNodeSet`, `ExportNodeSetPerNamespace` | Export address space to NodeSet2 XML |
| **Convenience** | `ReadValue`, `ReadValues`, `WriteValue`, `BrowseAll`, `CallMethod`, `ReadNode`, `Cancel` | Simplified high-level operations |
| **Packet Capture** | `list_interfaces`, `start_capture`, `stop_capture`, `list_captures`, `get_capture`, `capture_now`, `list_active_channels`, `dump_keys`, `decode_pcap_with_keys`, `summarize_service_calls`, `replay_pcap`, `stop_replay`, `list_replays` | OPC UA-aware packet capture, offline decode, service-call summaries, replay |

All OPC UA types (NodeId, DataValue, Variant, StatusCode, etc.) are represented as JSON for LLM-friendly interaction. Variant serialization dispatches by `BuiltInType`, so scalar defaults such as Boolean `false` and numeric `0` remain JSON `false` / `0` instead of being confused with `Variant.Null`.

The packet-capture tools are described in detail in [Diagnostics](Diagnostics.md#4-packet-capture-dissection-and-replay). For convenience the per-tool surface is:

| Tool | Description | Parameters |
|---|---|---|
| `list_interfaces` | Enumerates NICs available to SharpPcap; `linkType` may be `null` because enumeration does not open adapters. | None |
| `start_capture` | Starts a capture session. | `source`: `nic \| inproc-client \| inproc-server \| replay`; optional interface, filter, endpoint, limits, folder |
| `stop_capture` | Stops an active session and finalizes artifacts. | `sessionId` |
| `list_captures` | Lists capture sessions. | Optional `state` |
| `get_capture` | Returns an artifact or formatted analysis. | `sessionId`; `format`: `pcap \| pcapng \| json \| csv \| text \| service-timeline`; optional packet/partial controls |
| `capture_now` | Starts, waits, stops, and returns output. | Capture options plus the same output `format` values |
| `list_active_channels` | Lists in-process secure channels with current tokens. | None |
| `dump_keys` | Emits keylog data. | Optional `sessionId`, `format`, `includeExpired` |
| `decode_pcap_with_keys` | Decodes an existing pcap and keylog offline. | `pcapPath`, `keylogPath`, `format`, optional `maxFrames` |
| `summarize_service_calls` | Reports service counts, latency, and errors. | `sessionId` or `pcapPath` + `keylogPath`, optional `top` |
| `replay_pcap` | Replays as a mock server or mock client. | `pcapPath`, `keylogPath`, `mode`, endpoints, `speed` |
| `stop_replay` | Stops an active replay session. | `sessionId` |
| `list_replays` | Lists active and recently-completed replay sessions. | None |

The canonical hyphenated values above are emitted on output. CLR enum
names such as `InProcessClient` and `ServiceTimeline` remain accepted
on input.

## Tool Profiles

The server selects its tool catalog through a **tool profile** — a bounded set of tool classes registered at startup. Profiles let a client request only the tools it needs (smaller catalogs are easier for an LLM to reason about and reduce prompt size), while `full` preserves the complete surface for clients that want everything.

| Profile | Tool classes registered | Typical use case |
|---|---|---|
| `core` | Configuration, Connection, Convenience | Minimal footprint: connect, read/write values, adjust settings |
| `services` | Attribute, Configuration, Connection, Convenience, Discovery, Method, MonitoredItem, Node Management, Subscription, View | Full OPC UA Part 4 client workflows without PKI/PubSub/packet capture |
| `administration` | Configuration, Connection, NodeSet Export, PKI Management | Certificate trust management and NodeSet export |
| `pubsub` | PubSub runtime, discovery, action, and capture tools (plus PubSub decode when diagnostics tools are enabled) | Part 14 PubSub publish/subscribe, discovery, and capture workflows |
| `diagnostics` | Connection, Packet Capture (plus decode/replay when diagnostics tools are enabled) | OPC UA-aware packet capture, offline decode, and replay |
| `robotics` | Connection plus Robot Intent discovery, paged monitoring, control, mission and Vision-guided Pick tools | Commanding and monitoring a Robot Intent controller |
| `vision` | Connection plus the Vision discovery, monitoring, seeing, inference, feedback and geometry tools | Perception-driven agents that need to see through a Vision server, compose poses across the §5.12 frame graph, run or submit inference, and (when composed with `robotics`) act on what they see — see the [Vision developer guide](Vision.md) |
| `amb` | Connection plus asset discovery, reads, observations and explicit asset changes | Asset inventory, health, maintenance and documentation |
| `machinery` | Connection plus machine building blocks, process values, energy, job information and results | Machine monitoring and result retrieval; compose with `isa95` for job commands |
| `scales` | Connection plus weights, products, recipes and scale/PackML commands | Weighing workflows; compose with `di` for explicit product locking |
| `pumps` | Connection plus pump identity, values, ports, supervision and maintenance | Inspect pump state without inventing control methods |
| `di` | Connection plus device topology, identification, locks, transfers and software updates | Device configuration and explicitly requested updates |
| `isa95` | Connection plus common objects and V1/V2 job order/response tools | Job submission, control and monitoring |
| `positioning` | Connection plus spatial frames, global locations and coordinate conversion | Inspect and transform relative/global positions |
| `full` (default) | Every tool class above | Unrestricted access; the current-major default so existing integrations keep working unchanged |

`full` is the default for the current major version — `core` and the other bounded profiles are opt-in. Select a profile with:

- The `--profile` CLI option, e.g. `opcua-mcp --profile core`
- The `McpServer:ToolProfile` configuration value
- The `OPCUA_MCP_TOOL_PROFILE` environment variable

**Profiles compose.** A `--profile` value can name more than one bounded profile at a time — the `BinPickingClient` sample runs `--profile vision,robotics` and exposes both catalogs from the same MCP host, deduplicating the shared `Connection` tools. The composed set uses the `WithOpcUaCoreTools(McpToolProfileSet)` / `WithOpcUaVisionTools(McpToolProfileSet)` / `WithOpcUaRoboticsTools(McpToolProfileSet)` overloads, and the core-tools overload owns the single `ConnectionTools` registration across every package that references the same MCP server. See the [Vision developer guide](Vision.md#mcp-tools) for the composed 64-tool example and the [BinPickingClient sample](../samples/Robotics/BinPickingClient) for the running catalog.

Because the exact number of tools in each profile (and in `full`) changes as tools are added or removed, this document intentionally does not hard-code a tool count. Use the tables above (or `tools/list`) to enumerate the tools actually exposed by a running server.

The [industrial companion tools](#industrial-companion-tools) cover the seven
families, cross-family handoffs, bounded observations and configured file
transfers. The new catalogs are additive to `full`; existing bounded
profiles keep their tool sets. Profiles select a catalog, not permissions.

### Vision-guided Robotics

`robotics_vision_pick` closes the common perception-to-action path without
making an agent copy a result NodeId and several resource NodeIds between tool
calls. It runs one detection inference through `Opc.Ua.Vision.Client`, on the
same named OPC UA session as the Robot Intent controller, then submits either
one Pick or a two-step Pick/Place mission:

```json
{
  "request": {
    "controller": "BinPickingController",
    "pipeline": "BinPickingPipeline",
    "source": "Bin",
    "tool": "ParallelGripper",
    "destination": "Fixture",
    "classLabel": "RedCube",
    "minimumConfidence": 0.9,
    "missionId": "place-red-cube"
  }
}
```

Detection selection is deterministic: exact DetectionId/ClassLabel filters and
the confidence threshold are applied first, then highest confidence wins with
ordinal DetectionId and original result order as tie-breakers. The result
contains Vision result/pipeline/sensor/model/frame provenance, the selected
pose, and the authoritative intent or mission handle. The helper does not
request authority, wait, retry, cancel, or reinterpret a server refusal.

### Industrial companion tools

The MCP server exposes the stack's typed companion clients as task-oriented
tools. It does not create another OPC UA connection or reimplement the models:
each invocation resolves the current named session and calls the existing
client or source-generated ObjectType proxy.

Select a single industrial profile or compose the catalogs needed by a workflow:

```powershell
dnx OPCFoundation.NetStandard.Opc.Ua.Mcp -- --profile amb
dnx OPCFoundation.NetStandard.Opc.Ua.Mcp -- --profile machinery,isa95
dnx OPCFoundation.NetStandard.Opc.Ua.Mcp -- --profile scales,di
```

| Profile | Scope | Model guide |
| --- | --- | --- |
| `amb` | Asset discovery, identification, health, maintenance, documentation links, locations and relationships | [AMB](AMB.md) |
| `machinery` | Machines and building blocks, state, counters, process values, energy, job information and results | [Machinery](Machinery.md) |
| `scales` | Weights and units, products, recipes, scale-specific operations and PackML | [Scales](Scales.md) |
| `pumps` | Pump identity, configuration, operational measurements, supervision and maintenance | [Pumps](Pumps.md) |
| `di` | Device topology and identity, locking, parameter transfer and software update | [Device Integration](DeviceIntegration.md) |
| `isa95` | Common objects and V1/V2 job order control, responses and status | [ISA-95](ISA95.md) |
| `positioning` | Relative spatial frames, global positions, zones and coordinate conversion | [Positioning](Positioning.md) |

`full` includes every industrial tool, but also includes unrelated workflows
and consequential operations. Prefer a smaller profile or composition for
agent work. Profile selection is not an authorization check; use `tools/list`
to inspect the exact names and input schemas.

IA, PA-DIM, ECM and PackML are represented through the typed clients that use
them, not through duplicate catalogs. AI, WoT Connectivity, OpenUSD and
xRegistry are outside this industrial set. See [companion workflows](#companion-workflows)
for cross-family handoffs, [file transfers](#companion-file-transfers) for host
policy, and [embedding industrial companions](#embedding-industrial-companions)
for library registration.

## Resources

The MCP server exposes connected sessions as **MCP resources**, enabling the LLM to discover, inspect, and subscribe to session state.

| Resource URI | Type | Description |
|---|---|---|
| `opcua://sessions` | Direct | List all active sessions with connection status |
| `opcua://sessions/{name}` | Template | Full details of a named session (endpoint, security, namespaces) |
| `opcua://sessions/{name}/namespaces` | Template | Server namespace table for a session |

### Multi-Session Support

The server supports **multiple simultaneous sessions** to different OPC UA servers. Each session is identified by a name.

```
Tool: Connect
  endpointUrl: "opc.tcp://server1:62541/ReferenceServer"
  name: "refserver"          (optional — auto-generated from hostname if omitted)
  autoAcceptCerts: true

Tool: Connect
  endpointUrl: "opc.tcp://plc1:4840"
  name: "plc1"

Tool: Browse
  nodeId: "i=85"
  sessionName: "refserver"   (optional — uses the only session if there's just one)

Tool: ReadValue
  nodeId: "ns=2;s=Temperature"
  sessionName: "plc1"
```

Sessions are listed via `resources/list` and detailed via `resources/read`.

## Installation

### Option 1: Run without installing (.NET 10 SDK)

Run the published tool directly from nuget.org. `dotnet tool exec` downloads
and caches the tool without adding a global tool installation:

```bash
# Run the latest release
dotnet tool exec OPCFoundation.NetStandard.Opc.Ua.Mcp

# Or stay on one release line, for example 2.0.x
dotnet tool exec "OPCFoundation.NetStandard.Opc.Ua.Mcp@2.0.*"

# Short form of dotnet tool exec
dnx OPCFoundation.NetStandard.Opc.Ua.Mcp
```

### Option 2: Install as a .NET global tool

```bash
dotnet tool install --global OPCFoundation.NetStandard.Opc.Ua.Mcp
```

After installation, the `opcua-mcp` command is available globally.

The tool includes every [tool profile](#tool-profiles), including `robotics`,
`vision`, and the composed `vision,robotics` profile. Only the libraries for
embedding the Robotics and Vision tools in your own server are preview
packages; see [Packages](#packages).

### Option 3: Run from source

```bash
cd tools/Opc.Ua.Mcp
dotnet run -c Release
```

### Option 4: Install from local build

```bash
dotnet pack tools/Opc.Ua.Mcp/Opc.Ua.Mcp.csproj -c Release
dotnet tool install --global --prerelease --add-source tools/Opc.Ua.Mcp/bin/Release OPCFoundation.NetStandard.Opc.Ua.Mcp
```

If multiple prerelease versions are visible from configured sources, add
`--version <the-version-produced-by-dotnet-pack>` to select the local package
explicitly.

## Configuration

### Run on demand without a global installation

MCP clients that accept an executable and argument array can launch the tool
through `dotnet tool exec`. Put MCP tool arguments after `--`:

```json
{
  "mcpServers": {
    "opcua": {
      "command": "dotnet",
      "args": [
        "tool",
        "exec",
        "OPCFoundation.NetStandard.Opc.Ua.Mcp",
        "--",
        "--profile",
        "core"
      ]
    }
  }
}
```

### Claude Desktop

Add to your `claude_desktop_config.json` (typically at `%APPDATA%\Claude\claude_desktop_config.json` on Windows or `~/Library/Application Support/Claude/claude_desktop_config.json` on macOS):

```json
{
  "mcpServers": {
    "opcua": {
      "command": "opcua-mcp"
    }
  }
}
```

### VS Code / GitHub Copilot

Add to your workspace `.vscode/mcp.json`:

```json
{
  "servers": {
    "opcua": {
      "command": "opcua-mcp"
    }
  }
}
```

### Cursor

Add to your Cursor MCP settings:

```json
{
  "mcpServers": {
    "opcua": {
      "command": "opcua-mcp"
    }
  }
}
```

### HTTP Transport (for remote clients)

By default, the server uses stdio transport for local tool integration. For remote clients, use the Streamable HTTP transport, exposed only at the `/mcp` path (there is no root route):

```bash
opcua-mcp --transport http --port 5100
```

The server listens at `http://localhost:5100/mcp`. `--transport sse` is a deprecated alias for `http` kept for the current major version only — prefer `http` in new configurations.

### Companion file transfers

DI package uploads and Machinery result downloads use an explicitly configured,
host-owned directory. They are disabled until that directory is configured and
already exists:

```powershell
$env:OPCUA_MCP_TRANSFER_ROOT = 'D:\OpcUaTransfer'
$env:OPCUA_MCP_MAX_TRANSFER_BYTES = '67108864'
opcua-mcp --profile di,machinery
```

An embedding application can set `OpcUaMcpOptions.TransferRoot` and
`MaxTransferBytes`; the executable also accepts configuration keys
`McpServer:TransferRoot` and `McpServer:MaxTransferBytes`. The default byte limit
is 64 MiB. A caller cannot raise it through a tool argument.

Tool paths must resolve beneath the root. Symlinks, junctions, path escapes
and Windows alternate data streams are rejected. The host must keep the root
and its ancestors unwritable by untrusted local users; portable path checks
cannot prevent a privileged local process replacing directories concurrently.
Do not use a shared world-writable directory as the transfer root.

Uploads check the initial length and enforce the limit again while reading.
Downloads enforce the limit while writing, use a temporary file, publish the
completed file without overwriting an existing destination, and remove partial
files on failure or cancellation. Parent directories must already exist.
There is no arbitrary URL fetching or large inline base64 transport.
Uploading a software package does not install or confirm it; those are separate
explicit operations.

## Usage

### Companion workflows

Connect using the usual MCP connection tools. Every family accepts
`sessionName`; omit it only when there is exactly one active session.
Discovery returns explicit node identifiers. Use those identifiers for later
reads and actions rather than guessing namespace indexes or selecting the first
matching object. Returned remote references are information, not an instruction
to connect to another server.

Related reads share finite facet selectors where that keeps the catalog small.
Commands remain explicit and do not implicitly acquire a lock, install a
package, retry a refused operation or change another state.

Two workflows intentionally compose catalogs:

- **Machine jobs:** discover the real order receiver and optional response
  provider through Machinery, then use the canonical ISA-95 tools with those
  role identifiers. A Machinery machine does not expose an ISA-95 response
  receiver. An absent response provider must not be replaced with the order
  receiver.
- **Scale products:** discover the product's DI lock and use the DI tools to
  acquire, renew and release it explicitly around protected changes. The
  Scales tools never acquire or break a lock on the caller's behalf.

There are no invented Machinery item-state or operation-mode setters: those
state machines have no standard client cause methods. Pumps and Positioning
remain observational. AMB history is not promised where the typed client has
no history implementation.

Creation-time bounds passed to `machinery_list_results` must include `Z` or
an explicit UTC offset. For example, `2026-01-01T00:00:00Z` and
`2026-01-01T02:00:00+02:00` identify the same instant. Timezone-less timestamps
are rejected instead of being interpreted in the MCP host's local timezone.

### Companion results, errors and bounds

Companion tools return matching textual JSON and MCP structured content.
UA values retain their type, quality and timestamps where the client contract
provides them. Non-finite numbers are explicit `"NaN"`, `"Infinity"` or
`"-Infinity"` strings, not zero or an absent measurement. Structured UA values
use their generated encoding implementation instead of reflective property
enumeration or a `ToString()` summary.

A service failure is an MCP error with its original status. Model-specific
return codes, including ISA-95 job and DI lock statuses, are not confused
with a successful UA service response. Missing optional capabilities are
distinguished from an empty supported result. A timeout does not prove that
a remote mutation did not occur; inspect server state before deciding to retry.

Paged lists return `items`, `offset`, `nextOffset`, `hasMore` and
`consistency: "live"`. The default page size is 100, with a maximum of 500.
Offsets are bounded and refer to a fresh live enumeration, not a durable
snapshot. Concurrent server changes can shift entries between pages.

Observations are finite MCP calls, not persistent background subscriptions.
They accept an item limit and a duration of at most 30 seconds, and report
whether the source completed or the item/duration bound ended the call.
External request cancellation propagates. Disposing the invocation's
enumerator removes its own monitored items; it does not dispose the managed
session's shared streaming subscription.

### Typical Workflow

**1. Discover endpoints (no session required):**

```
Tool: GetEndpoints
  endpointUrl: "opc.tcp://localhost:62541/Quickstarts/ReferenceServer"
```

**2. Connect to an OPC UA server (auto-select most secure, anonymous):**

```
Tool: Connect
  endpointUrl: "opc.tcp://localhost:62541/Quickstarts/ReferenceServer"
  autoAcceptCerts: true
```

**Or connect with specific security and authentication:**

```
Tool: Connect
  endpointUrl: "opc.tcp://localhost:62541/Quickstarts/ReferenceServer"
  securityMode: "SignAndEncrypt"
  securityPolicy: "Basic256Sha256"
  authType: "Username"
  username: "admin"
  password: "password"
  autoAcceptCerts: true
```

**2. Explore the address space:**

```
Tool: BrowseAll
  nodeId: "i=85"     (Objects folder)
  maxDepth: 2
  maxResults: 50
```

**3. Read values:**

```
Tool: ReadValue
  nodeId: "ns=2;s=MyTemperatureSensor"
```

**4. Write values:**

```
Tool: WriteValue
  nodeId: "ns=2;s=MySetpoint"
  value: "72.5"
  dataType: "Double"
```

**5. Call a method:**

```
Tool: CallMethod
  objectId: "ns=2;s=MyMachine"
  methodId: "ns=2;s=StartProcess"
  inputArguments: ["fast", "true"]
```

**6. Monitor changes:**

```
Tool: CreateSubscription
  publishingInterval: 1000

Tool: CreateMonitoredItems
  subscriptionId: <from above>
  nodeIds: ["ns=2;s=Temperature", "ns=2;s=Pressure"]

Tool: Publish
  (retrieves queued notifications)
```

**7. Manage PKI (trust rejected certificates):**

```
Tool: ListCertificates
  store: "Rejected"

Tool: TrustCertificate
  thumbprint: "A1B2C3..."    (from ListCertificates results)

Tool: GetPkiStorePaths
  (shows where certificate stores are located on disk)
```

**8. Adjust configuration for current session:**

```
Tool: GetConfiguration
  (view current settings)

Tool: SetConfiguration
  operationTimeout: 60000
  maxArrayLength: 131072
  autoAcceptUntrustedCertificates: true
```

**9. Export the server's address space to NodeSet2 XML:**

```
Tool: ExportNodeSet
  filePath: "C:\\export\\server-nodeset.xml"
  startingNodeId: "i=85"     (Objects folder)
  exportMode: "Complete"     (includes values)
```

Or export split by namespace (one file per companion spec):

```
Tool: ExportNodeSetPerNamespace
  outputDirectory: "C:\\export\\namespaces"
```

**10. Disconnect:**

```
Tool: Disconnect
```

### NodeId Formats

The MCP server accepts NodeIds in standard OPC UA string format:

| Format | Example | Description |
|---|---|---|
| Numeric | `i=2258` | Numeric identifier in namespace 0 |
| Numeric with namespace | `ns=2;i=1001` | Numeric identifier in namespace 2 |
| String | `ns=2;s=MyVariable` | String identifier |
| GUID | `ns=2;g=12345678-1234-1234-1234-123456789abc` | GUID identifier |
| Opaque | `ns=2;b=Base64EncodedData` | ByteString identifier |

### Common Well-Known NodeIds

| NodeId | Description |
|---|---|
| `i=84` | Root node |
| `i=85` | Objects folder |
| `i=86` | Types folder |
| `i=87` | Views folder |
| `i=2253` | Server object |
| `i=2258` | Server/ServerStatus/CurrentTime |

### Error Handling

When an OPC UA service returns an error, tools return a structured JSON response:

```json
{
  "error": true,
  "statusCode": "BadNodeIdUnknown",
  "message": "The node id refers to a node that does not exist in the server address space."
}
```

This is normal behavior — not all servers support all services. Common status codes:

| Status Code | Meaning |
|---|---|
| `BadServiceUnsupported` | Server doesn't implement this service |
| `BadNodeIdUnknown` | NodeId doesn't exist |
| `BadNotWritable` | Node attribute is read-only |
| `BadMethodInvalid` | Method not found on the specified object |
| `BadUserAccessDenied` | Insufficient permissions |

## PubSub Tools

In addition to the client services above, the server exposes OPC UA PubSub
(Part 14) tools, backed by `Opc.Ua.PubSub` and
`Opc.Ua.PubSub.Diagnostics`. See
[Diagnostics.md §5](Diagnostics.md#5-pubsub-packet-capture-and-dissection) for
the capture / dissection details.

**Configuration and Security Key Service methods.** PubSub configuration
(`AddConnection`, `AddWriterGroup`, `AddReaderGroup`, `AddDataSetWriter`,
`AddDataSetReader`, `Status.Enable` / `Disable`) and the Security Key Service
(`GetSecurityKeys`, `AddSecurityGroup` / `RemoveSecurityGroup`) are standard
server-side `PublishSubscribe` object methods, so they are invoked with the
generic [`Call`](#usage) tool rather than dedicated wrappers — pass the
`PublishSubscribe` object NodeId (or the target connection / group NodeId) and
the corresponding method NodeId (e.g. `i=14443` for
`PublishSubscribe_AddConnection`, `i=15215` for
`PublishSubscribe_GetSecurityKeys`).

**In-process publish/subscribe runtime:**

| Tool | Purpose |
| --- | --- |
| `pubsub_runtime_start_publisher` / `pubsub_runtime_start_subscriber` | Start an in-process UDP publisher / subscriber |
| `pubsub_runtime_publish` | Publish a DataSet update |
| `pubsub_runtime_read_received` | Read DataSets received by the subscriber |
| `pubsub_runtime_status` / `pubsub_runtime_stop` | Status / stop the runtime |

**Discovery** (Part 14 §7.2.4.6 — send a discovery request from the active
runtime and collect publisher responses):

| Tool | Purpose |
| --- | --- |
| `pubsub_discover_metadata` | Learn the field-level schema of a DataSetWriter (names, field count) |
| `pubsub_discover_writer_config` | Learn a publisher's WriterGroupId and DataSetWriterIds |
| `pubsub_discover_publisher_endpoints` | Learn a publisher's transport endpoint URLs |

**Actions** (Part 14 §7.2.5.6 — request/response over PubSub):

| Tool | Purpose |
| --- | --- |
| `pubsub_invoke_action` | Invoke an action target and await the correlated response |
| `pubsub_register_action_responder` | Register a demo/echo responder for round-trip testing |
| `pubsub_bind_action_method` | Bind an action to a server method (ObjectId/MethodId) |
| `pubsub_list_action_targets` / `pubsub_list_action_responders` | List known targets / registered responders |

**Capture and dissection:**

| Tool | Purpose |
| --- | --- |
| `pubsub_start_capture` / `pubsub_stop_capture` / `pubsub_capture_status` | Manage an in-process PubSub capture session |
| `pubsub_write_pcap` | Flush captured frames to `.pcap` / `.pcapng` |
| `pubsub_dissect_capture` | Dissect captured frames (decrypts encrypted UADP when a key log is supplied) |
| `pubsub_decode_pcap` | Decode a libpcap file of UDP PubSub traffic |
| `pubsub_load_keylog` | Load a PubSub key log for offline decryption |

## Architecture

The MCP tools ship as thirteen libraries plus the executable that composes them.
The executable owns only transport, logging and CLI plumbing; every tool lives
in a library that an application can reference on its own.

```
tools/
├── Opc.Ua.Mcp.Core/                     # Part 4 service tools, session manager, options, filters
│   ├── OpcUaMcpCoreExtensions.cs        # AddOpcUaMcpCore, WithOpcUaCoreTools, WithOpcUaMcpFilters
│   ├── OpcUaSessionManager.cs           # OPC UA client session lifecycle
│   ├── McpCapturePath.cs                # Path-traversal guard shared by the capture packages
│   ├── Tools/
│   │   ├── ConnectionTools.cs           # GetEndpoints, Connect, Disconnect, GetConnectionStatus
│   │   ├── AttributeServiceTools.cs     # Read, Write, HistoryRead, HistoryUpdate
│   │   ├── ViewServiceTools.cs          # Browse, BrowseNext, TranslateBrowsePaths, etc.
│   │   ├── NodeManagementServiceTools.cs# AddNodes, AddReferences, DeleteNodes, etc.
│   │   ├── MethodServiceTools.cs        # Call
│   │   ├── SubscriptionServiceTools.cs  # CreateSubscription, Publish, etc.
│   │   ├── MonitoredItemServiceTools.cs # CreateMonitoredItems, etc.
│   │   ├── DiscoveryServiceTools.cs     # FindServers, RegisterServer, etc.
│   │   ├── PkiTools.cs                  # ListCertificates, TrustCertificate, etc.
│   │   ├── ConfigurationTools.cs        # Full-profile compatibility configuration tools
│   │   ├── ConfigurationReadTools.cs    # Profile-safe configuration reader
│   │   ├── ConfigurationUpdateTools.cs  # Focused transport/client/security setters
│   │   ├── NodeSetExportTools.cs        # ExportNodeSet, ExportNodeSetPerNamespace
│   │   └── ConvenienceTools.cs          # ReadValue, BrowseAll, CallMethod, etc.
│   └── Serialization/
│       └── OpcUaJsonHelper.cs           # OPC UA ↔ JSON type conversion
├── Opc.Ua.Mcp.PubSub/                   # PubSub runtime, actions and discovery
│   ├── PubSubRuntimeManager.cs
│   └── Tools/{PubSubRuntime,PubSubAction,PubSubDiscovery}Tools.cs
├── Opc.Ua.Mcp.Diagnostics/              # UA-TCP capture, decode and replay
│   └── Tools/{PacketCapture,PacketDecode,PacketReplay}Tools.cs
├── Opc.Ua.Mcp.PubSub.Diagnostics/       # PubSub capture and decode
│   └── Tools/{PubSubCapture,PubSubDecode}Tools.cs
├── Opc.Ua.Mcp.Robotics/                 # Robot Intent plus same-session Vision-guided picking
│   ├── RoboticsIntentManager.cs
│   ├── VisionGuidedRoboticsManager.cs
│   └── Tools/Robotics{Discovery,Monitoring,Control,Mission,Vision}Tools.cs
├── Opc.Ua.Mcp.Vision/                   # Vision discovery, monitoring, seeing, inference, feedback, geometry
│   ├── VisionClientAccessor.cs
│   └── Tools/Vision{Discovery,Monitoring,Seeing,Inference,Feedback,Geometry}Tools.cs
├── Opc.Ua.Mcp.AMB/                      # Asset inventory, health, maintenance and documentation
├── Opc.Ua.Mcp.Machinery/                # Machine monitoring, process values, energy, jobs and results
├── Opc.Ua.Mcp.Scales/                   # Weighing, products, recipes and PackML
├── Opc.Ua.Mcp.Pumps/                    # Pump measurements, supervision and maintenance
├── Opc.Ua.Mcp.Di/                       # Device topology, locking, transfers and software updates
├── Opc.Ua.Mcp.ISA95/                    # Common objects and V1/V2 job control
├── Opc.Ua.Mcp.Positioning/              # Spatial frames, global locations and transforms
└── Opc.Ua.Mcp/                          # .NET 10 project, packaged as dotnet tool
    ├── Program.cs                       # Entry point, stdio + Streamable HTTP transport (/mcp)
    ├── McpHostBuilder.cs                # Composes the tool libraries
    ├── Opc.Ua.Mcp.Config.xml            # OPC UA client application config
    └── .mcp/server.json                 # MCP server manifest for NuGet discovery
```

### Packages

| Package | Tools | Depends on |
|---|---|---|
| `OPCFoundation.NetStandard.Opc.Ua.Mcp.Core` | Part 4 services, connection, configuration, PKI, NodeSet export | `Opc.Ua.Core`, `.Configuration`, `.Client` |
| `OPCFoundation.NetStandard.Opc.Ua.Mcp.PubSub` | PubSub runtime, actions, discovery | Core + `Opc.Ua.PubSub` |
| `OPCFoundation.NetStandard.Opc.Ua.Mcp.Diagnostics` | UA-TCP capture, decode, replay | Core + `Opc.Ua.Core.Diagnostics` |
| `OPCFoundation.NetStandard.Opc.Ua.Mcp.PubSub.Diagnostics` | PubSub capture, decode | Core + `Opc.Ua.PubSub.Diagnostics` |
| `OPCFoundation.NetStandard.Opc.Ua.Mcp.Robotics` | Robot Intent discovery, typed control/missions, paged monitoring, Vision-guided Pick | Core + `Opc.Ua.Robotics.Client` + `Opc.Ua.Vision.Client` |
| `OPCFoundation.NetStandard.Opc.Ua.Mcp.Vision` | Vision discovery, monitoring, seeing (`vision_get_frame` returns an MCP `ImageContentBlock`), inference, off-server feedback, §5.12 pose composition | Core + `Opc.Ua.Vision.Client` |
| `OPCFoundation.NetStandard.Opc.Ua.Mcp.AMB` | Asset inventory, health, maintenance and documentation | Core + `Opc.Ua.AMB.Client` |
| `OPCFoundation.NetStandard.Opc.Ua.Mcp.Machinery` | Machine monitoring, process values, energy, jobs and results | Core + `Opc.Ua.Machinery.Client` |
| `OPCFoundation.NetStandard.Opc.Ua.Mcp.Scales` | Weighing, products, recipes and PackML | Core + `Opc.Ua.Scales.Client` |
| `OPCFoundation.NetStandard.Opc.Ua.Mcp.Pumps` | Pump values, supervision and maintenance | Core + `Opc.Ua.Pumps.Client` |
| `OPCFoundation.NetStandard.Opc.Ua.Mcp.Di` | Device topology, locks, transfers and software updates | Core + `Opc.Ua.Di.Client` |
| `OPCFoundation.NetStandard.Opc.Ua.Mcp.ISA95` | Common objects and job control V1/V2 | Core + `Opc.Ua.ISA95.Client` |
| `OPCFoundation.NetStandard.Opc.Ua.Mcp.Positioning` | Spatial frames, global locations and transforms | Core + `Opc.Ua.Positioning.Client` |
| `OPCFoundation.NetStandard.Opc.Ua.Mcp` | the ready-to-run `opcua-mcp` tool | all of the above |

The libraries multi-target `net8.0;net9.0;net10.0`; the executable targets
`net10.0`. Only the Robotics and Vision MCP libraries are preview-only packages.
The seven industrial companion MCP libraries, the other MCP libraries and the
`opcua-mcp` tool follow the root release channel: a stable root release produces
stable versions, while development builds retain the root prerelease version.

### Embedding the tools in your own MCP server

An application that wants OPC UA tools *and* its own application-level tools
composes them rather than shelling out to `opcua-mcp`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Opc.Ua.Mcp;

builder.Services.AddOpcUaMcpCore();

builder.Services.AddMcpServer()
    .WithStdioServerTransport()
    .WithOpcUaMcpFilters()
    .WithOpcUaCoreTools(McpToolProfile.Services)
    .WithTools<MyApplicationTools>();
```

Add any of the other packages the same way — each contributes a service
registration and a tool registration:

```csharp
builder.Services.AddOpcUaMcpPubSub();
builder.Services.AddOpcUaMcpDiagnostics();
builder.Services.AddOpcUaMcpPubSubDiagnostics();
builder.Services.AddOpcUaMcpRobotics();
builder.Services.AddOpcUaMcpVision();

mcp.WithOpcUaPubSubTools(profile)
   .WithOpcUaDiagnosticsTools(profile, diagnosticsEnabled)
   .WithOpcUaPubSubDiagnosticsTools(profile, diagnosticsEnabled)
   .WithOpcUaRoboticsTools(profile)
   .WithOpcUaVisionTools(profile);
```

`WithOpcUaMcpFilters` registers the request and schema filters that make tool
errors actionable and tool schemas explicit. Call it once per server, even when
several OPC UA tool packages are composed.

`McpToolProfile` is shared vocabulary that lives in Core, and each package maps
the profile to its own tools. A profile a package does not own contributes
nothing rather than failing, so the same profile value can be passed to every
package a host references — `Full` in a host that never referenced
`Opc.Ua.Mcp.Diagnostics` simply yields no capture tools.

Bounded profiles that own their own connection tools — `Vision` in the
example above — carry `ConnectionTools` themselves in the single-profile
overload of `WithOpcUa...Tools`. When two or more bounded profiles are
composed through the `McpToolProfileSet` overloads (for example the
[`vision,robotics` composition](Vision.md#mcp-tools) the BinPickingClient
sample runs), the corresponding `WithOpcUaCoreTools(McpToolProfileSet)`
overload owns and deduplicates `ConnectionTools` across every package.
Each Vision or Robotics package's `McpToolProfileSet` overload never
registers `ConnectionTools` directly, so the composed catalog contains
one Connection surface, not several.

The capture tool classes stay `internal` and are reachable only through their
registration extensions.

### Embedding industrial companions

Each industrial family is a separate
`OPCFoundation.NetStandard.Opc.Ua.Mcp.<Family>` package, so an embedding host
only needs to reference and register the modules it uses:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Opc.Ua.Mcp;

var profiles = McpToolProfileSet.Parse("machinery,isa95");

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

Register filters once. Connection tools are registered once even when multiple
families need them. A model/package dependency does not silently enable its
MCP catalog. Accessors can also be constructed directly with an existing
`OpcUaSessionManager`; they do not own the sessions.

New value projections avoid JSON reflection, but the MCP SDK's existing
tool-registration path still requires JSON reflection. This feature does not
make the complete MCP executable NativeAOT-compatible. The repository's MCP
AOT tests pin that limitation explicitly.

## Security Notes

- Companion file transfers are disabled until an explicit host-owned root is
  configured and are limited to 64 MiB by default. See
  [file transfer policy](#companion-file-transfers).
- Companion commands preserve server permissions and refusals. Discovery never
  requests authority or takes locks; software upload never implicitly installs.
- The `autoAcceptCerts` parameter is for **testing only**. In production, configure proper certificate trust using the OPC UA certificate stores under `%LocalApplicationData%/OPC Foundation/pki/`.
- The server supports simultaneous named OPC UA sessions. Use the intended session identifier for each operation and disconnect that session when finished; connecting to another server does not require closing unrelated sessions.
- Application certificates are automatically created on first use and stored in the local certificate store.
- Logs are written to `%LocalApplicationData%/OPC Foundation/Logs/McpServer.log.txt`.

## Agent-Usability Quality Gate

Contributors can run the same deterministic static and live-probe grading used in CI:

```powershell
./tools/Opc.Ua.Mcp/Test-McpGrade.ps1
```

The script builds the server, checks out `mcpgrade` at the reviewed commit pinned in the script,
grades the `core` and `full` Streamable HTTP catalogs, and probes the full stdio catalog. Reports
are written under `artifacts/mcpgrade` by default.

The repository `.mcpgraderc.json` disables N002 after descriptions have been disambiguated.
`RegisterServer`/`RegisterServer2` are OPC UA standard service names, while
`ReadValue`/`ReadValues` and `GetConfiguration`/`SetConfiguration` are compatibility-sensitive
singular/plural and get/set pairs. These names must not be changed solely to satisfy edit-distance
heuristics.

## Requirements

- .NET 10 SDK or later
- An OPC UA server to connect to
