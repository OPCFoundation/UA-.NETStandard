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
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.Bindings;
using Opc.Ua.Client;
using Opc.Ua.Pcap.Bindings;
using Opc.Ua.PubSub.Transports;
using UaLens.Diagnostics.Capture;
using UaLens.Connection;
using UaLens.Diagnostics;
using UaLens.Plugins.Companions;
using UaLens.Tests.Companions;
using UaLens.Tests.PubSub;

namespace UaLens.Tests.Diagnose
{
    [TestFixture]
    public sealed class DiagnosticCaptureTests
    {
        [TestCase(0)]
        [TestCase(1)]
        public void PayloadBudgetNeverAdmitsAFrameBeyondTheExactRemainingBytes(int excess)
        {
            var budget = new DiagnosticCaptureBudget(new DiagnosticCaptureLimits(MaxFrames: 2, MaxPayloadBytes: 16));
            Assert.That(budget.TryAccept(8), Is.True);
            Assert.That(budget.TryAccept(8 + excess), Is.EqualTo(excess == 0));
            Assert.That(budget.Snapshot().Frames, Is.EqualTo(excess == 0 ? 2 : 1));
            Assert.That(budget.Snapshot().PayloadBytes, Is.EqualTo(excess == 0 ? 16 : 8));
            Assert.That(budget.TryAccept(1), Is.False);
            Assert.That(budget.Reached.IsCompletedSuccessfully, Is.True);
        }

        [Test]
        public void ParallelFrameAdmissionCannotExceedTheSharedBudget()
        {
            var budget = new DiagnosticCaptureBudget(new DiagnosticCaptureLimits(MaxFrames: 17, MaxPayloadBytes: 100));
            int accepted = 0;
            Parallel.For(0, 100, _ =>
            {
                if (budget.TryAccept(10))
                {
                    Interlocked.Increment(ref accepted);
                }
            });
            Assert.That(accepted, Is.EqualTo(10));
            Assert.That(budget.Snapshot(), Is.EqualTo((10L, 100L)));
        }

        [Test]
        public void MonotonicDeadlineRejectsTheExactDeadlineEvenBeforeTheStopTimerRuns()
        {
            var clock = new PubSubTestClock();
            var budget = new DiagnosticCaptureBudget(new DiagnosticCaptureLimits(DurationSeconds: 1), clock);
            clock.Advance(TimeSpan.FromSeconds(1) - TimeSpan.FromTicks(1));
            Assert.That(budget.TryAccept(8), Is.True);
            clock.Advance(TimeSpan.FromTicks(1));
            Assert.That(budget.TryAccept(8), Is.False);
            Assert.That(budget.Snapshot(), Is.EqualTo((1L, 8L)));
            Assert.That(budget.Reached.IsCompletedSuccessfully, Is.True);
        }

        [Test]
        public async Task NoConsentOrInvalidBoundsCanInstallAnObserverOrCreateCaptureFilesAsync()
        {
            var test = new CaptureFixture();
            await using (test.ConfigureAwait(false))
            {
                Assert.That(test.Workspace.Snapshot().Phase, Is.EqualTo(DiagnosticCapturePhase.Empty));
                Assert.That(test.Folders, Is.Empty);
                await Assert.ThatAsync(() => test.Workspace.StartAsync("client", new(), false),
                    Throws.TypeOf<InvalidOperationException>()).ConfigureAwait(false);
                await Assert.ThatAsync(() => test.Workspace.StartAsync("client", new(MaxFrames: 2049), true),
                    Throws.TypeOf<ArgumentOutOfRangeException>()).ConfigureAwait(false);
                Assert.That(test.Registry.CurrentObserver, Is.Null);
                Assert.That(test.Folders, Is.Empty);
            }
        }

        [Test]
        public async Task MalformedReplayRemainsAnExplicitFailureButClearCanDiscardItsOwnedFilesAsync()
        {
            var test = new CaptureFixture();
            await using (test.ConfigureAwait(false))
            {
                using var source = new MemoryStream(s_chunk, writable: false);
                await Assert.ThatAsync(() => test.Workspace.ReplayAsync(source, new(), true),
                    Throws.InstanceOf<IOException>()).ConfigureAwait(false);
                Assert.That(test.Workspace.Snapshot().Phase, Is.EqualTo(DiagnosticCapturePhase.Failed));
                await test.Workspace.ClearAsync().ConfigureAwait(false);
                Assert.That(test.Workspace.Snapshot().Phase, Is.EqualTo(DiagnosticCapturePhase.Empty));
                Assert.That(test.Folders.All(folder => !Directory.Exists(folder)), Is.True);
                Assert.That(source.ToArray(), Is.EqualTo(s_chunk));
            }
        }

