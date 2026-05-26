/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
 * MIT License - see /Docs/License.md
 * ======================================================================*/
#nullable enable
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Client.Tests
{
    /// <summary>
    /// Unit tests for <see cref="NodeCache.InvalidateNode"/>.
    /// Verifies the no-op contract for null ids and that invalidation
    /// removes entries from every internal cache (nodes, references,
    /// values) — observed via subsequent fetch counts on the mocked
    /// <see cref="INodeCacheContext"/>.
    /// </summary>
    [TestFixture]
    [Category("Client")]
    [Parallelizable]
    public sealed class NodeCacheInvalidateNodeTests
    {
        private static Mock<INodeCacheContext> CreateContextMock()
        {
            var context = new Mock<INodeCacheContext>();
            context.Setup(c => c.NamespaceUris).Returns(new NamespaceTable());
            context.Setup(c => c.FetchReferencesAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<NodeId>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ArrayOf<ReferenceDescription>());
            return context;
        }

        [Test]
        public void InvalidateNodeWithNullNodeIdIsNoOp()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            Mock<INodeCacheContext> context = CreateContextMock();
            using var cache = new NodeCache(context.Object, telemetry);

            Assert.That(() => cache.InvalidateNode(NodeId.Null),
                Throws.Nothing);
        }

        [Test]
        public async Task InvalidateNodeRemovesEntryFromAllInternalDictionaries()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var id = new NodeId("inv-test", 0);

            Mock<INodeCacheContext> context = CreateContextMock();
            context.Setup(c => c.FetchNodeAsync(
                    It.IsAny<RequestHeader>(),
                    It.Is<NodeId>(i => i == id),
                    NodeClass.Unspecified,
                    false,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Node { NodeId = id });
            context.Setup(c => c.FetchValueAsync(
                    It.IsAny<RequestHeader>(),
                    It.Is<NodeId>(i => i == id),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new DataValue(new Variant(123)));

            using var cache = new NodeCache(context.Object, telemetry);

            // Populate node + value + references caches.
            _ = await cache.GetNodeAsync(id, default).ConfigureAwait(false);
            _ = await cache.GetValueAsync(id, default).ConfigureAwait(false);

            // Second read of value should hit cache, not fetch.
            _ = await cache.GetValueAsync(id, default).ConfigureAwait(false);

            context.Verify(c => c.FetchNodeAsync(
                It.IsAny<RequestHeader>(),
                It.Is<NodeId>(i => i == id),
                NodeClass.Unspecified, false,
                It.IsAny<CancellationToken>()), Times.Once());
            context.Verify(c => c.FetchValueAsync(
                It.IsAny<RequestHeader>(),
                It.Is<NodeId>(i => i == id),
                It.IsAny<CancellationToken>()), Times.Once());

            // Invalidate — should clear node, value and references caches.
            cache.InvalidateNode(id);

            // Subsequent calls force fresh fetches.
            _ = await cache.GetNodeAsync(id, default).ConfigureAwait(false);
            _ = await cache.GetValueAsync(id, default).ConfigureAwait(false);

            context.Verify(c => c.FetchNodeAsync(
                It.IsAny<RequestHeader>(),
                It.Is<NodeId>(i => i == id),
                NodeClass.Unspecified, false,
                It.IsAny<CancellationToken>()), Times.Exactly(2));
            context.Verify(c => c.FetchValueAsync(
                It.IsAny<RequestHeader>(),
                It.Is<NodeId>(i => i == id),
                It.IsAny<CancellationToken>()), Times.Exactly(2));
        }

        [Test]
        public async Task InvalidateNodeLeavesOtherNodesIntact()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var id1 = new NodeId("keep", 0);
            var id2 = new NodeId("drop", 0);

            Mock<INodeCacheContext> context = CreateContextMock();
            context.Setup(c => c.FetchNodeAsync(
                    It.IsAny<RequestHeader>(),
                    It.Is<NodeId>(i => i == id1),
                    NodeClass.Unspecified, false,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Node { NodeId = id1 });
            context.Setup(c => c.FetchNodeAsync(
                    It.IsAny<RequestHeader>(),
                    It.Is<NodeId>(i => i == id2),
                    NodeClass.Unspecified, false,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Node { NodeId = id2 });

            using var cache = new NodeCache(context.Object, telemetry);

            _ = await cache.GetNodeAsync(id1, default).ConfigureAwait(false);
            _ = await cache.GetNodeAsync(id2, default).ConfigureAwait(false);

            cache.InvalidateNode(id2);

            // Re-fetching id1 should hit the cache — fetch count remains 1.
            _ = await cache.GetNodeAsync(id1, default).ConfigureAwait(false);
            // Re-fetching id2 should miss the cache — fetch count goes to 2.
            _ = await cache.GetNodeAsync(id2, default).ConfigureAwait(false);

            context.Verify(c => c.FetchNodeAsync(
                It.IsAny<RequestHeader>(),
                It.Is<NodeId>(i => i == id1),
                NodeClass.Unspecified, false,
                It.IsAny<CancellationToken>()), Times.Once());
            context.Verify(c => c.FetchNodeAsync(
                It.IsAny<RequestHeader>(),
                It.Is<NodeId>(i => i == id2),
                NodeClass.Unspecified, false,
                It.IsAny<CancellationToken>()), Times.Exactly(2));
        }
    }
}
