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
using Opc.Ua.WotCon.Client;

namespace Opc.Ua.WotCon.Tests.Client
{
    /// <summary>
    /// Status-bearing CloseAndUpdate uploads retain protocol results and source ownership.
    /// </summary>
    [TestFixture]
    [Category("WotCon")]
    [Parallelizable]
    public sealed class WoTAssetFileTypeResultTests
    {
        [SetUp]
        public void SetUp()
        {
            m_calls.Clear();
            m_status = StatusCodes.Good;
            m_beforeCall = null;
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var context = ServiceMessageContext.Create(telemetry);
            context.NamespaceUris.GetIndexOrAppend(Namespaces.WotCon);
            m_commitId = ExpandedNodeId.ToNodeId(MethodIds.WoTAssetFileType_CloseAndUpdate, context.NamespaceUris);
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
                    return new ValueTask<CallResponse>(new CallResponse
                    {
                        ResponseHeader = new ResponseHeader(),
                        Results =
                        [
                            new CallMethodResult
                            {
                                StatusCode = request.MethodId == m_commitId ? m_status : StatusCodes.Good,
                                OutputArguments = request.MethodId == Ua.MethodIds.FileType_Open
                                    ? [Variant.From(kHandle)] : []
                            }
                        ]
                    });
                });
            m_file = new WoTAssetFileTypeClient(m_session.Object, new NodeId("td-file", 1), telemetry);
        }

        [TestCaseSource(nameof(s_goodStatuses))]
        public async Task UploadAndUpdateWithResultAsyncPreservesActualStatusAndExactByteCountAsync(StatusCode status)
        {
            m_status = status;
            using var source = new NonSeekableUploadSource([11, 12, 13, 14, 15]);
            using var cancellation = new CancellationTokenSource();

            (long bytes, StatusCode actualStatus) = await m_file.UploadAndUpdateWithResultAsync(
                source, 5, 4, cancellation.Token).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(bytes, Is.EqualTo(5));
                Assert.That(actualStatus, Is.EqualTo(status));
                Assert.That(WrittenChunks(), Is.EqualTo(new byte[][] { [11, 12], [13, 14], [15] }));
                Assert.That(source.ReadCalls, Is.EqualTo(4));
                Assert.That(source.BytesRead, Is.EqualTo(5));
                Assert.That(source.IsDisposed, Is.False);
                Assert.That(m_calls.Select(c => c.Request.MethodId), Is.EqualTo(new[]
                {
                    Ua.MethodIds.FileType_Open, Ua.MethodIds.FileType_Write, Ua.MethodIds.FileType_Write,
                    Ua.MethodIds.FileType_Write, m_commitId
                }));
                Assert.That(m_calls[0].Request.InputArguments, Is.EqualTo(new[] { Variant.From((byte)6) }));
                Assert.That(m_calls.All(c => c.Token == cancellation.Token), Is.True);
                Assert.That(m_calls.All(c => c.Request.ObjectId == new NodeId("td-file", 1)), Is.True);
            });
            AssertHandleArguments();
            VerifyNoFallback();
        }

        [Test]
        public async Task UploadAndUpdateWithResultAsyncEmptySourceCommitsOnceWithZeroBytesAsync()
        {
            m_status = StatusCodes.GoodResultsMayBeIncomplete;
            using var source = new NonSeekableUploadSource([]);

            (long bytes, StatusCode status) = await m_file.UploadAndUpdateWithResultAsync(source, 1, 1)
                .ConfigureAwait(false);

            Assert.That(bytes, Is.Zero);
            Assert.That(status, Is.EqualTo(StatusCodes.GoodResultsMayBeIncomplete));
            Assert.That(m_calls.Select(c => c.Request.MethodId),
                Is.EqualTo(new[] { Ua.MethodIds.FileType_Open, m_commitId }));
            Assert.That(source.ReadCalls, Is.EqualTo(1));
            Assert.That(source.IsDisposed, Is.False);
            AssertHandleArguments();
        }

        [TestCaseSource(nameof(s_badStatuses))]
        public async Task UploadAndUpdateWithResultAsyncRefusalPreservesStatusAndDoesNotRetryCommit(StatusCode status)
        {
            m_status = status;
            using var source = new NonSeekableUploadSource([21, 22]);

            ServiceResultException exception = (await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await m_file.UploadAndUpdateWithResultAsync(source, 2, 4).ConfigureAwait(false)))!;

            Assert.That(exception.StatusCode, Is.EqualTo(status));
            Assert.That(WrittenChunks(), Is.EqualTo(new byte[][] { [21, 22] }));
            Assert.That(m_calls.Select(c => c.Request.MethodId), Is.EqualTo(new[]
            {
                Ua.MethodIds.FileType_Open, Ua.MethodIds.FileType_Write, m_commitId, Ua.MethodIds.FileType_Close
            }));
            Assert.That(m_calls[m_calls.Count - 1].Token.CanBeCanceled, Is.False);
            Assert.That(source.IsDisposed, Is.False);
            AssertHandleArguments();
            VerifyNoFallback();
        }

        [Test]
        public async Task UploadAndUpdateWithResultAsyncWriteFailureSurvivesCleanupFailure()
        {
            var primary = new IOException("Write failed.");
            var cleanup = new ServiceResultException(StatusCodes.BadSessionClosed);
            using var source = new NonSeekableUploadSource([31, 32]);
            m_beforeCall = (request, _) =>
            {
                if (request.MethodId == Ua.MethodIds.FileType_Write)
                {
                    throw primary;
                }
                if (request.MethodId == Ua.MethodIds.FileType_Close)
                {
                    throw cleanup;
                }
            };

            IOException exception = (await Assert.ThrowsAsync<IOException>(async () =>
                await m_file.UploadAndUpdateWithResultAsync(source, 2, 4).ConfigureAwait(false)))!;

            Assert.That(exception, Is.SameAs(primary));
            Assert.That(m_calls.Select(c => c.Request.MethodId),
                Is.EqualTo(new[]
                {
                    Ua.MethodIds.FileType_Open, Ua.MethodIds.FileType_Write, Ua.MethodIds.FileType_Close
                }));
            Assert.That(m_calls[2].Token.CanBeCanceled, Is.False);
            Assert.That(source.IsDisposed, Is.False);
        }

        [Test]
        public async Task UploadAndUpdateWithResultAsyncLateCancellationDoesNotReplaceCommittedSuccessAsync()
        {
            using var source = new NonSeekableUploadSource([41, 42, 43]);
            using var cancellation = new CancellationTokenSource();
            m_status = StatusCodes.GoodResultsMayBeIncomplete;
            m_beforeCall = (request, token) =>
            {
                if (request.MethodId == m_commitId)
                {
                    Assert.That(token, Is.EqualTo(cancellation.Token));
                    cancellation.Cancel();
                }
            };

            (long bytes, StatusCode status) = await m_file.UploadAndUpdateWithResultAsync(
                source, 3, 4, cancellation.Token).ConfigureAwait(false);

            Assert.That(bytes, Is.EqualTo(3));
            Assert.That(status, Is.EqualTo(StatusCodes.GoodResultsMayBeIncomplete));
            Assert.That(WrittenChunks(), Is.EqualTo(new byte[][] { [41, 42], [43] }));
            Assert.That(cancellation.IsCancellationRequested, Is.True);
            Assert.That(m_calls.Count(c => c.Request.MethodId == m_commitId), Is.EqualTo(1));
            Assert.That(m_calls.Any(c => c.Request.MethodId == Ua.MethodIds.FileType_Close), Is.False);
            Assert.That(source.IsDisposed, Is.False);
        }

        [Test]
        public async Task UploadAndUpdateWithResultAsyncRejectsNonSeekableOverflowWithoutCommit()
        {
            using var source = new NonSeekableUploadSource([51, 52, 53]);

            await Assert.ThrowsAsync<IOException>(async () =>
                await m_file.UploadAndUpdateWithResultAsync(source, 2, 4).ConfigureAwait(false));

            Assert.That(WrittenChunks(), Is.EqualTo(new byte[][] { [51, 52] }));
            Assert.That(m_calls.Any(c => c.Request.MethodId == m_commitId), Is.False);
            Assert.That(m_calls.Count(c => c.Request.MethodId == Ua.MethodIds.FileType_Close), Is.EqualTo(1));
            Assert.That(source.IsDisposed, Is.False);
        }

        [Test]
        public async Task UploadAndUpdateWithResultAsyncRejectsSeekableOverflowBeforeOpen()
        {
            using var source = new MemoryStream([51, 52, 53]);

            await Assert.ThrowsAsync<IOException>(async () =>
                await m_file.UploadAndUpdateWithResultAsync(source, 2, 4).ConfigureAwait(false));

            Assert.That(source.Position, Is.Zero);
            Assert.That(source.CanRead, Is.True);
            Assert.That(m_calls, Is.Empty);
        }

        [TestCase(0L, 4, "maxBytes")]
        [TestCase(-1L, 4, "maxBytes")]
        [TestCase(8L, 0, "chunkSize")]
        [TestCase(8L, -1, "chunkSize")]
        [TestCase(8L, 1048577, "chunkSize")]
        public async Task UploadAndUpdateWithResultAsyncValidatesLimitsBeforeOpen(long limit, int chunk, string parameter)
        {
            using var source = new NonSeekableUploadSource([61]);

            ArgumentOutOfRangeException exception = (await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
                await m_file.UploadAndUpdateWithResultAsync(source, limit, chunk).ConfigureAwait(false)))!;

            Assert.That(exception.ParamName, Is.EqualTo(parameter));
            Assert.That(source.ReadCalls, Is.Zero);
            Assert.That(source.IsDisposed, Is.False);
            Assert.That(m_calls, Is.Empty);
        }

        [Test]
        public async Task UploadAndUpdateWithResultAsyncRejectsNullSourceBeforeOpen()
        {
            ArgumentNullException exception = (await Assert.ThrowsAsync<ArgumentNullException>(async () =>
                await m_file.UploadAndUpdateWithResultAsync(null!, 8, 4).ConfigureAwait(false)))!;
            Assert.That(exception.ParamName, Is.EqualTo("source"));
            Assert.That(m_calls, Is.Empty);
        }

        [Test]
        public async Task UploadAndUpdateWithResultAsyncRejectsNullFileAndUnreadableSourceBeforeOpen()
        {
            using var source = new NonSeekableUploadSource([71]);
            using Stream unreadable = new Mock<Stream>().Object;

            ArgumentNullException fileException = (await Assert.ThrowsAsync<ArgumentNullException>(async () =>
                await WoTAssetFileTypeClientExtensions.UploadAndUpdateWithResultAsync(
                    null!, source, 8).ConfigureAwait(false)))!;
            ArgumentException streamException = (await Assert.ThrowsAsync<ArgumentException>(async () =>
                await m_file.UploadAndUpdateWithResultAsync(unreadable, 8).ConfigureAwait(false)))!;

            Assert.That(fileException.ParamName, Is.EqualTo("file"));
            Assert.That(streamException.ParamName, Is.EqualTo("source"));
            Assert.That(source.ReadCalls, Is.Zero);
            Assert.That(source.IsDisposed, Is.False);
            Assert.That(m_calls, Is.Empty);
        }

        private byte[][] WrittenChunks()
        {
            return m_calls.Where(c => c.Request.MethodId == Ua.MethodIds.FileType_Write).Select(c =>
            {
                Assert.That(c.Request.InputArguments[1].TryGetValue(out ByteString bytes), Is.True);
                return bytes.Span.ToArray();
            }).ToArray();
        }

        private void AssertHandleArguments()
        {
            foreach ((CallMethodRequest request, _) in
                m_calls.Where(c => c.Request.MethodId != Ua.MethodIds.FileType_Open))
            {
                Assert.That(request.InputArguments[0], Is.EqualTo(Variant.From(kHandle)));
            }
        }

        private void VerifyNoFallback()
        {
            m_session.Verify(s => s.TranslateBrowsePathsToNodeIdsAsync(
                It.IsAny<RequestHeader>(), It.IsAny<ArrayOf<BrowsePath>>(),
                It.IsAny<CancellationToken>()), Times.Never);
        }

        private const uint kHandle = 47;
        private static readonly StatusCode[] s_goodStatuses =
        [
            StatusCodes.Good, StatusCodes.GoodResultsMayBeIncomplete
        ];
        private static readonly StatusCode[] s_badStatuses =
        [
            StatusCodes.BadInvalidState, StatusCodes.BadMethodInvalid, StatusCodes.BadUserAccessDenied
        ];
        private Mock<ISessionClient> m_session = null!;
        private WoTAssetFileTypeClient m_file = null!;
        private NodeId m_commitId;
        private StatusCode m_status;
        private Action<CallMethodRequest, CancellationToken>? m_beforeCall;
        private readonly List<(CallMethodRequest Request, CancellationToken Token)> m_calls = [];
    }
}
