/* Copyright (c) 2026 The OPC Foundation, Inc. All rights reserved.
 * Licensed under the MIT License. See LICENSE.txt in the project root. */

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Client.Tests
{
    /// <summary>
    /// Continuation ownership and progress guarantees of the lazy browse API.
    /// </summary>
    [TestFixture]
    [Category("Client")]
    [Parallelizable]
    public sealed class SessionBrowseStreamingTests
    {
        [SetUp]
        public void SetUp()
        {
            m_browseRequests.Clear();
            m_nextRequests.Clear();
            m_firstPage = Page(default);
            m_nextPage = (_, _) => throw new InvalidOperationException("Unexpected next page.");
            m_session = new Mock<ISessionClient>(MockBehavior.Strict);
            m_session.SetupGet(s => s.MessageContext)
                .Returns(ServiceMessageContext.Create(NUnitTelemetryContext.Create()));
            m_session.Setup(s => s.BrowseAsync(
                    It.IsAny<RequestHeader>(), It.IsAny<ViewDescription>(), It.IsAny<uint>(),
                    It.IsAny<ArrayOf<BrowseDescription>>(), It.IsAny<CancellationToken>()))
                .Returns<RequestHeader, ViewDescription, uint, ArrayOf<BrowseDescription>, CancellationToken>(
                    (_, _, size, requests, token) =>
                    {
                        Assert.That(requests.Count, Is.EqualTo(1));
                        m_browseRequests.Add((size, requests[0], token));
                        return new ValueTask<BrowseResponse>(new BrowseResponse
                        {
                            ResponseHeader = new ResponseHeader(),
                            Results = [m_firstPage]
                        });
                    });
            m_session.Setup(s => s.BrowseNextAsync(
                    It.IsAny<RequestHeader>(), It.IsAny<bool>(), It.IsAny<ArrayOf<ByteString>>(),
                    It.IsAny<CancellationToken>()))
                .Returns<RequestHeader, bool, ArrayOf<ByteString>, CancellationToken>(
                    (_, release, points, token) =>
                    {
                        Assert.That(points.Count, Is.EqualTo(1));
                        m_nextRequests.Add((release, points[0], token));
                        BrowseResult page = release ? Page(default) : m_nextPage(points[0], token);
                        return new ValueTask<BrowseNextResponse>(new BrowseNextResponse
                        {
                            ResponseHeader = new ResponseHeader(),
                            Results = [page]
                        });
                    });
        }

        [Test]
        public async Task EnumerateReferencesAsyncIsLazyAndFollowsShortAndEmptyPagesAsync()
        {
            ByteString first = ByteString.From([11]);
            ByteString second = ByteString.From([22]);
            m_firstPage = Page(first, Reference("alpha"));
            var pages = new Queue<BrowseResult>(
                [Page(second), Page(default, Reference("beta"), Reference("gamma"))]);
            m_nextPage = (_, _) => pages.Dequeue();
            using var cancellation = new CancellationTokenSource();

            IAsyncEnumerable<ReferenceDescription> references = m_session.Object.EnumerateReferencesAsync(
                new NodeId("root", 2), ReferenceTypeIds.HasComponent, BrowseDirection.Inverse,
                includeSubtypes: false, nodeClassMask: (uint)NodeClass.Variable,
                pageSize: 7, ct: cancellation.Token);
            Assert.That(m_browseRequests, Is.Empty, "Constructing a sequence must not browse.");
            IAsyncEnumerator<ReferenceDescription> iterator = references.GetAsyncEnumerator();
            await using (iterator.ConfigureAwait(false))
            {
                Assert.That(m_browseRequests, Is.Empty, "Creating the iterator must not browse.");
                Assert.That(await iterator.MoveNextAsync().ConfigureAwait(false), Is.True);
                Assert.That(iterator.Current.NodeId, Is.EqualTo(new ExpandedNodeId("alpha", 2)));
                Assert.That(m_nextRequests, Is.Empty, "Do not fetch a page the caller has not requested.");

                Assert.That(await iterator.MoveNextAsync().ConfigureAwait(false), Is.True);
                Assert.That(iterator.Current.NodeId, Is.EqualTo(new ExpandedNodeId("beta", 2)));
                Assert.That(await iterator.MoveNextAsync().ConfigureAwait(false), Is.True);
                Assert.That(iterator.Current.NodeId, Is.EqualTo(new ExpandedNodeId("gamma", 2)));
                Assert.That(await iterator.MoveNextAsync().ConfigureAwait(false), Is.False);
            }

            Assert.That(m_browseRequests, Has.Count.EqualTo(1));
            (uint size, BrowseDescription request, CancellationToken token) = m_browseRequests[0];
            Assert.Multiple(() =>
            {
                Assert.That(size, Is.EqualTo(7));
                Assert.That(request.NodeId, Is.EqualTo(new NodeId("root", 2)));
                Assert.That(request.ReferenceTypeId, Is.EqualTo(ReferenceTypeIds.HasComponent));
                Assert.That(request.BrowseDirection, Is.EqualTo(BrowseDirection.Inverse));
                Assert.That(request.IncludeSubtypes, Is.False);
                Assert.That(request.NodeClassMask, Is.EqualTo((uint)NodeClass.Variable));
                Assert.That(request.ResultMask, Is.EqualTo((uint)BrowseResultMask.All));
                Assert.That(token, Is.EqualTo(cancellation.Token));
                Assert.That(m_nextRequests.Select(r => r.Point), Is.EqualTo(new[] { first, second }));
                Assert.That(m_nextRequests.All(r => !r.Release && r.Token == cancellation.Token), Is.True);
            });
        }

        [Test]
        public async Task EnumerateReferencesAsyncReleasesLatestContinuationOnEarlyStopAsync()
        {
            ByteString first = ByteString.From([1]);
            ByteString latest = ByteString.From([2]);
            m_firstPage = Page(first, Reference("first"));
            m_nextPage = (_, _) => Page(latest, Reference("second"), Reference("unconsumed"));

            IAsyncEnumerator<ReferenceDescription> iterator = Enumerate().GetAsyncEnumerator();
            await using (iterator.ConfigureAwait(false))
            {
                Assert.That(await iterator.MoveNextAsync().ConfigureAwait(false), Is.True);
                Assert.That(await iterator.MoveNextAsync().ConfigureAwait(false), Is.True);
                Assert.That(iterator.Current.NodeId, Is.EqualTo(new ExpandedNodeId("second", 2)));
            }

            Assert.That(m_nextRequests.Select(r => (r.Release, r.Point)),
                Is.EqualTo(new[] { (false, first), (true, latest) }));
            Assert.That(m_nextRequests[1].Token.CanBeCanceled, Is.False);
        }

        [Test]
        public async Task EnumerateReferencesAsyncRejectsSixteenConsecutiveEmptyPages()
        {
            ByteString point = ByteString.From([3]);
            m_firstPage = Page(point);
            m_nextPage = (_, _) => Page(point);

            ServiceResultException exception = (await Assert.ThrowsAsync<ServiceResultException>(
                () => DrainAsync(Enumerate())))!;

            Assert.Multiple(() =>
            {
                Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadNoData));
                Assert.That(m_browseRequests, Has.Count.EqualTo(1));
                Assert.That(m_nextRequests.Count(r => !r.Release), Is.EqualTo(15));
                Assert.That(m_nextRequests.Count(r => r.Release), Is.EqualTo(1));
                Assert.That(m_nextRequests[m_nextRequests.Count - 1].Point, Is.EqualTo(point));
            });
        }

        [Test]
        public async Task EnumerateReferencesAsyncAllowsFifteenEmptyPagesAndResetsAfterProgressAsync()
        {
            ByteString point = ByteString.From([4]);
            var pages = new Queue<BrowseResult>();
            for (int index = 0; index < 15; index++)
            {
                pages.Enqueue(Page(point));
            }
            pages.Enqueue(Page(point, Reference("progress")));
            for (int index = 0; index < 15; index++)
            {
                pages.Enqueue(Page(point));
            }
            pages.Enqueue(Page(default, Reference("end")));
            m_firstPage = pages.Dequeue();
            m_nextPage = (_, _) => pages.Dequeue();

            List<ReferenceDescription> actual = await DrainAsync(Enumerate()).ConfigureAwait(false);

            Assert.That(actual.Select(r => r.NodeId),
                Is.EqualTo(new[] { new ExpandedNodeId("progress", 2), new ExpandedNodeId("end", 2) }));
            Assert.That(m_nextRequests, Has.Count.EqualTo(31));
            Assert.That(m_nextRequests.Any(r => r.Release), Is.False);
        }

        [Test]
        public async Task EnumerateReferencesAsyncReleasesContinuationAfterNextPageFailure()
        {
            ByteString point = ByteString.From([5]);
            var primary = new ServiceResultException(StatusCodes.BadCommunicationError);
            m_firstPage = Page(point, Reference("first"));
            m_nextPage = (_, _) => throw primary;

            ServiceResultException exception = (await Assert.ThrowsAsync<ServiceResultException>(
                () => DrainAsync(Enumerate())))!;

            Assert.That(exception, Is.SameAs(primary));
            Assert.That(m_nextRequests.Select(r => (r.Release, r.Point)),
                Is.EqualTo(new[] { (false, point), (true, point) }));
            Assert.That(m_nextRequests[1].Token.CanBeCanceled, Is.False);
        }

        [Test]
        public async Task EnumerateReferencesAsyncReleasesContinuationAfterConsumerFailure()
        {
            ByteString point = ByteString.From([6]);
            var primary = new IOException("Consumer failed.");
            m_firstPage = Page(point, Reference("first"));

            IOException exception = (await Assert.ThrowsAsync<IOException>(async () =>
            {
                await foreach (ReferenceDescription reference in Enumerate().ConfigureAwait(false))
                {
                    Assert.That(reference.NodeId, Is.EqualTo(new ExpandedNodeId("first", 2)));
                    throw primary;
                }
            }))!;

            Assert.That(exception, Is.SameAs(primary));
            Assert.That(m_nextRequests.Select(r => (r.Release, r.Point)),
                Is.EqualTo(new[] { (true, point) }));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task EnumerateReferencesAsyncCancellationReleasesWithUncancelledTokenAsync(bool duringNext)
        {
            ByteString point = ByteString.From([7]);
            using var cancellation = new CancellationTokenSource();
            m_firstPage = Page(point, Reference("first"));
            m_nextPage = (_, token) =>
            {
                Assert.That(token, Is.EqualTo(cancellation.Token));
                cancellation.Cancel();
                throw new OperationCanceledException(token);
            };

            IAsyncEnumerator<ReferenceDescription> iterator = Enumerate(cancellation.Token).GetAsyncEnumerator();
            await using (iterator.ConfigureAwait(false))
            {
                Assert.That(await iterator.MoveNextAsync().ConfigureAwait(false), Is.True);
                if (!duringNext)
                {
                    cancellation.Cancel();
                }
                OperationCanceledException exception = (await Assert.ThrowsAsync<OperationCanceledException>(
                    async () => await iterator.MoveNextAsync().ConfigureAwait(false)))!;
                Assert.That(exception.CancellationToken, Is.EqualTo(cancellation.Token));
            }

            Assert.That(m_nextRequests.Count(r => !r.Release), Is.EqualTo(duringNext ? 1 : 0));
            Assert.That(m_nextRequests.Where(r => r.Release).Select(r => (r.Point, r.Token)),
                Is.EqualTo(new[] { (point, CancellationToken.None) }));
        }

        [Test]
        public async Task EnumerateReferencesAsyncHonorsEnumeratorCancellationBeforeNextReferenceAsync()
        {
            ByteString point = ByteString.From([71]);
            using var cancellation = new CancellationTokenSource();
            m_firstPage = Page(point, Reference("first"), Reference("must-not-yield"));
            IAsyncEnumerator<ReferenceDescription> iterator = Enumerate().GetAsyncEnumerator(cancellation.Token);

            await using (iterator.ConfigureAwait(false))
            {
                Assert.That(await iterator.MoveNextAsync().ConfigureAwait(false), Is.True);
                Assert.That(iterator.Current.NodeId, Is.EqualTo(new ExpandedNodeId("first", 2)));
                cancellation.Cancel();

                OperationCanceledException exception = (await Assert.ThrowsAsync<OperationCanceledException>(
                    async () => await iterator.MoveNextAsync().ConfigureAwait(false)))!;
                Assert.That(exception.CancellationToken, Is.EqualTo(cancellation.Token));
            }

            Assert.That(m_browseRequests[0].Token, Is.EqualTo(cancellation.Token));
            Assert.That(m_nextRequests, Is.EqualTo(new[] { (true, point, CancellationToken.None) }));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task EnumerateReferencesAsyncDoesNotReleaseAnExhaustedFinalPageAsync(bool readToEnd)
        {
            ByteString point = ByteString.From([8]);
            m_firstPage = Page(point, Reference("first"));
            m_nextPage = (_, _) => Page(default, Reference("last"));

            IAsyncEnumerator<ReferenceDescription> iterator = Enumerate().GetAsyncEnumerator();
            await using (iterator.ConfigureAwait(false))
            {
                Assert.That(await iterator.MoveNextAsync().ConfigureAwait(false), Is.True);
                Assert.That(await iterator.MoveNextAsync().ConfigureAwait(false), Is.True);
                Assert.That(iterator.Current.NodeId, Is.EqualTo(new ExpandedNodeId("last", 2)));
                if (readToEnd)
                {
                    Assert.That(await iterator.MoveNextAsync().ConfigureAwait(false), Is.False);
                }
            }

            Assert.That(m_nextRequests.Select(r => (r.Release, r.Point)),
                Is.EqualTo(new[] { (false, point) }));
        }

        [TestCase(1u)]
        [TestCase(1000u)]
        public async Task EnumerateReferencesAsyncAcceptsPageSizeBoundariesAndEmptyResultAsync(uint size)
        {
            List<ReferenceDescription> result = await DrainAsync(m_session.Object.EnumerateReferencesAsync(
                new NodeId("empty", 2), ReferenceTypeIds.Organizes, pageSize: size)).ConfigureAwait(false);

            Assert.That(result, Is.Empty);
            Assert.That(m_browseRequests.Select(r => r.Size), Is.EqualTo(new[] { size }));
            Assert.That(m_nextRequests, Is.Empty);
        }

        [TestCase(0u)]
        [TestCase(1001u)]
        public void EnumerateReferencesAsyncRejectsInvalidPageSizeBeforeDispatch(uint size)
        {
            ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
                m_session.Object.EnumerateReferencesAsync(
                    new NodeId("root", 2), ReferenceTypeIds.Organizes, pageSize: size))!;

            Assert.That(exception.ParamName, Is.EqualTo("pageSize"));
            Assert.That(m_browseRequests, Is.Empty);
            Assert.That(m_nextRequests, Is.Empty);
        }

        [Test]
        public void EnumerateReferencesAsyncRejectsNullSession()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(() =>
                SessionBrowseExtensions.EnumerateReferencesAsync(null!, NodeId.Null, ReferenceTypeIds.Organizes))!;
            Assert.That(exception.ParamName, Is.EqualTo("session"));
        }

        private IAsyncEnumerable<ReferenceDescription> Enumerate(CancellationToken ct = default)
        {
            return m_session.Object.EnumerateReferencesAsync(new NodeId("root", 2), ReferenceTypeIds.Organizes, ct: ct);
        }

        private static async Task<List<ReferenceDescription>> DrainAsync(
            IAsyncEnumerable<ReferenceDescription> sequence)
        {
            var result = new List<ReferenceDescription>();
            await foreach (ReferenceDescription reference in sequence.ConfigureAwait(false))
            {
                result.Add(reference);
            }
            return result;
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

        private static ReferenceDescription Reference(string id)
        {
            return new ReferenceDescription { NodeId = new ExpandedNodeId(id, 2) };
        }

        private Mock<ISessionClient> m_session = null!;
        private BrowseResult m_firstPage = null!;
        private Func<ByteString, CancellationToken, BrowseResult> m_nextPage = null!;
        private readonly List<(uint Size, BrowseDescription Request, CancellationToken Token)> m_browseRequests = [];
        private readonly List<(bool Release, ByteString Point, CancellationToken Token)> m_nextRequests = [];
    }
}
