/* Copyright (c) 2026 The OPC Foundation, Inc. All rights reserved.
 * Licensed under the MIT License. See LICENSE.txt in the project root. */

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Tests;
using Opc.Ua.WotCon.Client;

namespace Opc.Ua.WotCon.Tests.Client
{
    /// <summary>
    /// Exercises all three WoT browse wrappers, not just the shared browse helper.
    /// </summary>
    [TestFixture]
    [Category("WotCon")]
    [Parallelizable]
    public sealed class WotPagedBrowseTests
    {
        [SetUp]
        public void SetUp()
        {
            m_browseRequests.Clear();
            m_browseTokens.Clear();
            m_nextRequests.Clear();
            m_pages.Clear();
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            m_context = ServiceMessageContext.Create(telemetry);
            m_namespace = m_context.NamespaceUris.GetIndexOrAppend(Namespaces.WotCon);
            var session = new Mock<ISession>(MockBehavior.Strict);
            session.SetupGet(s => s.MessageContext).Returns(m_context);
            session.SetupGet(s => s.NamespaceUris).Returns(m_context.NamespaceUris);
            session.Setup(s => s.BrowseAsync(
                    It.IsAny<RequestHeader>(), It.IsAny<ViewDescription>(), It.IsAny<uint>(),
                    It.IsAny<ArrayOf<BrowseDescription>>(), It.IsAny<CancellationToken>()))
                .Returns<RequestHeader, ViewDescription, uint, ArrayOf<BrowseDescription>, CancellationToken>(
                    (_, _, size, requests, token) =>
                    {
                        Assert.That(size, Is.EqualTo(100));
                        Assert.That(requests.Count, Is.EqualTo(1));
                        m_browseRequests.Add(requests[0]);
                        m_browseTokens.Add(token);
                        return new ValueTask<BrowseResponse>(new BrowseResponse
                        {
                            ResponseHeader = new ResponseHeader(),
                            Results = [m_pages.Dequeue()]
                        });
                    });
            session.Setup(s => s.BrowseNextAsync(
                    It.IsAny<RequestHeader>(), It.IsAny<bool>(), It.IsAny<ArrayOf<ByteString>>(),
                    It.IsAny<CancellationToken>()))
                .Returns<RequestHeader, bool, ArrayOf<ByteString>, CancellationToken>(
                    (_, release, points, token) =>
                    {
                        Assert.That(points.Count, Is.EqualTo(1));
                        m_nextRequests.Add((release, points[0], token));
                        return new ValueTask<BrowseNextResponse>(new BrowseNextResponse
                        {
                            ResponseHeader = new ResponseHeader(),
                            Results = [release ? Page(default) : m_pages.Dequeue()]
                        });
                    });
            m_connectivity = new WotConnectivityClient(
                session.Object, new NodeId("management", m_namespace), telemetry);
            m_asset = new WotAssetClient(
                session.Object, new NodeId("asset", m_namespace), "Asset",
                new WoTAssetFileTypeClient(session.Object, new NodeId("td", m_namespace), telemetry), telemetry);
        }

        [Test]
        public async Task EnumerateAssetsAsyncFollowsShortAndEmptyPagesWithExactNamesAsync()
        {
            ArrangeThreePages();
            IAsyncEnumerable<WotAssetEntry> sequence = m_connectivity.EnumerateAssetsAsync();
            Assert.That(m_browseRequests, Is.Empty);
            var actual = new List<WotAssetEntry>();

            await foreach (WotAssetEntry entry in sequence.ConfigureAwait(false))
            {
                actual.Add(entry);
            }

            Assert.That(actual, Is.EqualTo(new[]
            {
                new WotAssetEntry(new NodeId("one", m_namespace), "First display name"),
                new WotAssetEntry(new NodeId("two", m_namespace), string.Empty)
            }));
            AssertBrowse(new NodeId("management", m_namespace), Ua.ReferenceTypeIds.Organizes, true, NodeClass.Object);
            AssertFollowedBothTokensWithoutRelease();
        }

