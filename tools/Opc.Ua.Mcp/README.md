# OPC UA MCP Server

An MCP (Model Context Protocol) server that exposes OPC UA Part 4 service calls as MCP tools, enabling LLMs (Claude, Copilot, VS Code, etc.) to interact with OPC UA servers.

## Features

- **64 MCP tools** covering all OPC UA Part 4 service sets (except session management), plus PKI, configuration, NodeSet export, and OPC UA-aware packet capture
- **Both stdio and HTTP/SSE** transports
- **JSON representation** of all OPC UA types for LLM-friendly interactions
- **Session management** via Connect/Disconnect tools

### Tool Inventory

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
| Configuration | GetConfiguration, SetConfiguration | View/modify client settings for current session |
| NodeSet Export | ExportNodeSet, ExportNodeSetPerNamespace | Export address space to NodeSet2 XML |
| Convenience | ReadValue, ReadValues, WriteValue, BrowseAll, CallMethod, ReadNode, Cancel | Simplified operations |
| Packet Capture | list_interfaces, start_capture, stop_capture, list_captures, get_capture, capture_now, list_active_channels, dump_keys, decode_pcap_with_keys, summarize_service_calls, replay_pcap, stop_replay, list_replays | OPC UA-aware packet capture, offline decode, service-call summaries, replay |

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
