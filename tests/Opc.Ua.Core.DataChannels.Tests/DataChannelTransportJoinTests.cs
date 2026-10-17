#if NET9_0_OR_GREATER
/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Net.Quic;
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
    public class DataChannelTransportJoinTests
    {
        [SetUp]
        public void SetUp()
        {
            QuicTestSupport.SkipUnlessAvailable();

            m_telemetry = NUnitTelemetryContext.Create();
            m_bufferManager = new BufferManager("data-channel-transport-join", 65536, m_telemetry);
            m_certificate = CreateCertificate();
        }

        [TearDown]
        public void TearDown()
        {
            m_certificate?.Dispose();
        }

        [Test]
        public async Task SourceToSinkOpenAllocatesServerStreamAndCarriesDataAsync()
        {
            await using QuicLoopback loopback = await QuicLoopback
                .StartAsync(m_certificate!, m_bufferManager!, m_telemetry!)
                .ConfigureAwait(false);

            var secureChannelContext = SecureChannel("join-7");
            ((IUaSCSecureChannelBoundTransport)loopback.Server)
                .OnSecureChannelAttached(secureChannelContext.SecureChannelId);

            var serverTransport = new QuicServerDataChannelTransport();
            Assert.That(
                serverTransport.TryGetManager(
                    secureChannelContext,
                    ServerCapabilities(),
                    m_telemetry!,
                    out DataChannelManager serverManager,
                    out uint maxFrameSize,
                    out bool isReliable),
                Is.True);

            await using (serverManager.ConfigureAwait(false))
            await using (var clientData = new QuicDataChannelTransport(
                loopback.Client,
                m_bufferManager!,
                m_telemetry!))
            await using (var clientManager = new DataChannelManager(
                clientData,
                isServer: false,
                m_telemetry!))
            {
                clientData.Manager = clientManager;
                var source = new TestSource(SourceNodeId, SourceCapabilities());
                var sources = new DataChannelSourceRegistry();
                sources.Register(source);

                var handler = new DataChannelServiceHandler(
                    serverManager,
                    sources,
                    ServerCapabilities(),
                    new PermissiveAuthorizer(),
                    streamAllocator: new ServerTransportAllocator(serverTransport, secureChannelContext));

                OpenDataChannelResponse response = await handler
                    .OpenDataChannelAsync(
                        RequestContext(maxFrameSize),
                        SourceNodeId,
                        0,
                        Parameters(DataChannelDirection.SourceToSink),
                        TimeoutToken())
                    .ConfigureAwait(false);
                handler.OnResponseSent(response.ChannelId);

                DataChannel sink = clientManager.Register(
                    response.ChannelId,
                    SourceNodeId,
                    DataChannelSettings.FromParameters(response.RevisedParameters),
                    isSource: false,
                    response.RevisedTransportChannelId);
                clientManager.MarkOpen(response.ChannelId);

                Task bindClient = clientData
                    .BindChannelAsync(
                        response.ChannelId,
                        response.RevisedTransportChannelId,
                        DataChannelDirection.SourceToSink,
                        isOpcUaServer: false,
                        TimeoutToken())
                    .AsTask();

                byte[] payload = [0x41, 0x42, 0x43, 0x44];
                source.OpenedChannels[0].Write(
                    payload,
                    DataChannelFrameFlags.MessageStart | DataChannelFrameFlags.MessageEnd);

                await bindClient.ConfigureAwait(false);
                using DataChannelMessage? message = await sink
                    .ReadAsync(TimeoutToken())
                    .ConfigureAwait(false);

                Assert.Multiple(() =>
                {
                    Assert.That(response.RevisedTransportChannelId, Is.Not.Zero);
                    Assert.That((int)(response.RevisedTransportChannelId & 0x03), Is.EqualTo(3));
                    Assert.That(isReliable, Is.True);
                    Assert.That(message, Is.Not.Null);
                    Assert.That(message!.Payload.Span.ToArray(), Is.EqualTo(payload));
                });
            }
        }

        [TestCase(DataChannelDirection.SinkToSource)]
        [TestCase(DataChannelDirection.Bidirectional)]
        public async Task ClientInitiatedDirectionsEchoClientStreamIdAsync(
            DataChannelDirection direction)
        {
            await using QuicLoopback loopback = await QuicLoopback
                .StartAsync(m_certificate!, m_bufferManager!, m_telemetry!)
                .ConfigureAwait(false);

            var secureChannelContext = SecureChannel("join-11");
            ((IUaSCSecureChannelBoundTransport)loopback.Server)
                .OnSecureChannelAttached(secureChannelContext.SecureChannelId);

            var serverTransport = new QuicServerDataChannelTransport();
            Assert.That(
                serverTransport.TryGetManager(
                    secureChannelContext,
                    ServerCapabilities(),
                    m_telemetry!,
                    out DataChannelManager serverManager,
                    out uint maxFrameSize,
                    out _),
                Is.True);

            await using (serverManager.ConfigureAwait(false))
            await using (var clientData = new QuicDataChannelTransport(
                loopback.Client,
                m_bufferManager!,
                m_telemetry!))
            {
                ulong streamId = await clientData
                    .OpenChannelStreamAsync(99, direction, isOpcUaServer: false, TimeoutToken())
                    .ConfigureAwait(false);

                var source = new TestSource(SourceNodeId, SourceCapabilities(direction));
                var sources = new DataChannelSourceRegistry();
                sources.Register(source);

                var handler = new DataChannelServiceHandler(
                    serverManager,
                    sources,
                    ServerCapabilities(),
                    new PermissiveAuthorizer(),
                    streamAllocator: new ServerTransportAllocator(serverTransport, secureChannelContext));

                OpenDataChannelResponse response = await handler
                    .OpenDataChannelAsync(
                        RequestContext(maxFrameSize, streamId),
                        SourceNodeId,
                        0,
                        Parameters(direction),
                        TimeoutToken())
                    .ConfigureAwait(false);

                Assert.That(response.RevisedTransportChannelId, Is.EqualTo(streamId));
            }
        }

        /// <summary>
        /// §7.4 obliges a Server to validate <c>transportChannelId</c> before
        /// it binds a channel, and forbids it echoing a value it has not
        /// validated. The check runs inside the transport, so it only protects
        /// anything if its result reaches the Service call — which is what
        /// this exercises, rather than calling the transport directly.
        /// </summary>
        [Test]
        public async Task ClientNamingAStreamItCouldNotHaveOpenedIsRefusedAsync()
        {
            await using QuicLoopback loopback = await QuicLoopback
                .StartAsync(m_certificate!, m_bufferManager!, m_telemetry!)
                .ConfigureAwait(false);

            var secureChannelContext = SecureChannel("join-21");
            ((IUaSCSecureChannelBoundTransport)loopback.Server)
                .OnSecureChannelAttached(secureChannelContext.SecureChannelId);

            var serverTransport = new QuicServerDataChannelTransport();
            Assert.That(
                serverTransport.TryGetManager(
                    secureChannelContext,
                    ServerCapabilities(),
                    m_telemetry!,
                    out DataChannelManager serverManager,
                    out uint maxFrameSize,
                    out _),
                Is.True);

            await using (serverManager.ConfigureAwait(false))
            {
                var sources = new DataChannelSourceRegistry();
                sources.Register(new TestSource(
                    SourceNodeId,
                    SourceCapabilities(DataChannelDirection.SinkToSource)));

                var handler = new DataChannelServiceHandler(
                    serverManager,
                    sources,
                    ServerCapabilities(),
                    new PermissiveAuthorizer(),
                    streamAllocator: new ServerTransportAllocator(serverTransport, secureChannelContext));

                // Stream id 3 is server-initiated unidirectional. Only the
                // Server can open it, so a Client naming it is claiming a
                // stream it does not own.
                ServiceResultException? exception = Assert.ThrowsAsync<ServiceResultException>(
                    async () => await handler
                        .OpenDataChannelAsync(
                            RequestContext(maxFrameSize, 3),
                            SourceNodeId,
                            0,
                            Parameters(DataChannelDirection.SinkToSource),
                            TimeoutToken())
                        .ConfigureAwait(false));

                Assert.That(
                    exception!.StatusCode,
                    Is.EqualTo(StatusCodes.BadDataChannelLimitsExceeded));
            }
        }

        /// <summary>
        /// §7.4: a stream "shall not already be bound to another data channel
        /// on that connection". The second open has to be refused rather than
        /// accepted with the stream echoed back.
        /// </summary>
        [Test]
        public async Task ClientReusingAnAlreadyBoundStreamIsRefusedAsync()
        {
            await using QuicLoopback loopback = await QuicLoopback
                .StartAsync(m_certificate!, m_bufferManager!, m_telemetry!)
                .ConfigureAwait(false);

            var secureChannelContext = SecureChannel("join-22");
            ((IUaSCSecureChannelBoundTransport)loopback.Server)
                .OnSecureChannelAttached(secureChannelContext.SecureChannelId);

            var serverTransport = new QuicServerDataChannelTransport();
            Assert.That(
                serverTransport.TryGetManager(
                    secureChannelContext,
                    ServerCapabilities(),
                    m_telemetry!,
                    out DataChannelManager serverManager,
                    out uint maxFrameSize,
                    out _),
                Is.True);

            await using (serverManager.ConfigureAwait(false))
            await using (var clientData = new QuicDataChannelTransport(
                loopback.Client,
                m_bufferManager!,
                m_telemetry!))
            {
                ulong streamId = await clientData
                    .OpenChannelStreamAsync(
                        99,
                        DataChannelDirection.SinkToSource,
                        isOpcUaServer: false,
                        TimeoutToken())
                    .ConfigureAwait(false);

                var sources = new DataChannelSourceRegistry();
                sources.Register(new TestSource(
                    SourceNodeId,
                    SourceCapabilities(DataChannelDirection.SinkToSource)));

                var handler = new DataChannelServiceHandler(
                    serverManager,
                    sources,
                    ServerCapabilities(),
                    new PermissiveAuthorizer(),
                    streamAllocator: new ServerTransportAllocator(serverTransport, secureChannelContext));

                OpenDataChannelResponse first = await handler
                    .OpenDataChannelAsync(
                        RequestContext(maxFrameSize, streamId),
                        SourceNodeId,
                        0,
                        Parameters(DataChannelDirection.SinkToSource),
                        TimeoutToken())
                    .ConfigureAwait(false);

                ServiceResultException? exception = Assert.ThrowsAsync<ServiceResultException>(
                    async () => await handler
                        .OpenDataChannelAsync(
                            RequestContext(maxFrameSize, streamId),
                            SourceNodeId,
                            0,
                            Parameters(DataChannelDirection.SinkToSource),
                            TimeoutToken())
                        .ConfigureAwait(false));

                Assert.Multiple(() =>
                {
                    Assert.That(first.RevisedTransportChannelId, Is.EqualTo(streamId));
                    Assert.That(
                        exception!.StatusCode,
                        Is.EqualTo(StatusCodes.BadDataChannelLimitsExceeded));
                });
            }
        }

        [Test]
        public void ClientInitiatedDirectionWithoutStreamIdIsRefused()        {
            var manager = new DataChannelManager(
                new LoopbackTransport(m_bufferManager!, TimeProvider.System),
                true,
                m_telemetry!);
            var sources = new DataChannelSourceRegistry();
            sources.Register(new TestSource(
                SourceNodeId,
                SourceCapabilities(DataChannelDirection.SinkToSource)));
            var handler = new DataChannelServiceHandler(
                manager,
                sources,
                ServerCapabilities(),
                new PermissiveAuthorizer());

            ServiceResultException? exception = Assert.ThrowsAsync<ServiceResultException>(
                async () => await handler
                    .OpenDataChannelAsync(
                        RequestContext(65536, 0),
                        SourceNodeId,
                        0,
                        Parameters(DataChannelDirection.SinkToSource),
                        TimeoutToken())
                    .ConfigureAwait(false));

            Assert.That(exception!.StatusCode, Is.EqualTo(StatusCodes.BadDataChannelLimitsExceeded));
        }

        private static SecureChannelContext SecureChannel(string secureChannelId)
        {
            return new SecureChannelContext(
                secureChannelId,
                new EndpointDescription
                {
                    TransportProfileUri = Profiles.UaQuicTransport,
                    SecurityMode = MessageSecurityMode.SignAndEncrypt
                },
                RequestEncoding.Binary);
        }

        private static DataChannelRequestContext RequestContext(
            uint maxFrameSize,
            ulong transportChannelId = 0)
        {
            return new DataChannelRequestContext
            {
                SessionId = new NodeId(5001u),
                IsSessionActivated = true,
                SecurityMode = MessageSecurityMode.SignAndEncrypt,
                TransportProfileUri = Profiles.UaQuicTransport,
                TransportChannelId = transportChannelId,
                TransportIsReliable = true,
                TransportMaxFrameSize = maxFrameSize
            };
        }

        private static DataChannelParametersDataType Parameters(DataChannelDirection direction)
        {
            return new DataChannelParametersDataType
            {
                Direction = direction,
                DeliveryMode = DataChannelDeliveryMode.ReliableOrdered,
                ContentType = "application/octet-stream",
                MaxFrameSize = 4096,
                InitialCredit = 8192,
                Priority = 1
            };
        }

        private static DataChannelSourceCapabilities SourceCapabilities(
            DataChannelDirection direction = DataChannelDirection.SourceToSink)
        {
            return new DataChannelSourceCapabilities
            {
                Direction = direction,
                SupportedDeliveryModes = [DataChannelDeliveryMode.ReliableOrdered],
                ContentType = "application/octet-stream",
                MaxFrameSize = 65536,
                Priority = 1
            };
        }

        private static DataChannelServerCapabilities ServerCapabilities()
        {
            return new DataChannelServerCapabilities
            {
                MaxDataChannels = 16,
                MaxFrameSize = 65536,
                MaxCreditPerChannel = 1024 * 1024,
                SupportedDeliveryModes = [DataChannelDeliveryMode.ReliableOrdered],
                SupportedTransportProfileUris = [Profiles.UaQuicTransport]
            };
        }

        private static CancellationToken TimeoutToken()
        {
            return new CancellationTokenSource(TimeSpan.FromSeconds(10)).Token;
        }

        private static X509Certificate2 CreateCertificate()
        {
            using Certificate created = CertificateBuilder
                .Create("CN=UA QUIC DataChannel Transport Join")
                .AddExtension(new X509SubjectAltNameExtension(
                    "urn:localhost:UA:QuicTransportJoin",
                    ["localhost"]))
                .SetNotBefore(DateTime.UtcNow.AddDays(-1))
                .SetNotAfter(DateTime.UtcNow.AddDays(1))
                .SetRSAKeySize(2048)
                .CreateForRSA();

            byte[] pfx = created.AsX509Certificate2().Export(X509ContentType.Pfx);
            return X509CertificateLoader.LoadPkcs12(pfx, null, X509KeyStorageFlags.Exportable);
        }

        private sealed class ServerTransportAllocator(
            QuicServerDataChannelTransport transport,
            SecureChannelContext secureChannelContext) : IDataChannelTransportStreamAllocator
        {
            public ValueTask<ulong> AllocateServerStreamAsync(
                DataChannelRequestContext context,
                uint channelId,
                DataChannelDirection direction,
                CancellationToken ct)
            {
                return transport.AllocateServerStreamAsync(
                    secureChannelContext,
                    channelId,
                    direction,
                    ct);
            }

            public ValueTask BindClientStreamAsync(
                DataChannelRequestContext context,
                uint channelId,
                ulong streamId,
                DataChannelDirection direction,
                CancellationToken ct)
            {
                return transport.BindClientStreamAsync(
                    secureChannelContext,
                    channelId,
                    streamId,
                    direction,
                    ct);
            }
        }

        private sealed class TestSource(
            NodeId nodeId,
            DataChannelSourceCapabilities capabilities) : IDataChannelSource
        {
            public NodeId NodeId { get; } = nodeId;

            public DataChannelSourceCapabilities Capabilities { get; } = capabilities;

            public int ActiveChannelCount => OpenedChannels.Count;

            public List<DataChannel> OpenedChannels { get; } = [];

            public void OnChannelOpened(DataChannel channel)
            {
                OpenedChannels.Add(channel);
            }

            public void OnChannelClosed(DataChannel channel, StatusCode reason)
            {
            }
        }

        private sealed class PermissiveAuthorizer : IDataChannelAuthorizer
        {
            public ValueTask<bool> IsAuthorizedAsync(
                DataChannelRequestContext context,
                NodeId sourceNodeId,
                DataChannelDirection direction,
                CancellationToken ct)
            {
                return new ValueTask<bool>(true);
            }
        }

        private static readonly NodeId SourceNodeId = new(6001u);

        private ITelemetryContext? m_telemetry;
        private BufferManager? m_bufferManager;
        private X509Certificate2? m_certificate;
    }
}

#endif
