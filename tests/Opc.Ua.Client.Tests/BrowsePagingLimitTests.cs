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

// CA2000: test code; the sessions are disposed by the tests.
#pragma warning disable CA2000
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Time.Testing;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client.Roles;
using Opc.Ua.Client.TestFramework;

namespace Opc.Ua.Client.Tests
{
    /// <summary>
    /// The limits on the references, pages and time a browse spends on
    /// following continuation points.
    /// </summary>
    [TestFixture]
    [Category("Client")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public sealed class BrowsePagingLimitTests
    {
        /// <summary>
        /// Pages far beyond the defaults stop the test instead of letting a
        /// browse without limits run on.
        /// </summary>
        private const int kSafetyPages = 1000;

        /// <summary>
        /// References per page in the default limit tests: 100 pages reach
        /// the default reference limit.
        /// </summary>
        private const int kLargePage = 10_000;

        [Test]
        public void DefaultsAreFiniteAndNotPersisted()
        {
            var options = new BrowserOptions();
            Assert.Multiple(() =>
            {
                Assert.That(options.MaxReferencesPerNode, Is.EqualTo(BrowserOptions.DefaultMaxReferencesPerNode));
                Assert.That(options.MaxPagesPerNode, Is.EqualTo(BrowserOptions.DefaultMaxPagesPerNode));
                Assert.That(options.MaxBrowseDuration, Is.EqualTo(BrowserOptions.DefaultMaxBrowseDuration));
                Assert.That(BrowserOptions.DefaultMaxReferencesPerNode, Is.GreaterThan(0u));
                Assert.That(BrowserOptions.DefaultMaxPagesPerNode, Is.GreaterThan(0u));
                Assert.That(BrowserOptions.DefaultMaxBrowseDuration, Is.GreaterThan(TimeSpan.Zero));
            });
        }

        [Test]
        public async Task ManagedBrowseStopsAtDefaultReferenceLimitAsync()
        {
            using SessionMock session = SessionMock.Create();
            using var abort = new CancellationTokenSource();
            var server = new PagingServer(session, abort, kLargePage, endlessPrefix: 1);

            var browser = new Browser(session);
            ResultSet<ArrayOf<ReferenceDescription>> results = await browser
                .BrowseAsync(new[] { new NodeId("A", 2) }.ToArrayOf(), abort.Token)
                .ConfigureAwait(false);

            Assert.That(results.Errors[0].StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.BadEncodingLimitsExceeded));
            Assert.That(server.Pages,
                Is.EqualTo((int)(BrowserOptions.DefaultMaxReferencesPerNode / kLargePage)));
            Assert.That(server.Released, Is.EqualTo(1), "the last continuation point is released");
        }

        [Test]
        public async Task ManagedBrowseCompletesOtherNodesWhenOneReachesTheLimitAsync()
        {
            using SessionMock session = SessionMock.Create();
            using var abort = new CancellationTokenSource();
            var server = new PagingServer(session, abort, 3, endlessPrefix: 1, finitePages: 4);

            var browser = new Browser(session, new BrowserOptions { MaxPagesPerNode = 20 });
            ResultSet<ArrayOf<ReferenceDescription>> results = await browser
                .BrowseAsync(new[] { new NodeId("A", 2), new NodeId("B", 2) }.ToArrayOf(), abort.Token)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(results.Errors[0].StatusCode,
                    Is.EqualTo((StatusCode)StatusCodes.BadEncodingLimitsExceeded));
                Assert.That(ServiceResult.IsGood(results.Errors[1]), Is.True);
                Assert.That(results.Results[1].Count, Is.EqualTo(4 * 3), "all pages of node B");
                Assert.That(server.Released, Is.EqualTo(1));
            });
        }

        [Test]
        public async Task ManagedBrowseStopsAtConfiguredPageLimitAsync()
        {
            using SessionMock session = SessionMock.Create();
            using var abort = new CancellationTokenSource();
            var server = new PagingServer(session, abort, 1, endlessPrefix: 1);

            var browser = new Browser(session, new BrowserOptions { MaxPagesPerNode = 5 });
            ResultSet<ArrayOf<ReferenceDescription>> results = await browser
                .BrowseAsync(new[] { new NodeId("A", 2) }.ToArrayOf(), abort.Token)
                .ConfigureAwait(false);

            Assert.That(results.Errors[0].StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.BadEncodingLimitsExceeded));
            Assert.That(server.Pages, Is.EqualTo(5));
            Assert.That(server.Released, Is.EqualTo(1));
        }

        [Test]
        public async Task ManagedBrowseStopsAtMaxBrowseDurationAsync()
        {
            using SessionMock session = SessionMock.Create();
            using var abort = new CancellationTokenSource();
            var time = new FakeTimeProvider();
            var server = new PagingServer(session, abort, 1, endlessPrefix: 1)
            {
                OnPage = () => time.Advance(TimeSpan.FromMinutes(1))
            };

            var browser = new Browser(session) { TimeProvider = time };
            ResultSet<ArrayOf<ReferenceDescription>> results = await browser
                .BrowseAsync(new[] { new NodeId("A", 2) }.ToArrayOf(), abort.Token)
                .ConfigureAwait(false);

            Assert.That(results.Errors[0].StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadTimeout));
            Assert.That(server.Pages, Is.EqualTo(11), "ten minutes of one-minute pages, then one more");
            Assert.That(server.Released, Is.EqualTo(1));
        }

        [Test]
        public async Task ManagedBrowseWithoutLimitsFollowsAllPagesAsync()
        {
            using SessionMock session = SessionMock.Create();
            using var abort = new CancellationTokenSource();
            var server = new PagingServer(session, abort, 2, finitePrefix: 1, finitePages: 50);

            var browser = new Browser(session, new BrowserOptions
            {
                MaxPagesPerNode = 0,
                MaxReferencesPerNode = 0,
                MaxBrowseDuration = TimeSpan.Zero
            });
            ResultSet<ArrayOf<ReferenceDescription>> results = await browser
                .BrowseAsync(new[] { new NodeId("A", 2) }.ToArrayOf(), abort.Token)
                .ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(results.Errors[0]), Is.True);
            Assert.That(results.Results[0].Count, Is.EqualTo(100));
            Assert.That(server.Released, Is.Zero);
        }

        [Test]
        public async Task NodeBrowseStopsAtDefaultReferenceLimitAsync()
        {
            using SessionMock session = SessionMock.Create();
            using var abort = new CancellationTokenSource();
            var server = new PagingServer(session, abort, kLargePage, endlessPrefix: 1);

            var browser = new Browser(session) { ContinueUntilDone = true };
            ServiceResultException ex = await Assert.ThrowsAsync<ServiceResultException>(
                async () => await browser.BrowseAsync(new NodeId("A", 2), abort.Token).ConfigureAwait(false));

            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
            Assert.That(server.Pages,
                Is.EqualTo((int)(BrowserOptions.DefaultMaxReferencesPerNode / kLargePage)));
            Assert.That(server.Released, Is.EqualTo(1));
        }

        [Test]
        public async Task NodeBrowseStopsAtMaxBrowseDurationAsync()
        {
            using SessionMock session = SessionMock.Create();
            using var abort = new CancellationTokenSource();
            var time = new FakeTimeProvider();
            var server = new PagingServer(session, abort, 1, endlessPrefix: 1)
            {
                OnPage = () => time.Advance(TimeSpan.FromMinutes(1))
            };

            var browser = new Browser(session, new BrowserOptions { MaxBrowseDuration = TimeSpan.FromMinutes(3) })
            {
                TimeProvider = time
            };
            ServiceResultException ex = await Assert.ThrowsAsync<ServiceResultException>(
                async () => await browser.BrowseAsync(new NodeId("A", 2), abort.Token).ConfigureAwait(false));

            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadTimeout));
            Assert.That(server.Pages, Is.EqualTo(4));
            Assert.That(server.Released, Is.EqualTo(1));
        }

        [Test]
        public async Task NodeBrowseReturnsAllPagesInOrderAsync()
        {
            using SessionMock session = SessionMock.Create();
            using var abort = new CancellationTokenSource();
            _ = new PagingServer(session, abort, 3, finitePrefix: 1, finitePages: 5);

            var browser = new Browser(session);
            ArrayOf<ReferenceDescription> references = await browser
                .BrowseAsync(new NodeId("A", 2), abort.Token)
                .ConfigureAwait(false);

            Assert.That(references.Count, Is.EqualTo(15));
            for (int ii = 0; ii < references.Count; ii++)
            {
                Assert.That(references[ii].BrowseName.Name, Is.EqualTo("r" + ii));
            }
        }

        [Test]
        public async Task NodeBrowsePassesCollectedReferencesToMoreReferencesAsync()
        {
            using SessionMock session = SessionMock.Create();
            using var abort = new CancellationTokenSource();
            var server = new PagingServer(session, abort, 2, finitePrefix: 1, finitePages: 5);

            var browser = new Browser(session);
            var seen = new List<int>();
            browser.MoreReferences += (_, e) =>
            {
                seen.Add(e.References.Count);
                e.Cancel = seen.Count == 3;
            };
            ArrayOf<ReferenceDescription> references = await browser
                .BrowseAsync(new NodeId("A", 2), abort.Token)
                .ConfigureAwait(false);

            Assert.That(string.Join(",", seen), Is.EqualTo("2,4,6"));
            Assert.That(references.Count, Is.EqualTo(6));
            Assert.That(server.Released, Is.EqualTo(1));
        }

        [Test]
        public async Task ListRolesStopsAtDefaultReferenceLimitAsync()
        {
            using var abort = new CancellationTokenSource();
            Mock<ISession> session = CreateRoleSession(abort, kLargePage, out Func<int> pages, out Func<int> released);
            var client = new RoleManagementClient(session.Object);

            ServiceResultException ex = await Assert.ThrowsAsync<ServiceResultException>(
                async () => await client.ListRolesAsync(abort.Token).ConfigureAwait(false));

            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
            Assert.That(pages(), Is.EqualTo((int)(BrowserOptions.DefaultMaxReferencesPerNode / kLargePage)));
            Assert.That(released(), Is.EqualTo(1));
        }

        [Test]
        public async Task ListRolesStopsAtPageLimitAsync()
        {
            using var abort = new CancellationTokenSource();
            Mock<ISession> session = CreateRoleSession(abort, 1, out Func<int> pages, out Func<int> released);
            var client = new RoleManagementClient(session.Object)
            {
                StartBrowseLimits = () => new BrowsePagingLimits(0, 7, TimeSpan.Zero, null)
            };

            ServiceResultException ex = await Assert.ThrowsAsync<ServiceResultException>(
                async () => await client.ListRolesAsync(abort.Token).ConfigureAwait(false));

            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
            Assert.That(pages(), Is.EqualTo(7));
            Assert.That(released(), Is.EqualTo(1));
        }

        [Test]
        public void PageCounterAcceptsTheLastPageBeyondTheLimit()
        {
            var limits = new BrowsePagingLimits(10, 2, TimeSpan.Zero, null);
            var counter = new BrowsePageCounter();
            Assert.That(counter.Record(limits, 5, true), Is.Null);
            Assert.That(counter.Record(limits, 50, false), Is.Null,
                "a page without continuation point completes the node");
        }

        private static Mock<ISession> CreateRoleSession(
            CancellationTokenSource abort,
            int referencesPerPage,
            out Func<int> pages,
            out Func<int> released)
        {
            ArrayOf<ReferenceDescription> page = CreatePage(referencesPerPage);
            int pageCount = 0;
            int releaseCount = 0;
            var session = new Mock<ISession>();
            session.SetupGet(s => s.NamespaceUris).Returns(new NamespaceTable());
            session.SetupGet(s => s.MessageContext)
                .Returns(ServiceMessageContext.Create(Opc.Ua.Tests.NUnitTelemetryContext.Create()));
            session.Setup(s => s.BrowseAsync(
                    null, null, 0, It.IsAny<ArrayOf<BrowseDescription>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(() =>
                {
                    Interlocked.Increment(ref pageCount);
                    return new BrowseResponse
                    {
                        Results = [new BrowseResult { References = page, ContinuationPoint = NextPoint(1, 0) }]
                    };
                });
            session.Setup(s => s.BrowseNextAsync(
                    null, false, It.IsAny<ArrayOf<ByteString>>(), It.IsAny<CancellationToken>()))
                .Returns((RequestHeader? _, bool _, ArrayOf<ByteString> _, CancellationToken ct) =>
                {
                    ct.ThrowIfCancellationRequested();
                    int p = Interlocked.Increment(ref pageCount);
                    if (p >= kSafetyPages)
                    {
                        abort.Cancel();
                    }
                    return new ValueTask<BrowseNextResponse>(new BrowseNextResponse
                    {
                        Results = [new BrowseResult { References = page, ContinuationPoint = NextPoint(1, p) }]
                    });
                });
            session.Setup(s => s.BrowseNextAsync(
                    null, true, It.IsAny<ArrayOf<ByteString>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(() =>
                {
                    Interlocked.Increment(ref releaseCount);
                    return new BrowseNextResponse { Results = [new BrowseResult()] };
                });
            pages = () => Volatile.Read(ref pageCount);
            released = () => Volatile.Read(ref releaseCount);
            return session;
        }

        private static ArrayOf<ReferenceDescription> CreatePage(int count, int offset = 0)
        {
            var references = new ReferenceDescription[count];
            for (int ii = 0; ii < count; ii++)
            {
                references[ii] = new ReferenceDescription
                {
                    NodeId = new ExpandedNodeId((uint)(offset + ii), 2),
                    BrowseName = new QualifiedName("r" + (offset + ii), 2),
                    ReferenceTypeId = ReferenceTypeIds.HasComponent,
                    IsForward = true
                };
            }
            return references.ToArrayOf();
        }

        private static ByteString NextPoint(byte node, int page)
        {
            return ByteString.From(new byte[] { node, (byte)page, (byte)(page >> 8), (byte)(page >> 16) });
        }

        /// <summary>
        /// Serves Browse and BrowseNext for nodes whose continuation points
        /// start with a node prefix. Nodes browsed with
        /// <c>endlessPrefix</c> never run out of pages; the others end after
        /// <c>finitePages</c> pages.
        /// </summary>
        private sealed class PagingServer
        {
            public PagingServer(
                SessionMock session,
                CancellationTokenSource abort,
                int referencesPerPage,
                byte endlessPrefix = 0,
                byte finitePrefix = 2,
                int finitePages = 0)
            {
                m_abort = abort;
                m_referencesPerPage = referencesPerPage;
                m_sharedPage = CreatePage(referencesPerPage);
                m_endlessPrefix = endlessPrefix;
                m_finitePages = finitePages;
                m_finitePrefix = finitePrefix;

                session.Channel
                    .Setup(c => c.SendRequestAsync(It.IsAny<BrowseRequest>(), It.IsAny<CancellationToken>()))
                    .Returns((IServiceRequest request, CancellationToken _) =>
                    {
                        var browse = (BrowseRequest)request;
                        var results = new List<BrowseResult>();
                        for (int ii = 0; ii < browse.NodesToBrowse.Count; ii++)
                        {
                            // The first node is the endless one when there is one.
                            byte prefix = ii == 0 && m_endlessPrefix != 0 ? m_endlessPrefix : m_finitePrefix;
                            results.Add(Serve(prefix, 0));
                        }
                        return new ValueTask<IServiceResponse>(new BrowseResponse
                        {
                            Results = results.ToArrayOf(),
                            DiagnosticInfos = []
                        });
                    });
                session.Channel
                    .Setup(c => c.SendRequestAsync(It.IsAny<BrowseNextRequest>(), It.IsAny<CancellationToken>()))
                    .Returns((IServiceRequest request, CancellationToken ct) =>
                    {
                        ct.ThrowIfCancellationRequested();
                        var next = (BrowseNextRequest)request;
                        var results = new List<BrowseResult>();
                        foreach (ByteString point in next.ContinuationPoints)
                        {
                            if (next.ReleaseContinuationPoints)
                            {
                                Interlocked.Increment(ref m_released);
                                results.Add(new BrowseResult { StatusCode = StatusCodes.Good });
                                continue;
                            }
                            byte[] bytes = point.ToArray();
                            int page = bytes[1] | (bytes[2] << 8) | (bytes[3] << 16);
                            results.Add(Serve(bytes[0], page + 1));
                        }
                        return new ValueTask<IServiceResponse>(new BrowseNextResponse
                        {
                            Results = results.ToArrayOf(),
                            DiagnosticInfos = []
                        });
                    });
            }

            /// <summary>
            /// Pages served for the endless node, or for the finite node when
            /// there is no endless one.
            /// </summary>
            public int Pages => Volatile.Read(ref m_pages);

            /// <summary>
            /// Continuation points released by the client.
            /// </summary>
            public int Released => Volatile.Read(ref m_released);

            /// <summary>
            /// Called for every page served.
            /// </summary>
            public Action? OnPage { get; set; }

            private BrowseResult Serve(byte prefix, int page)
            {
                OnPage?.Invoke();
                bool endless = prefix == m_endlessPrefix;
                if (endless || m_endlessPrefix == 0)
                {
                    if (Interlocked.Increment(ref m_pages) >= kSafetyPages)
                    {
                        m_abort.Cancel();
                    }
                }
                bool last = !endless && page + 1 >= m_finitePages;
                return new BrowseResult
                {
                    StatusCode = StatusCodes.Good,
                    References = endless
                        ? m_sharedPage
                        : CreatePage(m_referencesPerPage, page * m_referencesPerPage),
                    ContinuationPoint = last ? default : NextPoint(prefix, page)
                };
            }

            private readonly CancellationTokenSource m_abort;
            private readonly int m_referencesPerPage;
            private readonly ArrayOf<ReferenceDescription> m_sharedPage;
            private readonly byte m_endlessPrefix;
            private readonly byte m_finitePrefix;
            private readonly int m_finitePages;
            private int m_pages;
            private int m_released;
        }
    }
}
