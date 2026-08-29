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
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Client;

namespace Opc.Ua.Vision.Client
{
    /// <summary>
    /// Shared low-level plumbing used by every high-level Vision client:
    /// namespace-index resolution, subtype-aware browsing, browse-path
    /// resolution, and typed value reads over the connected session.
    /// </summary>
    internal sealed class VisionClientOperations
    {
        public VisionClientOperations(ISession session, ITelemetryContext telemetry)
        {
            Session = session ?? throw new ArgumentNullException(nameof(session));
            Telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
            RegisterEncodeableTypes(session);
        }

        public ISession Session { get; }

        public ITelemetryContext Telemetry { get; }

        public bool TryGetVisionNamespaceIndex(out ushort namespaceIndex)
        {
            int index = Session.NamespaceUris.GetIndex(Namespaces.Vision);
            if (index < 0)
            {
                namespaceIndex = 0;
                return false;
            }
            namespaceIndex = (ushort)index;
            return true;
        }

        public NodeId VisionNamespaceType(uint identifier)
        {
            return TryGetVisionNamespaceIndex(out ushort ns)
                ? new NodeId(identifier, ns)
                : NodeId.Null;
        }

        public NodeId VisionReference(uint identifier)
        {
            return VisionNamespaceType(identifier);
        }

        public async ValueTask<ArrayOf<ReferenceDescription>> BrowseAsync(
            NodeId nodeId,
            NodeId referenceTypeId,
            BrowseDirection direction,
            uint nodeClassMask,
            CancellationToken cancellationToken)
        {
            (ArrayOf<ArrayOf<ReferenceDescription>> results, ArrayOf<ServiceResult> errors) =
                await Session.ManagedBrowseAsync(
                    requestHeader: null,
                    view: null,
                    nodesToBrowse: [nodeId],
                    maxResultsToReturn: 0,
                    browseDirection: direction,
                    referenceTypeId: referenceTypeId,
                    includeSubtypes: true,
                    nodeClassMask: nodeClassMask,
                    ct: cancellationToken).ConfigureAwait(false);
            if (errors.Count > 0 && ServiceResult.IsBad(errors[0]))
            {
                return ArrayOf<ReferenceDescription>.Empty;
            }
            return results.Count > 0 ? results[0] : ArrayOf<ReferenceDescription>.Empty;
        }

        public ValueTask<ArrayOf<ReferenceDescription>> BrowseHierarchicalObjectsAsync(
            NodeId nodeId, CancellationToken cancellationToken)
        {
            return BrowseAsync(
                nodeId,
                Opc.Ua.ReferenceTypeIds.HierarchicalReferences,
                BrowseDirection.Forward,
                (uint)NodeClass.Object,
                cancellationToken);
        }

        public async ValueTask<ArrayOf<NodeId>> DiscoverInstancesAsync(
            NodeId root, NodeId typeDefinition, CancellationToken cancellationToken)
        {
            if (typeDefinition.IsNull)
            {
                return ArrayOf<NodeId>.Empty;
            }
            ArrayOf<ReferenceDescription> references = await BrowseHierarchicalObjectsAsync(
                root, cancellationToken).ConfigureAwait(false);
            var matches = new List<NodeId>();
            for (int ii = 0; ii < references.Count; ii++)
            {
                ReferenceDescription reference = references[ii];
                NodeId typeDef = ExpandedNodeId.ToNodeId(
                    reference.TypeDefinition, Session.NamespaceUris);
                NodeId child = ExpandedNodeId.ToNodeId(
                    reference.NodeId, Session.NamespaceUris);
                if (typeDef.IsNull || child.IsNull)
                {
                    continue;
                }
                if (await Session.NodeCache.IsTypeOfAsync(
                        typeDef, typeDefinition, cancellationToken).ConfigureAwait(false))
                {
                    matches.Add(child);
                }
            }
            return matches.ToArrayOf();
        }

        public async ValueTask<ArrayOf<NodeId>> BrowseChildNodeIdsAsync(
            NodeId parent, CancellationToken cancellationToken)
        {
            ArrayOf<ReferenceDescription> references = await BrowseHierarchicalObjectsAsync(
                parent, cancellationToken).ConfigureAwait(false);
            var children = new List<NodeId>(references.Count);
            for (int ii = 0; ii < references.Count; ii++)
            {
                NodeId nodeId = ExpandedNodeId.ToNodeId(
                    references[ii].NodeId, Session.NamespaceUris);
                if (!nodeId.IsNull)
                {
                    children.Add(nodeId);
                }
            }
            return children.ToArrayOf();
        }

