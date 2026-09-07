# OPCFoundation.NetStandard.Opc.Ua.Mcp.PubSub

OPC UA Model Context Protocol (MCP) tools for PubSub, packaged so they can be
embedded in any MCP server host rather than only run as the shipped `opcua-mcp`
tool.

Use this package when an application wants to expose OPC UA PubSub publishers
and subscribers to an LLM alongside tools of its own.

## Tools

Runtime control of publishers and subscribers, PubSub action invocation, and
PubSub discovery.

## Usage

```csharp
builder.Services.AddOpcUaMcpCore();
builder.Services.AddOpcUaMcpPubSub();

builder.Services.AddMcpServer()
    .WithStdioServerTransport()
    .WithOpcUaMcpFilters()
    .WithOpcUaCoreTools(McpToolProfile.PubSub)
    .WithOpcUaPubSubTools(McpToolProfile.PubSub)
    .WithTools<MyApplicationTools>();
```

A profile that does not select PubSub contributes no tools rather than failing,
so the same profile value can be passed to every OPC UA tool package a host
references.

## Related packages

| Package | Adds |
|---|---|
| `OPCFoundation.NetStandard.Opc.Ua.Mcp.Core` | Part 4 service tools, session management, filters (required) |
| `OPCFoundation.NetStandard.Opc.Ua.Mcp.Diagnostics` | packet capture, decode and replay, including key logging |
| `OPCFoundation.NetStandard.Opc.Ua.Mcp.PubSub.Diagnostics` | PubSub packet capture and decode |
| `OPCFoundation.NetStandard.Opc.Ua.Mcp` | the ready-to-run `opcua-mcp` server composing all of the above |

## License

OPC Foundation MIT License 1.00 — <http://opcfoundation.org/License/MIT/1.00/>
