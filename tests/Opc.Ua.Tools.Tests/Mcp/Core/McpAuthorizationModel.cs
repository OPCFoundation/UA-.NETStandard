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
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Mcp;

namespace Opc.Ua.Tools.Tests.McpAuthorization
{
    /// <summary>
    /// Explicit wire pages and cache facts, not an authorization implementation.
    /// Mutation services stay strict until the borrowing fixture supplies a typed callback.
    /// </summary>
    internal sealed class McpAuthorizationModel
    {
        public const string BenignNamespaceUri = "urn:test:ordinary";
        public const string GdsNamespaceUri = "http://opcfoundation.org/UA/GDS/";
        public const string OnboardingNamespaceUri = "http://opcfoundation.org/UA/Onboarding/";
        public const string VendorNamespaceUri = "urn:test:vendor";

        public McpAuthorizationModel(
            MessageSecurityMode securityMode = MessageSecurityMode.SignAndEncrypt,
            string[]? namespaceUris = null,
            string registeredName = "Prod-A")
        {
            var telemetry = new Mock<ITelemetryContext>(MockBehavior.Strict);
            telemetry.SetupGet(context => context.LoggerFactory).Returns(NullLoggerFactory.Instance);
            MessageContext = ServiceMessageContext.CreateEmpty(telemetry.Object);
            foreach (string uri in namespaceUris ??
                [BenignNamespaceUri, GdsNamespaceUri, OnboardingNamespaceUri, VendorNamespaceUri])
            {
                NamespaceUris.Append(uri);
            }

            Endpoint = new EndpointDescription
            {
                EndpointUrl = "opc.tcp://unused.invalid:4840",
                SecurityMode = securityMode,
                SecurityPolicyUri = SecurityPolicies.Basic256Sha256
            };
            ConfiguredEndpoint = new ConfiguredEndpoint(null, Endpoint, EndpointConfiguration.Create());
            Session = new Mock<ISession>(MockBehavior.Strict);
            NodeCache = new Mock<INodeCache>(MockBehavior.Strict);
            Session.SetupGet(session => session.MessageContext).Returns(MessageContext);
            Session.SetupGet(session => session.NamespaceUris).Returns(() => NamespaceUris);
            Session.SetupGet(session => session.NodeCache).Returns(NodeCache.Object);
            Session.SetupGet(session => session.Connected).Returns(true);
            Session.SetupGet(session => session.SessionName).Returns("Remote-Untrusted-Session-Name");
            Session.SetupGet(session => session.SessionId).Returns(new NodeId("wire-session", 1));
            Session.SetupGet(session => session.ConfiguredEndpoint).Returns(ConfiguredEndpoint);
            SessionInfo = new OpcUaSessionManager.SessionInfo
            {
                Name = registeredName,
                Session = Session.Object,
                Endpoint = Endpoint,
                AuthType = "Anonymous",
                ConnectedAt = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc)
            };

            NodeCache
                .Setup(cache => cache.IsTypeOfAsync(
                    It.IsAny<NodeId>(), It.IsAny<NodeId>(), It.IsAny<CancellationToken>()))
                .Returns((NodeId actual, NodeId expected, CancellationToken token) =>
                {
                    bool matches = m_ancestry.Contains((actual, expected));
                    m_cacheQueries.Add(new CacheQuery(actual, expected, matches, token));
                    return ValueTask.FromResult(matches);
                });

            Session
                .Setup(session => session.BrowseAsync(
                    It.IsAny<RequestHeader?>(),
                    It.IsAny<ViewDescription?>(),
                    It.IsAny<uint>(),
                    It.IsAny<ArrayOf<BrowseDescription>>(),
                    It.IsAny<CancellationToken>()))
                .Returns((
                    RequestHeader? header,
                    ViewDescription? view,
                    uint maximum,
                    ArrayOf<BrowseDescription> requests,
                    CancellationToken token) =>
                {
                    var results = new BrowseResult[requests.Count];
                    for (int index = 0; index < requests.Count; index++)
                    {
                        BrowseDescription request = requests[index];
                        var key = new BrowseKey(request.NodeId, request.ReferenceTypeId, request.BrowseDirection);
                        if (!m_pages.TryGetValue(key, out BrowseResult? result))
                        {
                            result = new BrowseResult
                            {
                                StatusCode = m_nodes.Contains(request.NodeId)
                                    ? StatusCodes.Good
                                    : StatusCodes.BadNodeIdUnknown,
                                References = []
                            };
                        }
                        results[index] = result;
                        m_browses.Add(new BrowseQuery(header, view, maximum, request, result, token));
                    }
                    return ValueTask.FromResult(new BrowseResponse
                    {
                        ResponseHeader = new ResponseHeader(),
                        Results = results,
                        DiagnosticInfos = []
                    });
                });