        public async ValueTask<ArrayOf<NodeId>> ResolveChildrenAsync(
            NodeId parent,
            ArrayOf<string> browseNames,
            ushort namespaceIndex,
            CancellationToken cancellationToken)
        {
            var paths = new List<BrowsePath>(browseNames.Count);
            for (int ii = 0; ii < browseNames.Count; ii++)
            {
                paths.Add(CreateBrowsePath(parent, browseNames[ii], namespaceIndex));
            }
            TranslateBrowsePathsToNodeIdsResponse response = await Session
                .TranslateBrowsePathsToNodeIdsAsync(
                    null, paths.ToArrayOf(), cancellationToken).ConfigureAwait(false);
            var results = new List<NodeId>(browseNames.Count);
            for (int ii = 0; ii < response.Results.Count; ii++)
            {
                BrowsePathResult result = response.Results[ii];
                results.Add(StatusCode.IsGood(result.StatusCode) && result.Targets.Count > 0
                    ? ExpandedNodeId.ToNodeId(result.Targets[0].TargetId, Session.NamespaceUris)
                    : NodeId.Null);
            }
            while (results.Count < browseNames.Count)
            {
                results.Add(NodeId.Null);
            }
            return results.ToArrayOf();
        }

        public async ValueTask<NodeId> ResolveChildAsync(
            NodeId parent,
            string browseName,
            ushort namespaceIndex,
            CancellationToken cancellationToken)
        {
            ArrayOf<NodeId> nodes = await ResolveChildrenAsync(
                parent, [browseName], namespaceIndex, cancellationToken).ConfigureAwait(false);
            return nodes.Count > 0 ? nodes[0] : NodeId.Null;
        }

        public async ValueTask<NodeId> ResolveChildAsync(
            NodeId parent,
            string browseName,
            CancellationToken cancellationToken)
        {
            if (!TryGetVisionNamespaceIndex(out ushort ns))
            {
                return NodeId.Null;
            }
            return await ResolveChildAsync(parent, browseName, ns, cancellationToken)
                .ConfigureAwait(false);
        }

        public async ValueTask<ArrayOf<NodeId>> ResolveChildrenAsync(
            NodeId parent,
            ArrayOf<string> browseNames,
            CancellationToken cancellationToken)
        {
            if (!TryGetVisionNamespaceIndex(out ushort ns))
            {
                var nulls = new List<NodeId>(browseNames.Count);
                for (int ii = 0; ii < browseNames.Count; ii++)
                {
                    nulls.Add(NodeId.Null);
                }
                return nulls.ToArrayOf();
            }
            return await ResolveChildrenAsync(parent, browseNames, ns, cancellationToken)
                .ConfigureAwait(false);
        }

        public async ValueTask<DataValue> ReadValueAsync(
            NodeId nodeId, CancellationToken cancellationToken)
        {
            if (nodeId.IsNull)
            {
                return DataValue.Null;
            }
            return await Session.ReadValueAsync(nodeId, cancellationToken).ConfigureAwait(false);
        }

        public async ValueTask<ArrayOf<DataValue>> ReadValuesAsync(
            ArrayOf<NodeId> nodeIds, CancellationToken cancellationToken)
        {
            var reads = new List<ReadValueId>(nodeIds.Count);
            for (int ii = 0; ii < nodeIds.Count; ii++)
            {
                if (nodeIds[ii].IsNull)
                {
                    continue;
                }
                reads.Add(new ReadValueId
                {
                    NodeId = nodeIds[ii],
                    AttributeId = Attributes.Value
                });
            }
            if (reads.Count == 0)
            {
                return ArrayOf<DataValue>.Empty;
            }
            ArrayOf<ReadValueId> nodesToRead = reads.ToArrayOf();
            ReadResponse response = await Session.ReadAsync(
                null, 0, TimestampsToReturn.Both, nodesToRead, cancellationToken)
                .ConfigureAwait(false);
            ClientBase.ValidateResponse(response.Results, nodesToRead);
            return response.Results;
        }

        public async ValueTask<T> ReadStructureAsync<T>(
            NodeId nodeId, CancellationToken cancellationToken)
            where T : class, IEncodeable
        {
            DataValue value = await ReadValueAsync(nodeId, cancellationToken).ConfigureAwait(false);
            if (StatusCode.IsBad(value.StatusCode))
            {
                throw new ServiceResultException(value.StatusCode);
            }
#pragma warning disable CS8600 // TryGetValue uses [MaybeNullWhen(false)] on encodeable overloads.
            if (!value.WrappedValue.TryGetValue(
                    out T structure, Session.MessageContext))
#pragma warning restore CS8600
            {
                throw new ServiceResultException(
                    StatusCodes.BadTypeMismatch,
                    $"Node '{nodeId}' does not contain a {typeof(T).Name} value.");
            }
            return structure;
        }

