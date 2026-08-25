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

`vision_run_inference` accepts one structured request and returns the ResultId,
result NodeId, authoritative result kind and provenance in the same call. Its
default `Summary` detail includes a bounded detection, inspection or
segmentation summary; use the corresponding `vision_read_*_result` tool for the
complete result. Pipelines can be selected by NodeId or by an exact,
unambiguous BrowseName/DisplayName:

```json
{
  "request": {
    "pipeline": "BinPickingPipeline",
    "expectedKind": "Detection",
    "detail": "Summary",
    "maxItems": 20
  }
}
```

## Tools (22)

- **Discovery (4)** — `vision_list_sensors`, `vision_list_pipelines`,
  `vision_list_frames`, `vision_list_calibrations`.
- **Monitoring (6)** — `vision_read_sensor`,
  `vision_read_extrinsic_calibration`, `vision_read_pipeline`,
  `vision_read_detection_result`, `vision_read_inspection_result`,
  `vision_read_segmentation_result`.
- **Seeing (2)** — `vision_get_frame` (`ImageContentBlock`),
  `vision_get_frame_metadata`.
- **Inference (3)** — `vision_run_inference`,
  `vision_start_continuous_inference`, `vision_stop_inference`.
- **Feedback (4)** — `vision_submit_detections`,
  `vision_submit_inspection_result`, `vision_submit_correction`,
  `vision_submit_image_reference`.
- **Geometry (3)** — `vision_read_frame`, `vision_compose_pose`,
  `vision_compose_transform`.

Server refusals are returned honestly with the exact `StatusCode` and message;
the MCP layer does not retry and never acquires command authority as a side
effect.

## Usage

### Standalone `vision` profile

Every Vision tool resolves a named OPC UA session, and only the connection
tools can open one. The single-profile overload therefore carries
`ConnectionTools` itself, so the bounded `vision` profile is usable
end-to-end without composing with any other package:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Opc.Ua.Mcp;

builder.Services.AddOpcUaMcpCore();
builder.Services.AddOpcUaMcpVision();

builder.Services.AddMcpServer()
    .WithStdioServerTransport()
    .WithOpcUaMcpFilters()
    .WithOpcUaCoreTools(McpToolProfile.Vision)
    .WithOpcUaVisionTools(McpToolProfile.Vision);
```

### Composed with `robotics`

Use the `McpToolProfileSet` overloads to combine Vision with Robotics — the
composition the [BinPickingClient sample](https://github.com/OPCFoundation/UA-.NETStandard/tree/main/samples/Robotics/BinPickingClient)
runs. The core-tools overload owns and deduplicates `ConnectionTools`
across every package that references the same MCP server:

```csharp
using Opc.Ua.Mcp;

McpToolProfileSet profiles = new McpToolProfileSet(
    new[] { McpToolProfile.Vision, McpToolProfile.Robotics });

builder.Services.AddOpcUaMcpCore();
builder.Services.AddOpcUaMcpVision();
builder.Services.AddOpcUaMcpRobotics();

builder.Services.AddMcpServer()
    .WithStdioServerTransport()
    .WithOpcUaMcpFilters()
    .WithOpcUaCoreTools(profiles)
    .WithOpcUaVisionTools(profiles)
    .WithOpcUaRoboticsTools(profiles);
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

See the [Vision developer guide](https://github.com/OPCFoundation/UA-.NETStandard/blob/main/docs/Vision.md#mcp-tools)
and the [MCP Server guide](https://github.com/OPCFoundation/UA-.NETStandard/blob/main/docs/McpServer.md)
for the profile table, composition rules and the bin-picking sample.

## License

OPC Foundation MIT License 1.00 — <http://opcfoundation.org/License/MIT/1.00/>