            Session
                .Setup(session => session.BrowseNextAsync(
                    It.IsAny<RequestHeader?>(),
                    It.IsAny<bool>(),
                    It.IsAny<ArrayOf<ByteString>>(),
                    It.IsAny<CancellationToken>()))
                .Returns((
                    RequestHeader? header,
                    bool release,
                    ArrayOf<ByteString> points,
                    CancellationToken token) =>
                {
                    var results = new BrowseResult[points.Count];
                    for (int index = 0; index < points.Count; index++)
                    {
                        ByteString point = points[index];
                        BrowseResult result = release
                            ? new BrowseResult { StatusCode = StatusCodes.Good, References = [] }
                            : m_continuationPages[point];
                        results[index] = result;
                        m_continuations.Add(new ContinuationQuery(header, release, point, result, token));
                    }
                    return ValueTask.FromResult(new BrowseNextResponse
                    {
                        ResponseHeader = new ResponseHeader(),
                        Results = results,
                        DiagnosticInfos = []
                    });
                });
        }

        public Mock<ISession> Session { get; }
        public Mock<INodeCache> NodeCache { get; }
        public ServiceMessageContext MessageContext { get; }
        public NamespaceTable NamespaceUris => MessageContext.NamespaceUris;
        public EndpointDescription Endpoint { get; }
        public ConfiguredEndpoint ConfiguredEndpoint { get; }
        public OpcUaSessionManager.SessionInfo SessionInfo { get; }
        public IReadOnlyCollection<NodeId> KnownNodes => m_nodes;
        public IReadOnlyList<BrowseQuery> Browses => m_browses;
        public IReadOnlyList<ContinuationQuery> Continuations => m_continuations;
        public IReadOnlyList<CacheQuery> CacheQueries => m_cacheQueries;

        /// <summary>
        /// A mutation callback records its operation here and captures its typed arguments in the fixture.
        /// </summary>
        public List<string> Mutations { get; } = [];

        public NodeId AddNode(NodeId node)
        {
            m_nodes.Add(node);
            return node;
        }

        public void AddTypeDefinition(NodeId node, ExpandedNodeId type, QualifiedName browseName = default)
        {
            AddReference(node, ReferenceTypeIds.HasTypeDefinition, BrowseDirection.Forward, new ReferenceDescription
            {
                NodeId = type,
                ReferenceTypeId = ReferenceTypeIds.HasTypeDefinition,
                IsForward = true,
                NodeClass = NodeClass.ObjectType,
                BrowseName = browseName
            });
        }

        public void AddOwner(NodeId node, ExpandedNodeId owner, NodeId referenceType)
        {
            AddReference(node, ReferenceTypeIds.HierarchicalReferences, BrowseDirection.Inverse,
                new ReferenceDescription
                {
                    NodeId = owner,
                    ReferenceTypeId = referenceType,
                    IsForward = false,
                    NodeClass = NodeClass.Object
                });
        }

        public void AddSubtype(NodeId type, ExpandedNodeId supertype)
        {
            AddReference(type, ReferenceTypeIds.HasSubtype, BrowseDirection.Inverse, new ReferenceDescription
            {
                NodeId = supertype,
                ReferenceTypeId = ReferenceTypeIds.HasSubtype,
                IsForward = false,
                NodeClass = NodeClass.ObjectType
            });
        }

        public void AddAncestry(NodeId actual, NodeId expected)
        {
            m_ancestry.Add((actual, expected));
        }

        public void SetBrowsePage(
            NodeId node,
            NodeId referenceType,
            BrowseDirection direction,
            ArrayOf<ReferenceDescription> references,
            ByteString continuationPoint = default)
        {
            AddNode(node);
            m_pages[new BrowseKey(node, referenceType, direction)] = new BrowseResult
            {
                StatusCode = StatusCodes.Good,
                References = references,
                ContinuationPoint = continuationPoint
            };
        }

