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
using Moq;
using NUnit.Framework;
using Opc.Ua.PubSub.Connections;
using Opc.Ua.PubSub.Diagnostics;
using Opc.Ua.PubSub.Encoding;
using Opc.Ua.PubSub.Groups;
using Opc.Ua.PubSub.MetaData;
using Opc.Ua.PubSub.Security;
using Opc.Ua.PubSub.Transcoding;
using Opc.Ua.PubSub.Transports;
using Opc.Ua.Tests;
using static Opc.Ua.PubSub.Tests.Transcoding.TranscodingTestUtilities;
using JsonNetworkMessageV2 = Opc.Ua.PubSub.Encoding.Json.JsonNetworkMessage;
using UadpDecoderV2 = Opc.Ua.PubSub.Encoding.Uadp.UadpDecoder;
using UadpEncoderV2 = Opc.Ua.PubSub.Encoding.Uadp.UadpEncoder;
using UadpNetworkMessageV2 = Opc.Ua.PubSub.Encoding.Uadp.UadpNetworkMessage;

namespace Opc.Ua.PubSub.Tests.Transcoding
{
    /// <summary>
    /// End-to-end integration tests for the transcoding bridge: a real
    /// <see cref="PubSubConnection"/> receive path drives the receive
    /// hook, the transcoder, and the egress.
    /// </summary>
    [TestFixture]
    [CancelAfter(15000)]
    public sealed class PubSubTranscodingBridgeIntegrationTests
    {
        [Test]
        public async Task ReceivedUadpFrame_TranscodesToJson_AndReachesEgress()
        {
            TranscodeContext context = NewContext();
            byte[] frame = await BuildUadpFrameAsync(context, value: 42).ConfigureAwait(false);
            var transport = new LoopbackReceiveTransport([frame]);
            await using PubSubConnection source = NewReceiveConnection(transport);

            var spec = new TranscodeSpec
            {
                TargetEncoding = TranscodeEncoding.Json,
                Transforms = [new IdRemapTransform(PublisherId.FromUInt16(7))]
            };
            var transcoder = new PubSubTranscoder(
                spec, TranscodingTestUtilities.Encoders(), context);
            var egress = new CapturingEgress();
            await using var bridge = new PubSubTranscodingBridge(
                source, transcoder, egress, NUnitTelemetryContext.Create());
            bridge.Start();

            await source.EnableAsync().ConfigureAwait(false);
            await transport.WaitUntilDrainedAsync().ConfigureAwait(false);
            await egress.WaitForFrameAsync().ConfigureAwait(false);
            await source.DisableAsync().ConfigureAwait(false);

            Assert.That(egress.Frames, Has.Count.EqualTo(1));
            JsonNetworkMessageV2 decoded = await DecodeJsonAsync(egress.Frames[0], context)
                .ConfigureAwait(false);
            Assert.That(decoded.DataSetMessages, Has.Count.EqualTo(1));
            Assert.That(decoded.DataSetMessages[0].Fields[0].Value, Is.EqualTo(new Variant(42)));
        }

        [Test]
        public async Task Bridge_TransformDropsMessage_EgressNotInvoked()
        {
            IReceivedNetworkMessageSink? capturedSink = null;
            var registration = new Mock<IDisposable>();
            var source = new Mock<IPubSubConnection>();
            source.SetupGet(c => c.Name).Returns("src");
            source.Setup(c => c.RegisterReceivedNetworkMessageSink(
                    It.IsAny<IReceivedNetworkMessageSink>()))
                .Callback<IReceivedNetworkMessageSink>(sink => capturedSink = sink)
                .Returns(registration.Object);

            TranscodeContext context = NewContext();
            var spec = new TranscodeSpec
            {
                TargetEncoding = TranscodeEncoding.Json,
                Transforms = [DelegateMessageTransform.FromSync(_ => null)]
            };
            var transcoder = new PubSubTranscoder(
                spec, TranscodingTestUtilities.Encoders(), context);
            var egress = new CapturingEgress();
            await using var bridge = new PubSubTranscodingBridge(
                source.Object, transcoder, egress, NUnitTelemetryContext.Create());
            bridge.Start();

            var received = new ReceivedNetworkMessage
            {
                Message = NewUadpMessage(
                    PublisherId.FromByte(1), 10, 100, Field("a", new Variant(1)))
            };
            await capturedSink!.OnReceivedAsync(received).ConfigureAwait(false);

            Assert.That(egress.Frames, Is.Empty);
        }

        [Test]
        public async Task Bridge_Dispose_RemovesRegistration()
        {
            var registration = new Mock<IDisposable>();
            var source = new Mock<IPubSubConnection>();
            source.SetupGet(c => c.Name).Returns("src");
            source.Setup(c => c.RegisterReceivedNetworkMessageSink(
                    It.IsAny<IReceivedNetworkMessageSink>()))
                .Returns(registration.Object);

            TranscodeContext context = NewContext();
            var transcoder = new PubSubTranscoder(
                new TranscodeSpec(), TranscodingTestUtilities.Encoders(), context);
            var bridge = new PubSubTranscodingBridge(
                source.Object, transcoder, new CapturingEgress(),
                NUnitTelemetryContext.Create());
            bridge.Start();

            await bridge.DisposeAsync().ConfigureAwait(false);

            registration.Verify(r => r.Dispose(), Times.Once);
        }

