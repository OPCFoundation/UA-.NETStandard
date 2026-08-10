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
  point;
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
- `AddVisionClient()` — DI registration for `IOpcUaClientBuilder`.

> The namespace `http://opcfoundation.org/UA/Vision/` and every NodeId in it
> are **provisional**. The model is a working-group draft and is neither
> official nor endorsed by the OPC Foundation.
