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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Opc.Ua.Client;

namespace Opc.Ua.OpenUsd.Client.Tests
{
    /// <summary>
    /// A tiny in-memory OPC UA address space that answers Browse and Read requests,
    /// so the connector's discovery, resolution and authorisation paths can be driven
    /// deterministically without a live server.
    /// </summary>
    internal sealed class FakeAddressSpace
    {
        /// <summary>
        /// One reference held by the fake address space.
        /// </summary>
        private sealed class FakeReference
        {
            public NodeId Source { get; init; } = NodeId.Null;
            public NodeId Target { get; init; } = NodeId.Null;
            public NodeId ReferenceTypeId { get; init; } = NodeId.Null;
            public QualifiedName BrowseName { get; init; } = QualifiedName.Null;
            public NodeClass NodeClass { get; init; }
            public NodeId TypeDefinition { get; init; } = NodeId.Null;
        }

        private readonly List<FakeReference> m_references = [];
        private readonly Dictionary<NodeId, DataValue> m_values = [];
        private readonly Dictionary<NodeId, DataValue> m_accessLevels = [];
        private readonly Dictionary<NodeId, DataValue> m_rolePermissions = [];
        private readonly Dictionary<NodeId, DataValue> m_executables = [];
        private readonly Dictionary<NodeId, byte[]> m_fileContents = [];
        private readonly Dictionary<NodeId, (NodeId File, string Name)> m_fileMethods = [];
        private readonly Dictionary<uint, (NodeId File, int Offset)> m_openHandles = [];
        private int m_nextIdentifier = 5000;
        private int m_nextHandle;

        /// <summary>
        /// The namespace table shared with the fake session.
        /// </summary>
        public NamespaceTable NamespaceUris { get; } = new();

        /// <summary>
        /// The namespace index the OpenUSD companion specification occupies.
        /// </summary>
        public ushort OpenUsdNamespaceIndex { get; }

        /// <summary>
        /// Number of Browse service invocations served so far.
        /// </summary>
        public int BrowseCount { get; private set; }

        /// <summary>
        /// Number of Read service invocations served so far.
        /// </summary>
        public int ReadCount { get; private set; }

        /// <summary>
        /// Creates an address space whose namespace table carries the OpenUSD namespace.
        /// </summary>
        public FakeAddressSpace()
        {
            OpenUsdNamespaceIndex = (ushort)NamespaceUris.GetIndexOrAppend(OpenUsdModel.NamespaceUri);
        }

        /// <summary>
        /// Allocates a fresh numeric NodeId in namespace zero.
        /// </summary>
        public NodeId NewNodeId()
        {
            return new NodeId((uint)Interlocked.Increment(ref m_nextIdentifier));
        }

        /// <summary>
        /// Adds an Object child reachable through the supplied hierarchical reference type.
        /// </summary>
        public NodeId AddObject(
            NodeId parent,
            string browseName,
            NodeId typeDefinition = default,
            NodeId referenceTypeId = default,
            ushort browseNameNamespace = 0,
            NodeId nodeId = default)
        {
            return Add(parent, browseName, NodeClass.Object, typeDefinition,
                referenceTypeId.IsNull ? ReferenceTypeIds.Organizes : referenceTypeId,
                browseNameNamespace, nodeId);
        }

        /// <summary>
        /// Adds a Variable child carrying the supplied value.
        /// </summary>
        public NodeId AddVariable(
            NodeId parent,
            string browseName,
            Variant value,
            NodeId referenceTypeId = default,
            ushort browseNameNamespace = 0,
            NodeId nodeId = default)
        {
            NodeId id = Add(parent, browseName, NodeClass.Variable, NodeId.Null,
                referenceTypeId.IsNull ? ReferenceTypeIds.HasComponent : referenceTypeId,
                browseNameNamespace, nodeId);
            m_values[id] = new DataValue(value);
            return id;
        }

