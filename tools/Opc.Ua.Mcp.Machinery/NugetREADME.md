# OPC UA Machinery MCP tools

Embed OPC 40001 Machinery tools with `AddOpcUaMcpMachinery()` and
`WithOpcUaMachineryTools(McpToolProfile.Machinery)`, after registering MCP Core.
Discovery, monitoring, job endpoint lookup, results and bounded streaming downloads
use the typed Machinery client. Zero-point adjustment is an explicit consequential tool.
There are no invented machine state or operating-mode setters.

Compose `machinery,isa95` for explicit ISA-95 job commands against the actual endpoint
IDs returned by Machinery. Configure the Core download root before downloading.
See `docs/Machinery.md` in the source repository for examples.
