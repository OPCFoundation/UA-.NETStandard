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
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client.FileSystem;
using Opc.Ua.Client.Tests.SessionContexts;

namespace Opc.Ua.Client.Tests.FileSystem
{
    /// <summary>
    /// Verifies bounded transfers and private handle ownership through real generation-checked dispatch.
    /// </summary>
    [TestFixture]
    [Parallelizable(ParallelScope.All)]
    public sealed class SessionFileHandleTests
    {
        /// <summary>
        /// Neither a write nor disposal can deliver an old handle to a recreated session.
        /// </summary>
        [Test]
        public async Task CapturedFileHandleRejectsWriteAndCleanupAfterRecreation()
        {
            await using var fixture = new SessionRequestContextTestContext();
            Script(fixture);
            await using SessionRequestContext context =
                await SessionRequestContext.CaptureAsync(fixture.ManagedSession).ConfigureAwait(false);
            SessionFileHandle handle = await context.OpenFileAsync(s_file, 6).ConfigureAwait(false);
            await using (handle.ConfigureAwait(false))
            {
                fixture.Recreate();
                using var source = new MemoryStream([1]);
                ServiceResultException failure = (await Assert.ThrowsAsync<ServiceResultException>(
                    async () => await handle.WriteFromAsync(source, 1).ConfigureAwait(false)))!;
                Assert.That(failure.StatusCode, Is.EqualTo(StatusCodes.BadSessionIdInvalid));
                Assert.That(handle.SessionId, Is.EqualTo(NodeId.Parse("s=original-session")));
            }
            Assert.That(Methods(fixture), Is.EqualTo([MethodIds.FileType_Open]));
        }

        /// <summary>
        /// Open is checked after waiting for recovery, not just when the caller obtained the context.
        /// </summary>
        [Test]
        public async Task CapturedOpenRejectsGenerationChangeAtDispatchGate()
        {
            await using var fixture = new SessionRequestContextTestContext();
            Script(fixture);
            await using SessionRequestContext context =
                await SessionRequestContext.CaptureAsync(fixture.ManagedSession).ConfigureAwait(false);
            Task<SessionFileHandle> opening;
            using (await fixture.ServiceGate.WriterLockAsync().ConfigureAwait(false))
            {
                opening = context.OpenFileAsync(s_file, 6).AsTask();
                Assert.That(opening.IsCompleted, Is.False);
                fixture.Recreate();
            }
            ServiceResultException failure = (await Assert.ThrowsAsync<ServiceResultException>(
                async () => await opening.ConfigureAwait(false)))!;
            Assert.That(failure.StatusCode, Is.EqualTo(StatusCodes.BadSessionIdInvalid));
            Assert.That(fixture.Requests, Is.Empty);
        }

        /// <summary>
        /// Short reads are drained, the exact byte bound is accepted, and the terminal status is retained.
        /// </summary>
        [Test]
        public async Task ScopedDownloadContinuesShortReadsAndPreservesCloseResult()
        {
            await using var fixture = new SessionRequestContextTestContext();
            var chunks = new Queue<ByteString>([ByteString.From([1]), ByteString.From([2, 3]), ByteString.Empty]);
            var expected = new CallMethodResult
            {
                StatusCode = StatusCodes.GoodResultsMayBeIncomplete,
                InputArgumentResults = [StatusCodes.BadOutOfRange],
                InputArgumentDiagnosticInfos = [new DiagnosticInfo { AdditionalInfo = "close diagnostic" }]
            };
            Script(fixture, chunks, (method, _) => method.MethodId == MethodIds.FileType_Close ? expected : null);
            await using SessionRequestContext context =
                await SessionRequestContext.CaptureAsync(fixture.ManagedSession).ConfigureAwait(false);
            using var destination = new MemoryStream();
            (long total, CallMethodResult result) = await context.DownloadToStreamWithResultAsync(
                s_file, destination, 3, 4).ConfigureAwait(false);
            Assert.That(total, Is.EqualTo(3));
            Assert.That(destination.ToArray(), Is.EqualTo(new byte[] { 1, 2, 3 }));
            Assert.That(result, Is.SameAs(expected));
            Assert.That(Methods(fixture), Is.EqualTo(
            [
                MethodIds.FileType_Open, MethodIds.FileType_Read, MethodIds.FileType_Read,
                MethodIds.FileType_Read, MethodIds.FileType_Close
            ]));
        }

