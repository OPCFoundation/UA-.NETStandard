# OPC UA Pumps MCP tools

Embed eight read-only OPC 40223 tools with `AddOpcUaMcpPumps()` and
`WithOpcUaPumpsTools(McpToolProfile.Pumps)`, after registering MCP Core.
The `pumps` profile discovers pumps, reads nameplates, markings, operational and
maintenance groups, ports, typed value sets and snapshots. It never writes pump controls.

Every call resolves its `sessionName` anew. Discovery uses live offset paging;
measurements retain UA types, quality, units, ranges and requested source timestamps.

## Arguments and results

`pumps_read_group` accepts a discovered pump NodeId and a finite facet such as
`Measurements`, `Supervision` or `Maintenance`:

```json
{
  "pumpNodeId": "ns=2;s=Pump1",
  "facet": "Measurements",
  "includeMetadata": true,
  "includeTimestamps": true,
  "sessionName": "plant"
}
```

`pumps_list_markings` takes the **markings folder** NodeId returned by
`pumps_read_nameplate`, not the pump NodeId. Discovery deduplicates pumps visible
under both DeviceSet and Machines. Pages describe live enumerations, not durable
snapshots.

See the [tool catalogue](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/docs/McpServer.md#pumps-tools)
and [Pumps client guide](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/docs/Pumps.md).
