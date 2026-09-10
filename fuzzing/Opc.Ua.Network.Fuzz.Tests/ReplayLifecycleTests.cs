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

#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using Opc.Ua.Pcap.Capture;
using Opc.Ua.Pcap.Capture.Sources;
using Opc.Ua.Pcap.DependencyInjection;
using Opc.Ua.Pcap.Frame;
using Opc.Ua.Pcap.KeyLog;
using Opc.Ua.Pcap.Models;
using Opc.Ua.Pcap.Replay;

namespace Opc.Ua.Fuzzing
{
    /// <summary>
    /// Offline lifecycle regressions, not stateful network replay coverage.
    /// No test starts a mock listener or runs a mock-client conversation.
    /// </summary>
    [TestFixture]
    [Category("Fuzzing")]
    public sealed class ReplayLifecycleTests
    {
        [Test]
        public async Task MockServerLifecycleDoesNotListenOrConsumeCapture()
        {
            var fixture = new ReplayFixture();
            await using (fixture.ConfigureAwait(false))
            {
                await fixture.WritePcapAsync().ConfigureAwait(false);
                await fixture.Source.StartAsync(fixture.Request, CancellationToken.None).ConfigureAwait(false);
                using var loggerFactory = new RecordingLoggerFactory();
                var replay = new MockServerReplay(fixture.Source, loggerFactory);
                await using (replay.ConfigureAwait(false))
                {
                    Assert.That(replay.Speed, Is.EqualTo(1.0));
                    Assert.That(replay.ListenUri, Is.Null);
                    Assert.That(loggerFactory.Categories, Has.Count.EqualTo(1));
                    Assert.That(loggerFactory.Categories[0], Is.EqualTo("Opc.Ua.Pcap.Replay.MockServerReplay"));
                    AssertStartedAndUnread(fixture);

                    using var cancellation = new CancellationTokenSource();
                    await cancellation.CancelAsync().ConfigureAwait(false);
                    await Assert.ThatAsync(
                        async () => await replay.StopAsync(cancellation.Token).ConfigureAwait(false),
                        Throws.TypeOf<OperationCanceledException>()
                            .With.Property("CancellationToken").EqualTo(cancellation.Token)).ConfigureAwait(false);
                    Assert.That(replay.ListenUri, Is.Null);
                    AssertStartedAndUnread(fixture);

                    await replay.StopAsync(CancellationToken.None).ConfigureAwait(false);
                    await replay.StopAsync(CancellationToken.None).ConfigureAwait(false);

                    Assert.That(replay.ListenUri, Is.Null);
                    AssertStartedAndUnread(fixture);
                    await AssertSyntheticFramesAsync(fixture).ConfigureAwait(false);
                    await replay.DisposeAsync().ConfigureAwait(false);

                    Assert.That(replay.ListenUri, Is.Null);
                    Assert.That(loggerFactory.IsDisposed, Is.False,
                        "The supplied logger factory remains caller-owned.");
                    Assert.That(fixture.Source.FrameCount, Is.EqualTo(2));
                    Assert.That(fixture.Source.ByteCount, Is.EqualTo(108));
                }
            }
        }

        [TestCase(true)]
        [TestCase(false)]
        public async Task MockReplayDisposeAsyncPropagatesSourceFailure(bool server)
        {
            var fixture = new ReplayFixture();
            await using (fixture.ConfigureAwait(false))
            {
                await fixture.WritePcapAsync().ConfigureAwait(false);
                var failure = new IOException("Synthetic replay-source disposal failure.");
                var source = new GatedCaptureSource(fixture.Source) { DisposalFailure = failure };
                await using (source.ConfigureAwait(false))
                {
                    source.ReleaseDisposal();
                    IAsyncDisposable replay = server
                        ? new MockServerReplay(source)
                        : new MockClientReplay(source, "opc.tcp://127.0.0.1:49321/synthetic");
                    await using (replay.ConfigureAwait(false))
                    {
                        await Assert.ThatAsync(
                            async () => await replay.DisposeAsync().ConfigureAwait(false),
                            Throws.Exception.SameAs(failure)).ConfigureAwait(false);

                        Assert.That(source.DisposalStarted, Is.True);
                        Assert.That(source.DisposalCompleted, Is.False);
                        await AssertNoCaptureAsync(fixture.Source).ConfigureAwait(false);
                        await fixture.Source.StartAsync(fixture.Request, CancellationToken.None).ConfigureAwait(false);
                        await AssertSyntheticFramesAsync(fixture).ConfigureAwait(false);
                    }
                }
            }
        }

