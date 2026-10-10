# OPC UA Scales MCP tools

Embeddable OPC 40200 discovery, weight monitoring, products, recipes and
explicit scale/PackML commands. This is a preview package for .NET 8, 9 and 10.

```csharp
builder.Services.AddOpcUaMcpCore();
builder.Services.AddOpcUaMcpScales();
builder.Services.AddMcpServer()
    .WithStdioServerTransport()
    .WithOpcUaMcpFilters()
    .WithOpcUaScalesTools(McpToolProfile.Scales);
```

The shipped `opcua-mcp` tool includes the catalog with `--profile scales`.
Compose `--profile scales,di` when a product requires a DI lock. Use the actual
`lockNodeId` from the Products facet with the DI tools; scale commands never
acquire or break locks automatically.

Discovery/read lists and observations are bounded. Missing weight capability
is distinct from a valid scale's uninitialized `"NaN"` measurement. Units,
quality, timestamps and server refusals remain visible.

See the [companion MCP guide](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/docs/CompanionMcp.md)
and [Scales guide](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/docs/Scales.md).
