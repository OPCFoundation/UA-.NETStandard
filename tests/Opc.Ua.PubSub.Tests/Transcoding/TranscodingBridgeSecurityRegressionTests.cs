/* Copyright (c) 2026 The OPC Foundation, Inc. All rights reserved.
 * Licensed under the MIT License. See LICENSE.txt in the project root.
 */

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NUnit.Framework;
using Opc.Ua.PubSub.Application;
using Opc.Ua.PubSub.Connections;
using Opc.Ua.PubSub.Diagnostics;
using Opc.Ua.PubSub.Encoding;
using Opc.Ua.PubSub.Groups;
using Opc.Ua.PubSub.MetaData;
using Opc.Ua.PubSub.Security;
using Opc.Ua.PubSub.Security.Policies;
using Opc.Ua.PubSub.Tests.Security;
using Opc.Ua.PubSub.Transcoding;
using Opc.Ua.PubSub.Transports;
using Opc.Ua.Tests;
using JsonDataSetMessageV2 = Opc.Ua.PubSub.Encoding.Json.JsonDataSetMessage;
using JsonNetworkMessageV2 = Opc.Ua.PubSub.Encoding.Json.JsonNetworkMessage;
using UadpDecoder = Opc.Ua.PubSub.Encoding.Uadp.UadpDecoder;
using UadpEncoder = Opc.Ua.PubSub.Encoding.Uadp.UadpEncoder;
using static Opc.Ua.PubSub.Tests.Transcoding.TranscodingTestUtilities;

namespace Opc.Ua.PubSub.Tests.Transcoding
{
    /// <summary>
    /// Exercises real bridge activation and target transport egress with independently configured DI security.
    /// </summary>
    [TestFixture]
    [Category("SecurityRegression")]
    [CancelAfter(10000)]
    public sealed class TranscodingBridgeSecurityRegressionTests
    {
        /// <summary>
        /// Activation uses the connection's actual wrapper even when the DI resolver cannot supply its keys.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task SecurityR26BridgeUsesTargetEffectiveSecurityContextAsync(bool allowInsecure)
        {
            using var ring = new PubSubSecurityKeyRing("target");
            ring.SetCurrent(TestSecurityKeyFactory.Create(1));
            using var nonce = new RandomNonceProvider(PublisherId.FromByte(1));
            var window = new SecurityTokenWindow();
            window.RegisterToken(1);
            var wrapper = new UadpSecurityWrapper(PubSubAes128CtrPolicy.Instance,
                new StaticSecurityKeyProvider("target", ring), nonce, window,
                NUnitTelemetryContext.Create());
            var transport = new CapturingTransport();
            await using PubSubConnection target = NewTarget(transport, wrapper);
            await target.EnableAsync().ConfigureAwait(false);
            IReceivedNetworkMessageSink? sink = null;
            var source = new Mock<IPubSubConnection>();
            source.SetupGet(c => c.Name).Returns("source");
            source.Setup(c => c.RegisterReceivedNetworkMessageSink(It.IsAny<IReceivedNetworkMessageSink>()))
                .Callback<IReceivedNetworkMessageSink>(value => sink = value).Returns(Mock.Of<IDisposable>());
            var app = new Mock<IPubSubApplication>();
            app.SetupGet(a => a.Connections).Returns(new[] { source.Object, target });
            app.SetupGet(a => a.MetaDataRegistry).Returns(new DataSetMetaDataRegistry());
            app.SetupGet(a => a.Diagnostics).Returns(new PubSubDiagnostics(PubSubDiagnosticsLevel.Low));
            var wrongResolver = new Mock<IPubSubSecurityWrapperResolver>(MockBehavior.Strict);
            var services = new ServiceCollection();
            services.AddSingleton(app.Object);
            services.AddSingleton(NUnitTelemetryContext.Create());
            services.AddSingleton(TimeProvider.System);
            services.AddSingleton(wrongResolver.Object);
            services.AddSingleton<INetworkMessageEncoder>(new UadpEncoder());
            await using ServiceProvider provider = services.BuildServiceProvider();
            var descriptor = new TranscodingBridgeDescriptor(
                "source", "target", new TranscodeSpec { TargetEncoding = TranscodeEncoding.Uadp },
                allowInsecure, null);
            await using PubSubTranscodingBridge bridge = new TranscodingBridgeActivator(provider).Create(descriptor);
            bridge.Start();
            await sink!.OnReceivedAsync(new ReceivedNetworkMessage
            {
                Message = new JsonNetworkMessageV2
                {
                    PublisherId = PublisherId.FromByte(4),
                    WriterGroupId = 8,
                    DataSetMessages =
                    [
                        new JsonDataSetMessageV2
                        {
                            DataSetWriterId = 12,
                            MessageType = PubSubDataSetMessageType.KeyFrame,
                            Fields = [Field("value", new Variant(42))]
                        }
                    ]
                },
                SourceTransportProfileUri = Profiles.PubSubMqttJsonTransport,
                FrameSecured = false
            }).ConfigureAwait(false);

            Assert.That(transport.Frames, Has.Count.EqualTo(1));
            ReadOnlyMemory<byte> encrypted = transport.Frames[0];
            Assert.That(UadpDecoder.TryReadOuterPrefix(encrypted, out int prefixLength,
                out bool secured, out _, out _), Is.True);
            Assert.That(secured, Is.True);
            UadpSecurityWrapper.UnwrapResult unwrapped = await wrapper.TryUnwrapAsync(
                encrypted[..prefixLength], encrypted[prefixLength..]).ConfigureAwait(false);
            Assert.That(unwrapped.IsSuccess, Is.True, unwrapped.Reason);
            byte[] clear = new byte[prefixLength + unwrapped.InnerPayload!.Value.Length];
            encrypted.Span[..prefixLength].CopyTo(clear);
            unwrapped.InnerPayload.Value.Span.CopyTo(clear.AsSpan(prefixLength));
            var decoded = await DecodeUadpAsync(clear, NewContext()).ConfigureAwait(false);
            Assert.That(decoded.DataSetMessages[0].Fields[0].Value, Is.EqualTo(new Variant(42)));
            Assert.That(decoded.PublisherId, Is.EqualTo(PublisherId.FromByte(4)));
            Assert.That(decoded.WriterGroupId, Is.EqualTo(8));
            wrongResolver.Verify(r => r.Resolve(It.IsAny<PubSubConnectionDataType>()), Times.Never);
        }

