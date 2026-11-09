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

## Arguments, statuses and bounds

All tools take optional `sessionName`; omission requires exactly one active
session. Constructors use the shared `OpcUaSessionManager` and resolve a fresh
model client per call. Both single-profile and `McpToolProfileSet` registration
deduplicate connection tools. Compose with `scales` to use the Products facet's
actual DI lock.

Discovery/topology use `offset` and `maxResults` (1..500) over live enumerations.
Missing optional facets report `supported: false`, not invented NodeIds or
versions. `di_read_device` provides a summary; `di_read_property` preserves
localization, UA type, quality and timestamps. Failed reads remain errors.

Lock tools take the **Lock child NodeId**, not the owning device. Use the same
named session throughout lock ownership. `returnStatus` is the device's signed
integer: zero is success; every nonzero value, including vendor codes, sets
`error: true` and an MCP error. A Good UA Call response alone is insufficient.
`di_release_lock` never falls back to BreakLock.

Transfer initiation returns `transferId`, not completion. Fetch with
`sequenceNumber: 0`, then follow `nextSequenceNumber` until `complete: true`.
Each invocation fetches one chunk of at most 500 results without a retained
server cursor or unbounded poller. Unknown payloads, excessive responses and
unusable sequences fail explicitly; errors retain raw status and diagnostics.

Package installation input contains `manufacturerUri`, `softwareRevision`,
the JSON string array `patchIdentifiers`, and `hashBase64`. File installation
takes a nonempty JSON string array `fileNodeIds` of actual server File NodeIds,
not host paths. Both arrays are bounded to 500.

Software observations accept 1..30,000 ms and at most 500 snapshots. `stoppedBy`
distinguishes completion, duration and item limits. External cancellation
propagates; absent state machines are errors. State/transition NodeIds,
localized names, timestamps, quality and nested states are preserved.

`methodCompleted: true` describes the method response, not completion of an
asynchronous device action. Upload commits bytes without invoking Installation
or Confirmation, but a server's DirectLoading profile may apply software at
commit; uploads are therefore destructive. Preparation, upload, installation,
resume, uninstall and confirmation are never chained implicitly.

See the [tool catalogue](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/docs/McpServer.md#device-integration-tools)
for behavior and the [DI client guide](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/docs/DeviceIntegration.md)
for the underlying SDK.