        [Test]
        public async Task RebindingStopsOnlyClientCaptureAndLeavesBothBorrowedSessionsUntouchedAsync()
        {
            var first = new Mock<ISession>(MockBehavior.Strict);
            var second = new Mock<ISession>(MockBehavior.Strict);
            ISession session = first.Object;
            var connection = new Mock<IConnectionWorkspace>(MockBehavior.Strict);
            connection.SetupGet(value => value.CurrentSession).Returns(() => session);
            Func<CancellationToken, Task>? changed = null;
            connection.SetupAdd(value => value.ConnectionChangedAsync += It.IsAny<Func<CancellationToken, Task>>())
                .Callback((Func<CancellationToken, Task> handler) => changed = handler);
            connection.SetupRemove(value => value.ConnectionChangedAsync -= It.IsAny<Func<CancellationToken, Task>>());
            var test = new CaptureFixture(connection.Object);
            await using (test.ConfigureAwait(false))
            {
                await test.Workspace.StartAsync("client", new(), true).ConfigureAwait(false);
                test.Registry.CurrentObserver!.OnFrameSent(1, s_chunk);
                session = second.Object;
                await (changed ?? throw new AssertionException("Missing rebind observer."))(CancellationToken.None)
                    .ConfigureAwait(false);
                Assert.That(test.Registry.CurrentObserver, Is.Null);
                Assert.That(test.Workspace.Snapshot().Phase, Is.EqualTo(DiagnosticCapturePhase.Completed));
                first.VerifyNoOtherCalls();
                second.VerifyNoOtherCalls();
            }
        }

        [Test]
        public void OrdinaryDiagnosticExportCannotIncludeCapturePayloadPathsLabelsOrErrors()
        {
            var snapshot = new DiagnosticCaptureSnapshot(Guid.NewGuid(), DiagnosticCaptureKind.Client,
                DiagnosticCapturePhase.Failed, 2, 16, 180, string.Empty,
                "secret endpoint and local path", [new(1, DateTimeOffset.UnixEpoch, 90, "private packet marker")]);
            ByteString export = DiagnosticEvidenceExport.Create(null, null, capture: snapshot);
            string json = System.Text.Encoding.UTF8.GetString(export.Span);
            Assert.That(json, Does.Not.Contain("secret endpoint").And.Not.Contain("private packet marker"));
            Assert.That(json, Does.Contain(snapshot.Id.ToString()).And.Contain("\"frames\": 2"));
            Assert.That(json, Does.Not.Contain("Packet capture is unconfigured"));
        }

        [Test]
        public async Task ClientCaptureExportsExactPcapAndReplaysOfflineWithoutKeysOrNetworkTrafficAsync()
        {
            var test = new CaptureFixture();
            await using (test.ConfigureAwait(false))
            {
                await test.Workspace.StartAsync("client", new(MaxFrames: 2, MaxPayloadBytes: 16), true)
                    .ConfigureAwait(false);
                IFrameCaptureSink observer = test.Registry.CurrentObserver ??
                    throw new AssertionException("No active observer.");
                observer.OnFrameSent(1, s_chunk);
                observer.OnFrameReceived(1, s_chunk);
                await test.Workspace.StopAsync().ConfigureAwait(false);
                DiagnosticCaptureSnapshot captured = test.Workspace.Snapshot();
                Assert.That(captured.Phase, Is.EqualTo(DiagnosticCapturePhase.Completed), captured.Status);
                Assert.That(captured.Frames, Is.EqualTo(2));
                Assert.That(captured.PayloadBytes, Is.EqualTo(16));
                Assert.That(captured.Packets.Count, Is.EqualTo(2));
                Assert.That(test.Registry.CurrentObserver, Is.Null);
                string exported = Path.Combine(test.Root, "export.pcap");
                await test.Workspace.ExportAsync(captured.Id, exported, true).ConfigureAwait(false);
                byte[] bytes = await File.ReadAllBytesAsync(exported).ConfigureAwait(false);
                Assert.That(Convert.ToHexString(SHA256.HashData(bytes)), Is.EqualTo(captured.Sha256));
                Assert.That(bytes.LongLength, Is.EqualTo(captured.FileBytes));
                using var source = new MemoryStream(bytes, writable: false);
                await test.Workspace.ReplayAsync(source, new(MaxFrames: 2), true).ConfigureAwait(false);
                DiagnosticCaptureSnapshot replay = test.Workspace.Snapshot();
                Assert.That(replay.Kind, Is.EqualTo(DiagnosticCaptureKind.Replay));
                Assert.That(replay.Phase, Is.EqualTo(DiagnosticCapturePhase.Completed), replay.Status);
                Assert.That(replay.Id, Is.Not.EqualTo(captured.Id));
                Assert.That(replay.Sha256, Is.EqualTo(captured.Sha256));
                Assert.That(replay.Packets.ToList(), Is.EqualTo(captured.Packets.ToList()));
                Assert.That(source.CanRead, Is.True, "Replay borrows the selected stream.");
                Assert.That(test.Registry.CurrentObserver, Is.Null);
                Assert.That(File.Exists(exported), Is.True, "The exported destination is not owned temporary storage.");
                await test.Workspace.ClearAsync().ConfigureAwait(false);
                Assert.That(test.Folders.All(folder => !Directory.Exists(folder)), Is.True);
                Assert.That(File.Exists(exported), Is.True);
            }
        }

