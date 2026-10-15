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
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Client;
using Opc.Ua.Client.Subscriptions;
using Opc.Ua.Client.Subscriptions.Streaming;
using Opc.Ua.Gpos;
using Opc.Ua.Rsl;
using MonitoringOptions =
    Opc.Ua.Client.Subscriptions.MonitoredItems.MonitoredItemOptions;

namespace Opc.Ua.Positioning.Client
{
    /// <summary>
    /// High-level client for the OPC 10000-211 Global Positioning model.
    /// </summary>
    public sealed class GlobalPositioningClient
    {
        /// <summary>
        /// Creates a GPOS client.
        /// </summary>
        public GlobalPositioningClient(
            ISession session,
            ITelemetryContext telemetry)
        {
            m_operations = new PositioningClientOperations(session, telemetry);
            RegisterTypes();
        }

        /// <summary>
        /// Creates a GPOS client with strict reads and bounded discovery and structure arrays.
        /// The supplied session remains owned by the caller.
        /// </summary>
        public GlobalPositioningClient(
            ISession session,
            ITelemetryContext telemetry,
            PositioningClientReadOptions options)
        {
            options.ThrowIfNull(nameof(options));
            m_operations = new PositioningClientOperations(session, telemetry, options);
            RegisterTypes();
        }

        /// <summary>
        /// OPC UA session used by this client.
        /// </summary>
        public ISession Session => m_operations.Session;

        /// <summary>
        /// Telemetry context used by generated proxies.
        /// </summary>
        public ITelemetryContext Telemetry => m_operations.Telemetry;

        /// <summary>
        /// Standardized GlobalLocations entry point.
        /// </summary>
        public NodeId GlobalLocationsId => m_operations.Strict
            ? new NodeId(Gpos.Objects.GlobalLocations, m_operations.GetNamespaceIndex(Gpos.Namespaces.GPOS))
            : NodeId.Create(Gpos.Objects.GlobalLocations, Gpos.Namespaces.GPOS, Session.NamespaceUris);

        /// <summary>
        /// Enumerates Zones organized by GlobalLocations.
        /// </summary>
        public async IAsyncEnumerable<PositioningObjectEntry> EnumerateZonesAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            ArrayOf<ReferenceDescription> references = await m_operations.BrowseAsync(
                GlobalLocationsId,
                ReferenceTypeIds.Organizes,
                (uint)NodeClass.Object,
                cancellationToken).ConfigureAwait(false);

            ExpandedNodeId expectedType = Opc.Ua.Gpos.ObjectTypeIds.ZoneType;
            for (int i = 0; i < references.Count; i++)
            {
                ReferenceDescription reference = references[i];
                if (await Session.NodeCache.IsTypeOfAsync(
                    reference.TypeDefinition,
                    expectedType,
                    cancellationToken).ConfigureAwait(false))
                {
                    yield return m_operations.CreateEntry(reference);
                }
            }
        }

        /// <summary>
        /// Opens the generated proxy for a Zone.
        /// </summary>
        public ZoneTypeClient OpenZone(NodeId nodeId)
        {
            return new ZoneTypeClient(Session, nodeId, Telemetry);
        }

        /// <summary>
        /// Reads a GlobalPositionType Variable and its projection metadata.
        /// </summary>
        public async ValueTask<GlobalPositionValue> ReadGlobalPositionAsync(
            NodeId nodeId,
            CancellationToken cancellationToken = default)
        {
            DataValue dataValue = await m_operations.ReadValueAsync(
                nodeId,
                cancellationToken).ConfigureAwait(false);
            GlobalPositionDataType position = m_operations.DecodeStructure<GlobalPositionDataType>(dataValue);
            (
                NodeId sourceId,
                uint coordinateReferenceSystem
            ) = await ReadPositionMetadataAsync(
                nodeId,
                cancellationToken).ConfigureAwait(false);
            m_operations.ValidateGlobalPosition(position, coordinateReferenceSystem);
            return new GlobalPositionValue(
                nodeId,
                position,
                sourceId,
                coordinateReferenceSystem,
                dataValue.StatusCode,
                dataValue.SourceTimestamp);
        }

