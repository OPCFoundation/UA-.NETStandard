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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Server.FileSystem;
using Opc.Ua.Server.Tests.NodeManager;

namespace Opc.Ua.Server.Tests.FileSystem
{
    /// <summary>
    /// Exercises bounded content modes and session closure during asynchronous publication.
    /// </summary>
    [TestFixture]
    [Category("FileSystem")]
    public sealed class FileTypeBinderLifetimeTests
    {
        /// <summary>
        /// Bounded combined opens retain one cursor and implement erase, append, and subsequent seeking.
        /// </summary>
        [TestCase((byte)3, 0ul, 3ul)]
        [TestCase((byte)7, 0ul, 0ul)]
        [TestCase((byte)11, 3ul, 3ul)]
        [TestCase((byte)15, 0ul, 0ul)]
        public async Task CombinedModesShareCursorAndHonorPositioningAsync(byte mode, ulong position, ulong size)
        {
            await using var harness = new Harness();
            await harness.BindAsync().ConfigureAwait(false);
            uint id = await harness.Binding.OpenAsync(harness.Session.Object.Id, mode).ConfigureAwait(false);
            (_, List<Variant> before) = await harness.CallAsync("GetPosition", [id]).ConfigureAwait(false);
            Assert.That(before[0].TryGetValue(out ulong actualPosition), Is.True);
            Assert.That(actualPosition, Is.EqualTo(position));
            await harness.CallGoodAsync("Write", [id, ByteString.From([9])]).ConfigureAwait(false);
            await harness.CallGoodAsync("SetPosition", [id, 0ul]).ConfigureAwait(false);
            (_, List<Variant> read) = await harness.CallAsync("Read", [id, 4]).ConfigureAwait(false);
            Assert.That(read[0].TryGetValue(out ByteString content), Is.True);
            byte[] expected = mode == 11 ? [1, 2, 3, 9] : size == 0 ? [9] : [9, 2, 3];
            Assert.That(content.ToArray(), Is.EqualTo(expected));
            await harness.CallGoodAsync("SetPosition", [id, ulong.MaxValue]).ConfigureAwait(false);
            (_, List<Variant> after) = await harness.CallAsync("GetPosition", [id]).ConfigureAwait(false);
            Assert.That(after[0].TryGetValue(out ulong end), Is.True);
            Assert.That(end, Is.EqualTo((ulong)expected.Length));
        }

        /// <summary>
        /// Accepted access bits, not memory-stream capabilities, authorize bounded file operations.
        /// </summary>
        [TestCase((byte)1)]
        [TestCase((byte)2)]
        public async Task AcceptedModeControlsBoundedReadWriteRightsAsync(byte mode)
        {
            await using var harness = new Harness();
            await harness.BindAsync().ConfigureAwait(false);
            uint id = await harness.Binding.OpenAsync(harness.Session.Object.Id, mode).ConfigureAwait(false);
            (ServiceResult result, _) = mode == 1
                ? await harness.CallAsync("Write", [id, ByteString.From([9])]).ConfigureAwait(false)
                : await harness.CallAsync("Read", [id, 1]).ConfigureAwait(false);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
            (_, List<Variant> output) = await harness.CallAsync("GetPosition", [id]).ConfigureAwait(false);
            Assert.That(output[0].TryGetValue(out ulong position), Is.True);
            Assert.That(position, Is.Zero);
        }

        /// <summary>
        /// Result-only bindings remain read-only when no publication callback is supplied.
        /// </summary>
        [TestCase((byte)2)]
        [TestCase((byte)3)]
        [TestCase((byte)7)]
        public async Task NoCommitCallbackLeavesBindingReadOnlyAsync(byte mode)
        {
            await using var harness = new Harness();
            await harness.BindAsync(writable: false).ConfigureAwait(false);
            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await harness.Binding.OpenAsync(harness.Session.Object.Id, mode).ConfigureAwait(false));
            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
            Assert.That(harness.File.Writable!.Value, Is.False);
            Assert.That(harness.File.OpenCount!.Value, Is.Zero);
        }

