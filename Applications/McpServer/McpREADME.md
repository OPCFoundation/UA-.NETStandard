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

# HTTP/SSE transport — for remote clients
opcua-mcp --transport sse --port 5100
```

## Tools

43 MCP tools covering all OPC UA Part 4 service sets:

- **Connection**: Connect, Disconnect, GetConnectionStatus
- **Attribute**: Read, Write, HistoryRead, HistoryUpdate
- **View**: Browse, BrowseNext, TranslateBrowsePaths, RegisterNodes, UnregisterNodes, QueryFirst, QueryNext
- **Node Management**: AddNodes, AddReferences, DeleteNodes, DeleteReferences
- **Method**: Call
- **Subscription**: CreateSubscription, ModifySubscription, SetPublishingMode, Publish, Republish, DeleteSubscriptions, TransferSubscriptions
- **MonitoredItem**: CreateMonitoredItems, ModifyMonitoredItems, SetMonitoringMode, SetTriggering, DeleteMonitoredItems
- **Discovery**: FindServers, FindServersOnNetwork, GetEndpoints, RegisterServer, RegisterServer2
- **Convenience**: ReadValue, ReadValues, WriteValue, BrowseAll, CallMethod, ReadNode, Cancel

## Documentation

See the [full documentation](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/Docs/McpServer.md).
