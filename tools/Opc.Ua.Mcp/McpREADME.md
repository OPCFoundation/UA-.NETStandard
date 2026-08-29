# OPC UA MCP Server

A [Model Context Protocol (MCP)](https://modelcontextprotocol.io) server that exposes OPC UA Part 4 service calls as MCP tools, enabling AI assistants (Claude, GitHub Copilot, VS Code, etc.) to interact with OPC UA servers.

## Install

```bash
dotnet tool install --global OPCFoundation.NetStandard.Opc.Ua.Mcp
```

## Usage

```bash
# stdio transport (default) — for Claude Desktop, VS Code, Copilot
opcua-mcp

# Streamable HTTP transport (exposed only at /mcp) — for remote clients
opcua-mcp --transport http --port 5100

# --transport sse is a deprecated alias for --transport http
```

## Tools

The server exposes tools through a **tool profile** — a bounded, named catalog
selected with
`--profile core|services|administration|pubsub|diagnostics|robotics|vision|full`.
Profiles can be composed, for example `--profile vision,robotics`. `full` is the
default; the other profiles expose smaller focused subsets. See the
[full documentation](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/docs/McpServer.md#tool-profiles)
for the profile-to-tool mapping.

Tools in the `full` profile cover all OPC UA Part 4 service sets:

- **Connection**: Connect, Disconnect, GetConnectionStatus
- **Attribute**: Read, Write, HistoryRead, HistoryUpdate
- **View**: Browse, BrowseNext, TranslateBrowsePaths, RegisterNodes, UnregisterNodes, QueryFirst, QueryNext
- **Node Management**: AddNodes, AddReferences, DeleteNodes, DeleteReferences
- **Method**: Call
- **Subscription**: CreateSubscription, ModifySubscription, SetPublishingMode, Publish, Republish, DeleteSubscriptions, TransferSubscriptions
- **MonitoredItem**: CreateMonitoredItems, ModifyMonitoredItems, SetMonitoringMode, SetTriggering, DeleteMonitoredItems
- **Discovery**: FindServers, FindServersOnNetwork, GetEndpoints, RegisterServer, RegisterServer2
- **Convenience**: ReadValue, ReadValues, WriteValue, BrowseAll, CallMethod, ReadNode, Cancel
- **Robotics**: typed Robot Intent control and missions, paged monitoring,
  bounded operation/mission waits, and same-session `robotics_vision_pick`
- **Vision**: image capture, structured one-shot inference, result monitoring,
  feedback and frame-graph composition

## Embedding

The tools also ship as libraries, so an application can offer OPC UA tools to an
LLM alongside its own without forking this server:

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
| `OPCFoundation.NetStandard.Opc.Ua.Mcp.Robotics` | Robot Intent control, missions, waits and Vision-guided Pick |
| `OPCFoundation.NetStandard.Opc.Ua.Mcp.Vision` | Vision discovery, seeing, inference, feedback and geometry |

## Documentation

See the [full documentation](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/docs/McpServer.md).
