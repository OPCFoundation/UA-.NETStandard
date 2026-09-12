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

// CA1835: the byte[] ReadAsync overload is used because the fixture targets
// every TFM of the parent project.
#pragma warning disable CA1835
// CA2000: test code; short-lived disposables, no real leak risk.
#pragma warning disable CA2000
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client.FileSystem;
using Opc.Ua.Client.Tests.FileSystem;
using Opc.Ua.Client.WebApi;
using Opc.Ua.Bindings;
using Opc.Ua.Tests;

namespace Opc.Ua.Client.Tests.AuditRegressions
{
    /// <summary>
    /// Regressions for the node cache, file system, web API and reverse
    /// connect defects reported by the Opc.Ua.Client audit. Each test names
    /// the defect it pins down.
    /// </summary>
    [TestFixture]
    [Category("Client")]
    [Category("AuditRegression")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public sealed class ClientServicesAuditRegressionTests
    {
        /// <summary>
        /// The batched GetReferencesAsync emitted the targets of the already
        /// cached inputs first and the freshly fetched ones afterwards, so the
        /// result no longer followed the caller's input order - which every
        /// consumer that pairs results with inputs positionally relies on.
        /// </summary>
        [Test]
        public async Task BatchedGetReferencesKeepsTheInputOrderAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            NodeId first = new("First", 2);
            NodeId second = new("Second", 2);

            var context = new Mock<INodeCacheContext>();
            context.SetupGet(c => c.NamespaceUris).Returns(new NamespaceTable());
            context.SetupGet(c => c.ServerUris).Returns(new StringTable());
            context
                .Setup(c => c.FetchReferencesAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<NodeId>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((RequestHeader _, NodeId id, CancellationToken _) =>
                    new[] { Reference(id.IdentifierAsString + "Target") }.ToArrayOf());
            context
                .Setup(c => c.FetchNodesAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<ArrayOf<NodeId>>(),
                    It.IsAny<bool>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((RequestHeader _, ArrayOf<NodeId> ids, bool _, CancellationToken _)
                    => new ResultSet<Node>
                    {
                        Results = ids
                            .ConvertAll(id => new Node { NodeId = id, NodeClass = NodeClass.Object })
                            .ToList(),
                        Errors = ids.ConvertAll(_ => ServiceResult.Good).ToList()
                    });

            using var nodeCache = new NodeCache(context.Object, telemetry);

            // Warm the cache for the SECOND node only, so the buggy
            // implementation would emit its target before the first node's.
            await nodeCache
                .GetReferencesAsync(second, ReferenceTypeIds.HasComponent, false, false, default)
                .ConfigureAwait(false);

            ArrayOf<INode> result = await nodeCache
                .GetReferencesAsync(
                    new[] { first, second }.ToArrayOf(),
                    new[] { ReferenceTypeIds.HasComponent }.ToArrayOf(),
                    false,
                    false,
                    default)
                .ConfigureAwait(false);

            Assert.That(result.Count, Is.EqualTo(2));
            Assert.That(
                result[0].NodeId.IdentifierAsString,
                Is.EqualTo("FirstTarget"),
                "results must follow the order of the supplied node ids");
        }

        /// <summary>
        /// The supertype walk had no cycle protection, so a server answering
        /// HasSubtype with a loop spun forever.
        /// </summary>
        [Test]
        public async Task SupertypeWalkTerminatesOnACyclicHierarchyAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            NodeId a = new("A", 2);
            NodeId b = new("B", 2);

            // Bound the fake so a regression fails the assertion below instead
            // of spinning the test host forever.
            const int maxAnswers = 1000;
            int answers = 0;

            var context = new Mock<INodeCacheContext>();
            context.SetupGet(c => c.NamespaceUris).Returns(new NamespaceTable());
            context.SetupGet(c => c.ServerUris).Returns(new StringTable());
            context
                .Setup(c => c.FetchReferencesAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<NodeId>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((RequestHeader _, NodeId id, CancellationToken _) =>
                {
                    if (Interlocked.Increment(ref answers) > maxAnswers)
                    {
                        return ArrayOf.Empty<ReferenceDescription>();
                    }
                    // A's supertype is B and B's supertype is A.
                    return new[] { InverseSubtypeOf(id == a ? b : a) }.ToArrayOf();
                });

            using var nodeCache = new NodeCache(context.Object, telemetry);

            BuiltInType result = await nodeCache
                .GetBuiltInTypeAsync(a, default)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.EqualTo(BuiltInType.Null));
                Assert.That(
                    answers,
                    Is.LessThan(maxAnswers),
                    "a cyclic HasSubtype answer must stop the walk, not loop on it");
            });
        }

