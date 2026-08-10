# OPCFoundation.NetStandard.Opc.Ua.Mcp.Vision

OPC UA Model Context Protocol (MCP) tools that let a language model **see**
through an OPC UA Vision server and act on what it sees, packaged so they can
be embedded in any MCP server host rather than only run as the shipped
`opcua-mcp` tool.

Use this package when an application wants an LLM agent to enumerate Vision
sensors and pipelines, capture the current camera frame as MCP image content,
run inference or submit off-server perception feedback, and compose poses
between named coordinate frames.

## The headline capability

`vision_get_frame` returns the encoded still image as an MCP `ImageContentBlock`
with the correct MIME type, so the model actually sees pixels rather than
reading a description of them. When the server cannot render — for example on
CI without a graphics device — the tool returns an actionable text explanation
instead of a broken image.

## Tools

Discovery, per-sensor and per-pipeline reads, live result reads,
`vision_get_frame` and its metadata variant, single-shot and continuous
inference, off-server feedback (`SubmitDetections`, `SubmitInspectionResult`,
`SubmitCorrection`, `SubmitImageReference`), and coordinate-frame
composition via §5.12 conventions.

Server refusals are returned honestly with the exact `StatusCode` and message;
the MCP layer does not retry and never acquires command authority as a side
effect.

## Usage

```csharp
builder.Services.AddOpcUaMcpCore();
builder.Services.AddOpcUaMcpVision();

builder.Services.AddMcpServer()
    .WithStdioServerTransport()
    .WithOpcUaMcpFilters()
    .WithOpcUaCoreTools(McpToolProfile.Vision)
    .WithOpcUaVisionTools(McpToolProfile.Vision)
    .WithTools<MyApplicationTools>();
```

A profile that does not select Vision contributes no tools rather than
failing, so the same profile value can be passed to every OPC UA tool package a
host references.

## Related packages

| Package | Adds |
|---|---|
| `OPCFoundation.NetStandard.Opc.Ua.Mcp.Core` | Part 4 service tools, session management, filters (required) |
| `OPCFoundation.NetStandard.Opc.Ua.Vision.Client` | Vision discovery, sensors, frames, media, inference, feedback client API |
| `OPCFoundation.NetStandard.Opc.Ua.Mcp.Robotics` | Robot Intent tools that pair with Vision for pick-and-pack scenarios |
| `OPCFoundation.NetStandard.Opc.Ua.Mcp` | the ready-to-run `opcua-mcp` server composing all OPC UA MCP tool packages |

## License

OPC Foundation MIT License 1.00 — <http://opcfoundation.org/License/MIT/1.00/>
