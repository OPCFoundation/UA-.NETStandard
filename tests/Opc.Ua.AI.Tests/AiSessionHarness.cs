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

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Opc.Ua.AI.Client;
using Opc.Ua.Client;

namespace Opc.Ua.AI.Tests
{
    internal sealed class AISessionHarness
    {
        private readonly Dictionary<(NodeId Parent, QualifiedName BrowseName), NodeId> m_children = [];
        private readonly Dictionary<NodeId, List<ReferenceDescription>> m_browse = [];
        private readonly HashSet<(NodeId TypeDefinition, NodeId SuperType)> m_subtypes = [];
        private readonly Dictionary<NodeId, Variant> m_values = [];

        public AISessionHarness()
        {
            Telemetry = new Mock<ITelemetryContext>().Object;
            NamespaceUris.GetIndexOrAppend(Opc.Ua.Namespaces.OpcUa);
            NamespaceUris.GetIndexOrAppend(Namespaces.AI);
            MessageContext = ServiceMessageContext.Create(Telemetry);
            MessageContext.NamespaceUris.GetIndexOrAppend(Opc.Ua.Namespaces.OpcUa);
            MessageContext.NamespaceUris.GetIndexOrAppend(Namespaces.AI);
            Session.SetupGet(s => s.NamespaceUris).Returns(NamespaceUris);
            Session.SetupGet(s => s.MessageContext).Returns(MessageContext);
            Session.SetupGet(s => s.Factory).Returns(MessageContext.Factory);
            Session.SetupGet(s => s.OperationLimits).Returns(new OperationLimits());
            Session.SetupGet(s => s.ServerCapabilities).Returns(new ServerCapabilities());
            Session.SetupGet(s => s.ContinuationPointPolicy).Returns(ContinuationPointPolicy.Default);
            Session.SetupGet(s => s.NodeCache).Returns(NodeCache.Object);
            NodeCache
                .Setup(c => c.IsTypeOfAsync(
                    It.IsAny<NodeId>(), It.IsAny<NodeId>(), It.IsAny<CancellationToken>()))
                .Returns<NodeId, NodeId, CancellationToken>((typeDefinition, superType, _) =>
                    new ValueTask<bool>(
                        typeDefinition == superType ||
                        m_subtypes.Contains((typeDefinition, superType))));
            SetupTranslate();
            SetupBrowse();
            SetupRead();
            AddChild(AIRootId, BrowseNames.Models, ModelsFolderId);
            AddChild(AIRootId, BrowseNames.Deployments, DeploymentsFolderId);
            Client = new AIClient(Session.Object, Telemetry);
        }

        public Mock<ISession> Session { get; } = new(MockBehavior.Loose);

        public Mock<INodeCache> NodeCache { get; } = new(MockBehavior.Loose);

        public ITelemetryContext Telemetry { get; }

        public NamespaceTable NamespaceUris { get; } = new();

        public ServiceMessageContext MessageContext { get; }

        public AIClient Client { get; }

        public ushort AINamespaceIndex => (ushort)NamespaceUris.GetIndex(Namespaces.AI);

        public NodeId AIRootId => NodeId.Create(Objects.AiModelManagement, Namespaces.AI, NamespaceUris);

        public NodeId ModelsFolderId => NodeId.Create(Objects.AiRootType_Models, Namespaces.AI, NamespaceUris);

        public NodeId DeploymentsFolderId => NodeId.Create(Objects.AiRootType_Deployments, Namespaces.AI, NamespaceUris);

        public NodeId ModelNodeId { get; } = new(2000u, 3);

        public NodeId DeploymentNodeId { get; } = new(3000u, 3);

        public void AddModel(string browseName = "Model1")
        {
            AddBrowse(ModelsFolderId, [Ref(ModelNodeId, browseName, ObjectTypes.ModelType)]);
        }

        public void AddDeployment(string browseName = "Deployment1")
        {
            AddBrowse(DeploymentsFolderId, [Ref(DeploymentNodeId, browseName, ObjectTypes.DeploymentType)]);
        }

        public ReferenceDescription Ref(NodeId nodeId, string browseName, uint typeId)
        {
            return new ReferenceDescription
            {
                NodeId = new ExpandedNodeId(nodeId),
                BrowseName = new QualifiedName(browseName, AINamespaceIndex),
                DisplayName = new LocalizedText(browseName),
                NodeClass = NodeClass.Object,
                TypeDefinition = new ExpandedNodeId(new NodeId(typeId, AINamespaceIndex)),
                ReferenceTypeId = Opc.Ua.ReferenceTypeIds.HierarchicalReferences,
                IsForward = true
            };
        }

        public void AddBrowse(NodeId folder, IReadOnlyList<ReferenceDescription> references)
        {
            m_browse[folder] = [.. references];
        }

        public void AddChild(NodeId parent, string browseName, NodeId child)
        {
            AddChild(parent, new QualifiedName(browseName, AINamespaceIndex), child);
        }

        public void AddChild(NodeId parent, string browseName, ushort namespaceIndex, NodeId child)
        {
            AddChild(parent, new QualifiedName(browseName, namespaceIndex), child);
        }