        public async ValueTask<T?> TryReadStructureAsync<T>(
            NodeId nodeId, CancellationToken cancellationToken)
            where T : class, IEncodeable
        {
            if (nodeId.IsNull)
            {
                return null;
            }
            DataValue value = await ReadValueAsync(nodeId, cancellationToken).ConfigureAwait(false);
            if (StatusCode.IsBad(value.StatusCode))
            {
                return null;
            }
#pragma warning disable CS8600 // TryGetValue uses [MaybeNullWhen(false)] on encodeable overloads.
            return value.WrappedValue.TryGetValue(
                    out T structure, Session.MessageContext)
                ? structure
                : null;
#pragma warning restore CS8600
        }

        public async ValueTask<ArrayOf<T>> ReadStructureArrayAsync<T>(
            NodeId nodeId, CancellationToken cancellationToken)
            where T : class, IEncodeable
        {
            DataValue value = await ReadValueAsync(nodeId, cancellationToken).ConfigureAwait(false);
            if (StatusCode.IsBad(value.StatusCode))
            {
                throw new ServiceResultException(value.StatusCode);
            }
            if (!value.WrappedValue.TryGetValue(
                    out ArrayOf<T> array, Session.MessageContext))
            {
                throw new ServiceResultException(
                    StatusCodes.BadTypeMismatch,
                    $"Node '{nodeId}' does not contain an array of {typeof(T).Name} values.");
            }
            return array;
        }

        public async ValueTask<ArrayOf<T>> TryReadStructureArrayAsync<T>(
            NodeId nodeId, CancellationToken cancellationToken)
            where T : class, IEncodeable
        {
            if (nodeId.IsNull)
            {
                return ArrayOf<T>.Empty;
            }
            DataValue value = await ReadValueAsync(nodeId, cancellationToken).ConfigureAwait(false);
            if (StatusCode.IsBad(value.StatusCode))
            {
                return ArrayOf<T>.Empty;
            }
            return value.WrappedValue.TryGetValue(
                    out ArrayOf<T> array, Session.MessageContext)
                ? array
                : ArrayOf<T>.Empty;
        }

        public static string? ReadString(DataValue value)
        {
            return value.WrappedValue.TryGetValue(out string? text) ? text : null;
        }

        public static bool TryReadEnum<TEnum>(DataValue value, out TEnum result)
            where TEnum : struct, Enum
        {
            if (value.WrappedValue.TryGetValue(out int intValue))
            {
                result = (TEnum)Enum.ToObject(typeof(TEnum), intValue);
                return true;
            }
            if (value.WrappedValue.TryGetValue(out uint uintValue))
            {
                result = (TEnum)Enum.ToObject(typeof(TEnum), uintValue);
                return true;
            }
            result = default;
            return false;
        }

        public static bool TryReadNodeId(DataValue value, out NodeId nodeId)
        {
            if (value.WrappedValue.TryGetValue(out NodeId candidate))
            {
                nodeId = candidate;
                return !nodeId.IsNull;
            }
            nodeId = NodeId.Null;
            return false;
        }

        private static BrowsePath CreateBrowsePath(
            NodeId parent, string browseName, ushort namespaceIndex)
        {
            return new BrowsePath
            {
                StartingNode = parent,
                RelativePath = new RelativePath
                {
                    Elements =
                    [
                        new RelativePathElement
                        {
                            ReferenceTypeId = Opc.Ua.ReferenceTypeIds.HierarchicalReferences,
                            IsInverse = false,
                            IncludeSubtypes = true,
                            TargetName = new QualifiedName(browseName, namespaceIndex)
                        }
                    ]
                }
            };
        }

        private static void RegisterEncodeableTypes(ISession session)
        {
            RegisterEncodeableTypes(session.Factory);
            if (!ReferenceEquals(session.MessageContext.Factory, session.Factory))
            {
                RegisterEncodeableTypes(session.MessageContext.Factory);
            }
        }

        private static void RegisterEncodeableTypes(IEncodeableFactory factory)
        {
            var probe = new VisionPose3DDataType();
            if (!factory.TryGetEncodeableType(probe.BinaryEncodingId, out _))
            {
                factory.Builder.AddOpcUaVision().Commit();
            }
        }
    }
}
