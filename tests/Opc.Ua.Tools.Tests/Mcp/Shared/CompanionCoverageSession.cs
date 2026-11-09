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
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Mcp;
using ISession = Opc.Ua.Client.ISession;

namespace Opc.Ua.Tools.Tests.McpCompanion
{
    /// <summary>
    /// Supplies strict wire responses to real AMB, Scales and Pumps clients without a live server.
    /// </summary>
    internal sealed class CompanionCoverageSession
    {
        /// <summary>
        /// Creates an isolated session with explicit, namespace-qualified address-space responses.
        /// </summary>
        internal CompanionCoverageSession(ITelemetryContext telemetry, bool publishModels = true)
        {
            Context = ServiceMessageContext.Create(telemetry);
            Context.NamespaceUris.GetIndexOrAppend("urn:coverage:instances");
            if (publishModels)
            {
                Context.NamespaceUris.GetIndexOrAppend(Di.Namespaces.OpcUaDi);
                Context.NamespaceUris.GetIndexOrAppend(AMB.Namespaces.AMB);
                Context.NamespaceUris.GetIndexOrAppend(Scales.Namespaces.Scales);
                Context.NamespaceUris.GetIndexOrAppend(Pumps.Namespaces.Pumps);
                Context.NamespaceUris.GetIndexOrAppend(Machinery.Namespaces.Machinery);
                Context.NamespaceUris.GetIndexOrAppend(PackML.Namespaces.PackML);
            }
            Session.SetupGet(value => value.MessageContext).Returns(Context);
            Session.SetupGet(value => value.NamespaceUris).Returns(Context.NamespaceUris);
            Session.SetupGet(value => value.Connected).Returns(true);
            Session.SetupGet(value => value.SessionName).Returns("companion-coverage");
            Session.SetupGet(value => value.SessionId).Returns(Id("session"));
            Session.SetupGet(value => value.ConfiguredEndpoint).Returns(new ConfiguredEndpoint(
                null,
                new EndpointDescription { EndpointUrl = "opc.tcp://localhost:4840/coverage" },
                EndpointConfiguration.Create()));
            Session.SetupGet(value => value.OperationLimits).Returns(new OperationLimits());
            Session.SetupGet(value => value.ServerCapabilities).Returns(new Client.ServerCapabilities());
            Session.SetupGet(value => value.ContinuationPointPolicy).Returns(ContinuationPointPolicy.Default);
            Session.SetupGet(value => value.NodeCache).Returns(NodeCache.Object);
            Session.Setup(value => value.TranslateBrowsePathsToNodeIdsAsync(
                It.IsAny<RequestHeader?>(), It.IsAny<ArrayOf<BrowsePath>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((RequestHeader? _, ArrayOf<BrowsePath> paths, CancellationToken token) =>
                {
                    token.ThrowIfCancellationRequested();
                    Paths.AddRange(paths);
                    return new TranslateBrowsePathsToNodeIdsResponse
                    {
                        ResponseHeader = new ResponseHeader(),
                        Results = paths.ConvertAll(Resolve)
                    };
                });
            Session.Setup(value => value.ReadAsync(
                It.IsAny<RequestHeader?>(), It.IsAny<double>(), It.IsAny<TimestampsToReturn>(),
                It.IsAny<ArrayOf<ReadValueId>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((RequestHeader? _, double _, TimestampsToReturn timestamps,
                    ArrayOf<ReadValueId> requests, CancellationToken token) =>
                {
                    token.ThrowIfCancellationRequested();
                    Reads.AddRange(requests);
                    TimestampRequests.Add(timestamps);
                    return new ReadResponse
                    {
                        ResponseHeader = new ResponseHeader(),
                        Results = requests.ConvertAll(request =>
                            m_values.TryGetValue((request.NodeId, request.AttributeId), out DataValue value)
                                ? value
                                : DataValue.FromStatusCode(StatusCodes.BadNodeIdUnknown))
                    };
                });
            Session.Setup(value => value.BrowseAsync(
                It.IsAny<RequestHeader?>(), It.IsAny<ViewDescription?>(), It.IsAny<uint>(),
                It.IsAny<ArrayOf<BrowseDescription>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((RequestHeader? _, ViewDescription? _, uint _,
                    ArrayOf<BrowseDescription> requests, CancellationToken token) =>
                {
                    token.ThrowIfCancellationRequested();
                    Browses.AddRange(requests);
                    return new BrowseResponse
                    {
                        ResponseHeader = new ResponseHeader(),
                        Results = requests.ConvertAll(Browse)
                    };
                });
        }

        /// <summary>
        /// Gets the real UA encoding context used by every companion client and projection.
        /// </summary>
        internal ServiceMessageContext Context { get; }

        /// <summary>
        /// Gets the strict external session boundary.
        /// </summary>
        internal Mock<ISession> Session { get; } = new(MockBehavior.Strict);

        /// <summary>
        /// Gets the external type hierarchy cache for discovery tests.
        /// </summary>
        internal Mock<INodeCache> NodeCache { get; } = new(MockBehavior.Strict);

        /// <summary>
        /// Gets the browse paths actually requested on the wire.
        /// </summary>
        internal List<BrowsePath> Paths { get; } = [];

        /// <summary>
        /// Gets the attribute reads actually requested on the wire.
        /// </summary>
        internal List<ReadValueId> Reads { get; } = [];

        /// <summary>
        /// Gets each read's requested timestamp mode.
        /// </summary>
        internal List<TimestampsToReturn> TimestampRequests { get; } = [];

        /// <summary>
        /// Gets the browse requests actually sent to the server.
        /// </summary>
        internal List<BrowseDescription> Browses { get; } = [];

        /// <summary>
        /// Gets all dispatched method requests, including any unintended additional commands.
        /// </summary>
        internal List<CallMethodRequest> Calls { get; } = [];

        /// <summary>
        /// Gets all writes, including any unintended prerequisite mutations.
        /// </summary>
        internal List<WriteValue> Writes { get; } = [];

        /// <summary>
        /// Gets or sets a specific malformed or refused translation response for boundary tests.
        /// </summary>
        internal Func<BrowsePath, BrowsePathResult?>? TranslateOverride { get; set; }

        /// <summary>
        /// Creates the existing MCP services without starting a host or making a connection.
        /// </summary>
        internal static ServiceProvider Services()
        {
            var services = new ServiceCollection();
            services.AddOpcUaMcpCore().AddOpcUaMcpAmb().AddOpcUaMcpScales().AddOpcUaMcpPumps();
            return services.BuildServiceProvider();
        }

        /// <summary>
        /// Creates a stable local instance identifier.
        /// </summary>
        internal static NodeId Id(string identifier)
        {
            return new NodeId(identifier, 1);
        }

        /// <summary>
        /// Reads matching structured and textual MCP content and checks the expected error outcome.
        /// </summary>
        internal static JsonObject Result(CallToolResult result, bool isError = false)
        {
            string text = result.Content.OfType<TextContentBlock>().Single().Text;
            Assert.That(result.IsError, Is.EqualTo(isError), text);
            JsonObject json = JsonNode.Parse(text)!.AsObject();
            Assert.That(result.StructuredContent.HasValue, Is.True);
            Assert.That(JsonNode.DeepEquals(
                json, JsonNode.Parse(result.StructuredContent!.Value.GetRawText())), Is.True);
            return json;
        }

        /// <summary>
        /// Compares a manually specified JSON contract with a projection.
        /// </summary>
        internal static void AssertJson(JsonNode? actual, string expected)
        {
            Assert.That(JsonNode.DeepEquals(actual, JsonNode.Parse(expected)), Is.True, actual?.ToJsonString());
        }

        /// <summary>
        /// Creates an address-space reference with an actual type definition and localized name.
        /// </summary>
        internal static ReferenceDescription Reference(
            NodeId node,
            string name,
            NodeClass nodeClass = NodeClass.Object,
            NodeId typeDefinition = default)
        {
            return new ReferenceDescription
            {
                NodeId = node,
                BrowseName = new QualifiedName(name, node.NamespaceIndex),
                DisplayName = new LocalizedText("de-DE", name),
                NodeClass = nodeClass,
                TypeDefinition = typeDefinition,
                ReferenceTypeId = ReferenceTypeIds.HasComponent,
                IsForward = true
            };
        }

        /// <summary>
        /// Resolves a model namespace to the index exposed by this test server.
        /// </summary>
        internal ushort NamespaceIndex(string namespaceUri)
        {
            return checked((ushort)Context.NamespaceUris.GetIndex(namespaceUri));
        }

        /// <summary>
        /// Creates a model-qualified browse name.
        /// </summary>
        internal QualifiedName Name(string name, string namespaceUri)
        {
            return new QualifiedName(name, NamespaceIndex(namespaceUri));
        }

        /// <summary>
        /// Publishes one exact path; all other paths are absent rather than guessed by name.
        /// </summary>
        internal void MapPath(NodeId parent, NodeId child, params QualifiedName[] path)
        {
            m_paths[(parent, PathKey(path))] = child;
        }

        /// <summary>
        /// Publishes a path and its value for semantic client reads.
        /// </summary>
        internal NodeId Publish(NodeId parent, string identifier, Variant value, params QualifiedName[] path)
        {
            NodeId child = Id(identifier);
            MapPath(parent, child, path);
            SetValue(child, value);
            return child;
        }

        /// <summary>
        /// Supplies a value attribute without converting its UA type.
        /// </summary>
        internal void SetValue(NodeId node, Variant value, uint attributeId = Attributes.Value)
        {
            SetDataValue(node, new DataValue(value), attributeId);
        }

        /// <summary>
        /// Supplies a value with explicit quality and timestamp.
        /// </summary>
        internal void SetDataValue(NodeId node, DataValue value, uint attributeId = Attributes.Value)
        {
            m_values[(node, attributeId)] = value;
        }

        /// <summary>
        /// Publishes references for exactly one reference type and direction.
        /// </summary>
        internal void SetBrowse(
            NodeId parent,
            ArrayOf<ReferenceDescription> references,
            NodeId referenceType = default,
            BrowseDirection direction = BrowseDirection.Forward)
        {
            m_references[(parent,
                referenceType.IsNull ? ReferenceTypeIds.HierarchicalReferences : referenceType,
                direction)] = references;
        }

        /// <summary>
        /// Enables a single-call response contract while retaining every dispatched request for assertions.
        /// </summary>
        internal void ReplyToCalls(ArrayOf<Variant> output = default, StatusCode status = default)
        {
            Session.Setup(value => value.CallAsync(
                It.IsAny<RequestHeader?>(), It.IsAny<ArrayOf<CallMethodRequest>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((RequestHeader? _, ArrayOf<CallMethodRequest> requests, CancellationToken token) =>
                {
                    token.ThrowIfCancellationRequested();
                    Calls.AddRange(requests);
                    return new CallResponse
                    {
                        ResponseHeader = new ResponseHeader(),
                        Results = requests.ConvertAll(_ => new CallMethodResult
                        {
                            StatusCode = status,
                            OutputArguments = output
                        })
                    };
                });
        }

        /// <summary>
        /// Enables value writes with an explicit server status.
        /// </summary>
        internal void ReplyToWrites(StatusCode status = default)
        {
            Session.Setup(value => value.WriteAsync(
                It.IsAny<RequestHeader?>(), It.IsAny<ArrayOf<WriteValue>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((RequestHeader? _, ArrayOf<WriteValue> requests, CancellationToken token) =>
                {
                    token.ThrowIfCancellationRequested();
                    Writes.AddRange(requests);
                    return new WriteResponse
                    {
                        ResponseHeader = new ResponseHeader(),
                        Results = requests.ConvertAll(_ => status)
                    };
                });
        }

        private static string PathKey(IEnumerable<QualifiedName> path)
        {
            return string.Join("/", path.Select(name => name.ToString()));
        }

        private BrowsePathResult Resolve(BrowsePath path)
        {
            BrowsePathResult? overridden = TranslateOverride?.Invoke(path);
            if (overridden != null)
            {
                return overridden;
            }
            string key = PathKey(path.RelativePath.Elements.ToArray()!.Select(element => element.TargetName));
            return m_paths.TryGetValue((path.StartingNode, key), out NodeId target)
                ? new BrowsePathResult
                {
                    StatusCode = StatusCodes.Good,
                    Targets = [new BrowsePathTarget { TargetId = target, RemainingPathIndex = uint.MaxValue }]
                }
                : new BrowsePathResult { StatusCode = StatusCodes.BadNoMatch };
        }

        private BrowseResult Browse(BrowseDescription request)
        {
            m_references.TryGetValue(
                (request.NodeId, request.ReferenceTypeId, request.BrowseDirection),
                out ArrayOf<ReferenceDescription> references);
            var matching = new List<ReferenceDescription>();
            foreach (ReferenceDescription reference in references)
            {
                if (request.NodeClassMask == 0 || (request.NodeClassMask & (uint)reference.NodeClass) != 0)
                {
                    matching.Add(reference);
                }
            }
            return new BrowseResult
            {
                StatusCode = StatusCodes.Good,
                References = matching.ToArrayOf()
            };
        }

        private readonly Dictionary<(NodeId Parent, string Path), NodeId> m_paths = [];
        private readonly Dictionary<(NodeId Node, uint Attribute), DataValue> m_values = [];

        private readonly Dictionary<(NodeId Node, NodeId Reference, BrowseDirection Direction),
            ArrayOf<ReferenceDescription>> m_references = [];
    }
}
#endif