        /// <summary>
        /// An unsecured raw frame cannot bypass an encrypted connection through bridge egress.
        /// </summary>
        [Test]
        public async Task SecurityR26BridgeCannotSendPlaintextToEncryptedTargetAsync()
        {
            using var ring = new PubSubSecurityKeyRing("target");
            ring.SetCurrent(TestSecurityKeyFactory.Create(1));
            using var nonce = new RandomNonceProvider(PublisherId.FromByte(1));
            var wrapper = new UadpSecurityWrapper(PubSubAes128CtrPolicy.Instance,
                new StaticSecurityKeyProvider("target", ring), nonce, new SecurityTokenWindow(),
                NUnitTelemetryContext.Create());
            var transport = new CapturingTransport();
            await using PubSubConnection target = NewTarget(transport, wrapper);
            await target.EnableAsync().ConfigureAwait(false);
            ReadOnlyMemory<byte> plaintext = await new UadpEncoder().EncodeAsync(
                NewUadpMessage(PublisherId.FromByte(1), 8, 12, Field("value", new Variant(42))),
                NewContext().EncodingContext).ConfigureAwait(false);
            var egress = new ConnectionTranscodeEgress(target);
            await Assert.ThatAsync(async () => await egress.SendAsync(new TranscodeResult
            {
                Frames = [plaintext]
            }).ConfigureAwait(false), Throws.TypeOf<ServiceResultException>()
                .With.Property(nameof(ServiceResultException.StatusCode)).EqualTo(StatusCodes.BadSecurityModeInsufficient));
            Assert.That(transport.Frames, Is.Empty);
        }

