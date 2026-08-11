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

## Trimming and Native AOT

The assembly is marked `IsAotCompatible` on `net10.0`: its own IL is free of trim
and AOT warnings, and tool results are written with `Utf8JsonWriter` rather than
the reflection-based `JsonSerializer`.

That is not yet the same as working in an ahead-of-time published host. Registering
the tools still requires JSON reflection, because the MCP SDK builds each tool's
input schema by asking `JsonSchemaExporter` for a `JsonTypeInfo` of every parameter
type; without a source-generated context covering the tool signatures this throws
`NotSupportedException`. `Opc.Ua.Aot.Tests.McpAotTests` pins that limitation so it
is stated rather than discovered at run time.

`OPCFoundation.NetStandard.Opc.Ua.Mcp.Diagnostics` is deliberately *not* marked
AOT-compatible: it depends on SharpPcap and on reflective service-call dissection.

## Related packages

| Package | Adds |
|---|---|
| `OPCFoundation.NetStandard.Opc.Ua.Mcp.Diagnostics` | packet capture, decode and replay, including key logging |
| `OPCFoundation.NetStandard.Opc.Ua.Mcp.PubSub` | PubSub runtime, actions and discovery |
| `OPCFoundation.NetStandard.Opc.Ua.Mcp.PubSub.Diagnostics` | PubSub packet capture and decode |
| `OPCFoundation.NetStandard.Opc.Ua.Mcp` | the ready-to-run `opcua-mcp` server composing all of the above |

## License

OPC Foundation MIT License 1.00 — <http://opcfoundation.org/License/MIT/1.00/>
