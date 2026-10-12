# OPC UA xRegistry MCP tools

Embeddable, domain-neutral tools for OPC UA registry groups, logical resources,
versions, labels and streamed document transfers. The information model is a
draft; its maturity is independent of the package release channel.

```csharp
services.AddOpcUaMcpCore(options).AddOpcUaMcpXRegistry();
services.AddMcpServer().WithOpcUaCoreTools(McpToolProfile.XRegistry)
    .WithOpcUaXRegistryTools(McpToolProfile.XRegistry);
```

Use `--profile xregistry`, or compose `core,wot,xregistry,wot-documents` in the
installed MCP host. Mutations require an encrypted session and explicit
concurrency tokens. Files must remain below the host's configured transfer root.
Tools do not follow federation links, create extra sessions, or automatically
refresh or promote uploaded documents.

See the [MCP server guide](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/docs/McpServer.md).
