#if NET9_0_OR_GREATER
/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Quic;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Bindings;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Tests;

namespace Opc.Ua.Core.DataChannels.Tests
{
    /// <summary>
    /// Exercises the QUIC data-channel binding edge cases not covered by
    /// the happy-path loopback tests.
    /// </summary>
    [TestFixture]
    [Category("DataChannels")]
    [Category("Quic")]
    [NonParallelizable]
    public class QuicTransportInternalsTests
    {
        [SetUp]
        public void SetUp()
        {
            m_telemetry = NUnitTelemetryContext.Create();
            m_bufferManager = new BufferManager("quic-internals", 65536, m_telemetry);
            m_certificate = CreateCertificate();
        }

        [TearDown]
        public void TearDown()
        {
            m_certificate?.Dispose();
        }

        [Test]
        public async Task HasTransportFlowControlSuppressesAllCreditTrafficAsync()
        {
            var serverTransport = new QuicLikeLoopbackTransport(m_bufferManager!, TimeProvider.System);
            var clientTransport = new QuicLikeLoopbackTransport(m_bufferManager!, TimeProvider.System);

            await using var server = new DataChannelManager(serverTransport, true, m_telemetry!);
            await using var client = new DataChannelManager(clientTransport, false, m_telemetry!);
            serverTransport.Peer = client;
            clientTransport.Peer = server;

            DataChannel source = OpenPair(server, client, DataChannelDirection.SourceToSink);
            DataChannel sink = client.Channels[0];

            byte[] payload = [1, 2, 3, 4, 5, 6, 7, 8];
            source.Write(
                payload,
                DataChannelFrameFlags.MessageStart | DataChannelFrameFlags.MessageEnd);

            using DataChannelMessage? message = await ReadWithTimeoutAsync(sink)
                .ConfigureAwait(false);

            Assert.That(message, Is.Not.Null);
            Assert.Multiple(() =>
            {
                Assert.That(serverTransport.HasTransportFlowControl, Is.True);
                Assert.That(serverTransport.CountOf(DataChannelFrameType.Credit), Is.Zero);
                Assert.That(clientTransport.CountOf(DataChannelFrameType.Credit), Is.Zero);
                Assert.That(message!.Payload.Span.ToArray(), Is.EqualTo(payload));
            });
        }