        /// <summary>
        /// Reads a GlobalLocationType Variable.
        /// </summary>
        public async ValueTask<GlobalLocationValue> ReadGlobalLocationAsync(
            NodeId nodeId,
            CancellationToken cancellationToken = default)
        {
            ushort rslNamespaceIndex = m_operations.GetNamespaceIndex(
                Opc.Ua.Rsl.Namespaces.RSL);
            NodeId positionId = await m_operations.ResolveRequiredChildAsync(
                nodeId,
                ReferenceTypeIds.HasComponent,
                new QualifiedName("Position", rslNamespaceIndex),
                cancellationToken).ConfigureAwait(false);

            DataValue dataValue = DataValue.Null;
            GlobalLocationDataType location = null!;
            bool coherent = false;
            for (int attempt = 0; attempt < 3; attempt++)
            {
                dataValue = await m_operations.ReadValueAsync(
                    nodeId,
                    cancellationToken).ConfigureAwait(false);
                location = m_operations.DecodeStructure<GlobalLocationDataType>(dataValue);
                m_operations.ValidateGlobalLocation(location);
                GlobalPositionDataType componentPosition =
                    await m_operations.ReadStructureAsync<GlobalPositionDataType>(
                        positionId,
                        cancellationToken).ConfigureAwait(false);
                if (location.Position.IsEqual(componentPosition))
                {
                    coherent = true;
                    break;
                }
                await Task.Yield();
            }
            if (!coherent)
            {
                throw new ServiceResultException(
                    StatusCodes.BadDataEncodingInvalid,
                    "GlobalLocation aggregate and Position component values differ.");
            }

            (
                NodeId sourceId,
                uint coordinateReferenceSystem
            ) = await ReadPositionMetadataAsync(
                positionId,
                cancellationToken).ConfigureAwait(false);
            m_operations.ValidateGlobalPosition(location.Position, coordinateReferenceSystem);
            return new GlobalLocationValue(
                nodeId,
                location,
                sourceId,
                coordinateReferenceSystem,
                dataValue.StatusCode,
                dataValue.SourceTimestamp);
        }

        /// <summary>
        /// Reads and fits the GroundControlPoints exposed by a Zone.
        /// </summary>
        public async ValueTask<GroundControlPointFitResult> ReadZoneTransformAsync(
            NodeId zoneId,
            GroundControlPointFitOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            ushort namespaceIndex = m_operations.GetNamespaceIndex(
                Opc.Ua.Gpos.Namespaces.GPOS);
            NodeId groundControlPointsId =
                await m_operations.ResolveRequiredChildAsync(
                    zoneId,
                    ReferenceTypeIds.HasComponent,
                    new QualifiedName("GroundControlPoints", namespaceIndex),
                    cancellationToken).ConfigureAwait(false);
            ArrayOf<GroundControlPointDataType> groundControlPoints =
                await m_operations
                    .ReadStructureArrayAsync<GroundControlPointDataType>(
                        groundControlPointsId,
                        cancellationToken)
                    .ConfigureAwait(false);
            try
            {
                m_operations.ValidateGroundControlPoints(groundControlPoints, options, cancellationToken);
                GroundControlPointFitResult result = new GroundControlPointFitter(
                    options?.CoordinateReferenceSystem)
                    .Fit(groundControlPoints, options);
                m_operations.ValidateFit(result);
                return result;
            }
            catch (ArgumentException ex)
            {
                throw new ServiceResultException(
                    StatusCodes.BadInvalidArgument,
                    "The Zone GroundControlPoints do not define a valid transform.",
                    ex);
            }
        }

        /// <summary>
        /// Converts a global coordinate to the local coordinate system of a Zone.
        /// </summary>
        public async ValueTask<ThreeDCartesianCoordinates> GlobalToLocalAsync(
            NodeId zoneId,
            S3DGeographicCoordinateDataType globalPosition,
            AngleUnit angleUnit,
            GroundControlPointFitOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            globalPosition.ThrowIfNull(nameof(globalPosition));
            m_operations.ValidateGeographic(
                globalPosition, angleUnit, options?.CoordinateReferenceSystem is null ||
                options.CoordinateReferenceSystem is Wgs84CoordinateReferenceSystemTransformer);
            GroundControlPointFitResult transform = await ReadZoneTransformAsync(
                zoneId,
                options,
                cancellationToken).ConfigureAwait(false);
            ThreeDCartesianCoordinates result = transform.GlobalToLocal(globalPosition, angleUnit);
            m_operations.ValidateCartesian(result);
            return result;
        }

        /// <summary>
        /// Converts a local Zone coordinate to a global coordinate.
        /// </summary>
        public async ValueTask<S3DGeographicCoordinateDataType> LocalToGlobalAsync(
            NodeId zoneId,
            ThreeDCartesianCoordinates localPosition,
            AngleUnit angleUnit,
            GroundControlPointFitOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            localPosition.ThrowIfNull(nameof(localPosition));
            m_operations.ValidateCartesian(localPosition);
            m_operations.ValidateAngleUnit(angleUnit);
            GroundControlPointFitResult transform = await ReadZoneTransformAsync(
                zoneId,
                options,
                cancellationToken).ConfigureAwait(false);
            S3DGeographicCoordinateDataType result = transform.LocalToGlobal(localPosition, angleUnit);
            m_operations.ValidateGeographic(
                result, angleUnit, options?.CoordinateReferenceSystem is null ||
                options.CoordinateReferenceSystem is Wgs84CoordinateReferenceSystemTransformer);
            return result;
        }

