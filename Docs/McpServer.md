# OPC UA MCP Server

The OPC UA MCP Server exposes all OPC UA Part 4 service calls as [Model Context Protocol (MCP)](https://modelcontextprotocol.io) tools. This enables AI assistants — Claude, GitHub Copilot, VS Code Copilot, Cursor, and any MCP-compatible client — to connect to OPC UA servers and interact with industrial automation systems through natural language.

## What It Does

The MCP server wraps the OPC UA .NET Standard client library, translating between JSON-based MCP tool calls and OPC UA binary protocol operations. It provides **43 tools** organized by OPC UA Part 4 service set:

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
| **Configuration** | `GetConfiguration`, `SetConfiguration` | View/modify client settings for current session |
| **NodeSet Export** | `ExportNodeSet`, `ExportNodeSetPerNamespace` | Export address space to NodeSet2 XML |
| **Convenience** | `ReadValue`, `ReadValues`, `WriteValue`, `BrowseAll`, `CallMethod`, `ReadNode`, `Cancel` | Simplified high-level operations |
| **Packet Capture** | `list_interfaces`, `start_capture`, `stop_capture`, `list_captures`, `get_capture`, `capture_now`, `list_active_channels`, `dump_keys`, `decode_pcap_with_keys`, `summarize_service_calls`, `replay_pcap`, `stop_replay`, `list_replays` | OPC UA-aware packet capture, offline decode, service-call summaries, replay |

All OPC UA types (NodeId, DataValue, Variant, StatusCode, etc.) are represented as JSON for LLM-friendly interaction.

The packet-capture tools are described in detail in [Diagnostics](Diagnostics.md#4-packet-capture-dissection-and-replay). For convenience the per-tool surface is:

| Tool | Description | Parameters |
|---|---|---|
| `list_interfaces` | Enumerates NICs available to SharpPcap. | None |
| `start_capture` | Starts a capture session. | `source`; optional interface, filter, endpoint, limits, folder |
| `stop_capture` | Stops an active session and finalizes artifacts. | `sessionId` |
| `list_captures` | Lists capture sessions. | Optional `state` |
| `get_capture` | Returns an artifact or formatted analysis. | `sessionId`, `format`, optional packet/partial controls |
| `capture_now` | Starts, waits, stops, and returns output. | Capture options plus output `format` |
| `list_active_channels` | Lists in-process secure channels with current tokens. | None |
| `dump_keys` | Emits keylog data. | Optional `sessionId`, `format`, `includeExpired` |
| `decode_pcap_with_keys` | Decodes an existing pcap and keylog offline. | `pcapPath`, `keylogPath`, `format`, optional `maxFrames` |
| `summarize_service_calls` | Reports service counts, latency, and errors. | `sessionId` or `pcapPath` + `keylogPath`, optional `top` |
| `replay_pcap` | Replays as a mock server or mock client. | `pcapPath`, `keylogPath`, `mode`, endpoints, `speed` |
| `stop_replay` | Stops an active replay session. | `sessionId` |
| `list_replays` | Lists active and recently-completed replay sessions. | None |

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

### Option 1: Install as a .NET global tool (recommended)

```bash
dotnet tool install --global OPCFoundation.NetStandard.Opc.Ua.Mcp
```

After installation, the `opcua-mcp` command is available globally.

### Option 2: Run from source

```bash
cd Applications/McpServer
dotnet run -c Release
```

### Option 3: Install from local build

```bash
dotnet pack Applications/McpServer/Opc.Ua.Mcp.csproj -c Release
dotnet tool install --global --add-source Applications/McpServer/bin/Release OPCFoundation.NetStandard.Opc.Ua.Mcp
```

## Configuration

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

### HTTP/SSE Transport (for remote clients)

By default, the server uses stdio transport for local tool integration. For remote clients, use the HTTP/SSE transport:

```bash
opcua-mcp --transport sse --port 5100
```

## Usage

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

## Architecture

```
Applications/McpServer/
├── McpServer.csproj                     # .NET 10 project, packaged as dotnet tool
├── Program.cs                           # Entry point, stdio + HTTP/SSE transport
├── OpcUaSessionManager.cs               # OPC UA client session lifecycle
├── McpServer.Config.xml                 # OPC UA client application config
├── .mcp/server.json                     # MCP server manifest for NuGet discovery
├── Tools/
│   ├── ConnectionTools.cs               # GetEndpoints, Connect, Disconnect, GetConnectionStatus
│   ├── AttributeServiceTools.cs         # Read, Write, HistoryRead, HistoryUpdate
│   ├── ViewServiceTools.cs              # Browse, BrowseNext, TranslateBrowsePaths, etc.
│   ├── NodeManagementServiceTools.cs    # AddNodes, AddReferences, DeleteNodes, etc.
│   ├── MethodServiceTools.cs            # Call
│   ├── SubscriptionServiceTools.cs      # CreateSubscription, Publish, etc.
│   ├── MonitoredItemServiceTools.cs     # CreateMonitoredItems, etc.
│   ├── DiscoveryServiceTools.cs         # FindServers, RegisterServer, etc.
│   ├── PkiTools.cs                      # ListCertificates, TrustCertificate, etc.
│   ├── ConfigurationTools.cs            # GetConfiguration, SetConfiguration
│   ├── NodeSetExportTools.cs            # ExportNodeSet, ExportNodeSetPerNamespace
│   └── ConvenienceTools.cs              # ReadValue, BrowseAll, CallMethod, etc.
└── Serialization/
    └── OpcUaJsonHelper.cs               # OPC UA ↔ JSON type conversion
```

## Security Notes

- The `autoAcceptCerts` parameter is for **testing only**. In production, configure proper certificate trust using the OPC UA certificate stores under `%LocalApplicationData%/OPC Foundation/pki/`.
- The server manages a single OPC UA session at a time. Disconnect before connecting to a different server.
- Application certificates are automatically created on first use and stored in the local certificate store.
- Logs are written to `%LocalApplicationData%/OPC Foundation/Logs/McpServer.log.txt`.

## Requirements

- .NET 10 SDK or later
- An OPC UA server to connect to