        /// <summary>
        /// Session cleanup discards a buffer without starting an ordinary commit-on-close transaction.
        /// </summary>
        [Test]
        public async Task SessionClosureDoesNotStartUnrequestedCommitAsync()
        {
            await using var harness = new Harness();
            int calls = 0;
            await harness.BindAsync(commitOnClose: true, commit: (_, _) =>
            {
                calls++;
                return default;
            }).ConfigureAwait(false);
            uint id = await harness.Binding.OpenAsync(harness.Session.Object.Id, 7).ConfigureAwait(false);
            await harness.CallGoodAsync("Write", [id, ByteString.From([9])]).ConfigureAwait(false);
            harness.CloseSession();
            Assert.That(calls, Is.Zero);
            ServiceResultException? closed = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await harness.Binding.CloseAsync(harness.Session.Object.Id, id).ConfigureAwait(false));
            Assert.That(closed!.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
            Assert.That(harness.File.OpenCount!.Value, Is.Zero);
            harness.Context.SessionId = harness.OtherSession.Object.Id;
            uint reader = await harness.Binding.OpenAsync(harness.OtherSession.Object.Id, 1).ConfigureAwait(false);
            (_, List<Variant> output) = await harness.CallAsync("Read", [reader, 4]).ConfigureAwait(false);
            Assert.That(output[0].TryGetValue(out ByteString content), Is.True);
            Assert.That(content.ToArray(), Is.EqualTo(s_initialContent));
        }

