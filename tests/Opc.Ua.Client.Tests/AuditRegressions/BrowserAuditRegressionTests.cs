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

// CA2000: test code; many disposables are ownership-transferred to test fixtures or short-lived,
// making CA2000 noisy without a real leak risk. Disabled file-level for the suite.
#pragma warning disable CA2000
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client.TestFramework;

namespace Opc.Ua.Client.Tests.AuditRegressions
{
    /// <summary>
    /// Regressions for the browse and node cache defects reported by the
    /// Opc.Ua.Client audit. Each test names the defect it pins down.
    /// </summary>
    [TestFixture]
    [Category("Client")]
    [Category("AuditRegression")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public sealed class BrowserAuditRegressionTests
    {
        /// <summary>
        /// The managed browse reset its batch offset on every retry pass, so on
        /// pass 2+ it wrote the retried node's status into the slot of whatever
        /// node happened to sit at that offset in the original list. Here node
        /// A fails once with BadNoContinuationPoints and succeeds on the retry;
        /// the second pass browses only A but its offset 0 pointed at A anyway
        /// - so the case that breaks is C retried into A's slot.
        /// </summary>
        [Test]
        public async Task ManagedBrowseRetryWritesErrorsIntoTheOriginalSlotAsync()
        {
            using SessionMock session = SessionMock.Create();

            NodeId nodeA = new("A", 2);
            NodeId nodeB = new("B", 2);
            NodeId nodeC = new("C", 2);

            // Pass 1: A and B good, C needs a retry.
            // Pass 2: only C is browsed, and it fails terminally.
            session.Channel
                .SetupSequence(c => c.SendRequestAsync(
                    It.IsAny<BrowseRequest>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<IServiceResponse>(new BrowseResponse
                {
                    Results =
                    [
                        GoodResult("targetA"),
                        GoodResult("targetB"),
                        BadResult(StatusCodes.BadNoContinuationPoints)
                    ],
                    DiagnosticInfos = []
                }))
                .Returns(new ValueTask<IServiceResponse>(new BrowseResponse
                {
                    Results = [BadResult(StatusCodes.BadUserAccessDenied)],
                    DiagnosticInfos = []
                }));

            var browser = new Browser(session);
            ResultSet<ArrayOf<ReferenceDescription>> results = await browser
                .BrowseAsync(new[] { nodeA, nodeB, nodeC }.ToArrayOf())
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(
                    ServiceResult.IsGood(results.Errors[0]),
                    Is.True,
                    "node A succeeded; the retry status of node C must not land here");
                Assert.That(ServiceResult.IsGood(results.Errors[1]), Is.True);
                Assert.That(
                    results.Errors[2].StatusCode,
                    Is.EqualTo((StatusCode)StatusCodes.BadUserAccessDenied),
                    "node C must carry its own retry status");
            });
        }

        /// <summary>
        /// The managed browse re-queued every BadNoContinuationPoints answer
        /// without any bound, so a server that keeps returning it kept the
        /// call running forever.
        /// </summary>
        [Test]
        public async Task ManagedBrowseGivesUpOnEndlessContinuationPointErrorsAsync()
        {
            using SessionMock session = SessionMock.Create();

            session.Channel
                .Setup(c => c.SendRequestAsync(
                    It.IsAny<BrowseRequest>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<IServiceResponse>(new BrowseResponse
                {
                    Results = [BadResult(StatusCodes.BadNoContinuationPoints)],
                    DiagnosticInfos = []
                }));

            var browser = new Browser(session);

            // The token only matters when the cap is missing: it stops the
            // runaway loop so the test host is not left spinning.
            using var abort = new CancellationTokenSource();
            Task<ResultSet<ArrayOf<ReferenceDescription>>> browse = browser
                .BrowseAsync(new[] { new NodeId("A", 2) }.ToArrayOf(), abort.Token)
                .AsTask();

            Task completed = await Task
                .WhenAny(browse, Task.Delay(System.TimeSpan.FromSeconds(30)))
                .ConfigureAwait(false);
            if (!ReferenceEquals(completed, browse))
            {
                abort.Cancel();
                Assert.Fail("an unbounded retry loop never returns");
            }

            ResultSet<ArrayOf<ReferenceDescription>> results = await browse
                .ConfigureAwait(false);
            Assert.That(
                results.Errors[0].StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.BadNoContinuationPoints));
        }

        /// <summary>
        /// The give-up guard above was a fixed pass count, which also cut short
        /// a large browse against a conforming server: with a small
        /// continuation point quota only a few nodes complete per pass, so a
        /// browse of many nodes legitimately needs more passes than the cap.
        /// Here one node completes per pass and every node must still finish.
        /// </summary>
        [Test]
        public async Task ManagedBrowseKeepsGoingWhileNodesCompleteAsync()
        {
            using SessionMock session = SessionMock.Create();

            // The server answers the first node of every request and has no
            // continuation point left for the rest.
            session.Channel
                .Setup(c => c.SendRequestAsync(
                    It.IsAny<BrowseRequest>(),
                    It.IsAny<CancellationToken>()))
                .Returns((IServiceRequest request, CancellationToken _) =>
                {
                    var browse = (BrowseRequest)request;
                    var results = new List<BrowseResult>(browse.NodesToBrowse.Count);
                    for (int i = 0; i < browse.NodesToBrowse.Count; i++)
                    {
                        results.Add(i == 0
                            ? GoodResult("target")
                            : BadResult(StatusCodes.BadNoContinuationPoints));
                    }
                    return new ValueTask<IServiceResponse>(new BrowseResponse
                    {
                        Results = results.ToArrayOf(),
                        DiagnosticInfos = []
                    });
                });

            const int nodeCount = 40;
            var nodes = new List<NodeId>(nodeCount);
            for (int i = 0; i < nodeCount; i++)
            {
                nodes.Add(new NodeId("N" + i, 2));
            }

            var browser = new Browser(session);
            ResultSet<ArrayOf<ReferenceDescription>> results = await browser
                .BrowseAsync(nodes.ToArrayOf())
                .ConfigureAwait(false);

            for (int i = 0; i < nodeCount; i++)
            {
                Assert.That(
                    ServiceResult.IsGood(results.Errors[i]),
                    Is.True,
                    $"node {i} completed on a later pass and must not be reported " +
                    "as failed by a pass-count cap");
            }
        }

        /// <summary>
        /// A browse that competes with another browse on the same session for
        /// a small continuation point quota completes nothing for several
        /// passes in a row while the other one holds the quota. Giving up
        /// after a couple of such passes returned a truncated reference list,
        /// so the no-progress bound has to ride out a long starvation run.
        /// </summary>
        [Test]
        public async Task ManagedBrowseRidesOutAStarvationRunAsync()
        {
            using SessionMock session = SessionMock.Create();

            // Ten passes during which the competing browse holds the quota,
            // then the point is free and the node completes.
            const int starvedPasses = 10;
            int passes = 0;

            session.Channel
                .Setup(c => c.SendRequestAsync(
                    It.IsAny<BrowseRequest>(),
                    It.IsAny<CancellationToken>()))
                .Returns((IServiceRequest _, CancellationToken _) =>
                {
                    BrowseResult result = Interlocked.Increment(ref passes) <= starvedPasses
                        ? BadResult(StatusCodes.BadNoContinuationPoints)
                        : GoodResult("target");
                    return new ValueTask<IServiceResponse>(new BrowseResponse
                    {
                        Results = [result],
                        DiagnosticInfos = []
                    });
                });

            var browser = new Browser(session);
            ResultSet<ArrayOf<ReferenceDescription>> results = await browser
                .BrowseAsync(new[] { new NodeId("A", 2) }.ToArrayOf())
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(
                    ServiceResult.IsGood(results.Errors[0]),
                    Is.True,
                    "the browse must keep retrying while another browse holds the quota");
                Assert.That(results.Results[0].Count, Is.EqualTo(1));
            });
        }

        /// <summary>
        /// FetchReferencesAsync(NodeId) returned the empty result set of a
        /// failed browse, so the node cache stored "this node has no
        /// references" for a node the server merely refused to browse.
        /// </summary>
        [Test]
        public void FetchReferencesSurfacesTheBrowseError()
        {
            using SessionMock session = SessionMock.Create();

            session.Channel
                .Setup(c => c.SendRequestAsync(
                    It.IsAny<BrowseRequest>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<IServiceResponse>(new BrowseResponse
                {
                    Results = [BadResult(StatusCodes.BadUserAccessDenied)],
                    DiagnosticInfos = []
                }));

            var context = new NodeCacheContext(session);

            ServiceResultException ex = Assert.ThrowsAsync<ServiceResultException>(
                async () => await context
                    .FetchReferencesAsync(null, new NodeId("Denied", 2))
                    .ConfigureAwait(false));

            Assert.That(
                ex.StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.BadUserAccessDenied),
                "caching 'no references' for a denied browse answers later " +
                "type queries wrongly for the whole cache lifetime");
        }

        private static BrowseResult GoodResult(string targetName)
        {
            return new BrowseResult
            {
                StatusCode = StatusCodes.Good,
                ContinuationPoint = default,
                References =
                [
                    new ReferenceDescription
                    {
                        NodeId = new ExpandedNodeId(targetName, 2),
                        BrowseName = new QualifiedName(targetName, 2),
                        ReferenceTypeId = ReferenceTypeIds.HasComponent,
                        IsForward = true
                    }
                ]
            };
        }

        private static BrowseResult BadResult(StatusCode statusCode)
        {
            return new BrowseResult
            {
                StatusCode = statusCode,
                ContinuationPoint = default,
                References = new List<ReferenceDescription>().ToArrayOf()
            };
        }
    }
}
