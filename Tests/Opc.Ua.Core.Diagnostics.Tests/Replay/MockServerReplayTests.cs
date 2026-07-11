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
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Pcap.Capture;
using Opc.Ua.Pcap.Capture.Sources;
using Opc.Ua.Pcap.Frame;
using Opc.Ua.Pcap.KeyLog;
using Opc.Ua.Pcap.Models;
using Opc.Ua.Pcap.Replay;

namespace Opc.Ua.Pcap.Tests.Replay
{
    [TestFixture]
    public sealed class MockServerReplayTests : TempDirectoryFixture
    {
        [Test]
        public async Task StartAsyncWithReplayCaptureSourceCanBeCanceledWhileReadingPcap()
        {
            FakeCaptureFolder capture = await CreateSingleFrameCaptureAsync().ConfigureAwait(false);
            ReplayCaptureSource source = await ReplayTestHelpers.CreateReplaySourceAsync(
                capture,
                includeKeyLog: true,
                CancellationToken.None).ConfigureAwait(false);
            await using (source.ConfigureAwait(false))
            {
                var replay = new MockServerReplay(source);
                await using (replay.ConfigureAwait(false))
                {
                    // Pre-cancel the token so the test is deterministic
                    // on fast machines. A 20 ms time-based cancel races
                    // with a single-frame replay that finishes loading
                    // in well under 20 ms; the cancel never fires.
                    using var cts = new CancellationTokenSource();
                    cts.Cancel();

                    Exception? exception = Assert.CatchAsync(async () =>
                        await replay.StartAsync("opc.tcp", null, cts.Token).ConfigureAwait(false));

                    Assert.That(exception, Is.InstanceOf<OperationCanceledException>());
                }
            }
        }

        [Test]
        public async Task StartAsyncThrowsForNonPositiveSpeed()
        {
            FakeCaptureFolder capture = await CreateSingleFrameCaptureAsync().ConfigureAwait(false);
            ReplayCaptureSource source = await ReplayTestHelpers.CreateReplaySourceAsync(
                capture,
                includeKeyLog: true,
                CancellationToken.None).ConfigureAwait(false);
            await using (source.ConfigureAwait(false))
            {
                var replay = new MockServerReplay(source)
                {
                    Speed = 0.0
                };
                await using (replay.ConfigureAwait(false))
                {
                    PcapDiagnosticsException? exception = Assert.ThrowsAsync<PcapDiagnosticsException>(async () =>
                        await replay.StartAsync("opc.tcp", null, CancellationToken.None).ConfigureAwait(false));

                    Assert.That(exception, Is.Not.Null);
                    Assert.That(exception!.Message, Does.Contain("speed"));
                }
            }
        }

        [Test]
        public async Task StopAsyncBeforeStartCompletes()
        {
            FakeCaptureFolder capture = await CreateSingleFrameCaptureAsync().ConfigureAwait(false);
            ReplayCaptureSource source = await ReplayTestHelpers.CreateReplaySourceAsync(
                capture,
                includeKeyLog: true,
                CancellationToken.None).ConfigureAwait(false);
            await using (source.ConfigureAwait(false))
            {
                var replay = new MockServerReplay(source);
                await using (replay.ConfigureAwait(false))
                {
                    Assert.That(
                        async () => await replay.StopAsync(CancellationToken.None).ConfigureAwait(false),
                        Throws.Nothing);
                }
            }
        }

        [Test]
        public async Task DisposeAsyncIsIdempotent()
        {
            FakeCaptureFolder capture = await CreateSingleFrameCaptureAsync().ConfigureAwait(false);
            ReplayCaptureSource source = await ReplayTestHelpers.CreateReplaySourceAsync(
                capture,
                includeKeyLog: true,
                CancellationToken.None).ConfigureAwait(false);
            var replay = new MockServerReplay(source);
            await using (replay.ConfigureAwait(false))
            {
                Assert.That(
                    async () => await replay.DisposeAsync().ConfigureAwait(false),
                    Throws.Nothing);
                Assert.That(
                    async () => await replay.DisposeAsync().ConfigureAwait(false),
                    Throws.Nothing);
            }
        }

        [Test]
        public async Task StartAsyncThrowsPcapDiagnosticsExceptionForNonReplaySource()
        {
            var source = new NonReplayCaptureSource();
            await using (source.ConfigureAwait(false))
            {
                var replay = new MockServerReplay(source);
                await using (replay.ConfigureAwait(false))
                {
                    PcapDiagnosticsException? exception = Assert.ThrowsAsync<PcapDiagnosticsException>(async () =>
                        await replay.StartAsync("opc.tcp", null, CancellationToken.None).ConfigureAwait(false));

                    Assert.That(exception, Is.Not.Null);
                    Assert.That(exception!.Message, Does.Contain("ReplayCaptureSource"));
                }
            }
        }

