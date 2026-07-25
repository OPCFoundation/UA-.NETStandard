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

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.Gpos;
using Opc.Ua.Rsl;
using Opc.Ua.Server;
using Opc.Ua.Server.NodeManager;

namespace Opc.Ua.Positioning.Server
{
    /// <summary>
    /// Creates RSL and GPOS instances in any <see cref="AsyncCustomNodeManager"/>.
    /// </summary>
    public sealed class PositioningAddressSpaceBuilder
    {
        private readonly ILogger m_logger;

        private readonly Dictionary<SpatialObjectState, SpatialObjectsListState>
            m_spatialObjectLists = [];

        private readonly Dictionary<NodeState, SpatialObjectsListState>
            m_pendingModelChanges = [];

        /// <summary>
        /// Creates a builder for <paramref name="nodeManager"/>.
        /// </summary>
        public PositioningAddressSpaceBuilder(AsyncCustomNodeManager nodeManager)
        {
            NodeManager = nodeManager ??
                throw new ArgumentNullException(nameof(nodeManager));
            m_logger = nodeManager.Server.Telemetry
                .CreateLogger<PositioningAddressSpaceBuilder>();
        }

        /// <summary>
        /// Owning node manager.
        /// </summary>
        public AsyncCustomNodeManager NodeManager { get; }

        /// <summary>
        /// System context used to create model instances.
        /// </summary>
        public ServerSystemContext SystemContext => NodeManager.SystemContext;

        /// <summary>
        /// Registers a completed node subtree with the owning node manager.
        /// </summary>
        public async ValueTask RegisterAsync(
            NodeState node,
            CancellationToken cancellationToken = default)
        {
            node.ThrowIfNull(nameof(node));
            await NodeManager.AddPredefinedNodeAsync(
                node,
                cancellationToken).ConfigureAwait(false);
            SynchronizeFrameSubtree(node);

            if (m_pendingModelChanges.TryGetValue(
                node,
                out SpatialObjectsListState? list))
            {
                m_pendingModelChanges.Remove(node);
                IncrementNodeVersion(list);
                ReportModelChange(list, ModelChangeVerbs.ReferenceAdded);
            }
        }

        /// <summary>
        /// Creates a spatial objects list and links it from the standardized RSL entry point.
        /// </summary>
        /// <exception cref="ArgumentException"></exception>
        public SpatialObjectsListState CreateSpatialObjectsList(
            NodeState owner,
            QualifiedName browseName,
            string identifier,
            ThreeDFrame worldFrame,
            EUInformation lengthUnit,
            EUInformation angleUnit)
        {
            owner.ThrowIfNull(nameof(owner));
            if (string.IsNullOrWhiteSpace(identifier))
            {
                throw new ArgumentException(
                    "A stable list identifier is required.",
                    nameof(identifier));
            }
            worldFrame.ThrowIfNull(nameof(worldFrame));
            lengthUnit.ThrowIfNull(nameof(lengthUnit));
            angleUnit.ThrowIfNull(nameof(angleUnit));
            ValidateFrame(worldFrame, nameof(worldFrame));
            EnsureUniqueChild(owner, browseName);

            SpatialObjectsListState list = SystemContext
                .CreateInstanceOfSpatialObjectsListType(owner, browseName);
            list.ReferenceTypeId = ReferenceTypeIds.HasAddIn;
            owner.AddChild(list);
            list.Identifier!.Value = identifier;
            list.NodeVersion!.Value = "1";

            CartesianFrameAngleOrientationState concreteWorld =
                CreateCartesianFrame(
                    list,
                    new QualifiedName("WorldFrame", RslNamespaceIndex),
                    NodeId.Null,
                    worldFrame,
                    lengthUnit,
                    angleUnit);
            list.CreateOrReplaceWorldFrame(SystemContext, concreteWorld);

            FolderState root = GetRelativeSpatialLocations();
            root.AddReference(ReferenceTypeIds.Organizes, false, list.NodeId);
            list.AddReference(ReferenceTypeIds.Organizes, true, root.NodeId);
            return list;
        }

        /// <summary>
        /// Attaches a spatial-object AddIn to an existing object and organizes it from a list.
        /// </summary>
        public SpatialObjectState AttachSpatialObject(
            NodeState owner,
            SpatialObjectsListState list,
            QualifiedName browseName,
            string? identifier,
            ThreeDFrame positionFrame,
            EUInformation lengthUnit,
            EUInformation angleUnit)
        {
            owner.ThrowIfNull(nameof(owner));
            list.ThrowIfNull(nameof(list));
            positionFrame.ThrowIfNull(nameof(positionFrame));
            lengthUnit.ThrowIfNull(nameof(lengthUnit));
            angleUnit.ThrowIfNull(nameof(angleUnit));
            ValidateFrame(positionFrame, nameof(positionFrame));
            EnsureUniqueChild(owner, browseName);

            SpatialObjectState spatialObject = SystemContext
                .CreateInstanceOfSpatialObjectType(owner, browseName);
            spatialObject.ReferenceTypeId = ReferenceTypeIds.HasAddIn;
            owner.AddChild(spatialObject);

            if (!string.IsNullOrWhiteSpace(identifier))
            {
                spatialObject.AddIdentifier(SystemContext);
                spatialObject.Identifier!.Value = identifier;
            }

            CartesianFrameAngleOrientationState concreteFrame =
                CreateCartesianFrame(
                    spatialObject,
                    new QualifiedName("PositionFrame", RslNamespaceIndex),
                    list.WorldFrame!.NodeId,
                    positionFrame,
                    lengthUnit,
                    angleUnit);
            spatialObject.CreateOrReplacePositionFrame(SystemContext, concreteFrame);

            list.AddReference(ReferenceTypeIds.Organizes, false, spatialObject.NodeId);
            spatialObject.AddReference(ReferenceTypeIds.Organizes, true, list.NodeId);
            m_spatialObjectLists[spatialObject] = list;
            m_pendingModelChanges[spatialObject] = list;
            return spatialObject;
        }

