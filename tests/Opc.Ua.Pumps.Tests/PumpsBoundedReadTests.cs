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
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Pumps.Client;
using Opc.Ua.Tests;

namespace Opc.Ua.Pumps.Tests
{
    [TestFixture]
    public sealed class PumpsBoundedReadTests
    {
        [TestCase(-1)]
        [TestCase(0)]
        [TestCase(65537)]
        public void BoundedConstructorRejectsInvalidLimits(int limit)
        {
            (Mock<ISession> session, ITelemetryContext telemetry) = CreateSession();
            Assert.That(
                () => new PumpsClient(session.Object, telemetry, limit),
                Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        [Test]
        public async Task EmptyGroupNeedsNoBrowseAndPreservesAbsenceAsync()
        {
            (Mock<ISession> session, ITelemetryContext telemetry) = CreateSession();
            var client = new PumpsClient(session.Object, telemetry, 1);
            PumpValueSet values = await client.ReadValueSetAsync(NodeId.Null).ConfigureAwait(false);
            Assert.That(values, Is.SameAs(PumpValueSet.Empty));
            Assert.That(values.NodeId.IsNull, Is.True);
            session.VerifyGet(value => value.MessageContext, Times.Once);
            session.VerifyNoOtherCalls();
        }

        [TestCaseSource(nameof(s_nonGoodStatuses))]
        public async Task NonGoodBrowseCannotBecomeAnEmptyOptionalGroupAsync(StatusCode status)
        {
            (Mock<ISession> session, ITelemetryContext telemetry) = CreateSession();
            SetupBrowse(session, new BrowseResult { StatusCode = status });
            var client = new PumpsClient(session.Object, telemetry, 4);
            await Assert.ThatAsync(
                async () => await client.ReadValueSetAsync(new NodeId(1u), PumpReadOptions.ValuesOnly)
                    .ConfigureAwait(false),
                Throws.TypeOf<ServiceResultException>().With.Property("StatusCode").EqualTo(status))
                .ConfigureAwait(false);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task ReferenceLimitFailsWithoutReadingAndReleasesTheExactContinuationAsync(bool emptyReleaseResults)
        {
            (Mock<ISession> session, ITelemetryContext telemetry) = CreateSession();
            ByteString point = ByteString.From("owned-page"u8);
            SetupBrowse(
                session,
                new BrowseResult
                {
                    References = [Variable(1u), Variable(2u)],
                    ContinuationPoint = point
                });
            bool released = false;
            session.Setup(
                value => value.BrowseNextAsync(
                    It.IsAny<RequestHeader?>(),
                    true,
                    It.IsAny<ArrayOf<ByteString>>(),
                    It.IsAny<CancellationToken>()))
                .Returns(
                    (RequestHeader? _, bool _, ArrayOf<ByteString> points, CancellationToken token) =>
                    {
                        Assert.That(points, Has.Count.EqualTo(1));
                        Assert.That(points[0], Is.EqualTo(point));
                        Assert.That(token.CanBeCanceled, Is.True);
                        Assert.That(token.IsCancellationRequested, Is.False);
                        released = true;
                        return new ValueTask<BrowseNextResponse>(
                            new BrowseNextResponse
                            {
                                ResponseHeader = new ResponseHeader(),
                                Results = emptyReleaseResults ? [] : [new BrowseResult()]
                            });
                    });
            var client = new PumpsClient(session.Object, telemetry, 1);
            await Assert.ThatAsync(
                async () => await client.ReadValueSetAsync(new NodeId(3u), PumpReadOptions.ValuesOnly)
                    .ConfigureAwait(false),
                Throws.TypeOf<ServiceResultException>().With.Property("StatusCode")
                    .EqualTo(StatusCodes.BadEncodingLimitsExceeded)).ConfigureAwait(false);
            Assert.That(released, Is.True);
            session.Verify(
                value => value.ReadAsync(
                    It.IsAny<RequestHeader?>(),
                    It.IsAny<double>(),
                    It.IsAny<TimestampsToReturn>(),
                    It.IsAny<ArrayOf<ReadValueId>>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Test]
        public async Task ValuesAtTheLimitKeepQualityAndExactNodeIdentityAsync()
        {
            (Mock<ISession> session, ITelemetryContext telemetry) = CreateSession();
            SetupBrowse(session, new BrowseResult { References = [Variable(1u), Variable(2u)] });
            session.Setup(
                value => value.ReadAsync(
                    It.IsAny<RequestHeader?>(),
                    0,
                    TimestampsToReturn.Neither,
                    It.IsAny<ArrayOf<ReadValueId>>(),
                    It.IsAny<CancellationToken>()))
                .Returns(
                    (
                        RequestHeader? _,
                        double _,
                        TimestampsToReturn _,
                        ArrayOf<ReadValueId> reads,
                        CancellationToken _) =>
                    {
                        Assert.That(reads, Has.Count.EqualTo(2));
                        Assert.That(reads[0].NodeId, Is.EqualTo(new NodeId(1u)));
                        Assert.That(reads[1].NodeId, Is.EqualTo(new NodeId(2u)));
                        return new ValueTask<ReadResponse>(
                            new ReadResponse
                            {
                                ResponseHeader = new ResponseHeader(),
                                Results =
                                [
                                    new DataValue(Variant.From(12.5)),
                                    new DataValue(Variant.From(0.0), StatusCodes.BadOutOfService)
                                ]
                            });
                    });
            var client = new PumpsClient(session.Object, telemetry, 2);
            PumpValueSet values = await client.ReadValueSetAsync(
                new NodeId(3u),
                PumpReadOptions.ValuesOnly).ConfigureAwait(false);
            Assert.That(values, Has.Count.EqualTo(2));
            Assert.That(values["Value1"]!.Value, Is.EqualTo(Variant.From(12.5)));
            Assert.That(values["Value2"]!.StatusCode, Is.EqualTo(StatusCodes.BadOutOfService));
            Assert.That(values["Value2"]!.NodeId, Is.EqualTo(new NodeId(2u)));
        }

        [Test]
        public async Task DuplicateNamesCannotSilentlyOverwriteAValueAsync()
        {
            (Mock<ISession> session, ITelemetryContext telemetry) = CreateSession();
            ReferenceDescription duplicate = Variable(2u);
            duplicate.BrowseName = Variable(1u).BrowseName;
            SetupBrowse(session, new BrowseResult { References = [Variable(1u), duplicate] });
            var client = new PumpsClient(session.Object, telemetry, 2);
            await Assert.ThatAsync(
                async () => await client.ReadValueSetAsync(new NodeId(3u), PumpReadOptions.ValuesOnly)
                    .ConfigureAwait(false),
                Throws.TypeOf<ServiceResultException>().With.Property("StatusCode")
                    .EqualTo(StatusCodes.BadBrowseNameDuplicated)).ConfigureAwait(false);
        }

        [TestCase(0)]
        [TestCase(2)]
        public async Task IncompleteOrExtraReadResultsAreRejectedAsync(int count)
        {
            (Mock<ISession> session, ITelemetryContext telemetry) = CreateSession();
            SetupBrowse(session, new BrowseResult { References = [Variable(1u)] });
            session.Setup(
                value => value.ReadAsync(
                    It.IsAny<RequestHeader?>(),
                    It.IsAny<double>(),
                    It.IsAny<TimestampsToReturn>(),
                    It.IsAny<ArrayOf<ReadValueId>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(
                    new ReadResponse
                    {
                        ResponseHeader = new ResponseHeader(),
                        Results = new DataValue[count]
                    });
            var client = new PumpsClient(session.Object, telemetry, 2);
            await Assert.ThatAsync(
                async () => await client.ReadValueSetAsync(new NodeId(3u), PumpReadOptions.ValuesOnly)
                    .ConfigureAwait(false),
                Throws.TypeOf<ServiceResultException>().With.Property("StatusCode")
                    .EqualTo(StatusCodes.BadDecodingError)).ConfigureAwait(false);
        }

        [TestCaseSource(nameof(s_nonGoodStatuses))]
        public async Task FailedChildResolutionIsNotOptionalAbsenceAsync(StatusCode status)
        {
            (Mock<ISession> session, ITelemetryContext telemetry) = CreateSession();
            session.Setup(
                value => value.TranslateBrowsePathsToNodeIdsAsync(
                    It.IsAny<RequestHeader?>(),
                    It.IsAny<ArrayOf<BrowsePath>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(
                    new TranslateBrowsePathsToNodeIdsResponse
                    {
                        ResponseHeader = new ResponseHeader(),
                        Results = [new BrowsePathResult { StatusCode = status }]
                    });
            var client = new PumpsClient(session.Object, telemetry, 2);
            await Assert.ThatAsync(
                async () => await client.ResolvePumpsChildAsync(new NodeId(3u), BrowseNames.Measurements)
                    .ConfigureAwait(false),
                Throws.TypeOf<ServiceResultException>().With.Property("StatusCode").EqualTo(status))
                .ConfigureAwait(false);
        }

        [TestCase("wrong-type")]
        [TestCase("denied")]
        public async Task InvalidEngineeringMetadataCannotBecomeMissingUnitsAsync(string failure)
        {
            (Mock<ISession> session, ITelemetryContext telemetry) = CreateSession();
            SetupBrowse(session, new BrowseResult { References = [Variable(1u)] });
            session.Setup(
                value => value.TranslateBrowsePathsToNodeIdsAsync(
                    It.IsAny<RequestHeader?>(),
                    It.IsAny<ArrayOf<BrowsePath>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(
                    new TranslateBrowsePathsToNodeIdsResponse
                    {
                        ResponseHeader = new ResponseHeader(),
                        Results =
                        [
                            new BrowsePathResult
                            {
                                Targets = [new BrowsePathTarget
                                {
                                    TargetId = new ExpandedNodeId(99u),
                                    RemainingPathIndex = uint.MaxValue
                                }]
                            }
                        ]
                    });
            session.Setup(
                value => value.ReadAsync(
                    It.IsAny<RequestHeader?>(),
                    It.IsAny<double>(),
                    It.IsAny<TimestampsToReturn>(),
                    It.IsAny<ArrayOf<ReadValueId>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(
                    new ReadResponse
                    {
                        ResponseHeader = new ResponseHeader(),
                        Results =
                        [
                            new DataValue(Variant.From(12.5)),
                            new DataValue(
                                Variant.From(123u),
                                failure == "denied" ? StatusCodes.BadUserAccessDenied : StatusCodes.Good)
                        ]
                    });
            var client = new PumpsClient(session.Object, telemetry, 2);
            await Assert.ThatAsync(
                async () => await client.ReadValueSetAsync(
                    new NodeId(3u),
                    new PumpReadOptions { IncludeRanges = false }).ConfigureAwait(false),
                Throws.TypeOf<ServiceResultException>().With.Property("StatusCode").EqualTo(
                    failure == "denied" ? StatusCodes.BadUserAccessDenied : StatusCodes.BadTypeMismatch))
                .ConfigureAwait(false);
        }

        [TestCase("ambiguous")]
        [TestCase("incomplete")]
        [TestCase("remote")]
        public async Task ChildResolutionRequiresOneCompleteLocalTargetAsync(string kind)
        {
            (Mock<ISession> session, ITelemetryContext telemetry) = CreateSession();
            var target = new BrowsePathTarget
            {
                TargetId = kind == "remote"
                    ? new ExpandedNodeId(99u, 0, null, 1)
                    : new ExpandedNodeId(99u),
                RemainingPathIndex = kind == "incomplete" ? 0 : uint.MaxValue
            };
            session.Setup(
                value => value.TranslateBrowsePathsToNodeIdsAsync(
                    It.IsAny<RequestHeader?>(),
                    It.IsAny<ArrayOf<BrowsePath>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(
                    new TranslateBrowsePathsToNodeIdsResponse
                    {
                        ResponseHeader = new ResponseHeader(),
                        Results = [new BrowsePathResult
                        {
                            Targets = kind == "ambiguous" ? [target, target] : [target]
                        }]
                    });
            var client = new PumpsClient(session.Object, telemetry, 2);
            await Assert.ThatAsync(
                async () => await client.ResolvePumpsChildAsync(
                    new NodeId(3u),
                    BrowseNames.Measurements).ConfigureAwait(false),
                Throws.TypeOf<ServiceResultException>().With.Property("StatusCode")
                    .EqualTo(StatusCodes.BadDecodingError)).ConfigureAwait(false);
        }

        [Test]
        public async Task EmptyContinuationPagesStopAt128AndReleaseOwnershipAsync()
        {
            (Mock<ISession> session, ITelemetryContext telemetry) = CreateSession();
            ByteString point = ByteString.From("empty-pages"u8);
            SetupBrowse(session, new BrowseResult { ContinuationPoint = point });
            int nextCalls = 0;
            int releases = 0;
            session.Setup(
                value => value.BrowseNextAsync(
                    It.IsAny<RequestHeader?>(),
                    It.IsAny<bool>(),
                    It.IsAny<ArrayOf<ByteString>>(),
                    It.IsAny<CancellationToken>()))
                .Returns(
                    (RequestHeader? _, bool release, ArrayOf<ByteString> points, CancellationToken _) =>
                    {
                        Assert.That(points, Has.Count.EqualTo(1));
                        Assert.That(points[0], Is.EqualTo(point));
                        if (release)
                        {
                            releases++;
                        }
                        else
                        {
                            nextCalls++;
                        }
                        return new ValueTask<BrowseNextResponse>(
                            new BrowseNextResponse
                            {
                                ResponseHeader = new ResponseHeader(),
                                Results = [new BrowseResult { ContinuationPoint = release ? default : point }]
                            });
                    });
            var client = new PumpsClient(session.Object, telemetry, 2);
            await Assert.ThatAsync(
                async () => await client.ReadValueSetAsync(new NodeId(3u))
                    .ConfigureAwait(false),
                Throws.TypeOf<ServiceResultException>().With.Property("StatusCode")
                    .EqualTo(StatusCodes.BadEncodingLimitsExceeded)).ConfigureAwait(false);
            Assert.That(nextCalls, Is.EqualTo(127));
            Assert.That(releases, Is.EqualTo(1));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task CancellationAfterAcquiringAContinuationStillReleasesItAsync(bool emptyReleaseResults)
        {
            (Mock<ISession> session, ITelemetryContext telemetry) = CreateSession();
            using var cancellation = new CancellationTokenSource();
            ByteString point = ByteString.From("canceled-page"u8);
            session.Setup(
                value => value.BrowseAsync(
                    It.IsAny<RequestHeader?>(),
                    It.IsAny<ViewDescription?>(),
                    It.IsAny<uint>(),
                    It.IsAny<ArrayOf<BrowseDescription>>(),
                    It.IsAny<CancellationToken>()))
                .Returns(
                    async (
                        RequestHeader? _,
                        ViewDescription? _,
                        uint _,
                        ArrayOf<BrowseDescription> _,
                        CancellationToken _) =>
                    {
                        await cancellation.CancelAsync().ConfigureAwait(false);
                        return new BrowseResponse
                        {
                            ResponseHeader = new ResponseHeader(),
                            Results = [new BrowseResult { ContinuationPoint = point }]
                        };
                    });
            bool released = false;
            session.Setup(
                value => value.BrowseNextAsync(
                    It.IsAny<RequestHeader?>(),
                    true,
                    It.IsAny<ArrayOf<ByteString>>(),
                    It.IsAny<CancellationToken>()))
                .Returns(
                    (RequestHeader? _, bool _, ArrayOf<ByteString> points, CancellationToken token) =>
                    {
                        Assert.That(points[0], Is.EqualTo(point));
                        Assert.That(token.IsCancellationRequested, Is.False);
                        released = true;
                        return new ValueTask<BrowseNextResponse>(
                            new BrowseNextResponse
                            {
                                ResponseHeader = new ResponseHeader(),
                                Results = emptyReleaseResults ? [] : [new BrowseResult()]
                            });
                    });
            var client = new PumpsClient(session.Object, telemetry, 2);
            await Assert.ThatAsync(
                async () => await client.ReadValueSetAsync(
                    new NodeId(3u),
                    cancellationToken: cancellation.Token).ConfigureAwait(false),
                Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);
            Assert.That(released, Is.True);
        }

        /// <summary>
        /// Successful empty-release handling must not hide an invalid service header.
        /// </summary>
        [Test]
        public async Task EmptyReleaseStillRequiresAGoodHeaderAsync()
        {
            (Mock<ISession> session, ITelemetryContext telemetry) = CreateSession();
            SetupBrowse(
                session,
                new BrowseResult
                {
                    References = [Variable(1u), Variable(2u)],
                    ContinuationPoint = ByteString.From("owned"u8)
                });
            session.Setup(
                item => item.BrowseNextAsync(
                    It.IsAny<RequestHeader?>(),
                    true,
                    It.IsAny<ArrayOf<ByteString>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(
                    new BrowseNextResponse
                    {
                        ResponseHeader = new ResponseHeader { ServiceResult = StatusCodes.BadTimeout },
                        Results = []
                    });
            var client = new PumpsClient(session.Object, telemetry, 1);

            AggregateException? failure = null;
            try
            {
                await client.ReadValueSetAsync(new NodeId(3u), PumpReadOptions.ValuesOnly).ConfigureAwait(false);
            }
            catch (AggregateException error)
            {
                failure = error;
            }
            Assert.That(failure, Is.Not.Null);
            Assert.That(failure!.InnerExceptions, Has.Count.EqualTo(2));
            Assert.That(
                failure.InnerExceptions[0],
                Is.TypeOf<ServiceResultException>().With.Property("StatusCode")
                    .EqualTo(StatusCodes.BadEncodingLimitsExceeded));
            Assert.That(
                failure.InnerExceptions[1],
                Is.TypeOf<ServiceResultException>().With.Property("StatusCode")
                    .EqualTo(StatusCodes.BadTimeout));
        }

        [Test]
        public async Task BrowseAndCleanupFailuresRemainSeparatelyObservableAsync()
        {
            (Mock<ISession> session, ITelemetryContext telemetry) = CreateSession();
            ByteString point = ByteString.From("owned"u8);
            SetupBrowse(
                session,
                new BrowseResult
                {
                    References = [Variable(1u), Variable(2u)],
                    ContinuationPoint = point
                });
            var cleanup = new InvalidOperationException("The release failed.");
            session.Setup(
                value => value.BrowseNextAsync(
                    It.IsAny<RequestHeader?>(),
                    true,
                    It.IsAny<ArrayOf<ByteString>>(),
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(cleanup);
            var client = new PumpsClient(session.Object, telemetry, 1);
            AggregateException? observed = null;
            try
            {
                await client.ReadValueSetAsync(new NodeId(3u), PumpReadOptions.ValuesOnly).ConfigureAwait(false);
            }
            catch (AggregateException exception)
            {
                observed = exception;
            }
            Assert.That(observed, Is.Not.Null);
            Assert.That(observed!.InnerExceptions, Has.Count.EqualTo(2));
            Assert.That(
                observed.InnerExceptions[0],
                Is.TypeOf<ServiceResultException>().With.Property("StatusCode")
                    .EqualTo(StatusCodes.BadEncodingLimitsExceeded));
            Assert.That(observed.InnerExceptions[1], Is.SameAs(cleanup));
        }

        private static (Mock<ISession> Session, ITelemetryContext Telemetry) CreateSession()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            ServiceMessageContext context = ServiceMessageContext.Create(telemetry);
            context.NamespaceUris.GetIndexOrAppend(Namespaces.Pumps);
            var session = new Mock<ISession>(MockBehavior.Strict);
            session.SetupGet(value => value.MessageContext).Returns(context).Verifiable();
            session.SetupGet(value => value.NamespaceUris).Returns(context.NamespaceUris).Verifiable();
            return (session, telemetry);
        }

        private static void SetupBrowse(Mock<ISession> session, BrowseResult result)
        {
            session.Setup(
                value => value.BrowseAsync(
                    It.IsAny<RequestHeader?>(),
                    It.IsAny<ViewDescription?>(),
                    It.IsAny<uint>(),
                    It.IsAny<ArrayOf<BrowseDescription>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new BrowseResponse { ResponseHeader = new ResponseHeader(), Results = [result] });
        }

        private static ReferenceDescription Variable(uint id)
        {
            return new ReferenceDescription
            {
                NodeId = new ExpandedNodeId(id),
                NodeClass = NodeClass.Variable,
                BrowseName = new QualifiedName("Value" + id)
            };
        }

        private static readonly StatusCode[] s_nonGoodStatuses =
            [StatusCodes.BadUserAccessDenied, StatusCodes.Uncertain];
    }
}
