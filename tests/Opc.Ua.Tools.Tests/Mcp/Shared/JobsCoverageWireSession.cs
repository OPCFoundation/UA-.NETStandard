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

#if NET10_0
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client;

namespace Opc.Ua.Tools.Tests.McpCompanion
{
    /// <summary>
    /// A strict wire boundary for the jobs, machinery and positioning behavioral tests.
    /// </summary>
    internal sealed class JobsCoverageWireSession
    {
        /// <summary>
        /// Creates a connected session whose unknown reads fail the test.
        /// </summary>
        public JobsCoverageWireSession(ITelemetryContext telemetry)
        {
            Session = JobMcpTestSession.Create(telemetry);
            Context = Session.Object.MessageContext;
            foreach (string uri in new[]
            {
                Di.Namespaces.OpcUaDi,
                Machinery.Namespaces.Machinery,
                Machinery.ProcessValues.Namespaces.MachineryProcessValues,
                Machinery.Jobs.Namespaces.MachineryJobs,
                Machinery.Result.Namespaces.MachineryResult,
                Machinery.Energy.Namespaces.MachineryEnergy,
                PADIM.Namespaces.PADIM,
                ECM.Namespaces.ECM,
                Gpos.Namespaces.GPOS,
                Rsl.Namespaces.RSL
            })
            {
                Context.NamespaceUris.GetIndexOrAppend(uri);
            }
            VendorIndex = Context.NamespaceUris.GetIndexOrAppend("urn:opcua:tests:jobs-coverage");
            Session.SetupGet(value => value.Factory).Returns(Context.Factory);
            Session.SetupGet(value => value.OperationLimits).Returns(new OperationLimits());
            Session.SetupGet(value => value.ServerCapabilities).Returns(new ServerCapabilities());
            Session.SetupGet(value => value.ContinuationPointPolicy).Returns(ContinuationPointPolicy.Default);
            var cache = new Mock<INodeCache>(MockBehavior.Strict);
            cache.Setup(value => value.IsTypeOfAsync(
                    It.IsAny<NodeId>(), It.IsAny<NodeId>(), It.IsAny<CancellationToken>()))
                .Returns((NodeId actual, NodeId expected, CancellationToken ct) =>
                {
                    ct.ThrowIfCancellationRequested();
                    return new ValueTask<bool>(actual == expected || m_subtypes.Contains((actual, expected)));
                });
            cache.Setup(value => value.IsTypeOfAsync(
                    It.IsAny<ExpandedNodeId>(), It.IsAny<ExpandedNodeId>(), It.IsAny<CancellationToken>()))
                .Returns((ExpandedNodeId actual, ExpandedNodeId expected, CancellationToken ct) =>
                {
                    ct.ThrowIfCancellationRequested();
                    NodeId actualId = Local(actual);
                    NodeId expectedId = Local(expected);
                    return new ValueTask<bool>(
                        actualId == expectedId || m_subtypes.Contains((actualId, expectedId)));
                });
            Session.SetupGet(value => value.NodeCache).Returns(cache.Object);
            Session.Setup(value => value.TranslateBrowsePathsToNodeIdsAsync(
                    It.IsAny<RequestHeader?>(), It.IsAny<ArrayOf<BrowsePath>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((RequestHeader? _, ArrayOf<BrowsePath> paths, CancellationToken ct) =>
                {
                    ct.ThrowIfCancellationRequested();
                    Paths.AddRange(paths);
                    return new TranslateBrowsePathsToNodeIdsResponse
                    {
                        Results = paths.ConvertAll(path => m_paths.TryGetValue(
                            (path.StartingNode, string.Join("/", path.RelativePath.Elements.ToArray()!
                                .Select(element => element.TargetName.ToString()))), out NodeId target)
                            ? new BrowsePathResult
                            {
                                StatusCode = StatusCodes.Good,
                                Targets =
                                [
                                    new BrowsePathTarget
                                    {
                                        TargetId = new ExpandedNodeId(target),
                                        RemainingPathIndex = uint.MaxValue
                                    }
                                ]
                            }
                            : new BrowsePathResult { StatusCode = StatusCodes.BadNoMatch })
                    };
                });
            Session.Setup(value => value.ReadAsync(
                    It.IsAny<RequestHeader?>(), It.IsAny<double>(), It.IsAny<TimestampsToReturn>(),
                    It.IsAny<ArrayOf<ReadValueId>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((RequestHeader? _, double _, TimestampsToReturn timestamps,
                    ArrayOf<ReadValueId> reads, CancellationToken ct) =>
                {
                    ct.ThrowIfCancellationRequested();
                    Reads.AddRange(reads);
                    TimestampRequests.Add(timestamps);
                    return new ReadResponse
                    {
                        Results = reads.ConvertAll(read => m_values.TryGetValue(
                            (read.NodeId, read.AttributeId), out DataValue value)
                            ? value
                            : throw new AssertionException(
                                $"Unexpected read of {read.NodeId}, attribute {read.AttributeId}.")),
                        DiagnosticInfos = []
                    };
                });
            Session.Setup(value => value.BrowseAsync(
                    It.IsAny<RequestHeader?>(), It.IsAny<ViewDescription?>(), It.IsAny<uint>(),
                    It.IsAny<ArrayOf<BrowseDescription>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((RequestHeader? _, ViewDescription? _, uint _,
                    ArrayOf<BrowseDescription> descriptions, CancellationToken ct) =>
                {
                    ct.ThrowIfCancellationRequested();
                    Browses.AddRange(descriptions);
                    return new BrowseResponse
                    {
                        Results = descriptions.ConvertAll(description => m_browses.TryGetValue(
                            (description.NodeId, description.ReferenceTypeId, description.NodeClassMask),
                            out BrowseResult? result)
                            ? result
                            : new BrowseResult { StatusCode = StatusCodes.Good, References = [] }),
                        DiagnosticInfos = []
                    };
                });
        }

        /// <summary>
        /// Gets the mocked external session.
        /// </summary>
        public Mock<ISession> Session { get; }

        /// <summary>
        /// Gets the session's real encoder context.
        /// </summary>
        public IServiceMessageContext Context { get; }

        /// <summary>
        /// Gets the namespace index for test instance nodes.
        /// </summary>
        public ushort VendorIndex { get; }

        /// <summary>
        /// Gets the browse paths sent by the real client.
        /// </summary>
        public List<BrowsePath> Paths { get; } = [];

        /// <summary>
        /// Gets the attribute reads sent by the real client.
        /// </summary>
        public List<ReadValueId> Reads { get; } = [];

        /// <summary>
        /// Gets the requested timestamp policies.
        /// </summary>
        public List<TimestampsToReturn> TimestampRequests { get; } = [];

        /// <summary>
        /// Gets the actual browse requests.
        /// </summary>
        public List<BrowseDescription> Browses { get; } = [];

        /// <summary>
        /// Creates an instance identifier in the registered vendor namespace.
        /// </summary>
        public NodeId Node(string name)
        {
            return new NodeId(name, VendorIndex);
        }

        /// <summary>
        /// Resolves a model identifier against this session's namespace table.
        /// </summary>
        public NodeId Local(ExpandedNodeId node)
        {
            return ExpandedNodeId.ToNodeId(node, Context.NamespaceUris);
        }

        /// <summary>
        /// Creates a qualified model browse name.
        /// </summary>
        public QualifiedName Name(string name, string namespaceUri)
        {
            return new QualifiedName(name, Context.NamespaceUris.GetIndexOrAppend(namespaceUri));
        }

        /// <summary>
        /// Registers an explicit relative browse path.
        /// </summary>
        public void Path(NodeId parent, NodeId target, params QualifiedName[] names)
        {
            m_paths[(parent, string.Join("/", names.Select(name => name.ToString())))] = target;
        }

        /// <summary>
        /// Registers one readable attribute.
        /// </summary>
        public void Value(NodeId node, Variant value, uint attribute = Attributes.Value)
        {
            m_values[(node, attribute)] = new DataValue(value);
        }

        /// <summary>
        /// Registers a value with explicit quality and source timestamp.
        /// </summary>
        public void Value(NodeId node, DataValue value)
        {
            m_values[(node, Attributes.Value)] = value;
        }

        /// <summary>
        /// Registers a named child and its value without creating production nodes.
        /// </summary>
        public NodeId Child(NodeId parent, QualifiedName name, Variant value)
        {
            NodeId child = Node($"{parent}/{name}");
            Path(parent, child, name);
            Value(child, value);
            return child;
        }

        /// <summary>
        /// Registers a complete browse result for the precise requested reference and node class.
        /// </summary>
        public void Browse(
            NodeId parent,
            NodeId reference,
            NodeClass nodeClass,
            ArrayOf<ReferenceDescription> references,
            StatusCode status = default)
        {
            m_browses[(parent, reference, (uint)nodeClass)] = new BrowseResult
            {
                StatusCode = status,
                References = references
            };
        }

        /// <summary>
        /// Registers the node cache's answer for a vendor subtype.
        /// </summary>
        public void Subtype(NodeId actual, ExpandedNodeId expected)
        {
            m_subtypes.Add((actual, Local(expected)));
        }

        /// <summary>
        /// Describes a discoverable node with a non-default locale and actual type.
        /// </summary>
        public ReferenceDescription Reference(
            NodeId node,
            string name,
            NodeId type,
            NodeClass nodeClass = NodeClass.Object,
            NodeId referenceType = default)
        {
            return new ReferenceDescription
            {
                NodeId = new ExpandedNodeId(node),
                BrowseName = new QualifiedName(name, VendorIndex),
                DisplayName = new LocalizedText("de", name),
                TypeDefinition = new ExpandedNodeId(type),
                NodeClass = nodeClass,
                ReferenceTypeId = referenceType.IsNull ? ReferenceTypeIds.Organizes : referenceType,
                IsForward = true
            };
        }

        private readonly Dictionary<(NodeId Node, string Path), NodeId> m_paths = [];
        private readonly Dictionary<(NodeId Node, uint Attribute), DataValue> m_values = [];
        private readonly Dictionary<(NodeId Node, NodeId Reference, uint Class), BrowseResult> m_browses = [];
        private readonly HashSet<(NodeId Actual, NodeId Expected)> m_subtypes = [];
    }
}
#endif
