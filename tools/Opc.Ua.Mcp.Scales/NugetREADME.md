# OPC UA Scales MCP tools

Embeddable OPC 40200 discovery, weight monitoring, products, recipes and
explicit scale/PackML commands for .NET 8, 9 and 10. The package follows the
stack's root release channel and is not preview-only.

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

## Arguments and results

Use a NodeId returned by `scales_list`:

```json
{
  "nodeId": "ns=2;s=Scale1",
  "facet": "CurrentWeight",
  "sessionName": "plant"
}
```

`scales_read` reports `available=false` for an object without a weight item.
A published but uninitialized measurement remains available with `"NaN"`,
not zero. Standard scale, vehicle and recipe commands use finite enum
selectors; arbitrary vendor method names are not accepted.

`scales_add_recipe_element` takes `input.previousElements` as a JSON array of
NodeId strings, bounded to 1..500 entries, not the CLR `ArrayOf` representation.
Creating a recipe or element does not start it.

See the [companion MCP guide](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/docs/McpServer.md#industrial-companion-tools)
and [Scales guide](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/docs/Scales.md).