        /// <summary>
        /// UaFileStream copied whatever the server returned into the caller's
        /// buffer, so a server answering with more bytes than were asked for
        /// wrote past the requested window.
        /// </summary>
        [Test]
        public async Task FileStreamRejectsAnOversizedServerReadAsync()
        {
            var session = new FileTypeSessionMock();
            var proxy = new FileTypeClient(
                session.Session,
                new NodeId(42),
                session.Session.MessageContext.Telemetry);
            session.OnClose(_ => { });

            // The server answers a 4 byte chunk request with 16 bytes.
            session.OnRead((_, length) => new byte[16]);

            var stream = new UaFileStream(
                proxy,
                handle: 7,
                UaFileMode.Read,
                initialLength: 1024,
                initialPosition: 0,
                chunkSize: 4);
            await using (stream.ConfigureAwait(false))
            {
                var buffer = new byte[4];
                Assert.ThrowsAsync<ServiceResultException>(
                    () => stream.ReadAsync(buffer, 0, buffer.Length, CancellationToken.None),
                    "the surplus must not be written past the caller's window");
            }
        }

        /// <summary>
        /// DisposeHandler defaulted to true and was honoured for injected
        /// handlers, so the first channel to close disposed the handler every
        /// other channel was still using.
        /// </summary>
        [Test]
        public void InjectedHttpMessageHandlerIsNotDisposedByDefault()
        {
            var handler = new TrackingHandler();

            using (WebApiClient.Create(
                new Uri("https://localhost:4843/"),
                new WebApiClientOptions { HttpMessageHandler = handler }))
            {
            }

            Assert.That(
                handler.Disposed,
                Is.False,
                "a caller supplied handler is commonly shared between channels");
        }

        /// <summary>
        /// Wait and callback registrations were identified by
        /// <see cref="object.GetHashCode"/>, which is not unique - so
        /// unregistering could remove someone else's registration.
        /// </summary>
        [Test]
        public async Task ReverseConnectRegistrationIdsAreUniqueAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using var manager = new ReverseConnectManager(telemetry);

            var ids = new List<int>();
            for (int i = 0; i < 64; i++)
            {
                ids.Add(await manager
                    .RegisterWaitingConnectionAsync(
                        new Uri("opc.tcp://localhost:54330/"),
                        null,
                        (_, _) => { },
                        ReverseConnectManager.ReverseConnectStrategy.Once)
                    .ConfigureAwait(false));
            }

            // Minted from a counter, so they are unique by construction.
            // Object.GetHashCode gives neither uniqueness nor ordering.
            Assert.That(
                ids,
                Is.Ordered.Ascending.And.Unique,
                "registration ids must come from a counter; colliding hash " +
                "codes unregister the wrong waiter");

