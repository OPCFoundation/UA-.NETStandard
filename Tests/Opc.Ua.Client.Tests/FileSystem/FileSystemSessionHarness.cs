/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using Moq;
using Opc.Ua.Tests;

namespace Opc.Ua.Client.Tests.FileSystem
{
    /// <summary>
    /// Scriptable in-memory mock of an OPC UA <see cref="ISession"/>
    /// that exposes a tiny FileType / FileDirectoryType address space
    /// and answers <c>TranslateBrowsePathsToNodeIds</c> /
    /// <c>Browse</c> / <c>Read</c> / <c>Call</c> requests against it.
    /// Used by the high-level <c>FileSystemClient</c> tests instead of
    /// a live server.
    /// </summary>
    /// <remarks>
    /// The fake address space is a plain <c>Dictionary</c> graph:
    /// each registered node carries a <see cref="NodeId"/>,
    /// <see cref="QualifiedName"/>, type definition (FileType or
    /// FileDirectoryType), and (optionally) a property bag for
    /// FileType metadata (Size, Writable, MimeType, …). Hierarchical
    /// relationships are tracked per parent in
    /// <see cref="ChildrenOf"/>.
    /// </remarks>
    internal sealed class FileSystemSessionHarness
    {
        public Mock<ISession> SessionMock { get; }
        public ISession Session => SessionMock.Object;
        public IServiceMessageContext MessageContext { get; }

        public NodeId Root { get; }
        public Dictionary<NodeId, FakeNode> Nodes { get; } = [];
        public Dictionary<NodeId, List<NodeId>> ChildrenOf { get; } = [];

        public List<CallMethodRequest> CallRequests { get; } = [];
        public Func<CallMethodRequest, CallMethodResult> CallHandler { get; set; }

        private FileSystemSessionHarness(
            Mock<ISession> mock,
            IServiceMessageContext messageContext,
            NodeId rootId)
        {
            SessionMock = mock;
            MessageContext = messageContext;
            Root = rootId;
        }

        public static FileSystemSessionHarness Create(NodeId rootId = default)
        {
            if (rootId.IsNull)
            {
                rootId = new NodeId(1000);
            }

            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            IServiceMessageContext messageContext = ServiceMessageContext.Create(telemetry);

            var sessionMock = new Mock<ISession>(MockBehavior.Loose);
            sessionMock.SetupGet(s => s.MessageContext).Returns(messageContext);

            var typeTree = new Mock<ITypeTable>(MockBehavior.Loose);
            // Subtype check: nothing is a subtype except equality (the
            // harness's nodes use the exact base type NodeId).
            typeTree
                .Setup(t => t.IsTypeOf(It.IsAny<NodeId>(), It.IsAny<NodeId>()))
                .Returns<NodeId, NodeId>((sub, super) => sub.Equals(super));
            sessionMock.SetupGet(s => s.TypeTree).Returns(typeTree.Object);
            sessionMock
                .Setup(s => s.FetchTypeTreeAsync(
                    It.IsAny<ExpandedNodeId>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            var harness = new FileSystemSessionHarness(sessionMock, messageContext, rootId);
            harness.RegisterNode(rootId, new QualifiedName("FileSystem"),
                ObjectTypeIds.FileDirectoryType, isDirectory: true);

            // Wire up the per-service handlers.
            sessionMock
                .Setup(s => s.TranslateBrowsePathsToNodeIdsAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<ArrayOf<BrowsePath>>(),
                    It.IsAny<CancellationToken>()))
                .Returns<RequestHeader, ArrayOf<BrowsePath>, CancellationToken>(
                    (_, paths, _) =>
                    {
                        var results = new BrowsePathResult[paths.Count];
                        for (int i = 0; i < paths.Count; i++)
                        {
                            results[i] = harness.ResolveBrowsePath(paths[i]);
                        }
                        var response = new TranslateBrowsePathsToNodeIdsResponse
                        {
                            ResponseHeader = new ResponseHeader(),
                            Results = results.ToArrayOf(),
                            DiagnosticInfos = default
                        };
                        return new ValueTask<TranslateBrowsePathsToNodeIdsResponse>(response);
                    });

            sessionMock
                .Setup(s => s.BrowseAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<ViewDescription>(),
                    It.IsAny<uint>(),
                    It.IsAny<ArrayOf<BrowseDescription>>(),
                    It.IsAny<CancellationToken>()))
                .Returns<RequestHeader, ViewDescription, uint, ArrayOf<BrowseDescription>,
                    CancellationToken>(
                    (_, _, _, descriptions, _) =>
                    {
                        var results = new BrowseResult[descriptions.Count];
                        for (int i = 0; i < descriptions.Count; i++)
                        {
                            results[i] = harness.ResolveBrowse(descriptions[i]);
                        }
                        var response = new BrowseResponse
                        {
                            ResponseHeader = new ResponseHeader(),
                            Results = results.ToArrayOf(),
                            DiagnosticInfos = default
                        };
                        return new ValueTask<BrowseResponse>(response);
                    });

            sessionMock
                .Setup(s => s.ReadAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<double>(),
                    It.IsAny<TimestampsToReturn>(),
                    It.IsAny<ArrayOf<ReadValueId>>(),
                    It.IsAny<CancellationToken>()))
                .Returns<RequestHeader, double, TimestampsToReturn, ArrayOf<ReadValueId>,
                    CancellationToken>(
                    (_, _, _, nodesToRead, _) =>
                    {
                        var results = new DataValue[nodesToRead.Count];
                        for (int i = 0; i < nodesToRead.Count; i++)
                        {
                            results[i] = harness.ResolveRead(nodesToRead[i]);
                        }
                        var response = new ReadResponse
                        {
                            ResponseHeader = new ResponseHeader(),
                            Results = results.ToArrayOf(),
                            DiagnosticInfos = default
                        };
                        return new ValueTask<ReadResponse>(response);
                    });