        /// <summary>
        /// An idle bounded writer releases exclusivity before queued lifecycle continuations run.
        /// </summary>
        [Test]
        public async Task SessionClosureReleasesIdleWriterBeforeDeferredCallbacksAsync()
        {
            await using var harness = new Harness();
            bool committed = false;
            await harness.BindAsync(commitOnClose: true, commit: (_, _) =>
            {
                committed = true;
                return default;
            }).ConfigureAwait(false);
            uint writer = await harness.Binding.OpenAsync(harness.Session.Object.Id, 6).ConfigureAwait(false);
            await harness.CallGoodAsync("Write", [writer, ByteString.From([9])]).ConfigureAwait(false);
            var scheduler = new DeferredSynchronizationContext();
            SynchronizationContext? previous = SynchronizationContext.Current;
            try
            {
                try
                {
                    SynchronizationContext.SetSynchronizationContext(scheduler);
                    harness.CloseSession();
                }
                finally
                {
                    SynchronizationContext.SetSynchronizationContext(previous);
                }
                Assert.That(harness.File.OpenCount!.Value, Is.Zero);
                Assert.That(committed, Is.False);
                (ServiceResult stale, _) = await harness.CallAsync("Read", [writer, 1]).ConfigureAwait(false);
                Assert.That(stale.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
                harness.Context.SessionId = harness.OtherSession.Object.Id;
                uint reader = await harness.Binding.OpenAsync(harness.OtherSession.Object.Id, 1).ConfigureAwait(false);
                (_, List<Variant> output) = await harness.CallAsync("Read", [reader, 4]).ConfigureAwait(false);
                Assert.That(output[0].TryGetValue(out ByteString content), Is.True);
                Assert.That(content.ToArray(), Is.EqualTo(s_initialContent));
                await harness.Binding.CloseAsync(harness.OtherSession.Object.Id, reader).ConfigureAwait(false);
            }
            finally
            {
                scheduler.RunPostedCallbacks();
            }
        }

        /// <summary>
        /// A failed, completed commit detaches its callback token before the remaining idle handle closes.
        /// </summary>
        [Test]
        public async Task CompletedFailedCommitDetachesCallbackCancellationAsync()
        {
            await using var harness = new Harness();
            CancellationToken callbackToken = default;
            CancellationTokenRegistration registration = default;
            bool cancelled = false;
            await harness.BindAsync(commit: (_, token) =>
            {
                callbackToken = token;
                registration = token.Register(() => cancelled = true);
                throw new IOException("Publication failed.");
            }).ConfigureAwait(false);
            try
            {
                uint writer = await harness.Binding.OpenAsync(harness.Session.Object.Id, 7).ConfigureAwait(false);
                await Assert.ThrowsAsync<IOException>(async () =>
                    await harness.Binding.CommitAsync(harness.Session.Object.Id, writer).ConfigureAwait(false));
                harness.CloseSession();
                Assert.That(callbackToken.IsCancellationRequested, Is.False);
                Assert.That(cancelled, Is.False);
                harness.Context.SessionId = harness.OtherSession.Object.Id;
                uint reader = await harness.Binding.OpenAsync(harness.OtherSession.Object.Id, 1).ConfigureAwait(false);
                await harness.Binding.CloseAsync(harness.OtherSession.Object.Id, reader).ConfigureAwait(false);
            }
            finally
            {
                registration.Dispose();
            }
        }

        /// <summary>
        /// A running publication is cancelled on session closure but retains exclusivity until its actual outcome.
        /// </summary>
        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public async Task SessionClosureTracksRunningCommitWithoutUndoingPublicationAsync(
            bool commitOnClose, bool publishDespiteCancellation)
        {
            await using var harness = new Harness();
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var cancelled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            ByteString published = default;
            await harness.BindAsync(commitOnClose: commitOnClose, commit: async (bytes, ct) =>
            {
                using CancellationTokenRegistration registration = ct.Register(() => cancelled.TrySetResult(true));
                entered.TrySetResult(true);
                await release.Task.ConfigureAwait(false);
                if (!publishDespiteCancellation)
                {
                    ct.ThrowIfCancellationRequested();
                }
                published = bytes;
            }).ConfigureAwait(false);
            uint id = await harness.Binding.OpenAsync(harness.Session.Object.Id, 7).ConfigureAwait(false);
            await harness.CallGoodAsync("Write", [id, ByteString.From([4, 5])]).ConfigureAwait(false);
            Task commit = commitOnClose
                ? harness.Binding.CloseAsync(harness.Session.Object.Id, id).AsTask()
                : harness.Binding.CommitAsync(harness.Session.Object.Id, id).AsTask();
            try
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                harness.CloseSession();
                await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                Assert.That(commit.IsCompleted, Is.False);
                ServiceResultException? blocked = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await harness.Binding.OpenAsync(harness.OtherSession.Object.Id, 3).ConfigureAwait(false));
                Assert.That(blocked!.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
                Assert.That(published.IsNull, Is.True);
            }
            finally
            {
                release.TrySetResult(true);
            }
            if (publishDespiteCancellation)
            {
                await commit.ConfigureAwait(false);
                Assert.That(published.ToArray(), Is.EqualTo(s_committedContent));
            }
            else
            {
                await Assert.ThrowsAsync<OperationCanceledException>(async () => await commit.ConfigureAwait(false));
                Assert.That(published.IsNull, Is.True);
            }
            harness.Context.SessionId = harness.OtherSession.Object.Id;
            uint reader = await harness.Binding.OpenAsync(harness.OtherSession.Object.Id, 1).ConfigureAwait(false);
            (_, List<Variant> output) = await harness.CallAsync("Read", [reader, 4]).ConfigureAwait(false);
            Assert.That(output[0].TryGetValue(out ByteString content), Is.True);
            Assert.That(content.ToArray(),
                Is.EqualTo(publishDespiteCancellation ? s_committedContent : s_initialContent));
        }

