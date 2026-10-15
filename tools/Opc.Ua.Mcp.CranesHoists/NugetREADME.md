# OPC UA Cranes and Hoists MCP tools (preview)

Bounded OPC 40020-1 workflows over `CranesHoistsClient`. This package and its
Debug variant are preview-only. It uses the host's existing OPC UA session
manager; it does not implement a separate protocol or subscription engine.

```csharp
services.AddOpcUaMcpCore();
services.AddOpcUaMcpCranesHoists();
services.AddMcpServer()
    .WithStdioServerTransport()
    .WithOpcUaMcpFilters()
    .WithOpcUaCranesHoistsTools(McpToolProfile.CranesHoists);
```

`WithOpcUaCranesHoistsTools` also accepts a `McpToolProfileSet` for composition.
`Full` selects all crane tools. Other profiles contribute nothing. Shared
connection tools are registered at most once, even in composed catalogs.
Register `CranesHoistsClientOptions` before the accessor to customize bounded
topology discovery, and `TimeProvider` to customize the observation clock.
Direct construction with `new CranesHoistsClientAccessor(sessionManager)`
uses the same typed workflows and borrows, rather than owns, the session.

| Tool | Workflow |
| --- | --- |
| `craneshoists_list_systems` | Subtype-aware discovery from DeviceSet or a supplied organizer, including Machines |
| `craneshoists_read_system` | System/device/axis/load/motor/brake/protection snapshots and actual capabilities |
| `craneshoists_read_element` | Focused snapshot, nameplate, axis metadata and native relationships |
| `craneshoists_observe_values` | One bounded observation of 1–64 distinct signal NodeIds |
| `craneshoists_observe_events` | One bounded observation of an event-enabled system or server notifier |
| `craneshoists_configure_limit` | One explicitly authorized standard limit-configuration request |

Present signals include their actual NodeId, current-user write access and a
native UA JSON `reading.DataValue`, retaining wire types, quality and source/server
timestamps. Decode it with the stack's `JsonDecoder.ReadDataValue("DataValue")`.
`present=false` means a selected optional field is absent; it is not a failed
read. Service/access failures return an MCP error with the original UA status.
Snapshots keep actual vendor subtype identities and forward `ProtectsAxis` or
physical-component references. Namespace presence alone is not a capability claim.

Requests and replies are bounded. Discovery defaults to 50 systems (maximum
100); the typed client also limits visited objects and browse pages. Observations
default to 5 seconds and 50 notifications, with ceilings of 60 seconds,
100 notifications and 1 MiB decoded payload/serialized JSON. Serialized replies
are bounded separately, including metadata. Overflow fails instead of returning
partial success. Observations expose the completion reason and dropped count,
preserve event IDs and qualified field order, and release only their own
subscription, including on error and cancellation.
The server honors standard MCP cancellation notifications; a client must send
the notification, not merely abandon its local wait. Direct embedding calls
also honor their `CancellationToken`.

## Explicit configuration, not motion control

`craneshoists_configure_limit` is marked device-changing and non-idempotent.
It requires `confirmConfiguration=true` for each call and still requires the
connected caller's server-side authorization and provider acceptance. Exact
selectors are:

- `SpeedLimitDirMinusSetpoint`, `SpeedLimitDirPlusSetpoint`: finite JSON numbers,
  inclusive range 0–100, sent as UA Double.
- `SpeedLimitEnabledDirMinus`, `SpeedLimitEnabledDirPlus`: JSON Booleans on an axis.
- `SpeedLimitGlobalEnabled`: a JSON Boolean on a system.

For example, a call with `elementNodeId` from an axis snapshot,
`signal="SpeedLimitDirPlusSetpoint"`, `value=25.0` and
`confirmConfiguration=true` submits only that standard request property.
`requestAccepted=true` is **not** proof that an active limit or hardware changed;
the response explicitly reports `activeValueVerified=false`. Re-read the axis's
separate `SpeedLimitDirPlus` signal to inspect the provider-reported active value.
No implicit retries, motion methods, control takeover or safety bypass exist.
If a configuration call is canceled or disconnected after submission, its
outcome may be unknown: inspect request and active values rather than retrying
automatically.
Discovery and observations never configure limits, refresh/acknowledge/confirm
conditions or initiate equipment effects.