        /// <summary>
        /// Oversized server chunks are refused before any bytes reach the destination.
        /// </summary>
        [Test]
        public async Task ScopedDownloadRejectsOversizedChunkBeforeWriting()
        {
            await using var fixture = new SessionRequestContextTestContext();
            Script(fixture, new Queue<ByteString>([ByteString.From([1, 2, 3, 4])]));
            await using SessionRequestContext context =
                await SessionRequestContext.CaptureAsync(fixture.ManagedSession).ConfigureAwait(false);
            using var destination = new MemoryStream();
            await Assert.ThrowsAsync<IOException>(async () => await context.DownloadToStreamWithResultAsync(
                s_file, destination, 3, 4).ConfigureAwait(false));
            Assert.That(destination.Length, Is.Zero);
            Assert.That(Methods(fixture), Is.EqualTo(
            [
                MethodIds.FileType_Open, MethodIds.FileType_Read, MethodIds.FileType_Close
            ]));
        }

        /// <summary>
        /// Invalid or already oversized seekable uploads do not allocate a remote handle.
        /// </summary>
        [TestCase(0)]
        [TestCase(2)]
        public async Task ScopedUploadRejectsInvalidBoundBeforeOpen(int limit)
        {
            await using var fixture = new SessionRequestContextTestContext();
            Script(fixture);
            await using SessionRequestContext context =
                await SessionRequestContext.CaptureAsync(fixture.ManagedSession).ConfigureAwait(false);
            using var source = new MemoryStream([1, 2, 3]);
            if (limit == 0)
            {
                await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
                    async () => await context.UploadFromWithResultAsync(s_file, source, limit).ConfigureAwait(false));
            }
            else
            {
                await Assert.ThrowsAsync<IOException>(
                    async () => await context.UploadFromWithResultAsync(s_file, source, limit).ConfigureAwait(false));
            }
            Assert.That(fixture.Requests, Is.Empty);
            Assert.That(source.Position, Is.Zero);
        }

        /// <summary>
        /// Nonseekable short reads are counted as they arrive, before the excess chunk is written.
        /// </summary>
        [Test]
        public async Task ScopedUploadBoundsNonSeekableShortReads()
        {
            await using var fixture = new SessionRequestContextTestContext();
            Script(fixture);
            await using SessionRequestContext context =
                await SessionRequestContext.CaptureAsync(fixture.ManagedSession).ConfigureAwait(false);
            using var source = new ShortSource([1, 2, 3, 4]);
            await Assert.ThrowsAsync<IOException>(
                async () => await context.UploadFromWithResultAsync(s_file, source, 3, 4).ConfigureAwait(false));
            Assert.That(Methods(fixture), Is.EqualTo(
            [
                MethodIds.FileType_Open, MethodIds.FileType_Write, MethodIds.FileType_Close
            ]));
            CallMethodRequest write = ((CallRequest)fixture.Requests[1]).MethodsToCall[0];
            Assert.That(write.InputArguments[1].TryGetValue(out ByteString bytes), Is.True);
            Assert.That(bytes, Is.EqualTo(ByteString.From([1, 2])));
            Assert.That(source.CanRead, Is.True);
        }

        /// <summary>
        /// A successful temporary commit remains successful when cancellation and recreation follow its effect.
        /// </summary>
        [Test]
        public async Task TemporaryUploadPreservesCommitAfterCancellationAndRecreation()
        {
            await using var fixture = new SessionRequestContextTestContext();
            using var cancellation = new CancellationTokenSource();
            NodeId completion = new(90u, 1);
            var expected = new CallMethodResult
            {
                StatusCode = StatusCodes.GoodCompletesAsynchronously,
                OutputArguments = [Variant.From(completion)]
            };
            Script(fixture, overrideResult: (method, _) =>
            {
                if (method.MethodId != MethodIds.TemporaryFileTransferType_CloseAndCommit)
                {
                    return null;
                }
                fixture.Recreate();
                cancellation.Cancel();
                return expected;
            });
            await using SessionRequestContext context =
                await SessionRequestContext.CaptureAsync(fixture.ManagedSession).ConfigureAwait(false);
            var transfer = new TemporaryFileTransferClient(
                context.Session, s_transfer, new FileSystemClientOptions { ChunkSize = 2 });
            using var source = new MemoryStream([1, 2, 3]);
            (long count, NodeId file, NodeId state, CallMethodResult result) = await transfer.UploadFromAsync(
                context, source, 3, ct: cancellation.Token).ConfigureAwait(false);
            Assert.That(count, Is.EqualTo(3));
            Assert.That(file, Is.EqualTo(s_file));
            Assert.That(state, Is.EqualTo(completion));
            Assert.That(result, Is.SameAs(expected));
            Assert.That(cancellation.IsCancellationRequested, Is.True);
            Assert.That(Methods(fixture), Is.EqualTo(
            [
                MethodIds.TemporaryFileTransferType_GenerateFileForWrite,
                MethodIds.FileType_Write, MethodIds.FileType_Write,
                MethodIds.TemporaryFileTransferType_CloseAndCommit
            ]));
        }

