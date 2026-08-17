# OPC UA MCP Server

An MCP (Model Context Protocol) server that exposes OPC UA Part 4 service calls as MCP tools, enabling LLMs (Claude, Copilot, VS Code, etc.) to interact with OPC UA servers.

## Features

- **Profile-based tool catalog** covering all OPC UA Part 4 service sets (except session management), plus PKI, configuration, NodeSet export, PubSub, and OPC UA-aware packet capture. The default `full` profile exposes every tool below; smaller profiles (`core`, `services`, `administration`, `pubsub`, `diagnostics`) expose a bounded subset — see [Tool Profiles](../../docs/McpServer.md#tool-profiles)
- **Both stdio and Streamable HTTP** transports (HTTP is exposed only at `/mcp`; `--transport sse` is a deprecated alias for `--transport http`)
- **JSON representation** of all OPC UA types for LLM-friendly interactions; scalar `Variant` values preserve typed `false` and `0` values rather than treating default values as `null`
- **Session management** via Connect/Disconnect tools
- **Embeddable** — the tools ship as libraries so an application can offer OPC UA tools to an LLM alongside its own, without forking or shelling out; see [Embedding](#embedding-the-tools-in-your-own-server)

### OPC UA sessions and the stateless MCP protocol

The server is built on the ModelContextProtocol 2.x SDK, which follows the 2026-07-28 specification revision
and is **stateless by default**: there is no `initialize` handshake and no `Mcp-Session-Id`, and every request
carries what it needs.

An OPC UA session is a different thing, and the distinction matters. It is a real, long-lived, secured
connection to a server — application state rather than protocol state. `Connect` opens one and returns a
**name**, and later tool calls pass that name back. Passing an explicit identifier is exactly the shape the
stateless model asks for, so the two fit together well.

What it does assume is that the same process handles both calls. `Connect` on one instance and `Read` on
another will not find the session, because the connection lives in the process that opened it. A single server
process — which is what both the stdio and HTTP hosts are — is therefore the supported deployment. Running
several instances behind a load balancer would need either session affinity or a shared connection broker,
and neither is provided here.

### Tool Inventory (`full` profile)

The tables below list every tool available in the default `full` profile. Running with `--profile core|services|administration|pubsub|diagnostics` exposes only the tool classes relevant to that profile — see [Tool Profiles](../../docs/McpServer.md#tool-profiles) for the mapping.

| Service Set | Tools | Description |
|---|---|---|
| Connection | GetEndpoints, Connect, Disconnect, GetConnectionStatus | Endpoint discovery and session lifecycle |
| Attribute | Read, Write, HistoryRead, HistoryUpdate | Read/write node attributes and historical data |
| View | Browse, BrowseNext, TranslateBrowsePaths, RegisterNodes, UnregisterNodes, QueryFirst, QueryNext | Navigate and query the address space |
| Node Management | AddNodes, AddReferences, DeleteNodes, DeleteReferences | Modify address space |
| Method | Call | Invoke methods |
| Subscription | CreateSubscription, ModifySubscription, SetPublishingMode, Publish, Republish, DeleteSubscriptions, TransferSubscriptions | Notification subscriptions |
| MonitoredItem | CreateMonitoredItems, ModifyMonitoredItems, SetMonitoringMode, SetTriggering, DeleteMonitoredItems | Data change monitoring |
| Discovery | FindServers, FindServersOnNetwork, RegisterServer, RegisterServer2 | Server discovery and registration |
| PKI Management | ListCertificates, TrustCertificate, RemoveCertificate, GetPkiStorePaths | Manage certificate trust lists |
| Configuration | GetConfiguration, SetTransportConfiguration, SetClientConfiguration, SetSecurityConfiguration, SetConfiguration | View/modify in-memory client settings; SetConfiguration is the full-profile compatibility tool |
| NodeSet Export | ExportNodeSet, ExportNodeSetPerNamespace | Export address space to NodeSet2 XML |
| Convenience | ReadValue, ReadValues, WriteValue, BrowseAll, CallMethod, ReadNode, Cancel | Simplified operations |
| Packet Capture | list_interfaces, start_capture, stop_capture, list_captures, get_capture, capture_now, list_active_channels, dump_keys, decode_pcap_with_keys, summarize_service_calls, replay_pcap, stop_replay, list_replays | OPC UA-aware packet capture, offline decode, service-call summaries, replay |

## Embedding the tools in your own server

The tools live in libraries, so an application that wants OPC UA tools *and* its
own application-level tools composes them instead of running this executable:

```csharp
builder.Services.AddOpcUaMcpCore();

builder.Services.AddMcpServer()
    .WithStdioServerTransport()
    .WithOpcUaMcpFilters()
    .WithOpcUaCoreTools(McpToolProfile.Services)
    .WithTools<MyApplicationTools>();
```

| Package | Tools |
|---|---|
| `OPCFoundation.NetStandard.Opc.Ua.Mcp.Core` | Part 4 services, connection, configuration, PKI, NodeSet export |
| `OPCFoundation.NetStandard.Opc.Ua.Mcp.PubSub` | PubSub runtime, actions, discovery |
| `OPCFoundation.NetStandard.Opc.Ua.Mcp.Diagnostics` | UA-TCP capture, decode, replay |
| `OPCFoundation.NetStandard.Opc.Ua.Mcp.PubSub.Diagnostics` | PubSub capture, decode |
| `OPCFoundation.NetStandard.Opc.Ua.Mcp` | this ready-to-run `opcua-mcp` tool |

See [Architecture](../../docs/McpServer.md#architecture) for the full picture.

## Documentation

See the [full documentation](../../docs/McpServer.md) and [NuGet readme](McpREADME.md).

### Claude Desktop Configuration

Add to your `claude_desktop_config.json`:

```json
{
  "mcpServers": {
    "opcua": {
      "command": "dotnet",
      "args": ["run", "--project", "/path/to/tools/Opc.Ua.Mcp/Opc.Ua.Mcp.csproj"]
    }
  }
}
```

### VS Code Configuration

Add to `.vscode/mcp.json`:

```json
{
  "servers": {
    "opcua": {
      "command": "dotnet",
      "args": ["run", "--project", "/path/to/tools/Opc.Ua.Mcp/Opc.Ua.Mcp.csproj"]
    }
  }
}
```

## Example Tool Calls

### Connect to a server

``` text
Tool: Connect
Arguments:
  endpointUrl: "opc.tcp://localhost:62541/Quickstarts/ReferenceServer"
  useSecurity: true
  autoAcceptCerts: true
```

### Browse the Objects folder

``` text
Tool: Browse
Arguments:
  nodeId: "i=85"
```

### Read a variable value

``` text
Tool: ReadValue
Arguments:
  nodeId: "i=2258"
```

### Write a value

``` text
Tool: WriteValue
Arguments:
  nodeId: "ns=2;s=MyVariable"
  value: "42"
  dataType: "Int32"
```

### Call a method

``` text
Tool: CallMethod
Arguments:
  objectId: "ns=2;s=MyObject"
  methodId: "ns=2;s=MyMethod"
  inputArguments: ["arg1", "arg2"]
```

## OPC UA Client Configuration

The server uses `McpServer.Config.xml` for OPC UA client configuration, including:

- Application certificate settings
- Trust list management
- Transport quotas
- Operation limits

Certificates are stored under `%LocalApplicationData%/OPC Foundation/pki/`.
