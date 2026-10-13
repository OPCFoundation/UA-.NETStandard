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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using Opc.Ua.Client.Historian;
using Opc.Ua.Client.Tests.SessionContexts;
using Opc.Ua.Tests;

namespace Opc.Ua.Client.Tests.Historian
{
    /// <summary>
    /// Verifies retained history cursor ownership and failure-preserving cleanup.
    /// </summary>
    [TestFixture]
    [Parallelizable(ParallelScope.All)]
    public sealed class HistoryClientGenerationTests
    {
        /// <summary>
        /// A normal cursor reuses the original token and consumes the server's point exactly once.
        /// </summary>
        [Test]
        public async Task PagingUsesOriginalContextAcrossMoveNextCalls()
        {
            await using var fixture = new SessionRequestContextTestContext();
            fixture.Handler = (request, _) =>
            {
                var history = (HistoryReadRequest)request;
                return new ValueTask<IServiceResponse>(history.NodesToRead[0].ContinuationPoint.IsEmpty
                    ? Page(1, s_point) : Page(2));
            };
            await using SessionRequestContext context =
                await SessionRequestContext.CaptureAsync(fixture.ManagedSession).ConfigureAwait(false);
            var client = new HistoryClient(context.Session, new HistoryClientOptions { BindSessionGeneration = true });
            await using IAsyncEnumerator<DataValue> cursor = Read(client).GetAsyncEnumerator();
            Assert.That(await cursor.MoveNextAsync().ConfigureAwait(false), Is.True);
            Assert.That(cursor.Current.WrappedValue.TryGetValue(out int first), Is.True);
            Assert.That(first, Is.EqualTo(1));
            Assert.That(await cursor.MoveNextAsync().ConfigureAwait(false), Is.True);
            Assert.That(cursor.Current.WrappedValue.TryGetValue(out int second), Is.True);
            Assert.That(second, Is.EqualTo(2));
            Assert.That(await cursor.MoveNextAsync().ConfigureAwait(false), Is.False);
            Assert.That(fixture.Requests, Has.Count.EqualTo(2));
            Assert.That(((HistoryReadRequest)fixture.Requests[1]).NodesToRead[0].ContinuationPoint,
                Is.EqualTo(s_point));
            Assert.That(fixture.Requests.All(request =>
                request.RequestHeader.AuthenticationToken == NodeId.Parse("s=original-token")), Is.True);
        }

        /// <summary>
        /// Replacement between pages prevents both the next read and old-point cleanup from reaching the server.
        /// </summary>
        [Test]
        public async Task OptInPagingNeverSendsOldPointToReplacement()
        {
            await using var fixture = new SessionRequestContextTestContext();
            fixture.Handler = (_, _) => new ValueTask<IServiceResponse>(Page(1, s_point));
            var client = new HistoryClient(fixture.ManagedSession,
                new HistoryClientOptions { BindSessionGeneration = true });
            await using IAsyncEnumerator<DataValue> cursor = Read(client).GetAsyncEnumerator();
            Assert.That(await cursor.MoveNextAsync().ConfigureAwait(false), Is.True);
            fixture.Recreate();
            ServiceResultException failure = Assert.ThrowsAsync<ServiceResultException>(
                async () => await cursor.MoveNextAsync().ConfigureAwait(false))!;
            Assert.That(failure.StatusCode, Is.EqualTo(StatusCodes.BadSessionIdInvalid));
            Assert.That(fixture.Requests, Has.Count.EqualTo(1));
            Assert.That(fixture.ManagedSession.SessionId, Is.EqualTo(NodeId.Parse("s=replacement-session")));
        }

        /// <summary>
        /// Early disposal still releases the original point after cancellation of the read token.
        /// </summary>
        [Test]
        public async Task EarlyDisposeReleasesOriginalPointWithoutCancelledToken()
        {
            await using var fixture = new SessionRequestContextTestContext();
            using var cancellation = new CancellationTokenSource();
            CancellationToken releaseToken = cancellation.Token;
            fixture.Handler = (request, token) =>
            {
                var history = (HistoryReadRequest)request;
                if (history.ReleaseContinuationPoints)
                {
                    releaseToken = token;
                }
                return new ValueTask<IServiceResponse>(history.ReleaseContinuationPoints ? Page(0) : Page(1, s_point));
            };
            var client = new HistoryClient(fixture.ManagedSession,
                new HistoryClientOptions { BindSessionGeneration = true });
            IAsyncEnumerator<DataValue> cursor = Read(client, cancellation.Token).GetAsyncEnumerator();
            try
            {
                Assert.That(await cursor.MoveNextAsync().ConfigureAwait(false), Is.True);
                await cancellation.CancelAsync().ConfigureAwait(false);
            }
            finally
            {
                await cursor.DisposeAsync().ConfigureAwait(false);
            }
            Assert.That(fixture.Requests, Has.Count.EqualTo(2));
            var release = (HistoryReadRequest)fixture.Requests[1];
            Assert.That(release.ReleaseContinuationPoints, Is.True);
            Assert.That(release.NodesToRead[0].ContinuationPoint, Is.EqualTo(s_point));
            Assert.That(release.NodesToRead[0].NodeId, Is.EqualTo(new NodeId(10u, 1)));
            Assert.That(release.RequestHeader.AuthenticationToken, Is.EqualTo(NodeId.Parse("s=original-token")));
            Assert.That(releaseToken, Is.EqualTo(CancellationToken.None));
            Assert.That(fixture.InnerSession.Connected, Is.True);
        }

