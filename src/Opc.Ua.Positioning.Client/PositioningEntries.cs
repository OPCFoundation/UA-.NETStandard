// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using Opc.Ua.Gpos;

namespace Opc.Ua.Positioning.Client
{
    /// <summary>
    /// A discovered Positioning object.
    /// </summary>
    public sealed record PositioningObjectEntry(
        NodeId NodeId,
        QualifiedName BrowseName,
        LocalizedText DisplayName,
        NodeId TypeDefinitionId);

    /// <summary>
    /// A typed RSL frame value and its Base relationship.
    /// </summary>
    public sealed record RelativeSpatialFrameValue(
        NodeId NodeId,
        NodeId BaseNodeId,
        ThreeDFrame Frame,
        StatusCode StatusCode,
        DateTimeUtc SourceTimestamp);

    /// <summary>
    /// An RSL frame resolved through its Base chain to the world frame.
    /// </summary>
    public sealed record ResolvedRelativeSpatialFrame(
        NodeId NodeId,
        RslFrameTransform TransformToWorld,
        ArrayOf<NodeId> FrameChain);

    /// <summary>
    /// A change to an RSL spatial list NodeVersion.
    /// </summary>
    public sealed record PositioningNodeVersionChange(
        NodeId ListNodeId,
        string NodeVersion,
        StatusCode StatusCode,
        DateTimeUtc SourceTimestamp);

    /// <summary>
    /// A typed GPOS global position value and its projection metadata.
    /// </summary>
    public sealed record GlobalPositionValue(
        NodeId NodeId,
        GlobalPositionDataType Position,
        NodeId SourceNodeId,
        uint CoordinateReferenceSystem,
        StatusCode StatusCode,
        DateTimeUtc SourceTimestamp);

    /// <summary>
    /// A typed GPOS global location value and its projection metadata.
    /// </summary>
    public sealed record GlobalLocationValue(
        NodeId NodeId,
        GlobalLocationDataType Location,
        NodeId SourceNodeId,
        uint CoordinateReferenceSystem,
        StatusCode StatusCode,
        DateTimeUtc SourceTimestamp);
}
