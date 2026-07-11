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
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;
using NUnit.Framework;
using Opc.Ua.PubSub.Application;
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
using UadpEncoderV2 = Opc.Ua.PubSub.Encoding.Uadp.UadpEncoder;
using UadpNetworkMessageV2 = Opc.Ua.PubSub.Encoding.Uadp.UadpNetworkMessage;

namespace Opc.Ua.PubSub.Tests.Transcoding
{
    /// <summary>
    /// Coverage tests for the bridge, egress, connection send path, and
    /// DI hosted-service wiring.
    /// </summary>
    [TestFixture]
    public class TranscodingWiringCoverageTests
    {
        [Test]
        public async Task Egress_SendAsync_SendsEveryFrame()
        {
            var transport = new CapturingSendTransport();
            await using PubSubConnection target = NewConnection(transport);
            SetTransport(target, transport);
            var egress = new ConnectionTranscodeEgress(target);
            var result = new TranscodeResult
            {
                Frames = new List<ReadOnlyMemory<byte>>
                {
                    new byte[] { 1, 2 },
                    new byte[] { 3, 4 }
                }
            };

            await egress.SendAsync(result).ConfigureAwait(false);

            Assert.That(egress.TransportProfileUri, Is.EqualTo(Profiles.PubSubUdpUadpTransport));
            Assert.That(transport.Sent, Has.Count.EqualTo(2));
        }

        [Test]
        public async Task Egress_SendAsync_NullResult_Throws()
        {
            var transport = new CapturingSendTransport();
            await using PubSubConnection target = NewConnection(transport);
            SetTransport(target, transport);
            var egress = new ConnectionTranscodeEgress(target);

            Assert.That(async () => await egress.SendAsync(null!).ConfigureAwait(false),
                Throws.ArgumentNullException);
        }