        /// <summary>
        /// Adds a Method child.
        /// </summary>
        public NodeId AddMethod(NodeId parent, string browseName)
        {
            return Add(parent, browseName, NodeClass.Method, NodeId.Null,
                ReferenceTypeIds.HasComponent, 0, default);
        }

        /// <summary>
        /// Adds a non-hierarchical reference (used for HasDictionaryEntry semantics).
        /// </summary>
        public void AddReference(NodeId source, NodeId referenceTypeId, NodeId target, string browseName)
        {
            m_references.Add(new FakeReference
            {
                Source = source,
                Target = target,
                ReferenceTypeId = referenceTypeId,
                BrowseName = new QualifiedName(browseName),
                NodeClass = NodeClass.Object,
                TypeDefinition = NodeId.Null
            });
        }

        /// <summary>
        /// Adds the Part 5 FileType Open/Read/Close methods that stream the supplied
        /// content, so the connector can fetch a served layer through the fake session.
        /// </summary>
        public void AddPart5File(NodeId fileNodeId, byte[] content)
        {
            m_fileContents[fileNodeId] = content;
            m_fileMethods[Add(fileNodeId, "Open", NodeClass.Method, NodeId.Null,
                ReferenceTypeIds.HasComponent, 0, default)] = (fileNodeId, "Open");
            m_fileMethods[Add(fileNodeId, "Read", NodeClass.Method, NodeId.Null,
                ReferenceTypeIds.HasComponent, 0, default)] = (fileNodeId, "Read");
            m_fileMethods[Add(fileNodeId, "Close", NodeClass.Method, NodeId.Null,
                ReferenceTypeIds.HasComponent, 0, default)] = (fileNodeId, "Close");
        }

        /// <summary>
        /// When set, the fake Open method returns these output arguments instead of a
        /// file handle.
        /// </summary>
        public Variant[]? OpenResultOverride { get; set; }

        /// <summary>
        /// Number of Close invocations served so far.
        /// </summary>
        public int CloseCount { get; private set; }

        /// <summary>
        /// Serves a Call request for the Part 5 file methods registered with
        /// <see cref="AddPart5File"/>.
        /// </summary>
        public CallResponse Call(ArrayOf<CallMethodRequest> requests)
        {
            var results = new List<CallMethodResult>();
            for (int i = 0; i < requests.Count; i++)
            {
                CallMethodRequest request = requests[i];
                results.Add(CallOne(request));
            }
            return new CallResponse
            {
                ResponseHeader = new ResponseHeader(),
                Results = results.ToArrayOf(),
                DiagnosticInfos = []
            };
        }

        private CallMethodResult CallOne(CallMethodRequest request)
        {
            if (!m_fileMethods.TryGetValue(request.MethodId,
                    out (NodeId File, string Name) method))
            {
                return new CallMethodResult { StatusCode = StatusCodes.BadMethodInvalid };
            }
            switch (method.Name)
            {
                case "Open":
                    if (OpenResultOverride != null)
                    {
                        return new CallMethodResult
                        {
                            StatusCode = StatusCodes.Good,
                            OutputArguments = OpenResultOverride.ToArrayOf()
                        };
                    }
                    uint handle = (uint)Interlocked.Increment(ref m_nextHandle);
                    m_openHandles[handle] = (method.File, 0);
                    return new CallMethodResult
                    {
                        StatusCode = StatusCodes.Good,
                        OutputArguments = [new Variant(handle)]
                    };
                case "Read":
                    return Read(request);
                default:
                    CloseCount++;
                    return new CallMethodResult { StatusCode = StatusCodes.Good };
            }
        }

