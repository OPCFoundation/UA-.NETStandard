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
using Opc.Ua.Rsl;
using MonitoringOptions =
    Opc.Ua.Client.Subscriptions.MonitoredItems.MonitoredItemOptions;

namespace Opc.Ua.Positioning.Client
{
    /// <summary>
    /// High-level client for the OPC 10000-210 Relative Spatial Location model.
    /// </summary>
    public sealed class RelativeSpatialLocationClient
    {
        private readonly PositioningClientOperations m_operations;

        /// <summary>
        /// Creates an RSL client.
        /// </summary>
        public RelativeSpatialLocationClient(
            ISession session,
            ITelemetryContext telemetry)
        {
            m_operations = new PositioningClientOperations(session, telemetry);
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
        /// Standardized RelativeSpatialLocations entry point.
        /// </summary>
        public NodeId RelativeSpatialLocationsId => NodeId.Create(
            Rsl.Objects.RelativeSpatialLocations,
            Rsl.Namespaces.RSL,
            Session.NamespaceUris);

        /// <summary>
        /// Enumerates all spatial object lists from the standardized entry point.
        /// </summary>
        public async IAsyncEnumerable<PositioningObjectEntry>
            EnumerateSpatialObjectListsAsync(
                [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            ArrayOf<ReferenceDescription> references = await m_operations.BrowseAsync(
                RelativeSpatialLocationsId,
                ReferenceTypeIds.Organizes,
                (uint)NodeClass.Object,
                cancellationToken).ConfigureAwait(false);

            ExpandedNodeId expectedType = Rsl.ObjectTypeIds
                .SpatialObjectsListType;
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
        /// Enumerates spatial objects organized by a spatial object list.
        /// </summary>
        public async IAsyncEnumerable<PositioningObjectEntry>
            EnumerateSpatialObjectsAsync(
                NodeId listId,
                [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            ArrayOf<ReferenceDescription> references = await m_operations.BrowseAsync(
                listId,
                ReferenceTypeIds.Organizes,
                (uint)NodeClass.Object,
                cancellationToken).ConfigureAwait(false);

            ExpandedNodeId expectedType = Rsl.ObjectTypeIds.SpatialObjectType;
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
        /// Opens the generated proxy for a spatial object.
        /// </summary>
        public SpatialObjectTypeClient OpenSpatialObject(NodeId nodeId)
        {
            return new SpatialObjectTypeClient(Session, nodeId, Telemetry);
        }

        /// <summary>
        /// Opens the generated proxy for a spatial object list.
        /// </summary>
        public SpatialObjectsListTypeClient OpenSpatialObjectList(NodeId nodeId)
        {
            return new SpatialObjectsListTypeClient(Session, nodeId, Telemetry);
        }

        /// <summary>
        /// Enumerates spatial frames below an RSL frame folder.
        /// </summary>
        public async IAsyncEnumerable<PositioningObjectEntry> EnumerateFramesAsync(
            NodeId folderId,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            ArrayOf<ReferenceDescription> references = await m_operations.BrowseAsync(
                folderId,
                ReferenceTypeIds.HasComponent,
                (uint)NodeClass.Variable,
                cancellationToken).ConfigureAwait(false);

            ExpandedNodeId expectedType = Rsl.VariableTypeIds.SpatialLocationType;
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
        /// Reads the mandatory PositionFrame and its Base NodeId.
        /// </summary>
        public async ValueTask<RelativeSpatialFrameValue> ReadPositionFrameAsync(
            NodeId spatialObjectId,
            CancellationToken cancellationToken = default)
        {
            ushort namespaceIndex = Session.NamespaceUris.GetIndexOrAppend(
                Rsl.Namespaces.RSL);
            NodeId frameId = await m_operations.ResolveRequiredChildAsync(
                spatialObjectId,
                ReferenceTypeIds.HasComponent,
                new QualifiedName("PositionFrame", namespaceIndex),
                cancellationToken).ConfigureAwait(false);
            return await ReadFrameAsync(
                frameId,
                cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Reads an RSL frame Variable and its Base NodeId.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public async ValueTask<RelativeSpatialFrameValue> ReadFrameAsync(
            NodeId frameId,
            CancellationToken cancellationToken = default)
        {
            ushort namespaceIndex = Session.NamespaceUris.GetIndexOrAppend(
                Rsl.Namespaces.RSL);
            NodeId baseId = await m_operations.ResolveRequiredChildAsync(
                frameId,
                ReferenceTypeIds.HasComponent,
                new QualifiedName("Base", namespaceIndex),
                cancellationToken).ConfigureAwait(false);

            DataValue frameValue = await Session.ReadValueAsync(
                frameId,
                cancellationToken).ConfigureAwait(false);
            ThreeDFrame frame;
            if (!frameValue.WrappedValue.TryGetValue(
                out frame!,
                Session.MessageContext))
            {
                throw new ServiceResultException(
                    StatusCodes.BadTypeMismatch,
                    "PositionFrame does not contain a 3DFrame value.");
            }

            NodeId frameBase = await m_operations.ReadNodeIdAsync(
                baseId,
                cancellationToken).ConfigureAwait(false);

            return new RelativeSpatialFrameValue(
                frameId,
                frameBase,
                frame,
                frameValue.StatusCode,
                frameValue.SourceTimestamp);
        }

        /// <summary>
        /// Resolves a frame through its Base chain to the world coordinate system.
        /// </summary>
        /// <exception cref="ArgumentException"></exception>
        /// <exception cref="ServiceResultException"></exception>
        public async ValueTask<ResolvedRelativeSpatialFrame> ResolveFrameToWorldAsync(
            NodeId frameId,
            AngleUnit angleUnit,
            CancellationToken cancellationToken = default)
        {
            if (frameId.IsNull)
            {
                throw new ArgumentException(
                    "A non-null frame NodeId is required.",
                    nameof(frameId));
            }

            var visited = new HashSet<NodeId>();
            var chain = new List<NodeId>();
            RslFrameTransform transform = RslFrameTransform.Identity;
            NodeId current = frameId;

            while (!current.IsNull)
            {
                if (!visited.Add(current))
                {
                    throw new ServiceResultException(
                        StatusCodes.BadInvalidState,
                        $"The RSL Base chain contains a cycle at '{current}'.");
                }
                if (chain.Count >= 1024)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadEncodingLimitsExceeded,
                        "The RSL Base chain exceeds 1024 frames.");
                }

                RelativeSpatialFrameValue value = await ReadFrameAsync(
                    current,
                    cancellationToken).ConfigureAwait(false);
                if (StatusCode.IsBad(value.StatusCode))
                {
                    throw new ServiceResultException(
                        value.StatusCode,
                        $"RSL frame '{current}' has a bad status.");
                }

                var local = RslFrameTransform.FromFrame(
                    value.Frame,
                    angleUnit);
                transform = local.Compose(transform);
                chain.Add(current);
                current = value.BaseNodeId;
            }

            return new ResolvedRelativeSpatialFrame(
                frameId,
                transform,
                chain.ToArray().ToArrayOf());
        }

        /// <summary>
        /// Streams changes to an RSL frame Variable.
        /// </summary>
        public IAsyncEnumerable<RelativeSpatialFrameValue> ObserveFrameAsync(
            NodeId frameId,
            IStreamingSubscription streaming,
            MonitoringOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            streaming.ThrowIfNull(nameof(streaming));
            return ObserveFrameImplAsync(
                frameId,
                streaming,
                options,
                cancellationToken);
        }

        /// <summary>
        /// Streams changes to a spatial object's mandatory PositionFrame.
        /// </summary>
        public IAsyncEnumerable<RelativeSpatialFrameValue>
            ObservePositionFrameAsync(
                NodeId spatialObjectId,
                IStreamingSubscription streaming,
                MonitoringOptions? options = null,
                CancellationToken cancellationToken = default)
        {
            streaming.ThrowIfNull(nameof(streaming));
            return ObservePositionFrameImplAsync(
                spatialObjectId,
                streaming,
                options,
                cancellationToken);
        }

        private async IAsyncEnumerable<RelativeSpatialFrameValue>
            ObservePositionFrameImplAsync(
                NodeId spatialObjectId,
                IStreamingSubscription streaming,
                MonitoringOptions? options,
                [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            ushort namespaceIndex = Session.NamespaceUris.GetIndexOrAppend(
                Rsl.Namespaces.RSL);
            NodeId frameId = await m_operations.ResolveRequiredChildAsync(
                spatialObjectId,
                ReferenceTypeIds.HasComponent,
                new QualifiedName("PositionFrame", namespaceIndex),
                cancellationToken).ConfigureAwait(false);
            await foreach (RelativeSpatialFrameValue value in ObserveFrameImplAsync(
                frameId,
                streaming,
                options,
                cancellationToken).ConfigureAwait(false))
            {
                yield return value;
            }
        }

        /// <summary>
        /// Streams changes to an RSL spatial object's list NodeVersion.
        /// </summary>
        public IAsyncEnumerable<PositioningNodeVersionChange>
            ObserveNodeVersionAsync(
                NodeId listId,
                IStreamingSubscription streaming,
                MonitoringOptions? options = null,
                CancellationToken cancellationToken = default)
        {
            streaming.ThrowIfNull(nameof(streaming));
            return ObserveNodeVersionImplAsync(
                listId,
                streaming,
                options,
                cancellationToken);
        }

        private async IAsyncEnumerable<PositioningNodeVersionChange>
            ObserveNodeVersionImplAsync(
                NodeId listId,
                IStreamingSubscription streaming,
                MonitoringOptions? options,
                [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            NodeId nodeVersionId = await m_operations.ResolveRequiredChildAsync(
                listId,
                ReferenceTypeIds.HasProperty,
                QualifiedName.From(BrowseNames.NodeVersion),
                cancellationToken).ConfigureAwait(false);

            await foreach (DataValueChange change in streaming
                .SubscribeDataChangesAsync(
                    nodeVersionId,
                    options,
                    cancellationToken)
                .ConfigureAwait(false))
            {
                string nodeVersion = null!;
                if (!change.Value.WrappedValue.TryGetValue(
                    out nodeVersion))
                {
                    throw new ServiceResultException(
                        StatusCodes.BadTypeMismatch,
                        "NodeVersion does not contain a String value.");
                }
                yield return new PositioningNodeVersionChange(
                    listId,
                    nodeVersion,
                    change.Value.StatusCode,
                    change.Value.SourceTimestamp);
            }
        }

        private async IAsyncEnumerable<RelativeSpatialFrameValue>
            ObserveFrameImplAsync(
                NodeId frameId,
                IStreamingSubscription streaming,
                MonitoringOptions? options,
                [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            ushort namespaceIndex = Session.NamespaceUris.GetIndexOrAppend(
                Rsl.Namespaces.RSL);
            NodeId baseId = await m_operations.ResolveRequiredChildAsync(
                frameId,
                ReferenceTypeIds.HasComponent,
                new QualifiedName("Base", namespaceIndex),
                cancellationToken).ConfigureAwait(false);

            await foreach (DataValueChange change in streaming
                .SubscribeDataChangesAsync(
                    frameId,
                    options,
                    cancellationToken)
                .ConfigureAwait(false))
            {
                ThreeDFrame frame = null!;
                if (!change.Value.WrappedValue.TryGetValue(
                    out frame!,
                    Session.MessageContext))
                {
                    throw new ServiceResultException(
                        StatusCodes.BadTypeMismatch,
                        "The monitored frame does not contain a 3DFrame value.");
                }
                NodeId baseNodeId = await m_operations.ReadNodeIdAsync(
                    baseId,
                    cancellationToken).ConfigureAwait(false);
                yield return new RelativeSpatialFrameValue(
                    frameId,
                    baseNodeId,
                    frame,
                    change.Value.StatusCode,
                    change.Value.SourceTimestamp);
            }
        }
    }
}