        [Test]
        public async Task QuicResetStreamCarriesStatusCodeAsApplicationErrorAsync()
        {
            RequireQuic();

            await using QuicLoopback loopback = await QuicLoopback
                .StartAsync(m_certificate!, m_bufferManager!, m_telemetry!)
                .ConfigureAwait(false);

            await using var clientData = new QuicDataChannelTransport(
                loopback.Client,
                m_bufferManager!,
                m_telemetry!);

            const uint channelId = 10;
            _ = await OpenClientStreamAndSendPrimerAsync(
                loopback,
                clientData,
                channelId).ConfigureAwait(false);

            ulong acceptedId = await loopback.Server
                .AcceptStreamAsync(TimeoutToken())
                .ConfigureAwait(false);

            await ReceiveAndReturnAsync(loopback.Server, acceptedId).ConfigureAwait(false);

            StatusCode expected = StatusCodes.BadDataChannelClosed;
            clientData.ReleaseChannel(channelId, expected);

            Exception? exception = await CatchAsync(
                async () => await loopback.Server
                    .ReceiveOnStreamAsync(acceptedId, TimeoutToken())
                    .ConfigureAwait(false)).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(exception, Is.Not.Null);
                Assert.That(ApplicationErrorCode(exception!), Is.EqualTo((long)expected.Code));
            });
        }

        [Test]
        public async Task UnreliableDeliveryIsRefusedRatherThanDowngradedAsync()
        {
            await using var transport = new QuicMultiplexedTransport(
                m_bufferManager!,
                65536,
                m_telemetry!);

            Assert.Multiple(() =>
            {
                Assert.That(transport.SupportsDatagrams, Is.False);
                Assert.That(transport.MaxDatagramSize, Is.Zero);
            });

            ServiceResultException datagram = Assert.ThrowsAsync<ServiceResultException>(
                async () => await transport
                    .SendDatagramAsync(new byte[] { 0x01 }, TimeoutToken())
                    .ConfigureAwait(false))!;

            Assert.That(datagram.StatusCode, Is.EqualTo(StatusCodes.BadDeliveryModeUnsupported));

            foreach (DataChannelDeliveryMode mode in new[]
            {
                DataChannelDeliveryMode.Unreliable,
                DataChannelDeliveryMode.PartiallyReliable
            })
            {
                Assert.That(
                    DataChannelNegotiator.TryRevise(
                        new DataChannelParametersDataType
                        {
                            Direction = DataChannelDirection.SourceToSink,
                            DeliveryMode = mode,
                            MaxFrameSize = 1024
                        },
                        new DataChannelSourceCapabilities(),
                        new DataChannelServerCapabilities
                        {
                            SupportsUnreliableDatagrams = false
                        },
                        8192,
                        transportIsReliable: true,
                        out DataChannelParametersDataType revised,
                        out StatusCode error),
                    Is.False,
                    $"{mode} must not be revised to {revised.DeliveryMode}");

                Assert.That(error, Is.EqualTo((StatusCode)StatusCodes.BadDeliveryModeUnsupported));
            }
        }

        [Test]
        public async Task EachChannelUsesAnIndependentQuicStreamAsync()
        {
            RequireQuic();

            await using QuicLoopback loopback = await QuicLoopback
                .StartAsync(m_certificate!, m_bufferManager!, m_telemetry!)
                .ConfigureAwait(false);

            await using var clientData = new QuicDataChannelTransport(
                loopback.Client,
                m_bufferManager!,
                m_telemetry!);

            await SendOnNewClientStreamAsync(loopback, clientData, 1, 0x11).ConfigureAwait(false);
            await SendOnNewClientStreamAsync(loopback, clientData, 2, 0x22).ConfigureAwait(false);

            Dictionary<uint, ulong> accepted = await AcceptAndDecodeInitialFramesAsync(loopback, 2)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(accepted.Keys, Is.EquivalentTo(new uint[] { 1, 2 }));
                Assert.That(accepted[1], Is.Not.EqualTo(accepted[2]));
            });

            clientData.ReleaseChannel(1, StatusCodes.Good);

            await clientData
                .SendFrameAsync(
                    DataChannelFrame.Data(
                        2,
                        2,
                        DataChannelFrameFlags.MessageStart,
                        new byte[] { 0x33 }),
                    TimeoutToken())
                .ConfigureAwait(false);

            ArraySegment<byte> secondChannelChunk = await loopback.Server
                .ReceiveOnStreamAsync(accepted[2], TimeoutToken())
                .ConfigureAwait(false);

            try
            {
                DataChannelFrame frame = DecodeChunk(secondChannelChunk);
                Assert.Multiple(() =>
                {
                    Assert.That(frame.ChannelId, Is.EqualTo(2u));
                    Assert.That(frame.Payload.Span.ToArray(), Is.EqualTo(new byte[] { 0x33 }));
                });
            }
            finally
            {
                m_bufferManager!.ReturnBuffer(secondChannelChunk.Array, nameof(EachChannelUsesAnIndependentQuicStreamAsync));
            }
        }

        [Test]
        public async Task ConnectionLossCompletesPendingStreamReadWithErrorAsync()
        {
            RequireQuic();

            await using QuicLoopback loopback = await QuicLoopback
                .StartAsync(m_certificate!, m_bufferManager!, m_telemetry!)
                .ConfigureAwait(false);

            await using var clientData = new QuicDataChannelTransport(
                loopback.Client,
                m_bufferManager!,
                m_telemetry!);

            await SendOnNewClientStreamAsync(loopback, clientData, 3, 0x44).ConfigureAwait(false);

            ulong acceptedId = await loopback.Server
                .AcceptStreamAsync(TimeoutToken())
                .ConfigureAwait(false);

            await ReceiveAndReturnAsync(loopback.Server, acceptedId).ConfigureAwait(false);

            Task<ArraySegment<byte>> pending = loopback.Server
                .ReceiveOnStreamAsync(acceptedId, TimeoutToken())
                .AsTask();

            await loopback.Client.DisposeAsync().ConfigureAwait(false);

            Exception? exception = await CatchAsync(
                async () => await pending.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false))
                .ConfigureAwait(false);

            Assert.That(exception, Is.Not.Null);
        }

        [Test]
        public async Task DisposeWithOpenStreamsIsIdempotentAndFutureUseFailsSafelyAsync()
        {
            RequireQuic();

            await using QuicLoopback loopback = await QuicLoopback
                .StartAsync(m_certificate!, m_bufferManager!, m_telemetry!)
                .ConfigureAwait(false);

            await using var clientData = new QuicDataChannelTransport(
                loopback.Client,
                m_bufferManager!,
                m_telemetry!);

            ulong streamId = await OpenClientStreamAndSendPrimerAsync(
                loopback,
                clientData,
                4).ConfigureAwait(false);

            _ = await loopback.Server.AcceptStreamAsync(TimeoutToken()).ConfigureAwait(false);

            await loopback.Client.DisposeAsync().ConfigureAwait(false);
            await loopback.Client.DisposeAsync().ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(
                    async () => await loopback.Client.OpenStreamAsync(true, TimeoutToken()).ConfigureAwait(false),
                    Throws.InstanceOf<ServiceResultException>());
                Assert.That(
                    async () => await loopback.Client
                        .SendOnStreamAsync(streamId, new byte[] { 0x01 }, TimeoutToken())
                        .ConfigureAwait(false),
                    Throws.InstanceOf<ServiceResultException>());
                Assert.That(
                    async () => await loopback.Client
                        .SendChunkAsync(
                            new byte[] { 0x41, 0x43, 0x4B, 0x46, 0x08, 0, 0, 0 },
                            TimeoutToken())
                        .ConfigureAwait(false),
                    Throws.InstanceOf<Exception>());
            });
        }

        [Test]
        public async Task QuicFramingEmitsOnlyMessageAndStreamHeadersAsync()
        {
            RequireQuic();

            await using QuicLoopback loopback = await QuicLoopback
                .StartAsync(m_certificate!, m_bufferManager!, m_telemetry!)
                .ConfigureAwait(false);

            await using var clientData = new QuicDataChannelTransport(
                loopback.Client,
                m_bufferManager!,
                m_telemetry!);

            byte[] vector = SpecVectors.Load("quic_data_stream");
            byte[] payload = Enumerable.Range(0, 16).Select(static x => (byte)x).ToArray();

            const uint channelId = 1;
            ulong streamId = await clientData
                .OpenChannelStreamAsync(
                    channelId,
                    DataChannelDirection.SinkToSource,
                    isOpcUaServer: false,
                    TimeoutToken())
                .ConfigureAwait(false);

            await clientData
                .SendFrameAsync(
                    DataChannelFrame.Data(
                        channelId,
                        1,
                        DataChannelFrameFlags.MessageStart |
                        DataChannelFrameFlags.MessageEnd |
                        DataChannelFrameFlags.Marker,
                        payload),
                    TimeoutToken())
                .ConfigureAwait(false);

            ulong acceptedId = await loopback.Server
                .AcceptStreamAsync(TimeoutToken())
                .ConfigureAwait(false);

            ArraySegment<byte> chunk = await loopback.Server
                .ReceiveOnStreamAsync(acceptedId, TimeoutToken())
                .ConfigureAwait(false);

            try
            {
                byte[] actual = chunk.AsSpan().ToArray();

                Assert.Multiple(() =>
                {
                    Assert.That(actual, Has.Length.EqualTo(12 + DataChannelConstants.StreamHeaderSize + payload.Length));
                    Assert.That(actual.AsSpan(0, 8).ToArray(), Is.EqualTo(vector.AsSpan(0, 8).ToArray()));
                    Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(actual.AsSpan(8, 4)), Is.Zero);
                    Assert.That(
                        actual.AsSpan(SpecVectors.QuicPrefix).ToArray(),
                        Is.EqualTo(vector.AsSpan(SpecVectors.QuicPrefix).ToArray()));
                    Assert.That(
                        actual,
                        Has.Length.LessThan(SpecVectors.InlinePrefix + DataChannelConstants.StreamHeaderSize + payload.Length));
                });
            }
            finally
            {
                m_bufferManager!.ReturnBuffer(chunk.Array, nameof(QuicFramingEmitsOnlyMessageAndStreamHeadersAsync));
            }
        }

        [Test]
        public async Task WrappingConstructorExposesConnectedEndpointsAsync()
        {
            RequireQuic();

            await using QuicLoopback loopback = await QuicLoopback
                .StartAsync(m_certificate!, m_bufferManager!, m_telemetry!)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(loopback.Client.Implementation, Is.EqualTo(QuicMultiplexedTransport.ImplementationName));
                Assert.That(loopback.Server.LocalEndpoint, Is.Not.Null);
                Assert.That(loopback.Server.RemoteEndpoint, Is.Not.Null);
            });
        }

        [Test]
        public async Task UnconnectedConstructorConnectAsyncFailsAgainstDeadEndpointAsync()
        {
            RequireQuic();

            int port = await ReserveAndReleaseQuicPortAsync().ConfigureAwait(false);

            await using var transport = new QuicMultiplexedTransport(
                m_bufferManager!,
                65536,
                m_telemetry!,
                new QuicClientOptions
                {
                    ServerCertificateValidation = (_, _, _, _) => true
                });

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));

            Assert.That(
                async () => await transport
                    .ConnectAsync(QuicTransport.CreateUrl("localhost", port), timeout.Token)
                    .ConfigureAwait(false),
                Throws.InstanceOf<Exception>());

            Assert.Multiple(() =>
            {
                Assert.That(transport.LocalEndpoint, Is.Null);
                Assert.That(transport.RemoteEndpoint, Is.Null);
            });
        }

        private static void RequireQuic()
        {
            if (!QuicListener.IsSupported || !QuicConnection.IsSupported)
            {
                Assert.Ignore("QUIC is unavailable on this platform (msquic missing).");
            }
        }

        private static DataChannel OpenPair(
            DataChannelManager server,
            DataChannelManager client,
            DataChannelDirection direction)
        {
            var settings = new DataChannelSettings
            {
                Direction = direction,
                DeliveryMode = DataChannelDeliveryMode.ReliableOrdered,
                MaxFrameSize = 4096,
                InitialCredit = 4096
            };

            Assert.That(server.TryAllocateChannelId(out uint channelId), Is.True);

            DataChannel source = server.Register(channelId, new NodeId(1u), settings, isSource: true);
            _ = client.Register(channelId, new NodeId(1u), settings, isSource: false);
            server.MarkOpen(channelId);
            client.MarkOpen(channelId);
            return source;
        }

        private Task<ulong> OpenClientStreamAndSendPrimerAsync(
            QuicLoopback loopback,
            QuicDataChannelTransport clientData,
            uint channelId)
        {
            return SendOnNewClientStreamAsync(loopback, clientData, channelId, 0xAA);
        }

        private async Task<ulong> SendOnNewClientStreamAsync(
            QuicLoopback loopback,
            QuicDataChannelTransport clientData,
            uint channelId,
            byte payload)
        {
            ulong streamId = await clientData
                .OpenChannelStreamAsync(
                    channelId,
                    DataChannelDirection.SinkToSource,
                    isOpcUaServer: false,
                    TimeoutToken())
                .ConfigureAwait(false);

            await clientData
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

        private async Task<Dictionary<uint, ulong>> AcceptAndDecodeInitialFramesAsync(
            QuicLoopback loopback,
            int count)
        {
            var accepted = new Dictionary<uint, ulong>();

            for (int ii = 0; ii < count; ii++)
            {
                ulong streamId = await loopback.Server
                    .AcceptStreamAsync(TimeoutToken())
                    .ConfigureAwait(false);

                ArraySegment<byte> chunk = await loopback.Server
                    .ReceiveOnStreamAsync(streamId, TimeoutToken())
                    .ConfigureAwait(false);

                try
                {
                    DataChannelFrame frame = DecodeChunk(chunk);
                    accepted[frame.ChannelId] = streamId;
                }
                finally
                {
                    m_bufferManager!.ReturnBuffer(chunk.Array, nameof(AcceptAndDecodeInitialFramesAsync));
                }
            }

            return accepted;
        }

        private async Task ReceiveAndReturnAsync(QuicMultiplexedTransport transport, ulong streamId)
        {
            ArraySegment<byte> chunk = await transport
                .ReceiveOnStreamAsync(streamId, TimeoutToken())
                .ConfigureAwait(false);

            m_bufferManager!.ReturnBuffer(chunk.Array, nameof(ReceiveAndReturnAsync));
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

        private static async Task<DataChannelMessage?> ReadWithTimeoutAsync(DataChannel channel)
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

            try
            {
                return await channel.ReadAsync(cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return null;
            }
        }

        private static async Task<Exception?> CatchAsync(Func<Task> action)
        {
            try
            {
                await action().ConfigureAwait(false);
                return null;
            }
            catch (Exception exception)
            {
                return exception;
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

        private async Task<int> ReserveAndReleaseQuicPortAsync()
        {
            QuicListener listener = await QuicListener
                .ListenAsync(new QuicListenerOptions
                {
                    ListenEndPoint = new IPEndPoint(IPAddress.IPv6Any, 0),
                    ApplicationProtocols = [QuicTransport.ApplicationProtocol],
                    ConnectionOptionsCallback = (_, _, _) => ValueTask.FromResult(
                        new QuicServerConnectionOptions
                        {
                            DefaultStreamErrorCode = 0x0A,
                            DefaultCloseErrorCode = 0x0B,
                            MaxInboundBidirectionalStreams = 1,
                            ServerAuthenticationOptions = new SslServerAuthenticationOptions
                            {
                                ApplicationProtocols = [QuicTransport.ApplicationProtocol],
                                ServerCertificate = m_certificate!
                            }
                        })
                })
                .ConfigureAwait(false);

            int port = listener.LocalEndPoint.Port;
            await listener.DisposeAsync().ConfigureAwait(false);
            return port;
        }

        private static CancellationToken TimeoutToken()
        {
            return new CancellationTokenSource(TimeSpan.FromSeconds(20)).Token;
        }

        private static X509Certificate2 CreateCertificate()
        {
            using Certificate created = CertificateBuilder
                .Create("CN=QuicInternals")
                .AddExtension(new X509SubjectAltNameExtension(
                    "urn:localhost:UA:QuicInternals",
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

    internal sealed class QuicLikeLoopbackTransport : IDataChannelTransport
    {
        public QuicLikeLoopbackTransport(BufferManager bufferManager, TimeProvider timeProvider)
        {
            BufferManager = bufferManager;
            TimeProvider = timeProvider;
        }

        public DataChannelManager? Peer { get; set; }

        public IReadOnlyList<DataChannelFrame> Sent => m_sent;

        public DataChannelFramingMode FramingMode => DataChannelFramingMode.Quic;

        public int MaxFrameBodySize => 4096;

        public bool HasTransportFlowControl => true;

        public BufferManager BufferManager { get; }

        public TimeProvider TimeProvider { get; }

        public ValueTask SendFrameAsync(DataChannelFrame frame, CancellationToken ct)
        {
            lock (m_lock)
            {
                m_sent.Add(frame);
            }

            if (Peer == null)
            {
                return default;
            }

            byte[] encoded = new byte[frame.EncodedSize];
            DataChannelFrameCodec.Encode(encoded, frame);

            if (DataChannelFrameCodec.TryDecode(
                encoded,
                0,
                out DataChannelFrame received,
                out DataChannelFrameError error))
            {
                Peer.HandleFrame(received);
            }
            else
            {
                OnProtocolFault(error);
            }

            return default;
        }

        public void OnProtocolFault(DataChannelFrameError error)
        {
            lock (m_lock)
            {
                m_faults.Add(error);
            }
        }

        public int CountOf(DataChannelFrameType frameType)
        {
            lock (m_lock)
            {
                return m_sent.Count(frame => frame.FrameType == frameType);
            }
        }

        private readonly List<DataChannelFrame> m_sent = [];
        private readonly List<DataChannelFrameError> m_faults = [];
        private readonly Lock m_lock = new();
    }
}

#endif
