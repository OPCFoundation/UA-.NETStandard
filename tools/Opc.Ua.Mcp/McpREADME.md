# OPC UA MCP Server

A [Model Context Protocol (MCP)](https://modelcontextprotocol.io) server that exposes OPC UA Part 4 service calls as MCP tools, enabling AI assistants (Claude, GitHub Copilot, VS Code, etc.) to interact with OPC UA servers.

## Run without installing (.NET 10 SDK)

```bash
dotnet tool exec OPCFoundation.NetStandard.Opc.Ua.Mcp

# Short form
dnx OPCFoundation.NetStandard.Opc.Ua.Mcp
```

## Install globally

```bash
dotnet tool install --global OPCFoundation.NetStandard.Opc.Ua.Mcp
```

The tool includes Robotics, Vision and the AMB, Machinery, Scales, Pumps,
Device Integration, ISA-95 and Positioning companion profiles. Profiles compose,
for example `--profile machinery,isa95` or `--profile scales,di`.

## Usage

The examples below assume a global installation. For run-on-demand use,
replace `opcua-mcp` with `dnx OPCFoundation.NetStandard.Opc.Ua.Mcp --`.

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
`--profile <name>`. Run `opcua-mcp --help` for the current profile names.
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
- **Industrial companions**: asset management, machinery, weighing, pumps,
  device integration, ISA-95 jobs and relative/global positioning. See the
  [companion guide](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/docs/CompanionMcp.md).

Companion calls preserve server failures, use bounded lists and observations,
and never acquire locks or retry commands implicitly. Package upload and result
download require `OPCUA_MCP_TRANSFER_ROOT`; the default streaming byte limit is
64 MiB (`OPCUA_MCP_MAX_TRANSFER_BYTES`). Paths cannot escape the configured root.

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
| `OPCFoundation.NetStandard.Opc.Ua.Mcp.AMB` | Asset management |
| `OPCFoundation.NetStandard.Opc.Ua.Mcp.Machinery` | Machine monitoring, process values, energy and results |
| `OPCFoundation.NetStandard.Opc.Ua.Mcp.Scales` | Weighing, products and recipes |
| `OPCFoundation.NetStandard.Opc.Ua.Mcp.Pumps` | Pump values, supervision and maintenance |
| `OPCFoundation.NetStandard.Opc.Ua.Mcp.Di` | Device integration, locking and updates |
| `OPCFoundation.NetStandard.Opc.Ua.Mcp.ISA95` | Common objects and V1/V2 job control |
| `OPCFoundation.NetStandard.Opc.Ua.Mcp.Positioning` | Spatial frames and coordinate conversion |

The Robotics, Vision and seven industrial companion libraries are preview
packages; the other libraries are stable.

## Documentation

See the [full documentation](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/docs/McpServer.md).
