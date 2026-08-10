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
- `ConfigureVision(...)` — fluent configurator that runs on server start.
- `VisionNodeManager` / `VisionNodeManagerFactory` — standalone node
  manager for direct construction and for hosting extensions.
- `IVisionModelProvider` — deterministic composition of additional compiled
  Vision-namespace providers.
- `IVisionMediaProvider`, `IVisionInferenceProvider`,
  `IVisionFeedbackSink` — the provider abstractions a host implements to
  supply media (streams, clips), run inference, and receive feedback from
  off-server callers.
- `IVisionBuildContext` and its builders — the fluent surface for adding
  coordinate frames, calibrations, sensors, media endpoints, inference
  pipelines and feedback objects to the address space.
- Facet derivation from the materialised address space, published in
  `Server.ServerCapabilities.ServerProfileArray` (VIS-Base, VIS-Media-*,
  VIS-Calibration, VIS-Result-*, VIS-Feedback, VIS-Inference-*,
  VIS-Simulation, VIS-Learning).

## Example

```csharp
services.AddOpcUaServer(...)
    .AddVision()
    .ConfigureVision(context =>
    {
        context.Nodes.Frames.Add("world", VisionFrameRoleEnum.World);
        context.Nodes.Frames.Add(
            "flange",
            VisionFrameRoleEnum.MechanicalInterface,
            parentFrame: "world");

        context.Nodes.Sensors.AddImage("cam0", cam => cam
            .WithSize(1920, 1080)
            .WithPixelFormat("Mono8")
            .MountedOn("flange")
            .WithMediaProvider(myMediaProvider));

        context.Nodes.Pipelines.Add("pipe0", pipe => pipe
            .UsesSensor("cam0")
            .WithInferenceProvider(myInferenceProvider)
            .WithFeedbackSink(myFeedbackSink));
    });
```

## Provider abstractions

- Implement `IVisionMediaProvider` on the host to serve media without
  putting pixels on OPC UA: `GetStreamAsync` returns a leased URI,
  `GetClipAsync` returns a `VisionImageReferenceDataType` and, when the
  caller asks for it and the encoded bytes fit, an inline `ByteString`.
- Implement `IVisionInferenceProvider` to bind a pipeline to whatever
  actually computes results — on the Server, on an edge GPU, in the cloud,
  or in a simulator. The Server publishes the result nodes and applies the
  spec's method conventions regardless of where inference runs.
- Implement `IVisionFeedbackSink` to receive `SubmitDetections`,
  `SubmitInspectionResult`, `SubmitCorrection` and
  `SubmitImageReference`. Off-server VLM agents publish through this path,
  and the Server records what it did not compute.

> The namespace `http://opcfoundation.org/UA/Vision/` and every NodeId in it
> are **provisional**. The model is a working-group draft and is neither
> official nor endorsed by the OPC Foundation.
