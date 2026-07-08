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
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Bindings;
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
                    Assert.DoesNotThrowAsync(async () =>
                        await replay.StopAsync(CancellationToken.None).ConfigureAwait(false));
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
                Assert.DoesNotThrowAsync(async () => await replay.DisposeAsync().ConfigureAwait(false));
                Assert.DoesNotThrowAsync(async () => await replay.DisposeAsync().ConfigureAwait(false));
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

        private ValueTask<FakeCaptureFolder> CreateSingleFrameCaptureAsync()
        {
            return ReplayTestHelpers.CreateFakeCaptureFolderAsync(
                TempDirectory,
                [ReplayTestHelpers.CreateFrame(DateTimeOffset.UtcNow, fromClient: false, 0xAA)],
                CancellationToken.None);
        }

        private sealed class NonReplayCaptureSource : ICaptureSource
        {
            public IReadOnlySet<FormatKind> SupportedFormats { get; } = new HashSet<FormatKind>();

            public long FrameCount => 0;

            public long ByteCount => 0;

            public ValueTask StartAsync(StartCaptureRequest request, CancellationToken ct)
            {
                return ValueTask.CompletedTask;
            }

            public ValueTask StopAsync(CancellationToken ct)
            {
                return ValueTask.CompletedTask;
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
                await Task.CompletedTask.ConfigureAwait(false);
                yield break;
            }

            public async IAsyncEnumerable<CaptureFrame> ReadCapturedFramesAsync(
                long? maxFrames,
                [EnumeratorCancellation] CancellationToken ct)
            {
                await Task.CompletedTask.ConfigureAwait(false);
                yield break;
            }

            public ValueTask DisposeAsync()
            {
                return ValueTask.CompletedTask;
            }
        }
    }
}
