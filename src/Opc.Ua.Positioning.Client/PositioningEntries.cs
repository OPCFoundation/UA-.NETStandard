/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person
 * obtaining a copy of this software and associated documentation
 * files (the "Software"), to deal in the Software without
 * restriction, including without limitation the rights to use,
 * copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following
 * conditions:
 *
 * The above copyright notice and this permission notice shall be
 * included in all copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
 * EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES
 * OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
 * NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
 * HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
 * WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
 * OTHER DEALINGS IN THE SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

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