            foreach (int id in ids)
            {
                manager.UnregisterWaitingConnection(id);
            }
        }

        /// <summary>
        /// PathCache was documented as not thread-safe but the FileSystemClient
        /// mutates it from concurrent operations, which corrupts the LRU list
        /// and the map.
        /// </summary>
        [Test]
        public void PathCacheToleratesConcurrentMutation()
        {
            var cache = new PathCache(64);
            var parent = new NodeId("parent", 2);

            Assert.That(
                () => Parallel.For(0, 2000, i =>
                {
                    var name = new QualifiedName("child" + (i % 128), 2);
                    cache.Put(parent, name, new NodeId("child" + i, 2));
                    cache.TryGet(parent, name);
                    if (i % 17 == 0)
                    {
                        cache.Invalidate(parent, name);
                    }
                    if (i % 501 == 0)
                    {
                        cache.InvalidateChildrenOf(parent);
                    }
                }),
                Throws.Nothing);
        }

        /// <summary>
        /// Surfacing the browse error from the single node fetch made the bulk
        /// paths inherit it: one node the server refuses to browse failed the
        /// whole batched GetReferencesAsync and the whole GetNodesAsync it was
        /// part of, where it previously merely contributed no references.
        /// </summary>
        [Test]
        public async Task BulkNodeCacheReadsTolerateAnUnbrowsableNodeAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            NodeId readable = new("Readable", 2);
            NodeId denied = new("Denied", 2);

            var context = new Mock<INodeCacheContext>();
            context.SetupGet(c => c.NamespaceUris).Returns(new NamespaceTable());
            context.SetupGet(c => c.ServerUris).Returns(new StringTable());
            context
                .Setup(c => c.FetchReferencesAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<NodeId>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((RequestHeader _, NodeId id, CancellationToken _) =>
                    id == denied
                        ? throw new ServiceResultException(StatusCodes.BadUserAccessDenied)
                        : new[] { Reference(id.IdentifierAsString + "Target") }.ToArrayOf());
            context
                .Setup(c => c.FetchNodesAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<ArrayOf<NodeId>>(),
                    It.IsAny<bool>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((RequestHeader _, ArrayOf<NodeId> ids, bool _, CancellationToken _)
                    => new ResultSet<Node>
                    {
                        Results = ids
                            .ConvertAll(id => new Node { NodeId = id, NodeClass = NodeClass.Object })
                            .ToList(),
                        Errors = ids.ConvertAll(_ => ServiceResult.Good).ToList()
                    });

            using var nodeCache = new NodeCache(context.Object, telemetry);

            ArrayOf<INode> nodes = await nodeCache
                .GetNodesAsync(new[] { readable, denied }.ToArrayOf(), default)
                .ConfigureAwait(false);
            ArrayOf<INode> targets = await nodeCache
                .GetReferencesAsync(
                    new[] { readable, denied }.ToArrayOf(),
                    new[] { ReferenceTypeIds.HasComponent }.ToArrayOf(),
                    false,
                    false,
                    default)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(
                    nodes.Count,
                    Is.EqualTo(2),
                    "a node the server reads but refuses to browse is still a node");
                Assert.That(targets.Count, Is.EqualTo(1));
                Assert.That(
                    targets[0].NodeId.IdentifierAsString,
                    Is.EqualTo("ReadableTarget"),
                    "the browsable input's references must still be returned");
            });
        }

        /// <summary>
        /// Tolerating an un-browsable node must not extend to a failure of the
        /// call itself: absorbing a session or channel error as "this node has
        /// no references" would hand the caller a silently incomplete result
        /// for every remaining input too.
        /// </summary>
        [Test]
        public void BulkNodeCacheReadsStillSurfaceAServiceLevelFailure()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            var context = new Mock<INodeCacheContext>();
            context.SetupGet(c => c.NamespaceUris).Returns(new NamespaceTable());
            context.SetupGet(c => c.ServerUris).Returns(new StringTable());
            context
                .Setup(c => c.FetchReferencesAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<NodeId>(),
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new ServiceResultException(StatusCodes.BadSessionClosed));
            context
                .Setup(c => c.FetchNodesAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<ArrayOf<NodeId>>(),
                    It.IsAny<bool>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((RequestHeader _, ArrayOf<NodeId> ids, bool _, CancellationToken _)
                    => new ResultSet<Node>
                    {
                        Results = ids
                            .ConvertAll(id => new Node { NodeId = id, NodeClass = NodeClass.Object })
                            .ToList(),
                        Errors = ids.ConvertAll(_ => ServiceResult.Good).ToList()
                    });

            using var nodeCache = new NodeCache(context.Object, telemetry);

            ServiceResultException ex = Assert.ThrowsAsync<ServiceResultException>(
                async () => await nodeCache
                    .GetNodesAsync(new NodeId[] { new("A", 2) }.ToArrayOf(), default)
                    .ConfigureAwait(false));

            Assert.That(
                ex!.StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.BadSessionClosed),
                "a dead session must not be reported as a node without references");
        }

        /// <summary>
        /// The browse path walker climbs supertypes when a name is not found,
        /// and that climb had no cycle guard: against a server whose HasSubtype
        /// chain loops back on itself it never reached Null and spun forever.
        /// </summary>
        [Test]
        public async Task BrowsePathSupertypeClimbTerminatesOnACycleAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            NodeId a = new("A", 2);
            NodeId b = new("B", 2);

            const int maxAnswers = 1000;
            int answers = 0;

            var context = new Mock<INodeCacheContext>();
            context.SetupGet(c => c.NamespaceUris).Returns(new NamespaceTable());
            context.SetupGet(c => c.ServerUris).Returns(new StringTable());
            context
                .Setup(c => c.FetchReferencesAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<NodeId>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((RequestHeader _, NodeId id, CancellationToken _) =>
                {
                    if (Interlocked.Increment(ref answers) > maxAnswers)
                    {
                        return ArrayOf.Empty<ReferenceDescription>();
                    }
                    // Only inverse HasSubtype references, and they loop: the
                    // browse name is never found at any level.
                    return new[] { InverseSubtypeOf(id == a ? b : a) }.ToArrayOf();
                });

            using var nodeCache = new NodeCache(context.Object, telemetry);

            // The bound is the assertion: after the first lap every lookup is a
            // cache hit, so an unguarded climb spins without ever calling the
            // mock again.
            Task<INode> walk = nodeCache
                .GetNodeWithBrowsePathAsync(
                    a,
                    new[] { new QualifiedName("Missing", 2) }.ToArrayOf(),
                    default)
                .AsTask();
            Task completed = await Task
                .WhenAny(walk, Task.Delay(TimeSpan.FromSeconds(10)))
                .ConfigureAwait(false);
            Assert.That(
                ReferenceEquals(completed, walk),
                Is.True,
                "the supertype climb must terminate on a cyclic hierarchy");
            Assert.That(await walk.ConfigureAwait(false), Is.Null);
        }

        /// <summary>
        /// The synchronous type-of walk used by the reference filter stops
        /// tracking nothing and counting levels only up to a depth no real
        /// hierarchy reaches; past that it must detect an actual repeated
        /// node rather than call a deep-but-finite chain a cycle.
        /// </summary>
        [Test]
        public async Task DeepTypeHierarchyIsNotMistakenForACycleAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            // Far deeper than the level at which cycle tracking starts.
            const int depth = 200;
            NodeId RefType(int level) => new("RefType" + level, 2);

            NodeId source = new("Source", 2);
            NodeId root = RefType(depth);

            var context = new Mock<INodeCacheContext>();
            context.SetupGet(c => c.NamespaceUris).Returns(new NamespaceTable());
            context.SetupGet(c => c.ServerUris).Returns(new StringTable());
            context
                .Setup(c => c.FetchReferencesAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<NodeId>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((RequestHeader _, NodeId id, CancellationToken _) =>
                {
                    if (id == source)
                    {
                        // One reference, typed with the deepest subtype.
                        return new[]
                        {
                            new ReferenceDescription
                            {
                                NodeId = new ExpandedNodeId(new NodeId("Target", 2)),
                                BrowseName = new QualifiedName("Target", 2),
                                ReferenceTypeId = RefType(0),
                                IsForward = true
                            }
                        }.ToArrayOf();
                    }
                    // RefType(n) is a subtype of RefType(n + 1) up to the root.
                    string identifier = id.IdentifierAsString;
                    if (identifier.StartsWith("RefType", StringComparison.Ordinal) &&
                        int.TryParse(
                            identifier["RefType".Length..],
                            System.Globalization.NumberStyles.Integer,
                            System.Globalization.CultureInfo.InvariantCulture,
                            out int level) &&
                        level < depth)
                    {
                        return new[] { InverseSubtypeOf(RefType(level + 1)) }.ToArrayOf();
                    }
                    return ArrayOf.Empty<ReferenceDescription>();
                });
            context
                .Setup(c => c.FetchNodesAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<ArrayOf<NodeId>>(),
                    It.IsAny<bool>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((RequestHeader _, ArrayOf<NodeId> ids, bool _, CancellationToken _)
                    => new ResultSet<Node>
                    {
                        Results = ids
                            .ConvertAll(id => new Node { NodeId = id, NodeClass = NodeClass.Object })
                            .ToList(),
                        Errors = ids.ConvertAll(_ => ServiceResult.Good).ToList()
                    });

            using var nodeCache = new NodeCache(context.Object, telemetry);

            ArrayOf<INode> targets = await nodeCache
                .GetReferencesAsync(source, root, false, true, default)
                .ConfigureAwait(false);

            Assert.That(
                targets.Count,
                Is.EqualTo(1),
                "a hierarchy that is deep but acyclic must still resolve");
        }

        private static ReferenceDescription Reference(string identifier)
        {
            return new ReferenceDescription
            {
                NodeId = new ExpandedNodeId(new NodeId(identifier, 2)),
                BrowseName = new QualifiedName(identifier, 2),
                ReferenceTypeId = ReferenceTypeIds.HasComponent,
                IsForward = true
            };
        }

        private static ReferenceDescription InverseSubtypeOf(NodeId superType)
        {
            return new ReferenceDescription
            {
                NodeId = new ExpandedNodeId(superType),
                BrowseName = new QualifiedName(superType.IdentifierAsString, 2),
                ReferenceTypeId = ReferenceTypeIds.HasSubtype,
                IsForward = false
            };
        }

        /// <summary>
        /// Handler that records whether it was disposed.
        /// </summary>
        private sealed class TrackingHandler : HttpMessageHandler
        {
            public bool Disposed { get; private set; }

            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken)
            {
                return Task.FromResult(new HttpResponseMessage());
            }

            protected override void Dispose(bool disposing)
            {
                Disposed = true;
                base.Dispose(disposing);
            }
        }
    }
}
