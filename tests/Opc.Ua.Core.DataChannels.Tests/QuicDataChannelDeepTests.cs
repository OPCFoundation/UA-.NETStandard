/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 * ======================================================================*/

using System;
#if NET9_0_OR_GREATER
using System.Buffers.Binary;
using System.Net;
using System.Net.Quic;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
#endif
using NUnit.Framework;
using Opc.Ua.Bindings;
#if NET9_0_OR_GREATER
using Opc.Ua.Security.Certificates;
using Opc.Ua.Tests;
#endif

namespace Opc.Ua.Core.DataChannels.Tests
{
    [TestFixture]
    [Category("DataChannels")]
    [Parallelizable(ParallelScope.All)]
    public class DataChannelFrameErrorDeepTests
    {
        [Test]
        public void ToStatusCodeMapsEveryErrorClass()
        {
            Assert.Multiple(() =>
            {
                Assert.That(
                    DataChannelFrameError.None.ToStatusCode(),
                    Is.EqualTo((StatusCode)StatusCodes.Good));
                Assert.That(
                    DataChannelFrameError.UnknownFrameType.ToStatusCode(),
                    Is.EqualTo((StatusCode)StatusCodes.BadDataChannelFrameTypeUnsupported));
                Assert.That(
                    DataChannelFrameError.PayloadOnNonDataFrame.ToStatusCode(),
                    Is.EqualTo((StatusCode)StatusCodes.BadDataChannelFrameInvalid));
                Assert.That(
                    DataChannelFrameError.MalformedHeader.ToStatusCode(),
                    Is.EqualTo((StatusCode)StatusCodes.BadTcpMessageTypeInvalid));
            });
        }
    }
}

#if NET9_0_OR_GREATER

namespace Opc.Ua.Core.DataChannels.Tests
{
    [TestFixture]
    [Category("DataChannels")]
    [Category("Quic")]
    [NonParallelizable]
    public class QuicDataChannelDeepTests
    {
        [SetUp]
        public void SetUp()
        {
            QuicTestSupport.SkipUnlessAvailable();

            m_telemetry = NUnitTelemetryContext.Create();
            m_bufferManager = new BufferManager("quic-data-channel-deep", 65536, m_telemetry);
            m_certificate = CreateCertificate();
        }

        [TearDown]
        public void TearDown()
        {
            m_certificate?.Dispose();
        }

