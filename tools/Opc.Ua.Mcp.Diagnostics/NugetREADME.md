# OPCFoundation.NetStandard.Opc.Ua.Mcp.Diagnostics

OPC UA Model Context Protocol (MCP) tools for protocol diagnostics, packaged so
they can be embedded in any MCP server host rather than only run as the shipped
`opcua-mcp` tool.

Use this package when an application wants to let an LLM capture and inspect
UA-TCP traffic alongside tools of its own.

## Tools

UA-TCP packet capture to pcapng, decode of captured traffic, and replay of a
capture against a live endpoint.

## Security

The decode and replay tools disclose symmetric channel keys and can re-send
captured traffic. They are **disabled by default** and are registered only when
a host opts in, either through `Pcap:EnableDiagnosticsTools` in configuration or
the `OPCUA_PCAP_ENABLE_DIAGNOSTICS` environment variable. `AreDiagnosticsToolsEnabled`
evaluates both.
Enabling diagnostics emits a startup warning so the choice is visible in logs.

Capture and key-log paths are confined by `McpCapturePath.ResolveAndValidate`.
Root precedence is `OpcUaMcpOptions.PcapBaseFolder`, then `PcapOptions.BaseFolder`,
then the per-user platform default documented in the
[diagnostics storage guide](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/docs/Diagnostics.md#default-storage).
Keep that directory owned by the dedicated service account. Treat captures and
key logs as secrets, authenticate the MCP transport, and audit its use.
Mock-client replay additionally requires `AllowMockClientReplay` and explicit
`AllowedReplayEndpoints`; no wildcard or implicit permission is granted.

## Arguments and outputs

| Tools | Arguments / result selection |
| --- | --- |
| `start_capture`, `capture_now` | `source`: `nic`, `inproc-client`, `inproc-server`, `replay`; optional interface, filter, endpoint, limits and folder |
| `get_capture`, `capture_now` | `format`: `pcap`, `pcapng`, `json`, `csv`, `text`, `service-timeline`; optional packet/partial controls |
| `stop_capture`, `get_capture`, `stop_replay` | `sessionId` returned by creation |
| `list_captures` | Optional `state` |
| `dump_keys` | Optional `sessionId`, `format`, `includeExpired` |
| `decode_pcap_with_keys` | `pcapPath`, `keylogPath`, `format`, optional `maxFrames` |
| `summarize_service_calls` | `sessionId` or `pcapPath` + `keylogPath`, optional `top` |
| `replay_pcap` | `pcapPath`, `keylogPath`, `mode`, endpoints and `speed` |

Hyphenated enum values are emitted on output; CLR names such as
`InProcessClient` and `ServiceTimeline` remain accepted on input.
Interface enumeration does not open adapters, so `linkType` may be null.

See the [tool catalogue](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/docs/McpServer.md#packet-capture-tools)
for per-tool behavior.

## Usage

```csharp
PcapOptions pcapOptions = OpcUaMcpDiagnosticsExtensions.CreatePcapOptions(builder.Configuration);
bool diagnosticsEnabled = OpcUaMcpDiagnosticsExtensions.AreDiagnosticsToolsEnabled(pcapOptions);

builder.Services.AddOpcUaMcpCore();
builder.Services.AddOpcUaMcpDiagnostics(options =>
    options.EnableDiagnosticsTools = pcapOptions.EnableDiagnosticsTools);

builder.Services.AddMcpServer()
    .WithStdioServerTransport()
    .WithOpcUaMcpFilters()
    .WithOpcUaCoreTools(McpToolProfile.Diagnostics)
    .WithOpcUaDiagnosticsTools(McpToolProfile.Diagnostics, diagnosticsEnabled)
    .WithTools<MyApplicationTools>();
```

A profile that does not select diagnostics contributes no tools rather than
failing, so the same profile value can be passed to every OPC UA tool package a
host references.

## Related packages

| Package | Adds |
|---|---|
| `OPCFoundation.NetStandard.Opc.Ua.Mcp.Core` | Part 4 service tools, session management, filters (required) |
| `OPCFoundation.NetStandard.Opc.Ua.Mcp.PubSub` | PubSub runtime, actions and discovery |
| `OPCFoundation.NetStandard.Opc.Ua.Mcp.PubSub.Diagnostics` | PubSub packet capture and decode |
| `OPCFoundation.NetStandard.Opc.Ua.Mcp` | the ready-to-run `opcua-mcp` server composing all of the above |

## License

OPC Foundation MIT License 1.00 — <http://opcfoundation.org/License/MIT/1.00/>