        /// <summary>
        /// Whole-result preflight rejects a weaker second frame before any first frame or chunk is sent.
        /// </summary>
        [TestCase(UadpSecurityWrapOptions.SignOnly)]
        [TestCase(UadpSecurityWrapOptions.SignAndEncrypt)]
        public async Task SecurityR26RawAndChunkedEgressCannotUndercutTargetModeAsync(UadpSecurityWrapOptions options)
        {
            using var ring = new PubSubSecurityKeyRing("target");
            ring.SetCurrent(TestSecurityKeyFactory.Create(1));
            using var nonce = new RandomNonceProvider(PublisherId.FromByte(1));
            var wrapper = new UadpSecurityWrapper(PubSubAes128CtrPolicy.Instance,
                new StaticSecurityKeyProvider("target", ring), nonce, new SecurityTokenWindow(),
                NUnitTelemetryContext.Create());
            var transport = new CapturingTransport();
            await using PubSubConnection target = NewTarget(transport, wrapper, maxFrameSize: 64);
            await target.EnableAsync().ConfigureAwait(false);
            var message = NewUadpMessage(
                PublisherId.FromByte(1), 8, 12, Field("value", new Variant(new string('x', 256))));
            ReadOnlyMemory<byte> encoded = UadpEncoder.EncodeWithSecurityBoundary(
                message, NewContext().EncodingContext, out int boundary);
            ReadOnlyMemory<byte> first = await wrapper.WrapAsync(encoded[..boundary], encoded[boundary..], options)
                .ConfigureAwait(false);
            ReadOnlyMemory<byte> plaintext = await new UadpEncoder().EncodeAsync(
                message, NewContext().EncodingContext).ConfigureAwait(false);
            Assert.That(plaintext.Length, Is.GreaterThan(64), "This frame would otherwise take the chunking path.");
            var egress = new ConnectionTranscodeEgress(target);
            await Assert.ThatAsync(async () => await egress.SendAsync(new TranscodeResult
            {
                Frames = [first, plaintext]
            }).ConfigureAwait(false), Throws.TypeOf<ServiceResultException>());
            Assert.That(transport.Frames, Is.Empty);
        }

        /// <summary>
        /// Legitimate plaintext, signed, and encrypted outputs retain their configured modes without double wrapping.
        /// </summary>
        [TestCase(MessageSecurityMode.None)]
        [TestCase(MessageSecurityMode.Sign)]
        [TestCase(MessageSecurityMode.SignAndEncrypt)]
        public async Task SecurityR26TargetModeMatrixPreservesAuthorizedEgressAsync(MessageSecurityMode mode)
        {
            using var ring = new PubSubSecurityKeyRing("target");
            ring.SetCurrent(TestSecurityKeyFactory.Create(1));
            using var nonce = new RandomNonceProvider(PublisherId.FromByte(1));
            var wrapper = new UadpSecurityWrapper(PubSubAes128CtrPolicy.Instance,
                new StaticSecurityKeyProvider("target", ring), nonce, new SecurityTokenWindow(),
                NUnitTelemetryContext.Create());
            var transport = new CapturingTransport();
            await using PubSubConnection target = NewTarget(
                transport, mode == MessageSecurityMode.None ? null : wrapper, mode: mode);
            await target.EnableAsync().ConfigureAwait(false);
            var message = NewUadpMessage(PublisherId.FromByte(1), 8, 12, Field("value", new Variant(42)));
            ReadOnlyMemory<byte> frame;
            if (mode == MessageSecurityMode.None)
            {
                frame = await new UadpEncoder().EncodeAsync(message, NewContext().EncodingContext).ConfigureAwait(false);
            }
            else
            {
                ReadOnlyMemory<byte> encoded = UadpEncoder.EncodeWithSecurityBoundary(
                    message, NewContext().EncodingContext, out int boundary);
                frame = await wrapper.WrapAsync(encoded[..boundary], encoded[boundary..],
                    mode == MessageSecurityMode.Sign ? UadpSecurityWrapOptions.SignOnly :
                        UadpSecurityWrapOptions.SignAndEncrypt).ConfigureAwait(false);
            }
            await new ConnectionTranscodeEgress(target).SendAsync(new TranscodeResult { Frames = [frame] })
                .ConfigureAwait(false);
            Assert.That(transport.Frames, Has.Count.EqualTo(1));
            Assert.That(transport.Frames[0].ToArray(), Is.EqualTo(frame.ToArray()));
        }

