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
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Server.FileSystem;

namespace Opc.Ua.Server.Tests.FileSystem
{
    /// <summary>
    /// Exercises optional combined access and the lifetime of serialized provider operations.
    /// </summary>
    [TestFixture]
    [Category("FileSystem")]
    public sealed class FileHandleLifetimeRegressionTests
    {
        /// <summary>
        /// A legacy provider must not receive any destructive open for a combined access request.
        /// </summary>
        [TestCase((byte)3)]
        [TestCase((byte)7)]
        [TestCase((byte)11)]
        public async Task LegacyCombinedOpenRejectsBeforeProviderIoAsync(byte mode)
        {
            var provider = new Mock<IFileSystemProvider>(MockBehavior.Strict);
            await using var handles = new FileHandle(provider.Object, "file");
            (ServiceResult result, uint id) = await handles.OpenAsync(s_session, mode, CancellationToken.None)
                .ConfigureAwait(false);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadNotSupported));
            Assert.That(id, Is.Zero);
            provider.VerifyNoOtherCalls();
        }

        /// <summary>
        /// An opt-in provider receives the write positioning flags and supplies exactly one shared cursor.
        /// </summary>
        [TestCase((byte)3, FileWriteMode.OpenOrCreate)]
        [TestCase((byte)7, FileWriteMode.Truncate)]
        [TestCase((byte)11, FileWriteMode.Append)]
        public async Task CombinedOpenUsesOneExclusiveSessionOwnedCursorAsync(byte mode, FileWriteMode writeMode)
        {
            using var stream = new MemoryStream(new byte[4], writable: true);
            Mock<IFileSystemReadWriteProvider> provider = Provider(stream);
            await using var handles = new FileHandle(provider.Object, "file");
            (ServiceResult result, uint id) = await handles.OpenAsync(s_session, mode, CancellationToken.None)
                .ConfigureAwait(false);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.Good));
            provider.Verify(value => value.OpenReadWriteAsync(
                "file", writeMode, It.IsAny<CancellationToken>()), Times.Once);
            Assert.That(handles.GetStream(s_session, id, 1), Is.SameAs(stream));
            Assert.That(handles.GetStream(s_session, id, 2), Is.SameAs(stream));
            Assert.That(await handles.AcquireAsync(s_other, id, 0, CancellationToken.None).ConfigureAwait(false),
                Is.Null);
            using (FileHandle.Operation? write = await handles.AcquireAsync(s_session, id, 2, CancellationToken.None)
                .ConfigureAwait(false))
            {
                Assert.That(write, Is.Not.Null);
#if NET48
                await write!.Stream.WriteAsync(new byte[] { 17 }, 0, 1, write.CancellationToken).ConfigureAwait(false);
#else
                await write!.Stream.WriteAsync(new byte[] { 17 }.AsMemory(), write.CancellationToken)
                    .ConfigureAwait(false);
#endif
            }
            using (FileHandle.Operation? read = await handles.AcquireAsync(s_session, id, 1, CancellationToken.None)
                .ConfigureAwait(false))
            {
                Assert.That(read!.Stream.Position, Is.EqualTo(1));
                read.Stream.Position = 0;
                byte[] buffer = new byte[1];
#if NET48
                await read.Stream.ReadAsync(buffer, 0, 1, read.CancellationToken).ConfigureAwait(false);
#else
                await read.Stream.ReadAsync(buffer.AsMemory(), read.CancellationToken).ConfigureAwait(false);
#endif
                Assert.That(buffer[0], Is.EqualTo(17));
            }
            (ServiceResult blocked, _) = await handles.OpenAsync(s_other, 7, CancellationToken.None)
                .ConfigureAwait(false);
            Assert.That(blocked.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
            provider.Verify(value => value.OpenReadWriteAsync(
                It.IsAny<string>(), It.IsAny<FileWriteMode>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        /// <summary>
        /// Combined access requires a provider stream that actually supports both directions and seeking.
        /// </summary>
        [Test]
        public async Task InvalidCombinedStreamIsDisposedBeforeReservationReleaseAsync()
        {
            using var stream = new MemoryStream(new byte[4], writable: false);
            Mock<IFileSystemReadWriteProvider> provider = Provider(stream);
            await using var handles = new FileHandle(provider.Object, "file");
            (ServiceResult result, uint id) = await handles.OpenAsync(s_session, 3, CancellationToken.None)
                .ConfigureAwait(false);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadNotSupported));
            Assert.That(id, Is.Zero);
            Assert.That(stream.CanRead, Is.False);
            Assert.That(handles.TryBlockOpens(), Is.True);
            handles.UnblockOpens();
        }

        /// <summary>
        /// An inline idle close releases the reservation without scheduling unnecessary cancellation.
        /// </summary>
        [Test]
        public async Task InlineSessionClosureReleasesIdleWriterImmediatelyAsync()
        {
            using var stream = new MemoryStream();
            Mock<IFileSystemReadWriteProvider> provider = Provider(stream);
            provider.Setup(value => value.OpenReadAsync("file", It.IsAny<CancellationToken>()))
                .Returns(() => new ValueTask<Stream>(new MemoryStream(new byte[] { 10, 20 }, writable: false)));
            await using var handles = new FileHandle(provider.Object, "file");
            (ServiceResult opened, uint writer) = await handles.OpenAsync(s_session, 3, CancellationToken.None)
                .ConfigureAwait(false);
            Assert.That(opened.StatusCode, Is.EqualTo(StatusCodes.Good));

            handles.CloseSession(s_session, defer: false);

            Assert.That(handles.OpenCount, Is.Zero);
            Assert.That(handles.GetStream(s_session, writer), Is.Null);
            Assert.That(stream.CanRead, Is.False);
            Assert.That(handles.CleanupCompletion.IsCompleted, Is.True);
            (ServiceResult result, uint reader) = await handles.OpenAsync(s_other, 1, CancellationToken.None)
                .ConfigureAwait(false);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(await handles.CloseAsync(s_other, reader).ConfigureAwait(false), Is.True);
        }

        /// <summary>
        /// Completed asynchronous open tokens are detached, while synchronous compatibility tokens still cancel.
        /// </summary>
        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public async Task CompletedProviderOpenPreservesCancellationContractAsync(bool synchronous, bool defer)
        {
            using var stream = new MemoryStream();
            Mock<IFileSystemReadWriteProvider> provider = Provider(stream);
            CancellationToken providerToken = default;
            CancellationTokenRegistration registration = default;
            var cancelled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            provider.Setup(value => value.OpenReadWriteAsync(
                "file", It.IsAny<FileWriteMode>(), It.IsAny<CancellationToken>()))
                .Returns((string _, FileWriteMode _, CancellationToken token) =>
                {
                    providerToken = token;
                    registration = token.Register(() => cancelled.TrySetResult(true));
                    return new ValueTask<Stream>(stream);
                });
            await using var handles = new FileHandle(provider.Object, "file");
            try
            {
                ServiceResult opened;
                if (synchronous)
                {
                    opened = handles.Open(s_session, 3, out _);
                }
                else
                {
                    (opened, _) = await handles.OpenAsync(s_session, 3, CancellationToken.None).ConfigureAwait(false);
                }
                Assert.That(opened.StatusCode, Is.EqualTo(StatusCodes.Good));
                handles.CloseSession(s_session, defer);
                await handles.CleanupCompletion.ConfigureAwait(false);
                Assert.That(providerToken.IsCancellationRequested, Is.EqualTo(synchronous));
                Assert.That(cancelled.Task.IsCompleted, Is.EqualTo(synchronous));
            }
            finally
            {
                registration.Dispose();
            }
        }

        /// <summary>
        /// Inline cleanup retains an open until the provider's linked cancellation token is detached.
        /// </summary>
        [Test]
        public async Task InlineSessionClosureWaitsForProviderTokenDetachmentAsync()
        {
            using var releaseSeek = new ManualResetEventSlim();
            using var releaseCallback = new ManualResetEventSlim();
            using var requestCancellation = new CancellationTokenSource();
            using var stream = new SuspendedSeekStream(releaseSeek);
            Mock<IFileSystemReadWriteProvider> provider = Provider(stream);
            var callbackEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            CancellationTokenRegistration registration = default;
            provider.Setup(value => value.OpenReadWriteAsync(
                "file", It.IsAny<FileWriteMode>(), It.IsAny<CancellationToken>()))
                .Returns((string _, FileWriteMode _, CancellationToken token) =>
                {
                    registration = token.Register(() =>
                    {
                        callbackEntered.TrySetResult(true);
                        if (!releaseCallback.Wait(TimeSpan.FromSeconds(10)))
                        {
                            throw new TimeoutException("The test did not release the provider cancellation callback.");
                        }
                    });
                    return new ValueTask<Stream>(stream);
                });
            await using var handles = new FileHandle(provider.Object, "file");
            Task<(ServiceResult Result, uint Handle)> open = Task.Run(async () =>
                await handles.OpenAsync(s_session, 3, requestCancellation.Token).ConfigureAwait(false));
            Task? cancel = null;
            try
            {
                Thread openingThread = await stream.SeekEntered.Task.WaitAsync(TimeSpan.FromSeconds(5))
                    .ConfigureAwait(false);
                cancel = Task.Run(() => requestCancellation.Cancel());
                await callbackEntered.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                releaseSeek.Set();
                await stream.SeekExited.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                await WaitForBlockedThreadAsync(openingThread).ConfigureAwait(false);
                Assert.That(open.IsCompleted, Is.False);

                handles.CloseSession(s_session, defer: false);

                Assert.That(stream.CanRead, Is.True);
                Assert.That(handles.TryBlockOpens(), Is.False);
                (ServiceResult blocked, _) = await handles.OpenAsync(s_other, 1, CancellationToken.None)
                    .ConfigureAwait(false);
                Assert.That(blocked.StatusCode, Is.EqualTo(StatusCodes.BadNotReadable));
            }
            finally
            {
                releaseSeek.Set();
                releaseCallback.Set();
                try
                {
                    await Task.WhenAll(cancel ?? Task.CompletedTask, open).WaitAsync(TimeSpan.FromSeconds(5))
                        .ConfigureAwait(false);
                }
                finally
                {
                    registration.Dispose();
                }
            }
            (ServiceResult opened, _) = await open.ConfigureAwait(false);
            Assert.That(opened.StatusCode, Is.EqualTo(StatusCodes.Good));
            await handles.CleanupCompletion.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            Assert.That(stream.CanRead, Is.False);
            Assert.That(handles.TryBlockOpens(), Is.True);
            handles.UnblockOpens();
        }

        /// <summary>
        /// Inline lifecycle cleanup still cancels active work and retains its reservation until it drains.
        /// </summary>
        [Test]
        public async Task InlineSessionClosureCancelsAndDrainsActiveOperationAsync()
        {
            using var stream = new MemoryStream();
            await using var handles = new FileHandle(Provider(stream).Object, "file");
            (_, uint writer) = await handles.OpenAsync(s_session, 3, CancellationToken.None).ConfigureAwait(false);
            FileHandle.Operation operation = (await handles.AcquireAsync(s_session, writer, 1, CancellationToken.None)
                .ConfigureAwait(false))!;
            var cancelled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using CancellationTokenRegistration registration =
                operation.CancellationToken.Register(() => cancelled.TrySetResult(true));
            try
            {
                handles.CloseSession(s_session, defer: false);
                await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                Assert.That(operation.CancellationToken.IsCancellationRequested, Is.True);
                Assert.That(handles.CleanupCompletion.IsCompleted, Is.False);
                Assert.That(stream.CanRead, Is.True);
                (ServiceResult blocked, _) = await handles.OpenAsync(s_other, 3, CancellationToken.None)
                    .ConfigureAwait(false);
                Assert.That(blocked.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
            }
            finally
            {
                operation.Dispose();
            }
            await handles.CleanupCompletion.ConfigureAwait(false);
            Assert.That(stream.CanRead, Is.False);
            Assert.That(handles.TryBlockOpens(), Is.True);
            handles.UnblockOpens();
        }

        /// <summary>
        /// Inline cleanup never runs or waits for an active application's blocking cancellation callback.
        /// </summary>
        [Test]
        public async Task InlineSessionClosureDoesNotWaitForBlockingCancellationCallbackAsync()
        {
            using var stream = new MemoryStream();
            using var release = new ManualResetEventSlim();
            await using var handles = new FileHandle(Provider(stream).Object, "file");
            (_, uint writer) = await handles.OpenAsync(s_session, 3, CancellationToken.None).ConfigureAwait(false);
            FileHandle.Operation operation = (await handles.AcquireAsync(s_session, writer, 1, CancellationToken.None)
                .ConfigureAwait(false))!;
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using CancellationTokenRegistration registration = operation.CancellationToken.Register(() =>
            {
                entered.TrySetResult(true);
                if (!release.Wait(TimeSpan.FromSeconds(10)))
                {
                    throw new TimeoutException("The test did not release the cancellation callback.");
                }
            });
            try
            {
                handles.CloseSession(s_session, defer: false);
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                Assert.That(handles.CleanupCompletion.IsCompleted, Is.False);
                Assert.That(stream.CanRead, Is.True);
                Assert.That(handles.TryBlockOpens(), Is.False);
                (ServiceResult blocked, _) = await handles.OpenAsync(s_other, 1, CancellationToken.None)
                    .ConfigureAwait(false);
                Assert.That(blocked.StatusCode, Is.EqualTo(StatusCodes.BadNotReadable));
            }
            finally
            {
                release.Set();
                operation.Dispose();
            }
            await handles.CleanupCompletion.ConfigureAwait(false);
            Assert.That(stream.CanRead, Is.False);
        }

        /// <summary>
        /// Explicit close stops admission but drains even operations already queued behind an active cursor owner.
        /// </summary>
        [Test]
        public async Task ExplicitCloseDrainsAdmittedOperationsBeforeDisposalAsync()
        {
            using var stream = new MemoryStream(new byte[4], writable: true);
            await using var handles = new FileHandle(Provider(stream).Object, "file");
            (_, uint id) = await handles.OpenAsync(s_session, 3, CancellationToken.None).ConfigureAwait(false);
            FileHandle.Operation first = (await handles.AcquireAsync(s_session, id, 2, CancellationToken.None)
                .ConfigureAwait(false))!;
            Task<FileHandle.Operation?> queued =
                handles.AcquireAsync(s_session, id, 1, CancellationToken.None).AsTask();
            Task<bool> close = handles.CloseAsync(s_session, id).AsTask();
            try
            {
                Assert.That(queued.IsCompleted, Is.False);
                Assert.That(close.IsCompleted, Is.False);
                Assert.That(stream.CanRead, Is.True);
                Assert.That(await handles.AcquireAsync(s_session, id, 0, CancellationToken.None).ConfigureAwait(false),
                    Is.Null);
                Assert.That(handles.TryBlockOpens(), Is.False);
                first.Stream.Position = 2;
            }
            finally
            {
                first.Dispose();
            }
            using (FileHandle.Operation? second = await queued.ConfigureAwait(false))
            {
                Assert.That(second, Is.Not.Null);
                Assert.That(second!.Stream.Position, Is.EqualTo(2));
                Assert.That(close.IsCompleted, Is.False);
            }
            Assert.That(await close.ConfigureAwait(false), Is.True);
            Assert.That(stream.CanRead, Is.False);
            Assert.That(handles.TryBlockOpens(), Is.True);
            handles.UnblockOpens();
        }

        /// <summary>
        /// A shutdown deadline cancels provider I/O but never disposes its stream or releases its reservation.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task CancellationIgnoringIoRetainsReservationPastLifecycleDeadlineAsync(bool shutdown)
        {
            using var stream = new SuspendedReadStream();
            await using var handles = new FileHandle(Provider(stream).Object, "file")
            {
                CloseTimeout = TimeSpan.Zero
            };
            (_, uint id) = await handles.OpenAsync(s_session, 3, CancellationToken.None).ConfigureAwait(false);
            FileHandle.Operation operation = (await handles.AcquireAsync(s_session, id, 1, CancellationToken.None)
                .ConfigureAwait(false))!;
            var cancelled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using CancellationTokenRegistration registration =
                operation.CancellationToken.Register(() => cancelled.TrySetResult(true));
            Task<int> read = operation.Stream.ReadAsync(new byte[1], 0, 1, operation.CancellationToken);
            try
            {
                if (shutdown)
                {
                    await handles.DisposeAsync().ConfigureAwait(false);
                }
                else
                {
                    await handles.CloseSessionAsync(s_session).ConfigureAwait(false);
                }
                await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                Assert.That(operation.CancellationToken.IsCancellationRequested, Is.True);
                Assert.That(read.IsCompleted, Is.False);
                Assert.That(stream.Disposed, Is.False);
                Assert.That(handles.CleanupCompletion.IsCompleted, Is.False);
                Assert.That(handles.TryBlockOpens(), Is.False);
                (ServiceResult blocked, _) = await handles.OpenAsync(s_other, 3, CancellationToken.None)
                    .ConfigureAwait(false);
                Assert.That(blocked.StatusCode,
                    Is.EqualTo(shutdown ? StatusCodes.BadShutdown : StatusCodes.BadInvalidState));
            }
            finally
            {
                stream.Release.TrySetResult(1);
                await read.ConfigureAwait(false);
                operation.Dispose();
            }
            await handles.CleanupCompletion.ConfigureAwait(false);
            Assert.That(stream.Disposed, Is.True);
            Assert.That(handles.TryBlockOpens(), Is.True);
            handles.UnblockOpens();
        }

        /// <summary>
        /// A cancellation-ignoring provider open retains exclusivity until its late stream has been disposed.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task PendingOpenKeepsWriterReservedUntilLateStreamCleanupAsync(bool useInlineClose)
        {
            var release = new TaskCompletionSource<Stream>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var stream = new MemoryStream(new byte[4], writable: true);
            Mock<IFileSystemReadWriteProvider> provider = Provider(stream);
            provider.Setup(value => value.OpenReadWriteAsync(
                    "file", It.IsAny<FileWriteMode>(), It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<Stream>(release.Task));
            await using var handles = new FileHandle(provider.Object, "file") { CloseTimeout = TimeSpan.Zero };
            Task<(ServiceResult Result, uint Handle)> open = handles.OpenAsync(s_session, 7, CancellationToken.None)
                .AsTask();
            try
            {
                if (useInlineClose)
                {
                    handles.CloseSession(s_session, defer: false);
                }
                else
                {
                    await handles.CloseSessionAsync(s_session).ConfigureAwait(false);
                }
                Assert.That(handles.TryBlockOpens(), Is.False);
                (ServiceResult blocked, _) = await handles.OpenAsync(s_other, 7, CancellationToken.None)
                    .ConfigureAwait(false);
                Assert.That(blocked.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
                provider.Verify(value => value.OpenReadWriteAsync(
                    "file", It.IsAny<FileWriteMode>(), It.IsAny<CancellationToken>()), Times.Once);
            }
            finally
            {
                release.TrySetResult(stream);
            }
            (ServiceResult result, uint id) = await open.ConfigureAwait(false);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadSessionClosed));
            Assert.That(id, Is.Zero);
            Assert.That(stream.CanRead, Is.False);
            Assert.That(handles.TryBlockOpens(), Is.True);
            handles.UnblockOpens();
        }

        /// <summary>
        /// Teardown attempts every stream and retains every disposal failure for asynchronous observation.
        /// </summary>
        [Test]
        public async Task ShutdownAttemptsAllStreamsAndSurfacesAllCleanupErrorsAsync()
        {
            using var first = new FailingDisposeStream();
            using var second = new FailingDisposeStream();
            var provider = new Mock<IFileSystemProvider>();
            provider.Setup(value => value.OpenReadAsync("file", It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<Stream>(first));
            var handles = new FileHandle(provider.Object, "file");
            await handles.OpenAsync(s_session, 1, CancellationToken.None).ConfigureAwait(false);
            provider.Setup(value => value.OpenReadAsync("file", It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<Stream>(second));
            await handles.OpenAsync(s_other, 1, CancellationToken.None).ConfigureAwait(false);
            AggregateException? error = await Assert.ThrowsAsync<AggregateException>(async () =>
                await handles.DisposeAsync().ConfigureAwait(false));
            Assert.That(error!.Flatten().InnerExceptions, Has.Count.EqualTo(2));
            Assert.That(first.DisposeAttempts, Is.EqualTo(1));
            Assert.That(second.DisposeAttempts, Is.EqualTo(1));
            Assert.That(handles.TryBlockOpens(), Is.False,
                "A failed Dispose does not establish that the provider released the underlying file.");
        }

        /// <summary>
        /// Cancelling a queued operation removes only its admission and never releases the active cursor owner.
        /// </summary>
        [Test]
        public async Task QueuedCancellationDoesNotReleaseActiveStreamAsync()
        {
            using var stream = new MemoryStream(new byte[4], writable: true);
            await using var handles = new FileHandle(Provider(stream).Object, "file");
            (_, uint id) = await handles.OpenAsync(s_session, 3, CancellationToken.None).ConfigureAwait(false);
            FileHandle.Operation first = (await handles.AcquireAsync(s_session, id, 2, CancellationToken.None)
                .ConfigureAwait(false))!;
            using var cancellation = new CancellationTokenSource();
            Task<FileHandle.Operation?> queued = handles.AcquireAsync(s_session, id, 1, cancellation.Token).AsTask();
            cancellation.Cancel();
            await Assert.CatchAsync<OperationCanceledException>(async () => await queued.ConfigureAwait(false));
            Task<bool> close = handles.CloseAsync(s_session, id).AsTask();
            try
            {
                Assert.That(close.IsCompleted, Is.False);
                Assert.That(stream.CanRead, Is.True);
            }
            finally
            {
                first.Dispose();
            }
            Assert.That(await close.ConfigureAwait(false), Is.True);
            Assert.That(stream.CanRead, Is.False);
        }

        /// <summary>
        /// An opt-in provider that cooperates with cancellation does not resurrect a session-owned open.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task CooperativePendingOpenCancellationReturnsSessionClosedAsync(bool useInlineClose)
        {
            Mock<IFileSystemReadWriteProvider> provider = Provider(Stream.Null);
            provider.Setup(value => value.OpenReadWriteAsync(
                    "file", It.IsAny<FileWriteMode>(), It.IsAny<CancellationToken>()))
                .Returns((string _, FileWriteMode _, CancellationToken ct) =>
                    new ValueTask<Stream>(WaitForCancellationAsync(ct)));
            await using var handles = new FileHandle(provider.Object, "file");
            Task<(ServiceResult Result, uint Handle)> open = handles.OpenAsync(s_session, 3, CancellationToken.None)
                .AsTask();
            if (useInlineClose)
            {
                handles.CloseSession(s_session, defer: false);
            }
            else
            {
                await handles.CloseSessionAsync(s_session).ConfigureAwait(false);
            }
            (ServiceResult result, uint id) = await open.ConfigureAwait(false);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadSessionClosed));
            Assert.That(id, Is.Zero);
            Assert.That(handles.TryBlockOpens(), Is.True);
            handles.UnblockOpens();
        }

        /// <summary>
        /// Admission retries an external session check if a close snapshot passed it before reservation.
        /// </summary>
        [Test]
        public async Task SessionClosureDuringAdmissionCannotPublishLateReservationAsync()
        {
            bool terminated = false;
            FileHandle handles = null!;
            var provider = new Mock<IFileSystemProvider>(MockBehavior.Strict);
            handles = new FileHandle(provider.Object, "file", session =>
            {
                if (!terminated)
                {
                    terminated = true;
                    handles.CloseSession(session);
                    return false;
                }
                return true;
            });
            await using (handles.ConfigureAwait(false))
            {
                (ServiceResult result, uint id) = await handles.OpenAsync(s_session, 1, CancellationToken.None)
                    .ConfigureAwait(false);
                Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadSessionClosed));
                Assert.That(id, Is.Zero);
                provider.VerifyNoOtherCalls();
            }
        }