        [Test]
        public void ConstructorRejectsNullSource()
        {
            Assert.That(
                () => new MockServerReplay(null!),
                Throws.TypeOf<ArgumentNullException>().With.Property("ParamName").EqualTo("source"));
        }

        [Test]
        public async Task StopAsyncHonorsCanceledTokenBeforeStart()
        {
            FakeCaptureFolder capture = await CreateSingleFrameCaptureAsync().ConfigureAwait(false);
            ReplayCaptureSource source = await ReplayTestHelpers.CreateReplaySourceAsync(
                capture,
                includeKeyLog: true,
                CancellationToken.None).ConfigureAwait(false);
            await using (source.ConfigureAwait(false))
            {
                var replay = new MockServerReplay(source);
                await using (replay.ConfigureAwait(false))
                {
                    using var cts = new CancellationTokenSource();
                    cts.Cancel();

                    Assert.That(
                        async () => await replay.StopAsync(cts.Token).ConfigureAwait(false),
                        Throws.InstanceOf<OperationCanceledException>());
                }
            }
        }

        [Test]
        public async Task PrivateScaleDelayHandlesZeroNegativeAndSpeedMultiplier()
        {
            FakeCaptureFolder capture = await CreateSingleFrameCaptureAsync().ConfigureAwait(false);
            ReplayCaptureSource source = await ReplayTestHelpers.CreateReplaySourceAsync(
                capture,
                includeKeyLog: true,
                CancellationToken.None).ConfigureAwait(false);
            await using (source.ConfigureAwait(false))
            {
                var replay = new MockServerReplay(source)
                {
                    Speed = 2.0
                };
                await using (replay.ConfigureAwait(false))
                {
                    Assert.That(InvokeScaleDelay(replay, TimeSpan.Zero), Is.EqualTo(TimeSpan.Zero));
                    Assert.That(InvokeScaleDelay(replay, TimeSpan.FromTicks(-1)), Is.EqualTo(TimeSpan.Zero));
                    Assert.That(
                        InvokeScaleDelay(replay, TimeSpan.FromSeconds(1)),
                        Is.EqualTo(TimeSpan.FromMilliseconds(500)));
                }
            }
        }

        [Test]
        public async Task ConnectedClientReceivesCapturedServerFrameAfterSendingClientBytes()
        {
            var timestamp = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
            await using var source = new NonReplayCaptureSource();
            var replay = new MockServerReplay(source)
            {
                Speed = double.MaxValue
            };
            await using (replay.ConfigureAwait(false))
            using (var listener = new TcpListener(IPAddress.Loopback, 0))
            {
                SetReplayFrames(
                    replay,
                    (timestamp, CaptureFrameDirection.ClientToServer, new byte[] { 1, 2, 3 }),
                    (timestamp.AddMilliseconds(1), CaptureFrameDirection.ServerToClient, new byte[] { 9, 8 }));

                listener.Start();
                int port = ((IPEndPoint)listener.LocalEndpoint).Port;
                using var client = new TcpClient();
                Task<TcpClient> acceptTask = listener.AcceptTcpClientAsync();
                await client.ConnectAsync("127.0.0.1", port).ConfigureAwait(false);
                using TcpClient accepted = await acceptTask.ConfigureAwait(false);
                using NetworkStream stream = client.GetStream();
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                ValueTask replayTask = InvokeReplayConnectionAsync(replay, accepted, cts.Token);

                await stream.WriteAsync(new byte[] { 1, 2, 3 }.AsMemory(), cts.Token).ConfigureAwait(false);
                await stream.WriteAsync(new byte[] { 4 }.AsMemory(), cts.Token).ConfigureAwait(false);
                byte[] response = new byte[2];
                int read = await ReadFullyAsync(stream, response, cts.Token).ConfigureAwait(false);
                client.Close();
                await replayTask.ConfigureAwait(false);

                Assert.That(read, Is.EqualTo(response.Length));
                Assert.That(response, Is.EqualTo(new byte[] { 9, 8 }).AsCollection);
            }
        }

