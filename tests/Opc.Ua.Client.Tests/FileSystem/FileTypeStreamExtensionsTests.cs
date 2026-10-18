/* Copyright (c) 2026 The OPC Foundation, Inc. All rights reserved.
 * Licensed under the MIT License. See LICENSE.txt in the project root. */

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;

namespace Opc.Ua.Client.Tests.FileSystem
{
    /// <summary>
    /// Wire-level and ownership checks for bounded FileType transfers.
    /// </summary>
    [TestFixture]
    [Category("FileSystem")]
    [Parallelizable]
    public sealed class FileTypeStreamExtensionsTests
    {
        [SetUp]
        public void SetUp()
        {
            m_calls.Clear();
            m_reads.Clear();
            m_beforeCall = null;
            m_closeResult = new CallMethodResult { StatusCode = StatusCodes.Good };
            m_logger = new RecordingLogger();
            var factory = new Mock<ILoggerFactory>();
            factory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(m_logger);
            var telemetry = new Mock<ITelemetryContext>();
            telemetry.SetupGet(t => t.LoggerFactory).Returns(factory.Object);
            var context = ServiceMessageContext.Create(telemetry.Object);
            m_session = new Mock<ISessionClient>(MockBehavior.Strict);
            m_session.SetupGet(s => s.MessageContext).Returns(context);
            m_session.Setup(s => s.CallAsync(
                    It.IsAny<RequestHeader>(), It.IsAny<ArrayOf<CallMethodRequest>>(),
                    It.IsAny<CancellationToken>()))
                .Returns<RequestHeader, ArrayOf<CallMethodRequest>, CancellationToken>((_, requests, token) =>
                {
                    Assert.That(requests.Count, Is.EqualTo(1));
                    CallMethodRequest request = requests[0];
                    m_calls.Add((request, token));
                    m_beforeCall?.Invoke(request, token);
                    ArrayOf<Variant> outputs = request.MethodId == MethodIds.FileType_Open
                        ? [Variant.From(kHandle)]
                        : request.MethodId == MethodIds.FileType_Read
                            ? [Variant.From(m_reads.Dequeue())]
                            : [];
                    return new ValueTask<CallResponse>(new CallResponse
                    {
                        ResponseHeader = new ResponseHeader(),
                        Results = [request.MethodId == MethodIds.FileType_Close
                            ? m_closeResult
                            : new CallMethodResult { StatusCode = StatusCodes.Good, OutputArguments = outputs }]
                    });
                });
            m_file = new FileTypeClient(m_session.Object, new NodeId("bounded-file", 2), telemetry.Object);
        }

