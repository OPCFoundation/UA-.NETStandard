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
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Server.FileSystem;
using Opc.Ua.Server.Tests.NodeManager;

namespace Opc.Ua.Server.Tests.FileSystem
{
    /// <summary>
    /// Exercises owner-session cleanup through the public directory binder and FileType methods.
    /// </summary>
    [TestFixture]
    [Category("FileSystem")]
    public sealed class FileDirectorySessionLifetimeTests
    {
        /// <summary>
        /// A request admitted before session termination cannot acquire a new file after termination starts.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task TerminatedSessionsCannotOpenAnotherFileAsync(bool expired)
        {
            await using var harness = new Harness();
            await harness.InitializeAsync().ConfigureAwait(false);
            harness.Session.SetupGet(value => value.IsClosing).Returns(!expired);
            harness.Session.SetupGet(value => value.HasExpired).Returns(expired);

            OpenMethodStateResult result = await harness.File.Open!.OnCallAsync!(
                harness.Context, harness.File.Open, harness.File.NodeId, 1, CancellationToken.None)
                .ConfigureAwait(false);

            Assert.That(result.ServiceResult.StatusCode, Is.EqualTo(StatusCodes.BadSessionClosed));
            Assert.That(result.FileHandle, Is.Zero);
            harness.Provider.Verify(value => value.OpenReadAsync(
                It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        /// <summary>
        /// Session termination cancels a suspended open; cancellation and disposal preserve their own outcomes.
        /// </summary>
        [TestCase("close")]
        [TestCase("expiry")]
        [TestCase("cancellation")]
        [TestCase("dispose")]
        public async Task PendingOpensReleaseReturnedStreamsWithoutResurrectingOwnershipAsync(string termination)
        {
            await using var harness = new Harness();
            await harness.InitializeAsync().ConfigureAwait(false);
            using var cancellation = new CancellationTokenSource();
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<Stream>(TaskCreationOptions.RunContinuationsAsynchronously);
            harness.Provider.Setup(value => value.OpenReadAsync("program", It.IsAny<CancellationToken>()))
                .Returns(() =>
                {
                    entered.TrySetResult(true);
                    return new ValueTask<Stream>(release.Task);
                });
            Task<OpenMethodStateResult> pending = harness.File.Open!.OnCallAsync!(
                harness.Context, harness.File.Open, harness.File.NodeId, 1, cancellation.Token).AsTask();
            using var stream = new MemoryStream([1, 2, 3], writable: false);
            try
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                if (termination == "cancellation")
                {
                    cancellation.Cancel();
                }
                else if (termination == "dispose")
                {
                    await harness.Binding.DisposeAsync().ConfigureAwait(false);
                }
                else
                {
                    harness.Session.SetupGet(value => value.IsClosing).Returns(true);
                    harness.Session.SetupGet(value => value.HasExpired).Returns(termination == "expiry");
                    harness.Sessions.Raise(value => value.SessionClosing += null!,
                        harness.Session.Object, SessionEventReason.Closing);
                }
            }
            finally
            {
                release.TrySetResult(stream);
            }

            if (termination == "cancellation")
            {
                await Assert.ThrowsAsync<OperationCanceledException>(async () =>
                    await pending.ConfigureAwait(false));
            }
            else
            {
                OpenMethodStateResult result = await pending.ConfigureAwait(false);
                Assert.That(result.ServiceResult.StatusCode, Is.EqualTo(StatusCodes.BadSessionClosed));
                Assert.That(result.FileHandle, Is.Zero);
            }
            Assert.That(stream.CanRead, Is.False);

            if (termination != "dispose")
            {
                harness.Context.SessionId = harness.OtherSession.Object.Id;
                harness.Provider.Setup(value => value.OpenReadAsync("program", It.IsAny<CancellationToken>()))
                    .Returns(() => new ValueTask<Stream>(new MemoryStream([4, 5, 6], writable: false)));
                uint handle = await FileReadRegressionTests.OpenAsync(harness.File, harness.Context)
                    .ConfigureAwait(false);
                ReadMethodStateResult read = await harness.File.Read!.OnCallAsync!(
                    harness.Context, harness.File.Read, harness.File.NodeId, handle, 3, CancellationToken.None)
                    .ConfigureAwait(false);
                Assert.That(read.ServiceResult.StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(read.Data.ToArray(), Is.EqualTo(new byte[] { 4, 5, 6 }));
            }
        }

        /// <summary>
        /// Expiry releases only the expired session and removes the lifecycle subscription on binding disposal.
        /// </summary>
        [Test]
        public async Task ExpiryPreservesOtherSessionAndDisposalDetachesCallbackAsync()
        {
            await using var harness = new Harness();
            await harness.InitializeAsync().ConfigureAwait(false);
            uint expiredHandle = await FileReadRegressionTests.OpenAsync(harness.File, harness.Context)
                .ConfigureAwait(false);
            harness.Context.SessionId = harness.OtherSession.Object.Id;
            uint survivingHandle = await FileReadRegressionTests.OpenAsync(harness.File, harness.Context)
                .ConfigureAwait(false);
            harness.Session.SetupGet(value => value.IsClosing).Returns(true);
            harness.Session.SetupGet(value => value.HasExpired).Returns(true);

            harness.Sessions.Raise(value => value.SessionClosing += null!,
                harness.Session.Object, SessionEventReason.Closing);

            harness.Context.SessionId = harness.Session.Object.Id;
            ReadMethodStateResult expired = await harness.File.Read!.OnCallAsync!(
                harness.Context, harness.File.Read, harness.File.NodeId, expiredHandle, 1, CancellationToken.None)
                .ConfigureAwait(false);
            Assert.That(expired.ServiceResult.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
            harness.Context.SessionId = harness.OtherSession.Object.Id;
            ReadMethodStateResult surviving = await harness.File.Read!.OnCallAsync!(
                harness.Context, harness.File.Read, harness.File.NodeId, survivingHandle, 3, CancellationToken.None)
                .ConfigureAwait(false);
            Assert.That(surviving.ServiceResult.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(surviving.Data.ToArray(), Is.EqualTo(new byte[] { 1, 2, 3 }));
            await harness.Binding.DisposeAsync().ConfigureAwait(false);
            harness.Sessions.VerifyRemove(value => value.SessionClosing -= It.IsAny<SessionEventHandler>(), Times.Once);
        }

        /// <summary>
        /// Owns a directory binding with a controllable server session lifecycle.
        /// </summary>
        private sealed class Harness : IAsyncDisposable
        {
            /// <summary>
            /// Creates two independent sessions and a single provider-backed file.
            /// </summary>
            public Harness()
            {
                Mock<IServerInternal> server = DeterministicServerMock.Create(out m_queues);
                Session.SetupGet(value => value.Id).Returns(new NodeId("closing", 1));
                OtherSession.SetupGet(value => value.Id).Returns(new NodeId("surviving", 1));
                Sessions.Setup(value => value.GetSessions()).Returns(() => [Session.Object, OtherSession.Object]);
                server.SetupGet(value => value.SessionManager).Returns(Sessions.Object);
                Context = new ServerSystemContext(server.Object) { SessionId = Session.Object.Id };
                Root = Context.CreateInstanceOfFileDirectoryType(
                    new BaseObjectState(null), new QualifiedName("Files", 1));
                Root.NodeId = new NodeId("Files", 1);
                Provider.SetupGet(value => value.MountName).Returns("Files");
                Provider.SetupGet(value => value.IsWritable).Returns(false);
                Provider.Setup(value => value.EnumerateAsync(string.Empty, It.IsAny<CancellationToken>()))
                    .Returns((string _, CancellationToken ct) => EnumerateAsync(ct));
                Provider.Setup(value => value.GetEntryAsync("program", It.IsAny<CancellationToken>()))
                    .ReturnsAsync(s_entry);
                Provider.Setup(value => value.OpenReadAsync("program", It.IsAny<CancellationToken>()))
                    .Returns(() => new ValueTask<Stream>(new MemoryStream([1, 2, 3], writable: false)));
            }

            /// <summary>
            /// Gets the calling context used by FileType methods.
            /// </summary>
            public ServerSystemContext Context { get; }

            /// <summary>
            /// Gets the directory being bound.
            /// </summary>
            public FileDirectoryState Root { get; }

            /// <summary>
            /// Gets the terminated session.
            /// </summary>
            public Mock<ISession> Session { get; } = new();

            /// <summary>
            /// Gets the independent surviving session.
            /// </summary>
            public Mock<ISession> OtherSession { get; } = new();

            /// <summary>
            /// Gets the lifecycle source used by the real binder.
            /// </summary>
            public Mock<ISessionManager> Sessions { get; } = new();

            /// <summary>
            /// Gets the controllable storage provider.
            /// </summary>
            public Mock<IFileSystemProvider> Provider { get; } = new(MockBehavior.Strict);

            /// <summary>
            /// Gets the materialized file.
            /// </summary>
            public FileState File { get; private set; } = null!;

            /// <summary>
            /// Gets the active directory binding.
            /// </summary>
            public IFileDirectoryBinding Binding { get; private set; } = null!;

            /// <summary>
            /// Materializes the provider through the public binder.
            /// </summary>
            public async ValueTask InitializeAsync()
            {
                Binding = await new FileDirectoryBinder().BindAsync(Root, Provider.Object, Context)
                    .ConfigureAwait(false);
                var children = new List<BaseInstanceState>();
                Root.GetChildren(Context, children);
                File = (FileState)children.Find(static child => child is FileState)!;
            }

            /// <inheritdoc/>
            public async ValueTask DisposeAsync()
            {
                if (Binding != null)
                {
                    await Binding.DisposeAsync().ConfigureAwait(false);
                }
                m_queues.Dispose();
            }

            /// <summary>
            /// Enumerates the fixture's immutable file.
            /// </summary>
            private static async IAsyncEnumerable<FileSystemEntry> EnumerateAsync(
                [EnumeratorCancellation] CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.CompletedTask.ConfigureAwait(false);
                yield return s_entry;
            }

            /// <summary>Queue resources owned by the test server context.</summary>
            private readonly MonitoredItemQueueFactory m_queues;
        }

        /// <summary>The fixture file's metadata.</summary>
        private static readonly FileSystemEntry s_entry = new(
            "program", "program", false, 3, false,
            new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc), "application/octet-stream");
    }
}
