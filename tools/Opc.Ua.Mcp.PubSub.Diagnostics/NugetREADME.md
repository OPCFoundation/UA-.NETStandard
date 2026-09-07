# OPCFoundation.NetStandard.Opc.Ua.Mcp.PubSub.Diagnostics

OPC UA Model Context Protocol (MCP) tools for PubSub protocol diagnostics,
packaged so they can be embedded in any MCP server host rather than only run as
the shipped `opcua-mcp` tool.

Use this package when an application wants to let an LLM capture and inspect
PubSub network traffic alongside tools of its own.

## Tools

PubSub packet capture to pcapng, and decode of captured PubSub traffic
including key-log handling.

## Security

The decode tool loads PubSub key material. It is **disabled by default** and is
registered only when a host opts in, using the same
`Pcap:EnableDiagnosticsTools` / `OPCUA_PCAP_ENABLE_DIAGNOSTICS` gate as
`OPCFoundation.NetStandard.Opc.Ua.Mcp.Diagnostics`.

## Usage

```csharp
builder.Services.AddOpcUaMcpCore();
builder.Services.AddOpcUaMcpDiagnostics();       // shared capture services
builder.Services.AddOpcUaMcpPubSubDiagnostics();

builder.Services.AddMcpServer()
    .WithStdioServerTransport()
    .WithOpcUaMcpFilters()
    .WithOpcUaCoreTools(McpToolProfile.PubSub)
    .WithOpcUaPubSubDiagnosticsTools(McpToolProfile.PubSub, diagnosticsEnabled)
    .WithTools<MyApplicationTools>();
```

A profile that selects neither PubSub nor diagnostics contributes no tools
rather than failing, so the same profile value can be passed to every OPC UA
tool package a host references.

## Related packages

| Package | Adds |
|---|---|
| `OPCFoundation.NetStandard.Opc.Ua.Mcp.Core` | Part 4 service tools, session management, filters (required) |
| `OPCFoundation.NetStandard.Opc.Ua.Mcp.Diagnostics` | UA-TCP packet capture, decode and replay (supplies the shared capture services) |
| `OPCFoundation.NetStandard.Opc.Ua.Mcp.PubSub` | PubSub runtime, actions and discovery |
| `OPCFoundation.NetStandard.Opc.Ua.Mcp` | the ready-to-run `opcua-mcp` server composing all of the above |

## License

OPC Foundation MIT License 1.00 — <http://opcfoundation.org/License/MIT/1.00/>