        [TestCase("opc.tcp://localhost:49321/synthetic")]
        [TestCase("opc.tcps://localhost:49321/synthetic")]
        [TestCase("opc.https://localhost:49321/synthetic")]
        public async Task MockClientConfiguredConstructorDoesNotConsumeCapture(string endpoint)
        {
            var fixture = new ReplayFixture();
            await using (fixture.ConfigureAwait(false))
            {
                await fixture.WritePcapAsync().ConfigureAwait(false);
                await fixture.Source.StartAsync(fixture.Request, CancellationToken.None).ConfigureAwait(false);
                using var loggerFactory = new RecordingLoggerFactory();
                var options = new PcapOptions
                {
                    BaseFolder = fixture.DirectoryPath,
                    AllowMockClientReplay = true,
                    AllowedReplayEndpoints = ["unrelated.invalid", "LOCALHOST"]
                };
                var replay = new MockClientReplay(fixture.Source, endpoint, options, loggerFactory);
                await using (replay.ConfigureAwait(false))
                {
                    Assert.That(replay.Speed, Is.EqualTo(1.0));
                    Assert.That(loggerFactory.Categories, Has.Count.EqualTo(1));
                    Assert.That(loggerFactory.Categories[0], Is.EqualTo("Opc.Ua.Pcap.Replay.MockClientReplay"));
                    AssertStartedAndUnread(fixture);
                    await AssertSyntheticFramesAsync(fixture).ConfigureAwait(false);
                    await replay.DisposeAsync().ConfigureAwait(false);

                    Assert.That(loggerFactory.IsDisposed, Is.False,
                        "The supplied logger factory remains caller-owned.");
                    Assert.That(fixture.Source.GetRawPcapFilePath(), Is.EqualTo(fixture.PcapPath));
                    Assert.That(fixture.Source.FrameCount, Is.EqualTo(2));
                    Assert.That(fixture.Source.ByteCount, Is.EqualTo(108));
                }
            }
        }

        [TestCase(true)]
        [TestCase(false)]
        public async Task MockReplayDisposeAsyncAwaitsOwnedSourceAndPreventsItsStart(bool server)
        {
            var fixture = new ReplayFixture();
            await using (fixture.ConfigureAwait(false))
            {
                await fixture.WritePcapAsync().ConfigureAwait(false);
                var source = new GatedCaptureSource(fixture.Source);
                await using (source.ConfigureAwait(false))
                {
                    try
                    {
                        IAsyncDisposable replay = server
                            ? new MockServerReplay(source)
                            : new MockClientReplay(source, "opc.tcp://127.0.0.1:49321/synthetic");
                        await using (replay.ConfigureAwait(false))
                        {
                            Task disposal = replay.DisposeAsync().AsTask();
                            try
                            {
                                Assert.That(source.DisposalStarted, Is.True);
                                Assert.That(source.DisposalCompleted, Is.False);
                                Assert.That(disposal.IsCompleted, Is.False,
                                    "Mock disposal must await its source's asynchronous cleanup.");
                                Assert.That(fixture.Source.GetRawPcapFilePath(), Is.Null);
                            }
                            finally
                            {
                                source.ReleaseDisposal();
                                await disposal.ConfigureAwait(false);
                            }

                            Assert.That(source.DisposalCompleted, Is.True);
                            // A fresh source can start, but a disposed source cannot. A source
                            // that was already running would reject this even if disposal were a no-op.
                            await AssertStartRejectedAsync(fixture.Source, fixture.Request).ConfigureAwait(false);
                            Assert.That(fixture.Source.FrameCount, Is.Zero);
                            Assert.That(fixture.Source.ByteCount, Is.Zero);

                            await replay.DisposeAsync().ConfigureAwait(false);
                            await AssertStartRejectedAsync(fixture.Source, fixture.Request).ConfigureAwait(false);
                        }
                    }
                    finally
                    {
                        source.ReleaseDisposal();
                    }
                }
            }
        }