        private CallMethodResult Read(CallMethodRequest request)
        {
            if (!request.InputArguments[0].TryGetValue(out uint handle) ||
                !m_openHandles.TryGetValue(handle, out (NodeId File, int Offset) state))
            {
                return new CallMethodResult { StatusCode = StatusCodes.BadInvalidArgument };
            }
            if (!request.InputArguments[1].TryGetValue(out int chunkSize))
            {
                chunkSize = 8192;
            }
            byte[] content = m_fileContents[state.File];
            int remaining = content.Length - state.Offset;
            int take = remaining < chunkSize ? remaining : chunkSize;
            if (take < 0)
            {
                take = 0;
            }
            var chunk = new byte[take];
            Array.Copy(content, state.Offset, chunk, 0, take);
            m_openHandles[handle] = (state.File, state.Offset + take);
            return new CallMethodResult
            {
                StatusCode = StatusCodes.Good,
                OutputArguments = [new Variant(new ByteString(chunk))]
            };
        }

        /// <summary>
        /// Overrides the value returned for the Value attribute of a node.
        /// </summary>
        public void SetValue(NodeId nodeId, DataValue value)
        {
            m_values[nodeId] = value;
        }

        /// <summary>
        /// Sets the UserAccessLevel attribute reported for a node.
        /// </summary>
        public void SetUserAccessLevel(NodeId nodeId, DataValue value)
        {
            m_accessLevels[nodeId] = value;
        }

        /// <summary>
        /// Sets the UserRolePermissions attribute reported for a node.
        /// </summary>
        public void SetUserRolePermissions(NodeId nodeId, DataValue value)
        {
            m_rolePermissions[nodeId] = value;
        }

        /// <summary>
        /// Sets the UserExecutable attribute reported for a method node.
        /// </summary>
        public void SetUserExecutable(NodeId nodeId, DataValue value)
        {
            m_executables[nodeId] = value;
        }

        /// <summary>
        /// Serves a Browse request from the in-memory graph.
        /// </summary>
        public BrowseResponse Browse(ArrayOf<BrowseDescription> nodesToBrowse)
        {
            BrowseCount++;
            var results = new List<BrowseResult>();
            for (int i = 0; i < nodesToBrowse.Count; i++)
            {
                BrowseDescription d = nodesToBrowse[i];
                var refs = new List<ReferenceDescription>();
                foreach (FakeReference r in m_references)
                {
                    bool forward = r.Source == d.NodeId;
                    bool inverse = r.Target == d.NodeId;
                    if (!forward && !inverse)
                    {
                        continue;
                    }
                    if (d.BrowseDirection == BrowseDirection.Forward && !forward)
                    {
                        continue;
                    }
                    if (d.BrowseDirection == BrowseDirection.Inverse && !inverse)
                    {
                        continue;
                    }
                    if (!Matches(r.ReferenceTypeId, d.ReferenceTypeId, d.IncludeSubtypes))
                    {
                        continue;
                    }
                    refs.Add(new ReferenceDescription
                    {
                        NodeId = new ExpandedNodeId(forward ? r.Target : r.Source),
                        BrowseName = r.BrowseName,
                        DisplayName = new LocalizedText(r.BrowseName.Name ?? string.Empty),
                        NodeClass = r.NodeClass,
                        ReferenceTypeId = r.ReferenceTypeId,
                        IsForward = forward,
                        TypeDefinition = new ExpandedNodeId(r.TypeDefinition)
                    });
                }
                results.Add(new BrowseResult
                {
                    StatusCode = StatusCodes.Good,
                    References = refs.ToArrayOf()
                });
            }
            return new BrowseResponse
            {
                ResponseHeader = new ResponseHeader(),
                Results = results.ToArrayOf(),
                DiagnosticInfos = []
            };
        }

        /// <summary>
        /// Attribute ids for which the fake Read service raises a service fault.
        /// </summary>
        public HashSet<uint> FaultingAttributes { get; } = [];

        /// <summary>
        /// Serves a Read request from the in-memory graph.
        /// </summary>
        public ReadResponse Read(ArrayOf<ReadValueId> nodesToRead)
        {
            ReadCount++;
            var results = new List<DataValue>();
            for (int i = 0; i < nodesToRead.Count; i++)
            {
                ReadValueId r = nodesToRead[i];
                if (FaultingAttributes.Contains(r.AttributeId))
                {
                    throw new ServiceResultException(StatusCodes.BadNotReadable);
                }
                results.Add(ReadAttribute(r.NodeId, r.AttributeId));
            }
            return new ReadResponse
            {
                ResponseHeader = new ResponseHeader(),
                Results = results.ToArrayOf(),
                DiagnosticInfos = []
            };
        }

