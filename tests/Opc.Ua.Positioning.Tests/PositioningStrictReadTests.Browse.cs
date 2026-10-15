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
using Opc.Ua.Client;
using Opc.Ua.Positioning.Client;

namespace Opc.Ua.Positioning.Tests
{
    public sealed partial class PositioningStrictReadTests
    {
        [Test]
        public async Task BrowseAtExactReferenceAndPageLimitsCompletesWithoutReleasingConsumedTokensAsync()
        {
            Mock<ISession> session = CreateSession();
            ByteString token = ByteString.From(1);
            SetupBrowse(session, () => new BrowseResult
            {
                References = [Reference("one")],
                ContinuationPoint = token
            });
            SetupNext(session, (release, point, _) =>
            {
                Assert.That(release, Is.False);
                Assert.That(point, Is.EqualTo(token));
                return new BrowseResult { References = [Reference("two")] };
            });

            List<PositioningObjectEntry> values = await CollectAsync(Relative(session, new PositioningClientReadOptions
            {
                MaximumBrowseReferences = 2,
                MaximumBrowsePages = 2
            }).EnumerateFramesAsync(s_frame)).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(values, Has.Count.EqualTo(2));
                Assert.That(values[0].BrowseName.Name, Is.EqualTo("one"));
                Assert.That(values[1].BrowseName.Name, Is.EqualTo("two"));
            });
            session.Verify(s => s.BrowseNextAsync(
                It.IsAny<RequestHeader?>(), true, It.IsAny<ArrayOf<ByteString>>(), It.IsAny<CancellationToken>()),
                Times.Never);
            VerifyBorrowed(session);
        }

        [Test]
        public async Task BrowseOneBeyondReferenceLimitFailsBeforeYieldingAndReleasesOnlyOwnedTokenAsync()
        {
            Mock<ISession> session = CreateSession();
            ByteString first = ByteString.From(2);
            ByteString second = ByteString.From(3);
            SetupBrowse(session, () => new BrowseResult
            {
                References = [Reference("one")],
                ContinuationPoint = first
            });
            SetupNext(session, (_, _, _) => new BrowseResult
            {
                References = [Reference("two"), Reference("three")],
                ContinuationPoint = second
            });
            var released = new List<ByteString>();
            SetupRelease(session, released);
            var emitted = new List<PositioningObjectEntry>();

            await AssertStatusAsync(async () =>
            {
                await foreach (PositioningObjectEntry entry in Relative(session, new PositioningClientReadOptions
                {
                    MaximumBrowseReferences = 2
                }).EnumerateFramesAsync(s_frame).ConfigureAwait(false))
                {
                    emitted.Add(entry);
                }
            }, StatusCodes.BadEncodingLimitsExceeded).ConfigureAwait(false);

            Assert.That(emitted, Is.Empty);
            Assert.That(released, Is.EqualTo(new[] { second }));
            VerifyBorrowed(session);
        }

        [Test]
        public async Task EmptyAndReusedContinuationPagesCountTowardsTheLimitAsync()
        {
            Mock<ISession> session = CreateSession();
            ByteString point = ByteString.From(4);
            SetupBrowse(session, () => new BrowseResult { ContinuationPoint = point });
            int nextCalls = 0;
            SetupNext(session, (release, token, cancellationToken) =>
            {
                Assert.That(token, Is.EqualTo(point));
                if (release)
                {
                    Assert.That(cancellationToken.CanBeCanceled, Is.True);
                    return new BrowseResult();
                }
                nextCalls++;
                return new BrowseResult { ContinuationPoint = point };
            });

            await AssertStatusAsync(async () => await BrowseAsync(session, new PositioningClientReadOptions
            {
                MaximumBrowsePages = 3
            }).ConfigureAwait(false), StatusCodes.BadEncodingLimitsExceeded).ConfigureAwait(false);

            Assert.That(nextCalls, Is.EqualTo(2));
            session.Verify(s => s.BrowseNextAsync(
                It.IsAny<RequestHeader?>(), true, It.IsAny<ArrayOf<ByteString>>(), It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Test]
        public async Task EmptyTerminalPageAtExactPageLimitCompletesAsync()
        {
            Mock<ISession> session = CreateSession();
            ByteString point = ByteString.From(5);
            SetupBrowse(session, () => new BrowseResult { ContinuationPoint = point });
            SetupNext(session, (release, _, _) =>
            {
                Assert.That(release, Is.False);
                return new BrowseResult();
            });
            ArrayOf<ReferenceDescription> result = await BrowseAsync(session, new PositioningClientReadOptions
            {
                MaximumBrowsePages = 2,
                MaximumBrowseReferences = 1
            }).ConfigureAwait(false);
            Assert.That(result.IsEmpty, Is.True);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task CancellationAfterAPageReleasesOnlyTheCurrentContinuationWithFreshTokenAsync(bool afterNext)
        {
            Mock<ISession> session = CreateSession();
            using var cancellation = new CancellationTokenSource();
            ByteString first = ByteString.From(6);
            ByteString second = ByteString.From(7);
            SetupBrowse(session, () =>
            {
                if (!afterNext)
                {
                    cancellation.Cancel();
                }
                return new BrowseResult { ContinuationPoint = first };
            });
            SetupNext(session, (release, point, _) =>
            {
                Assert.That(release, Is.False);
                Assert.That(point, Is.EqualTo(first));
                cancellation.Cancel();
                return new BrowseResult { ContinuationPoint = second };
            });
            var released = new List<ByteString>();
            SetupRelease(session, released);

            await Assert.ThatAsync(async () =>
                await Operations(session).BrowseAsync(
                    s_frame, ReferenceTypeIds.HasComponent, (uint)NodeClass.Variable, cancellation.Token)
                    .ConfigureAwait(false), Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);

            Assert.That(released, Is.EqualTo(new[] { afterNext ? second : first }));
            VerifyBorrowed(session);
        }

        [Test]
        public async Task CancellationWhileAwaitingNextReleasesTheSubmittedContinuationAsync()
        {
            Mock<ISession> session = CreateSession();
            using var cancellation = new CancellationTokenSource();
            ByteString point = ByteString.From(8);
            SetupBrowse(session, () => new BrowseResult { ContinuationPoint = point });
            session.Setup(s => s.BrowseNextAsync(
                    It.IsAny<RequestHeader?>(), false, It.IsAny<ArrayOf<ByteString>>(), It.IsAny<CancellationToken>()))
                .Returns((RequestHeader? _, bool _, ArrayOf<ByteString> _, CancellationToken token) =>
                {
                    cancellation.Cancel();
                    return new ValueTask<BrowseNextResponse>(Task.FromCanceled<BrowseNextResponse>(token));
                });
            var released = new List<ByteString>();
            SetupRelease(session, released);

            await Assert.ThatAsync(async () =>
                await Operations(session).BrowseAsync(
                    s_frame, ReferenceTypeIds.HasComponent, 0, cancellation.Token).ConfigureAwait(false),
                Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);

            Assert.That(released, Is.EqualTo(new[] { point }));
        }

        [Test]
        public async Task PrimaryAndCleanupFailuresAreBothRetainedAsync()
        {
            Mock<ISession> session = CreateSession();
            ByteString point = ByteString.From(9);
            SetupBrowse(session, () => new BrowseResult
            {
                References = [Reference(), Reference()],
                ContinuationPoint = point
            });
            SetupNext(session, (release, token, cancellationToken) =>
            {
                Assert.That(release, Is.True);
                Assert.That(token, Is.EqualTo(point));
                Assert.That(cancellationToken.CanBeCanceled, Is.True);
                throw new ServiceResultException(StatusCodes.BadTimeout);
            });
            AggregateException failure = await FailureAsync<AggregateException>(async () =>
                await BrowseAsync(session, new PositioningClientReadOptions { MaximumBrowseReferences = 1 })
                    .ConfigureAwait(false)).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(failure.InnerExceptions, Has.Count.EqualTo(2));
                Assert.That(failure.InnerExceptions[0], Is.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo(StatusCodes.BadEncodingLimitsExceeded));
                Assert.That(failure.InnerExceptions[1], Is.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode)).EqualTo(StatusCodes.BadTimeout));
            });
        }

        [Test]
        public async Task MalformedNextResponseReleasesOldAndReturnedTokensWithoutDuplicatesAsync()
        {
            Mock<ISession> session = CreateSession();
            ByteString first = ByteString.From(10);
            ByteString second = ByteString.From(11);
            ByteString third = ByteString.From(12);
            SetupBrowse(session, () => new BrowseResult { ContinuationPoint = first });
            session.Setup(s => s.BrowseNextAsync(
                    It.IsAny<RequestHeader?>(), false, It.IsAny<ArrayOf<ByteString>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new BrowseNextResponse
                {
                    ResponseHeader = new ResponseHeader(),
                    Results =
                    [
                        new BrowseResult { ContinuationPoint = second },
                        new BrowseResult { ContinuationPoint = second },
                        new BrowseResult { ContinuationPoint = third }
                    ]
                });
            var released = new List<ByteString>();
            SetupRelease(session, released);

            await AssertStatusAsync(async () => await BrowseAsync(session).ConfigureAwait(false),
                StatusCodes.BadDecodingError).ConfigureAwait(false);
            Assert.That(released, Is.EqualTo(new[] { first, second, third }));
        }

        [TestCaseSource(nameof(NonGoodStatuses))]
        public async Task NonGoodBrowseStatusIsPreservedAndReturnedContinuationIsReleasedAsync(StatusCode status)
        {
            Mock<ISession> session = CreateSession();
            ByteString point = ByteString.From(13);
            SetupBrowse(session, () => new BrowseResult { StatusCode = status, ContinuationPoint = point });
            var released = new List<ByteString>();
            SetupRelease(session, released);

            await AssertStatusAsync(async () => await BrowseAsync(session).ConfigureAwait(false), status)
                .ConfigureAwait(false);
            Assert.That(released, Is.EqualTo(new[] { point }));
        }

        [TestCase("header")]
        [TestCase("empty")]
        [TestCase("extra")]
        [TestCase("null")]
        [TestCase("diagnostics")]
        public async Task BrowseRejectsMalformedResponseShapesAsync(string shape)
        {
            Mock<ISession> session = CreateSession();
            var response = new BrowseResponse
            {
                ResponseHeader = new ResponseHeader
                {
                    ServiceResult = shape == "header" ? StatusCodes.BadDecodingError : StatusCodes.Good
                },
                Results = shape switch
                {
                    "empty" => [],
                    "extra" => [new BrowseResult(), new BrowseResult()],
                    "null" => [null!],
                    _ => [new BrowseResult()]
                },
                DiagnosticInfos = shape == "diagnostics" ? [new DiagnosticInfo(), new DiagnosticInfo()] : []
            };
            session.Setup(s => s.BrowseAsync(
                    It.IsAny<RequestHeader?>(), It.IsAny<ViewDescription?>(), It.IsAny<uint>(),
                    It.IsAny<ArrayOf<BrowseDescription>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(response);
            await AssertStatusAsync(async () => await BrowseAsync(session).ConfigureAwait(false),
                StatusCodes.BadDecodingError).ConfigureAwait(false);
        }

        [TestCaseSource(nameof(NonGoodStatuses))]
        public async Task BrowseHeaderFailureIsPreservedEvenWhenResultCountIsWrongAsync(StatusCode status)
        {
            Mock<ISession> session = CreateSession();
            session.Setup(s => s.BrowseAsync(
                    It.IsAny<RequestHeader?>(), It.IsAny<ViewDescription?>(), It.IsAny<uint>(),
                    It.IsAny<ArrayOf<BrowseDescription>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new BrowseResponse { ResponseHeader = new ResponseHeader { ServiceResult = status } });
            await AssertStatusAsync(async () => await BrowseAsync(session).ConfigureAwait(false), status)
                .ConfigureAwait(false);
        }

        [TestCase("header")]
        [TestCase("count")]
        [TestCase("data")]
        [TestCase("continuation")]
        public async Task InvalidCleanupResponsePreservesPrimaryFailureAsync(string shape)
        {
            Mock<ISession> session = CreateSession();
            SetupBrowse(session, () => new BrowseResult { ContinuationPoint = ByteString.From(14) });
            session.Setup(s => s.BrowseNextAsync(
                    It.IsAny<RequestHeader?>(), true, It.IsAny<ArrayOf<ByteString>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new BrowseNextResponse
                {
                    ResponseHeader = new ResponseHeader
                    {
                        ServiceResult = shape == "header" ? StatusCodes.BadDecodingError : StatusCodes.Good
                    },
                    Results = shape == "count" ? [] :
                    [
                        new BrowseResult
                        {
                            References = shape == "data" ? [Reference()] : [],
                            ContinuationPoint = shape == "continuation" ? ByteString.From(15) : default
                        }
                    ]
                });
            AggregateException failure = await FailureAsync<AggregateException>(async () =>
                await BrowseAsync(session, new PositioningClientReadOptions { MaximumBrowsePages = 1 })
                    .ConfigureAwait(false)).ConfigureAwait(false);
            Assert.That(failure.InnerExceptions[0], Is.TypeOf<ServiceResultException>()
                .With.Property(nameof(ServiceResultException.StatusCode))
                .EqualTo(StatusCodes.BadEncodingLimitsExceeded));
            Assert.That(failure.InnerExceptions[1], Is.TypeOf<ServiceResultException>()
                .With.Property(nameof(ServiceResultException.StatusCode)).EqualTo(StatusCodes.BadDecodingError));
        }

        [Test]
        public async Task AlreadyInvalidatedContinuationDoesNotMaskThePrimaryFailureAsync()
        {
            Mock<ISession> session = CreateSession();
            SetupBrowse(session, () => new BrowseResult { ContinuationPoint = ByteString.From(16) });
            SetupNext(session, (_, _, _) => new BrowseResult { StatusCode = StatusCodes.BadContinuationPointInvalid });
            await AssertStatusAsync(async () => await BrowseAsync(session, new PositioningClientReadOptions
            {
                MaximumBrowsePages = 1
            }).ConfigureAwait(false), StatusCodes.BadEncodingLimitsExceeded).ConfigureAwait(false);
        }

        [Test]
        public async Task CancelledBrowseDoesNotMakeARequestOrReleaseUnownedTokensAsync()
        {
            Mock<ISession> session = CreateSession();
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            await Assert.ThatAsync(async () => await Operations(session).BrowseAsync(
                    s_frame, ReferenceTypeIds.HasComponent, 0, cancellation.Token).ConfigureAwait(false),
                Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);
            session.Verify(s => s.BrowseAsync(
                It.IsAny<RequestHeader?>(), It.IsAny<ViewDescription?>(), It.IsAny<uint>(),
                It.IsAny<ArrayOf<BrowseDescription>>(), It.IsAny<CancellationToken>()), Times.Never);
            session.Verify(s => s.BrowseNextAsync(
                It.IsAny<RequestHeader?>(), It.IsAny<bool>(), It.IsAny<ArrayOf<ByteString>>(),
                It.IsAny<CancellationToken>()), Times.Never);
        }

        [Test]
        public async Task CancellationAndCleanupFailureBothRemainVisibleAsync()
        {
            Mock<ISession> session = CreateSession();
            using var cancellation = new CancellationTokenSource();
            ByteString point = ByteString.From(17);
            SetupBrowse(session, () =>
            {
                cancellation.Cancel();
                return new BrowseResult { ContinuationPoint = point };
            });
            SetupNext(session, (release, token, cleanup) =>
            {
                Assert.That(release, Is.True);
                Assert.That(token, Is.EqualTo(point));
                Assert.That(cleanup, Is.Not.EqualTo(cancellation.Token));
                Assert.That(cleanup.CanBeCanceled, Is.True);
                Assert.That(cleanup.IsCancellationRequested, Is.False);
                throw new ServiceResultException(StatusCodes.BadSessionClosed);
            });
            AggregateException failure = await FailureAsync<AggregateException>(async () =>
                await Operations(session).BrowseAsync(
                    s_frame, ReferenceTypeIds.HasComponent, 0, cancellation.Token).ConfigureAwait(false))
                .ConfigureAwait(false);
            Assert.That(failure.InnerExceptions[0], Is.InstanceOf<OperationCanceledException>());
            Assert.That(failure.InnerExceptions[1], Is.TypeOf<ServiceResultException>()
                .With.Property(nameof(ServiceResultException.StatusCode)).EqualTo(StatusCodes.BadSessionClosed));
        }

        [Test]
        public async Task EmptyFirstPageCompletesWithoutContinuationRequestsAsync()
        {
            Mock<ISession> session = CreateSession();
            SetupBrowse(session, () => new BrowseResult());
            ArrayOf<ReferenceDescription> result = await BrowseAsync(session, new PositioningClientReadOptions
            {
                MaximumBrowsePages = 1,
                MaximumBrowseReferences = 1
            }).ConfigureAwait(false);
            Assert.That(result.IsEmpty, Is.True);
            session.Verify(s => s.BrowseNextAsync(
                It.IsAny<RequestHeader?>(), It.IsAny<bool>(), It.IsAny<ArrayOf<ByteString>>(),
                It.IsAny<CancellationToken>()), Times.Never);
        }

        [TestCase("zone")]
        [TestCase("list")]
        [TestCase("object")]
        public async Task BothClientsDiscoverOnlyMatchingModelTypesWithStableNodeIdsAsync(string kind)
        {
            Mock<ISession> session = CreateSession();
            ReferenceDescription matching = Reference("matching");
            matching.NodeClass = NodeClass.Object;
            matching.TypeDefinition = kind switch
            {
                "zone" => Gpos.ObjectTypeIds.ZoneType,
                "list" => Rsl.ObjectTypeIds.SpatialObjectsListType,
                _ => Rsl.ObjectTypeIds.SpatialObjectType
            };
            SetupBrowse(session, () => new BrowseResult { References = [Reference("other"), matching] });
            List<PositioningObjectEntry> result = kind switch
            {
                "zone" => await CollectAsync(Global(session).EnumerateZonesAsync()).ConfigureAwait(false),
                "list" => await CollectAsync(Relative(session).EnumerateSpatialObjectListsAsync())
                    .ConfigureAwait(false),
                _ => await CollectAsync(Relative(session).EnumerateSpatialObjectsAsync(s_zone)).ConfigureAwait(false)
            };
            Assert.Multiple(() =>
            {
                Assert.That(result, Has.Count.EqualTo(1));
                Assert.That(result[0].NodeId, Is.EqualTo(new NodeId("matching", 3)));
                Assert.That(result[0].TypeDefinitionId, Is.EqualTo(
                    ExpandedNodeId.ToNodeId(matching.TypeDefinition, session.Object.NamespaceUris)));
                Assert.That(result[0].DisplayName.Text, Is.EqualTo("matching"));
            });
        }

        private static ValueTask<ArrayOf<ReferenceDescription>> BrowseAsync(
            Mock<ISession> session,
            PositioningClientReadOptions? options = null)
        {
            return Operations(session, options).BrowseAsync(
                s_frame, ReferenceTypeIds.HasComponent, (uint)NodeClass.Variable, CancellationToken.None);
        }
    }
}
