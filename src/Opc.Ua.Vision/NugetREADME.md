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

> The namespace `http://opcfoundation.org/UA/Vision/` and every NodeId in it are
> **provisional**. The model is a working-group draft and is neither official
> nor endorsed by the OPC Foundation.