        [Test]
        public async Task ReplaySourceStopAllowsOfflineReadsWithoutReplacingTheCapture()
        {
            var fixture = new ReplayFixture();
            await using (fixture.ConfigureAwait(false))
            {
                var other = new ReplayFixture();
                await using (other.ConfigureAwait(false))
                {
                    await fixture.WritePcapAsync().ConfigureAwait(false);
                    await other.WritePcapAsync().ConfigureAwait(false);
                    Assert.That(fixture.DirectoryPath, Is.Not.EqualTo(other.DirectoryPath));
                    await fixture.Source.StartAsync(fixture.Request, CancellationToken.None).ConfigureAwait(false);
                    await AssertStartRejectedAsync(fixture.Source, other.Request).ConfigureAwait(false);
                    AssertStartedAndUnread(fixture);

                    await fixture.Source.StopAsync(CancellationToken.None).ConfigureAwait(false);
                    await fixture.Source.StopAsync(CancellationToken.None).ConfigureAwait(false);
                    await AssertStartRejectedAsync(fixture.Source, other.Request).ConfigureAwait(false);
                    AssertStartedAndUnread(fixture);
                    await AssertSyntheticFramesAsync(fixture).ConfigureAwait(false);

                    await fixture.Source.DisposeAsync().ConfigureAwait(false);
                    await AssertStartRejectedAsync(fixture.Source, other.Request).ConfigureAwait(false);
                    Assert.That(fixture.Source.GetRawPcapFilePath(), Is.EqualTo(fixture.PcapPath));
                    Assert.That(fixture.Source.FrameCount, Is.EqualTo(2));
                    Assert.That(fixture.Source.ByteCount, Is.EqualTo(108));
                }
            }
        }

        [TestCase(null, "pcapFilePath")]
        [TestCase("", "pcapFilePath")]
        [TestCase(" ", "pcapFilePath")]
        [TestCase("missing.pcap", "does not exist")]
        public async Task ReplaySourceRejectedPathLeavesItStartable(string? fileName, string diagnostic)
        {
            var fixture = new ReplayFixture();
            await using (fixture.ConfigureAwait(false))
            {
                await fixture.WritePcapAsync().ConfigureAwait(false);
                var request = new StartCaptureRequest
                {
                    Source = CaptureSourceKind.Replay,
                    PcapFilePath = string.IsNullOrWhiteSpace(fileName)
                        ? fileName
                        : Path.Combine(fixture.DirectoryPath, fileName)
                };
                await Assert.ThatAsync(
                    async () => await fixture.Source.StartAsync(request, CancellationToken.None).ConfigureAwait(false),
                    Throws.TypeOf<PcapDiagnosticsException>().With.Message.Contains(diagnostic)).ConfigureAwait(false);

                await AssertNoCaptureAsync(fixture.Source).ConfigureAwait(false);
                await fixture.Source.StartAsync(fixture.Request, CancellationToken.None).ConfigureAwait(false);
                AssertStartedAndUnread(fixture);
                await AssertSyntheticFramesAsync(fixture).ConfigureAwait(false);
            }
        }

        [Test]
        public async Task ReplaySourceNullRequestLeavesItStartable()
        {
            var fixture = new ReplayFixture();
            await using (fixture.ConfigureAwait(false))
            {
                await fixture.WritePcapAsync().ConfigureAwait(false);
                await Assert.ThatAsync(
                    async () => await fixture.Source.StartAsync(null!, CancellationToken.None).ConfigureAwait(false),
                    Throws.TypeOf<ArgumentNullException>().With.Property("ParamName").EqualTo("request"))
                    .ConfigureAwait(false);

                await AssertNoCaptureAsync(fixture.Source).ConfigureAwait(false);
                await fixture.Source.StartAsync(fixture.Request, CancellationToken.None).ConfigureAwait(false);
                await AssertSyntheticFramesAsync(fixture).ConfigureAwait(false);
            }
        }