        private DataValue ReadAttribute(NodeId nodeId, uint attributeId)
        {
            switch (attributeId)
            {
                case Attributes.Value:
                    return m_values.TryGetValue(nodeId, out DataValue v)
                        ? v
                        : DataValue.FromStatusCode(StatusCodes.BadNodeIdUnknown);
                case Attributes.NodeClass:
                    NodeClass nodeClass = NodeClassOf(nodeId);
                    return nodeClass == NodeClass.Unspecified
                        ? DataValue.FromStatusCode(StatusCodes.BadNodeIdUnknown)
                        : new DataValue(new Variant((int)nodeClass));
                case Attributes.UserAccessLevel:
                    return m_accessLevels.TryGetValue(nodeId, out DataValue a)
                        ? a
                        : DataValue.FromStatusCode(StatusCodes.BadAttributeIdInvalid);
                case Attributes.UserRolePermissions:
                    return m_rolePermissions.TryGetValue(nodeId, out DataValue p)
                        ? p
                        : DataValue.FromStatusCode(StatusCodes.BadAttributeIdInvalid);
                case Attributes.UserExecutable:
                    return m_executables.TryGetValue(nodeId, out DataValue e)
                        ? e
                        : DataValue.FromStatusCode(StatusCodes.BadAttributeIdInvalid);
                default:
                    return DataValue.FromStatusCode(StatusCodes.BadAttributeIdInvalid);
            }
        }

        private NodeClass NodeClassOf(NodeId nodeId)
        {
            foreach (FakeReference r in m_references)
            {
                if (r.Target == nodeId)
                {
                    return r.NodeClass;
                }
            }
            return NodeClass.Unspecified;
        }

        private NodeId Add(
            NodeId parent,
            string browseName,
            NodeClass nodeClass,
            NodeId typeDefinition,
            NodeId referenceTypeId,
            ushort browseNameNamespace,
            NodeId nodeId)
        {
            NodeId id = nodeId.IsNull ? NewNodeId() : nodeId;
            m_references.Add(new FakeReference
            {
                Source = parent,
                Target = id,
                ReferenceTypeId = referenceTypeId,
                BrowseName = new QualifiedName(browseName, browseNameNamespace),
                NodeClass = nodeClass,
                TypeDefinition = typeDefinition
            });
            return id;
        }

        private static readonly Dictionary<NodeId, NodeId> s_referenceTypeParents = new()
        {
            { ReferenceTypeIds.HierarchicalReferences, ReferenceTypeIds.References },
            { ReferenceTypeIds.NonHierarchicalReferences, ReferenceTypeIds.References },
            { ReferenceTypeIds.HasChild, ReferenceTypeIds.HierarchicalReferences },
            { ReferenceTypeIds.Organizes, ReferenceTypeIds.HierarchicalReferences },
            { ReferenceTypeIds.HasEventSource, ReferenceTypeIds.HierarchicalReferences },
            { ReferenceTypeIds.Aggregates, ReferenceTypeIds.HasChild },
            { ReferenceTypeIds.HasSubtype, ReferenceTypeIds.HasChild },
            { ReferenceTypeIds.HasComponent, ReferenceTypeIds.Aggregates },
            { ReferenceTypeIds.HasProperty, ReferenceTypeIds.Aggregates },
            { ReferenceTypeIds.HasOrderedComponent, ReferenceTypeIds.HasComponent },
            { ReferenceTypeIds.HasNotifier, ReferenceTypeIds.HasEventSource },
            { ReferenceTypeIds.HasDictionaryEntry, ReferenceTypeIds.NonHierarchicalReferences },
            { ReferenceTypeIds.HasTypeDefinition, ReferenceTypeIds.NonHierarchicalReferences }
        };