        /// <summary>
        /// Streams changes to a GlobalPositionType Variable.
        /// </summary>
        public IAsyncEnumerable<GlobalPositionValue> ObserveGlobalPositionAsync(
            NodeId nodeId,
            IStreamingSubscription streaming,
            MonitoringOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            streaming.ThrowIfNull(nameof(streaming));
            return ObserveGlobalPositionImplAsync(
                nodeId,
                streaming,
                options,
                cancellationToken);
        }

        /// <summary>
        /// Streams changes to a GlobalLocationType Variable.
        /// </summary>
        public IAsyncEnumerable<GlobalLocationValue> ObserveGlobalLocationAsync(
            NodeId nodeId,
            IStreamingSubscription streaming,
            MonitoringOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            streaming.ThrowIfNull(nameof(streaming));
            return ObserveGlobalLocationImplAsync(
                nodeId,
                streaming,
                options,
                cancellationToken);
        }

        private async IAsyncEnumerable<GlobalPositionValue>
            ObserveGlobalPositionImplAsync(
                NodeId nodeId,
                IStreamingSubscription streaming,
                MonitoringOptions? options,
                [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            (
                NodeId sourceId,
                uint coordinateReferenceSystem
            ) = await ReadPositionMetadataAsync(
                nodeId,
                cancellationToken).ConfigureAwait(false);
            await foreach (DataValueChange change in streaming
                .SubscribeDataChangesAsync(
                    nodeId,
                    options,
                    cancellationToken)
                .ConfigureAwait(false))
            {
                GlobalPositionDataType position = m_operations.DecodeStructure<GlobalPositionDataType>(change.Value);
                m_operations.ValidateGlobalPosition(position, coordinateReferenceSystem);
                yield return new GlobalPositionValue(
                    nodeId,
                    position,
                    sourceId,
                    coordinateReferenceSystem,
                    change.Value.StatusCode,
                    change.Value.SourceTimestamp);
            }
        }

        private async IAsyncEnumerable<GlobalLocationValue>
            ObserveGlobalLocationImplAsync(
                NodeId nodeId,
                IStreamingSubscription streaming,
                MonitoringOptions? options,
                [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            ushort rslNamespaceIndex = m_operations.GetNamespaceIndex(
                Opc.Ua.Rsl.Namespaces.RSL);
            NodeId positionId = await m_operations.ResolveRequiredChildAsync(
                nodeId,
                ReferenceTypeIds.HasComponent,
                new QualifiedName("Position", rslNamespaceIndex),
                cancellationToken).ConfigureAwait(false);
            (
                NodeId sourceId,
                uint coordinateReferenceSystem
            ) = await ReadPositionMetadataAsync(
                positionId,
                cancellationToken).ConfigureAwait(false);

            await foreach (DataValueChange change in streaming
                .SubscribeDataChangesAsync(
                    nodeId,
                    options,
                    cancellationToken)
                .ConfigureAwait(false))
            {
                GlobalLocationDataType location = m_operations.DecodeStructure<GlobalLocationDataType>(change.Value);
                m_operations.ValidateGlobalLocation(location);
                m_operations.ValidateGlobalPosition(location.Position, coordinateReferenceSystem);
                yield return new GlobalLocationValue(
                    nodeId,
                    location,
                    sourceId,
                    coordinateReferenceSystem,
                    change.Value.StatusCode,
                    change.Value.SourceTimestamp);
            }
        }

        private async ValueTask<(NodeId SourceId, uint CoordinateReferenceSystem)>
            ReadPositionMetadataAsync(
                NodeId positionId,
                CancellationToken cancellationToken)
        {
            ushort namespaceIndex = m_operations.GetNamespaceIndex(
                Opc.Ua.Gpos.Namespaces.GPOS);
            NodeId sourceIdNode = await m_operations.ResolveRequiredChildAsync(
                positionId,
                ReferenceTypeIds.HasProperty,
                new QualifiedName("SourceId", namespaceIndex),
                cancellationToken).ConfigureAwait(false);
            NodeId coordinateReferenceSystemNode =
                await m_operations.ResolveRequiredChildAsync(
                    positionId,
                    ReferenceTypeIds.HasComponent,
                    new QualifiedName(
                        "CoordinateReferenceSystem",
                        namespaceIndex),
                    cancellationToken).ConfigureAwait(false);
            NodeId sourceId = await m_operations.ReadNodeIdAsync(
                sourceIdNode,
                cancellationToken).ConfigureAwait(false);
            uint coordinateReferenceSystem =
                await m_operations.ReadUInt32Async(
                    coordinateReferenceSystemNode,
                    cancellationToken).ConfigureAwait(false);
            return (sourceId, coordinateReferenceSystem);
        }

        private void RegisterTypes()
        {
            Session.Factory.Builder.AddOpcUaGpos().Commit();
            Session.MessageContext.Factory.Builder.AddOpcUaGpos().Commit();
        }

        private readonly PositioningClientOperations m_operations;
    }
}
