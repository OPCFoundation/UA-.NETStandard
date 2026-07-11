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
using Opc.Ua.Pcap.Capture;
using Opc.Ua.Pcap.Frame;
using Opc.Ua.Pcap.KeyLog;
using Opc.Ua.Pcap.Models;
using Opc.Ua.Pcap.Replay;

namespace Opc.Ua.Pcap.Tests.Replay
{
    [TestFixture]
    public sealed class MockClientReplayTests
    {
        [Test]
        public async Task RunAsyncAgainstClosedTargetThrowsBeforeReturningResult()
        {
            var source = new FiniteCaptureSource();
            await using (source.ConfigureAwait(false))
            {
                var replay = new MockClientReplay(source, "opc.tcp://127.0.0.1:1");
                await using (replay.ConfigureAwait(false))
                {
                    using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));

                    Exception? exception = Assert.CatchAsync(async () =>
                        await replay.RunAsync(cts.Token).ConfigureAwait(false));

                    Assert.That(exception, Is.Not.Null);
                }
            }
        }

        [Test]
        public async Task RunAsyncWithoutKeyLogStillFailsAtClosedTargetNotDuringDecodeSetup()
        {
            var source = new FiniteCaptureSource();
            await using (source.ConfigureAwait(false))
            {
                var replay = new MockClientReplay(source, "opc.tcp://127.0.0.1:1");
                await using (replay.ConfigureAwait(false))
                {
                    using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));

                    Exception? exception = Assert.CatchAsync(async () =>
                        await replay.RunAsync(cts.Token).ConfigureAwait(false));

                    Assert.That(exception, Is.Not.Null);
                }
            }
        }

        [Test]
        public async Task RunAsyncThrowsForNonPositiveSpeed()
        {
            var source = new FiniteCaptureSource();
            await using (source.ConfigureAwait(false))
            {
                var replay = new MockClientReplay(source, "opc.tcp://127.0.0.1:1")
                {
                    Speed = 0.0
                };
                await using (replay.ConfigureAwait(false))
                {
                    ArgumentException? exception = Assert.ThrowsAsync<ArgumentException>(async () =>
                        await replay.RunAsync(CancellationToken.None).ConfigureAwait(false));

                    Assert.That(exception, Is.Not.Null);
                    Assert.That(exception!.Message, Does.Contain("speed"));
                }
            }
        }

        [Test]
        public async Task DisposeAsyncIsIdempotent()
        {
            var source = new FiniteCaptureSource();
            await using (source.ConfigureAwait(false))
            {
                var replay = new MockClientReplay(source, "opc.tcp://127.0.0.1:1");
                await using (replay.ConfigureAwait(false))
                {
                    Assert.DoesNotThrowAsync(async () => await replay.DisposeAsync().ConfigureAwait(false));
                    Assert.DoesNotThrowAsync(async () => await replay.DisposeAsync().ConfigureAwait(false));
                }
            }
        }

        private sealed class FiniteCaptureSource : ICaptureSource
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
