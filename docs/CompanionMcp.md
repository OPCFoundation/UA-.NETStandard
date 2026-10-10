# Industrial companion MCP tools

The MCP server exposes the stack's typed companion clients as task-oriented
tools. It does not create another OPC UA connection or reimplement the
models. Tools resolve the current named session and call the existing
client or source-generated ObjectType proxy.

## Select a catalog

Use the installed `opcua-mcp` command, or run it without installing:

```powershell
dnx OPCFoundation.NetStandard.Opc.Ua.Mcp -- --profile amb
dnx OPCFoundation.NetStandard.Opc.Ua.Mcp -- --profile machinery,isa95
dnx OPCFoundation.NetStandard.Opc.Ua.Mcp -- --profile scales,di
```

| Profile | Scope | Model guide |
| --- | --- | --- |
| `amb` | Asset discovery, identification, health, maintenance, documentation links, locations and relationships | [AMB](AMB.md) |
| `machinery` | Machines and building blocks, state, counters, process values, energy, job information and results | [Machinery](Machinery.md) |
| `scales` | Weights and units, products, recipes, scale-specific operations and PackML | [Scales](Scales.md) |
| `pumps` | Pump identity, configuration, operational measurements, supervision and maintenance | [Pumps](Pumps.md) |
| `di` | Device topology and identity, locking, parameter transfer and software update | [Device Integration](DeviceIntegration.md) |
| `isa95` | Common objects and V1/V2 job order control, responses and status | [ISA-95](ISA95.md) |
| `positioning` | Relative spatial frames, global positions, zones and coordinate conversion | [Positioning](Positioning.md) |

The current-major default remains `full`, which includes all new tools.
Existing bounded profiles retain their catalogs. Use a smaller profile or
composition for agent work: `full` deliberately includes unrelated workflows
and consequential operations. Profile selection is not an authorization check.
Use `tools/list` to inspect the exact tool names and input schemas.

IA, PA-DIM, ECM and PackML are represented through the typed clients that use
them, not through duplicate catalogs. AI, WoT Connectivity, OpenUSD and
xRegistry are outside this industrial set.

## Connect, discover, inspect, act

Connect using the usual MCP connection tools. Every family accepts
`sessionName`; omit it only when there is exactly one active session.
Discovery returns explicit node identifiers. Use those identifiers for later
reads and actions rather than guessing namespace indexes or selecting the first
matching object. Returned remote references are information, not an instruction
to connect to another server.

Related reads share finite facet selectors where that keeps the catalog small.
Commands remain explicit and do not implicitly acquire a lock, install a
package, retry a refused operation or change another state.

Two workflows intentionally compose catalogs:

- **Machine jobs:** discover the real order receiver and optional response
  provider through Machinery, then use the canonical ISA-95 tools with those
  role identifiers. A Machinery machine does not expose an ISA-95 response
  receiver. An absent response provider must not be replaced with the order
  receiver.
- **Scale products:** discover the product's DI lock and use the DI tools to
  acquire, renew and release it explicitly around protected changes. The
  Scales tools never acquire or break a lock on the caller's behalf.

There are no invented Machinery item-state or operation-mode setters: those
state machines have no standard client cause methods. Pumps and Positioning
remain observational. AMB history is not promised where the typed client has
no history implementation.

## Results, errors and bounds

New companion tools return matching textual JSON and MCP structured content.
UA values retain their type, quality and timestamps where the client contract
provides them. Non-finite numbers are explicit `"NaN"`, `"Infinity"` or
`"-Infinity"` strings, not zero or an absent measurement. Structured UA values
use their generated encoding implementation instead of reflective property
enumeration or a `ToString()` summary.

A service failure is an MCP error with its original status. Model-specific
return codes, including ISA-95 job and DI lock statuses, are not confused
with a successful UA service response. Missing optional capabilities are
distinguished from an empty supported result. A timeout does not prove that
a remote mutation did not occur; inspect server state before deciding to retry.

Paged lists return `items`, `offset`, `nextOffset`, `hasMore` and
`consistency: "live"`. The default page size is 100, with a maximum of 500.
Offsets are bounded and refer to a fresh live enumeration, not a durable
snapshot. Concurrent server changes can shift entries between pages.

Observations are finite MCP calls, not persistent background subscriptions.
They accept an item limit and a duration of at most 30 seconds, and report
whether the source completed or the item/duration bound ended the call.
External request cancellation propagates. Disposing the invocation's
enumerator removes its own monitored items; it does not dispose the managed
session's shared streaming subscription.

## File transfers

DI package uploads and Machinery result downloads use an explicitly configured,
host-owned directory. They are disabled until that directory is configured and
already exists:

```powershell
$env:OPCUA_MCP_TRANSFER_ROOT = 'D:\OpcUaTransfer'
$env:OPCUA_MCP_MAX_TRANSFER_BYTES = '67108864'
opcua-mcp --profile di,machinery
```

An embedding application can set `OpcUaMcpOptions.TransferRoot` and
`MaxTransferBytes`; the executable also accepts configuration keys
`McpServer:TransferRoot` and `McpServer:MaxTransferBytes`. The default byte limit
is 64 MiB. A caller cannot raise it through a tool argument.

Tool paths must resolve beneath the root. Symlinks, junctions, path escapes
and Windows alternate data streams are rejected. The host must keep the root
and its ancestors unwritable by untrusted local users; portable path checks
cannot prevent a privileged local process replacing directories concurrently.
Do not use a shared world-writable directory as the transfer root.

Uploads check the initial length and enforce the limit again while reading.
Downloads enforce the limit while writing, use a temporary file, publish the
completed file without overwriting an existing destination, and remove partial
files on failure or cancellation. Parent directories must already exist.
There is no arbitrary URL fetching or large inline base64 transport.
Uploading a software package does not install or confirm it; those are separate
explicit operations.

## Embed selected modules

Each family is a separate `OPCFoundation.NetStandard.Opc.Ua.Mcp.<Family>` package.
The industrial MCP libraries are preview packages and target .NET 8, 9 and 10;
the ready-to-run tool targets .NET 10.

```csharp
using Microsoft.Extensions.DependencyInjection;
using Opc.Ua.Mcp;

var profiles = McpToolProfileSet.Parse("machinery,isa95");

builder.Services.AddOpcUaMcpCore();
builder.Services.AddOpcUaMcpMachinery();
builder.Services.AddOpcUaMcpIsa95();
builder.Services.AddMcpServer()
    .WithStdioServerTransport()
    .WithOpcUaMcpFilters()
    .WithOpcUaCoreTools(profiles)
    .WithOpcUaMachineryTools(profiles)
    .WithOpcUaIsa95Tools(profiles);
```

Register filters once. Connection tools are registered once even when multiple
families need them. A model/package dependency does not silently enable its
MCP catalog. Accessors can also be constructed directly with an existing
`OpcUaSessionManager`; they do not own the sessions.

New value projections avoid JSON reflection, but the MCP SDK's existing
tool-registration path still requires JSON reflection. This feature does not
make the complete MCP executable NativeAOT-compatible. The repository's MCP
AOT tests pin that limitation explicitly.

See [MCP server](McpServer.md) for transports, certificate trust, sessions,
resources and the catalog usability gate.