#if NET5_0_OR_GREATER
        /// <summary>
        /// Stream cleanup itself remains part of the exclusive writer reservation.
        /// </summary>
        [Test]
        public async Task CloseRetainsWriterDuringAsynchronousStreamDisposalAsync()
        {
            using var stream = new SuspendedDisposeStream();
            await using var handles = new FileHandle(Provider(stream).Object, "file");
            (_, uint id) = await handles.OpenAsync(s_session, 3, CancellationToken.None).ConfigureAwait(false);
            Task<bool> close = handles.CloseAsync(s_session, id).AsTask();
            try
            {
                await stream.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                Assert.That(close.IsCompleted, Is.False);
                Assert.That(handles.TryBlockOpens(), Is.False);
                (ServiceResult result, _) = await handles.OpenAsync(s_other, 7, CancellationToken.None)
                    .ConfigureAwait(false);
                Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
            }
            finally
            {
                stream.Release.TrySetResult(true);
            }
            Assert.That(await close.ConfigureAwait(false), Is.True);
            Assert.That(stream.CanRead, Is.False);
        }
#endif

        /// <summary>
        /// Waits until the opening thread blocks while detaching its linked token.
        /// </summary>
        private static async Task WaitForBlockedThreadAsync(Thread thread)
        {
            for (int attempt = 0; attempt < 500; attempt++)
            {
                if ((thread.ThreadState & ThreadState.WaitSleepJoin) != 0)
                {
                    return;
                }
                await Task.Delay(10).ConfigureAwait(false);
            }
            Assert.Fail("The provider-open token was not blocked in detachment.");
        }

        /// <summary>
        /// Suspends a provider open until its cancellation token is requested.
        /// </summary>
        private static async Task<Stream> WaitForCancellationAsync(CancellationToken ct)
        {
            await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false);
            return Stream.Null;
        }

        /// <summary>
        /// Creates an opt-in provider around a caller-controlled stream.
        /// </summary>
        private static Mock<IFileSystemReadWriteProvider> Provider(Stream stream)
        {
            var provider = new Mock<IFileSystemReadWriteProvider>();
            provider.SetupGet(value => value.IsWritable).Returns(true);
            provider.Setup(value => value.OpenReadWriteAsync(
                    "file", It.IsAny<FileWriteMode>(), It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<Stream>(stream));
            return provider;
        }

        /// <summary>
        /// Pauses stream validation on the opening thread.
        /// </summary>
        private sealed class SuspendedSeekStream : MemoryStream
        {
            /// <summary>
            /// Creates a stream controlled by the supplied seek gate.
            /// </summary>
            public SuspendedSeekStream(ManualResetEventSlim release)
            {
                m_release = release;
            }

            /// <summary>
            /// Gets the opening thread after seek validation starts.
            /// </summary>
            public TaskCompletionSource<Thread> SeekEntered { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            /// <summary>
            /// Gets the notification that seek validation is continuing.
            /// </summary>
            public TaskCompletionSource<bool> SeekExited { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            /// <inheritdoc/>
            public override bool CanSeek
            {
                get
                {
                    if (Interlocked.Exchange(ref m_seekStarted, 1) == 0)
                    {
                        SeekEntered.TrySetResult(Thread.CurrentThread);
                        if (!m_release.Wait(TimeSpan.FromSeconds(10)))
                        {
                            throw new TimeoutException("The test did not release seek validation.");
                        }
                        SeekExited.TrySetResult(true);
                    }
                    return base.CanSeek;
                }
            }

            private readonly ManualResetEventSlim m_release;
            private int m_seekStarted;
        }

        /// <summary>
        /// Suspends a real asynchronous read while deliberately ignoring its cancellation token.
        /// </summary>
        private sealed class SuspendedReadStream : MemoryStream
        {
            /// <summary>
            /// Gets the externally completed read result.
            /// </summary>
            public TaskCompletionSource<int> Release { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            /// <summary>
            /// Gets whether stream cleanup has started.
            /// </summary>
            public bool Disposed { get; private set; }

            /// <inheritdoc/>
            public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct) =>
                Release.Task;

            /// <inheritdoc/>
            protected override void Dispose(bool disposing)
            {
                Disposed = true;
                base.Dispose(disposing);
            }
        }

        /// <summary>
        /// Reports one cleanup failure while allowing test fixture cleanup to finish.
        /// </summary>
        private sealed class FailingDisposeStream : MemoryStream
        {
            /// <summary>
            /// Gets the number of attempted disposals.
            /// </summary>
            public int DisposeAttempts { get; private set; }

            /// <inheritdoc/>
            protected override void Dispose(bool disposing)
            {
                DisposeAttempts++;
                base.Dispose(disposing);
                if (DisposeAttempts == 1)
                {
                    throw new IOException("Stream cleanup failed.");
                }
            }
        }

#if NET5_0_OR_GREATER
        /// <summary>
        /// Keeps asynchronous disposal incomplete until the test releases it.
        /// </summary>
        private sealed class SuspendedDisposeStream : MemoryStream
        {
            /// <summary>
            /// Gets the notification that asynchronous stream cleanup has started.
            /// </summary>
            public TaskCompletionSource<bool> Entered { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            /// <summary>
            /// Gets the externally controlled cleanup completion.
            /// </summary>
            public TaskCompletionSource<bool> Release { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            /// <inheritdoc/>
            public override async ValueTask DisposeAsync()
            {
                try
                {
                    Entered.TrySetResult(true);
                    await Release.Task.ConfigureAwait(false);
                }
                finally
                {
                    await base.DisposeAsync().ConfigureAwait(false);
                }
            }
        }
#endif

        /// <summary>Identifies the owning session.</summary>
        private static readonly NodeId s_session = new("owner", 1);
        /// <summary>Identifies an unrelated session.</summary>
        private static readonly NodeId s_other = new("other", 1);
    }
}
