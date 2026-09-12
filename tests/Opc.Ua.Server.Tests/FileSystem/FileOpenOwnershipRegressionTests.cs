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
    [TestFixture]
    [Category("FileSystem")]
    public sealed class FileOpenOwnershipRegressionTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public async Task RejectedEraseNeverOpensTheDestructiveProviderAsync(bool existingWriter)
        {
            using var harness = new FileHarness();
            uint original = await FileReadRegressionTests.OpenAsync(
                harness.File, harness.Manager.SystemContext, existingWriter ? (byte)2 : (byte)1).ConfigureAwait(false);
            harness.Provider.Invocations.Clear();
            (ServiceResult result, _) = await FileReadRegressionTests.CallAsync(
                harness.File.Open, harness.Manager.SystemContext, harness.File.NodeId, [(byte)6]).ConfigureAwait(false);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
            harness.Provider.Verify(value => value.OpenWriteAsync(
                It.IsAny<string>(), It.IsAny<FileWriteMode>(), It.IsAny<CancellationToken>()), Times.Never);
            Assert.That(harness.GetHandle().GetStream(harness.SessionId, original), Is.Not.Null);
        }

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
                    harness.File.Open, harness.Manager.SystemContext, harness.File.NodeId, [(byte)1])
                    .ConfigureAwait(false));
            try
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                (ServiceResult result, _) = await FileReadRegressionTests.CallAsync(
                    harness.File.Open, harness.Manager.SystemContext, harness.File.NodeId, [(byte)6])
                    .ConfigureAwait(false);
                Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
                harness.Provider.Verify(value => value.OpenWriteAsync(
                    It.IsAny<string>(), It.IsAny<FileWriteMode>(), It.IsAny<CancellationToken>()), Times.Never);
            }
            finally
            {
                release.TrySetResult(new MemoryStream(new byte[4], writable: true));
                await read.ConfigureAwait(false);
            }
        }

        [TestCase((byte)1)]
        [TestCase((byte)2)]
        public async Task FileModeNotStreamCapabilitiesControlsReadWriteAccessAsync(byte mode)
        {
            using var harness = new FileHarness();
            uint id = await FileReadRegressionTests.OpenAsync(harness.File, harness.Manager.SystemContext, mode)
                .ConfigureAwait(false);
            Stream stream = harness.GetHandle().GetStream(harness.SessionId, id);
            Assert.That(stream.CanRead && stream.CanWrite, Is.True);
            MethodState method = mode == 1 ? harness.File.Write : harness.File.Read;
            ArrayOf<Variant> arguments = mode == 1 ? [id, ByteString.From([99])] : [id, 1];
            (ServiceResult result, _) = await FileReadRegressionTests.CallAsync(
                method, harness.Manager.SystemContext, harness.File.NodeId, arguments).ConfigureAwait(false);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
            Assert.That(stream.Position, Is.Zero);
        }

        [TestCase(4ul)]
        [TestCase(5ul)]
        [TestCase(ulong.MaxValue)]
        public async Task PositionBeyondEndClampsBeforeNarrowingAsync(ulong requested)
        {
            using var harness = new FileHarness();
            uint id = await FileReadRegressionTests.OpenAsync(harness.File, harness.Manager.SystemContext)
                .ConfigureAwait(false);
            (ServiceResult result, _) = await FileReadRegressionTests.CallAsync(
                harness.File.SetPosition, harness.Manager.SystemContext, harness.File.NodeId, [id, requested])
                .ConfigureAwait(false);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(harness.GetHandle().GetStream(harness.SessionId, id).Position,
                Is.EqualTo(4));
        }

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
                Assert.ThrowsAsync<OperationCanceledException>(async () => await pending.ConfigureAwait(false));
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

        private sealed class FileHarness : IDisposable
        {
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

            public Mock<IFileSystemProvider> Provider { get; } = new();
            public FileSystemNodeManager Manager { get; }
            public FileObjectState File { get; }
            public NodeId SessionId { get; } = new(100, 1);

            public FileHandle GetHandle()
            {
                return Manager.GetOrCreateHandle(File.NodeId, File.ProviderPath);
            }

            public void Dispose()
            {
                Manager.Dispose();
                m_queues.Dispose();
            }

            private readonly MonitoredItemQueueFactory m_queues;
        }
    }
}
