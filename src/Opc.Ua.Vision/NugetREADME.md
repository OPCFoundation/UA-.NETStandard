# Opc.Ua.Vision

Server/client-independent foundation for the **draft** *OPC UA — Vision*
companion specification.

The Vision NodeSet is **source-generated** here directly over the base OPC UA
namespace (no DI, Machinery or Robotics dependency), exposing generated
ObjectTypes, ReferenceTypes, enums, typed node states and client proxies, plus
the `AddOpcUaVision` model loader. The generated `ObjectTypeIds`,
`ReferenceTypeIds` and `DataTypeIds` classes are the source of truth for the
model.

The model covers the vision domain end-to-end: a `VisionRootType` topology
grouping vision sensors (`VisionSensorType`, `ImageSensorType`,
`Depth3DSensorType`) together with their `OpticsType` and `IlluminationType`
components; `CoordinateFrameType`, `VisionCalibrationType`,
`IntrinsicCalibrationType` and `ExtrinsicCalibrationType` for the spatial
grounding of every sensor; media surfaces (`MediaEndpointType`,
`StreamEndpointType`, `ClipEndpointType`, `VisionMediaManagementType`) for how
image and video data leaves the server; inference (`InferencePipelineType`);
results (`VisionResultType`, `InspectionResultType`, `DetectionResultType`,
`SegmentationResultType`) and their `VisionFeedbackType` correction cycle; and
the `IVisionSimulatedType` marker interface for simulated sensors. The
`HasCalibration`, `MountedOn`, `HasScenePrim` and `ProducedBy` reference types
carry the semantic relationships between these components.

## Numerical conventions

Every DataType in this package respects §5.12 of the specification:

- Positions are metres.
- Orientations are unit quaternions ordered `(x, y, z, w)`.
- `VisionIntrinsicsDataType.Cx` / `Cy` are corner-datum principal-point
  coordinates.
- An empty covariance array is the sentinel for "not reported" — not a
  zero matrix.

## Related packages

| Package | Adds |
|---|---|
| `OPCFoundation.NetStandard.Opc.Ua.Vision.Server` | Hosting a Vision server: `AddVision`, `ConfigureVision`, provider abstractions, fluent topology builders, facet derivation |
| `OPCFoundation.NetStandard.Opc.Ua.Vision.Client` | `VisionClient` discovery, sensors, media, inference, results, `VisionFrameGraph` pose composition, `VisionFeedbackClient` |
| `OPCFoundation.NetStandard.Opc.Ua.Vision.OpenUsd` | Rendering a simulated sensor's camera view offscreen from an OpenUSD stage |
| `OPCFoundation.NetStandard.Opc.Ua.Mcp.Vision` | MCP tools for perception agents, including `vision_get_frame` returning an MCP `ImageContentBlock` |

See the [Vision developer guide](https://github.com/OPCFoundation/UA-.NETStandard/blob/main/docs/Vision.md)
for the full end-to-end story, code examples and the bin-picking sample.

> The namespace `http://opcfoundation.org/UA/Vision/` and every NodeId in it are
> **provisional**. The model is a working-group draft.

## License

OPC Foundation MIT License 1.00 — <http://opcfoundation.org/License/MIT/1.00/>
