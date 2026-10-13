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

This package follows the repository root version, without a forced-preview override.
Development builds still use the root prerelease version. The optional
`Opc.Ua.Vision.OpenUsd` renderer remains preview-only.

## Tools (39)

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
- **Released Machine Vision (17)** — `vision_machine_list`,
  `vision_machine_read_state`, `vision_machine_command`, `vision_machine_read_asset`,
  `vision_machine_recipes`, `vision_machine_configurations`, `vision_machine_results`,
  `vision_machine_report_safety`, `vision_machine_file`, `vision_machine_recipe_object`,
  `vision_machine_condition`, `vision_machine_capabilities`,
  `vision_machine_relationships`, `vision_machine_observe`,
  `vision_machine_upload_recipe`, `vision_machine_upload_configuration`,
  `vision_machine_download_result`. See the
  [Machine Vision tool catalogue](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/docs/McpServer.md#machine-vision-tools)
  for tool behavior.

Capability discovery reports optional interfaces on the actual selected system.
Status detail is opt-in (`includeDetails: true`). One-shot observation defaults to
5 seconds/50 notifications, caps requests at 60 seconds/100 notifications, and
always disposes its own subscription. Conditions retain the exact identifiers
needed for explicit acknowledgement and confirmation.

The complete-file tools accept at most 1 MiB of decoded content and never prepare,
activate, or start acquisition implicitly. Larger files use the existing chunked
tool; its `ReadCompletion` and `WaitForCompletion` operations inspect a returned
commit state machine. A completion wait is capped at 60 seconds.

Server refusals are returned honestly with the exact `StatusCode` and message;
the MCP layer does not retry and never acquires command authority as a side
effect.

## Machine Vision JSON contract

Metadata pages contain at most 100 entries, asset snapshots at most 1,024 values,
file chunks at most 1 MiB, and serialized responses at most 2 MiB of UTF-8 JSON.
Oversized responses fail rather than truncate; narrow the root/page or use chunks.

`vision_machine_command.parametersJson` is an object using the UA JSON encoding
of a Variant `Parameters` array and optional `ExternalRecipeId` structure.
Recipe/configuration `identifierJson` preserves complete binary identifiers,
including version/hash metadata. Result `filtersJson` supports `MeasId`,
`PartId`, recipe/configuration identifiers, `ProductId` and `JobId`; `Components`
returns all result components. These inputs use `JsonEncoder`'s representation,
with a one-MiB input bound. Conflicting scalar and structured selectors fail.

Call `vision_machine_capabilities` without a selector for model support, or
with `systemNodeId` to resolve optional management/transfer/safety/step objects
and instance methods. `vision_machine_read_state.includeDetails=true` adds
DiagnosticLevel, SystemState, active configuration, safety and step states.
Absent optional values retain their resolution status.

`vision_machine_relationships.relationship` selects `SameEntity`, `OpticalPath`,
`DataTransmission` or `Interface`; `direction` selects `Forward`, `Inverse` or
`Both`. Results contain at most 100 unique references and a `nextIndex`, with
offsets capped at 10,000. Each page is a live view.

For example, `vision_machine_observe` accepts:

```json
{
  "notifierNodeId": "ns=4;s=Inspection",
  "mode": "Conditions",
  "refreshConditions": true,
  "durationSeconds": 5,
  "maximumNotifications": 50
}
```

The result includes `completionReason`, `refreshComplete`, `droppedNotifications`,
namespace-qualified `fieldPaths` and event fields. An empty path selects the
condition's NodeId attribute. Pass the exact `eventIdBase64` and
`conditionNodeId` to an explicit condition operation. A refresh start/end pair
sets `refreshComplete`; it does not imply that no events were dropped.
Cancellation, setup and refresh failures remain errors. The tool owns its
subscription and leaves the session and other subscriptions open.

Complete uploads accept a system NodeId, `externalId` or full `identifierJson`,
and `dataBase64`; recipes also accept `productId`. They transfer only when the
server requests it and wait for commit, without preparation/activation.
`vision_machine_download_result` takes `systemNodeId`, `resultId` and
`maximumBytes` (1..1,048,576), returning the entire file as a UA ByteString or
an error. File and metadata handles are released on every exit.

For larger transfers use `vision_machine_file`. After `Commit`, pass the returned
completion-state-machine NodeId to `ReadCompletion` or `WaitForCompletion`.
A null NodeId means synchronous completion. Waiting is capped at 60 seconds;
Error, unavailable state and timeout are errors, never a retry of Commit.

Perception `vision_run_inference.request.expectedKind` may require Detection,
Inspection or Segmentation. `detail=Summary` returns a bounded typed summary
with ResultId, result NodeId, requested/published pipeline and sensor/model/frame
provenance; `HandleOnly` omits the payload. Read the complete payload with the
corresponding result tool.

## Usage

`VisionMachineToolsTests` and `McpToolProfileCompositionTests` cover registration
and payload bounds. The MCP host's catalogue NativeAOT limits are separate from
the reflection-free UA model encoders.

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

See the [Vision developer guide](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/docs/Vision.md)
and the [MCP Server guide](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/docs/McpServer.md)
for the profile table, composition rules and the bin-picking sample.

## License

OPC Foundation MIT License 1.00 — <http://opcfoundation.org/License/MIT/1.00/>