        [Test]
        public async Task RawExportRequiresFreshIdentityConsentAndANewUnchangedDestinationAsync()
        {
            var test = new CaptureFixture();
            await using (test.ConfigureAwait(false))
            {
                await test.Workspace.StartAsync("client", new(MaxFrames: 1), true).ConfigureAwait(false);
                test.Registry.CurrentObserver!.OnFrameSent(1, s_chunk);
                await test.Workspace.StopAsync().ConfigureAwait(false);
                DiagnosticCaptureSnapshot snapshot = test.Workspace.Snapshot();
                string destination = Path.Combine(test.Root, "existing.pcap");
                await File.WriteAllTextAsync(destination, "keep existing data").ConfigureAwait(false);
                await Assert.ThatAsync(() => test.Workspace.ExportAsync(snapshot.Id, destination, false),
                    Throws.TypeOf<InvalidOperationException>()).ConfigureAwait(false);
                await Assert.ThatAsync(() => test.Workspace.ExportAsync(Guid.NewGuid(), destination, true),
                    Throws.TypeOf<InvalidOperationException>()).ConfigureAwait(false);
                await Assert.ThatAsync(() => test.Workspace.ExportAsync(snapshot.Id, destination, true),
                    Throws.TypeOf<IOException>()).ConfigureAwait(false);
                Assert.That(await File.ReadAllTextAsync(destination).ConfigureAwait(false),
                    Is.EqualTo("keep existing data"));
                Assert.That(Directory.GetFiles(test.Root, "*.pending"), Is.Empty);
            }
        }

        [Test]
        public async Task ChangedPrivatePacketBytesCannotBePublishedWithTheOldDigestAsync()
        {
            var test = new CaptureFixture();
            await using (test.ConfigureAwait(false))
            {
                await test.Workspace.StartAsync("client", new(MaxFrames: 1), true).ConfigureAwait(false);
                test.Registry.CurrentObserver!.OnFrameSent(1, s_chunk);
                await test.Workspace.StopAsync().ConfigureAwait(false);
                DiagnosticCaptureSnapshot snapshot = test.Workspace.Snapshot();
                string path = Directory.GetFiles(test.Folders.Single(), "*.pcap", SearchOption.AllDirectories).Single();
                byte[] changed = await File.ReadAllBytesAsync(path).ConfigureAwait(false);
                changed[^1] ^= 1;
                await File.WriteAllBytesAsync(path, changed).ConfigureAwait(false);
                string destination = Path.Combine(test.Root, "not-published.pcap");
                await Assert.ThatAsync(() => test.Workspace.ExportAsync(snapshot.Id, destination, true),
                    Throws.TypeOf<IOException>().With.Message.Contains("changed")).ConfigureAwait(false);
                Assert.That(File.Exists(destination), Is.False);
                Assert.That(test.Workspace.Snapshot().Sha256, Is.EqualTo(snapshot.Sha256));
            }
        }

        [Test]
        public async Task ClosingAPubSubSourceDrainsItsCaptureButDoesNotCloseTheBorrowedTransportAsync()
        {
            var test = new CaptureFixture();
            await using (test.ConfigureAwait(false))
            {
                var inner = new PubSubControlledRuntimeTests.EncodedLoopbackTransport();
                DiagnosticPubSubCaptureRegistration registration = test.Workspace.RegisterPubSub(inner, "Owned test");
                IPubSubTransport transport = registration.Factory.Create(
                    new PubSubConnectionDataType(), DefaultTelemetry.Create(static _ => { }), TimeProvider.System);
                await using (transport.ConfigureAwait(false))
                {
                    await transport.OpenAsync().ConfigureAwait(false);
                    DiagnosticCaptureSource source = test.Workspace.Sources.ToList()
                        .Single(value => value.Kind == DiagnosticCaptureKind.PubSub);
                    await test.Workspace.StartAsync(source.Id, new(), true).ConfigureAwait(false);
                    await transport.SendAsync(s_chunk).ConfigureAwait(false);
                    await registration.DisposeAsync().ConfigureAwait(false);
                    DiagnosticCaptureSnapshot snapshot = test.Workspace.Snapshot();
                    Assert.That(snapshot.Phase, Is.EqualTo(DiagnosticCapturePhase.Completed), snapshot.Status);
                    Assert.That(snapshot.Frames, Is.EqualTo(1));
                    Assert.That(snapshot.Packets.Count, Is.EqualTo(1));
                    Assert.That(transport.IsConnected, Is.True);
                    Assert.That(test.Workspace.Sources.Contains(value => value.Id == source.Id), Is.False);
                    await Assert.ThatAsync(() => test.Workspace.StartAsync(source.Id, new(), true),
                        Throws.TypeOf<InvalidOperationException>()).ConfigureAwait(false);
                }
                Assert.That(inner.IsConnected, Is.False);
            }
        }

