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
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Server.FileSystem;
using Opc.Ua.Server.Tests.NodeManager;

namespace Opc.Ua.Server.Tests.FileSystem
{
    /// <summary>
    /// Verifies file-open reservations, per-handle access modes, and cleanup of interrupted provider opens.
    /// </summary>
    [TestFixture]
    [Category("FileSystem")]
    public sealed class FileOpenOwnershipRegressionTests
    {
        /// <summary>
        /// Read with Append starts at EOF, reports that position, and permits rewinding without opening for write.
        /// </summary>
        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public async Task ReadAppendReportsEofAndReadsAfterRewindAsync(bool synchronousOpen, bool emptyFile)
        {
            using var harness = new FileHarness();
            byte[] content = emptyFile ? [] : [10, 20, 30, 40];
            harness.Provider.SetupGet(value => value.IsWritable).Returns(false);
            harness.Provider.Setup(value => value.OpenReadAsync("file", It.IsAny<CancellationToken>()))
                .Returns(() => new ValueTask<Stream>(new MemoryStream(content, writable: false)));
            uint id;
            if (synchronousOpen)
            {
                ServiceResult opened = harness.GetHandle().Open(harness.SessionId, 0x09, out id);
                Assert.That(opened.StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(id, Is.Not.Zero);
            }
            else
            {
                id = await FileReadRegressionTests.OpenAsync(
                    harness.File, harness.Manager.SystemContext, 0x09).ConfigureAwait(false);
            }
            Assert.That(harness.GetHandle().OpenCount, Is.EqualTo(1));
            (ServiceResult positionResult, List<Variant> positionOutput) = await FileReadRegressionTests.CallAsync(
                harness.File.GetPosition!, harness.Manager.SystemContext, harness.File.NodeId, [id])
                .ConfigureAwait(false);
            Assert.That(positionResult.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(positionOutput, Has.Count.EqualTo(1));
            Assert.That(positionOutput[0].TryGetValue(out ulong position), Is.True);
            Assert.That(position, Is.EqualTo((ulong)content.Length));

            (ServiceResult eofResult, List<Variant> eofOutput) = await FileReadRegressionTests.CallAsync(
                harness.File.Read!, harness.Manager.SystemContext, harness.File.NodeId, [id, 4])
                .ConfigureAwait(false);
            Assert.That(eofResult.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(eofOutput, Has.Count.EqualTo(1));
            Assert.That(eofOutput[0].TryGetValue(out ByteString eof), Is.True);
            Assert.That(eof.IsEmpty, Is.True);

            (ServiceResult rewindResult, _) = await FileReadRegressionTests.CallAsync(
                harness.File.SetPosition!, harness.Manager.SystemContext, harness.File.NodeId, [id, 0ul])
                .ConfigureAwait(false);
            Assert.That(rewindResult.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(harness.GetHandle().GetStream(harness.SessionId, id)!.Position, Is.Zero);
            (ServiceResult readResult, List<Variant> readOutput) = await FileReadRegressionTests.CallAsync(
                harness.File.Read!, harness.Manager.SystemContext, harness.File.NodeId, [id, 4])
                .ConfigureAwait(false);
            Assert.That(readResult.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(readOutput, Has.Count.EqualTo(1));
            Assert.That(readOutput[0].TryGetValue(out ByteString data), Is.True);
            Assert.That(data, Is.EqualTo(ByteString.From(content.AsSpan())));
            harness.Provider.Verify(value => value.OpenWriteAsync(
                It.IsAny<string>(), It.IsAny<FileWriteMode>(), It.IsAny<CancellationToken>()), Times.Never);
            Assert.That(await harness.GetHandle().CloseAsync(harness.SessionId, id).ConfigureAwait(false), Is.True);
            Assert.That(harness.GetHandle().OpenCount, Is.Zero);
        }

        /// <summary>
        /// Failed EOF positioning disposes the stream without retaining its open reservation.
        /// </summary>
        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public async Task ReadAppendPositionFailureReleasesStreamAndReservationAsync(
            bool synchronousOpen, bool seekFailure)
        {
            using var harness = new FileHarness();
            var stream = new Mock<MemoryStream> { CallBase = true };
            if (seekFailure)
            {
                stream.Setup(value => value.Seek(0, SeekOrigin.End)).Throws(new IOException("seek failed"));
            }
            else
            {
                stream.SetupGet(value => value.CanSeek).Returns(false);
            }
            harness.Provider.Setup(value => value.OpenReadAsync("file", It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<Stream>(stream.Object));

            ServiceResult result;
            uint id;
            if (synchronousOpen)
            {
                result = harness.GetHandle().Open(harness.SessionId, 0x09, out id);
            }
            else
            {
                (result, id) = await harness.GetHandle()
                    .OpenAsync(harness.SessionId, 0x09, CancellationToken.None).ConfigureAwait(false);
            }

            Assert.That(result.StatusCode,
                Is.EqualTo(seekFailure ? StatusCodes.BadInvalidState : StatusCodes.BadNotSupported));
            Assert.That(id, Is.Zero);
            Assert.That(stream.Object.CanRead, Is.False);
            Assert.That(harness.GetHandle().OpenCount, Is.Zero);
            harness.Provider.Setup(value => value.OpenReadAsync("file", It.IsAny<CancellationToken>()))
                .Returns(() => new ValueTask<Stream>(new MemoryStream(new byte[4], writable: false)));
            (ServiceResult next, uint nextId) = await harness.GetHandle()
                .OpenAsync(harness.SessionId, 0x09, CancellationToken.None).ConfigureAwait(false);
            Assert.That(next.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(nextId, Is.Not.Zero);
            Assert.That(harness.GetHandle().GetStream(harness.SessionId, nextId)!.Position, Is.EqualTo(4));
        }

        /// <summary>
        /// Verifies that an existing reader or writer rejects an erase-open before the destructive provider is invoked.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task RejectedEraseNeverOpensTheDestructiveProviderAsync(bool existingWriter)
        {
            using var harness = new FileHarness();
            uint original = await FileReadRegressionTests.OpenAsync(
                harness.File, harness.Manager.SystemContext, existingWriter ? (byte)2 : (byte)1).ConfigureAwait(false);
            harness.Provider.Invocations.Clear();
            (ServiceResult result, _) = await FileReadRegressionTests.CallAsync(
                harness.File.Open!, harness.Manager.SystemContext, harness.File.NodeId, [(byte)6]).ConfigureAwait(false);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadNotWritable));
            harness.Provider.Verify(value => value.OpenWriteAsync(
                It.IsAny<string>(), It.IsAny<FileWriteMode>(), It.IsAny<CancellationToken>()), Times.Never);
            Assert.That(harness.GetHandle().GetStream(harness.SessionId, original), Is.Not.Null);
        }

        /// <summary>
        /// Verifies that a pending reader reserves access before its provider stream is returned.
        /// </summary>
        [Test]
        public async Task ReaderReservationExcludesWriterBeforeProviderReadCompletesAsync()
        {
            using var harness = new FileHarness();
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<Stream>(TaskCreationOptions.RunContinuationsAsynchronously);
            harness.Provider.Setup(value => value.OpenReadAsync("file", It.IsAny<CancellationToken>()))
                .Returns(() =>
                {
                    entered.TrySetResult(true);
                    return new ValueTask<Stream>(release.Task);
                });
            Task<(ServiceResult Result, List<Variant> Output)> read = Task.Run(async () =>
                await FileReadRegressionTests.CallAsync(
                    harness.File.Open!, harness.Manager.SystemContext, harness.File.NodeId, [(byte)1])
                    .ConfigureAwait(false));
            try
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                (ServiceResult result, _) = await FileReadRegressionTests.CallAsync(
                    harness.File.Open!, harness.Manager.SystemContext, harness.File.NodeId, [(byte)6])
                    .ConfigureAwait(false);
                Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadNotWritable));
                harness.Provider.Verify(value => value.OpenWriteAsync(
                    It.IsAny<string>(), It.IsAny<FileWriteMode>(), It.IsAny<CancellationToken>()), Times.Never);
            }
            finally
            {
                release.TrySetResult(new MemoryStream(new byte[4], writable: true));
                await read.ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Existing and pending opens reject erase-open before the provider can modify content on either call path.
        /// </summary>
        [TestCase(false, false, false)]
        [TestCase(false, false, true)]
        [TestCase(false, true, false)]
        [TestCase(false, true, true)]
        [TestCase(true, false, false)]
        [TestCase(true, false, true)]
        [TestCase(true, true, false)]
        [TestCase(true, true, true)]
        public async Task WriteConflictRejectsBeforeProviderEffectsAsync(
            bool existingWriter, bool pendingOpen, bool synchronousRejection)
        {
            using var harness = new FileHarness();
            using var stream = new MemoryStream(new byte[] { 10, 20, 30, 40 }, writable: true);
            var release = new TaskCompletionSource<Stream>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (pendingOpen)
            {
                if (existingWriter)
                {
                    harness.Provider.Setup(value => value.OpenWriteAsync(
                        "file", It.IsAny<FileWriteMode>(), It.IsAny<CancellationToken>()))
                        .Returns(new ValueTask<Stream>(release.Task));
                }
                else
                {
                    harness.Provider.Setup(value => value.OpenReadAsync("file", It.IsAny<CancellationToken>()))
                        .Returns(new ValueTask<Stream>(release.Task));
                }
            }
            else
            {
                release.TrySetResult(stream);
                harness.Provider.Setup(value => value.OpenReadAsync("file", It.IsAny<CancellationToken>()))
                    .Returns(new ValueTask<Stream>(stream));
                harness.Provider.Setup(value => value.OpenWriteAsync(
                    "file", It.IsAny<FileWriteMode>(), It.IsAny<CancellationToken>()))
                    .Returns(new ValueTask<Stream>(stream));
            }
            FileHandle handle = harness.GetHandle();
            Task<(ServiceResult Result, uint Handle)> original =
                handle.OpenAsync(harness.SessionId, existingWriter ? (byte)2 : (byte)1, CancellationToken.None)
                    .AsTask();
            try
            {
                Assert.That(original.IsCompleted, Is.EqualTo(!pendingOpen));
                Assert.That(handle.OpenCount, Is.EqualTo(pendingOpen ? 0 : 1));
                harness.Provider.Invocations.Clear();
                if (synchronousRejection)
                {
                    ServiceResult result = handle.Open(harness.SessionId, 6, out uint rejectedHandle);
                    Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadNotWritable));
                    Assert.That(rejectedHandle, Is.Zero);
                }
                else
                {
                    OpenMethodStateResult result = await harness.File.Open!.OnCallAsync!(
                        harness.Manager.SystemContext, harness.File.Open, harness.File.NodeId, 6,
                        CancellationToken.None)
                        .ConfigureAwait(false);
                    Assert.That(result.ServiceResult.StatusCode, Is.EqualTo(StatusCodes.BadNotWritable));
                    Assert.That(result.FileHandle, Is.Zero);
                }
                harness.Provider.Verify(value => value.OpenWriteAsync(
                    It.IsAny<string>(), It.IsAny<FileWriteMode>(), It.IsAny<CancellationToken>()), Times.Never);
                harness.Provider.Verify(value => value.OpenReadAsync(
                    It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
                Assert.That(stream.ToArray(), Is.EqualTo(new byte[] { 10, 20, 30, 40 }));
                Assert.That(stream.Position, Is.Zero);
                Assert.That(stream.CanRead, Is.True);
                Assert.That(handle.OpenCount, Is.EqualTo(pendingOpen ? 0 : 1));
            }
            finally
            {
                release.TrySetResult(stream);
                (ServiceResult result, uint id) = await original.ConfigureAwait(false);
                Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(id, Is.Not.Zero);
                Assert.That(handle.GetStream(harness.SessionId, id), Is.SameAs(stream));
                Assert.That(handle.OpenCount, Is.EqualTo(1));
                Assert.That(await handle.CloseAsync(harness.SessionId, id).ConfigureAwait(false), Is.True);
                Assert.That(handle.OpenCount, Is.Zero);
            }
        }

        /// <summary>
        /// Verifies that the granted file mode restricts operations even when the provider stream supports both.
        /// </summary>
        [TestCase((byte)1)]
        [TestCase((byte)2)]
        public async Task FileModeNotStreamCapabilitiesControlsReadWriteAccessAsync(byte mode)
        {
            using var harness = new FileHarness();
            uint id = await FileReadRegressionTests.OpenAsync(harness.File, harness.Manager.SystemContext, mode)
                .ConfigureAwait(false);
            Stream stream = harness.GetHandle().GetStream(harness.SessionId, id)!;
            Assert.That(stream!.CanRead && stream.CanWrite, Is.True);
            MethodState method = (mode == 1 ? harness.File.Write : harness.File.Read)!;
            ArrayOf<Variant> arguments = mode == 1 ? [id, ByteString.From([99])] : [id, 1];
            (ServiceResult result, _) = await FileReadRegressionTests.CallAsync(
                method!, harness.Manager.SystemContext, harness.File.NodeId, arguments).ConfigureAwait(false);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
            Assert.That(stream.Position, Is.Zero);
        }

        /// <summary>
        /// Verifies that oversized unsigned positions clamp to the file length before conversion to a signed offset.
        /// </summary>
        [TestCase(4ul)]
        [TestCase(5ul)]
        [TestCase(ulong.MaxValue)]
        public async Task PositionBeyondEndClampsBeforeNarrowingAsync(ulong requested)
        {
            using var harness = new FileHarness();
            uint id = await FileReadRegressionTests.OpenAsync(harness.File, harness.Manager.SystemContext)
                .ConfigureAwait(false);
            (ServiceResult result, _) = await FileReadRegressionTests.CallAsync(
                harness.File.SetPosition!, harness.Manager.SystemContext, harness.File.NodeId, [id, requested])
                .ConfigureAwait(false);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(harness.GetHandle().GetStream(harness.SessionId, id)!.Position,
                Is.EqualTo(4));
        }

        /// <summary>
        /// Verifies that advertised writability follows the provider rather than currently open handles.
        /// </summary>
        [TestCase((byte)1)]
        [TestCase((byte)2)]
        public async Task WritableCapabilityDoesNotDependOnOpenHandlesAsync(byte mode)
        {
            using var harness = new FileHarness();
            await FileReadRegressionTests.OpenAsync(harness.File, harness.Manager.SystemContext, mode)
                .ConfigureAwait(false);
            Assert.That(harness.GetHandle().IsWriteable, Is.True);
            harness.Provider.SetupGet(value => value.IsWritable).Returns(false);
            Assert.That(harness.GetHandle().IsWriteable, Is.False);
        }

        /// <summary>
        /// Verifies that interrupted opens retire their reservation, release returned streams, and preserve future
        /// admission.
        /// </summary>
        [TestCase("failure")]
        [TestCase("cancellation")]
        [TestCase("sessionClose")]
        [TestCase("dispose")]
        public async Task InterruptedProviderOpenRetiresOnlyItsReservationAsync(string outcome)
        {
            using var harness = new FileHarness();
            using var cancellation = new CancellationTokenSource();
            var release = new TaskCompletionSource<Stream>(TaskCreationOptions.RunContinuationsAsynchronously);
            harness.Provider.Setup(value => value.OpenReadAsync("file", It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<Stream>(release.Task));
            FileHandle handle = harness.GetHandle();
            Task<(ServiceResult Result, uint Handle)> pending =
                handle.OpenAsync(harness.SessionId, 1, cancellation.Token).AsTask();
            Assert.That(pending.IsCompleted, Is.False);
            Assert.That(handle.OpenCount, Is.Zero);
            using var opened = new MemoryStream(new byte[4], writable: true);
            if (outcome == "failure")
            {
                release.SetException(new IOException("open failed"));
            }
            else
            {
                if (outcome == "cancellation")
                {
                    cancellation.Cancel();
                }
                else if (outcome == "sessionClose")
                {
                    handle.CloseSession(harness.SessionId);
                }
                else
                {
                    handle.Dispose();
                }
                release.SetResult(opened);
            }
            if (outcome == "cancellation")
            {
                await Assert.ThrowsAsync<OperationCanceledException>(async () => await pending.ConfigureAwait(false));
            }
            else
            {
                (ServiceResult result, uint id) = await pending.ConfigureAwait(false);
                Assert.That(result.StatusCode,
                    Is.EqualTo(outcome == "failure" ? StatusCodes.BadInvalidState : StatusCodes.BadSessionClosed));
                Assert.That(id, Is.Zero);
            }
            Assert.That(handle.OpenCount, Is.Zero);
            if (outcome != "failure")
            {
                Assert.That(opened.CanRead, Is.False);
            }
            harness.Provider.Setup(value => value.OpenReadAsync("file", It.IsAny<CancellationToken>()))
                .Returns(() => new ValueTask<Stream>(new MemoryStream(new byte[4], writable: true)));
            (ServiceResult next, uint nextId) = await handle.OpenAsync(harness.SessionId, 1, CancellationToken.None)
                .ConfigureAwait(false);
            Assert.That(next.StatusCode, Is.EqualTo(outcome == "dispose" ? StatusCodes.BadShutdown : StatusCodes.Good));
            Assert.That(nextId == 0, Is.EqualTo(outcome == "dispose"));
        }

        /// <summary>
        /// Protocol methods serialize the cursor and explicit Close drains a previously admitted position request.
        /// </summary>
        [Test]
        public async Task ProtocolCloseDrainsReadAndQueuedPositionBeforeDisposingAsync()
        {
            using var harness = new FileHarness();
            using var stream = new SuspendedProtocolReadStream();
            harness.Provider.Setup(value => value.OpenReadAsync("file", It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<Stream>(stream));
            uint id = await FileReadRegressionTests.OpenAsync(harness.File, harness.Manager.SystemContext)
                .ConfigureAwait(false);
            Task<ReadMethodStateResult> read = harness.File.Read!.OnCallAsync!(
                harness.Manager.SystemContext, harness.File.Read, harness.File.NodeId, id, 1, CancellationToken.None)
                .AsTask();
            Task<GetPositionMethodStateResult> position = harness.File.GetPosition!.OnCallAsync!(
                harness.Manager.SystemContext, harness.File.GetPosition, harness.File.NodeId, id,
                CancellationToken.None)
                .AsTask();
            Task<CloseMethodStateResult> close = harness.File.Close!.OnCallAsync!(
                harness.Manager.SystemContext, harness.File.Close, harness.File.NodeId, id, CancellationToken.None)
                .AsTask();
            try
            {
                Assert.That(position.IsCompleted, Is.False);
                Assert.That(close.IsCompleted, Is.False);
                Assert.That(stream.CanRead, Is.True);
                SetPositionMethodStateResult rejected = await harness.File.SetPosition!.OnCallAsync!(
                    harness.Manager.SystemContext, harness.File.SetPosition, harness.File.NodeId, id, 0,
                    CancellationToken.None).ConfigureAwait(false);
                Assert.That(rejected.ServiceResult.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
                Assert.That(harness.GetHandle().TryBlockOpens(), Is.False);
            }
            finally
            {
                stream.Release.TrySetResult(true);
            }
            ReadMethodStateResult readResult = await read.ConfigureAwait(false);
            GetPositionMethodStateResult positionResult = await position.ConfigureAwait(false);
            CloseMethodStateResult closeResult = await close.ConfigureAwait(false);
            Assert.That(readResult.ServiceResult.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(positionResult.Position, Is.EqualTo(1));
            Assert.That(closeResult.ServiceResult.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(stream.CanRead, Is.False);
        }

        /// <summary>
        /// The node manager's session and shutdown callbacks cancel I/O without disposing an actively used stream.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task ManagerLifecycleRetainsCancellationIgnoringProtocolReadAsync(bool shutdown)
        {
            using var harness = new FileHarness();
            using var stream = new SuspendedProtocolReadStream();
            harness.Manager.CloseTimeout = TimeSpan.Zero;
            harness.Provider.Setup(value => value.OpenReadAsync("file", It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<Stream>(stream));
            uint id = await FileReadRegressionTests.OpenAsync(harness.File, harness.Manager.SystemContext)
                .ConfigureAwait(false);
            FileHandle handles = harness.GetHandle();
            Task<ReadMethodStateResult> read = harness.File.Read!.OnCallAsync!(
                harness.Manager.SystemContext, harness.File.Read, harness.File.NodeId, id, 1, CancellationToken.None)
                .AsTask();
            try
            {
                if (shutdown)
                {
                    await harness.Manager.DisposeAsync().ConfigureAwait(false);
                }
                else
                {
                    await harness.Manager.SessionClosingAsync(null!, harness.SessionId, false).ConfigureAwait(false);
                }
                await stream.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                Assert.That(read.IsCompleted, Is.False);
                Assert.That(stream.CanRead, Is.True);
                Assert.That(handles.TryBlockOpens(), Is.False);
                Assert.That(handles.CleanupCompletion.IsCompleted, Is.False);
            }
            finally
            {
                stream.Release.TrySetResult(true);
                await read.ConfigureAwait(false);
                await handles.CleanupCompletion.ConfigureAwait(false);
            }
            Assert.That(stream.CanRead, Is.False);
        }

        /// <summary>
        /// Keeps a provider read active until the test releases it, even after lifetime cancellation.
        /// </summary>
        private sealed class SuspendedProtocolReadStream : MemoryStream
        {
            /// <summary>
            /// Creates a four-byte readable stream.
            /// </summary>
            public SuspendedProtocolReadStream()
                : base(new byte[4], writable: true)
            {
            }

            /// <summary>
            /// Gets the externally controlled read completion.
            /// </summary>
            public TaskCompletionSource<bool> Release { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            /// <summary>
            /// Gets the cancellation notification from the active provider request.
            /// </summary>
            public TaskCompletionSource<bool> Cancelled { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            /// <inheritdoc/>
            public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct)
            {
                using CancellationTokenRegistration registration = ct.Register(() => Cancelled.TrySetResult(true));
                await Release.Task.ConfigureAwait(false);
                return base.Read(buffer, offset, count);
            }

#if NET5_0_OR_GREATER
            /// <inheritdoc/>
            public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
            {
                using CancellationTokenRegistration registration = ct.Register(() => Cancelled.TrySetResult(true));
                await Release.Task.ConfigureAwait(false);
                return base.Read(buffer.Span);
            }
#endif
        }

        /// <summary>
        /// Owns a file-system manager with a controllable provider and a shared test session.
        /// </summary>
        private sealed class FileHarness : IDisposable
        {
            /// <summary>
            /// Creates a writable four-byte file whose provider opens can be observed or suspended.
            /// </summary>
            public FileHarness()
            {
                Mock<IServerInternal> server = DeterministicServerMock.Create(out m_queues);
                Provider.SetupGet(value => value.MountName).Returns("file-regression");
                Provider.SetupGet(value => value.IsWritable).Returns(true);
                Provider.Setup(value => value.GetEntryAsync("file", It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new FileSystemEntry("file", "file", false, 4, true, DateTime.UtcNow, string.Empty));
                Provider.Setup(value => value.OpenReadAsync("file", It.IsAny<CancellationToken>()))
                    .Returns(() => new ValueTask<Stream>(new MemoryStream(new byte[4], writable: true)));
                Provider.Setup(value => value.OpenWriteAsync(
                        "file", It.IsAny<FileWriteMode>(), It.IsAny<CancellationToken>()))
                    .Returns(() => new ValueTask<Stream>(new MemoryStream(new byte[4], writable: true)));
                Manager = new FileSystemNodeManager(server.Object, new ApplicationConfiguration(), Provider.Object);
                Manager.SystemContext.SessionId = SessionId;
                File = new FileObjectState(
                    Manager.SystemContext, FileSystemNodeId.BuildFile("file", Manager.NamespaceIndex), "file", "file");
            }

            /// <summary>
            /// Gets the provider mock used to control stream acquisition and file capabilities.
            /// </summary>
            public Mock<IFileSystemProvider> Provider { get; } = new();

            /// <summary>
            /// Gets the manager that owns the file's open-handle state.
            /// </summary>
            public FileSystemNodeManager Manager { get; }

            /// <summary>
            /// Gets the file node exposing the protocol methods under test.
            /// </summary>
            public FileObjectState File { get; }

            /// <summary>
            /// Gets the session identity owning successful opens and in-flight reservations.
            /// </summary>
            public NodeId SessionId { get; } = new(100, 1);

            /// <summary>
            /// Gets the manager-owned handle state for the test file.
            /// </summary>
            public FileHandle GetHandle()
            {
                return Manager.GetOrCreateHandle(File.NodeId, File.ProviderPath)!;
            }

            /// <summary>
            /// Disposes the file-system manager and monitored-item queue factory.
            /// </summary>
            public void Dispose()
            {
                Manager.Dispose();
                m_queues.Dispose();
            }

            /// <summary>
            /// Owns the monitored-item queues used by the test node manager.
            /// </summary>
            private readonly MonitoredItemQueueFactory m_queues;
        }
    }
}
