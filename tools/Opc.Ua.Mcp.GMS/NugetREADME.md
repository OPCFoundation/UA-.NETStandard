# OPC UA GMS MCP tools

Six bounded workflows for OPC 40210 Geometric Measurement Systems, using
`GmsClient`, `GmsResultClient`, generated proxies and the shared session,
subscription and result-file services. This package has no server dependency.
It follows the normal repository version policy, including `.Debug` packages.

The reference edition is 1.0.1; the released namespace model is 1.0.0.

## Embedding

```csharp
services.AddOpcUaMcpCore();
services.AddOpcUaMcpGms();
services.AddMcpServer()
    .WithStdioServerTransport()
    .WithOpcUaMcpFilters()
    .WithOpcUaGmsTools(McpToolProfile.Gms);
```

Use `WithOpcUaGmsTools(McpToolProfileSet.Parse("core,gms"))` for composition.
Both overloads include GMS for `Full` and exclude it for other selections.
Repeated registration deduplicates both GMS and shared connection tools.
Existing named sessions are borrowed from `OpcUaSessionManager`; disposing a
call's facade never closes the host's session or unrelated subscriptions.
Configure `GmsClientOptions` and `TimeProvider` through DI when tighter native
read bounds or an injected observation clock are needed.

## Catalog

| Tool | Workflow |
| --- | --- |
| `gms_list_systems` | Actual GMSType/vendor-subtype inventory, not namespace-only detection. |
| `gms_read_system` | `System`, `Equipment`, `Calibration`, `Production`, or per-instance `Capabilities`. |
| `gms_read_results` | `Latest`, `ById`, bounded `Query`, raw `Published` slots/UsedTools, or `Corrections`. |
| `gms_download_result` | Bounded inline base64 via standard temporary-file transfer; no host paths. |
| `gms_observe` | `Data`, `Events`, initial retained `Conditions`, or `IntermediateResults`. |
| `gms_acknowledge_results` | Standard result acknowledgement with explicit `confirmAcknowledgement=true`. |

Every tool accepts `sessionName`; omission is valid only when exactly one
session is connected. Selectors are exact and case-sensitive. Descriptions and
schemas specify hard limits: 1–100 inventory/results/notifications, 1–60
seconds, 1–524288 download bytes, and 1024–1048576 complete UTF-8 reply bytes
(default 128 KiB). Host native-read limits may be tighter. A response that
exceeds its count or byte budget fails, rather than silently truncating.
Query asks the server for one extra identifier to detect count overflow.
Its native `query` object groups `filterField`, `filterValue`, and `orderBy`.
Use a `ResultId` or `JobId` string equality filter, or an `IsPartial` Boolean.
Omitting `query` (or using `{}`) means no filter and ascending `ResultId` order;
`orderBy: "None"` preserves the server's order. Nondefault query selections
require `operation: "Query"`. Unknown or duplicate object properties, invalid
selector names, and incorrectly typed values are rejected before session use.
The filter remains a standard Machinery `ContentFilter`, not another protocol.

`gms_read_results` has seven top-level parameters: `systemNodeId`, `operation`,
`resultId`, `maximumResults`, `query`, `maximumResponseBytes`, and `sessionName`.
Move the former top-level `filterField`, `filterValue`, and `orderBy` arguments
inside `query`; they are no longer top-level parameters.

Example MCP arguments:

```json
{
  "systemNodeId": "ns=12;s=InspectionCell",
  "operation": "Query",
  "query": {
    "filterField": "JobId",
    "filterValue": "J-1",
    "orderBy": "ResultId"
  },
  "maximumResults": 20,
  "sessionName": "metrology"
}
```

For intermediate results, explicitly select instance-qualified characteristic
placeholders; their namespace need not be the GMS namespace:

```json
{
  "systemNodeId": "ns=12;s=InspectionCell",
  "kind": "IntermediateResults",
  "characteristics": [{"name": "Bore", "namespaceIndex": 12}],
  "durationSeconds": 5,
  "maximumNotifications": 10
}
```

Native values use the stack's JSON encoder, preserving wire numeric types,
structures, status and source/server timestamps. A missing optional variable,
folder or property is distinguished from a present bad/uncertain sample.
Method and event values have no invented per-field acquisition quality.
Published samples retain their original `DataValue`; correction measurements
retain their independent engineering-unit samples. Native catalogs preserve
Number scalar/array representation, and qualification retains its actual
remaining-hours countdown, including negative values after expiry.

## Side effects and lifetime

Passive reads never acknowledge results/conditions, confirm alarms, start
jobs or measurements, fetch calibration-certificate URIs, change equipment,
or apply corrections. There are no invented GMS start/acquire/actuation verbs.
Acknowledgement is a separate, explicitly authorized standard result operation;
per-result domain errors remain distinct from UA service failures, and no
automatic retry is performed.

Retained conditions use standard `ConditionRefresh` and report
`refreshCompleted`; an incomplete or lossy refresh is an MCP error.
Intermediate observation awaits real monitored-item creation and its status:
setup failure is not an empty successful window. Every observation releases
only its own subscription/items on completion, error or cancellation.
Metadata and temporary-file handle ownership stays with the existing clients.

MCP 2.2's local client cancellation token does not necessarily send a protocol
cancel notification. The adapter honors its server-side cancellation token;
clients should send the standard MCP `notifications/cancelled` notification
when immediate server cancellation is needed. The bounded window still
terminates naturally. Cleanup may continue briefly under the typed client's
independent cleanup token after the user-facing window expires.

## Conformance boundary

Capabilities describe actual selected surfaces, not authorization or external
certification. Native job/part monitoring and inherited statistics are exposed,
but the complete GMS Production facet is not claimed: the released specification
requires `GMS_ProductionStatisticsType` without defining its type or NodeId.
This package does not invent that missing definition.
