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

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;

namespace Opc.Ua.Server.Tests.NodeManager
{
    [TestFixture]
    [Category("NodeManager")]
    public sealed class BrowseContinuationOwnershipRegressionTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public async Task PermissionDeniedReleasesTheClaimedContinuationAsync(bool release)
        {
            using var harness = new BrowseHarness();
            var resource = new Mock<IDisposable>();
            ContinuationPoint point = harness.AddPoint(resource);
            harness.Metadata.RolePermissions =
            [
                new RolePermissionType { RoleId = ObjectIds.WellKnownRole_Anonymous, Permissions = 0 }
            ];

            (ArrayOf<BrowseResult> results, _) = await harness.Master.BrowseNextAsync(
                harness.Context, release, [Token(point)]).ConfigureAwait(false);
            Assert.That(results[0].StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
            Assert.That(results[0].ContinuationPoint.IsEmpty, Is.True);
            Assert.That(harness.Points.RestoreBrowse(Token(point)), Is.Null);
            resource.Verify(value => value.Dispose(), Times.Once);
        }

        [Test]
        public void MetadataFailureReleasesTheClaimedContinuation()
        {
            using var harness = new BrowseHarness();
            var resource = new Mock<IDisposable>();
            ContinuationPoint point = harness.AddPoint(resource);
            harness.Manager.Setup(value => value.GetNodeMetadataAsync(
                    It.IsAny<OperationContext>(), It.IsAny<object>(),
                    It.IsAny<BrowseResultMask>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("metadata failure"));

            InvalidOperationException error = Assert.ThrowsAsync<InvalidOperationException>(async () =>
            {
                await harness.Master.BrowseNextAsync(harness.Context, false, [Token(point)]).ConfigureAwait(false);
            });
            Assert.That(error.Message, Is.EqualTo("metadata failure"));
            resource.Verify(value => value.Dispose(), Times.Once);
        }

        [Test]
        public async Task BrowseFailureReleasesTheClaimedContinuationAsync()
        {
            using var harness = new BrowseHarness();
            var resource = new Mock<IDisposable>();
            ContinuationPoint point = harness.AddPoint(resource);
            harness.Manager.Setup(value => value.BrowseAsync(
                    It.IsAny<OperationContext>(), It.IsAny<ContinuationPoint>(),
                    It.IsAny<IList<ReferenceDescription>>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("browse failure"));

            (ArrayOf<BrowseResult> results, _) = await harness.Master.BrowseNextAsync(
                harness.Context, false, [Token(point)]).ConfigureAwait(false);
            Assert.That(results[0].StatusCode, Is.EqualTo(StatusCodes.BadUnexpectedError));
            Assert.That(results[0].ContinuationPoint.IsEmpty, Is.True);
            resource.Verify(value => value.Dispose(), Times.Once);
        }

        [Test]
        public async Task FilteringFailureReleasesTheReplacementContinuationAsync()
        {
            using var harness = new BrowseHarness();
            var originalResource = new Mock<IDisposable>();
            var replacementResource = new Mock<IDisposable>();
            ContinuationPoint point = harness.AddPoint(originalResource);
            ContinuationPoint replacement = harness.NewPoint(replacementResource);
            harness.Manager.SetupSequence(value => value.GetNodeMetadataAsync(
                    It.IsAny<OperationContext>(), It.IsAny<object>(),
                    It.IsAny<BrowseResultMask>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(harness.Metadata)
                .ThrowsAsync(new InvalidOperationException("reference metadata failure"));
            harness.Manager.Setup(value => value.BrowseAsync(
                    It.IsAny<OperationContext>(), It.IsAny<ContinuationPoint>(),
                    It.IsAny<IList<ReferenceDescription>>(), It.IsAny<CancellationToken>()))
                .Returns((OperationContext _, ContinuationPoint current, IList<ReferenceDescription> references,
                    CancellationToken _) =>
                {
                    current.Dispose();
                    references.Add(new ReferenceDescription { NodeId = harness.Metadata.NodeId, Unfiltered = true });
                    return new ValueTask<ContinuationPoint>(replacement);
                });

            (ArrayOf<BrowseResult> results, _) = await harness.Master.BrowseNextAsync(
                harness.Context, false, [Token(point)]).ConfigureAwait(false);
            Assert.That(results[0].StatusCode, Is.EqualTo(StatusCodes.BadUnexpectedError));
            originalResource.Verify(value => value.Dispose(), Times.Once);
            replacementResource.Verify(value => value.Dispose(), Times.Once);
        }

        [Test]
        public async Task RetainedContinuationStaysOwnedUntilItIsReleasedAsync()
        {
            using var harness = new BrowseHarness();
            var resource = new Mock<IDisposable>();
            ContinuationPoint point = harness.AddPoint(resource);
            harness.ReturnAnotherPage();
            (ArrayOf<BrowseResult> results, _) = await harness.Master.BrowseNextAsync(
                harness.Context, false, [Token(point)]).ConfigureAwait(false);
            Assert.That(results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(results[0].References, Has.Count.EqualTo(1));
            Assert.That(results[0].ContinuationPoint.IsEmpty, Is.False);
            resource.Verify(value => value.Dispose(), Times.Never);

            (ArrayOf<BrowseResult> released, _) = await harness.Master.BrowseNextAsync(
                harness.Context, true, [results[0].ContinuationPoint]).ConfigureAwait(false);
            Assert.That(released[0].StatusCode, Is.EqualTo(StatusCodes.Good));
            resource.Verify(value => value.Dispose(), Times.Once);
        }

        [Test]
        public async Task CompletedBrowseDoesNotDisposeItsConsumedPointTwiceAsync()
        {
            using var harness = new BrowseHarness();
            var resource = new Mock<IDisposable>();
            ContinuationPoint point = harness.AddPoint(resource);
            harness.Manager.Setup(value => value.BrowseAsync(
                    It.IsAny<OperationContext>(), It.IsAny<ContinuationPoint>(),
                    It.IsAny<IList<ReferenceDescription>>(), It.IsAny<CancellationToken>()))
                .Returns((OperationContext _, ContinuationPoint current, IList<ReferenceDescription> _,
                    CancellationToken _) =>
                {
                    current.Dispose();
                    return new ValueTask<ContinuationPoint>((ContinuationPoint)null);
                });

            (ArrayOf<BrowseResult> results, _) = await harness.Master.BrowseNextAsync(
                harness.Context, false, [Token(point)]).ConfigureAwait(false);
            Assert.That(results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(results[0].ContinuationPoint.IsEmpty, Is.True);
            resource.Verify(value => value.Dispose(), Times.Once);
        }

        [Test]
        public async Task ContinuationBudgetRetainsOnlyTheAdmittedPageAsync()
        {
            using var harness = new BrowseHarness(maxPerBrowse: 1);
            var firstResource = new Mock<IDisposable>();
            var secondResource = new Mock<IDisposable>();
            ContinuationPoint first = harness.AddPoint(firstResource);
            ContinuationPoint second = harness.AddPoint(secondResource);
            harness.ReturnAnotherPage();

            (ArrayOf<BrowseResult> results, _) = await harness.Master.BrowseNextAsync(
                harness.Context, false, [Token(first), Token(second)]).ConfigureAwait(false);
            Assert.That(results, Has.Count.EqualTo(2));
            Assert.That(results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(results[0].ContinuationPoint.IsEmpty, Is.False);
            Assert.That(results[1].StatusCode, Is.EqualTo(StatusCodes.BadNoContinuationPoints));
            Assert.That(results[1].ContinuationPoint.IsEmpty, Is.True);
            firstResource.Verify(value => value.Dispose(), Times.Never);
            secondResource.Verify(value => value.Dispose(), Times.Once);

            await harness.Master.BrowseNextAsync(
                harness.Context, true, [results[0].ContinuationPoint]).ConfigureAwait(false);
            firstResource.Verify(value => value.Dispose(), Times.Once);
        }

        [Test]
        public void CancellationReleasesCurrentAndEarlierUnreturnedPages()
        {
            using var harness = new BrowseHarness();
            using var cancellation = new CancellationTokenSource();
            var firstResource = new Mock<IDisposable>();
            var secondResource = new Mock<IDisposable>();
            ContinuationPoint first = harness.AddPoint(firstResource);
            ContinuationPoint second = harness.AddPoint(secondResource);
            harness.Manager.Setup(value => value.BrowseAsync(
                    It.IsAny<OperationContext>(), It.IsAny<ContinuationPoint>(),
                    It.IsAny<IList<ReferenceDescription>>(), It.IsAny<CancellationToken>()))
                .Returns((OperationContext _, ContinuationPoint current, IList<ReferenceDescription> references,
                    CancellationToken token) =>
                {
                    if (ReferenceEquals(current, second))
                    {
                        cancellation.Cancel();
                        return new ValueTask<ContinuationPoint>(Task.FromCanceled<ContinuationPoint>(token));
                    }
                    references.Add(new ReferenceDescription { NodeId = harness.Metadata.NodeId });
                    return new ValueTask<ContinuationPoint>(current);
                });

            Assert.CatchAsync<OperationCanceledException>(async () =>
            {
                await harness.Master.BrowseNextAsync(
                    harness.Context, false, [Token(first), Token(second)], cancellation.Token).ConfigureAwait(false);
            });
            firstResource.Verify(value => value.Dispose(), Times.Once);
            secondResource.Verify(value => value.Dispose(), Times.Once);
            Assert.That(harness.Points.RestoreBrowse(Token(first)), Is.Null);
        }

        [Test]
        public async Task CloseDuringBrowseRejectsResavingAndDisposesTheClaimedPointOnceAsync()
        {
            using var harness = new BrowseHarness();
            var resource = new Mock<IDisposable>();
            ContinuationPoint point = harness.AddPoint(resource);
            harness.Manager.Setup(value => value.BrowseAsync(
                    It.IsAny<OperationContext>(), It.IsAny<ContinuationPoint>(),
                    It.IsAny<IList<ReferenceDescription>>(), It.IsAny<CancellationToken>()))
                .Returns((OperationContext _, ContinuationPoint current, IList<ReferenceDescription> references,
                    CancellationToken _) =>
                {
                    harness.Points.Clear();
                    references.Add(new ReferenceDescription { NodeId = harness.Metadata.NodeId });
                    return new ValueTask<ContinuationPoint>(current);
                });
            (ArrayOf<BrowseResult> results, _) = await harness.Master.BrowseNextAsync(
                harness.Context, false, [Token(point)]).ConfigureAwait(false);
            Assert.That(results[0].StatusCode, Is.EqualTo(StatusCodes.BadSessionClosed));
            Assert.That(results[0].ContinuationPoint.IsEmpty, Is.True);
            Assert.That(harness.Points.RestoreBrowse(Token(point)), Is.Null);
            resource.Verify(value => value.Dispose(), Times.Once);
        }

        private static ByteString Token(ContinuationPoint point)
        {
            return point.Id.ToByteArray().ToByteString();
        }

        private sealed class BrowseHarness : IDisposable
        {
            public BrowseHarness(int maxPerBrowse = 10)
            {
                Mock<IServerInternal> server = DeterministicServerMock.Create(out m_queues);
                var factory = new Mock<IMainNodeManagerFactory>();
                factory.Setup(value => value.CreateConfigurationNodeManager())
                    .Returns(new Mock<IConfigurationNodeManager>().Object);
                factory.Setup(value => value.CreateCoreNodeManager(It.IsAny<ushort>()))
                    .Returns(new Mock<ICoreNodeManager>().Object);
                server.SetupGet(value => value.MainNodeManagerFactory).Returns(factory.Object);
                var handle = new NodeHandle { NodeId = new NodeId(1, 1) };
                Metadata = new NodeMetadata(handle, handle.NodeId) { NodeClass = NodeClass.Object };
                Manager.SetupGet(value => value.NamespaceUris).Returns([DeterministicServerMock.TestNamespaceUri]);
                Manager.Setup(value => value.GetNodeMetadataAsync(
                        It.IsAny<OperationContext>(), It.IsAny<object>(),
                        It.IsAny<BrowseResultMask>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(Metadata);
                Manager.Setup(value => value.GetManagerHandleAsync(
                        It.IsAny<NodeId>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(handle);
                Master = new MasterNodeManager(
                    server.Object,
                    new ApplicationConfiguration
                    {
                        ServerConfiguration = new ServerConfiguration { MaxBrowseContinuationPoints = maxPerBrowse }
                    },
                    null,
                    Manager.Object);
                Points = new SessionContinuationPoints(() => new NodeId(99, 1), 10, 10, null);
                var session = new Mock<ISession>();
                session.SetupGet(value => value.EffectiveIdentity).Returns(new UserIdentity());
                session.SetupGet(value => value.ContinuationPoints).Returns(Points);
                Context = new OperationContext(
                    new RequestHeader(), null, RequestType.BrowseNext, RequestLifetime.None, session.Object);
            }

            public Mock<IAsyncNodeManager> Manager { get; } = new();
            public MasterNodeManager Master { get; }
            public NodeMetadata Metadata { get; }
            public SessionContinuationPoints Points { get; }
            public OperationContext Context { get; }

            public ContinuationPoint AddPoint(Mock<IDisposable> resource)
            {
                ContinuationPoint point = NewPoint(resource);
                Points.SaveBrowse(point);
                return point;
            }

            public ContinuationPoint NewPoint(Mock<IDisposable> resource)
            {
                return new ContinuationPoint
                {
                    Id = Guid.NewGuid(),
                    Manager = Manager.Object,
                    NodeToBrowse = Metadata.Handle,
                    RequestedNodeId = Metadata.NodeId,
                    MaxResultsToReturn = 1,
                    ResultMask = BrowseResultMask.All,
                    Data = resource.Object
                };
            }

            public void ReturnAnotherPage()
            {
                Manager.Setup(value => value.BrowseAsync(
                        It.IsAny<OperationContext>(), It.IsAny<ContinuationPoint>(),
                        It.IsAny<IList<ReferenceDescription>>(), It.IsAny<CancellationToken>()))
                    .Returns((OperationContext _, ContinuationPoint current, IList<ReferenceDescription> references,
                        CancellationToken _) =>
                    {
                        references.Add(new ReferenceDescription { NodeId = Metadata.NodeId });
                        return new ValueTask<ContinuationPoint>(current);
                    });
            }

            public void Dispose()
            {
                Points.Clear();
                Context.Dispose();
                Master.Dispose();
                m_queues.Dispose();
            }

            private readonly MonitoredItemQueueFactory m_queues;
        }
    }
}
