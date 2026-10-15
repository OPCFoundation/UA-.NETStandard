# MachineTool and wireless-peripheral MCP workflows

Typed, bounded MCP tools for released OPC 40501-1 MachineTool 1.02.0 and
OPC 40505 WMTP 1.0.0. WMTP is bundled in this package and the `machinetool`
profile, not a separate package/profile. WMTP-only endpoints need no fictitious
MachineTool instance.

## Embedding

```csharp
using Microsoft.Extensions.DependencyInjection;
using Opc.Ua.Mcp;

services.AddOpcUaMcpCore().AddOpcUaMcpMachineTool();
services.AddMcpServer()
    .WithStdioServerTransport()
    .WithOpcUaMcpFilters()
    .WithOpcUaMachineToolTools(McpToolProfile.MachineTool);
```

Use `McpToolProfileSet` to compose catalogs. Common connection tools are
deduplicated. The package borrows host-managed named sessions; supply
`sessionName` when the selection is ambiguous. It depends on the typed client,
not server runtime, and follows the normal root package-version policy.

## Passive machine workflows

| Tool | Purpose |
| --- | --- |
| `machinetool_list_devices` | Actual MachineTool/WMTP identities and vendor subtypes |
| `machinetool_read_machine` | Monitoring, declared capabilities, real job/file root identities |
| `machinetool_read_equipment` | Tools, multi-tools, locations, locks and independent life values |
| `machinetool_read_production` | Retained jobs/programs/part sets/parts, states and counters |
| `machinetool_read_prognoses` | Native forecast kinds and actual affected-entity references |
| `machinetool_observe_values` | Bounded selection of native variables |
| `machinetool_observe_machine` | Bounded machine monitoring |
| `machinetool_observe_events` | Bounded events or explicit retained-condition refresh |

Raw DataValues preserve type, quality and source/server timestamps. Missing
optional nodes do not become Good zero values. Inventory/report bounds fail
rather than silently truncating. Observation limits, overflow and refresh
completion are explicit. Each call owns its subscription, not the host session.

## Modern jobs and remote files

`machinetool_query_jobs` reads actual orders, response-by-ID or responses by a
complete standard state path. `machinetool_control_job` supports Store,
StoreAndStart, Start, Stop, Abort, Pause, Resume, Clear, Cancel, Update and
RevokeStart through generated ISA95 V2 proxies. Missing AddIns are
BadNotSupported, not empty catalogs.

Store/StoreAndStart/Update accept native UA JSON job orders; other verbs use
`jobOrderId`. Example:

```json
{
  "JobOrderID": "Job-1",
  "EncodingMask": 18,
  "WorkMasterID": [{"ID": "WM-1"}],
  "Priority": 0
}
```

The stack decoder preserves optional masks, arrays and Variants. Whole job
input is limited to 65536 UTF-8 bytes, depth 32, 4096 values and 1024 items per
array; comments allow eight LocalizedText values. ReturnStatus is the exact
UInt64 bitmap with success bit **1**, not UA Good=0. Acceptance does not assert
physical execution.

`machinetool_list_files`, `machinetool_read_file` and
`machinetool_change_file` target the actual FileSystem or qualified WorkMasters
root. Paths are remote UaPaths, never local paths/URLs or cross-root handles.
Immediate lists cap at 128 entries. Reads cap raw data at 524288 bytes and
include base64/JSON expansion in the whole response limit.

Write replaces an existing file only; empty base64 clears it. CreateFile makes
an empty file; CreateDirectory creates one leaf. Delete requires explicit
recursive selection for nonempty directories. Move/Copy require a new
destination with an existing parent and never overwrite. Mutations require
`confirm=true`, authoritative server/provider permission and a pre-budgeted
receipt. No implicit retry, compensation, persistent handle or hidden upload
echo is used.

Related file arguments are grouped in the native `options` object:
`destination`, `contentBase64`, `maximumBytes` (1-524288, default 65536) and
`recursive` (default false). Other tool parameters remain top-level.
Unknown/duplicate properties and invalid primitive types are rejected before
I/O. For example, a consented write to an existing file uses:

```json
{
  "machineNodeId": "ns=7;s=Mill",
  "operation": "Write",
  "path": "program.bin",
  "options": {"contentBase64": "RE1P", "maximumBytes": 1024},
  "confirm": true
}
```

## Wireless peripherals

| Tool | Purpose |
| --- | --- |
| `wmtp_read_peripheral` | Device metadata and optional measurement inventory |
| `wmtp_count_records` | Work/service all-record or acquisition-time count |
| `wmtp_report_records` | Complete all/time/index/first/last selection |
| `wmtp_configure_measurement` | Checked trigger or typed delta setting |
| `wmtp_configure_peripheral` | Calibration Boolean or UTC/time-zone setting |
| `wmtp_delete_records` | Explicit all/index/time deletion |
| `wmtp_abort_operation` | Explicit authorized peripheral-wide abort |
| `wmtp_transfer` | Bounded firmware/licence byte acceptance |

Use returned measurement `valueNodeId` with `machinetool_observe_values`;
object `nodeId` is the separate configuration target. Work/service stores and
instances remain isolated. Reports copy, never consume. All/time/index bounds
are inclusive and CombinedReportAll either returns everything or fails.

Native query examples:

```json
{"selection":"index","fromIndex":1,"toIndex":20}
```

```json
{"selection":"time","fromTime":"2026-09-01T00:00:00Z","toTime":"2026-09-01T01:00:00Z"}
```

Trigger takes whole milliseconds representable by UInt32. Delta uses an
explicit numeric union, for example `{"type":"UInt64","value":"9007199254740992"}`.
Integral values do not round through Double. The server rejects an
unrepresentable published delta and enforces trigger/delta not both zero.
Unsupported Decimal is rejected rather than coerced.

Time setting uses the actual TimeZoneDataType, not host local time:

```json
{"utcTime":"2026-09-01T00:00:00Z","timeZone":{"offset":120,"daylightSavingInOffset":true}}
```

Mutating tools require `confirm=true` and remain subject to server refusal.
Abort is never inferred from local cancellation or timeout and cannot target
another caller without server authorization. Transfer accepts inline base64
up to 1 MiB decoded, not paths/URLs; call-owned copies are cleared.
`acceptedByProvider=true` does not imply installation or licence activation.

Wireless deadlines are 1-60 seconds, default 30. Replies default to 128 KiB
and cap at 1 MiB; measurement inventories cap at 256 and reports at 4096.
Every parameter, union, enum, permission hint and bound is described in the
live tool schema. These adapters do not implement CNC, Bluetooth or firmware
installation.

See the [developer guide](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/docs/MachineTool.md)
and [MCP reference](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/docs/McpServer.md).