        public void SetBrowseFailure(
            NodeId node,
            NodeId referenceType,
            BrowseDirection direction,
            StatusCode status)
        {
            m_pages[new BrowseKey(node, referenceType, direction)] = new BrowseResult
            {
                StatusCode = status,
                References = []
            };
        }

        public void SetContinuationPage(
            ByteString point,
            ArrayOf<ReferenceDescription> references,
            ByteString nextPoint = default)
        {
            m_continuationPages[point] = new BrowseResult
            {
                StatusCode = StatusCodes.Good,
                References = references,
                ContinuationPoint = nextPoint
            };
        }

        public void VerifyNoMutations()
        {
            Assert.That(Mutations, Is.Empty);
            Session.Verify(session => session.CallAsync(
                It.IsAny<RequestHeader?>(), It.IsAny<ArrayOf<CallMethodRequest>>(),
                It.IsAny<CancellationToken>()), Times.Never);
            Session.Verify(session => session.WriteAsync(
                It.IsAny<RequestHeader?>(), It.IsAny<ArrayOf<WriteValue>>(),
                It.IsAny<CancellationToken>()), Times.Never);
            Session.Verify(session => session.AddNodesAsync(
                It.IsAny<RequestHeader?>(), It.IsAny<ArrayOf<AddNodesItem>>(),
                It.IsAny<CancellationToken>()), Times.Never);
            Session.Verify(session => session.DeleteNodesAsync(
                It.IsAny<RequestHeader?>(), It.IsAny<ArrayOf<DeleteNodesItem>>(),
                It.IsAny<CancellationToken>()), Times.Never);
            Session.Verify(session => session.AddReferencesAsync(
                It.IsAny<RequestHeader?>(), It.IsAny<ArrayOf<AddReferencesItem>>(),
                It.IsAny<CancellationToken>()), Times.Never);
            Session.Verify(session => session.DeleteReferencesAsync(
                It.IsAny<RequestHeader?>(), It.IsAny<ArrayOf<DeleteReferencesItem>>(),
                It.IsAny<CancellationToken>()), Times.Never);
            Session.Verify(session => session.HistoryUpdateAsync(
                It.IsAny<RequestHeader?>(), It.IsAny<ArrayOf<ExtensionObject>>(),
                It.IsAny<CancellationToken>()), Times.Never);
        }

        private void AddReference(
            NodeId node,
            NodeId referenceType,
            BrowseDirection direction,
            ReferenceDescription reference)
        {
            AddNode(node);
            var key = new BrowseKey(node, referenceType, direction);
            if (!m_pages.TryGetValue(key, out BrowseResult? page))
            {
                page = new BrowseResult { StatusCode = StatusCodes.Good, References = [] };
                m_pages.Add(key, page);
            }
            var references = new ReferenceDescription[page.References.Count + 1];
            for (int index = 0; index < page.References.Count; index++)
            {
                references[index] = page.References[index];
            }
            references[^1] = reference;
            page.References = references;
        }

        public readonly record struct BrowseKey(NodeId Node, NodeId ReferenceType, BrowseDirection Direction);

        public sealed record BrowseQuery(
            RequestHeader? Header,
            ViewDescription? View,
            uint Maximum,
            BrowseDescription Request,
            BrowseResult Result,
            CancellationToken Token);

        public sealed record ContinuationQuery(
            RequestHeader? Header,
            bool Release,
            ByteString Point,
            BrowseResult Result,
            CancellationToken Token);

        public readonly record struct CacheQuery(
            NodeId Actual, NodeId Expected, bool Matches, CancellationToken Token);

        private readonly HashSet<NodeId> m_nodes = [];
        private readonly Dictionary<BrowseKey, BrowseResult> m_pages = [];
        private readonly Dictionary<ByteString, BrowseResult> m_continuationPages = [];
        private readonly HashSet<(NodeId Actual, NodeId Expected)> m_ancestry = [];
        private readonly List<BrowseQuery> m_browses = [];
        private readonly List<ContinuationQuery> m_continuations = [];
        private readonly List<CacheQuery> m_cacheQueries = [];
    }
}
#endif
