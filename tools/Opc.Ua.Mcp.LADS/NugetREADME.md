# OPC UA LADS MCP tools

`OPCFoundation.NetStandard.Opc.Ua.Mcp.LADS` adds OPC 30500-1 device discovery,
typed snapshots, state-machine commands, program templates and starts, results,
result-file downloads, and maintenance tasks to an MCP server. Tools use the
typed `Opc.Ua.LADS.Client` API over the host's current named session.

## Embedding

Register the shared OPC UA services, the LADS tool services, and the bounded
LADS profile:

```csharp
services.AddOpcUaMcpCore()
    .AddOpcUaMcpLads();

services.AddMcpServer()
    .WithOpcUaLadsTools(McpToolProfile.Lads);
```

For composed catalogs, pass the same `McpToolProfileSet` to the core and LADS
registration methods. This keeps connection tools deduplicated.

## Catalog

The catalog discovers devices; reads device, unit, function, program-template,
result, result-file and maintenance data; sends explicit state-machine and
maintenance commands; starts programs from published templates; and downloads
selected result files under the host's configured transfer root.

Commands request a server-side action; server authorization and state-machine
preconditions apply, and acceptance does not mean physical completion.
Downloads reject existing destinations and obey the host transfer limits.
See the [LADS guide](../../docs/LADS.md#mcp-tools) and the
[MCP server reference](../../docs/McpServer.md#lads-tools).