        [Test]
        public async Task EnumeratePropertiesAsyncFollowsPagesUsingWoTVariableFilterAsync()
        {
            ArrangeThreePages();
            var actual = new List<WotAssetVariableEntry>();

            await foreach (WotAssetVariableEntry entry in m_asset.EnumeratePropertiesAsync().ConfigureAwait(false))
            {
                actual.Add(entry);
            }

            Assert.That(actual, Is.EqualTo(new[]
            {
                new WotAssetVariableEntry(new NodeId("one", m_namespace), "first-browse-name"),
                new WotAssetVariableEntry(new NodeId("two", m_namespace), string.Empty)
            }));
            AssertBrowse(new NodeId("asset", m_namespace),
                ExpandedNodeId.ToNodeId(ReferenceTypeIds.HasWoTComponent, m_context.NamespaceUris),
                false, NodeClass.Variable);
            AssertFollowedBothTokensWithoutRelease();
        }

        [Test]
        public async Task EnumerateActionsAsyncFollowsPagesUsingMethodFilterAsync()
        {
            ArrangeThreePages();
            var actual = new List<WotAssetVariableEntry>();

            await foreach (WotAssetVariableEntry entry in m_asset.EnumerateActionsAsync().ConfigureAwait(false))
            {
                actual.Add(entry);
            }

            Assert.That(actual, Is.EqualTo(new[]
            {
                new WotAssetVariableEntry(new NodeId("one", m_namespace), "first-browse-name"),
                new WotAssetVariableEntry(new NodeId("two", m_namespace), string.Empty)
            }));
            AssertBrowse(new NodeId("asset", m_namespace), Ua.ReferenceTypeIds.HasComponent, false, NodeClass.Method);
            AssertFollowedBothTokensWithoutRelease();
        }