            sessionMock
                .Setup(s => s.CallAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<ArrayOf<CallMethodRequest>>(),
                    It.IsAny<CancellationToken>()))
                .Returns<RequestHeader, ArrayOf<CallMethodRequest>, CancellationToken>(
                    (_, methodsToCall, _) =>
                    {
                        var results = new CallMethodResult[methodsToCall.Count];
                        for (int i = 0; i < methodsToCall.Count; i++)
                        {
                            CallMethodRequest req = methodsToCall[i];
                            harness.CallRequests.Add(req);
                            results[i] = harness.CallHandler != null
                                ? harness.CallHandler(req)
                                : new CallMethodResult
                                {
                                    StatusCode = StatusCodes.Good,
                                    OutputArguments = Array.Empty<Variant>().ToArrayOf()
                                };
                        }
                        var response = new CallResponse
                        {
                            ResponseHeader = new ResponseHeader(),
                            Results = results.ToArrayOf(),
                            DiagnosticInfos = default
                        };
                        return new ValueTask<CallResponse>(response);
                    });

            return harness;
        }

        public NodeId RegisterDirectory(NodeId parent, QualifiedName name, NodeId childId = default)
        {
            if (childId.IsNull)
            {
                childId = new NodeId((uint)s_nextId++);
            }
            RegisterNode(childId, name, ObjectTypeIds.FileDirectoryType, isDirectory: true);
            LinkChild(parent, childId);
            return childId;
        }

        public NodeId RegisterFile(
            NodeId parent,
            QualifiedName name,
            NodeId childId = default,
            FileProperties properties = null)
        {
            if (childId.IsNull)
            {
                childId = new NodeId((uint)s_nextId++);
            }
            RegisterNode(childId, name, ObjectTypeIds.FileType, isDirectory: false,
                properties: properties);
            LinkChild(parent, childId);
            return childId;
        }

        /// <summary>
        /// Registers a child object with an arbitrary type definition;
        /// useful for tests that need to assert filtering behaviour.
        /// </summary>
        public NodeId RegisterObject(
            NodeId parent, QualifiedName name, NodeId typeDef, NodeId childId = default)
        {
            if (childId.IsNull)
            {
                childId = new NodeId((uint)s_nextId++);
            }
            Nodes[childId] = new FakeNode(childId, name, typeDef, isDirectory: false);
            LinkChild(parent, childId);
            return childId;
        }

        private void RegisterNode(
            NodeId nodeId,
            QualifiedName name,
            NodeId typeDefinition,
            bool isDirectory,
            FileProperties properties = null)
        {
            Nodes[nodeId] = new FakeNode(nodeId, name, typeDefinition, isDirectory, properties);
        }

        private void LinkChild(NodeId parent, NodeId child)
        {
            if (!ChildrenOf.TryGetValue(parent, out List<NodeId> list))
            {
                list = [];
                ChildrenOf[parent] = list;
            }
            list.Add(child);
        }

        // ---- Request resolvers -----------------------------------------

        /// <summary>
        /// Test-only accessor exposing the harness's
        /// <c>ResolveBrowsePath</c> logic so individual tests can wire
        /// up their own <c>Setup(...).Returns(...)</c> handlers that
        /// also delegate back into the in-memory state.
        /// </summary>
        public BrowsePathResult ResolveBrowsePathForTest(BrowsePath path)
        {
            return ResolveBrowsePath(path);
        }

        private BrowsePathResult ResolveBrowsePath(BrowsePath path)
        {
            NodeId current = path.StartingNode;
            foreach (RelativePathElement element in path.RelativePath.Elements)
            {
                NodeId match = NodeId.Null;

                // Look at child nodes first.
                if (ChildrenOf.TryGetValue(current, out List<NodeId> children))
                {
                    foreach (NodeId childId in children)
                    {
                        FakeNode child = Nodes[childId];
                        if (child.Name.Equals(element.TargetName))
                        {
                            match = childId;
                            break;
                        }
                    }
                }

                // Fall back to the property bag (FileType metadata).
                if (match.IsNull &&
                    Nodes.TryGetValue(current, out FakeNode owner) &&
                    owner.Properties != null &&
                    owner.Properties.TryGetProperty(element.TargetName, out NodeId propId))
                {
                    match = propId;
                }

                if (match.IsNull)
                {
                    return BadResult(StatusCodes.BadNoMatch);
                }
                current = match;
            }
            var target = new BrowsePathTarget
            {
                TargetId = current,
                RemainingPathIndex = uint.MaxValue
            };
            return new BrowsePathResult
            {
                StatusCode = StatusCodes.Good,
                Targets = new[] { target }.ToArrayOf()
            };
        }

        private static BrowsePathResult BadResult(StatusCode code)
        {
            return new BrowsePathResult
            {
                StatusCode = code,
                Targets = Array.Empty<BrowsePathTarget>().ToArrayOf()
            };
        }

        private BrowseResult ResolveBrowse(BrowseDescription description)
        {
            NodeId source = description.NodeId;
            var refs = new List<ReferenceDescription>();

            // HasTypeDefinition browse — used by ReadTypeDefinitionAsync
            // to classify a single object.
            if (description.ReferenceTypeId.Equals(ReferenceTypeIds.HasTypeDefinition))
            {
                if (Nodes.TryGetValue(source, out FakeNode node) && !node.TypeDefinition.IsNull)
                {
                    refs.Add(new ReferenceDescription
                    {
                        ReferenceTypeId = ReferenceTypeIds.HasTypeDefinition,
                        IsForward = true,
                        NodeId = node.TypeDefinition,
                        NodeClass = NodeClass.ObjectType
                    });
                }
            }
            else
            {
                // Hierarchical browse — used by EnumerateChildrenAsync.
                if (ChildrenOf.TryGetValue(source, out List<NodeId> children))
                {
                    foreach (NodeId childId in children)
                    {
                        FakeNode child = Nodes[childId];
                        if (((uint)NodeClass.Object & description.NodeClassMask) != 0)
                        {
                            // Only emit Object-class children.
                            refs.Add(new ReferenceDescription
                            {
                                ReferenceTypeId = ReferenceTypeIds.Organizes,
                                IsForward = true,
                                NodeId = childId,
                                BrowseName = child.Name,
                                DisplayName = new LocalizedText(child.Name.Name),
                                NodeClass = NodeClass.Object,
                                TypeDefinition = child.TypeDefinition
                            });
                        }
                    }
                }
            }

            return new BrowseResult
            {
                StatusCode = StatusCodes.Good,
                ContinuationPoint = default,
                References = refs.ToArrayOf()
            };
        }

        private DataValue ResolveRead(ReadValueId rvi)
        {
            if (rvi.AttributeId == Attributes.BrowseName)
            {
                if (Nodes.TryGetValue(rvi.NodeId, out FakeNode node))
                {
                    return new DataValue(new Variant(node.Name));
                }
                return new DataValue(StatusCodes.BadNodeIdUnknown);
            }
            if (rvi.AttributeId == Attributes.Value)
            {
                // For property NodeIds we look up the owning file and
                // read the value off the FileProperties bag.
                foreach (FakeNode candidate in Nodes.Values)
                {
                    if (candidate.Properties == null)
                    {
                        continue;
                    }
                    Variant? value = candidate.Properties.TryGetPropertyValue(rvi.NodeId);
                    if (value != null)
                    {
                        return new DataValue(value.Value);
                    }
                }
                return new DataValue(StatusCodes.BadNodeIdUnknown);
            }
            return new DataValue(StatusCodes.BadAttributeIdInvalid);
        }

        private static int s_nextId = 2000;

        internal sealed class FakeNode
        {
            public FakeNode(
                NodeId id,
                QualifiedName name,
                NodeId typeDefinition,
                bool isDirectory,
                FileProperties properties = null)
            {
                Id = id;
                Name = name;
                TypeDefinition = typeDefinition;
                IsDirectory = isDirectory;
                Properties = properties;
            }

            public NodeId Id { get; }
            public QualifiedName Name { get; }
            public NodeId TypeDefinition { get; }
            public bool IsDirectory { get; }
            public FileProperties Properties { get; }
        }
    }

    /// <summary>
    /// Backing store for the seven well-known FileType properties used
    /// by the FileSystem harness. Each property is identified by its
    /// browse name in the standard UA namespace; the harness allocates
    /// fresh NodeIds for them on demand.
    /// </summary>
    internal sealed class FileProperties
    {
        private readonly Dictionary<QualifiedName, NodeId> m_propertyNodeIds = [];
        private readonly Dictionary<NodeId, Variant> m_values = [];
        private static int s_nextId = 5000;

        public ulong? Size { get; set; }
        public bool? Writable { get; set; }
        public bool? UserWritable { get; set; }
        public ushort? OpenCount { get; set; }
        public string MimeType { get; set; }
        public uint? MaxByteStringLength { get; set; }
        public DateTime? LastModifiedTime { get; set; }

        public void Realize()
        {
            BindProperty("Size", Size.HasValue ? new Variant(Size.Value) : default);
            BindProperty("Writable", Writable.HasValue ? new Variant(Writable.Value) : default);
            BindProperty("UserWritable",
                UserWritable.HasValue ? new Variant(UserWritable.Value) : default);
            BindProperty("OpenCount",
                OpenCount.HasValue ? new Variant(OpenCount.Value) : default);
            if (MimeType != null)
            {
                BindProperty("MimeType", new Variant(MimeType));
            }
            if (MaxByteStringLength.HasValue)
            {
                BindProperty("MaxByteStringLength",
                    new Variant(MaxByteStringLength.Value));
            }
            if (LastModifiedTime.HasValue)
            {
                BindProperty("LastModifiedTime",
                    new Variant(LastModifiedTime.Value));
            }
        }

        public bool TryGetProperty(QualifiedName name, out NodeId nodeId)
        {
            return m_propertyNodeIds.TryGetValue(name, out nodeId);
        }

        public Variant? TryGetPropertyValue(NodeId propertyNodeId)
        {
            if (m_values.TryGetValue(propertyNodeId, out Variant value))
            {
                return value;
            }
            return null;
        }

        private void BindProperty(string name, Variant value)
        {
            var qname = new QualifiedName(name);
            var nodeId = new NodeId((uint)s_nextId++);
            m_propertyNodeIds[qname] = nodeId;
            m_values[nodeId] = value;
        }
    }
}
