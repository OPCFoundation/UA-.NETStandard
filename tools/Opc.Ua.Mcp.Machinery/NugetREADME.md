# OPC UA Machinery MCP tools

Embed OPC 40001 Machinery tools with `AddOpcUaMcpMachinery()` and
`WithOpcUaMachineryTools(McpToolProfile.Machinery)`, after registering MCP Core.
Discovery, monitoring, job endpoint lookup, results and bounded streaming downloads
use the typed Machinery client. Zero-point adjustment is an explicit consequential tool.
There are no invented machine state or operating-mode setters.

Compose `machinery,isa95` for explicit ISA-95 job commands against the actual endpoint
IDs returned by Machinery. Configure the Core download root before downloading.

## Composed registration

```csharp
var profiles = McpToolProfileSet.Parse("machinery,isa95");
services.AddOpcUaMcpCore(new OpcUaMcpOptions
{
    TransferRoot = configuredResultDirectory,
    MaxTransferBytes = 16 * 1024 * 1024
});
services.AddOpcUaMcpMachinery();
services.AddOpcUaMcpIsa95();
services.AddMcpServer()
    .WithOpcUaMcpFilters()
    .WithOpcUaCoreTools(profiles)
    .WithOpcUaMachineryTools(profiles)
    .WithOpcUaIsa95Tools(profiles);
```

The 19 `machinery_*` tools accept optional `sessionName`, resolving the current
session per call rather than caching a proxy, endpoint or subscription.
Registration of Machinery alone does not add ISA-95 controls.

## Arguments and results

`machinery_read` has finite `Identification`, `Health`, `OperationCounters`,
`ItemState` and `OperationMode` facets. Live list pages contain at most 500 items
and report `nextOffset`, not a persistent snapshot cursor.

`machinery_list_results` instead reports `limitReached`. Narrow the exact
job/part selectors and inclusive creation-time bounds when it is true. Bounds
must include `Z` or an explicit UTC offset; timezone-less timestamps are refused.
For example, `2026-01-01T00:00:00Z` and `2026-01-01T02:00:00+02:00` denote
the same instant.

Observations last at most 30 seconds and collect at most 500 items. They borrow
the selected managed session's streaming subscription and release only their
enumerator. Result query/read handles are released before returning.

Downloads require `TransferRoot`, obey `MaxTransferBytes`, never overwrite an
existing destination and remove incomplete artifacts. They are not annotated
read-only because they create a local file. `machinery_get_job_endpoints` returns
only actual role endpoints; missing roles are not substituted, and Machinery
defines no response receiver. ISA-95 command bitmaps are successful only at `1`.

See the [tool catalogue](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/docs/McpServer.md#machinery-tools)
and [Machinery client guide](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/docs/Machinery.md#client).
