# OPC UA Device Integration MCP tools

Embeddable `di_*` tools for device discovery, topology, identification,
functional groups, explicit tag writes, locking, parameter transfers and
software updates. Uses the existing DI client helpers and generated model
proxies; each call resolves its current named session.

```csharp
using Microsoft.Extensions.DependencyInjection;
using Opc.Ua.Mcp;

builder.Services
    .AddOpcUaMcpCore(options => options.TransferRoot = @"D:\opcua-transfers")
    .AddOpcUaMcpDi();

builder.Services.AddMcpServer()
    .WithStdioServerTransport()
    .WithOpcUaMcpFilters()
    .WithOpcUaDiTools(McpToolProfile.Di);
```

Use `di_read_device` to obtain actual `lockNodeId`,
`transferServicesNodeId` and `softwareUpdateNodeId` child identities.
Missing optional facets are reported explicitly. Lock operations preserve
the raw integer return status and do not acquire, renew or release a lock
automatically.

Uploading, preparing, installing, uninstalling, resuming and confirming
software are separate explicit mutations. Uploads use the shared host
`TransferRoot` and byte limit (64 MiB by default); configuring no root
disables uploads. Power-cycle support is read/observe only.

See the repository's `docs/DeviceIntegration.md` MCP section for the
catalogue, bounded observation semantics and software-update workflow.