        /// <summary>
        /// A failed release is logged while the original page failure remains the thrown exception.
        /// </summary>
        [Test]
        public async Task CleanupFailureIsLoggedWithoutMaskingPrimaryReadFailure()
        {
            using var logs = new RecordingLoggerProvider();
            ITelemetryContext telemetry = DefaultTelemetry.Create(
                builder => builder.SetMinimumLevel(LogLevel.Trace).AddProvider(logs));
            await using var fixture = new SessionRequestContextTestContext(telemetry);
            var primary = new ServiceResultException(StatusCodes.BadHistoryOperationUnsupported);
            var cleanup = new InvalidOperationException("scripted cleanup failure");
            fixture.Handler = (request, _) =>
            {
                var history = (HistoryReadRequest)request;
                if (history.ReleaseContinuationPoints)
                {
                    throw cleanup;
                }
                if (!history.NodesToRead[0].ContinuationPoint.IsEmpty)
                {
                    throw primary;
                }
                return new ValueTask<IServiceResponse>(Page(1, s_point));
            };
            var client = new HistoryClient(fixture.ManagedSession,
                new HistoryClientOptions { BindSessionGeneration = true });
            await using IAsyncEnumerator<DataValue> cursor = Read(client).GetAsyncEnumerator();
            Assert.That(await cursor.MoveNextAsync().ConfigureAwait(false), Is.True);
            ServiceResultException failure = Assert.ThrowsAsync<ServiceResultException>(
                async () => await cursor.MoveNextAsync().ConfigureAwait(false))!;
            Assert.That(failure, Is.SameAs(primary));
            RecordedLogRecord record = logs.Records.Single(value => value.EventId.Id == ClientEventIds.HistoryClient);
            Assert.That(record.Exception, Is.SameAs(cleanup));
            Assert.That(record.LogLevel, Is.EqualTo(LogLevel.Warning));
            Assert.That(record.Message, Does.Not.Contain("retained-point"));
            Assert.That(fixture.Requests, Has.Count.EqualTo(3));
        }

        /// <summary>
        /// Annotation-node resolution cannot silently become the first history read on another server session.
        /// </summary>
        [Test]
        public async Task AnnotationResolutionAndPagingShareTheCapturedGeneration()
        {
            await using var fixture = new SessionRequestContextTestContext();
            fixture.Handler = (_, _) =>
            {
                fixture.Recreate();
                return new ValueTask<IServiceResponse>(new TranslateBrowsePathsToNodeIdsResponse
                {
                    Results =
                    [
                        new BrowsePathResult
                        {
                            Targets =
                            [
                                new BrowsePathTarget
                                {
                                    TargetId = new ExpandedNodeId(12u, 1),
                                    RemainingPathIndex = uint.MaxValue
                                }
                            ]
                        }
                    ]
                });
            };
            var client = new HistoryClient(fixture.ManagedSession,
                new HistoryClientOptions { BindSessionGeneration = true });
            ServiceResultException failure = Assert.ThrowsAsync<ServiceResultException>(async () =>
            {
                await foreach (Annotation annotation in client.ReadAnnotationsAsync(
                    new NodeId(10u, 1), s_start, s_start.AddHours(1)).ConfigureAwait(false))
                {
                    Assert.Fail($"Unexpected annotation: {annotation.Message}");
                }
            })!;
            Assert.That(failure.StatusCode, Is.EqualTo(StatusCodes.BadSessionIdInvalid));
            Assert.That(fixture.Requests, Has.Count.EqualTo(1));
            Assert.That(fixture.Requests[0], Is.TypeOf<TranslateBrowsePathsToNodeIdsRequest>());
        }

        /// <summary>
        /// Creates one small historical response.
        /// </summary>
        private static HistoryReadResponse Page(int value, ByteString point = default)
        {
            return new HistoryReadResponse
            {
                Results =
                [
                    new HistoryReadResult
                    {
                        ContinuationPoint = point,
                        HistoryData = new ExtensionObject(new HistoryData
                        {
                            DataValues = [new DataValue(Variant.From(value))]
                        })
                    }
                ]
            };
        }

        /// <summary>
        /// Starts a deterministic time-range query.
        /// </summary>
        private static IAsyncEnumerable<DataValue> Read(HistoryClient client, CancellationToken ct = default)
        {
            return client.ReadRawAsync(new NodeId(10u, 1), s_start, s_start.AddHours(1), cancellationToken: ct);
        }

        private static readonly DateTime s_start = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        private static readonly ByteString s_point = ByteString.From("retained-point"u8);
    }
}
