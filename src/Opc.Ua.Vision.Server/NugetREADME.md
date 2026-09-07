# Opc.Ua.Vision.Server

Server hosting for the **draft** *OPC UA — Vision* companion model.

The package materialises the well-known `Vision` object under the Server
object (i=2253), exposes fluent APIs to describe sensors, coordinate frames,
calibrations, media endpoints and inference pipelines, and wires host-supplied
providers behind the model's methods so a Server never hard-codes a
particular camera, scene or model.

## What it gives you

- `AddVision()` — registers the stock `VisionNodeManager` and factory in the
  Generic Host pipeline. Requires no DI, Machinery or Robotics dependency:
  the Vision NodeSet only requires the base UA namespace.
- `ConfigureVision(...)` / `ConfigureVisionFor<TNodeManager>(...)` — fluent
  configurator that runs on server start.
- `VisionNodeManager` / `VisionNodeManagerFactory` — standalone node
  manager for direct construction and for hosting extensions.
- `IVisionModelProvider` — deterministic composition of additional compiled
  Vision-namespace providers.
- `IVisionMediaProvider`, `IVisionInferenceProvider`,
  `IVisionFeedbackSink` — the provider abstractions a host implements to
  supply media (streams, clips), run inference, and receive off-server
  feedback.
- `IVisionBuildContext` and its `IVisionNodeBuilder` — the fluent surface
  for adding coordinate frames, calibrations, sensors, media endpoints,
  inference pipelines and feedback objects to the address space.
- Facet derivation from the materialised address space, published in
  `Server.ServerCapabilities.ServerProfileArray` (VIS-Base, VIS-Media-*,
  VIS-Calibration, VIS-Result-*, VIS-Feedback, VIS-Inference-OnServer /
  VIS-Inference-OffServer, VIS-Simulation, VIS-Learning).

## Example

```csharp
using Microsoft.Extensions.Hosting;
using Opc.Ua;
using Opc.Ua.Server;
using Opc.Ua.Vision;
using Opc.Ua.Vision.Server;
using Opc.Ua.Vision.Server.Builders;

builder.Services
    .AddOpcUa()
    .AddServer(options => options.EndpointUrls.Add("opc.tcp://localhost:62855/VisionServer"))
    .AddVision(options => options.InstanceNamespaceUri = "urn:example:vision:instances")
    .AddVisionMediaProvider<MyMediaProvider>(sensorBrowseName: "Camera01")
    .AddVisionInferenceProvider<MyGroundTruthProvider>(
        pipelineBrowseName: "Detector",
        onServer: true)
    .ConfigureVision((context, ct) =>
    {
        IVisionNodeBuilder nodes = context.Nodes;

        nodes.AddFrame("World", f => f
            .WithFrameId("world")
            .WithRole(VisionFrameRoleEnum.World));

        nodes.AddFrame("Flange", f => f
            .WithFrameId("flange")
            .WithRole(VisionFrameRoleEnum.MechanicalInterface)
            .WithParent("world"));

        nodes.AddImageSensor("Camera01", s => s
            .WithSensorId("cam-01")
            .WithModality(VisionSensorModalityEnum.Area2D)
            .WithFrameId("flange")
            .WithResolution(1920u, 1080u)
            .WithPixelFormat("Mono8")
            .AddClipEndpoint("Clips", ep => ep
                .WithEndpointId("clip-01")
                .WithClipFormat(VisionClipFormatEnum.Png)
                .WithResolution(1920u, 1080u)
                .WithInlineDelivery(enabled: true, maxInlineClipSize: 8_388_608u)));

        nodes.AddPipeline("Detector", pipe => pipe
            .WithPipelineId("pipe-01")
            .WithSensor(NodeId.Null));

        return ValueTask.CompletedTask;
    });
```

## Provider abstractions

- Implement `IVisionMediaProvider` on the host to serve media without
  putting pixels on OPC UA. `GetStreamAsync` returns a leased URI;
  `GetClipAsync` returns a `VisionImageReferenceDataType` and, when the
  caller asks for inline delivery and the encoded bytes fit the effective
  limit, an inline `ByteString`. The §6.4 `Bad_NotSupported` /
  `Bad_NoDataAvailable` / `Bad_EncodingLimitsExceeded` states are all
  observable by a client through `LatestClip` while `LatestClipMetadata`
  keeps returning the URI.
- Implement `IVisionInferenceProvider` to bind a pipeline to whatever
  actually computes results — on the Server, on an edge GPU, in the cloud,
  or in a simulator. The Server publishes the result nodes and applies the
  spec's method conventions regardless of where inference runs.
- Implement `IVisionFeedbackSink` to receive `SubmitDetections`,
  `SubmitInspectionResult`, `SubmitCorrection` and
  `SubmitImageReference`. Off-server VLM agents publish through this path,
  and the Server records what it did not compute.

## Related packages

| Package | Adds |
|---|---|
| `OPCFoundation.NetStandard.Opc.Ua.Vision` | Source-generated Vision model (required) |
| `OPCFoundation.NetStandard.Opc.Ua.Vision.Client` | High-level client for the same model |
| `OPCFoundation.NetStandard.Opc.Ua.Vision.OpenUsd` | A reference `ISceneCameraCaptureProvider` that renders a `UsdGeomCamera` offscreen and reports `NoRenderingBackend` gracefully on CI |
| `OPCFoundation.NetStandard.Opc.Ua.Mcp.Vision` | MCP tools that let a language-model agent drive a Vision server |

See the [Vision developer guide](https://github.com/OPCFoundation/UA-.NETStandard/blob/main/docs/Vision.md)
for the hosting-API table, the full topology-builder surface, the two
perception paths (`OnServer` vs `EdgeOffServer`), facet derivation and the
bin-picking sample.

> The namespace `http://opcfoundation.org/UA/Vision/` and every NodeId in it
> are **provisional**. The model is a working-group draft.

## License

OPC Foundation MIT License 1.00 — <http://opcfoundation.org/License/MIT/1.00/>
