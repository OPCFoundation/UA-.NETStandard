# Opc.Ua.Vision.Client

Client-side helpers for the **OPC UA — Vision** companion model (working-group
draft).

Built on **Opc.Ua.Vision** and **Opc.Ua.Client**, this package exposes a
high-level surface over the source-generated proxies so a client can drive a
Vision server without knowing NodeIds or BrowseNames:

- `VisionClient` — resolves the well-known `Vision` object (§4.2) and
  enumerates sensors, inference pipelines, and coordinate frames;
- `VisionSensorClient` — reads a sensor's identity, imaging members, optics,
  illumination, mounted frame and calibrations (intrinsic and hand-eye
  extrinsic);
- `VisionFrameGraph` — walks the `CoordinateFrameType` tree and composes
  transforms between any two named frames (camera → flange → base, camera →
  flange → tool centre point). Follows the §5.12 conventions exactly —
  right-handed frames, quaternion order (x, y, z, w), corner-datum principal
  point, and refuses a non-unit quaternion within tolerance 1e-6;
- `VisionMediaClient` — `GetClip` by reference (default), inline
  `LatestClip`/`LatestClipMetadata`, and honest surfacing of the §6.4 case
  where inline delivery is disabled (`Bad_NotSupported`);
- `VisionPipelineClient` — `RunInference`, `StartContinuous`, `Stop`, and
  reading pipeline members including the deployment inference location;
- `VisionResultReader` — reads `DetectionResultType`, `InspectionResultType`,
  `SegmentationResultType` and streams result changes over an
  `IStreamingSubscription`;
- `VisionFeedbackClient` — `SubmitDetections`, `SubmitInspectionResult`,
  `SubmitCorrection`, `SubmitImageReference`, the headline path for an
  off-server vision-language model publishing results the Server did not
  compute;
- `AddVisionClient()` — DI registration for `IOpcUaClientBuilder`;
- `session.Vision(telemetry)` — non-DI extension for creating a client over
  any connected `ISession`.

## Example

```csharp
using Opc.Ua.Client;
using Opc.Ua.Vision;
using Opc.Ua.Vision.Client;

// From any connected session:
VisionClient vision = session.Vision(telemetry);
if (!vision.IsVisionNamespaceAvailable)
{
    return; // Server does not implement Vision.
}

await foreach (VisionNodeEntry sensor in vision.EnumerateSensorsAsync(ct))
{
    VisionSensorClient s = vision.Sensor(sensor.NodeId);
    VisionSensorIdentity identity = await s.ReadIdentityAsync(ct);
    // Read intrinsic / hand-eye calibrations, media endpoints, etc.
}

// Read the latest detection result for a pipeline, then compose the
// first detection's pose from the camera frame into the world frame.
VisionFrameGraph frames = vision.Frames();
VisionDetectionResultSnapshot det =
    await vision.Result(resultNodeId).ReadDetectionAsync(ct);
if (det.Detections.Count > 0 && det.Detections[0].HasPose)
{
    VisionPose3DDataType inWorld = await frames.ComposeAsync(
        det.Detections[0].Pose, cameraFrameNodeId, worldFrameNodeId, ct);
}
```

See the [Vision developer guide](https://github.com/OPCFoundation/UA-.NETStandard/blob/main/docs/Vision.md)
for discovery, streaming, feedback and the frame-graph composition
example.

> The namespace `http://opcfoundation.org/UA/Vision/` and every NodeId in it
> are **provisional**. The model is a working-group draft.

## Related packages

| Package | Adds |
|---|---|
| `OPCFoundation.NetStandard.Opc.Ua.Vision` | Source-generated Vision model (required) |
| `OPCFoundation.NetStandard.Opc.Ua.Vision.Server` | Hosting a Vision server |
| `OPCFoundation.NetStandard.Opc.Ua.Mcp.Vision` | MCP tools that let a language model use `VisionClient` from an agent |

## License

OPC Foundation MIT License 1.00 — <http://opcfoundation.org/License/MIT/1.00/>
