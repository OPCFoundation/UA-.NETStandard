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
 * MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
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
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Opc.Ua.Client.Network
{
    public sealed partial class NetworkModelClient
    {
        /// <summary>
        /// Discovers standard and vendor-subtype IETF interfaces from the standard entry point,
        /// including objects that implement the IETF interface without deriving from its concrete type.
        /// </summary>
        public ValueTask<ArrayOf<NodeId>> DiscoverInterfacesAsync(CancellationToken cancellationToken = default)
        {
            return DiscoverAsync(ObjectIds.NetworkInterfaces, ObjectTypeIds.IetfBaseNetworkInterfaceType,
                ObjectTypeIds.IIetfBaseNetworkInterfaceType, cancellationToken);
        }

        /// <summary>
        /// Discovers priority tables without assuming instance namespace indexes or names.
        /// </summary>
        public ValueTask<ArrayOf<NodeId>> DiscoverMappingTablesAsync(CancellationToken cancellationToken = default)
        {
            return DiscoverAsync(ObjectIds.MappingTables, ObjectTypeIds.PriorityMappingTableType,
                NodeId.Null, cancellationToken);
        }

        /// <summary>
        /// Discovers talker or listener objects implementing the base TSN stream interface.
        /// </summary>
        public ValueTask<ArrayOf<NodeId>> DiscoverStreamsAsync(
            bool talkers,
            CancellationToken cancellationToken = default)
        {
            return DiscoverAsync(talkers ? ObjectIds.TalkerStreams : ObjectIds.ListenerStreams,
                NodeId.Null, ObjectTypeIds.IIeeeBaseTsnStreamType, cancellationToken);
        }

        /// <summary>
        /// Discovers LLDP ports below the standard LLDP entry point.
        /// </summary>
        public ValueTask<ArrayOf<NodeId>> DiscoverLldpPortsAsync(CancellationToken cancellationToken = default)
        {
            return DiscoverAsync(ObjectIds.LLDP, ObjectTypeIds.LldpPortInformationType,
                NodeId.Null, cancellationToken);
        }

        /// <summary>
        /// Discovers current LLDP neighbors beneath one port, including vendor subtypes.
        /// </summary>
        public ValueTask<ArrayOf<NodeId>> DiscoverNeighborsAsync(
            NodeId portId,
            CancellationToken cancellationToken = default)
        {
            ValidateNodeId(portId);
            return DiscoverAsync(portId, ObjectTypeIds.LldpRemoteSystemType, NodeId.Null, cancellationToken);
        }

        /// <summary>
        /// Resolves priority-table inheritance one layer at a time. A directly assigned table stops
        /// fallback on that branch. Aggregated interfaces may resolve multiple distinct tables;
        /// missing targets, cycles and multiple direct tables are reported, not selected arbitrarily.
        /// </summary>
        public async ValueTask<ArrayOf<NodeId>> GetEffectiveMappingTablesAsync(
            NodeId interfaceId,
            CancellationToken cancellationToken = default)
        {
            ValidateNodeId(interfaceId);
            var pending = new Queue<(NodeId Id, HashSet<NodeId> Ancestors)>();
            pending.Enqueue((interfaceId, []));
            var result = new HashSet<NodeId>();
            int examined = 0;
            while (pending.Count != 0)
            {
                if (++examined > 4096)
                {
                    throw new ServiceResultException(StatusCodes.BadTooManyOperations,
                        "The lower-layer interface graph exceeds the client traversal limit.");
                }
                (NodeId current, HashSet<NodeId> ancestors) = pending.Dequeue();
                if (!ancestors.Add(current))
                {
                    throw new ServiceResultException(StatusCodes.BadInvalidState,
                        "The lower-layer interface graph contains a cycle.");
                }
                ArrayOf<ReferenceDescription> tables = await BrowseAsync(
                    current, ReferenceTypeIds.UsesPriorityMappingTable,
                    BrowseDirection.Forward, NodeClass.Object, cancellationToken).ConfigureAwait(false);
                if (tables.Count > 1)
                {
                    throw new ServiceResultException(StatusCodes.BadInvalidState,
                        "An interface references more than one priority table.");
                }
                if (tables.Count == 1)
                {
                    NodeId table = LocalNodeId(tables[0].NodeId);
                    ArrayOf<ReferenceDescription> typeDefinitions = await BrowseAsync(
                        table, ReferenceTypeIds.HasTypeDefinition,
                        BrowseDirection.Forward, NodeClass.ObjectType, cancellationToken).ConfigureAwait(false);
                    if (typeDefinitions.Count != 1 ||
                        !await MatchesTypeAsync(
                            LocalNodeId(typeDefinitions[0].NodeId),
                            ObjectTypeIds.PriorityMappingTableType,
                            NodeId.Null,
                            cancellationToken).ConfigureAwait(false))
                    {
                        throw new ServiceResultException(StatusCodes.BadTypeDefinitionInvalid,
                            "A referenced mapping table must have one type definition derived from " +
                            "PriorityMappingTableType.");
                    }
                    result.Add(table);
                    continue;
                }
                ArrayOf<ReferenceDescription> lower = await BrowseAsync(
                    current, ReferenceTypeIds.HasLowerLayerInterface,
                    BrowseDirection.Forward, NodeClass.Object, cancellationToken).ConfigureAwait(false);
                for (int index = 0; index < lower.Count; index++)
                {
                    pending.Enqueue((LocalNodeId(lower[index].NodeId), new HashSet<NodeId>(ancestors)));
                }
            }
            var ids = new NodeId[result.Count];
            result.CopyTo(ids);
            return ArrayOf.Wrapped(ids);
        }

        /// <summary>
        /// Reads all immediate parameters with independent quality and acquisition times.
        /// Nested Ethernet, traffic, status and LLDP parameter objects can be read explicitly.
        /// </summary>
        public async ValueTask<ArrayOf<NetworkParameterValue>> ReadParametersAsync(
            NodeId objectId,
            CancellationToken cancellationToken = default)
        {
            ValidateNodeId(objectId);
            ArrayOf<ReferenceDescription> variables = await BrowseAsync(
                objectId, ReferenceTypeIds.HasChild, BrowseDirection.Forward, NodeClass.Variable,
                cancellationToken).ConfigureAwait(false);
            if (variables.Count == 0)
            {
                return [];
            }
            var requests = new ReadValueId[variables.Count];
            for (int index = 0; index < requests.Length; index++)
            {
                requests[index] = new ReadValueId
                {
                    NodeId = LocalNodeId(variables[index].NodeId),
                    AttributeId = Attributes.Value
                };
            }
            ArrayOf<ReadValueId> read = ArrayOf.Wrapped(requests);
            ReadResponse response = await Session.ReadAsync(
                null, 0, TimestampsToReturn.Both, read, cancellationToken).ConfigureAwait(false);
            ValidateService(response.ResponseHeader);
            ClientBase.ValidateResponse(response.Results, read);
            ClientBase.ValidateDiagnosticInfos(response.DiagnosticInfos, read);
            var values = new NetworkParameterValue[requests.Length];
            for (int index = 0; index < values.Length; index++)
            {
                values[index] = new NetworkParameterValue(
                    requests[index].NodeId, variables[index].BrowseName, response.Results[index]);
            }
            return ArrayOf.Wrapped(values);
        }

        /// <summary>
        /// Browses immediate object components for optional parameter-set and LLDP inspection.
        /// </summary>
        public ValueTask<ArrayOf<ReferenceDescription>> BrowseObjectsAsync(
            NodeId objectId,
            CancellationToken cancellationToken = default)
        {
            ValidateNodeId(objectId);
            return BrowseAsync(objectId, ReferenceTypeIds.HierarchicalReferences,
                BrowseDirection.Forward, NodeClass.Object, cancellationToken);
        }

        /// <summary>
        /// Traverses one bounded entry-point hierarchy using actual type/interface identities.
        /// </summary>
        private async ValueTask<ArrayOf<NodeId>> DiscoverAsync(
            NodeId root,
            NodeId expectedType,
            NodeId expectedInterface,
            CancellationToken cancellationToken)
        {
            var pending = new Queue<NodeId>();
            var visited = new HashSet<NodeId> { root };
            var found = new List<NodeId>();
            pending.Enqueue(root);
            while (pending.Count != 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                NodeId current = pending.Dequeue();
                bool matches = false;
                ArrayOf<ReferenceDescription> types = await BrowseAsync(
                    current, ReferenceTypeIds.HasTypeDefinition, BrowseDirection.Forward,
                    NodeClass.ObjectType, cancellationToken).ConfigureAwait(false);
                for (int index = 0; index < types.Count && !matches; index++)
                {
                    NodeId typeId = LocalNodeId(types[index].NodeId);
                    matches = await MatchesTypeAsync(typeId, expectedType, expectedInterface, cancellationToken)
                        .ConfigureAwait(false);
                }
                if (!matches && !expectedInterface.IsNull)
                {
                    matches = await ImplementsAsync(current, expectedInterface, cancellationToken)
                        .ConfigureAwait(false);
                }
                if (matches)
                {
                    found.Add(current);
                }
                ArrayOf<ReferenceDescription> children = await BrowseObjectsAsync(current, cancellationToken)
                    .ConfigureAwait(false);
                for (int index = 0; index < children.Count; index++)
                {
                    NodeId id = LocalNodeId(children[index].NodeId);
                    if (visited.Add(id))
                    {
                        if (visited.Count > 4096)
                        {
                            throw new ServiceResultException(StatusCodes.BadTooManyOperations,
                                "The network hierarchy exceeds the client traversal limit.");
                        }
                        pending.Enqueue(id);
                    }
                }
            }
            return ArrayOf.Wrapped(found.ToArray());
        }

        /// <summary>
        /// Matches vendor subtype ancestry and interfaces declared on any ancestor.
        /// </summary>
        private async ValueTask<bool> MatchesTypeAsync(
            NodeId type,
            NodeId expected,
            NodeId expectedInterface,
            CancellationToken cancellationToken)
        {
            var pending = new Queue<NodeId>();
            var visited = new HashSet<NodeId>();
            pending.Enqueue(type);
            while (pending.Count != 0)
            {
                NodeId current = pending.Dequeue();
                if (current == expected)
                {
                    return true;
                }
                if (!visited.Add(current))
                {
                    continue;
                }
                if (visited.Count > 256)
                {
                    throw new ServiceResultException(StatusCodes.BadTooManyOperations,
                        "The type ancestry exceeds the client traversal limit.");
                }
                if (!expectedInterface.IsNull &&
                    await ImplementsAsync(current, expectedInterface, cancellationToken).ConfigureAwait(false))
                {
                    return true;
                }
                ArrayOf<ReferenceDescription> parents = await BrowseAsync(
                    current, ReferenceTypeIds.HasSubtype, BrowseDirection.Inverse,
                    NodeClass.ObjectType, cancellationToken).ConfigureAwait(false);
                for (int index = 0; index < parents.Count; index++)
                {
                    pending.Enqueue(LocalNodeId(parents[index].NodeId));
                }
            }
            return false;
        }

        /// <summary>
        /// Checks an object's actual HasInterface references.
        /// </summary>
        private async ValueTask<bool> ImplementsAsync(
            NodeId id,
            NodeId expectedInterface,
            CancellationToken cancellationToken)
        {
            ArrayOf<ReferenceDescription> interfaces = await BrowseAsync(
                id, ReferenceTypeIds.HasInterface, BrowseDirection.Forward,
                NodeClass.ObjectType, cancellationToken).ConfigureAwait(false);
            for (int index = 0; index < interfaces.Count; index++)
            {
                if (LocalNodeId(interfaces[index].NodeId) == expectedInterface)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Resolves local targets with the connected session's namespace URI mapping.
        /// </summary>
        private NodeId LocalNodeId(ExpandedNodeId id)
        {
            NodeId local = ExpandedNodeId.ToNodeId(id, Session.MessageContext.NamespaceUris);
            if (id.ServerIndex != 0 || local.IsNull)
            {
                throw new ServiceResultException(StatusCodes.BadNotSupported,
                    "The reference target is not addressable in the current session.");
            }
            return local;
        }

        /// <summary>
        /// Validates service failures separately from per-value quality.
        /// </summary>
        private static void ValidateService(ResponseHeader header)
        {
            if (StatusCode.IsBad(header.ServiceResult))
            {
                throw new ServiceResultException(header.ServiceResult);
            }
        }

        /// <summary>
        /// Completes one browse and releases any continuation resource on failure or cancellation.
        /// </summary>
        private async ValueTask<ArrayOf<ReferenceDescription>> BrowseAsync(
            NodeId id,
            NodeId referenceType,
            BrowseDirection direction,
            NodeClass nodeClass,
            CancellationToken cancellationToken)
        {
            ArrayOf<BrowseDescription> requests =
            [
                new BrowseDescription
                {
                    NodeId = id,
                    ReferenceTypeId = referenceType,
                    BrowseDirection = direction,
                    IncludeSubtypes = true,
                    NodeClassMask = (uint)nodeClass,
                    ResultMask = (uint)BrowseResultMask.All
                }
            ];
            var references = new List<ReferenceDescription>();
            var continuationPoints = new List<ByteString>();
            try
            {
                BrowseResponse response = await Session.BrowseAsync(
                    null, null, 0, requests, cancellationToken).ConfigureAwait(false);
                RetainContinuationPoints(response.Results, continuationPoints);
                ValidateService(response.ResponseHeader);
                ClientBase.ValidateResponse(response.Results, requests);
                ClientBase.ValidateDiagnosticInfos(response.DiagnosticInfos, requests);
                BrowseResult result = response.Results[0];
                int pages = 0;
                while (true)
                {
                    if (++pages > 4096)
                    {
                        throw new ServiceResultException(StatusCodes.BadTooManyOperations,
                            "A network browse exceeds the client page limit.");
                    }
                    if (StatusCode.IsBad(result.StatusCode))
                    {
                        throw new ServiceResultException(result.StatusCode);
                    }
                    if (references.Count + result.References.Count > 16384)
                    {
                        throw new ServiceResultException(StatusCodes.BadTooManyOperations,
                            "A network browse exceeds the client reference limit.");
                    }
                    for (int index = 0; index < result.References.Count; index++)
                    {
                        references.Add(result.References[index]);
                    }
                    ByteString point = result.ContinuationPoint;
                    if (point.IsNull || point.Length == 0)
                    {
                        return ArrayOf.Wrapped(references.ToArray());
                    }
                    ArrayOf<ByteString> points = [point];
                    BrowseNextResponse next = await Session.BrowseNextAsync(
                        null, false, points, cancellationToken).ConfigureAwait(false);
                    int previousContinuationPointCount = continuationPoints.Count;
                    RetainContinuationPoints(next.Results, continuationPoints);
                    ValidateService(next.ResponseHeader);
                    continuationPoints.RemoveRange(0, previousContinuationPointCount);
                    ClientBase.ValidateResponse(next.Results, points);
                    ClientBase.ValidateDiagnosticInfos(next.DiagnosticInfos, points);
                    result = next.Results[0];
                }
            }
            finally
            {
                if (continuationPoints.Count > 0)
                {
                    await ReleaseBrowseContinuationPointsAsync(continuationPoints).ConfigureAwait(false);
                }
            }
        }

        /// <summary>
        /// Releases browse continuations and reports cleanup failures without replacing the browse outcome.
        /// </summary>
        private async ValueTask ReleaseBrowseContinuationPointsAsync(List<ByteString> continuationPoints)
        {
            ArrayOf<ByteString> points = ArrayOf.Wrapped(continuationPoints.ToArray());
            try
            {
                BrowseNextResponse release = await Session.BrowseNextAsync(
                    null, true, points, CancellationToken.None).ConfigureAwait(false);
                ValidateService(release.ResponseHeader);
                ClientBase.ValidateResponse(release.Results, points);
                ClientBase.ValidateDiagnosticInfos(release.DiagnosticInfos, points);
                for (int index = 0; index < release.Results.Count; index++)
                {
                    if (StatusCode.IsBad(release.Results[index].StatusCode))
                    {
                        throw new ServiceResultException(release.Results[index].StatusCode);
                    }
                }
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                NetworkModelClientLog.ContinuationReleaseFailed(
                    Telemetry.CreateLogger<NetworkModelClient>(), exception);
            }
        }

        /// <summary>
        /// Retains every continuation point returned, including those in malformed extra results.
        /// </summary>
        private static void RetainContinuationPoints(
            ArrayOf<BrowseResult> results,
            List<ByteString> continuationPoints)
        {
            for (int index = 0; index < results.Count; index++)
            {
                if (results[index] is not BrowseResult result)
                {
                    continue;
                }
                ByteString point = result.ContinuationPoint;
                if (!point.IsNull && point.Length != 0)
                {
                    continuationPoints.Add(point);
                }
            }
        }
    }

    /// <summary>
    /// Diagnostics for Part 22 client browse resources.
    /// </summary>
    internal static partial class NetworkModelClientLog
    {
        /// <summary>
        /// Reports a failed release of a browse continuation point.
        /// </summary>
        [LoggerMessage(
            EventId = ClientEventIds.NetworkModelClient,
            Level = LogLevel.Warning,
            Message = "Releasing a network-model browse continuation failed.")]
        internal static partial void ContinuationReleaseFailed(ILogger logger, Exception exception);
    }
}