        [Test]
        public void ConnectionTranscodeEgress_RejectsNonDefaultConnection()
        {
            var fake = new Mock<IPubSubConnection>();

            Assert.That(
                () => new ConnectionTranscodeEgress(fake.Object),
                Throws.ArgumentException);
        }

        private static async Task<byte[]> BuildUadpFrameAsync(TranscodeContext context, int value)
        {
            UadpNetworkMessageV2 message = NewUadpMessage(
                PublisherId.FromByte(1), 10, 100, Field("v", new Variant(value)));
            ReadOnlyMemory<byte> encoded = await new UadpEncoderV2()
                .EncodeAsync(message, context.EncodingContext)
                .ConfigureAwait(false);
            return encoded.ToArray();
        }

        private static PubSubConnection NewReceiveConnection(LoopbackReceiveTransport transport)
        {
            var cfg = new PubSubConnectionDataType
            {
                Name = "src",
                TransportProfileUri = Profiles.PubSubUdpUadpTransport
            };
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var readerGroup = new ReaderGroup(
                new ReaderGroupDataType { Name = "rg" },
                Array.Empty<DataSetReader>(),
                telemetry);
            return new PubSubConnection(
                cfg,
                new LoopbackTransportFactory(transport),
                new Dictionary<string, INetworkMessageEncoder>(),
                new Dictionary<string, INetworkMessageDecoder>
                {
                    [Profiles.PubSubUdpUadpTransport] = new UadpDecoderV2()
                },
                Array.Empty<WriterGroup>(),
                new[] { readerGroup },
                new DataSetMetaDataRegistry(),
                new PubSubDiagnostics(PubSubDiagnosticsLevel.Low),
                telemetry,
                TimeProvider.System,
                securityWrapper: null,
                UadpSecurityWrapOptions.SignAndEncrypt,
                maxNetworkMessageSize: 0,
                MessageSecurityMode.None);
        }

        private sealed class CapturingEgress : IPubSubTranscodeEgress
        {
            private readonly TaskCompletionSource<bool> m_signal =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            public List<ReadOnlyMemory<byte>> Frames { get; } = [];

            public string TransportProfileUri => Profiles.PubSubMqttJsonTransport;

            public ValueTask SendAsync(
                TranscodeResult result,
                string? topic = null,
                CancellationToken cancellationToken = default)
            {
                for (int i = 0; i < result.Frames.Count; i++)
                {
                    Frames.Add(result.Frames[i]);
                }
                m_signal.TrySetResult(true);
                return default;
            }

            public async Task WaitForFrameAsync()
            {
                Task completed = await Task.WhenAny(
                    m_signal.Task, Task.Delay(TimeSpan.FromSeconds(10))).ConfigureAwait(false);
                Assert.That(completed, Is.SameAs(m_signal.Task),
                    "Timed out waiting for the egress to receive a transcoded frame.");
            }
        }

        private sealed class LoopbackTransportFactory : IPubSubTransportFactory
        {
            private readonly LoopbackReceiveTransport m_transport;

            public LoopbackTransportFactory(LoopbackReceiveTransport transport)
            {
                m_transport = transport;
            }

            public string TransportProfileUri => Profiles.PubSubUdpUadpTransport;

            public IPubSubTransport Create(
                PubSubConnectionDataType connection,
                ITelemetryContext telemetry,
                TimeProvider timeProvider)
            {
                return m_transport;
            }
        }

        private sealed class LoopbackReceiveTransport : IPubSubTransport
        {
            private readonly IReadOnlyList<byte[]> m_frames;

            private readonly TaskCompletionSource<bool> m_drained =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            public LoopbackReceiveTransport(IReadOnlyList<byte[]> frames)
            {
                m_frames = frames;
            }

            public string TransportProfileUri => Profiles.PubSubUdpUadpTransport;

            public PubSubTransportDirection Direction => PubSubTransportDirection.Receive;

            public bool IsConnected { get; private set; }

            public event EventHandler<PubSubTransportStateChangedEventArgs>? StateChanged
            {
                add { }
                remove { }
            }

            public ValueTask OpenAsync(CancellationToken cancellationToken = default)
            {
                IsConnected = true;
                return default;
            }

            public ValueTask CloseAsync(CancellationToken cancellationToken = default)
            {
                IsConnected = false;
                return default;
            }

            public ValueTask SendAsync(
                ReadOnlyMemory<byte> payload,
                string? topic = null,
                CancellationToken cancellationToken = default)
            {
                return default;
            }

            public async IAsyncEnumerable<PubSubTransportFrame> ReceiveAsync(
                [EnumeratorCancellation] CancellationToken cancellationToken = default)
            {
                foreach (byte[] frame in m_frames)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    yield return new PubSubTransportFrame(
                        frame, null, DateTimeUtc.From(DateTime.UtcNow));
                }
                m_drained.TrySetResult(true);
                try
                {
                    await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                }
            }

            public ValueTask DisposeAsync()
            {
                IsConnected = false;
                m_drained.TrySetResult(true);
                return default;
            }

            public async Task WaitUntilDrainedAsync()
            {
                Task completed = await Task.WhenAny(
                    m_drained.Task, Task.Delay(TimeSpan.FromSeconds(10))).ConfigureAwait(false);
                Assert.That(completed, Is.SameAs(m_drained.Task),
                    "Timed out waiting for the transport to drain.");
                await Task.Delay(50).ConfigureAwait(false);
            }
        }
    }
}