        public void AddSubtype(NodeId typeDefinition, NodeId superType)
        {
            m_subtypes.Add((typeDefinition, superType));
        }

        public void AddValueChild(NodeId parent, string browseName, NodeId nodeId, Variant value)
        {
            AddChild(parent, browseName, nodeId);
            m_values[nodeId] = value;
        }

        private void SetupTranslate()
        {
            Session.Setup(s => s.TranslateBrowsePathsToNodeIdsAsync(
                It.IsAny<RequestHeader>(),
                It.IsAny<ArrayOf<BrowsePath>>(),
                It.IsAny<CancellationToken>()))
                .Returns<RequestHeader, ArrayOf<BrowsePath>, CancellationToken>((_, paths, _) =>
                {
                    var results = new List<BrowsePathResult>();
                    for (int ii = 0; ii < paths.Count; ii++)
                    {
                        BrowsePath path = paths[ii];
                        NodeId current = path.StartingNode;
                        bool found = true;
                        for (int jj = 0; jj < path.RelativePath.Elements.Count; jj++)
                        {
                            QualifiedName browseName = path.RelativePath.Elements[jj].TargetName;
                            if (!m_children.TryGetValue((current, browseName), out NodeId next))
                            {
                                found = false;
                                break;
                            }
                            current = next;
                        }
                        results.Add(found ? GoodPath(current) : BadPath());
                    }
                    return new ValueTask<TranslateBrowsePathsToNodeIdsResponse>(
                        new TranslateBrowsePathsToNodeIdsResponse
                        {
                            ResponseHeader = new ResponseHeader(),
                            Results = results.ToArrayOf(),
                            DiagnosticInfos = default
                        });
                });
        }

        private void SetupBrowse()
        {
            Session.Setup(s => s.BrowseAsync(
                It.IsAny<RequestHeader>(),
                It.IsAny<ViewDescription>(),
                It.IsAny<uint>(),
                It.IsAny<ArrayOf<BrowseDescription>>(),
                It.IsAny<CancellationToken>()))
                .Returns<RequestHeader, ViewDescription, uint, ArrayOf<BrowseDescription>, CancellationToken>(
                    (_, _, _, descriptions, _) =>
                    {
                        var results = new List<BrowseResult>(descriptions.Count);
                        for (int ii = 0; ii < descriptions.Count; ii++)
                        {
                            List<ReferenceDescription> refs = m_browse.TryGetValue(
                                descriptions[ii].NodeId, out List<ReferenceDescription>? value)
                                ? value
                                : [];
                            results.Add(new BrowseResult
                            {
                                StatusCode = StatusCodes.Good,
                                References = refs.ToArrayOf(),
                                ContinuationPoint = default
                            });
                        }
                        return new ValueTask<BrowseResponse>(new BrowseResponse
                        {
                            ResponseHeader = new ResponseHeader(),
                            Results = results.ToArrayOf(),
                            DiagnosticInfos = default
                        });
                    });
            Session.Setup(s => s.BrowseNextAsync(
                It.IsAny<RequestHeader>(),
                It.IsAny<bool>(),
                It.IsAny<ArrayOf<ByteString>>(),
                It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<BrowseNextResponse>(new BrowseNextResponse
                {
                    ResponseHeader = new ResponseHeader(),
                    Results = [new BrowseResult { StatusCode = StatusCodes.Good, References = [] }],
                    DiagnosticInfos = default
                }));
        }

        private void SetupRead()
        {
            Session.Setup(s => s.ReadAsync(
                It.IsAny<RequestHeader>(),
                It.IsAny<double>(),
                It.IsAny<TimestampsToReturn>(),
                It.IsAny<ArrayOf<ReadValueId>>(),
                It.IsAny<CancellationToken>()))
                .Returns<RequestHeader, double, TimestampsToReturn, ArrayOf<ReadValueId>, CancellationToken>(
                    (_, _, _, nodes, _) =>
                    {
                        var values = new List<DataValue>();
                        for (int ii = 0; ii < nodes.Count; ii++)
                        {
                            values.Add(m_values.TryGetValue(nodes[ii].NodeId, out Variant variant)
                                ? new DataValue(variant, StatusCodes.Good, System.DateTime.UtcNow, System.DateTime.UtcNow)
                                : new DataValue(Variant.Null, StatusCodes.BadNodeIdUnknown));
                        }
                        return new ValueTask<ReadResponse>(new ReadResponse
                        {
                            ResponseHeader = new ResponseHeader(),
                            Results = values.ToArrayOf(),
                            DiagnosticInfos = default
                        });
                    });
        }

        private static BrowsePathResult GoodPath(NodeId nodeId)
        {
            return new BrowsePathResult
            {
                StatusCode = StatusCodes.Good,
                Targets = [new BrowsePathTarget { TargetId = new ExpandedNodeId(nodeId) }]
            };
        }

        private static BrowsePathResult BadPath()
        {
            return new BrowsePathResult { StatusCode = StatusCodes.BadNoMatch, Targets = [] };
        }

        private void AddChild(NodeId parent, QualifiedName browseName, NodeId child)
        {
            m_children[(parent, browseName)] = child;
        }
    }
}