        [Test]
        public async Task PropertiesFaultsDisposeAndUseAfterDisposeAreSafeAsync()
        {
            await using var multiplexed = new QuicMultiplexedTransport(
                m_bufferManager!,
                65536,
                m_telemetry!);

            await using var transport = new QuicDataChannelTransport(
                multiplexed,
                m_bufferManager!,
                m_telemetry!);

            Assert.Multiple(() =>
            {
                Assert.That(transport.FramingMode, Is.EqualTo(DataChannelFramingMode.Quic));
                Assert.That(transport.HasTransportFlowControl, Is.True);
                Assert.That(transport.MaxFrameBodySize, Is.EqualTo(8192));
            });

            transport.OnProtocolFault(DataChannelFrameError.UnknownFrameType);
            transport.ReleaseChannel(123, StatusCodes.BadDataChannelClosed);
            await transport.DisposeAsync().ConfigureAwait(false);
            await transport.DisposeAsync().ConfigureAwait(false);

            ServiceResultException exception = Assert.ThrowsAsync<ServiceResultException>(
                async () => await transport
                    .SendFrameAsync(
                        DataChannelFrame.Data(
                            123,
                            1,
                            DataChannelFrameFlags.MessageStart,
                            new byte[] { 1 }),
                        TimeoutToken())
                    .ConfigureAwait(false))!;

            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadDataChannelIdInvalid));
        }

        [Test]
        public async Task MessageHeaderMatchesThePublishedQuicWireVectorAsync()
        {
            // The codec tests compare only from the stream header onward
            // (SpecVectors.QuicPrefix skips twelve bytes), so the Message
            // header a QUIC frame carries was never checked against the
            // specification. It is checked here.
            byte[] vector = SpecVectors.Load("quic_datagram_unreliable");

            await using var multiplexed = new QuicMultiplexedTransport(
                m_bufferManager!,
                65536,
                m_telemetry!);

            await using var transport = new QuicDataChannelTransport(
                multiplexed,
                m_bufferManager!,
                m_telemetry!);

            uint expectedChannelId = BinaryPrimitives.ReadUInt32LittleEndian(
                vector.AsSpan(8, 4));

            transport.SecureChannelId = expectedChannelId;

            var header = new byte[SpecVectors.QuicPrefix];
            transport.WriteMessageHeader(header, vector.Length);

            Assert.Multiple(() =>
            {
                // The vector must itself carry a real SecureChannelId; if
                // this ever became zero the check below would pass while
                // proving nothing.
                Assert.That(expectedChannelId, Is.Not.Zero);
                Assert.That(header, Is.EqualTo(vector[..SpecVectors.QuicPrefix]));
            });
        }

        [Test]
        public async Task ReceiveLoopDispatchesHeaderOnlyThenValidFrameAsync()
        {
            await using QuicLoopback loopback = await QuicLoopback
                .StartAsync(m_certificate!, m_bufferManager!, m_telemetry!)
                .ConfigureAwait(false);

            await using var serverData = new QuicDataChannelTransport(
                loopback.Server,
                m_bufferManager!,
                m_telemetry!);
            await using var serverManager = new DataChannelManager(serverData, false, m_telemetry!);
            serverData.Manager = serverManager;

            const uint channelId = 31;
            DataChannel sink = RegisterOpenSink(serverManager, channelId);

            ulong streamId = await loopback.Client
                .OpenStreamAsync(bidirectional: false, TimeoutToken())
                .ConfigureAwait(false);

            byte[] payload = [0x10, 0x20, 0x30];
            byte[] valid = BuildQuicChunk(DataChannelFrame.Data(
                channelId,
                1,
                DataChannelFrameFlags.MessageStart | DataChannelFrameFlags.MessageEnd,
                payload));

            byte[] combined = new byte[12 + valid.Length];
            BuildQuicChunk(ReadOnlySpan<byte>.Empty).CopyTo(combined, 0);
            valid.CopyTo(combined, 12);

            await loopback.Client
                .SendOnStreamAsync(streamId, combined, TimeoutToken())
                .ConfigureAwait(false);

            ulong acceptedId = await loopback.Server
                .AcceptStreamAsync(TimeoutToken())
                .ConfigureAwait(false);

            await serverData
                .BindChannelAsync(
                    channelId,
                    acceptedId,
                    DataChannelDirection.SinkToSource,
                    isOpcUaServer: true,
                    TimeoutToken())
                .ConfigureAwait(false);

            using DataChannelMessage? message = await sink
                .ReadAsync(TimeoutToken())
                .ConfigureAwait(false);

            Assert.That(message, Is.Not.Null);
            Assert.That(message!.Payload.Span.ToArray(), Is.EqualTo(payload));
        }

        [Test]
        public async Task MalformedFrameResetsOnlyTheBoundChannelAsync()
        {
            await using QuicLoopback loopback = await QuicLoopback
                .StartAsync(m_certificate!, m_bufferManager!, m_telemetry!)
                .ConfigureAwait(false);

            await using var serverData = new QuicDataChannelTransport(
                loopback.Server,
                m_bufferManager!,
                m_telemetry!);
            await using var serverManager = new DataChannelManager(serverData, false, m_telemetry!);
            serverData.Manager = serverManager;

            const uint channelId = 32;
            DataChannel sink = RegisterOpenSink(serverManager, channelId);

            ulong streamId = await loopback.Client
                .OpenStreamAsync(bidirectional: false, TimeoutToken())
                .ConfigureAwait(false);

            await loopback.Client
                .SendOnStreamAsync(streamId, BuildQuicChunk(new byte[11]), TimeoutToken())
                .ConfigureAwait(false);

            ulong acceptedId = await loopback.Server
                .AcceptStreamAsync(TimeoutToken())
                .ConfigureAwait(false);

            await serverData
                .BindChannelAsync(
                    channelId,
                    acceptedId,
                    DataChannelDirection.SinkToSource,
                    isOpcUaServer: true,
                    TimeoutToken())
                .ConfigureAwait(false);

            await WaitUntilAsync(
                () => sink.State == DataChannelState.Faulted,
                "malformed frame did not fault the channel").ConfigureAwait(false);

            Assert.That(sink.Status, Is.EqualTo((StatusCode)StatusCodes.BadTcpMessageTypeInvalid));
        }

        [Test]
        public async Task MisdirectedFrameIsIgnoredOnTheBoundStreamAsync()
        {
            await using QuicLoopback loopback = await QuicLoopback
                .StartAsync(m_certificate!, m_bufferManager!, m_telemetry!)
                .ConfigureAwait(false);

            await using var serverData = new QuicDataChannelTransport(
                loopback.Server,
                m_bufferManager!,
                m_telemetry!);
            await using var serverManager = new DataChannelManager(serverData, false, m_telemetry!);
            serverData.Manager = serverManager;

            const uint boundChannelId = 33;
            DataChannel sink = RegisterOpenSink(serverManager, boundChannelId);

            ulong streamId = await loopback.Client
                .OpenStreamAsync(bidirectional: false, TimeoutToken())
                .ConfigureAwait(false);

            await loopback.Client
                .SendOnStreamAsync(
                    streamId,
                    BuildQuicChunk(DataChannelFrame.Data(
                        boundChannelId + 1,
                        1,
                        DataChannelFrameFlags.MessageStart,
                        new byte[] { 0x44 })),
                    TimeoutToken())
                .ConfigureAwait(false);

            ulong acceptedId = await loopback.Server
                .AcceptStreamAsync(TimeoutToken())
                .ConfigureAwait(false);

            await serverData
                .BindChannelAsync(
                    boundChannelId,
                    acceptedId,
                    DataChannelDirection.SinkToSource,
                    isOpcUaServer: true,
                    TimeoutToken())
                .ConfigureAwait(false);

            using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(250));
            DataChannelMessage? message = null;
            try
            {
                message = await sink.ReadAsync(timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                message?.Dispose();
            }

            Assert.Multiple(() =>
            {
                Assert.That(message, Is.Null);
                Assert.That(sink.State, Is.EqualTo(DataChannelState.Open));
            });
        }

        [Test]
        public async Task ResetStreamCarriesStatusCodeFromServerToClientAsync()
        {
            await using QuicLoopback loopback = await QuicLoopback
                .StartAsync(m_certificate!, m_bufferManager!, m_telemetry!)
                .ConfigureAwait(false);

            await using var clientData = new QuicDataChannelTransport(
                loopback.Client,
                m_bufferManager!,
                m_telemetry!);
            await using var serverData = new QuicDataChannelTransport(
                loopback.Server,
                m_bufferManager!,
                m_telemetry!);

            const uint channelId = 34;
            ulong streamId = await clientData
                .OpenChannelStreamAsync(
                    channelId,
                    DataChannelDirection.Bidirectional,
                    isOpcUaServer: false,
                    TimeoutToken())
                .ConfigureAwait(false);

            await clientData
                .SendFrameAsync(
                    DataChannelFrame.Data(
                        channelId,
                        1,
                        DataChannelFrameFlags.MessageStart,
                        new byte[] { 0x55 }),
                    TimeoutToken())
                .ConfigureAwait(false);

            ulong acceptedId = await loopback.Server
                .AcceptStreamAsync(TimeoutToken())
                .ConfigureAwait(false);
            await ReceiveAndReturnAsync(loopback.Server, acceptedId).ConfigureAwait(false);

            await serverData
                .BindChannelAsync(
                    channelId,
                    acceptedId,
                    DataChannelDirection.Bidirectional,
                    isOpcUaServer: true,
                    TimeoutToken())
                .ConfigureAwait(false);
            StatusCode expected = StatusCodes.BadDataChannelClosed;
            serverData.ReleaseChannel(channelId, expected);

            Exception exception = Assert.CatchAsync(
                async () => await loopback.Client
                    .ReceiveOnStreamAsync(streamId, TimeoutToken())
                    .ConfigureAwait(false))!;

            Assert.That(ApplicationErrorCode(exception), Is.EqualTo((long)expected.Code));
        }

        [Test]
        public async Task AuthenticationFailureIsMappedToSecurityStatusCodeAsync()
        {
            using X509Certificate2 certificate = CreateCertificate();
            await using QuicListener listener = await QuicListener
                .ListenAsync(new QuicListenerOptions
                {
                    ListenEndPoint = new IPEndPoint(IPAddress.IPv6Any, 0),
                    ApplicationProtocols = [QuicTransport.ApplicationProtocol],
                    ConnectionOptionsCallback = (_, _, _) => ValueTask.FromResult(
                        new QuicServerConnectionOptions
                        {
                            DefaultStreamErrorCode = 0x0A,
                            DefaultCloseErrorCode = 0x0B,
                            ServerAuthenticationOptions = new SslServerAuthenticationOptions
                            {
                                ApplicationProtocols = [QuicTransport.ApplicationProtocol],
                                ServerCertificate = certificate
                            }
                        })
                })
                .ConfigureAwait(false);

            var options = new QuicClientOptions
            {
                HandshakeTimeout = TimeSpan.FromSeconds(5),
                ServerCertificateValidation = (_, _, _, _) => false
            };

            ServiceResultException exception = Assert.ThrowsAsync<ServiceResultException>(
                async () => await QuicConnectionBuilder
                    .ConnectAsync(
                        QuicTransport.CreateUrl("localhost", listener.LocalEndPoint.Port),
                        options,
                        TimeoutToken())
                    .ConfigureAwait(false))!;

            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadSecurityChecksFailed));
        }

        private static DataChannel RegisterOpenSink(DataChannelManager manager, uint channelId)
        {
            var settings = new DataChannelSettings
            {
                Direction = DataChannelDirection.SourceToSink,
                DeliveryMode = DataChannelDeliveryMode.ReliableOrdered,
                MaxFrameSize = 4096,
                InitialCredit = 4096
            };

            DataChannel channel = manager.Register(channelId, new NodeId(1u), settings, isSource: false);
            manager.MarkOpen(channelId);
            return channel;
        }

        private static byte[] BuildQuicChunk(DataChannelFrame frame)
        {
            byte[] body = new byte[frame.EncodedSize];
            int written = DataChannelFrameCodec.Encode(body, frame);
            Assert.That(written, Is.EqualTo(body.Length));
            return BuildQuicChunk(body);
        }

        private static byte[] BuildQuicChunk(ReadOnlySpan<byte> body)
        {
            byte[] chunk = new byte[12 + body.Length];
            chunk[0] = (byte)'S';
            chunk[1] = (byte)'T';
            chunk[2] = (byte)'R';
            chunk[3] = (byte)'F';
            BinaryPrimitives.WriteUInt32LittleEndian(chunk.AsSpan(4, 4), (uint)chunk.Length);
            BinaryPrimitives.WriteUInt32LittleEndian(chunk.AsSpan(8, 4), 0);
            body.CopyTo(chunk.AsSpan(12));
            return chunk;
        }

        private async Task ReceiveAndReturnAsync(QuicMultiplexedTransport transport, ulong streamId)
        {
            ArraySegment<byte> chunk = await transport
                .ReceiveOnStreamAsync(streamId, TimeoutToken())
                .ConfigureAwait(false);

            m_bufferManager!.ReturnBuffer(chunk.Array, nameof(ReceiveAndReturnAsync));
        }

        private static async Task WaitUntilAsync(Func<bool> predicate, string failure)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));

            while (!predicate())
            {
                if (timeout.IsCancellationRequested)
                {
                    Assert.Fail(failure);
                }

                await Task.Delay(25, CancellationToken.None).ConfigureAwait(false);
            }
        }

        private static long? ApplicationErrorCode(Exception exception)
        {
            Exception probe = exception;

            while (probe is AggregateException aggregate && aggregate.InnerExceptions.Count == 1)
            {
                probe = aggregate.InnerExceptions[0];
            }

            object? value = probe.GetType().GetProperty("ApplicationErrorCode")?.GetValue(probe);
            return value switch
            {
                long signed => signed,
                ulong unsigned => unchecked((long)unsigned),
                _ => null
            };
        }

        private static CancellationToken TimeoutToken()
        {
            return new CancellationTokenSource(TimeSpan.FromSeconds(20)).Token;
        }

        private static X509Certificate2 CreateCertificate()
        {
            using Certificate created = CertificateBuilder
                .Create("CN=QuicDataChannelDeep")
                .AddExtension(new X509SubjectAltNameExtension(
                    "urn:localhost:UA:QuicDataChannelDeep",
                    s_domainNames))
                .SetNotBefore(DateTime.UtcNow.AddDays(-1))
                .SetNotAfter(DateTime.UtcNow.AddDays(1))
                .SetRSAKeySize(2048)
                .CreateForRSA();

            byte[] pfx = created.AsX509Certificate2().Export(X509ContentType.Pfx);
            return X509CertificateLoader.LoadPkcs12(pfx, null, X509KeyStorageFlags.Exportable);
        }

        private static readonly string[] s_domainNames = ["localhost"];

        private ITelemetryContext m_telemetry = null!;
        private BufferManager m_bufferManager = null!;
        private X509Certificate2? m_certificate;
    }
}

#endif
