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

## Identity and concurrency contract

A logical Resource and concrete Version are distinct even though both use
`ResourceType`. Resource deletion removes its versions and uses `MetaEpoch`;
version deletion uses that version's `Epoch`. Ambiguous legacy flat layouts
remain inspectable but cannot disguise whole-resource deletion.

Methods that support expected epoch/generation arguments require them
explicitly; zero requires an explicit unconditional-operation opt-in. FileType
writes have no atomic epoch argument, so a metadata pre-read does not provide
one. Uploads target concrete versions, not mutable default-version aliases.
Server-side automatic behavior is not overridden.

See the [MCP server guide](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/docs/McpServer.md).