        /// <summary>
        /// Temporary reads use the generated handle, preserve short reads and close without an extra Open.
        /// </summary>
        [Test]
        public async Task TemporaryDownloadUsesGeneratedHandleAndPreservesBound()
        {
            await using var fixture = new SessionRequestContextTestContext();
            Script(fixture, new Queue<ByteString>([ByteString.From([1]), ByteString.From([2, 3]), ByteString.Empty]));
            await using SessionRequestContext context =
                await SessionRequestContext.CaptureAsync(fixture.ManagedSession).ConfigureAwait(false);
            var transfer = new TemporaryFileTransferClient(
                context.Session, s_transfer, new FileSystemClientOptions { ChunkSize = 2 });
            using var destination = new MemoryStream();
            (long count, NodeId file, CallMethodResult result) = await transfer.DownloadToAsync(
                context, destination, 3).ConfigureAwait(false);
            Assert.That(count, Is.EqualTo(3));
            Assert.That(file, Is.EqualTo(s_file));
            Assert.That(destination.ToArray(), Is.EqualTo(new byte[] { 1, 2, 3 }));
            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(Methods(fixture), Is.EqualTo(
            [
                MethodIds.TemporaryFileTransferType_GenerateFileForRead,
                MethodIds.FileType_Read, MethodIds.FileType_Read, MethodIds.FileType_Read, MethodIds.FileType_Close
            ]));
        }

