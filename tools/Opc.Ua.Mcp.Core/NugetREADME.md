# OPCFoundation.NetStandard.Opc.Ua.Mcp.Core

OPC UA Model Context Protocol (MCP) tools for the Part 4 services, packaged so
they can be embedded in any MCP server host rather than only run as the shipped
`opcua-mcp` tool.

Use this package when an application wants to offer OPC UA tools to an LLM
*and* add tools of its own.

## Tools

Connect and session management, browse and view services, read and write
attributes, method calls, subscriptions and monitored items, node management,
NodeSet2 export, and PKI/certificate administration.

## Usage

```csharp
builder.Services.AddOpcUaMcpCore();

builder.Services.AddMcpServer()
    .WithStdioServerTransport()
    .WithOpcUaMcpFilters()
    .WithOpcUaCoreTools(McpToolProfile.Services)
    .WithTools<MyApplicationTools>();
```

`WithOpcUaMcpFilters` registers the request and schema filters that make tool
errors actionable and tool schemas explicit; call it once per server even when
several OPC UA tool packages are composed.

## Related packages

| Package | Adds |
|---|---|
| `OPCFoundation.NetStandard.Opc.Ua.Mcp.Diagnostics` | packet capture, decode and replay, including key logging |
| `OPCFoundation.NetStandard.Opc.Ua.Mcp.PubSub` | PubSub runtime, actions and discovery |
| `OPCFoundation.NetStandard.Opc.Ua.Mcp.PubSub.Diagnostics` | PubSub packet capture and decode |
| `OPCFoundation.NetStandard.Opc.Ua.Mcp` | the ready-to-run `opcua-mcp` server composing all of the above |

## License

OPC Foundation MIT License 1.00 — <http://opcfoundation.org/License/MIT/1.00/>