        /// <summary>
        /// Combined writes obey the exact capacity boundary and foreign sessions cannot use the handle.
        /// </summary>
        [Test]
        public async Task CombinedWriterPreservesBoundsAndSessionOwnershipAsync()
        {
            await using var harness = new Harness();
            await harness.BindAsync().ConfigureAwait(false);
            uint id = await harness.Binding.OpenAsync(harness.Session.Object.Id, 7).ConfigureAwait(false);
            await harness.CallGoodAsync("Write", [id, ByteString.From([1, 2, 3, 4])]).ConfigureAwait(false);
            (ServiceResult overflow, _) = await harness.CallAsync("Write", [id, ByteString.From([5])])
                .ConfigureAwait(false);
            Assert.That(overflow.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
            harness.Context.SessionId = harness.OtherSession.Object.Id;
            (ServiceResult foreign, _) = await harness.CallAsync("GetPosition", [id]).ConfigureAwait(false);
            Assert.That(foreign.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
            harness.Context.SessionId = harness.Session.Object.Id;
            (_, List<Variant> output) = await harness.CallAsync("GetPosition", [id]).ConfigureAwait(false);
            Assert.That(output[0].TryGetValue(out ulong position), Is.True);
            Assert.That(position, Is.EqualTo(4));
        }

        /// <summary>
        /// The owner-session restriction applies before any handle is allocated.
        /// </summary>
        [Test]
        public async Task OwnerRestrictedBindingRejectsForeignOpenAsync()
        {
            await using var harness = new Harness();
            await harness.BindAsync(ownerSession: harness.Session.Object.Id).ConfigureAwait(false);
            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await harness.Binding.OpenAsync(harness.OtherSession.Object.Id, 1).ConfigureAwait(false));
            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
            Assert.That(harness.File.OpenCount!.Value, Is.Zero);
            await harness.Binding.OpenAsync(harness.Session.Object.Id, 1).ConfigureAwait(false);
            Assert.That(harness.File.OpenCount!.Value, Is.EqualTo(1));
        }

        /// <summary>
        /// Direct opens require a live registered owner even before the binding observes a close event.
        /// </summary>
        [TestCase("absent")]
        [TestCase("closing")]
        [TestCase("expired")]
        public async Task DirectOpenRejectsUnavailableSessionBeforeFirstOpenAsync(string state)
        {
            await using var harness = new Harness();
            await harness.BindAsync().ConfigureAwait(false);
            if (state == "absent")
            {
                harness.Sessions.Setup(value => value.GetSessions()).Returns(() => [harness.OtherSession.Object]);
            }
            else if (state == "closing")
            {
                Assert.That(SessionTermination.TryClaimClose(harness.Session.Object), Is.True);
            }
            else
            {
                harness.Session.SetupGet(value => value.HasExpired).Returns(true);
            }

            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await harness.Binding.OpenAsync(harness.Session.Object.Id, 1).ConfigureAwait(false));
            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadSessionClosed));
            Assert.That(harness.File.OpenCount!.Value, Is.Zero);
            uint surviving = await harness.Binding.OpenAsync(harness.OtherSession.Object.Id, 1).ConfigureAwait(false);
            Assert.That(harness.File.OpenCount.Value, Is.EqualTo(1));
            await harness.Binding.CloseAsync(harness.OtherSession.Object.Id, surviving).ConfigureAwait(false);
            Assert.That(harness.File.OpenCount.Value, Is.Zero);
            Assert.That(harness.File.Size!.Value, Is.EqualTo((ulong)s_initialContent.Length));
        }

        /// <summary>
        /// A failed explicit commit leaves the writer open for a retry, preserving the existing transaction contract.
        /// </summary>
        [Test]
        public async Task FailedExplicitCommitPreservesWriterForRetryAsync()
        {
            await using var harness = new Harness();
            int calls = 0;
            ByteString published = default;
            await harness.BindAsync(commit: (bytes, _) =>
            {
                if (++calls == 1)
                {
                    throw new IOException("Publication failed.");
                }
                published = bytes;
                return default;
            }).ConfigureAwait(false);
            uint id = await harness.Binding.OpenAsync(harness.Session.Object.Id, 7).ConfigureAwait(false);
            await harness.CallGoodAsync("Write", [id, ByteString.From([4])]).ConfigureAwait(false);
            await Assert.ThrowsAsync<IOException>(async () =>
                await harness.Binding.CommitAsync(harness.Session.Object.Id, id).ConfigureAwait(false));
            Assert.That(harness.File.OpenCount!.Value, Is.EqualTo(1));
            await harness.CallGoodAsync("Write", [id, ByteString.From([5])]).ConfigureAwait(false);
            await harness.Binding.CommitAsync(harness.Session.Object.Id, id).ConfigureAwait(false);
            Assert.That(published.ToArray(), Is.EqualTo(s_committedContent));
            Assert.That(calls, Is.EqualTo(2));
            Assert.That(harness.File.OpenCount.Value, Is.Zero);
        }

        /// <summary>
        /// Close joins an admitted publication rather than publishing twice or releasing its writer early.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task ExplicitCloseDrainsAlreadyRunningCommitAsync(bool commitOnClose)
        {
            await using var harness = new Harness();
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            int calls = 0;
            await harness.BindAsync(commitOnClose: commitOnClose, commit: async (_, ct) =>
            {
                calls++;
                entered.TrySetResult(true);
                await release.Task.WaitAsync(ct).ConfigureAwait(false);
            }).ConfigureAwait(false);
            uint id = await harness.Binding.OpenAsync(harness.Session.Object.Id, 7).ConfigureAwait(false);
            Task commit = harness.Binding.CommitAsync(harness.Session.Object.Id, id).AsTask();
            Task? close = null;
            try
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                close = harness.Binding.CloseAsync(harness.Session.Object.Id, id).AsTask();
                Assert.That(close.IsCompleted, Is.False);
                Assert.That(harness.File.OpenCount!.Value, Is.EqualTo(1));
            }
            finally
            {
                release.TrySetResult(true);
                await commit.ConfigureAwait(false);
                if (close != null)
                {
                    await close.ConfigureAwait(false);
                }
            }
            Assert.That(calls, Is.EqualTo(1));
            Assert.That(harness.File.OpenCount!.Value, Is.Zero);
        }

        /// <summary>
        /// Binding disposal requests publication cancellation and drains its actual outcome before releasing state.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task BindingDisposalCancelsAndDrainsRunningCommitAsync(bool publishDespiteCancellation)
        {
            await using var harness = new Harness();
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var cancelled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            ByteString published = default;
            await harness.BindAsync(commit: async (bytes, ct) =>
            {
                using CancellationTokenRegistration registration = ct.Register(() => cancelled.TrySetResult(true));
                entered.TrySetResult(true);
                await release.Task.ConfigureAwait(false);
                if (!publishDespiteCancellation)
                {
                    ct.ThrowIfCancellationRequested();
                }
                published = bytes;
            }).ConfigureAwait(false);
            uint id = await harness.Binding.OpenAsync(harness.Session.Object.Id, 7).ConfigureAwait(false);
            await harness.CallGoodAsync("Write", [id, ByteString.From([4, 5])]).ConfigureAwait(false);
            Task commit = harness.Binding.CommitAsync(harness.Session.Object.Id, id).AsTask();
            Task? disposal = null;
            try
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                disposal = harness.Binding.DisposeAsync().AsTask();
                await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                Assert.That(commit.IsCompleted, Is.False);
                Assert.That(disposal.IsCompleted, Is.False);
                Assert.That(published.IsNull, Is.True);
            }
            finally
            {
                release.TrySetResult(true);
                if (publishDespiteCancellation)
                {
                    await commit.ConfigureAwait(false);
                }
                else
                {
                    ServiceResultException? failure = await Assert.ThrowsAsync<ServiceResultException>(
                        async () => await commit.ConfigureAwait(false));
                    Assert.That(failure!.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
                }
                if (disposal != null)
                {
                    await disposal.ConfigureAwait(false);
                }
            }
            if (publishDespiteCancellation)
            {
                Assert.That(published.ToArray(), Is.EqualTo(s_committedContent));
            }
            else
            {
                Assert.That(published.IsNull, Is.True);
            }
            Assert.That(harness.File.Size!.Value, Is.Zero);
            Assert.That(harness.File.OpenCount!.Value, Is.Zero);
        }

        /// <summary>
        /// Shutdown retains callback failures, including a status identical to its own cancellation mapping.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task BindingDisposalReportsCommitProviderFailureAsync(bool statusFailure)
        {
            var harness = new Harness();
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var cancelled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Exception publicationFailure = statusFailure
                ? new ServiceResultException(StatusCodes.BadInvalidState, "The file binding is being disposed.")
                : new IOException("Publication failed.");
            Task? commit = null;
            Task? disposal = null;
            try
            {
                await harness.BindAsync(commit: async (_, ct) =>
                {
                    using CancellationTokenRegistration registration = ct.Register(() => cancelled.TrySetResult(true));
                    entered.TrySetResult(true);
                    await release.Task.ConfigureAwait(false);
                    throw publicationFailure;
                }).ConfigureAwait(false);
                uint id = await harness.Binding.OpenAsync(harness.Session.Object.Id, 7).ConfigureAwait(false);
                commit = harness.Binding.CommitAsync(harness.Session.Object.Id, id).AsTask();
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                disposal = harness.DisposeAsync().AsTask();
                await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                Assert.That(commit.IsCompleted, Is.False);
                Assert.That(disposal.IsCompleted, Is.False);
            }
            finally
            {
                release.TrySetResult(true);
                try
                {
                    if (commit != null)
                    {
                        Exception? failure = statusFailure
                            ? await Assert.ThrowsAsync<ServiceResultException>(
                                async () => await commit.ConfigureAwait(false))
                            : await Assert.ThrowsAsync<IOException>(
                                async () => await commit.ConfigureAwait(false));
                        Assert.That(failure, Is.SameAs(publicationFailure));
                    }
                }
                finally
                {
                    if (disposal != null)
                    {
                        AggregateException? failure = await Assert.ThrowsAsync<AggregateException>(
                            async () => await disposal.ConfigureAwait(false));
                        Assert.That(failure!.Flatten().InnerExceptions, Has.Count.EqualTo(1));
                        Assert.That(failure.Flatten().InnerExceptions[0], Is.SameAs(publicationFailure));
                    }
                    else
                    {
                        await harness.DisposeAsync().ConfigureAwait(false);
                    }
                }
            }
            Assert.That(harness.File.Size!.Value, Is.Zero);
            Assert.That(harness.File.OpenCount!.Value, Is.Zero);
        }

        /// <summary>Initial content retained when a transaction does not publish.</summary>
        private static readonly byte[] s_initialContent = [1, 2, 3];

        /// <summary>Content published by successful callback cases.</summary>
        private static readonly byte[] s_committedContent = [4, 5];

        /// <summary>
        /// Holds queued lifecycle continuations until the test explicitly admits them.
        /// </summary>
        private sealed class DeferredSynchronizationContext : SynchronizationContext
        {
            /// <inheritdoc/>
            public override void Post(SendOrPostCallback callback, object? state)
            {
                m_callbacks.Enqueue(() => callback(state));
            }

            /// <summary>
            /// Runs all queued callbacks so failed assertions cannot strand binding disposal.
            /// </summary>
            public void RunPostedCallbacks()
            {
                while (m_callbacks.TryDequeue(out Action? callback))
                {
                    callback();
                }
            }

            /// <summary>Retains callbacks in admission order.</summary>
            private readonly ConcurrentQueue<Action> m_callbacks = new();
        }

        /// <summary>
        /// Creates a generated FileType and a real bounded binding with a controllable session registry.
        /// </summary>
        private sealed class Harness : IAsyncDisposable
        {
            /// <summary>
            /// Creates two registered sessions and the FileType to be bound.
            /// </summary>
            public Harness()
            {
                Mock<IServerInternal> server = DeterministicServerMock.Create(out m_queues);
                Session.SetupGet(value => value.Id).Returns(new NodeId("owner", 1));
                OtherSession.SetupGet(value => value.Id).Returns(new NodeId("other", 1));
                Sessions.Setup(value => value.GetSessions()).Returns(() => [Session.Object, OtherSession.Object]);
                server.SetupGet(value => value.SessionManager).Returns(Sessions.Object);
                Context = new ServerSystemContext(server.Object) { SessionId = Session.Object.Id };
                File = Context.CreateInstanceOfFileType(new BaseObjectState(null), new QualifiedName("Content", 1));
                File.NodeId = new NodeId("Content", 1);
            }

            /// <summary>
            /// Gets the server context used to invoke generated methods.
            /// </summary>
            public ServerSystemContext Context { get; }

            /// <summary>
            /// Gets the generated file node.
            /// </summary>
            public FileState File { get; }

            /// <summary>
            /// Gets the owning session mock.
            /// </summary>
            public Mock<ISession> Session { get; } = new();

            /// <summary>
            /// Gets a surviving session mock.
            /// </summary>
            public Mock<ISession> OtherSession { get; } = new();

            /// <summary>
            /// Gets the session event source.
            /// </summary>
            public Mock<ISessionManager> Sessions { get; } = new();

            /// <summary>
            /// Gets the initialized binding.
            /// </summary>
            public IFileTypeBinding Binding { get; private set; } = null!;

            /// <summary>
            /// Binds four-byte-bounded content with the requested publication callback.
            /// </summary>
            public ValueTask BindAsync(
                bool writable = true,
                bool commitOnClose = false,
                Func<ByteString, CancellationToken, ValueTask>? commit = null,
                NodeId ownerSession = default)
            {
                Binding = new FileTypeBinder().Bind(File, Context, ByteString.From([1, 2, 3]),
                    writable ? commit ?? ((_, _) => default) : null, commitOnClose, ownerSession, maximumBytes: 4);
                return default;
            }

            /// <summary>
            /// Raises the real session-close notification after marking the owner as closing.
            /// </summary>
            public void CloseSession()
            {
                Session.SetupGet(value => value.IsClosing).Returns(true);
                Sessions.Raise(value => value.SessionClosing += null!, Session.Object, SessionEventReason.Closing);
            }

            /// <summary>
            /// Invokes one bound FileType method through protocol argument validation.
            /// </summary>
            public ValueTask<(ServiceResult Result, List<Variant> Output)> CallAsync(
                string name, ArrayOf<Variant> arguments)
            {
                var method = (MethodState)File.FindChild(Context, new QualifiedName(name))!;
                return FileReadRegressionTests.CallAsync(method, Context, File.NodeId, arguments);
            }

            /// <summary>
            /// Invokes a FileType method and requires successful completion.
            /// </summary>
            public async ValueTask CallGoodAsync(string name, ArrayOf<Variant> arguments)
            {
                (ServiceResult result, _) = await CallAsync(name, arguments).ConfigureAwait(false);
                Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.Good), result.ToString());
            }

            /// <inheritdoc/>
            public async ValueTask DisposeAsync()
            {
                try
                {
                    if (Binding != null)
                    {
                        await Binding.DisposeAsync().ConfigureAwait(false);
                    }
                }
                finally
                {
                    m_queues.Dispose();
                }
            }

            /// <summary>Owns the deterministic monitored-item queues.</summary>
            private readonly MonitoredItemQueueFactory m_queues;
        }
    }
}