        [TestCase(true)]
        [TestCase(false)]
        public async Task ReplaySourceCanceledStartOrStopLeavesItStartable(bool start)
        {
            var fixture = new ReplayFixture();
            await using (fixture.ConfigureAwait(false))
            {
                await fixture.WritePcapAsync().ConfigureAwait(false);
                using var cancellation = new CancellationTokenSource();
                await cancellation.CancelAsync().ConfigureAwait(false);

                await Assert.ThatAsync(
                    async () =>
                    {
                        if (start)
                        {
                            await fixture.Source.StartAsync(fixture.Request, cancellation.Token).ConfigureAwait(false);
                        }
                        else
                        {
                            await fixture.Source.StopAsync(cancellation.Token).ConfigureAwait(false);
                        }
                    },
                    Throws.TypeOf<OperationCanceledException>()
                        .With.Property("CancellationToken").EqualTo(cancellation.Token)).ConfigureAwait(false);

                await AssertNoCaptureAsync(fixture.Source).ConfigureAwait(false);
                await fixture.Source.StartAsync(fixture.Request, CancellationToken.None).ConfigureAwait(false);
                await AssertSyntheticFramesAsync(fixture).ConfigureAwait(false);
            }
        }

        [TestCase(true)]
        [TestCase(false)]
        public async Task ReplaySourceStopOrDisposeBeforeStartIsTerminal(bool dispose)
        {
            var fixture = new ReplayFixture();
            await using (fixture.ConfigureAwait(false))
            {
                await fixture.WritePcapAsync().ConfigureAwait(false);
                if (dispose)
                {
                    await fixture.Source.DisposeAsync().ConfigureAwait(false);
                }
                else
                {
                    await fixture.Source.StopAsync(CancellationToken.None).ConfigureAwait(false);
                }

                await AssertStartRejectedAsync(fixture.Source, fixture.Request).ConfigureAwait(false);
                await AssertNoCaptureAsync(fixture.Source).ConfigureAwait(false);
            }
        }

        [TestCase(true)]
        [TestCase(false)]
        public async Task MockReplayConstructorsRejectNullSource(bool server)
        {
            await Assert.ThatAsync(
                async () =>
                {
                    IAsyncDisposable replay = server
                        ? new MockServerReplay(null!)
                        : new MockClientReplay(null!, "opc.tcp://127.0.0.1:49321/synthetic");
                    await replay.DisposeAsync().ConfigureAwait(false);
                },
                Throws.TypeOf<ArgumentNullException>().With.Property("ParamName").EqualTo("source"))
                .ConfigureAwait(false);
        }

        [TestCase(null, false, typeof(ArgumentNullException), "targetEndpointUrl")]
        [TestCase("", false, typeof(ArgumentException), "targetEndpointUrl")]
        [TestCase(" ", false, typeof(ArgumentException), "targetEndpointUrl")]
        [TestCase("opc.tcp://localhost:49321/synthetic", true, typeof(ArgumentNullException), "options")]
        public async Task MockClientRejectedArgumentsDoNotTakeOwnershipOfSource(
            string? endpoint,
            bool nullOptions,
            Type exceptionType,
            string parameter)
        {
            var fixture = new ReplayFixture();
            await using (fixture.ConfigureAwait(false))
            {
                await fixture.WritePcapAsync().ConfigureAwait(false);
                PcapOptions? options = nullOptions
                    ? null
                    : new PcapOptions
                    {
                        BaseFolder = fixture.DirectoryPath,
                        AllowMockClientReplay = true,
                        AllowedReplayEndpoints = ["localhost"]
                    };
                await Assert.ThatAsync(
                    async () =>
                    {
                        var replay = new MockClientReplay(fixture.Source, endpoint!, options!);
                        await replay.DisposeAsync().ConfigureAwait(false);
                    },
                    Throws.TypeOf(exceptionType).With.Property("ParamName").EqualTo(parameter)).ConfigureAwait(false);

                await AssertNoCaptureAsync(fixture.Source).ConfigureAwait(false);
                await fixture.Source.StartAsync(fixture.Request, CancellationToken.None).ConfigureAwait(false);
                await AssertSyntheticFramesAsync(fixture).ConfigureAwait(false);
            }
        }

