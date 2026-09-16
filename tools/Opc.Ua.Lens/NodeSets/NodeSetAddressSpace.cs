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
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using UaLens.NodeSets.Loading;

namespace UaLens.NodeSets
{
    /// <summary>
    /// Builds an isolated, read-only graph without starting a server or opening a session.
    /// </summary>
    internal interface INodeSetAddressSpaceFactory
    {
        Task<NodeSetAddressSpace> CreateAsync(
            ArrayOf<NodeSetDocument> documents, CancellationToken cancellationToken = default);
    }

    internal sealed class NodeSetAddressSpaceFactory : INodeSetAddressSpaceFactory
    {
        public NodeSetAddressSpaceFactory(ITelemetryContext telemetry)
        {
            m_telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
        }

        public Task<NodeSetAddressSpace> CreateAsync(
            ArrayOf<NodeSetDocument> documents, CancellationToken cancellationToken = default)
        {
            return Task.Run(
                () => new NodeSetAddressSpace(m_telemetry, documents, cancellationToken), cancellationToken);
        }

        private readonly ITelemetryContext m_telemetry;
    }

    /// <summary>
    /// Imports namespace-stable attributes and indexes both directions of every reference.
    /// The graph is private and is never mutated after construction.
    /// </summary>
    internal sealed class NodeSetAddressSpace
    {
        internal NodeSetAddressSpace(
            ITelemetryContext telemetry,
            ArrayOf<NodeSetDocument> documents,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(telemetry);
            if (documents.Count == 0)
            {
                throw new ArgumentException("Select at least one NodeSet2 document.", nameof(documents));
            }
            Documents = documents;
            NamespaceUris = new NamespaceTable();
            var types = new TypeTable(NamespaceUris);
            m_context = new SystemContext(telemetry)
            {
                NamespaceUris = NamespaceUris,
                ServerUris = new StringTable(),
                TypeTable = types,
                EncodeableFactory = EncodeableFactory.Create()
            };
            // Server index zero denotes this graph, never the first remote server.
            m_context.ServerUris.Append("urn:ualens:offline");
            foreach (NodeSetDocument document in documents)
            {
                foreach (string uri in document.NodeSet.NamespaceUris ?? [])
                {
                    NamespaceUris.GetIndexOrAppend(uri);
                }
            }
            foreach (NodeSetDocument document in documents)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var imported = new NodeStateCollection();
                document.NodeSet.Import(m_context, imported, stateFactory: null, linkParentChild: false);
                for (int i = 0; i < imported.Count; i++)
                {
                    NodeState node = imported[i];
                    cancellationToken.ThrowIfCancellationRequested();
                    if (node.NodeId.IsNull || !m_nodes.TryAdd(node.NodeId, node))
                    {
                        throw new InvalidOperationException(
                            $"Duplicate or null NodeId '{node.NodeId}' in '{document.Source}'.");
                    }
                    m_authored.Add(node.NodeId, (document, document.NodeSet.Items![i]));
                }
            }
            RegisterTypes(types, cancellationToken);
            var edges = new HashSet<(NodeId Source, NodeId Type, bool Inverse, ExpandedNodeId Target)>();
            foreach (NodeState node in m_nodes.Values)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var references = new List<IReference>();
                node.GetReferences(m_context, references);
                foreach (IReference reference in references)
                {
                    AddEdge(node.NodeId, reference.ReferenceTypeId, reference.IsInverse, reference.TargetId);
                }
                // These links are stored as NodeState properties, not in GetReferences().
                if (node is BaseTypeState type && !type.SuperTypeId.IsNull)
                {
                    AddEdge(node.NodeId, ReferenceTypeIds.HasSubtype, true, type.SuperTypeId);
                }
                if (node is BaseInstanceState instance)
                {
                    if (!instance.TypeDefinitionId.IsNull)
                    {
                        AddEdge(node.NodeId, ReferenceTypeIds.HasTypeDefinition, false, instance.TypeDefinitionId);
                    }
                    if (!instance.ModellingRuleId.IsNull)
                    {
                        AddEdge(node.NodeId, ReferenceTypeIds.HasModellingRule, false, instance.ModellingRuleId);
                    }
                }
            }
            foreach ((NodeId source, NodeId type, bool inverse, ExpandedNodeId target) in edges)
            {
                NodeId targetId = LocalId(target);
                m_nodes.TryGetValue(targetId, out NodeState? targetNode);
                ReferenceDescription description = Describe(targetNode, target);
                description.ReferenceTypeId = type;
                description.IsForward = !inverse;
                if (!m_references.TryGetValue(source, out List<ReferenceDescription>? references))
                {
                    references = [];
                    m_references.Add(source, references);
                }
                references.Add(description);
                if (targetNode is null)
                {
                    UnresolvedReferenceCount++;
                }
            }
            Nodes = [.. m_nodes.Values.OrderBy(node => node.BrowseName.Name, StringComparer.Ordinal)
                .ThenBy(node => node.NodeId.ToString(), StringComparer.Ordinal)
                .Select(node => Describe(node, node.NodeId))];

