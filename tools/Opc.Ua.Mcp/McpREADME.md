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

The server exposes tools through a **tool profile** — a bounded, named catalog selected with `--profile core|services|administration|pubsub|diagnostics|full`. `full` is the default and currently registers every tool listed below; `core` and the other profiles expose a smaller, focused subset. See the [full documentation](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/docs/McpServer.md#tool-profiles) for the profile-to-tool mapping.

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

## Documentation

See the [full documentation](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/docs/McpServer.md).