        [Test]
        public async Task StoppingReplayDrainsALateBorrowedStreamReadBeforeDeletingItsPrivateFilesAsync()
        {
            var test = new CaptureFixture();
            await using (test.ConfigureAwait(false))
            {
                var source = new DelayedStream(s_chunk);
                await using (source.ConfigureAwait(false))
                {
                    await AssertStoppedReplayDrainsBorrowedStreamAsync(test, source)
                        .ConfigureAwait(false);
                }
            }
        }

        private static async Task AssertStoppedReplayDrainsBorrowedStreamAsync(
            CaptureFixture test, DelayedStream source)
        {
            Task replay = test.Workspace.ReplayAsync(source, new(), true);
            try
            {
                await source.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                Task stop = test.Workspace.StopAsync();
                Assert.That(stop.IsCompleted, Is.False);
                source.Release.TrySetResult();
                await stop.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                await Assert.ThatAsync(() => replay, Throws.InstanceOf<OperationCanceledException>())
                    .ConfigureAwait(false);
                Assert.That(source.CanRead, Is.True);
                Assert.That(test.Workspace.Snapshot().Phase, Is.EqualTo(DiagnosticCapturePhase.Empty));
                await test.Workspace.ClearAsync().ConfigureAwait(false);
                Assert.That(test.Folders.All(folder => !Directory.Exists(folder)), Is.True);
            }
            finally
            {
                source.Release.TrySetResult();
                try
                {
                    await replay.ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (replay.IsCanceled)
                {
                    // The initiating task's cancellation was asserted above; disposal still waits for it.
                }
            }
        }

        [TestCase(0)]
        [TestCase(1)]
        public async Task SharedStreamExportEnforcesItsExactLimitWithoutPartialPublicationAsync(int excess)
        {
            var test = new CaptureFixture();
            await using (test.ConfigureAwait(false))
            {
                string destination = Path.Combine(test.Root, "bounded.pcap");
                using var source = new MemoryStream(new byte[16 + excess]);
                if (excess == 0)
                {
                    await CompanionLocalExport.CopyAsync(
                        destination, source, 16, static () => { }, CancellationToken.None)
                        .ConfigureAwait(false);
                    Assert.That(new FileInfo(destination).Length, Is.EqualTo(16));
                }
                else
                {
                    await Assert.ThatAsync(() => CompanionLocalExport.CopyAsync(
                        destination, source, 16, static () => { }, CancellationToken.None),
                        Throws.TypeOf<IOException>()).ConfigureAwait(false);
                    Assert.That(File.Exists(destination), Is.False);
                }
                Assert.That(source.CanRead, Is.True);
                Assert.That(Directory.GetFiles(test.Root, "*.pending"), Is.Empty);
            }
        }

        private sealed class CaptureFixture : IAsyncDisposable
        {
            public CaptureFixture(IConnectionWorkspace? connection = null)
            {
                Directory.CreateDirectory(Root);
                Workspace = new DiagnosticCaptureWorkspace(DefaultTelemetry.Create(static _ => { }), Registry,
                    connection,
                    newFolder: () =>
                    {
                        string path = Path.Combine(Root, Guid.NewGuid().ToString("N"));
                        Folders.Add(path);
                        return path;
                    });
            }

            public string Root { get; } = OpenUsdTestPaths.NewDestination();
            public ChannelCaptureRegistry Registry { get; } = new();
            public DiagnosticCaptureWorkspace Workspace { get; }
            public List<string> Folders { get; } = [];

            public async ValueTask DisposeAsync()
            {
                try
                {
                    await Workspace.DisposeAsync().ConfigureAwait(false);
                }
                finally
                {
                    Directory.Delete(Root, recursive: true);
                }
            }
        }

        private sealed class DelayedStream(byte[] bytes) : MemoryStream(bytes, writable: false)
        {
            public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
            public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

            public override async ValueTask<int> ReadAsync(
                Memory<byte> buffer, CancellationToken cancellationToken = default)
            {
                Entered.TrySetResult();
                await Release.Task.ConfigureAwait(false);
                return await base.ReadAsync(buffer, CancellationToken.None).ConfigureAwait(false);
            }
        }

        private static readonly byte[] s_chunk = [(byte)'H', (byte)'E', (byte)'L', (byte)'F', 8, 0, 0, 0];
    }
}