        /// <summary>
        /// Adds a concrete Cartesian attach-point frame below a spatial object.
        /// </summary>
        public CartesianFrameAngleOrientationState AddAttachPoint(
            SpatialObjectState spatialObject,
            QualifiedName browseName,
            NodeId baseFrameId,
            ThreeDFrame frame,
            EUInformation lengthUnit,
            EUInformation angleUnit)
        {
            return AddFrame(
                spatialObject,
                SpatialFrameCollection.AttachPoints,
                browseName,
                baseFrameId,
                frame,
                lengthUnit,
                angleUnit);
        }

        /// <summary>
        /// Adds a concrete Cartesian internal frame below a spatial object.
        /// </summary>
        public CartesianFrameAngleOrientationState AddInternalFrame(
            SpatialObjectState spatialObject,
            QualifiedName browseName,
            NodeId baseFrameId,
            ThreeDFrame frame,
            EUInformation lengthUnit,
            EUInformation angleUnit)
        {
            return AddFrame(
                spatialObject,
                SpatialFrameCollection.InternalFrames,
                browseName,
                baseFrameId,
                frame,
                lengthUnit,
                angleUnit);
        }

        /// <summary>
        /// Adds a concrete Cartesian alternative frame below a spatial object.
        /// </summary>
        public CartesianFrameAngleOrientationState AddAlternativeFrame(
            SpatialObjectState spatialObject,
            QualifiedName browseName,
            NodeId baseFrameId,
            ThreeDFrame frame,
            EUInformation lengthUnit,
            EUInformation angleUnit)
        {
            return AddFrame(
                spatialObject,
                SpatialFrameCollection.AlternativeFrames,
                browseName,
                baseFrameId,
                frame,
                lengthUnit,
                angleUnit);
        }

        /// <summary>
        /// Creates a GPOS Zone and links it from the standardized GlobalLocations entry point.
        /// </summary>
        /// <exception cref="ArgumentException"></exception>
        public ZoneState CreateZone(
            QualifiedName browseName,
            string zoneId,
            ArrayOf<GroundControlPointDataType> groundControlPoints)
        {
            if (string.IsNullOrWhiteSpace(zoneId))
            {
                throw new ArgumentException(
                    "A stable ZoneId is required.",
                    nameof(zoneId));
            }
            ValidateGroundControlPoints(groundControlPoints);

            FolderState root = GetGlobalLocations();
            EnsureUniqueChild(root, browseName);
            ZoneState zone = SystemContext.CreateInstanceOfZoneType(root, browseName);
            zone.ReferenceTypeId = ReferenceTypeIds.Organizes;
            root.AddChild(zone);
            zone.ZoneId!.Value = zoneId;

            if (groundControlPoints.Count > 0)
            {
                zone.AddGroundControlPoints(SystemContext);
                zone.GroundControlPoints!.Value = groundControlPoints;
            }

            return zone;
        }

        /// <summary>
        /// Creates a proximity-based GPOS Zone around a geographic position.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        public ZoneState CreateProximityZone(
            QualifiedName browseName,
            string zoneId,
            S3DGeographicCoordinateDataType position,
            double radius)
        {
            position.ThrowIfNull(nameof(position));
            if (!radius.IsFinite() || radius <= 0.0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(radius),
                    radius,
                    "The Zone radius must be finite and greater than zero.");
            }

