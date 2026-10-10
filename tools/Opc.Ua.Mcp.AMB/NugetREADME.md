# OPC UA Asset Management Basics MCP tools

Embeddable Model Context Protocol tools over `Opc.Ua.AMB.Client` (OPC 10000-110).
Targets .NET 8, 9 and 10. No server assembly dependency is required.

```csharp
services.AddOpcUaMcpCore();
services.AddOpcUaMcpAmb();
services.AddMcpServer().WithOpcUaAmbTools(McpToolProfile.Amb);
```

The `amb` profile supplies 12 tools and deduplicated connection tools. It can be
composed with other profiles through `McpToolProfileSet`, or selected by `Full`.
Every operation resolves its current named session. Discovery and collection
reads expose live pagination; observations have finite duration and item limits.
Remote asset references are returned without following them.

Asset-ID writes, condition acknowledgement, and documentation-link add, remove
and write operations are explicit tools. They require the server's authorization
and report OPC UA failures as MCP errors. Read and observation tools never issue
these commands. There is no invented AMB history interface.

See [AMB](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/docs/AMB.md)
for the client model, host composition and tool contracts.