        [TestCase(false, "localhost", "opc.tcp://localhost:49321/synthetic", "disabled")]
        [TestCase(true, null, "opc.tcp://localhost:49321/synthetic", "empty")]
        [TestCase(true, "localhost", "not-an-absolute-uri", "valid absolute URI")]
        [TestCase(true, "localhost", "https://localhost:49321/synthetic", "scheme 'https'")]
        [TestCase(true, "localhost", "opc.tcp://127.0.0.1:49321/synthetic", "host '127.0.0.1' is not in")]
        public async Task MockClientRejectedPolicyDoesNotTakeOwnershipOfSource(
            bool enabled,
            string? allowedHost,
            string endpoint,
            string diagnostic)
        {
            var fixture = new ReplayFixture();
            await using (fixture.ConfigureAwait(false))
            {
                await fixture.WritePcapAsync().ConfigureAwait(false);
                using var loggerFactory = new RecordingLoggerFactory();
                var options = new PcapOptions
                {
                    BaseFolder = fixture.DirectoryPath,
                    AllowMockClientReplay = enabled,
                    AllowedReplayEndpoints = allowedHost is null ? [] : [allowedHost]
                };
                await Assert.ThatAsync(
                    async () =>
                    {
                        var replay = new MockClientReplay(fixture.Source, endpoint, options, loggerFactory);
                        await replay.DisposeAsync().ConfigureAwait(false);
                    },
                    Throws.TypeOf<PcapDiagnosticsException>().With.Message.Contains(diagnostic)).ConfigureAwait(false);

                Assert.That(loggerFactory.Categories, Is.Empty);
                Assert.That(loggerFactory.IsDisposed, Is.False);
                await AssertNoCaptureAsync(fixture.Source).ConfigureAwait(false);
                await fixture.Source.StartAsync(fixture.Request, CancellationToken.None).ConfigureAwait(false);
                await AssertSyntheticFramesAsync(fixture).ConfigureAwait(false);
            }
        }

        [Test]
        public void NetworkTargetInventoryRetainsRealCallbacksAndExcludesLifecycleHelpers()
        {
            MethodInfo[] publicMethods = typeof(FuzzableCode).GetMethods(
                BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly);
            Assert.That(
                publicMethods.Select(static method => method.Name),
                Is.EquivalentTo(s_expectedPublicMethods));
            Assert.That(
                NetworkTests.FuzzableFunctions.Select(static target => target.MethodInfo.Name),
                Is.EquivalentTo(s_expectedPublicMethods.Where(static name => name != nameof(FuzzableCode.FuzzInfo))));
        }

        private static async Task AssertNoCaptureAsync(ReplayCaptureSource source)
        {
            Assert.That(source.GetRawPcapFilePath(), Is.Null);
            Assert.That(source.GetKeyLogFilePath(), Is.Null);
            Assert.That(source.FrameCount, Is.Zero);
            Assert.That(source.ByteCount, Is.Zero);
            IAsyncEnumerator<CaptureFrame> frames = source
                .ReadCapturedFramesAsync(null, CancellationToken.None)
                .GetAsyncEnumerator();
            await using (frames.ConfigureAwait(false))
            {
                Assert.That(await frames.MoveNextAsync().ConfigureAwait(false), Is.False);
            }
        }

        private static void AssertStartedAndUnread(ReplayFixture fixture)
        {
            Assert.That(fixture.Source.GetRawPcapFilePath(), Is.EqualTo(fixture.PcapPath));
            Assert.That(fixture.Source.GetKeyLogFilePath(), Is.Null);
            Assert.That(fixture.Source.FrameCount, Is.Zero);
            Assert.That(fixture.Source.ByteCount, Is.Zero);
        }