        [Test]
        public async Task StartAsyncThrowsWhenReplaySourceHasNoFramesOrStartedTwice()
        {
            FakeCaptureFolder capture = await ReplayTestHelpers.CreateFakeCaptureFolderAsync(
                TempDirectory,
                Array.Empty<FakeCaptureFrame>(),
                CancellationToken.None).ConfigureAwait(false);
            ReplayCaptureSource emptySource = await ReplayTestHelpers.CreateReplaySourceAsync(
                capture,
                includeKeyLog: true,
                CancellationToken.None).ConfigureAwait(false);
            await using (emptySource.ConfigureAwait(false))
            {
                var emptyReplay = new MockServerReplay(emptySource);
                await using (emptyReplay.ConfigureAwait(false))
                {
                    Assert.That(
                        async () => await emptyReplay.StartAsync("opc.tcp", null, CancellationToken.None)
                            .ConfigureAwait(false),
                        Throws.TypeOf<PcapDiagnosticsException>().With.Message.Contains("contains no captured frames"));
                }
            }

            FakeCaptureFolder singleCapture = await CreateSingleFrameCaptureAsync().ConfigureAwait(false);
            ReplayCaptureSource source = await ReplayTestHelpers.CreateReplaySourceAsync(
                singleCapture,
                includeKeyLog: true,
                CancellationToken.None).ConfigureAwait(false);
            await using (source.ConfigureAwait(false))
            {
                var replay = new MockServerReplay(source);
                await using (replay.ConfigureAwait(false))
                {
                    await replay.StartAsync("opc.tcp", null, CancellationToken.None).ConfigureAwait(false);

                    Assert.That(
                        async () => await replay.StartAsync("opc.tcp", null, CancellationToken.None)
                            .ConfigureAwait(false),
                        Throws.TypeOf<PcapDiagnosticsException>().With.Message.Contains("started twice"));
                }
            }
        }

        private ValueTask<FakeCaptureFolder> CreateSingleFrameCaptureAsync()
        {
            return ReplayTestHelpers.CreateFakeCaptureFolderAsync(
                TempDirectory,
                [ReplayTestHelpers.CreateFrame(DateTimeOffset.UtcNow, fromClient: false, 0xAA)],
                CancellationToken.None);
        }

        private static TimeSpan InvokeScaleDelay(MockServerReplay replay, TimeSpan delay)
        {
            MethodInfo method = typeof(MockServerReplay).GetMethod(
                "ScaleDelay",
                BindingFlags.NonPublic | BindingFlags.Instance)!;

            return (TimeSpan)method.Invoke(replay, new object[] { delay })!;
        }

        private static async ValueTask<int> ReadFullyAsync(
            NetworkStream stream,
            byte[] buffer,
            CancellationToken ct)
        {
            int total = 0;
            while (total < buffer.Length)
            {
                int read = await stream.ReadAsync(buffer.AsMemory(total, buffer.Length - total), ct)
                    .ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                total += read;
            }

            return total;
        }

        private static void SetReplayFrames(
            MockServerReplay replay,
            params (DateTimeOffset Timestamp, CaptureFrameDirection Direction, byte[] Data)[] frames)
        {
            Type replayFrameType = typeof(MockServerReplay).GetNestedType(
                "ReplayFrame",
                BindingFlags.NonPublic)!;
            var replayFrames = (System.Collections.IList)Activator.CreateInstance(
                typeof(List<>).MakeGenericType(replayFrameType))!;
            ConstructorInfo constructor = replayFrameType.GetConstructor(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                binder: null,
                new[] { typeof(DateTimeOffset), typeof(CaptureFrameDirection), typeof(byte[]) },
                modifiers: null)!;
            foreach ((DateTimeOffset frameTimestamp, CaptureFrameDirection direction, byte[] data) in frames)
            {
                replayFrames.Add(constructor.Invoke(new object[] { frameTimestamp, direction, data }));
            }

            typeof(MockServerReplay).GetField("m_frames", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(replay, replayFrames);
        }

        private static async ValueTask InvokeReplayConnectionAsync(
            MockServerReplay replay,
            TcpClient client,
            CancellationToken ct)
        {
            MethodInfo method = typeof(MockServerReplay).GetMethod(
                "ReplayConnectionAsync",
                BindingFlags.NonPublic | BindingFlags.Instance)!;
            var task = (ValueTask)method.Invoke(replay, new object[] { client, ct })!;
            await task.ConfigureAwait(false);
        }

        private sealed class NonReplayCaptureSource : ICaptureSource
        {
            public IReadOnlySet<FormatKind> SupportedFormats { get; } = new HashSet<FormatKind>();

            public long FrameCount => 0;

            public long ByteCount => 0;

            public ValueTask StartAsync(StartCaptureRequest request, CancellationToken ct)
            {
                return new ValueTask();
            }

            public ValueTask StopAsync(CancellationToken ct)
            {
                return new ValueTask();
            }

            public string? GetRawPcapFilePath()
            {
                return null;
            }

            public string? GetKeyLogFilePath()
            {
                return null;
            }

            public async IAsyncEnumerable<ChannelKeyMaterial> ReadKeyMaterialAsync(
                [EnumeratorCancellation] CancellationToken ct)
            {
                await Task.Yield();
                yield break;
            }

            public async IAsyncEnumerable<CaptureFrame> ReadCapturedFramesAsync(
                long? maxFrames,
                [EnumeratorCancellation] CancellationToken ct)
            {
                await Task.Yield();
                yield break;
            }

            public ValueTask DisposeAsync()
            {
                return new ValueTask();
            }
        }
    }
}
