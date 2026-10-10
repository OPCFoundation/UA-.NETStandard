# OPC UA Pumps MCP tools

Embed eight read-only OPC 40223 tools with `AddOpcUaMcpPumps()` and
`WithOpcUaPumpsTools(McpToolProfile.Pumps)`, after registering MCP Core.
The `pumps` profile discovers pumps, reads nameplates, markings, operational and
maintenance groups, ports, typed value sets and snapshots. It never writes pump controls.

Every call resolves its `sessionName` anew. Discovery uses live offset paging;
measurements retain UA types, quality, units, ranges and requested source timestamps.
See `docs/Pumps.md` in the source repository for examples.