        private static async Task AssertSyntheticFramesAsync(ReplayFixture fixture)
        {
            int count = 0;
            await foreach (CaptureFrame frame in fixture.Source.ReadCapturedFramesAsync(
                maxFrames: null,
                CancellationToken.None).ConfigureAwait(false))
            {
                Assert.That(count, Is.LessThan(2), "The pcap must end after its two synthetic records.");
                Assert.That(frame.Timestamp, Is.EqualTo(ReplayFixture.Timestamp.AddSeconds(count * 2)));
                Assert.That(frame.Direction, Is.EqualTo(CaptureFrameDirection.Unknown));
                Assert.That(frame.ClientEndpoint, Is.Empty);
                Assert.That(frame.ServerEndpoint, Is.Empty);
                Assert.That(frame.Data.ToArray(), Is.EqualTo(count == 0 ? fixture.ClientPacket : fixture.ServerPacket));
                Assert.That(frame.Data.Length, Is.EqualTo(count == 0 ? 52 : 56));
                count++;
                Assert.That(fixture.Source.FrameCount, Is.EqualTo(count));
                Assert.That(fixture.Source.ByteCount, Is.EqualTo(count == 1 ? 52 : 108));
            }

            Assert.That(count, Is.EqualTo(2));
            Assert.That(fixture.Source.FrameCount, Is.EqualTo(2));
            Assert.That(fixture.Source.ByteCount, Is.EqualTo(108));
            IAsyncEnumerator<ChannelKeyMaterial> keys = fixture.Source
                .ReadKeyMaterialAsync(CancellationToken.None)
                .GetAsyncEnumerator();
            await using (keys.ConfigureAwait(false))
            {
                Assert.That(await keys.MoveNextAsync().ConfigureAwait(false), Is.False);
            }
        }

        private static async Task AssertStartRejectedAsync(ReplayCaptureSource source, StartCaptureRequest request)
        {
            await Assert.ThatAsync(
                async () => await source.StartAsync(request, CancellationToken.None).ConfigureAwait(false),
                Throws.TypeOf<PcapDiagnosticsException>().With.Message.EqualTo(
                    "ReplayCaptureSource cannot be started twice.")).ConfigureAwait(false);
        }

        private sealed class ReplayFixture : IAsyncDisposable
        {
            public static DateTimeOffset Timestamp { get; } = DateTimeOffset.UnixEpoch
                .AddSeconds(42).AddTicks(1_234_560);

            public string DirectoryPath { get; } = Directory.CreateTempSubdirectory("opcua-replay-lifecycle-").FullName;

            public string PcapPath => Path.Combine(DirectoryPath, "synthetic.pcap");

            public ReplayCaptureSource Source { get; } = new();

            public StartCaptureRequest Request => new()
            {
                Source = CaptureSourceKind.Replay,
                PcapFilePath = PcapPath
            };

            public byte[] ClientPacket { get; } = LoopbackFrameBuilder.Build(
                fromClient: true,
                channelId: 0x12345678,
                [0x48, 0x45, 0x4C, 0x46, 0x08, 0, 0, 0]);

            public byte[] ServerPacket { get; } = LoopbackFrameBuilder.Build(
                fromClient: false,
                channelId: 0x12345678,
                [0x41, 0x43, 0x4B, 0x46, 0x0C, 0, 0, 0, 0x11, 0x22, 0x33, 0x44]);

            public async Task WritePcapAsync()
            {
                // PcapFileWriter initializes every global/record header field,
                // including timezone, accuracy and the microsecond timestamp.
                var writer = new PcapFileWriter(PcapPath, PcapFileWriter.LinkTypeNull);
                await using (writer.ConfigureAwait(false))
                {
                    await writer.WriteAsync(Timestamp, ClientPacket, CancellationToken.None).ConfigureAwait(false);
                    await writer.WriteAsync(Timestamp.AddSeconds(2), ServerPacket, CancellationToken.None)
                        .ConfigureAwait(false);
                }
            }

            public async ValueTask DisposeAsync()
            {
                try
                {
                    await Source.DisposeAsync().ConfigureAwait(false);
                }
                finally
                {
                    Directory.Delete(DirectoryPath, recursive: true);
                }
            }
        }