        private static bool Matches(NodeId actual, NodeId requested, bool includeSubtypes)
        {
            if (requested.IsNull || requested == ReferenceTypeIds.References)
            {
                return true;
            }
            if (actual == requested)
            {
                return true;
            }
            if (!includeSubtypes)
            {
                return false;
            }
            NodeId current = actual;
            while (s_referenceTypeParents.TryGetValue(current, out NodeId parent))
            {
                if (parent == requested)
                {
                    return true;
                }
                current = parent;
            }
            return false;
        }
    }

    /// <summary>
    /// Builds a <see cref="Mock{ISession}"/> wired to a <see cref="FakeAddressSpace"/>,
    /// including the properties the client's <c>Browser</c> requires.
    /// </summary>
    internal static class FakeSession
    {
        /// <summary>
        /// Creates a session mock that serves Browse and Read from the address space.
        /// </summary>
        public static Mock<ISession> Create(FakeAddressSpace space)
        {
            var telemetry = new Mock<ITelemetryContext>();
            telemetry.SetupGet(t => t.LoggerFactory).Returns(NullLoggerFactory.Instance);

            var messageContext = new Mock<IServiceMessageContext>();
            messageContext.SetupGet(c => c.Telemetry).Returns(telemetry.Object);
            messageContext.SetupGet(c => c.NamespaceUris).Returns(space.NamespaceUris);

            var session = new Mock<ISession>();
            session.SetupGet(s => s.NamespaceUris).Returns(space.NamespaceUris);
            session.SetupGet(s => s.MessageContext).Returns(messageContext.Object);
            session.SetupGet(s => s.OperationLimits).Returns(new OperationLimits
            {
                MaxNodesPerBrowse = 100,
                MaxNodesPerRead = 100
            });
            session.SetupGet(s => s.ServerCapabilities).Returns(new ServerCapabilities
            {
                MaxBrowseContinuationPoints = 0
            });
            session.SetupGet(s => s.ContinuationPointPolicy).Returns(ContinuationPointPolicy.Default);

            session
                .Setup(s => s.BrowseAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<ViewDescription>(),
                    It.IsAny<uint>(),
                    It.IsAny<ArrayOf<BrowseDescription>>(),
                    It.IsAny<CancellationToken>()))
                .Returns((RequestHeader _, ViewDescription _, uint _,
                    ArrayOf<BrowseDescription> nodes, CancellationToken _) =>
                    new ValueTask<BrowseResponse>(space.Browse(nodes)));

            session
                .Setup(s => s.ReadAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<double>(),
                    It.IsAny<TimestampsToReturn>(),
                    It.IsAny<ArrayOf<ReadValueId>>(),
                    It.IsAny<CancellationToken>()))
                .Returns((RequestHeader _, double _, TimestampsToReturn _,
                    ArrayOf<ReadValueId> nodes, CancellationToken _) =>
                    new ValueTask<ReadResponse>(space.Read(nodes)));

            session
                .Setup(s => s.CallAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<ArrayOf<CallMethodRequest>>(),
                    It.IsAny<CancellationToken>()))
                .Returns((RequestHeader _, ArrayOf<CallMethodRequest> requests, CancellationToken _) =>
                    new ValueTask<CallResponse>(space.Call(requests)));

            return session;
        }

        /// <summary>
        /// Convenience helper building a TranslateBrowsePathsToNodeIds response.
        /// </summary>
        public static TranslateBrowsePathsToNodeIdsResponse TranslateResponse(
            StatusCode statusCode,
            params BrowsePathTarget[] targets)
        {
            return new TranslateBrowsePathsToNodeIdsResponse
            {
                ResponseHeader = new ResponseHeader(),
                Results =
                [
                    new BrowsePathResult
                    {
                        StatusCode = statusCode,
                        Targets = targets.ToArrayOf()
                    }
                ],
                DiagnosticInfos = []
            };
        }
    }
}