        [TestCase("assets", false)]
        [TestCase("assets", true)]
        [TestCase("properties", false)]
        [TestCase("properties", true)]
        [TestCase("actions", false)]
        [TestCase("actions", true)]
        public async Task PagedWrapperRejectsUnresolvableOrRemoteReferenceAndReleasesToken(string kind, bool remote)
        {
            ByteString point = ByteString.From([91, 92]);
            ExpandedNodeId target = remote
                ? new ExpandedNodeId(new NodeId("foreign", m_namespace), null, 4)
                : new ExpandedNodeId("foreign", "urn:unresolvable:wot");
            m_pages.Enqueue(Page(point, new ReferenceDescription { NodeId = target }));
            int namespaceCount = m_context.NamespaceUris.Count;

            ServiceResultException exception = (await Assert.ThrowsAsync<ServiceResultException>(async () =>
            {
                await foreach (NodeId id in EnumerateIds(kind).ConfigureAwait(false))
                {
                    Assert.Fail($"An invalid reference was yielded as {id}.");
                }
            }))!;

            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadNotSupported));
            Assert.That(m_context.NamespaceUris.Count, Is.EqualTo(namespaceCount));
            Assert.That(m_browseRequests, Has.Count.EqualTo(1));
            Assert.That(m_nextRequests, Is.EqualTo(new[] { (true, point, CancellationToken.None) }));
        }

        [TestCase("assets")]
        [TestCase("properties")]
        [TestCase("actions")]
        public async Task PagedWrapperEarlyStopReleasesOutstandingTokenAsync(string kind)
        {
            ByteString point = ByteString.From([93]);
            m_pages.Enqueue(Page(point,
                new ReferenceDescription { NodeId = new ExpandedNodeId("one", m_namespace) },
                new ReferenceDescription { NodeId = new ExpandedNodeId("two", m_namespace) }));
            var actual = new List<NodeId>();

            await foreach (NodeId id in EnumerateIds(kind).ConfigureAwait(false))
            {
                actual.Add(id);
                break;
            }

            Assert.That(actual, Is.EqualTo(new[] { new NodeId("one", m_namespace) }));
            Assert.That(m_nextRequests, Is.EqualTo(new[] { (true, point, CancellationToken.None) }));
        }

        [TestCase("assets")]
        [TestCase("properties")]
        [TestCase("actions")]
        public async Task PagedWrapperCancellationStopsBeforeNextReferenceAndReleasesTokenAsync(string kind)
        {
            ByteString point = ByteString.From([94]);
            using var cancellation = new CancellationTokenSource();
            m_pages.Enqueue(Page(point,
                new ReferenceDescription { NodeId = new ExpandedNodeId("one", m_namespace) },
                new ReferenceDescription { NodeId = new ExpandedNodeId("must-not-yield", m_namespace) }));
            IAsyncEnumerator<NodeId> iterator = EnumerateIds(kind, cancellation.Token).GetAsyncEnumerator();

            await using (iterator.ConfigureAwait(false))
            {
                Assert.That(await iterator.MoveNextAsync().ConfigureAwait(false), Is.True);
                Assert.That(iterator.Current, Is.EqualTo(new NodeId("one", m_namespace)));
                cancellation.Cancel();
                OperationCanceledException exception = (await Assert.ThrowsAsync<OperationCanceledException>(
                    async () => await iterator.MoveNextAsync().ConfigureAwait(false)))!;
                Assert.That(exception.CancellationToken, Is.EqualTo(cancellation.Token));
            }

            Assert.That(m_browseTokens, Is.EqualTo(new[] { cancellation.Token }));
            Assert.That(m_nextRequests, Is.EqualTo(new[] { (true, point, CancellationToken.None) }));
        }

        private async IAsyncEnumerable<NodeId> EnumerateIds(
            string kind, [EnumeratorCancellation] CancellationToken ct = default)
        {
            if (kind == "assets")
            {
                await foreach (WotAssetEntry entry in m_connectivity.EnumerateAssetsAsync(ct).ConfigureAwait(false))
                {
                    yield return entry.AssetId;
                }
            }
            else
            {
                IAsyncEnumerable<WotAssetVariableEntry> sequence = kind == "properties"
                    ? m_asset.EnumeratePropertiesAsync(ct)
                    : m_asset.EnumerateActionsAsync(ct);
                await foreach (WotAssetVariableEntry entry in sequence.ConfigureAwait(false))
                {
                    yield return entry.NodeId;
                }
            }
        }

        private void ArrangeThreePages()
        {
            m_pages.Enqueue(Page(ByteString.From([1]),
                new ReferenceDescription
                {
                    NodeId = new ExpandedNodeId("one", Namespaces.WotCon),
                    BrowseName = new QualifiedName("first-browse-name", m_namespace),
                    DisplayName = new LocalizedText("First display name")
                }));
            m_pages.Enqueue(Page(ByteString.From([2])));
            m_pages.Enqueue(Page(default,
                new ReferenceDescription
                {
                    NodeId = new ExpandedNodeId("two", m_namespace),
                    BrowseName = QualifiedName.Null,
                    DisplayName = LocalizedText.Null
                }));
        }

        private void AssertBrowse(NodeId node, NodeId referenceType, bool subtypes, NodeClass nodeClass)
        {
            Assert.That(m_browseRequests, Has.Count.EqualTo(1));
            BrowseDescription request = m_browseRequests[0];
            Assert.Multiple(() =>
            {
                Assert.That(request.NodeId, Is.EqualTo(node));
                Assert.That(request.ReferenceTypeId, Is.EqualTo(referenceType));
                Assert.That(request.IncludeSubtypes, Is.EqualTo(subtypes));
                Assert.That(request.NodeClassMask, Is.EqualTo((uint)nodeClass));
                Assert.That(request.BrowseDirection, Is.EqualTo(BrowseDirection.Forward));
            });
        }

        private void AssertFollowedBothTokensWithoutRelease()
        {
            Assert.That(m_nextRequests.Select(r => (r.Release, r.Point)),
                Is.EqualTo(new[]
                {
                    (false, ByteString.From(new byte[] { 1 })), (false, ByteString.From(new byte[] { 2 }))
                }));
        }

        private static BrowseResult Page(ByteString point, params ReferenceDescription[] references)
        {
            return new BrowseResult
            {
                StatusCode = StatusCodes.Good,
                ContinuationPoint = point,
                References = references.ToArrayOf()
            };
        }

        private ushort m_namespace;
        private ServiceMessageContext m_context = null!;
        private WotConnectivityClient m_connectivity = null!;
        private WotAssetClient m_asset = null!;
        private readonly List<BrowseDescription> m_browseRequests = [];
        private readonly List<CancellationToken> m_browseTokens = [];
        private readonly List<(bool Release, ByteString Point, CancellationToken Token)> m_nextRequests = [];
        private readonly Queue<BrowseResult> m_pages = new();
    }
}