            void AddEdge(NodeId source, NodeId type, bool inverse, ExpandedNodeId target)
            {
                NodeId targetId = LocalId(target);
                ExpandedNodeId normalized = targetId.IsNull ? target : targetId;
                edges.Add((source, type, inverse, normalized));
                if (!targetId.IsNull && m_nodes.ContainsKey(targetId))
                {
                    edges.Add((targetId, type, !inverse, source));
                }
            }
        }

        public NamespaceTable NamespaceUris { get; }
        public ArrayOf<NodeSetDocument> Documents { get; }
        public ArrayOf<ReferenceDescription> Nodes { get; }
        public int UnresolvedReferenceCount { get; }

        public Task<BrowseResponse> BrowseAsync(
            ArrayOf<BrowseDescription> descriptions, CancellationToken cancellationToken = default)
        {
            var results = new List<BrowseResult>(descriptions.Count);
            foreach (BrowseDescription description in descriptions)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!m_nodes.ContainsKey(description.NodeId))
                {
                    results.Add(new BrowseResult { StatusCode = StatusCodes.BadNodeIdUnknown });
                    continue;
                }
                var references = new List<ReferenceDescription>();
                if (m_references.TryGetValue(description.NodeId, out List<ReferenceDescription>? candidates))
                {
                    foreach (ReferenceDescription reference in candidates)
                    {
                        if ((description.BrowseDirection == BrowseDirection.Forward && !reference.IsForward) ||
                            (description.BrowseDirection == BrowseDirection.Inverse && reference.IsForward) ||
                            (description.NodeClassMask != 0 &&
                                ((uint)reference.NodeClass & description.NodeClassMask) == 0))
                        {
                            continue;
                        }
                        if (description.ReferenceTypeId.IsNull ||
                            reference.ReferenceTypeId == description.ReferenceTypeId ||
                            (description.IncludeSubtypes &&
                                m_context.TypeTable.IsTypeOf(reference.ReferenceTypeId, description.ReferenceTypeId)))
                        {
                            references.Add(reference);
                        }
                    }
                }
                results.Add(new BrowseResult { References = [.. references] });
            }
            return Task.FromResult(new BrowseResponse { Results = [.. results] });
        }

        public async Task<ReadResponse> ReadAsync(
            ArrayOf<ReadValueId> attributes, CancellationToken cancellationToken = default)
        {
            var results = new List<DataValue>(attributes.Count);
            for (int i = 0; i < attributes.Count; i++)
            {
                ReadValueId attribute = attributes[i];
                cancellationToken.ThrowIfCancellationRequested();
                if (!m_nodes.TryGetValue(attribute.NodeId, out NodeState? node))
                {
                    results.Add(DataValue.FromStatusCode(StatusCodes.BadNodeIdUnknown));
                    continue;
                }
                (ServiceResult result, DataValue value) = await node.ReadAttributeAsync(
                    m_context, attribute.AttributeId, default, attribute.DataEncoding,
                    default, cancellationToken).ConfigureAwait(false);
                results.Add(ServiceResult.IsBad(result) ? DataValue.FromStatusCode(result.StatusCode) : value);
            }
            return new ReadResponse { Results = [.. results] };
        }

        public async Task<ArrayOf<NodeId>> ResolvePathAsync(
            NodeId anchor, string path, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!m_nodes.ContainsKey(anchor))
            {
                throw new ServiceResultException(StatusCodes.BadNodeIdUnknown);
            }
            var relative = RelativePath.Parse(path, m_context.TypeTable);
            ArrayOf<NodeId> current = [anchor];
            for (int i = 0; i < relative.Elements.Count; i++)
            {
                RelativePathElement element = relative.Elements[i];
                var next = new HashSet<NodeId>();
                for (int j = 0; j < current.Count; j++)
                {
                    NodeId nodeId = current[j];
                    BrowseResponse response = await BrowseAsync(
                        [new BrowseDescription
                        {
                            NodeId = nodeId,
                            BrowseDirection = element.IsInverse ? BrowseDirection.Inverse : BrowseDirection.Forward,
                            ReferenceTypeId = element.ReferenceTypeId,
                            IncludeSubtypes = element.IncludeSubtypes,
                            ResultMask = (uint)BrowseResultMask.All
                        }], cancellationToken).ConfigureAwait(false);
                    foreach (ReferenceDescription reference in response.Results[0].References)
                    {
                        NodeId local = LocalId(reference.NodeId);
                        if (!local.IsNull && (element.TargetName.IsNull || reference.BrowseName == element.TargetName))
                        {
                            next.Add(local);
                        }
                    }
                }
                current = [.. next];
            }
            return current;
        }

        public Task<string> ReadNodeXmlAsync(NodeId nodeId, CancellationToken cancellationToken = default)
        {
            return Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!m_authored.TryGetValue(nodeId, out (NodeSetDocument Document, Opc.Ua.Export.UANode Node) authored))
                {
                    throw new ServiceResultException(StatusCodes.BadNodeIdUnknown);
                }
                var nodeSet = new Opc.Ua.Export.UANodeSet
                {
                    NamespaceUris = authored.Document.NodeSet.NamespaceUris,
                    ServerUris = authored.Document.NodeSet.ServerUris,
                    Models = authored.Document.NodeSet.Models,
                    Aliases = authored.Document.NodeSet.Aliases,
                    Items = [authored.Node]
                };
                using var stream = new MemoryStream();
                nodeSet.Write(stream);
                cancellationToken.ThrowIfCancellationRequested();
                return Encoding.UTF8.GetString(stream.ToArray());
            }, cancellationToken);
        }

        private NodeId LocalId(ExpandedNodeId target)
        {
            return target.ServerIndex != 0 ? NodeId.Null : ExpandedNodeId.ToNodeId(target, NamespaceUris);
        }

        private static ReferenceDescription Describe(NodeState? node, ExpandedNodeId nodeId)
        {
            return new ReferenceDescription
            {
                NodeId = nodeId,
                BrowseName = node?.BrowseName ?? QualifiedName.Null,
                DisplayName = node?.DisplayName ?? new LocalizedText($"(unresolved) {nodeId}"),
                NodeClass = node?.NodeClass ?? NodeClass.Unspecified,
                TypeDefinition = node is BaseInstanceState instance ? instance.TypeDefinitionId : ExpandedNodeId.Null
            };
        }

        private void RegisterTypes(TypeTable types, CancellationToken cancellationToken)
        {
            var allTypes = m_nodes.Values.OfType<BaseTypeState>().ToDictionary(node => node.NodeId);
            var complete = new HashSet<NodeId>();
            foreach (BaseTypeState type in allTypes.Values)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var path = new HashSet<NodeId>();
                var chain = new Stack<BaseTypeState>();
                BaseTypeState? current = type;
                while (current is not null && !complete.Contains(current.NodeId))
                {
                    if (!path.Add(current.NodeId))
                    {
                        throw new InvalidOperationException($"Cyclic HasSubtype hierarchy at '{current.NodeId}'.");
                    }
                    chain.Push(current);
                    allTypes.TryGetValue(current.SuperTypeId, out current);
                }
                while (chain.TryPop(out BaseTypeState? next))
                {
                    NodeId parent = allTypes.ContainsKey(next.SuperTypeId) ? next.SuperTypeId : NodeId.Null;
                    if (next is ReferenceTypeState)
                    {
                        types.AddReferenceSubtype(next.NodeId, parent, next.BrowseName);
                    }
                    else
                    {
                        types.AddSubtype(next.NodeId, parent);
                    }
                    complete.Add(next.NodeId);
                }
            }
        }

        private readonly SystemContext m_context;
        private readonly Dictionary<NodeId, NodeState> m_nodes = [];
        private readonly Dictionary<NodeId, (NodeSetDocument Document, Opc.Ua.Export.UANode Node)> m_authored = [];
        private readonly Dictionary<NodeId, List<ReferenceDescription>> m_references = [];
    }
}