        [Test]
        public async Task UploadFromAsyncContinuesShortNonSeekableReadsAndCommitsExactLimitAsync()
        {
            using var source = new ShortReadStream([1, 2, 3, 4, 5], maxRead: 2);
            using var cancellation = new CancellationTokenSource();

            long count = await m_file.UploadFromAsync(source, 5, 4, cancellation.Token).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(count, Is.EqualTo(5));
                Assert.That(source.BytesRead, Is.EqualTo(5));
                Assert.That(source.ReadCalls, Is.EqualTo(4), "A short non-empty read is not EOF.");
                Assert.That(source.IsDisposed, Is.False);
                Assert.That(WrittenChunks(), Is.EqualTo(new byte[][] { [1, 2], [3, 4], [5] }));
                Assert.That(MethodsCalled(), Is.EqualTo(new[]
                {
                    MethodIds.FileType_Open, MethodIds.FileType_Write, MethodIds.FileType_Write,
                    MethodIds.FileType_Write, MethodIds.FileType_Close
                }));
                Assert.That(m_calls[0].Request.InputArguments, Is.EqualTo(new[] { Variant.From((byte)6) }));
                Assert.That(m_calls.All(c => c.Request.ObjectId == new NodeId("bounded-file", 2)), Is.True);
                Assert.That(m_calls.All(c => c.Token == cancellation.Token), Is.True);
            });
            AssertHandleArguments();
        }

        [Test]
        public void UploadFromAsyncRejectsCumulativeOverflowWithoutSendingOversizedChunk()
        {
            using var source = new ShortReadStream([1, 2, 3, 4, 5, 6], maxRead: 2);

            Assert.ThrowsAsync<IOException>(async () =>
                await m_file.UploadFromAsync(source, 5, 4).ConfigureAwait(false));

            Assert.That(WrittenChunks(), Is.EqualTo(new byte[][] { [1, 2], [3, 4] }));
            Assert.That(source.BytesRead, Is.EqualTo(6));
            Assert.That(source.IsDisposed, Is.False);
            AssertClosedOnceWithoutCancellation();
        }

        [Test]
        public void UploadFromAsyncRejectsSeekableOverflowBeforeOpen()
        {
            using var source = new MemoryStream([1, 2, 3, 4, 5, 6]);

            Assert.ThrowsAsync<IOException>(async () =>
                await m_file.UploadFromAsync(source, 5, 4).ConfigureAwait(false));

            Assert.That(source.Position, Is.Zero);
            Assert.That(source.CanRead, Is.True);
            Assert.That(m_calls, Is.Empty);
        }

        [Test]
        public async Task UploadFromAsyncMeasuresOnlySeekableBytesRemainingAsync()
        {
            using var source = new MemoryStream([91, 92, 3, 4, 5]) { Position = 2 };

            long count = await m_file.UploadFromAsync(source, 3, 2).ConfigureAwait(false);

            Assert.That(count, Is.EqualTo(3));
            Assert.That(WrittenChunks(), Is.EqualTo(new byte[][] { [3, 4], [5] }));
            Assert.That(source.Position, Is.EqualTo(5));
            Assert.That(source.CanRead, Is.True);
            AssertClosedOnceWithoutCancellation();
        }

        [Test]
        public async Task WriteStreamAsyncLeavesCallerOwnedHandleOpenAsync()
        {
            using var source = new ShortReadStream([7, 8, 9], maxRead: 2);

            long count = await m_file.WriteStreamAsync(kHandle, source, 3, 4).ConfigureAwait(false);

            Assert.That(count, Is.EqualTo(3));
            Assert.That(WrittenChunks(), Is.EqualTo(new byte[][] { [7, 8], [9] }));
            Assert.That(MethodsCalled(), Is.EqualTo(new[] { MethodIds.FileType_Write, MethodIds.FileType_Write }));
            Assert.That(source.IsDisposed, Is.False);
            AssertHandleArguments();
        }

        [Test]
        public void WriteStreamAsyncFailureDoesNotCloseCallerOwnedHandle()
        {
            using var source = new ShortReadStream([7, 8, 9], maxRead: 2);

            Assert.ThrowsAsync<IOException>(async () =>
                await m_file.WriteStreamAsync(kHandle, source, 2, 4).ConfigureAwait(false));

            Assert.That(WrittenChunks(), Is.EqualTo(new byte[][] { [7, 8] }));
            Assert.That(MethodsCalled(), Is.EqualTo(new[] { MethodIds.FileType_Write }));
            Assert.That(source.IsDisposed, Is.False);
        }

        [Test]
        public async Task WriteStreamAndCloseAsyncUsesExistingHandleAndClosesExactlyOnceAsync()
        {
            using var source = new ShortReadStream([7, 8, 9], maxRead: 2);

            long count = await m_file.WriteStreamAndCloseAsync(kHandle, source, 3, 4).ConfigureAwait(false);

            Assert.That(count, Is.EqualTo(3));
            Assert.That(WrittenChunks(), Is.EqualTo(new byte[][] { [7, 8], [9] }));
            Assert.That(MethodsCalled(),
                Is.EqualTo(new[] { MethodIds.FileType_Write, MethodIds.FileType_Write, MethodIds.FileType_Close }));
            Assert.That(source.IsDisposed, Is.False);
            AssertHandleArguments();
        }

        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public async Task ResultBearingUploadPreservesFinalCloseStatusAndDiagnosticsEvenAfterLateCancellationAsync(
            bool alreadyOpen, bool cancelOnClose)
        {
            using var source = new ShortReadStream([71, 72, 73], maxRead: 2);
            using var cancellation = new CancellationTokenSource();
            m_closeResult = new CallMethodResult
            {
                StatusCode = StatusCodes.GoodResultsMayBeIncomplete,
                InputArgumentResults = [StatusCodes.BadOutOfRange],
                InputArgumentDiagnosticInfos = [new DiagnosticInfo { AdditionalInfo = "original handle diagnostic" }]
            };
            m_beforeCall = (request, token) =>
            {
                if (request.MethodId == MethodIds.FileType_Close)
                {
                    Assert.That(token, Is.EqualTo(cancellation.Token));
                    if (cancelOnClose)
                    {
                        cancellation.Cancel();
                    }
                }
            };

            (long bytes, CallMethodResult result) = alreadyOpen
                ? await m_file.WriteStreamAndCloseWithResultAsync(kHandle, source, 3, 4, cancellation.Token)
                    .ConfigureAwait(false)
                : await m_file.UploadFromWithResultAsync(source, 3, 4, cancellation.Token).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(bytes, Is.EqualTo(3));
                Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.GoodResultsMayBeIncomplete));
                Assert.That(result.InputArgumentResults, Is.EqualTo(new StatusCode[] { StatusCodes.BadOutOfRange }));
                Assert.That(result.InputArgumentDiagnosticInfos[0].AdditionalInfo,
                    Is.EqualTo("original handle diagnostic"));
                Assert.That(WrittenChunks(), Is.EqualTo(new byte[][] { [71, 72], [73] }));
                Assert.That(source.IsDisposed, Is.False);
                Assert.That(m_calls.Count(c => c.Request.MethodId == MethodIds.FileType_Open),
                    Is.EqualTo(alreadyOpen ? 0 : 1));
                Assert.That(m_calls.Count(c => c.Request.MethodId == MethodIds.FileType_Close), Is.EqualTo(1));
                Assert.That(cancellation.IsCancellationRequested, Is.EqualTo(cancelOnClose));
            });
            AssertHandleArguments();
            m_session.Verify(s => s.TranslateBrowsePathsToNodeIdsAsync(
                It.IsAny<RequestHeader>(), It.IsAny<ArrayOf<BrowsePath>>(),
                It.IsAny<CancellationToken>()), Times.Never);
        }

        [TestCaseSource(nameof(s_badCloseStatuses))]
        public void ResultBearingUploadPreservesRefusedCloseAndOnlyPerformsHandleCleanup(StatusCode status)
        {
            using var source = new ShortReadStream([81, 82], maxRead: 2);
            using var cancellation = new CancellationTokenSource();
            m_beforeCall = (request, _) =>
            {
                if (request.MethodId == MethodIds.FileType_Close)
                {
                    m_closeResult = new CallMethodResult
                    {
                        StatusCode = m_calls.Count(c => c.Request.MethodId == MethodIds.FileType_Close) == 1
                            ? status : StatusCodes.Good
                    };
                }
            };

            ServiceResultException exception = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await m_file.UploadFromWithResultAsync(source, 2, 4, cancellation.Token).ConfigureAwait(false))!;

            Assert.That(exception.StatusCode, Is.EqualTo(status));
            Assert.That(WrittenChunks(), Is.EqualTo(new byte[][] { [81, 82] }));
            Assert.That(m_calls.Where(c => c.Request.MethodId == MethodIds.FileType_Close).Select(c => c.Token),
                Is.EqualTo(new[] { cancellation.Token, CancellationToken.None }));
            Assert.That(source.IsDisposed, Is.False);
            AssertHandleArguments();
            m_session.Verify(s => s.TranslateBrowsePathsToNodeIdsAsync(
                It.IsAny<RequestHeader>(), It.IsAny<ArrayOf<BrowsePath>>(),
                It.IsAny<CancellationToken>()), Times.Never);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task EmptyTransferClosesOnceAndRetainsCallerStreamAsync(bool upload)
        {
            using var stream = new MemoryStream();
            m_reads.Enqueue(ByteString.Empty);

            long count = upload
                ? await m_file.UploadFromAsync(stream, 1, 1).ConfigureAwait(false)
                : await m_file.DownloadToStreamAsync(stream, 1, 1).ConfigureAwait(false);

            Assert.That(count, Is.Zero);
            Assert.That(stream.Length, Is.Zero);
            Assert.That(stream.CanRead && stream.CanWrite, Is.True);
            Assert.That(m_calls.Count(c => c.Request.MethodId == MethodIds.FileType_Write), Is.Zero);
            Assert.That(m_calls[0].Request.InputArguments,
                Is.EqualTo(new[] { Variant.From(upload ? (byte)6 : (byte)1) }));
            Assert.That(MethodsCalled(), Is.EqualTo(upload
                ? new[] { MethodIds.FileType_Open, MethodIds.FileType_Close }
                : new[] { MethodIds.FileType_Open, MethodIds.FileType_Read, MethodIds.FileType_Close }));
            AssertClosedOnceWithoutCancellation();
        }

        [Test]
        public async Task DownloadToStreamAsyncFollowsShortReadsAndPreservesExactBytesAsync()
        {
            m_reads.Enqueue(ByteString.From([11, 12]));
            m_reads.Enqueue(ByteString.From([13]));
            m_reads.Enqueue(ByteString.From([14, 15]));
            m_reads.Enqueue(ByteString.Empty);
            using var destination = new MemoryStream();
            using var cancellation = new CancellationTokenSource();

            long count = await m_file.DownloadToStreamAsync(destination, 5, 4, cancellation.Token)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(count, Is.EqualTo(5));
                Assert.That(destination.ToArray(), Is.EqualTo(new byte[] { 11, 12, 13, 14, 15 }));
                Assert.That(destination.CanWrite, Is.True);
                Assert.That(m_calls[0].Request.InputArguments, Is.EqualTo(new[] { Variant.From((byte)1) }));
                Assert.That(m_calls.Where(c => c.Request.MethodId == MethodIds.FileType_Read)
                    .Select(c => c.Request.InputArguments),
                    Is.All.EqualTo(new[] { Variant.From(kHandle), Variant.From(4) }.ToArrayOf()));
                Assert.That(m_calls.Count(c => c.Request.MethodId == MethodIds.FileType_Read), Is.EqualTo(4));
                Assert.That(m_calls.Where(c => c.Request.MethodId != MethodIds.FileType_Close)
                    .All(c => c.Token == cancellation.Token), Is.True);
            });
            AssertClosedOnceWithoutCancellation();
        }

        [TestCase(false)]
        [TestCase(true)]
        public void DownloadToStreamAsyncRejectsRangeOrCumulativeOverflowBeforeWrite(bool ignoredRange)
        {
            m_reads.Enqueue(ByteString.From([1, 2]));
            m_reads.Enqueue(ignoredRange ? ByteString.From([3, 4, 5, 6, 7]) : ByteString.From([3, 4, 5, 6]));
            using var destination = new MemoryStream();

            Assert.ThrowsAsync<IOException>(async () =>
                await m_file.DownloadToStreamAsync(destination, ignoredRange ? 20 : 5, 4).ConfigureAwait(false));

            Assert.That(destination.ToArray(), Is.EqualTo(new byte[] { 1, 2 }));
            Assert.That(destination.CanWrite, Is.True);
            Assert.That(m_calls.Count(c => c.Request.MethodId == MethodIds.FileType_Read), Is.EqualTo(2));
            AssertClosedOnceWithoutCancellation();
        }

        [TestCase("source")]
        [TestCase("write")]
        [TestCase("read")]
        public void TransferFailureSurvivesCleanupFailureAndLogsExactFile(string failureAt)
        {
            var primary = new IOException("Primary transfer failure.");
            var cleanup = new ServiceResultException(StatusCodes.BadSessionClosed);
            using var source = new ShortReadStream([1, 2], maxRead: 2);
            using var destination = new MemoryStream();
            if (failureAt == "source")
            {
                source.BeforeRead = () => throw primary;
            }
            m_beforeCall = (request, _) =>
            {
                if (request.MethodId == MethodIds.FileType_Close)
                {
                    throw cleanup;
                }
                if ((failureAt == "write" && request.MethodId == MethodIds.FileType_Write) ||
                    (failureAt == "read" && request.MethodId == MethodIds.FileType_Read))
                {
                    throw primary;
                }
            };

            IOException exception = Assert.ThrowsAsync<IOException>(async () =>
            {
                if (failureAt == "read")
                {
                    await m_file.DownloadToStreamAsync(destination, 5, 4).ConfigureAwait(false);
                }
                else
                {
                    await m_file.UploadFromAsync(source, 5, 4).ConfigureAwait(false);
                }
            })!;

            Assert.That(exception, Is.SameAs(primary));
            Assert.That(source.IsDisposed, Is.False);
            Assert.That(destination.CanWrite, Is.True);
            AssertClosedOnceWithoutCancellation();
            AssertCleanupLogged(cleanup);
        }

        [Test]
        public void DownloadToStreamAsyncClosesHandleAfterDestinationWriteFailure()
        {
            var primary = new IOException("Destination rejected the bytes.");
            using var destination = new FailingWriteStream(primary);
            m_reads.Enqueue(ByteString.From([1, 2]));

            IOException exception = Assert.ThrowsAsync<IOException>(async () =>
                await m_file.DownloadToStreamAsync(destination, 5, 4).ConfigureAwait(false))!;

            Assert.That(exception, Is.SameAs(primary));
            Assert.That(destination.Length, Is.Zero);
            Assert.That(destination.CanWrite, Is.True);
            AssertClosedOnceWithoutCancellation();
        }

        [TestCase(false)]
        [TestCase(true)]
        public void TransferCancellationClosesWithUncancelledTokenAndRetainsStream(bool upload)
        {
            using var cancellation = new CancellationTokenSource();
            using var stream = new MemoryStream([1, 2]);
            m_beforeCall = (request, token) =>
            {
                if (request.MethodId == (upload ? MethodIds.FileType_Write : MethodIds.FileType_Read))
                {
                    Assert.That(token, Is.EqualTo(cancellation.Token));
                    cancellation.Cancel();
                    throw new OperationCanceledException(token);
                }
            };

            OperationCanceledException exception = Assert.ThrowsAsync<OperationCanceledException>(async () =>
            {
                if (upload)
                {
                    await m_file.UploadFromAsync(stream, 5, 4, cancellation.Token).ConfigureAwait(false);
                }
                else
                {
                    await m_file.DownloadToStreamAsync(stream, 5, 4, cancellation.Token).ConfigureAwait(false);
                }
            })!;

            Assert.That(exception.CancellationToken, Is.EqualTo(cancellation.Token));
            Assert.That(stream.CanRead && stream.CanWrite, Is.True);
            AssertClosedOnceWithoutCancellation();
        }

        [Test]
        public async Task UploadFromAsyncLateCancellationAfterCompletedCloseCannotReplaceSuccessAsync()
        {
            using var cancellation = new CancellationTokenSource();
            using var source = new ShortReadStream([61, 62], maxRead: 1);
            m_beforeCall = (request, _) =>
            {
                if (request.MethodId == MethodIds.FileType_Close)
                {
                    cancellation.Cancel();
                }
            };

            long count = await m_file.UploadFromAsync(source, 2, 4, cancellation.Token).ConfigureAwait(false);

            Assert.That(count, Is.EqualTo(2));
            Assert.That(WrittenChunks(), Is.EqualTo(new byte[][] { [61], [62] }));
            Assert.That(cancellation.IsCancellationRequested, Is.True);
            Assert.That(m_calls.Count(c => c.Request.MethodId == MethodIds.FileType_Close), Is.EqualTo(1));
            Assert.That(source.IsDisposed, Is.False);
        }

        [Test]
        public async Task UploadFromWithResultAsyncAcceptsMaximumChunkAndLongByteLimitAsync()
        {
            using var source = new ShortReadStream([91], maxRead: 1);

            (long bytes, CallMethodResult result) = await m_file.UploadFromWithResultAsync(
                source, long.MaxValue, 1_048_576).ConfigureAwait(false);

            Assert.That(bytes, Is.EqualTo(1));
            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(WrittenChunks(), Is.EqualTo(new byte[][] { [91] }));
            Assert.That(source.ReadCalls, Is.EqualTo(2));
            Assert.That(source.IsDisposed, Is.False);
            AssertClosedOnceWithoutCancellation();
        }

        [TestCase(false)]
        [TestCase(true)]
        public void TransferOpenFailureDoesNotCloseAnUnacquiredHandle(bool upload)
        {
            using var stream = new MemoryStream([1, 2]);
            var primary = new ServiceResultException(StatusCodes.BadUserAccessDenied);
            m_beforeCall = (request, _) =>
            {
                if (request.MethodId == MethodIds.FileType_Open)
                {
                    throw primary;
                }
            };

            ServiceResultException exception = Assert.ThrowsAsync<ServiceResultException>(async () =>
            {
                if (upload)
                {
                    await m_file.UploadFromAsync(stream, 2, 4).ConfigureAwait(false);
                }
                else
                {
                    await m_file.DownloadToStreamAsync(stream, 2, 4).ConfigureAwait(false);
                }
            })!;

            Assert.That(exception, Is.SameAs(primary));
            Assert.That(MethodsCalled(), Is.EqualTo(new[] { MethodIds.FileType_Open }));
            Assert.That(stream.Position, Is.Zero);
            Assert.That(stream.CanRead && stream.CanWrite, Is.True);
        }

        [Test]
        public void DownloadToStreamAsyncPreservesCloseFailureWhenCleanupAlsoFails()
        {
            var primary = new IOException("Initial Close failed.");
            var cleanup = new IOException("Cleanup Close failed.");
            using var destination = new MemoryStream();
            m_reads.Enqueue(ByteString.From([101, 102]));
            m_reads.Enqueue(ByteString.Empty);
            m_beforeCall = (request, _) =>
            {
                if (request.MethodId == MethodIds.FileType_Close)
                {
                    throw m_calls.Count(c => c.Request.MethodId == MethodIds.FileType_Close) == 1 ? primary : cleanup;
                }
            };

            IOException exception = Assert.ThrowsAsync<IOException>(async () =>
                await m_file.DownloadToStreamAsync(destination, 2, 4).ConfigureAwait(false))!;

            Assert.That(exception, Is.SameAs(primary));
            Assert.That(destination.ToArray(), Is.EqualTo(new byte[] { 101, 102 }));
            Assert.That(destination.CanWrite, Is.True);
            Assert.That(m_calls.Where(c => c.Request.MethodId == MethodIds.FileType_Close).Select(c => c.Token),
                Is.EqualTo(new[] { CancellationToken.None, CancellationToken.None }));
            AssertCleanupLogged(cleanup);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void TransferRejectsNullFileBeforeConsumingStream(bool upload)
        {
            using var stream = new MemoryStream([1]);

            ArgumentNullException exception = Assert.ThrowsAsync<ArgumentNullException>(async () =>
            {
                if (upload)
                {
                    await FileTypeStreamExtensions.UploadFromWithResultAsync(null!, stream, 1).ConfigureAwait(false);
                }
                else
                {
                    await FileTypeStreamExtensions.DownloadToStreamAsync(null!, stream, 1).ConfigureAwait(false);
                }
            })!;

            Assert.That(exception.ParamName, Is.EqualTo("file"));
            Assert.That(stream.Position, Is.Zero);
            Assert.That(stream.CanRead, Is.True);
            Assert.That(m_calls, Is.Empty);
        }

        [TestCase(false, 0L, 4, "maxBytes")]
        [TestCase(true, -1L, 4, "maxBytes")]
        [TestCase(false, 4L, 0, "chunkSize")]
        [TestCase(true, 4L, -1, "chunkSize")]
        [TestCase(false, 4L, 1048577, "chunkSize")]
        [TestCase(true, 4L, 1048577, "chunkSize")]
        public void TransferValidatesBoundsBeforeOpening(bool upload, long limit, int chunk, string parameter)
        {
            using var stream = new MemoryStream();

            ArgumentOutOfRangeException exception = Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
            {
                if (upload)
                {
                    await m_file.UploadFromAsync(stream, limit, chunk).ConfigureAwait(false);
                }
                else
                {
                    await m_file.DownloadToStreamAsync(stream, limit, chunk).ConfigureAwait(false);
                }
            })!;

            Assert.That(exception.ParamName, Is.EqualTo(parameter));
            Assert.That(m_calls, Is.Empty);
            Assert.That(stream.CanRead && stream.CanWrite, Is.True);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void TransferRejectsWrongStreamCapabilityBeforeOpening(bool upload)
        {
            using Stream stream = upload
                ? new ShortReadStream([1], maxRead: 1, readable: false)
                : new MemoryStream([1], writable: false);

            ArgumentException exception = Assert.ThrowsAsync<ArgumentException>(async () =>
            {
                if (upload)
                {
                    await m_file.UploadFromAsync(stream, 5, 4).ConfigureAwait(false);
                }
                else
                {
                    await m_file.DownloadToStreamAsync(stream, 5, 4).ConfigureAwait(false);
                }
            })!;

            Assert.That(exception.ParamName, Is.EqualTo("stream"));
            Assert.That(m_calls, Is.Empty);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void TransferRejectsNullStreamBeforeOpening(bool upload)
        {
            ArgumentNullException exception = Assert.ThrowsAsync<ArgumentNullException>(async () =>
            {
                if (upload)
                {
                    await m_file.UploadFromAsync(null!, 5, 4).ConfigureAwait(false);
                }
                else
                {
                    await m_file.DownloadToStreamAsync(null!, 5, 4).ConfigureAwait(false);
                }
            })!;
            Assert.That(exception.ParamName, Is.EqualTo("stream"));
            Assert.That(m_calls, Is.Empty);
        }

        [Test]
        public void CloseAfterFailureAsyncRejectsNullFile()
        {
            ArgumentNullException exception = Assert.ThrowsAsync<ArgumentNullException>(async () =>
                await FileTypeStreamExtensions.CloseAfterFailureAsync(null!, kHandle).ConfigureAwait(false))!;

            Assert.That(exception.ParamName, Is.EqualTo("file"));
            Assert.That(m_calls, Is.Empty);
        }

        private NodeId[] MethodsCalled()
        {
            return m_calls.Select(c => c.Request.MethodId).ToArray();
        }

        private byte[][] WrittenChunks()
        {
            return m_calls.Where(c => c.Request.MethodId == MethodIds.FileType_Write)
                .Select(c =>
                {
                    Assert.That(c.Request.InputArguments[1].TryGetValue(out ByteString bytes), Is.True);
                    return bytes.Span.ToArray();
                }).ToArray();
        }

        private void AssertHandleArguments()
        {
            foreach ((CallMethodRequest request, _) in
                m_calls.Where(c => c.Request.MethodId != MethodIds.FileType_Open))
            {
                Assert.That(request.InputArguments[0], Is.EqualTo(Variant.From(kHandle)));
            }
        }

        private void AssertClosedOnceWithoutCancellation()
        {
            (CallMethodRequest Request, CancellationToken Token)[] closes =
                m_calls.Where(c => c.Request.MethodId == MethodIds.FileType_Close).ToArray();
            Assert.That(closes, Has.Length.EqualTo(1));
            Assert.That(closes[0].Request.InputArguments, Is.EqualTo(new[] { Variant.From(kHandle) }));
            Assert.That(closes[0].Token.CanBeCanceled, Is.False);
        }

        private void AssertCleanupLogged(Exception cleanup)
        {
            Assert.That(m_logger.Entries, Has.Count.EqualTo(1));
            Assert.That(m_logger.Entries[0].Level, Is.EqualTo(LogLevel.Warning));
            Assert.That(m_logger.Entries[0].Event.Name, Is.EqualTo("FileHandleCleanupFailed"));
            Assert.That(m_logger.Entries[0].Exception, Is.SameAs(cleanup));
            Assert.That(m_logger.Entries[0].Message, Is.EqualTo(
                "Failed to close a remote file handle after a transfer failure for ns=2;s=bounded-file."));
        }

        /// <summary>
        /// Formats during Log, before source-generated logging can clear its reusable state.
        /// </summary>
        private sealed class RecordingLogger : ILogger
        {
            public List<(LogLevel Level, EventId Event, Exception? Exception, string Message)> Entries { get; } = [];

            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull
            {
                return null;
            }

            public bool IsEnabled(LogLevel logLevel)
            {
                return logLevel >= LogLevel.Warning;
            }

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                Entries.Add((logLevel, eventId, exception, formatter(state, exception)));
            }
        }

        private sealed class ShortReadStream : Stream
        {
            public ShortReadStream(byte[] content, int maxRead, bool readable = true)
            {
                m_content = content;
                m_maxRead = maxRead;
                m_readable = readable;
            }

            public int BytesRead { get; private set; }
            public int ReadCalls { get; private set; }
            public bool IsDisposed { get; private set; }
            public Action? BeforeRead { get; set; }
            public override bool CanRead => m_readable && !IsDisposed;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => throw new NotSupportedException();
            public override long Position
            {
                get => throw new NotSupportedException();
                set => throw new NotSupportedException();
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                return ReadCore(buffer.AsSpan(offset, count), default);
            }

            public override Task<int> ReadAsync(
                byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            {
                return Task.FromResult(ReadCore(buffer.AsSpan(offset, count), cancellationToken));
            }

#if NET6_0_OR_GREATER
            public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            {
                return new ValueTask<int>(ReadCore(buffer.Span, cancellationToken));
            }
#endif

            private int ReadCore(Span<byte> buffer, CancellationToken token)
            {
                token.ThrowIfCancellationRequested();
                BeforeRead?.Invoke();
                ReadCalls++;
                int count = Math.Min(Math.Min(buffer.Length, m_maxRead), m_content.Length - BytesRead);
                m_content.AsSpan(BytesRead, count).CopyTo(buffer);
                BytesRead += count;
                return count;
            }

            public override void Flush() => throw new NotSupportedException();
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

            protected override void Dispose(bool disposing)
            {
                IsDisposed = true;
                base.Dispose(disposing);
            }

            private readonly byte[] m_content;
            private readonly int m_maxRead;
            private readonly bool m_readable;
        }

        private sealed class FailingWriteStream : MemoryStream
        {
            public FailingWriteStream(Exception exception)
            {
                m_exception = exception;
            }

            public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            {
                throw m_exception;
            }

#if NET6_0_OR_GREATER
            public override ValueTask WriteAsync(
                ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
            {
                throw m_exception;
            }
#endif

            private readonly Exception m_exception;
        }

        private const uint kHandle = 23;
        private static readonly StatusCode[] s_badCloseStatuses =
        [
            StatusCodes.BadMethodInvalid, StatusCodes.BadUserAccessDenied
        ];
        private FileTypeClient m_file = null!;
        private Mock<ISessionClient> m_session = null!;
        private RecordingLogger m_logger = null!;
        private CallMethodResult m_closeResult = null!;
        private Action<CallMethodRequest, CancellationToken>? m_beforeCall;
        private readonly Queue<ByteString> m_reads = new();
        private readonly List<(CallMethodRequest Request, CancellationToken Token)> m_calls = [];
    }
}
