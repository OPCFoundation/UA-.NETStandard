#if NET9_0_OR_GREATER
/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 * ======================================================================*/

using System;
using System.Net.Quic;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Bindings;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Tests;

namespace Opc.Ua.Core.DataChannels.Tests
{
    [TestFixture]
    [Category("DataChannels")]
    [Category("Quic")]
    [NonParallelizable]
    public class QuicStreamMappingTests
    {
        [SetUp]
        public void SetUp()
        {
            QuicTestSupport.SkipUnlessAvailable();

            m_telemetry = NUnitTelemetryContext.Create();
            m_bufferManager = new BufferManager("quic-stream-mapping", 65536, m_telemetry);
            m_certificate = CreateCertificate();
        }

        [TearDown]
        public void TearDown()
        {
            m_certificate?.Dispose();
        }

        [Test]
        public async Task DirectionsUseTheSection74StreamInitiatorAndTypeAsync()
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

            ulong sourceToSink = await serverData
                .OpenChannelStreamAsync(1, DataChannelDirection.SourceToSink, true, TimeoutToken())
                .ConfigureAwait(false);
            ulong sinkToSource = await clientData
                .OpenChannelStreamAsync(2, DataChannelDirection.SinkToSource, false, TimeoutToken())
                .ConfigureAwait(false);
            ulong bidirectional = await clientData
                .OpenChannelStreamAsync(3, DataChannelDirection.Bidirectional, false, TimeoutToken())
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(StreamKind(sourceToSink), Is.EqualTo(3), "server-initiated unidirectional");
                Assert.That(StreamKind(sinkToSource), Is.EqualTo(2), "client-initiated unidirectional");
                Assert.That(StreamKind(bidirectional), Is.Zero, "client-initiated bidirectional");
            });
        }

        [Test]
        public async Task ReverseConnectInvertsQuicTypesButNotOpcUaChannelRolesAsync()
        {
            await using QuicLoopback loopback = await QuicLoopback
                .StartReverseAsync(m_certificate!, m_bufferManager!, m_telemetry!)
                .ConfigureAwait(false);

            await using var clientData = new QuicDataChannelTransport(
                loopback.Client,
                m_bufferManager!,
                m_telemetry!);
            await using var serverData = new QuicDataChannelTransport(
                loopback.Server,
                m_bufferManager!,
                m_telemetry!);

            const uint channelId = 11;
            ulong sourceToSink = await serverData
                .OpenChannelStreamAsync(channelId, DataChannelDirection.SourceToSink, true, TimeoutToken())
                .ConfigureAwait(false);
            ulong sinkToSource = await clientData
                .OpenChannelStreamAsync(12, DataChannelDirection.SinkToSource, false, TimeoutToken())
                .ConfigureAwait(false);
            ulong bidirectional = await clientData
                .OpenChannelStreamAsync(13, DataChannelDirection.Bidirectional, false, TimeoutToken())
                .ConfigureAwait(false);

            byte[] payload = [0x51, 0x74];
            await serverData
                .SendFrameAsync(
                    DataChannelFrame.Data(
                        channelId,
                        1,
                        DataChannelFrameFlags.MessageStart | DataChannelFrameFlags.MessageEnd,
                        payload),
                    TimeoutToken())
                .ConfigureAwait(false);

            ulong acceptedId = await loopback.Client
                .AcceptStreamAsync(TimeoutToken())
                .ConfigureAwait(false);
            ArraySegment<byte> chunk = await loopback.Client
                .ReceiveOnStreamAsync(acceptedId, TimeoutToken())
                .ConfigureAwait(false);

            try
            {
                DataChannelFrame frame = DecodeChunk(chunk);
                Assert.Multiple(() =>
                {
                    Assert.That(StreamKind(sourceToSink), Is.EqualTo(2), "OPC UA Server is the QUIC client");
                    Assert.That(StreamKind(sinkToSource), Is.EqualTo(3), "OPC UA Client is the QUIC server");
                    Assert.That(StreamKind(bidirectional), Is.EqualTo(1), "bidirectional stream is QUIC-server initiated");
                    Assert.That(acceptedId, Is.EqualTo(sourceToSink));
                    Assert.That(frame.ChannelId, Is.EqualTo(channelId));
                    Assert.That(frame.Payload.Span.ToArray(), Is.EqualTo(payload));
                });
            }
            finally
            {
                m_bufferManager!.ReturnBuffer(chunk.Array, nameof(ReverseConnectInvertsQuicTypesButNotOpcUaChannelRolesAsync));
            }
        }

        [Test]
        public async Task UnidirectionalStreamOnlyCarriesDataInThePermittedDirectionAsync()
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

            const uint channelId = 21;
            ulong streamId = await serverData
                .OpenChannelStreamAsync(channelId, DataChannelDirection.SourceToSink, true, TimeoutToken())
                .ConfigureAwait(false);

            byte[] payload = [0xA1, 0xA2, 0xA3];
            await serverData
                .SendFrameAsync(
                    DataChannelFrame.Data(
                        channelId,
                        1,
                        DataChannelFrameFlags.MessageStart | DataChannelFrameFlags.MessageEnd,
                        payload),
                    TimeoutToken())
                .ConfigureAwait(false);

            ulong acceptedId = await loopback.Client
                .AcceptStreamAsync(TimeoutToken())
                .ConfigureAwait(false);
            ArraySegment<byte> chunk = await loopback.Client
                .ReceiveOnStreamAsync(acceptedId, TimeoutToken())
                .ConfigureAwait(false);

            try
            {
                Assert.That(DecodeChunk(chunk).Payload.Span.ToArray(), Is.EqualTo(payload));
            }
            finally
            {
                m_bufferManager!.ReturnBuffer(chunk.Array, nameof(UnidirectionalStreamOnlyCarriesDataInThePermittedDirectionAsync));
            }

            await clientData
                .BindChannelAsync(
                    channelId,
                    streamId,
                    DataChannelDirection.SourceToSink,
                    isOpcUaServer: false,
                    TimeoutToken())
                .ConfigureAwait(false);

            ServiceResultException exception = Assert.ThrowsAsync<ServiceResultException>(
                async () => await clientData
                    .SendFrameAsync(
                        DataChannelFrame.Data(
                            channelId,
                            2,
                            DataChannelFrameFlags.MessageStart,
                            new byte[] { 0xFF }),
                        TimeoutToken())
                    .ConfigureAwait(false))!;

            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadDataChannelDirectionUnsupported));
        }

        [Test]
        public async Task ClosingOneMappedStreamDoesNotDisturbOtherChannelsAsync()
        {
            await using QuicLoopback loopback = await QuicLoopback
                .StartAsync(m_certificate!, m_bufferManager!, m_telemetry!)
                .ConfigureAwait(false);

            await using var clientData = new QuicDataChannelTransport(
                loopback.Client,
                m_bufferManager!,
                m_telemetry!);

            ulong first = await OpenAndSendAsync(clientData, 31, 0x31).ConfigureAwait(false);
            ulong second = await OpenAndSendAsync(clientData, 32, 0x32).ConfigureAwait(false);
            _ = first;

            (uint firstChannel, ulong firstAccepted) = await AcceptOneAsync(loopback).ConfigureAwait(false);
            (uint secondChannel, ulong secondAccepted) = await AcceptOneAsync(loopback).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(new[] { firstChannel, secondChannel }, Is.EquivalentTo(new uint[] { 31, 32 }));
                Assert.That(firstAccepted, Is.Not.EqualTo(secondAccepted));
            });

            clientData.ReleaseChannel(31, StatusCodes.Good);

            await clientData
                .SendFrameAsync(
                    DataChannelFrame.Data(
                        32,
                        2,
                        DataChannelFrameFlags.MessageStart,
                        new byte[] { 0x33 }),
                    TimeoutToken())
                .ConfigureAwait(false);

            ArraySegment<byte> chunk = await loopback.Server
                .ReceiveOnStreamAsync(second, TimeoutToken())
                .ConfigureAwait(false);

            try
            {
                DataChannelFrame frame = DecodeChunk(chunk);
                Assert.Multiple(() =>
                {
                    Assert.That(frame.ChannelId, Is.EqualTo(32u));
                    Assert.That(frame.Payload.Span.ToArray(), Is.EqualTo(new byte[] { 0x33 }));
                });
            }
            finally
            {
                m_bufferManager!.ReturnBuffer(chunk.Array, nameof(ClosingOneMappedStreamDoesNotDisturbOtherChannelsAsync));
            }
        }

        [Test]
        public async Task BindingAlreadyBoundStreamIsRejectedAndOriginalBindingRemainsAsync()
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

            const uint firstChannelId = 41;
            const uint secondChannelId = 42;

            // A QUIC stream does not reach the peer until something is
            // written on it, so the frame is what makes the server's accept
            // complete. Opening alone would leave the bind below waiting for
            // a stream the peer cannot yet see.
            ulong streamId = await OpenAndSendAsync(clientData, firstChannelId, 0x44)
                .ConfigureAwait(false);

            await serverData
                .BindChannelAsync(
                    firstChannelId,
                    streamId,
                    DataChannelDirection.SinkToSource,
                    isOpcUaServer: true,
                    TimeoutToken())
                .ConfigureAwait(false);

            ServiceResultException exception = Assert.ThrowsAsync<ServiceResultException>(
                async () => await serverData
                    .BindChannelAsync(
                        secondChannelId,
                        streamId,
                        DataChannelDirection.SinkToSource,
                        isOpcUaServer: true,
                        TimeoutToken())
                    .ConfigureAwait(false))!;

            Assert.Multiple(() =>
            {
                Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadDataChannelLimitsExceeded));
                Assert.That(BoundStream(serverData, firstChannelId), Is.EqualTo(streamId));
                Assert.That(IsChannelBound(serverData, secondChannelId), Is.False);
            });
        }

        private async Task<ulong> OpenAndSendAsync(
            QuicDataChannelTransport transport,
            uint channelId,
            byte payload)
        {
            ulong streamId = await transport
                .OpenChannelStreamAsync(
                    channelId,
                    DataChannelDirection.SinkToSource,
                    isOpcUaServer: false,
                    TimeoutToken())
                .ConfigureAwait(false);

            await transport
                .SendFrameAsync(
                    DataChannelFrame.Data(
                        channelId,
                        1,
                        DataChannelFrameFlags.MessageStart,
                        new byte[] { payload }),
                    TimeoutToken())
                .ConfigureAwait(false);

            return streamId;
        }

        private async Task<(uint ChannelId, ulong StreamId)> AcceptOneAsync(QuicLoopback loopback)
        {
            ulong streamId = await loopback.Server
                .AcceptStreamAsync(TimeoutToken())
                .ConfigureAwait(false);

            ArraySegment<byte> chunk = await loopback.Server
                .ReceiveOnStreamAsync(streamId, TimeoutToken())
                .ConfigureAwait(false);

            try
            {
                return (DecodeChunk(chunk).ChannelId, streamId);
            }
            finally
            {
                m_bufferManager!.ReturnBuffer(chunk.Array, nameof(AcceptOneAsync));
            }
        }

        private static DataChannelFrame DecodeChunk(ArraySegment<byte> chunk)
        {
            var body = new ReadOnlyMemory<byte>(
                chunk.Array!,
                chunk.Offset + SpecVectors.QuicPrefix,
                chunk.Count - SpecVectors.QuicPrefix);

            Assert.That(
                DataChannelFrameCodec.TryDecode(
                    body,
                    0,
                    out DataChannelFrame frame,
                    out DataChannelFrameError error),
                Is.True,
                error.ToString());

            return frame;
        }

        private static int StreamKind(ulong streamId)
        {
            return (int)(streamId & 0x03);
        }

        private static ulong BoundStream(QuicDataChannelTransport transport, uint channelId)
        {
            object binding = ChannelBinding(transport, channelId)
                ?? throw new AssertionException("Channel binding was not found.");
            PropertyInfo property = binding.GetType().GetProperty("StreamId")
                ?? throw new AssertionException("StreamId was not found.");

            return (ulong)property.GetValue(binding)!;
        }

        private static bool IsChannelBound(QuicDataChannelTransport transport, uint channelId)
        {
            return ChannelBinding(transport, channelId) != null;
        }

        private static object? ChannelBinding(QuicDataChannelTransport transport, uint channelId)
        {
            FieldInfo field = typeof(QuicDataChannelTransport).GetField(
                    "m_channelsByChannel",
                    BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new AssertionException("m_channelsByChannel was not found.");

            foreach (object entry in (System.Collections.IEnumerable)field.GetValue(transport)!)
            {
                PropertyInfo keyProperty = entry.GetType().GetProperty("Key")
                    ?? throw new AssertionException("Key was not found.");
                PropertyInfo valueProperty = entry.GetType().GetProperty("Value")
                    ?? throw new AssertionException("Value was not found.");

                if ((uint)keyProperty.GetValue(entry)! == channelId)
                {
                    return valueProperty.GetValue(entry);
                }
            }

            return null;
        }

        private static CancellationToken TimeoutToken()
        {
            return new CancellationTokenSource(TimeSpan.FromSeconds(20)).Token;
        }

        private static X509Certificate2 CreateCertificate()
        {
            using Certificate created = CertificateBuilder
                .Create("CN=QuicStreamMapping")
                .AddExtension(new X509SubjectAltNameExtension(
                    "urn:localhost:UA:QuicStreamMapping",
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