        /// <summary>
        /// Readiness metadata from an old generation never causes a Read or Close on its replacement.
        /// </summary>
        [Test]
        public async Task TemporaryReadReadinessRejectsReplacementBeforeReadOrCleanup()
        {
            await using var fixture = new SessionRequestContextTestContext();
            fixture.Handler = (request, _) =>
            {
                if (request is CallRequest)
                {
                    return new ValueTask<IServiceResponse>(new CallResponse
                    {
                        Results =
                        [
                            new CallMethodResult
                            {
                                OutputArguments =
                                    [Variant.From(s_file), Variant.From(kHandle), Variant.From(new NodeId(99u, 1))]
                            }
                        ]
                    });
                }
                Assert.That(request, Is.TypeOf<TranslateBrowsePathsToNodeIdsRequest>());
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
                                    TargetId = new ExpandedNodeId(100u, 1),
                                    RemainingPathIndex = uint.MaxValue
                                }
                            ]
                        }
                    ]
                });
            };
            await using SessionRequestContext context =
                await SessionRequestContext.CaptureAsync(fixture.ManagedSession).ConfigureAwait(false);
            var transfer = new TemporaryFileTransferClient(context.Session, s_transfer);
            ServiceResultException failure = (await Assert.ThrowsAsync<ServiceResultException>(
                async () => await transfer.GenerateFileForReadAsync(context).ConfigureAwait(false)))!;
            Assert.That(failure.StatusCode, Is.EqualTo(StatusCodes.BadSessionIdInvalid));
            Assert.That(fixture.Requests, Has.Count.EqualTo(2));
            Assert.That(fixture.Requests[0], Is.TypeOf<CallRequest>());
            Assert.That(fixture.Requests[1], Is.TypeOf<TranslateBrowsePathsToNodeIdsRequest>());
        }

        /// <summary>
        /// A refused commit is returned unchanged and only the original file handle is closed.
        /// </summary>
        [Test]
        public async Task TemporaryCommitPreservesRefusalAndClosesOriginalHandle()
        {
            await using var fixture = new SessionRequestContextTestContext();
            var expected = new CallMethodResult { StatusCode = StatusCodes.BadUserAccessDenied };
            Script(fixture, overrideResult: (method, _) =>
                method.MethodId == MethodIds.TemporaryFileTransferType_CloseAndCommit ? expected : null);
            await using SessionRequestContext context =
                await SessionRequestContext.CaptureAsync(fixture.ManagedSession).ConfigureAwait(false);
            var transfer = new TemporaryFileTransferClient(context.Session, s_transfer);
            using var source = new MemoryStream([1]);
            (long count, _, NodeId state, CallMethodResult result) = await transfer.UploadFromAsync(
                context, source, 1).ConfigureAwait(false);
            Assert.That(count, Is.EqualTo(1));
            Assert.That(state.IsNull, Is.True);
            Assert.That(result, Is.SameAs(expected));
            Assert.That(Methods(fixture), Is.EqualTo(
            [
                MethodIds.TemporaryFileTransferType_GenerateFileForWrite,
                MethodIds.FileType_Write, MethodIds.TemporaryFileTransferType_CloseAndCommit, MethodIds.FileType_Close
            ]));
            CallMethodRequest close = ((CallRequest)fixture.Requests[3]).MethodsToCall[0];
            Assert.That(close.ObjectId, Is.EqualTo(s_file));
            Assert.That(close.InputArguments, Is.EqualTo([Variant.From(kHandle)]));
        }

        /// <summary>
        /// A client cannot adopt another captured context, even for the same current server session.
        /// </summary>
        [Test]
        public async Task TemporaryClientRejectsAnotherContextBeforeGenerate()
        {
            await using var fixture = new SessionRequestContextTestContext();
            Script(fixture);
            await using SessionRequestContext original =
                await SessionRequestContext.CaptureAsync(fixture.ManagedSession).ConfigureAwait(false);
            await using SessionRequestContext other =
                await SessionRequestContext.CaptureAsync(fixture.ManagedSession).ConfigureAwait(false);
            var transfer = new TemporaryFileTransferClient(original.Session, s_transfer);
            await Assert.ThrowsAsync<ArgumentException>(
                async () => await transfer.GenerateFileForWriteAsync(other).ConfigureAwait(false));
            Assert.That(fixture.Requests, Is.Empty);
        }

        /// <summary>
        /// A malformed successful commit is known to be committed and is not followed by an unsafe Close.
        /// </summary>
        [Test]
        public async Task MalformedCompletedCommitDoesNotCloseConsumedHandle()
        {
            await using var fixture = new SessionRequestContextTestContext();
            Script(fixture, overrideResult: (method, _) =>
                method.MethodId == MethodIds.TemporaryFileTransferType_CloseAndCommit
                    ? new CallMethodResult { StatusCode = StatusCodes.Good, OutputArguments = [] } : null);
            await using SessionRequestContext context =
                await SessionRequestContext.CaptureAsync(fixture.ManagedSession).ConfigureAwait(false);
            var transfer = new TemporaryFileTransferClient(context.Session, s_transfer);
            SessionFileHandle handle = await transfer.GenerateFileForWriteAsync(context).ConfigureAwait(false);
            await using (handle.ConfigureAwait(false))
            {
                ServiceResultException failure = (await Assert.ThrowsAsync<ServiceResultException>(
                    async () => await handle.CommitAsync().ConfigureAwait(false)))!;
                Assert.That(failure.StatusCode, Is.EqualTo(StatusCodes.BadDecodingError));
                Assert.That(handle.IsCommitted, Is.True);
            }
            Assert.That(Methods(fixture), Is.EqualTo(
            [
                MethodIds.TemporaryFileTransferType_GenerateFileForWrite,
                MethodIds.TemporaryFileTransferType_CloseAndCommit
            ]));
        }

        /// <summary>
        /// Installs a minimal FileType and TemporaryFileTransfer service script.
        /// </summary>
        private static void Script(
            SessionRequestContextTestContext fixture, Queue<ByteString>? chunks = null,
            Func<CallMethodRequest, CancellationToken, CallMethodResult?>? overrideResult = null)
        {
            fixture.Handler = (request, token) =>
            {
                var call = (CallRequest)request;
                Assert.That(call.MethodsToCall.Count, Is.EqualTo(1));
                CallMethodRequest method = call.MethodsToCall[0];
                CallMethodResult? result = overrideResult?.Invoke(method, token);
                ArrayOf<Variant> output = method.MethodId == MethodIds.FileType_Open
                    ? [Variant.From(kHandle)]
                    : method.MethodId == MethodIds.FileType_Read
                        ? [Variant.From(chunks!.Dequeue())]
                        : method.MethodId == MethodIds.TemporaryFileTransferType_GenerateFileForWrite
                            ? [Variant.From(s_file), Variant.From(kHandle)]
                            : method.MethodId == MethodIds.TemporaryFileTransferType_GenerateFileForRead
                                ? [Variant.From(s_file), Variant.From(kHandle), Variant.From(NodeId.Null)]
                                : [];
                return new ValueTask<IServiceResponse>(new CallResponse
                {
                    Results = [result ?? new CallMethodResult { OutputArguments = output }]
                });
            };
        }

        /// <summary>
        /// Gets only the methods that reached the transport.
        /// </summary>
        private static NodeId[] Methods(SessionRequestContextTestContext fixture)
        {
            return [.. fixture.Requests.Cast<CallRequest>().Select(request => request.MethodsToCall[0].MethodId)];
        }

        /// <summary>
        /// Produces two-byte reads without exposing an advance length.
        /// </summary>
        private sealed class ShortSource(byte[] bytes) : MemoryStream(bytes)
        {
            /// <inheritdoc/>
            public override bool CanSeek => false;

            /// <inheritdoc/>
            public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct)
            {
                return base.ReadAsync(buffer, offset, Math.Min(count, 2), ct);
            }

#if NET6_0_OR_GREATER
            /// <inheritdoc/>
            public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
            {
                return base.ReadAsync(buffer[..Math.Min(buffer.Length, 2)], ct);
            }
#endif
        }

        private const uint kHandle = 73;
        private static readonly NodeId s_file = new(80u, 1);
        private static readonly NodeId s_transfer = new(81u, 1);
    }
}