        /// <summary>
        /// Claimed encryption flags are insufficient without an authentic signature from the target context.
        /// </summary>
        [Test]
        public async Task SecurityR26SecurityFlagsWithoutTargetSignatureCannotAuthorizeEgressAsync()
        {
            using var ring = new PubSubSecurityKeyRing("target");
            ring.SetCurrent(TestSecurityKeyFactory.Create(1));
            using var nonce = new RandomNonceProvider(PublisherId.FromByte(1));
            var wrapper = new UadpSecurityWrapper(PubSubAes128CtrPolicy.Instance,
                new StaticSecurityKeyProvider("target", ring), nonce, new SecurityTokenWindow(),
                NUnitTelemetryContext.Create());
            var transport = new CapturingTransport();
            await using PubSubConnection target = NewTarget(transport, wrapper);
            await target.EnableAsync().ConfigureAwait(false);
            ReadOnlyMemory<byte> encoded = UadpEncoder.EncodeWithSecurityBoundary(
                NewUadpMessage(PublisherId.FromByte(1), 8, 12, Field("value", new Variant(42))),
                NewContext().EncodingContext, out int boundary);
            byte[] forged = (await wrapper.WrapAsync(encoded[..boundary], encoded[boundary..])
                .ConfigureAwait(false)).ToArray();
            forged[^1] ^= 1;
            await Assert.ThatAsync(async () => await new ConnectionTranscodeEgress(target)
                .SendAsync(new TranscodeResult { Frames = [forged] }).ConfigureAwait(false),
                Throws.TypeOf<ServiceResultException>());
            Assert.That(transport.Frames, Is.Empty);
        }

        private static PubSubConnection NewTarget(
            CapturingTransport transport, UadpSecurityWrapper? wrapper, int maxFrameSize = 0,
            MessageSecurityMode mode = MessageSecurityMode.SignAndEncrypt)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var factory = new Mock<IPubSubTransportFactory>();
            factory.SetupGet(f => f.TransportProfileUri).Returns(Profiles.PubSubUdpUadpTransport);
            factory.Setup(f => f.Create(It.IsAny<PubSubConnectionDataType>(),
                It.IsAny<ITelemetryContext>(), It.IsAny<TimeProvider>())).Returns(transport);
            return new PubSubConnection(
                new PubSubConnectionDataType { Name = "target", TransportProfileUri = Profiles.PubSubUdpUadpTransport },
                factory.Object, new Dictionary<string, INetworkMessageEncoder>(),
                new Dictionary<string, INetworkMessageDecoder>(), Array.Empty<WriterGroup>(),
                new[] { new ReaderGroup(new ReaderGroupDataType(), Array.Empty<DataSetReader>(), telemetry) },
                new DataSetMetaDataRegistry(), new PubSubDiagnostics(PubSubDiagnosticsLevel.Low),
                telemetry, TimeProvider.System, wrapper,
                mode == MessageSecurityMode.Sign ? UadpSecurityWrapOptions.SignOnly :
                    UadpSecurityWrapOptions.SignAndEncrypt, maxFrameSize, mode);
        }

        private sealed class CapturingTransport : IPubSubTransport
        {
            public List<ReadOnlyMemory<byte>> Frames { get; } = [];
            public string TransportProfileUri => Profiles.PubSubUdpUadpTransport;
            public PubSubTransportDirection Direction => PubSubTransportDirection.SendReceive;
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
                ReadOnlyMemory<byte> payload, string? topic = null, CancellationToken cancellationToken = default)
            {
                Frames.Add(payload.ToArray());
                return default;
            }

            public async IAsyncEnumerable<PubSubTransportFrame> ReceiveAsync(
                [EnumeratorCancellation] CancellationToken cancellationToken = default)
            {
                await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
                yield break;
            }

            public ValueTask DisposeAsync()
            {
                return default;
            }
        }
    }
}
