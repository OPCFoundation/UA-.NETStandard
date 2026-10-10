# OPC UA ISA-95 MCP tools

Embed ISA-95 common-model and Job Control V1/V2 tools with `AddOpcUaMcpIsa95()`
and `WithOpcUaIsa95Tools(McpToolProfile.Isa95)`, after registering MCP Core.
Each explicit command accepts the exact endpoint role ID: an order receiver,
response provider, or response receiver. No missing role is fabricated.

All eleven V2 order verbs are separately exposed, with typed order, response,
resource and recursive parameter inputs. Optional masks preserve explicit zero
and empty values. ISA-95 return statuses are UInt64 bitmaps: only `1` is success.
Every other bitmap is a model refusal, separate from the UA service status, and
produces an MCP tool error. The exact numeric and decimal-string forms are returned.
See `docs/ISA95.md` in the source repository for the input contracts and composition.