            ValidateGeographicPosition(position, coordinateReferenceSystem: 4326);
            ZoneState zone = CreateZone(
                browseName,
                zoneId,
                default);
            zone.AddPosition(SystemContext);
            zone.Position!.Value = position;
            zone.AddRadius(SystemContext);
            zone.Radius!.Value = radius;
            return zone;
        }

        /// <summary>
        /// Attaches a GlobalPosition Variable to an existing tracked object.
        /// </summary>
        public GlobalPositionState AttachGlobalPosition(
            NodeState owner,
            QualifiedName browseName,
            NodeId sourceId,
            uint coordinateReferenceSystem)
        {
            owner.ThrowIfNull(nameof(owner));
            ValidateSourceNodeId(sourceId, nameof(sourceId));
            EnsureUniqueChild(owner, browseName);

            GlobalPositionState position = SystemContext
                .CreateInstanceOfGlobalPositionType(owner, browseName);
            position.ReferenceTypeId = ReferenceTypeIds.HasComponent;
            owner.AddChild(position);
            ConfigureGlobalPositionMetadata(
                position,
                sourceId,
                coordinateReferenceSystem);
            return position;
        }

        /// <summary>
        /// Attaches a GlobalLocation Variable to an existing tracked object.
        /// </summary>
        public GlobalLocationState AttachGlobalLocation(
            NodeState owner,
            QualifiedName browseName,
            NodeId sourceId,
            uint coordinateReferenceSystem,
            bool includeOrientation = true)
        {
            owner.ThrowIfNull(nameof(owner));
            ValidateSourceNodeId(sourceId, nameof(sourceId));
            EnsureUniqueChild(owner, browseName);

            GlobalLocationState location = SystemContext
                .CreateInstanceOfGlobalLocationType(owner, browseName);
            location.ReferenceTypeId = ReferenceTypeIds.HasComponent;
            owner.AddChild(location);
            location.Base!.Value = NodeId.Null;
            ConfigureGlobalPositionMetadata(
                location.Position!,
                sourceId,
                coordinateReferenceSystem);

            if (includeOrientation)
            {
                location.AddOrientation(SystemContext);
            }

            return location;
        }

        /// <summary>
        /// Applies a typed provider sample to a GlobalPosition Variable and its children.
        /// </summary>
        public void SetGlobalPositionValue(
            GlobalPositionState state,
            GlobalPositionSample sample)
        {
            state.ThrowIfNull(nameof(state));
            sample.ThrowIfNull(nameof(sample));

            GlobalPositionDataType position = sample.Location.Position;
            ValidateGlobalPosition(state, position);
            state.Value = position;
            state.StatusCode = sample.StatusCode;
            state.Timestamp = sample.SourceTimestamp;
            SetGlobalPositionComponents(
                state,
                position,
                sample.StatusCode,
                sample.SourceTimestamp);
            state.ClearChangeMasks(SystemContext, includeChildren: true);
        }

        /// <summary>
        /// Binds a GlobalPosition Variable to a provider source.
        /// </summary>
        public async ValueTask<PositioningProviderSubscription>
            BindGlobalPositionAsync(
                GlobalPositionState state,
                IGlobalPositionProvider provider,
                string sourceId,
                CancellationToken cancellationToken = default)
        {
            state.ThrowIfNull(nameof(state));
            provider.ThrowIfNull(nameof(provider));
            ValidateRequestedSourceId(sourceId, nameof(sourceId));

            try
            {
                GlobalPositionSample initial = await provider.ReadAsync(
                    sourceId,
                    cancellationToken).ConfigureAwait(false);
                ValidateProviderSourceMatch(initial.SourceId, sourceId);
                SetGlobalPositionValue(state, initial);
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                MarkGlobalPositionUnavailable(state);
                state.ClearChangeMasks(SystemContext, includeChildren: true);
                m_logger.GlobalProviderFailed(sourceId, ex);
                throw;
            }

            var cts = new CancellationTokenSource();
            Task completion = WatchGlobalPositionValueAsync(
                state,
                provider,
                sourceId,
                cts.Token);
            return new PositioningProviderSubscription(cts, completion);
        }

        /// <summary>
        /// Applies a typed provider sample to a GlobalLocation Variable and its children.
        /// </summary>
        public void SetGlobalLocationValue(
            GlobalLocationState state,
            GlobalPositionSample sample)
        {
            state.ThrowIfNull(nameof(state));
            sample.ThrowIfNull(nameof(sample));

            GlobalLocationDataType value = sample.Location;
            GlobalPositionDataType position = value.Position;
            ValidateGlobalLocation(state, value);
            state.Value = value;
            state.StatusCode = sample.StatusCode;
            state.Timestamp = sample.SourceTimestamp;

            state.Position!.Value = position;
            state.Position.StatusCode = sample.StatusCode;
            state.Position.Timestamp = sample.SourceTimestamp;
            SetGlobalPositionComponents(
                state.Position,
                position,
                sample.StatusCode,
                sample.SourceTimestamp);

            if ((value.EncodingMask &
                (uint)GlobalLocationDataTypeFields.Orientation) != 0)
            {
                state.AddOrientation(SystemContext);
                state.Orientation!.Value = value.Orientation;
                state.Orientation.StatusCode = sample.StatusCode;
                state.Orientation.Timestamp = sample.SourceTimestamp;
                SetVariable(
                    state.Orientation.A!,
                    value.Orientation.A,
                    sample.StatusCode,
                    sample.SourceTimestamp);
                SetVariable(
                    state.Orientation.B!,
                    value.Orientation.B,
                    sample.StatusCode,
                    sample.SourceTimestamp);
                SetVariable(
                    state.Orientation.C!,
                    value.Orientation.C,
                    sample.StatusCode,
                    sample.SourceTimestamp);
            }
            else if (state.Orientation != null)
            {
                MarkNoData(state.Orientation, sample.SourceTimestamp);
                MarkNoData(state.Orientation.A, sample.SourceTimestamp);
                MarkNoData(state.Orientation.B, sample.SourceTimestamp);
                MarkNoData(state.Orientation.C, sample.SourceTimestamp);
            }

            state.ClearChangeMasks(SystemContext, includeChildren: true);
        }

        /// <summary>
        /// Binds a GlobalLocation Variable to a provider source.
        /// </summary>
        public async ValueTask<PositioningProviderSubscription>
            BindGlobalLocationAsync(
                GlobalLocationState state,
                IGlobalPositionProvider provider,
                string sourceId,
                Func<GlobalPositionSample, CancellationToken, ValueTask>?
                    onSampleApplied = null,
                CancellationToken cancellationToken = default)
        {
            state.ThrowIfNull(nameof(state));
            provider.ThrowIfNull(nameof(provider));
            ValidateRequestedSourceId(sourceId, nameof(sourceId));

            try
            {
                GlobalPositionSample initial = await provider.ReadAsync(
                    sourceId,
                    cancellationToken).ConfigureAwait(false);
                ApplyGlobalSample(state, initial, sourceId);
                if (onSampleApplied != null)
                {
                    await onSampleApplied(initial, cancellationToken)
                        .ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                MarkGlobalLocationUnavailable(state);
                m_logger.GlobalProviderFailed(sourceId, ex);
                throw;
            }

            var cts = new CancellationTokenSource();
            Task completion = WatchGlobalPositionAsync(
                state,
                provider,
                sourceId,
                onSampleApplied,
                cts.Token);
            return new PositioningProviderSubscription(cts, completion);
        }

        /// <summary>
        /// Binds an RSL Cartesian frame to a provider source.
        /// </summary>
        public async ValueTask<PositioningProviderSubscription>
            BindRelativeSpatialLocationAsync(
                CartesianFrameAngleOrientationState state,
                IRelativeSpatialLocationProvider provider,
                string sourceId,
                CancellationToken cancellationToken = default)
        {
            state.ThrowIfNull(nameof(state));
            provider.ThrowIfNull(nameof(provider));
            ValidateRequestedSourceId(sourceId, nameof(sourceId));

            try
            {
                RelativeSpatialLocationSample initial = await provider.ReadAsync(
                    sourceId,
                    cancellationToken).ConfigureAwait(false);
                ValidateProviderSourceMatch(initial.SourceId, sourceId);
                SetFrameValue(
                    state,
                    initial.Frame,
                    initial.StatusCode,
                    initial.SourceTimestamp);
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                MarkFrameUnavailable(state);
                m_logger.RelativeProviderFailed(sourceId, ex);
                throw;
            }

            var cts = new CancellationTokenSource();
            Task completion = WatchRelativeSpatialLocationAsync(
                state,
                provider,
                sourceId,
                cts.Token);
            return new PositioningProviderSubscription(cts, completion);
        }

        /// <summary>
        /// Creates a concrete Cartesian frame and synchronizes its aggregate and child values.
        /// </summary>
        public CartesianFrameAngleOrientationState CreateCartesianFrame(
            NodeState parent,
            QualifiedName browseName,
            NodeId baseFrameId,
            ThreeDFrame frame,
            EUInformation lengthUnit,
            EUInformation angleUnit)
        {
            parent.ThrowIfNull(nameof(parent));
            frame.ThrowIfNull(nameof(frame));
            lengthUnit.ThrowIfNull(nameof(lengthUnit));
            angleUnit.ThrowIfNull(nameof(angleUnit));
            ValidateFrame(frame, nameof(frame));

            CartesianFrameAngleOrientationState state = SystemContext
                .CreateInstanceOfCartesianFrameAngleOrientationType(
                    parent,
                    browseName);
            state.ReferenceTypeId = ReferenceTypeIds.HasComponent;
            state.Base!.Value = baseFrameId;
            SetFrameValue(state, frame, StatusCodes.Good, DateTimeUtc.Now);
            state.Position!.LengthUnit!.Value = lengthUnit;
            state.Orientation!.AngleUnit!.Value = angleUnit;
            return state;
        }

        /// <summary>
        /// Updates a Cartesian frame and all mandatory child Variables coherently.
        /// </summary>
        public void SetFrameValue(
            CartesianFrameAngleOrientationState state,
            ThreeDFrame frame,
            StatusCode statusCode,
            DateTimeUtc sourceTimestamp)
        {
            state.ThrowIfNull(nameof(state));
            frame.ThrowIfNull(nameof(frame));

            state.Value = frame;
            state.StatusCode = statusCode;
            state.Timestamp = sourceTimestamp;
            state.Position!.Value = frame.CartesianCoordinates;
            state.Position.StatusCode = statusCode;
            state.Position.Timestamp = sourceTimestamp;
            SetVariable(
                state.Position.X!,
                frame.CartesianCoordinates.X,
                statusCode,
                sourceTimestamp);
            SetVariable(
                state.Position.Y!,
                frame.CartesianCoordinates.Y,
                statusCode,
                sourceTimestamp);
            SetVariable(
                state.Position.Z!,
                frame.CartesianCoordinates.Z,
                statusCode,
                sourceTimestamp);
            state.Orientation!.Value = frame.Orientation;
            state.Orientation.StatusCode = statusCode;
            state.Orientation.Timestamp = sourceTimestamp;
            SetVariable(
                state.Orientation.A!,
                frame.Orientation.A,
                statusCode,
                sourceTimestamp);
            SetVariable(
                state.Orientation.B!,
                frame.Orientation.B,
                statusCode,
                sourceTimestamp);
            SetVariable(
                state.Orientation.C!,
                frame.Orientation.C,
                statusCode,
                sourceTimestamp);
            state.ClearChangeMasks(SystemContext, includeChildren: true);
        }

        private ushort RslNamespaceIndex =>
            (ushort)NodeManager.Server.NamespaceUris.GetIndex(
                Rsl.Namespaces.RSL);

        private FolderState GetRelativeSpatialLocations()
        {
            var nodeId = NodeId.Create(
                Rsl.Objects.RelativeSpatialLocations,
                Rsl.Namespaces.RSL,
                NodeManager.Server.NamespaceUris);
            return NodeManager.FindPredefinedNode<FolderState>(nodeId) ??
                throw new ServiceResultException(
                    StatusCodes.BadNodeIdUnknown,
                    "The RSL RelativeSpatialLocations entry point is not loaded.");
        }

        private FolderState GetGlobalLocations()
        {
            var nodeId = NodeId.Create(
                Gpos.Objects.GlobalLocations,
                Gpos.Namespaces.GPOS,
                NodeManager.Server.NamespaceUris);
            return NodeManager.FindPredefinedNode<FolderState>(nodeId) ??
                throw new ServiceResultException(
                    StatusCodes.BadNodeIdUnknown,
                    "The GPOS GlobalLocations entry point is not loaded.");
        }

        private static void IncrementNodeVersion(SpatialObjectsListState list)
        {
            _ = int.TryParse(
                list.NodeVersion?.Value,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out int value);
            list.NodeVersion!.Value = (value + 1).ToString(
                CultureInfo.InvariantCulture);
        }

        private void ApplyGlobalSample(
            GlobalLocationState state,
            GlobalPositionSample sample,
            string sourceId)
        {
            ValidateProviderSourceMatch(sample.SourceId, sourceId);
            SetGlobalLocationValue(state, sample);
        }

        private async Task WatchGlobalPositionAsync(
            GlobalLocationState state,
            IGlobalPositionProvider provider,
            string sourceId,
            Func<GlobalPositionSample, CancellationToken, ValueTask>?
                onSampleApplied,
            CancellationToken cancellationToken)
        {
            try
            {
                await foreach (GlobalPositionSample sample in provider
                    .WatchAsync(sourceId, cancellationToken)
                    .WithCancellation(cancellationToken)
                    .ConfigureAwait(false))
                {
                    ApplyGlobalSample(state, sample, sourceId);
                    if (onSampleApplied != null)
                    {
                        await onSampleApplied(sample, cancellationToken)
                            .ConfigureAwait(false);
                    }
                }
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                MarkGlobalLocationUnavailable(state);
                m_logger.GlobalProviderFailed(sourceId, ex);
                throw;
            }
        }

        private async Task WatchGlobalPositionValueAsync(
            GlobalPositionState state,
            IGlobalPositionProvider provider,
            string sourceId,
            CancellationToken cancellationToken)
        {
            try
            {
                await foreach (GlobalPositionSample sample in provider
                    .WatchAsync(sourceId, cancellationToken)
                    .WithCancellation(cancellationToken)
                    .ConfigureAwait(false))
                {
                    ValidateProviderSourceMatch(sample.SourceId, sourceId);
                    SetGlobalPositionValue(state, sample);
                }
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                MarkGlobalPositionUnavailable(state);
                state.ClearChangeMasks(SystemContext, includeChildren: true);
                m_logger.GlobalProviderFailed(sourceId, ex);
                throw;
            }
        }

        private async Task WatchRelativeSpatialLocationAsync(
            CartesianFrameAngleOrientationState state,
            IRelativeSpatialLocationProvider provider,
            string sourceId,
            CancellationToken cancellationToken)
        {
            try
            {
                await foreach (RelativeSpatialLocationSample sample in provider
                    .WatchAsync(sourceId, cancellationToken)
                    .WithCancellation(cancellationToken)
                    .ConfigureAwait(false))
                {
                    ValidateProviderSourceMatch(sample.SourceId, sourceId);
                    SetFrameValue(
                        state,
                        sample.Frame,
                        sample.StatusCode,
                        sample.SourceTimestamp);
                }
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                MarkFrameUnavailable(state);
                m_logger.RelativeProviderFailed(sourceId, ex);
                throw;
            }
        }

        private CartesianFrameAngleOrientationState AddFrame(
            SpatialObjectState spatialObject,
            SpatialFrameCollection collection,
            QualifiedName browseName,
            NodeId baseFrameId,
            ThreeDFrame frame,
            EUInformation lengthUnit,
            EUInformation angleUnit)
        {
            spatialObject.ThrowIfNull(nameof(spatialObject));
            ValidateSourceNodeId(baseFrameId, nameof(baseFrameId));

            FolderState folder = collection switch
            {
                SpatialFrameCollection.AlternativeFrames =>
                    GetAlternativeFrames(spatialObject),
                SpatialFrameCollection.AttachPoints =>
                    GetAttachPoints(spatialObject),
                SpatialFrameCollection.InternalFrames =>
                    GetInternalFrames(spatialObject),
                _ => throw new ArgumentOutOfRangeException(nameof(collection))
            };
            EnsureUniqueChild(folder, browseName);

            CartesianFrameAngleOrientationState child = CreateCartesianFrame(
                folder,
                browseName,
                baseFrameId,
                frame,
                lengthUnit,
                angleUnit);
            child.ReferenceTypeId = ReferenceTypeIds.HasComponent;
            folder.AddChild(child);
            TrackModelChange(child, spatialObject);
            return child;
        }

        private void SynchronizeFrameSubtree(NodeState node)
        {
            if (node is CartesianFrameAngleOrientationState frameState &&
                frameState.Value != null)
            {
                SetFrameValue(
                    frameState,
                    frameState.Value,
                    frameState.StatusCode,
                    frameState.Timestamp);
            }

            var children = new List<BaseInstanceState>();
            node.GetChildren(SystemContext, children);
            for (int i = 0; i < children.Count; i++)
            {
                SynchronizeFrameSubtree(children[i]);
            }
        }

        private FolderState GetAlternativeFrames(SpatialObjectState spatialObject)
        {
            spatialObject.AddAlternativeFrames(SystemContext);
            return spatialObject.AlternativeFrames!;
        }

        private FolderState GetAttachPoints(SpatialObjectState spatialObject)
        {
            spatialObject.AddAttachPoints(SystemContext);
            return spatialObject.AttachPoints!;
        }

        private FolderState GetInternalFrames(SpatialObjectState spatialObject)
        {
            spatialObject.AddInternalFrames(SystemContext);
            return spatialObject.InternalFrames!;
        }

        private void TrackModelChange(
            NodeState node,
            SpatialObjectState spatialObject)
        {
            if (NodeManager.Find(spatialObject.NodeId) != null &&
                m_spatialObjectLists.TryGetValue(
                    spatialObject,
                    out SpatialObjectsListState? list))
            {
                m_pendingModelChanges[node] = list;
            }
        }

        private static void ConfigureGlobalPositionMetadata(
            GlobalPositionState position,
            NodeId sourceId,
            uint coordinateReferenceSystem)
        {
            position.SourceId!.Value = sourceId;
            position.CoordinateReferenceSystem!.Value =
                coordinateReferenceSystem;
        }

        private void SetGlobalPositionComponents(
            GlobalPositionState state,
            GlobalPositionDataType position,
            StatusCode statusCode,
            DateTimeUtc sourceTimestamp)
        {
            SetVariable(
                state.Longitude!,
                position.Longitude,
                statusCode,
                sourceTimestamp);
            SetVariable(
                state.Latitude!,
                position.Latitude,
                statusCode,
                sourceTimestamp);

            if ((position.EncodingMask &
                (uint)S3DGeographicCoordinateDataTypeFields.Elevation) != 0)
            {
                state.AddElevation(SystemContext);
                SetVariable(
                    state.Elevation!,
                    position.Elevation,
                    statusCode,
                    sourceTimestamp);
            }
            else
            {
                MarkNoData(state.Elevation, sourceTimestamp);
            }

            if ((position.EncodingMask &
                (uint)GlobalPositionDataTypeFields.Accuracy) != 0)
            {
                state.AddAccuracy(SystemContext);
                SetVariable(
                    state.Accuracy!,
                    position.Accuracy,
                    statusCode,
                    sourceTimestamp);
            }
            else
            {
                MarkNoData(state.Accuracy, sourceTimestamp);
            }

            if ((position.EncodingMask &
                (uint)GlobalPositionDataTypeFields.Floor) != 0)
            {
                state.AddFloor(SystemContext);
                SetVariable(
                    state.Floor!,
                    position.Floor,
                    statusCode,
                    sourceTimestamp);
            }
            else
            {
                MarkNoData(state.Floor, sourceTimestamp);
            }
        }

        private void SetVariable<T>(
            BaseDataVariableState<T> state,
            T value,
            StatusCode statusCode,
            DateTimeUtc sourceTimestamp)
        {
            state.Value = value;
            state.StatusCode = statusCode;
            state.Timestamp = sourceTimestamp;

            if (!state.NodeId.IsNull &&
                NodeManager.Find(state.NodeId) is BaseDataVariableState<T>
                    registeredState &&
                !ReferenceEquals(state, registeredState))
            {
                registeredState.Value = value;
                registeredState.StatusCode = statusCode;
                registeredState.Timestamp = sourceTimestamp;
                registeredState.ClearChangeMasks(
                    SystemContext,
                    includeChildren: false);
            }
        }

        private void MarkNoData(
            BaseVariableState? state,
            DateTimeUtc sourceTimestamp)
        {
            SetVariableStatus(
                state,
                StatusCodes.BadNoData,
                sourceTimestamp);
        }

        private void MarkGlobalLocationUnavailable(GlobalLocationState state)
        {
            MarkUnavailable(state);
            MarkGlobalPositionUnavailable(state.Position!);
            MarkUnavailable(state.Orientation);
            MarkUnavailable(state.Orientation?.A);
            MarkUnavailable(state.Orientation?.B);
            MarkUnavailable(state.Orientation?.C);
            state.ClearChangeMasks(SystemContext, includeChildren: true);
        }

        private void MarkGlobalPositionUnavailable(GlobalPositionState state)
        {
            MarkUnavailable(state);
            MarkUnavailable(state.Longitude);
            MarkUnavailable(state.Latitude);
            MarkUnavailable(state.Elevation);
            MarkUnavailable(state.Accuracy);
            MarkUnavailable(state.Floor);
        }

        private void MarkFrameUnavailable(
            CartesianFrameAngleOrientationState state)
        {
            MarkUnavailable(state);
            MarkUnavailable(state.Position);
            MarkUnavailable(state.Position?.X);
            MarkUnavailable(state.Position?.Y);
            MarkUnavailable(state.Position?.Z);
            MarkUnavailable(state.Orientation);
            MarkUnavailable(state.Orientation?.A);
            MarkUnavailable(state.Orientation?.B);
            MarkUnavailable(state.Orientation?.C);
            state.ClearChangeMasks(SystemContext, includeChildren: true);
        }

        private void MarkUnavailable(BaseVariableState? state)
        {
            if (state != null)
            {
                SetVariableStatus(
                    state,
                    StatusCodes.BadCommunicationError,
                    state.Timestamp);
            }
        }

        private void SetVariableStatus(
            BaseVariableState? state,
            StatusCode statusCode,
            DateTimeUtc sourceTimestamp)
        {
            if (state == null)
            {
                return;
            }

            state.StatusCode = statusCode;
            state.Timestamp = sourceTimestamp;

            if (!state.NodeId.IsNull &&
                NodeManager.Find(state.NodeId) is BaseVariableState
                    registeredState &&
                !ReferenceEquals(state, registeredState))
            {
                registeredState.StatusCode = statusCode;
                registeredState.Timestamp = sourceTimestamp;
                registeredState.ClearChangeMasks(
                    SystemContext,
                    includeChildren: false);
            }
        }

        private static void ValidateGlobalLocation(
            GlobalLocationState state,
            GlobalLocationDataType location)
        {
            location.Position.ThrowIfNull(nameof(location.Position));
            ValidateGlobalPosition(state.Position!, location.Position);
            if ((location.EncodingMask &
                (uint)GlobalLocationDataTypeFields.Orientation) != 0)
            {
                ThreeDOrientation orientation = location.Orientation;
                if (!orientation.A.IsFinite() ||
                    !orientation.B.IsFinite() ||
                    !orientation.C.IsFinite())
                {
                    throw new ServiceResultException(
                        StatusCodes.BadOutOfRange,
                        "The global orientation contains a non-finite angle.");
                }
            }
        }

        private static void ValidateGlobalPosition(
            GlobalPositionState state,
            GlobalPositionDataType position)
        {
            position.ThrowIfNull(nameof(position));
            uint coordinateReferenceSystem =
                state.CoordinateReferenceSystem?.Value ??
                throw new ServiceResultException(
                    StatusCodes.BadConfigurationError,
                    "CoordinateReferenceSystem is not configured.");
            ValidateGeographicPosition(position, coordinateReferenceSystem);

            if ((position.EncodingMask &
                (uint)GlobalPositionDataTypeFields.Accuracy) != 0 &&
                (!position.Accuracy.IsFinite() ||
                    position.Accuracy < 0.0))
            {
                throw new ServiceResultException(
                    StatusCodes.BadOutOfRange,
                    "Accuracy must be finite and non-negative.");
            }

            if ((position.EncodingMask &
                (uint)GlobalPositionDataTypeFields.Floor) != 0 &&
                (float.IsNaN(position.Floor) ||
                    float.IsInfinity(position.Floor)))
            {
                throw new ServiceResultException(
                    StatusCodes.BadOutOfRange,
                    "Floor must be finite.");
            }
        }

        private static void ValidateGeographicPosition(
            S3DGeographicCoordinateDataType position,
            uint coordinateReferenceSystem)
        {
            if (!position.Longitude.IsFinite() ||
                !position.Latitude.IsFinite())
            {
                throw new ServiceResultException(
                    StatusCodes.BadOutOfRange,
                    "The global position contains a non-finite coordinate.");
            }

            if (coordinateReferenceSystem == 4326 &&
                (position.Longitude < -180.0 ||
                    position.Longitude > 180.0 ||
                    position.Latitude < -90.0 ||
                    position.Latitude > 90.0))
            {
                throw new ServiceResultException(
                    StatusCodes.BadOutOfRange,
                    "EPSG:4326 longitude or latitude is outside its valid range.");
            }

            if ((position.EncodingMask &
                (uint)S3DGeographicCoordinateDataTypeFields.Elevation) != 0 &&
                !position.Elevation.IsFinite())
            {
                throw new ServiceResultException(
                    StatusCodes.BadOutOfRange,
                    "Elevation must be finite when present.");
            }
        }

        private static void ValidateGroundControlPoints(
            ArrayOf<GroundControlPointDataType> controlPoints)
        {
            if (controlPoints.IsNull)
            {
                return;
            }

            for (int i = 0; i < controlPoints.Count; i++)
            {
                GroundControlPointDataType controlPoint = controlPoints[i] ??
                    throw new ArgumentException(
                        $"Ground control point at index {i} is null.",
                        nameof(controlPoints));
                ThreeDCartesianCoordinates local =
                    controlPoint.LocalPosition ??
                    throw new ArgumentException(
                        $"Ground control point at index {i} has no local position.",
                        nameof(controlPoints));
                if (!local.X.IsFinite() ||
                    !local.Y.IsFinite() ||
                    !local.Z.IsFinite())
                {
                    throw new ArgumentException(
                        $"Ground control point at index {i} has a non-finite local position.",
                        nameof(controlPoints));
                }
                ValidateGeographicPosition(
                    controlPoint.GlobalPosition ??
                    throw new ArgumentException(
                        $"Ground control point at index {i} has no global position.",
                        nameof(controlPoints)),
                    coordinateReferenceSystem: 4326);
            }
        }

        private static void ValidateFrame(
            ThreeDFrame frame,
            string parameterName)
        {
            ThreeDCartesianCoordinates position =
                frame.CartesianCoordinates ??
                throw new ArgumentException(
                    "The frame has no CartesianCoordinates.",
                    parameterName);
            ThreeDOrientation orientation =
                frame.Orientation ??
                throw new ArgumentException(
                    "The frame has no Orientation.",
                    parameterName);
            if (!position.X.IsFinite() ||
                !position.Y.IsFinite() ||
                !position.Z.IsFinite() ||
                !orientation.A.IsFinite() ||
                !orientation.B.IsFinite() ||
                !orientation.C.IsFinite())
            {
                throw new ArgumentException(
                    "The frame contains a non-finite coordinate or angle.",
                    parameterName);
            }
        }

        private static void ValidateSourceNodeId(
            NodeId sourceId,
            string parameterName)
        {
            if (sourceId.IsNull)
            {
                throw new ArgumentException(
                    "A non-null source or Base NodeId is required.",
                    parameterName);
            }
        }

        private static void ValidateRequestedSourceId(
            string sourceId,
            string parameterName)
        {
            if (string.IsNullOrWhiteSpace(sourceId))
            {
                throw new ArgumentException(
                    "A stable provider source identifier is required.",
                    parameterName);
            }
        }

        private static void ValidateProviderSourceMatch(
            string actualSourceId,
            string expectedSourceId)
        {
            ValidateRequestedSourceId(actualSourceId, nameof(actualSourceId));
            if (!string.Equals(
                actualSourceId,
                expectedSourceId,
                StringComparison.Ordinal))
            {
                throw new ServiceResultException(
                    StatusCodes.BadInvalidArgument,
                    $"Provider source '{actualSourceId}' does not match requested source " +
                    $"'{expectedSourceId}'.");
            }
        }

        private void EnsureUniqueChild(
            NodeState parent,
            QualifiedName browseName)
        {
            var children = new List<BaseInstanceState>();
            parent.GetChildren(SystemContext, children);
            for (int i = 0; i < children.Count; i++)
            {
                if (children[i].BrowseName == browseName)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadBrowseNameDuplicated,
                        $"A child named '{browseName}' already exists below " +
                        $"'{parent.NodeId}'.");
                }
            }
        }

        private void ReportModelChange(
            BaseInstanceState affected,
            ModelChangeVerbs verb)
        {
            var modelChange = new GeneralModelChangeEventState(null);
            var message = new TranslationInfo(
                "PositioningModelChange",
                "en-US",
                "Positioning address space changed.");
            modelChange.Initialize(
                SystemContext,
                null,
                EventSeverity.Low,
                new LocalizedText(message));
            modelChange.SetChildValue(
                SystemContext,
                BrowseNames.SourceNode,
                ObjectIds.Server,
                false);
            modelChange.SetChildValue(
                SystemContext,
                BrowseNames.SourceName,
                "Server",
                false);
            modelChange.CreateOrReplaceChanges(SystemContext, null!);
            modelChange.Changes!.Value =
            [
                new ModelChangeStructureDataType
                {
                    Affected = affected.NodeId,
                    AffectedType = affected.TypeDefinitionId,
                    Verb = (byte)verb
                }
            ];
            NodeManager.Server.ReportEvent(modelChange);
        }

        private enum SpatialFrameCollection
        {
            AlternativeFrames,
            AttachPoints,
            InternalFrames
        }
    }

    internal static partial class PositioningAddressSpaceBuilderLog
    {
        [LoggerMessage(
            EventId = PositioningServerEventIds.GlobalProviderFailed,
            Level = LogLevel.Error,
            Message = "Global positioning provider source {SourceId} stopped.")]
        public static partial void GlobalProviderFailed(
            this ILogger logger,
            string sourceId,
            Exception exception);

        [LoggerMessage(
            EventId = PositioningServerEventIds.RelativeProviderFailed,
            Level = LogLevel.Error,
            Message = "Relative spatial location provider source {SourceId} stopped.")]
        public static partial void RelativeProviderFailed(
            this ILogger logger,
            string sourceId,
            Exception exception);
    }
}