        private sealed class RecordingLoggerFactory : ILoggerFactory
        {
            public List<string> Categories { get; } = [];

            public bool IsDisposed { get; private set; }

            public ILogger CreateLogger(string categoryName)
            {
                Categories.Add(categoryName);
                return NullLogger.Instance;
            }

            public void AddProvider(ILoggerProvider provider)
            {
                throw new NotSupportedException("Replay construction must not register logging providers.");
            }

            public void Dispose()
            {
                IsDisposed = true;
            }
        }

        private sealed class GatedCaptureSource(ReplayCaptureSource source) : ICaptureSource
        {
            public bool DisposalStarted { get; private set; }

            public bool DisposalCompleted { get; private set; }

            public IOException? DisposalFailure { get; set; }

            public IReadOnlySet<FormatKind> SupportedFormats => source.SupportedFormats;

            public long FrameCount => source.FrameCount;

            public long ByteCount => source.ByteCount;

            public ValueTask StartAsync(StartCaptureRequest request, CancellationToken ct)
            {
                return source.StartAsync(request, ct);
            }

            public ValueTask StopAsync(CancellationToken ct)
            {
                return source.StopAsync(ct);
            }

            public string? GetRawPcapFilePath()
            {
                return source.GetRawPcapFilePath();
            }

            public string? GetKeyLogFilePath()
            {
                return source.GetKeyLogFilePath();
            }

            public IAsyncEnumerable<ChannelKeyMaterial> ReadKeyMaterialAsync(CancellationToken ct)
            {
                return source.ReadKeyMaterialAsync(ct);
            }

            public IAsyncEnumerable<CaptureFrame> ReadCapturedFramesAsync(long? maxFrames, CancellationToken ct)
            {
                return source.ReadCapturedFramesAsync(maxFrames, ct);
            }

            public void ReleaseDisposal()
            {
                m_releaseDisposal.TrySetResult();
            }

            public async ValueTask DisposeAsync()
            {
                DisposalStarted = true;
                await m_releaseDisposal.Task.ConfigureAwait(false);
                if (DisposalFailure is IOException failure)
                {
                    // Fail one attempt so the test can still await real cleanup.
                    DisposalFailure = null;
                    throw failure;
                }

                await source.DisposeAsync().ConfigureAwait(false);
                DisposalCompleted = true;
            }

            private readonly TaskCompletionSource m_releaseDisposal = new(
                TaskCreationOptions.RunContinuationsAsynchronously);
        }

        private static readonly string[] s_expectedPublicMethods =
        [
            nameof(FuzzableCode.FuzzInfo),
            nameof(FuzzableCode.AflfuzzOpcUaFrameParser),
            nameof(FuzzableCode.LibfuzzOpcUaFrameParser),
            nameof(FuzzableCode.AflfuzzOfflineSecureChannelReadChunk),
            nameof(FuzzableCode.LibfuzzOfflineSecureChannelReadChunk),
            nameof(FuzzableCode.AflfuzzServiceCallReassembler),
            nameof(FuzzableCode.LibfuzzServiceCallReassembler),
            nameof(FuzzableCode.AflfuzzTcpStreamReassembler),
            nameof(FuzzableCode.LibfuzzTcpStreamReassembler),
            nameof(FuzzableCode.AflfuzzTcpChunkHeader),
            nameof(FuzzableCode.LibfuzzTcpChunkHeader),
            nameof(FuzzableCode.AflfuzzHelloMessage),
            nameof(FuzzableCode.LibfuzzHelloMessage),
            nameof(FuzzableCode.AflfuzzAcknowledgeMessage),
            nameof(FuzzableCode.LibfuzzAcknowledgeMessage),
            nameof(FuzzableCode.AflfuzzErrorMessage),
            nameof(FuzzableCode.LibfuzzErrorMessage),
            nameof(FuzzableCode.AflfuzzReverseHelloMessage),
            nameof(FuzzableCode.LibfuzzReverseHelloMessage),
            nameof(FuzzableCode.AflfuzzAsymmetricMessageHeader),
            nameof(FuzzableCode.LibfuzzAsymmetricMessageHeader)
        ];
    }
}