        [Test]
        public async Task Egress_SendAsync_PromotesProperties_ToHeaderTransport()
        {
            var transport = new CapturingHeaderTransport();
            await using PubSubConnection target = NewConnection(transport);
            SetTransport(target, transport);
            var egress = new ConnectionTranscodeEgress(target);
            var result = new TranscodeResult
            {
                Frames = new List<ReadOnlyMemory<byte>> { new byte[] { 1, 2 } },
                Properties = new List<PubSubMessageProperty>
                {
                    new("Temperature", "21.5"),
                    new("Unit", "C")
                }
            };

            await egress.SendAsync(result, "topic/x").ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(transport.HeaderSends, Has.Count.EqualTo(1));
                Assert.That(transport.HeaderSends[0].Count, Is.EqualTo(2));
                Assert.That(transport.PlainSends, Is.Empty);
            });
        }

        [Test]
        public async Task Egress_SendAsync_PlainTransport_IgnoresProperties()
        {
            var transport = new CapturingSendTransport();
            await using PubSubConnection target = NewConnection(transport);
            SetTransport(target, transport);
            var egress = new ConnectionTranscodeEgress(target);
            var result = new TranscodeResult
            {
                Frames = new List<ReadOnlyMemory<byte>> { "\t"u8.ToArray() },
                Properties = new List<PubSubMessageProperty> { new("k", "v") }
            };

            await egress.SendAsync(result, "topic").ConfigureAwait(false);

            Assert.That(transport.Sent, Has.Count.EqualTo(1));
        }

        [Test]
        public async Task SendTranscodedFrame_OversizedUadp_IsChunked()
        {
            TranscodeContext context = NewContext();
            var transport = new CapturingSendTransport();
            await using PubSubConnection target = NewConnection(transport, maxNetworkMessageSize: 32);
            SetTransport(target, transport);

            var message = new UadpNetworkMessageV2
            {
                ContentMask = UadpNetworkMessageContentMask.PublisherId |
                    UadpNetworkMessageContentMask.PayloadHeader,
                PublisherId = PublisherId.FromByte(1),
                DataSetMessages =
                [
                    new PubSub.Encoding.Uadp.UadpDataSetMessage
                    {
                        DataSetWriterId = 1,
                        FieldEncoding = PubSubFieldEncoding.Variant,
                        Fields =
                        [
                            Field("a", new Variant(new byte[64])),
                            Field("b", new Variant(new byte[64]))
                        ]
                    }
                ]
            };
            ReadOnlyMemory<byte> frame = await new UadpEncoderV2()
                .EncodeAsync(message, context.EncodingContext).ConfigureAwait(false);

            await target.SendTranscodedFrameAsync(frame, null, CancellationToken.None)
                .ConfigureAwait(false);

            Assert.That(transport.Sent, Has.Count.GreaterThanOrEqualTo(2));
        }

        [Test]
        public async Task SendTranscodedFrame_NoTransport_DoesNotThrow()
        {
            await using PubSubConnection target = NewConnection(new CapturingSendTransport());

            Assert.That(async () => await target
                .SendTranscodedFrameAsync(new byte[] { 1, 2, 3 }, null, CancellationToken.None)
                .ConfigureAwait(false), Throws.Nothing);
        }

        [Test]
        public async Task ReceiveSinkRegistration_RegisterAndUnregister()
        {
            await using PubSubConnection source = NewConnection(new CapturingSendTransport());
            var sink = new Mock<IReceivedNetworkMessageSink>();

            IDisposable token1 = source.RegisterReceivedNetworkMessageSink(sink.Object);
            IDisposable token2 = source.RegisterReceivedNetworkMessageSink(sink.Object);
            token1.Dispose();
            token1.Dispose();
            token2.Dispose();

            Assert.That(() => source.RegisterReceivedNetworkMessageSink(null!),
                Throws.ArgumentNullException);
        }

        [Test]
        public async Task Bridge_TranscoderThrows_IsSwallowed()
        {
            var transcoder = new Mock<ITranscoder>();
            transcoder.Setup(t => t.TranscodeAsync(It.IsAny<TranscodeInput>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("boom"));
            var egress = new CapturingEgress();
            IReceivedNetworkMessageSink? sink = CaptureSink(out Mock<IPubSubConnection> source);
            await using var bridge = new PubSubTranscodingBridge(
                source.Object, transcoder.Object, egress, NUnitTelemetryContext.Create());
            bridge.Start();
            bridge.Start();

            await sink!.OnReceivedAsync(NewReceived()).ConfigureAwait(false);

            Assert.That(egress.Count, Is.Zero);
        }

        [Test]
        public async Task Bridge_EgressThrows_IsSwallowed()
        {
            var transcoder = new Mock<ITranscoder>();
            transcoder.Setup(t => t.TranscodeAsync(It.IsAny<TranscodeInput>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new TranscodeResult { Frames = new List<ReadOnlyMemory<byte>> { new byte[] { 1 } } });
            var egress = new Mock<IPubSubTranscodeEgress>();
            egress.Setup(e => e.SendAsync(It.IsAny<TranscodeResult>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("egress down"));
            IReceivedNetworkMessageSink? sink = CaptureSink(out Mock<IPubSubConnection> source);
            await using var bridge = new PubSubTranscodingBridge(
                source.Object, transcoder.Object, egress.Object, NUnitTelemetryContext.Create());
            bridge.Start();

            Assert.That(async () => await sink!.OnReceivedAsync(NewReceived()).ConfigureAwait(false),
                Throws.Nothing);
        }

        [Test]
        public async Task Bridge_NullReceived_NoOp()
        {
            IReceivedNetworkMessageSink? sink = CaptureSink(out Mock<IPubSubConnection> source);
            var egress = new CapturingEgress();
            await using var bridge = new PubSubTranscodingBridge(
                source.Object, new PubSubTranscoder(new TranscodeSpec(), TranscodingTestUtilities.Encoders(), NewContext()),
                egress, NUnitTelemetryContext.Create());
            bridge.Start();

            await sink!.OnReceivedAsync(null!).ConfigureAwait(false);

            Assert.That(egress.Count, Is.Zero);
            Assert.That(bridge.SourceConnectionName, Is.EqualTo("src"));
        }

        [Test]
        public void Bridge_NullArguments_Throw()
        {
            var source = new Mock<IPubSubConnection>();
            var transcoder = new Mock<ITranscoder>();
            var egress = new Mock<IPubSubTranscodeEgress>();
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            Assert.Multiple(() =>
            {
                Assert.That(() => new PubSubTranscodingBridge(
                    null!, transcoder.Object, egress.Object, telemetry), Throws.ArgumentNullException);
                Assert.That(() => new PubSubTranscodingBridge(
                    source.Object, null!, egress.Object, telemetry), Throws.ArgumentNullException);
                Assert.That(() => new PubSubTranscodingBridge(
                    source.Object, transcoder.Object, null!, telemetry), Throws.ArgumentNullException);
                Assert.That(() => new PubSubTranscodingBridge(
                    source.Object, transcoder.Object, egress.Object, null!), Throws.ArgumentNullException);
            });
        }

        [Test]
        public async Task HostedService_Start_WiresBridge_And_Stop_Disposes()
        {
            var transport = new CapturingSendTransport();
            await using PubSubConnection sourceConn = NewConnection(transport, name: "src");
            await using PubSubConnection targetConn = NewConnection(transport, name: "tgt");
            SetTransport(targetConn, transport);
            IServiceProvider provider = BuildProvider(sourceConn, targetConn);
            TranscodingBridgeDescriptor descriptor = new PubSubTranscoderBuilder()
                .From("src").To("tgt", TranscodeEncoding.Json).Build();
            var service = new PubSubTranscodingBridgeHostedService(provider, descriptor);

            await service.StartAsync(CancellationToken.None).ConfigureAwait(false);
            await service.StopAsync(CancellationToken.None).ConfigureAwait(false);

            Assert.Pass();
        }

        [Test]
        public async Task HostedService_MissingConnection_Throws()
        {
            var transport = new CapturingSendTransport();
            await using PubSubConnection sourceConn = NewConnection(transport, name: "src");
            await using PubSubConnection targetConn = NewConnection(transport, name: "tgt");
            IServiceProvider provider = BuildProvider(sourceConn, targetConn);
            TranscodingBridgeDescriptor descriptor = new PubSubTranscoderBuilder()
                .From("missing").To("tgt", TranscodeEncoding.Json).Build();
            var service = new PubSubTranscodingBridgeHostedService(provider, descriptor);

            Assert.That(async () => await service.StartAsync(CancellationToken.None).ConfigureAwait(false),
                Throws.InvalidOperationException);
        }

        [Test]
        public void AddTranscodingBridge_RegistersHostedService()
        {
            var services = new ServiceCollection();
            services.AddSingleton(NUnitTelemetryContext.Create());
            services.AddLogging();
            services.AddOpcUa().AddPubSub(pubsub => pubsub
                .AddTranscodingBridge(b => b.From("a").To("b", TranscodeEncoding.Json)));

            bool registered = System.Linq.Enumerable.Any(services, d =>
                d.ServiceType == typeof(IHostedService) &&
                (d.ImplementationFactory is not null || d.ImplementationType is not null));
            Assert.That(registered, Is.True);
        }

        [Test]
        public void AddTranscodingBridge_NullArguments_Throw()
        {
            var services = new ServiceCollection();
            services.AddSingleton(NUnitTelemetryContext.Create());
            services.AddLogging();
            IPubSubBuilder? captured = null;
            services.AddOpcUa().AddPubSub(pubsub => captured = pubsub);

            Assert.Multiple(() =>
            {
                Assert.That(() => captured!.AddTranscodingBridge(null!), Throws.ArgumentNullException);
                Assert.That(() => PubSubTranscodingBuilderExtensions.AddTranscodingBridge(
                    null!, _ => { }), Throws.ArgumentNullException);
            });
        }

        private static ReceivedNetworkMessage NewReceived()
        {
            return new ReceivedNetworkMessage
            {
                Message = NewUadpMessage(
                    PublisherId.FromByte(1), 10, 100, Field("a", new Variant(1))),
                SourceConnectionName = "src"
            };
        }

        private static LateSink? CaptureSink(out Mock<IPubSubConnection> source)
        {
            IReceivedNetworkMessageSink? captured = null;
            var mock = new Mock<IPubSubConnection>();
            mock.SetupGet(c => c.Name).Returns("src");
            mock.Setup(c => c.RegisterReceivedNetworkMessageSink(It.IsAny<IReceivedNetworkMessageSink>()))
                .Callback<IReceivedNetworkMessageSink>(s => captured = s)
                .Returns(Mock.Of<IDisposable>());
            source = mock;
            // Return a late-bound accessor: the sink is captured on Start().
            return new LateSink(() => captured);
        }

        private static ServiceProvider BuildProvider(
            PubSubConnection source,
            PubSubConnection target)
        {
            var app = new Mock<IPubSubApplication>();
            app.SetupGet(a => a.Connections).Returns([source, target]);
            app.SetupGet(a => a.MetaDataRegistry).Returns(new DataSetMetaDataRegistry());
            app.SetupGet(a => a.Diagnostics).Returns(new PubSubDiagnostics(PubSubDiagnosticsLevel.Low));

            var services = new ServiceCollection();
            services.AddSingleton(NUnitTelemetryContext.Create());
            services.AddSingleton(TimeProvider.System);
            services.AddSingleton<INetworkMessageEncoder>(new UadpEncoderV2());
            services.AddSingleton<INetworkMessageEncoder>(new PubSub.Encoding.Json.JsonEncoder());
            services.AddSingleton(app.Object);
            return services.BuildServiceProvider();
        }

        private static PubSubConnection NewConnection(
            IPubSubTransport transport,
            int maxNetworkMessageSize = 0,
            string name = "conn")
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var readerGroup = new ReaderGroup(
                new ReaderGroupDataType { Name = "rg" },
                Array.Empty<DataSetReader>(),
                telemetry);
            return new PubSubConnection(
                new PubSubConnectionDataType
                {
                    Name = name,
                    TransportProfileUri = Profiles.PubSubUdpUadpTransport
                },
                new SingleTransportFactory(transport),
                new Dictionary<string, INetworkMessageEncoder>(),
                new Dictionary<string, INetworkMessageDecoder>(),
                Array.Empty<WriterGroup>(),
                new[] { readerGroup },
                new DataSetMetaDataRegistry(),
                new PubSubDiagnostics(PubSubDiagnosticsLevel.Low),
                telemetry,
                TimeProvider.System,
                securityWrapper: null,
                UadpSecurityWrapOptions.SignAndEncrypt,
                maxNetworkMessageSize,
                MessageSecurityMode.None);
        }

        private static void SetTransport(PubSubConnection connection, IPubSubTransport transport)
        {
            FieldInfo field = typeof(PubSubConnection).GetField(
                "m_transport", BindingFlags.Instance | BindingFlags.NonPublic)!;
            field.SetValue(connection, transport);
        }

        private sealed class LateSink : IReceivedNetworkMessageSink
        {
            private readonly Func<IReceivedNetworkMessageSink?> m_accessor;

            public LateSink(Func<IReceivedNetworkMessageSink?> accessor)
            {
                m_accessor = accessor;
            }

            public ValueTask OnReceivedAsync(
                ReceivedNetworkMessage received,
                CancellationToken cancellationToken = default)
            {
                return m_accessor()!.OnReceivedAsync(received, cancellationToken);
            }
        }

        private sealed class CapturingEgress : IPubSubTranscodeEgress
        {
            private int m_count;

            public int Count => m_count;

            public string TransportProfileUri => Profiles.PubSubMqttJsonTransport;

            public ValueTask SendAsync(
                TranscodeResult result,
                string? topic = null,
                CancellationToken cancellationToken = default)
            {
                Interlocked.Increment(ref m_count);
                return default;
            }
        }

        private sealed class SingleTransportFactory : IPubSubTransportFactory
        {
            private readonly IPubSubTransport m_transport;

            public SingleTransportFactory(IPubSubTransport transport)
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

        private sealed class CapturingSendTransport : IPubSubTransport
        {
            public List<byte[]> Sent { get; } = [];

            public string TransportProfileUri => Profiles.PubSubUdpUadpTransport;

            public PubSubTransportDirection Direction => PubSubTransportDirection.Send;

            public bool IsConnected => true;

            public event EventHandler<PubSubTransportStateChangedEventArgs>? StateChanged
            {
                add { }
                remove { }
            }

            public ValueTask OpenAsync(CancellationToken cancellationToken = default)
            {
                return default;
            }

            public ValueTask CloseAsync(CancellationToken cancellationToken = default)
            {
                return default;
            }

            public ValueTask SendAsync(
                ReadOnlyMemory<byte> payload,
                string? topic = null,
                CancellationToken cancellationToken = default)
            {
                Sent.Add(payload.ToArray());
                return default;
            }

#pragma warning disable CS1998 // async method lacks awaits — empty async sequence by design
            public async IAsyncEnumerable<PubSubTransportFrame> ReceiveAsync(
                [System.Runtime.CompilerServices.EnumeratorCancellation]
                CancellationToken cancellationToken = default)
            {
                yield break;
            }
#pragma warning restore CS1998

            public ValueTask DisposeAsync()
            {
                return default;
            }
        }

        private sealed class CapturingHeaderTransport
            : IPubSubTransport, IPubSubHeaderTransport
        {
            public List<byte[]> PlainSends { get; } = [];

            public List<ArrayOf<PubSubMessageProperty>> HeaderSends { get; } = [];

            public string TransportProfileUri => Profiles.PubSubMqttJsonTransport;

            public PubSubTransportDirection Direction => PubSubTransportDirection.Send;

            public bool IsConnected => true;

            public event EventHandler<PubSubTransportStateChangedEventArgs>? StateChanged
            {
                add { }
                remove { }
            }

            public ValueTask OpenAsync(CancellationToken cancellationToken = default)
            {
                return default;
            }

            public ValueTask CloseAsync(CancellationToken cancellationToken = default)
            {
                return default;
            }

            public ValueTask SendAsync(
                ReadOnlyMemory<byte> payload,
                string? topic = null,
                CancellationToken cancellationToken = default)
            {
                PlainSends.Add(payload.ToArray());
                return default;
            }

            public ValueTask SendAsync(
                ReadOnlyMemory<byte> payload,
                string? topic,
                ArrayOf<PubSubMessageProperty> properties,
                CancellationToken cancellationToken = default)
            {
                HeaderSends.Add(properties);
                return default;
            }

#pragma warning disable CS1998 // async method lacks awaits — empty async sequence by design
            public async IAsyncEnumerable<PubSubTransportFrame> ReceiveAsync(
                [System.Runtime.CompilerServices.EnumeratorCancellation]
                CancellationToken cancellationToken = default)
            {
                yield break;
            }
#pragma warning restore CS1998

            public ValueTask DisposeAsync()
            {
                return default;
            }
        }
    }
}
