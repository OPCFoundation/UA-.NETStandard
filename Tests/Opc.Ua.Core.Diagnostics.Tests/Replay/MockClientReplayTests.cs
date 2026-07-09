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
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Pcap.Capture;
using Opc.Ua.Pcap.DependencyInjection;
using Opc.Ua.Pcap.Dissection;
using Opc.Ua.Pcap.Frame;
using Opc.Ua.Pcap.KeyLog;
using Opc.Ua.Pcap.Models;
using Opc.Ua.Pcap.Replay;

using Opc.Ua.Bindings;

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
                    Assert.That(
                        async () => await replay.DisposeAsync().ConfigureAwait(false),
                        Throws.Nothing);
                    Assert.That(
                        async () => await replay.DisposeAsync().ConfigureAwait(false),
                        Throws.Nothing);
                }
            }
        }

        [Test]
        public void ConstructorsValidateArgumentsAndEndpointPolicy()
        {
            var source = new FiniteCaptureSource();

            Assert.That(
                () => new MockClientReplay(null!, "opc.tcp://localhost:4840"),
                Throws.TypeOf<ArgumentNullException>().With.Property("ParamName").EqualTo("source"));
            Assert.That(
                () => new MockClientReplay(source, " "),
                Throws.TypeOf<ArgumentException>().With.Property("ParamName").EqualTo("targetEndpointUrl"));
            Assert.That(
                () => new MockClientReplay(source, "opc.tcp://localhost:4840", options: null!),
                Throws.TypeOf<ArgumentNullException>().With.Property("ParamName").EqualTo("options"));
            Assert.That(
                () => new MockClientReplay(source, "opc.tcp://localhost:4840", new PcapOptions()),
                Throws.TypeOf<PcapDiagnosticsException>().With.Message.Contains("disabled"));
            Assert.That(
                () => new MockClientReplay(
                    source,
                    "opc.tcp://localhost:4840",
                    new PcapOptions { AllowMockClientReplay = true }),
                Throws.TypeOf<PcapDiagnosticsException>().With.Message.Contains("empty"));
            Assert.That(
                () => new MockClientReplay(source, "not a uri", Allowing("localhost")),
                Throws.TypeOf<PcapDiagnosticsException>().With.Message.Contains("valid absolute URI"));
            Assert.That(
                () => new MockClientReplay(source, "https://localhost:4840", Allowing("localhost")),
                Throws.TypeOf<PcapDiagnosticsException>().With.Message.Contains("scheme"));
            Assert.That(
                () => new MockClientReplay(source, "opc.tcp://otherhost:4840", Allowing("localhost")),
                Throws.TypeOf<PcapDiagnosticsException>().With.Message.Contains("not in"));
            Assert.That(
                () => new MockClientReplay(source, "opc.tcp://LOCALHOST:4840", Allowing("localhost")),
                Throws.Nothing);
        }

        [Test]
        public async Task RunAsyncHonorsCanceledTokenBeforeSpeedValidation()
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
                    using var cts = new CancellationTokenSource();
                    cts.Cancel();

                    Assert.That(
                        async () => await replay.RunAsync(cts.Token).ConfigureAwait(false),
                        Throws.InstanceOf<OperationCanceledException>());
                }
            }
        }

        [Test]
        public async Task PrivateDelayUntilReplayTimeHandlesFirstZeroAndScaledDelay()
        {
            var source = new FiniteCaptureSource();
            await using (source.ConfigureAwait(false))
            {
                var replay = new MockClientReplay(source, "opc.tcp://127.0.0.1:1")
                {
                    Speed = double.MaxValue
                };
                await using (replay.ConfigureAwait(false))
                {
                    var previous = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

                    await InvokeDelayAsync(replay, null, previous, CancellationToken.None).ConfigureAwait(false);
                    await InvokeDelayAsync(replay, previous, previous, CancellationToken.None).ConfigureAwait(false);
                    await InvokeDelayAsync(replay, previous, previous.AddTicks(1), CancellationToken.None)
                        .ConfigureAwait(false);

                    using var cts = new CancellationTokenSource();
                    cts.Cancel();
                    Assert.That(
                        async () => await InvokeDelayAsync(
                            replay,
                            previous,
                            previous.AddMilliseconds(1),
                            cts.Token).ConfigureAwait(false),
                        Throws.InstanceOf<OperationCanceledException>());
                }
            }
        }

        [Test]
        public void PrivateTypeStatsAggregateCountsAndLatency()
        {
            var calls = new List<DecodedServiceCall>
            {
                new()
                {
                    RequestName = "ReadRequest",
                    RequestTimestamp = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
                    ResponseTimestamp = new DateTimeOffset(2026, 1, 1, 0, 0, 1, TimeSpan.Zero)
                },
                new()
                {
                    RequestName = "ReadRequest",
                    RequestTimestamp = new DateTimeOffset(2026, 1, 1, 0, 0, 2, TimeSpan.Zero),
                    ResponseTimestamp = new DateTimeOffset(2026, 1, 1, 0, 0, 5, TimeSpan.Zero)
                },
                new()
                {
                    RequestName = "BrowseRequest"
                },
                new()
            };

            object builders = InvokeStatic<object>("BuildTypeStats", calls);
            var results = InvokeStatic<List<MockReplayRequestTypeResult>>("CreateRequestTypeResults", builders);

            MockReplayRequestTypeResult read = results.Find(static result => result.RequestName == "ReadRequest")!;
            MockReplayRequestTypeResult browse = results.Find(static result => result.RequestName == "BrowseRequest")!;

            Assert.That(results, Has.Count.EqualTo(2));
            Assert.That(read.Count, Is.EqualTo(2));
            Assert.That(read.AverageLatency, Is.EqualTo(TimeSpan.FromSeconds(2)));
            Assert.That(browse.Count, Is.EqualTo(1));
            Assert.That(browse.AverageLatency, Is.Null);
        }

        [Test]
        public void PrivateConcatenatePreservesChunkOrder()
        {
            var chunks = new List<byte[]> { new byte[] { 1, 2 }, Array.Empty<byte>(), new byte[] { 3 } };

            byte[] bytes = InvokeStatic<byte[]>("Concatenate", chunks);

            Assert.That(bytes, Is.EqualTo(new byte[] { 1, 2, 3 }).AsCollection);
        }

        private static PcapOptions Allowing(string host)
        {
            return new PcapOptions
            {
                AllowMockClientReplay = true,
                AllowedReplayEndpoints = [host]
            };
        }

        private static async ValueTask InvokeDelayAsync(
            MockClientReplay replay,
            DateTimeOffset? previousTimestamp,
            DateTimeOffset currentTimestamp,
            CancellationToken ct)
        {
            MethodInfo method = typeof(MockClientReplay).GetMethod(
                "DelayUntilReplayTimeAsync",
                BindingFlags.NonPublic | BindingFlags.Instance)!;
            var task = (ValueTask)method.Invoke(
                replay,
                new object?[] { previousTimestamp, currentTimestamp, ct })!;
            await task.ConfigureAwait(false);
        }

        private static T InvokeStatic<T>(string methodName, params object?[] parameters)
        {
            MethodInfo method = typeof(MockClientReplay).GetMethod(
                methodName,
                BindingFlags.NonPublic | BindingFlags.Static)!;
            return (T)method.Invoke(null, parameters)!;
        }

        private sealed class FiniteCaptureSource : ICaptureSource
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
