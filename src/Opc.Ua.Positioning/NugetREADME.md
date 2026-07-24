# OPC UA .NET Standard — Positioning

`OPCFoundation.NetStandard.Opc.Ua.Positioning` contains the source-generated
information models for OPC UA Relative Spatial Location (OPC 10000-210) and
Global Positioning (OPC 10000-211).

The package exposes the generated NodeIds, DataTypes, NodeState classes,
ObjectType client proxies, encodeable registration, and `AddOpcUaRsl` /
`AddOpcUaGpos` model loaders. It also provides shared, NativeAOT-compatible
relative-frame and global-coordinate transformation primitives used by the
companion client and server packages.

Pair it with `OPCFoundation.NetStandard.Opc.Ua.Positioning.Server` to expose
positioning information and with
`OPCFoundation.NetStandard.Opc.Ua.Positioning.Client` to consume it.

The transformation API includes the OPC 10000-210 Annex B frame convention,
composition/inversion, WGS84 and ECEF/ENU conversion, and rigid, similarity,
or affine ground-control-point fitting with diagnostics and reflection
handling.
