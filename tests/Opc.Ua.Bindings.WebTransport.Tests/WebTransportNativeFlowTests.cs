// Copyright (c) OPC Foundation. Licensed under the OPC Foundation MIT License 1.00.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Bindings.Native;
using Opc.Ua.DataChannels;
using Opc.Ua.Native.QuicGo;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.Bindings.WebTransport.Tests
{
    /// <summary>
    /// Combined production QUIC/H3/Core peer exchange, with actual native ownership and byte evidence.
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    [Category("WebTransportNative")]
    public sealed partial class WebTransportNativeFlowTests
    {
        /// <summary>
        /// Selects the explicitly reviewed production native images once.
        /// </summary>
        [OneTimeSetUp]
        public async Task LoadAsync()
        {
            (m_backend, m_library) = await WebTransportTestEnvironment.LoadAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// Starts with no outstanding native ownership and fresh isolated certificate stores.
        /// </summary>
        [SetUp]
        public async Task SetUpAsync()
        {
            await AssertNativeReleasedAsync().ConfigureAwait(false);
            m_environment = await WebTransportTestEnvironment.CreateAsync(m_backend!, m_library!).ConfigureAwait(false);
        }

        /// <summary>
        /// Requires real native jobs/handles and all shared Core/H3/host reservations to be released.
        /// </summary>
        [TearDown]
        public async Task TearDownAsync()
        {
            if (m_environment != null)
            {
                await m_environment.DisposeAsync().ConfigureAwait(false);
                Assert.Multiple(() =>
                {
                    DataChannelResourceAssertions.AreReleased(m_environment.ClientBudget.Usage);
                    DataChannelResourceAssertions.AreReleased(m_environment.ServerBudget.Usage);
                });
            }
            await AssertNativeReleasedAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// Requires actual confirmed mTLS/h3 and a WT management resource, never CONNECT/HTTP control as Core ingress.
        /// </summary>
        [Test]
        public async Task NativePeersEstablishExactWebTransportBeforeCoreHello()
        {
            await using WebTransportPair pair = await m_environment!.ConnectPairAsync().ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(pair.Client.Context.IsReady, Is.True);
                Assert.That(pair.Server.Context.IsReady, Is.True);
                Assert.That(pair.Client.Context.Channels, Is.Empty);
                Assert.That(pair.Server.Context.Channels, Is.Empty);
                Assert.That(pair.Client.NativeConnection.Peer.HandshakeConfirmed, Is.True);
                Assert.That(pair.Client.NativeConnection.Peer.MutualTlsAuthenticated, Is.True);
                Assert.That(pair.Server.NativeConnection.Peer.HandshakeConfirmed, Is.True);
                Assert.That(pair.Server.NativeConnection.Peer.MutualTlsAuthenticated, Is.True);
                Assert.That(pair.Client.NativeConnection.Peer.Used0Rtt, Is.False);
                Assert.That(pair.Client.NativeConnection.Peer.Resumed, Is.False);
                Assert.That(pair.Client.NativeConnection.Peer.ApplicationProtocol, Is.EqualTo("h3"));
                Assert.That(pair.Client.NativeConnection.Peer.LocalResetStreamAt09, Is.True);
                Assert.That(pair.Client.NativeConnection.Peer.PeerResetStreamAt09, Is.True);
                Assert.That(pair.Client.ManagementStreamId, Is.Not.Zero);
                Assert.That(pair.Client.ManagementStreamId & 3, Is.Zero);
                Assert.That(pair.Server.ManagementStreamId, Is.EqualTo(pair.Client.ManagementStreamId));
                Assert.That(pair.Client.Identity.PeerKeyThumbprint,
                    Is.EqualTo(pair.Server.Identity.LocalKeyThumbprint));
                Assert.That(pair.Client.Context.Identity.ContextId, Is.EqualTo(pair.Server.Context.Identity.ContextId));
                Assert.That(pair.Client.Context.Identity.LocalChallenge,
                    Is.EqualTo(pair.Server.Context.Identity.PeerChallenge));
            });
            RecordedStreamWrite prefix = m_environment.ClientBackend.Writes.Single(
                value => value.StreamId == pair.Client.ManagementStreamId && (value.Flags & 2) != 0);
            Assert.That(prefix.Payload.Span.ToArray(), Is.EqualTo(new byte[] { 0x40, 0x41, 0 }));
            Assert.That(m_environment.ClientBackend.Writes.All(value => value.Payload.Length != 0), Is.True);
            Assert.That(m_environment.ClientBackend.Writes.Any(value => value.StreamId == 0), Is.True);
            Assert.That(m_environment.ServerBackend.Writes.Any(
                value => value.StreamId == pair.Server.ManagementStreamId && (value.Flags & 2) != 0), Is.False);
        }

        /// <summary>
        /// Creates production H3 state from actual server-authenticated TLS without inventing a UASC identity.
        /// </summary>
        [Test]
        public async Task BaselinePeerConfigurationUsesActualTlsOnlyFacts()
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await using NativeQuicListener listener = await NativeQuicListener.ListenTlsAsync(
                m_backend!,
                m_environment!.TlsServerCredentials,
                "127.0.0.1:0",
                DataChannelProfiles.WebTransportAlpn,
                m_environment.Options.Native,
                cancellationToken: deadline.Token).ConfigureAwait(false);
            Task<NativeQuicConnection> accepting = listener.AcceptAsync(deadline.Token).AsTask();
            await using NativeQuicConnection client = await NativeQuicConnection.ConnectTlsAsync(
                m_environment.ClientBackend,
                m_environment.TlsClientCredentials,
                listener.Address,
                DataChannelProfiles.WebTransportAlpn,
                "localhost",
                m_environment.Options.Native,
                cancellationToken: deadline.Token).ConfigureAwait(false);
            await using NativeQuicConnection server = await accepting.ConfigureAwait(false);

            await using NativeQuicStream connect = await client.OpenStreamAsync(cancellationToken: deadline.Token)
                .ConfigureAwait(false);
            NativeQuicStream[]? clientCritical = null;
            NativeQuicStream[]? serverCritical = null;
            try
            {
                clientCritical = await OpenCriticalStreamsAsync(client, deadline.Token).ConfigureAwait(false);
                serverCritical = await OpenCriticalStreamsAsync(server, deadline.Token).ConfigureAwait(false);
                Assert.That(connect.StreamId, Is.Zero);
                Uri endpoint = new("https://localhost/ua/wt?resource=pubsub");
                Assert.Throws<ArgumentException>(() => WebTransportDataChannelConnection.CreateBaselinePeer(
                    client, new Uri("http://localhost/ua/wt"), m_environment.Options,
                    clientCritical[0], clientCritical[1], clientCritical[2]));
                using Http3Handle clientPeer = WebTransportDataChannelConnection.CreateBaselinePeer(
                    client, endpoint, m_environment.Options,
                    clientCritical[0], clientCritical[1], clientCritical[2]);
                using Http3Handle serverPeer = WebTransportDataChannelConnection.CreateBaselinePeer(
                    server, endpoint, m_environment.Options,
                    serverCritical[0], serverCritical[1], serverCritical[2]);

                Assert.Multiple(() =>
                {
                    Assert.That(client.IsClient, Is.True);
                    Assert.That(server.IsClient, Is.False);
                    Assert.That(client.TlsPeer.ApplicationProtocol, Is.EqualTo("h3"));
                    Assert.That(client.TlsPeer.LocalCertificateUsed, Is.False);
                    Assert.That(server.TlsPeer.LocalCertificateUsed, Is.True);
                    Assert.That(client.VerifiedPeerCertificateChain, Is.Not.Empty);
                    Assert.That(server.VerifiedPeerCertificateChain, Is.Empty);
                    Assert.Throws<InvalidOperationException>(() => _ = client.Peer);
                    Assert.Throws<InvalidOperationException>(() => _ = server.Peer);
                    Assert.Throws<InvalidOperationException>(() => _ = client.Credentials);
                    Assert.Throws<InvalidOperationException>(() => _ = server.Credentials);
                });
            }
            finally
            {
                foreach (NativeQuicStream stream in (clientCritical ?? []).Concat(serverCritical ?? []))
                {
                    await stream.DisposeAsync().ConfigureAwait(false);
                }
            }
        }

        /// <summary>
        /// Completes real CONNECT and carries opaque UACP bytes on the first client-created WT stream.
        /// </summary>
        [Test]
        public async Task NativeBaselineCompletesConnectAndCarriesUascBytesWithoutTlsIdentity()
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            var budget = new DataChannelApplicationBudget();
            var transport = new WebTransportUascTransport(
                m_backend!, m_library!, budget, m_environment!.Options);
            Uri requestedEndpoint = new("https://127.0.0.1:0/ua/wt?resource=pubsub");
            await using WebTransportUascListener listener = await transport.ListenAsync(
                requestedEndpoint,
                new IPEndPoint(IPAddress.Loopback, 0),
                m_environment.TlsServerCredentials,
                static (_, cancellationToken) =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    return ValueTask.FromResult(true);
                },
                cancellationToken: deadline.Token).ConfigureAwait(false);
            Task<WebTransportUascBaselineConnection> accepting = listener.AcceptAsync(deadline.Token).AsTask();
            await using WebTransportUascBaselineConnection clientCarrier = await transport.ConnectAsync(
                listener.EndpointUrl, m_environment.TlsClientCredentials,
                cancellationToken: deadline.Token).ConfigureAwait(false);
            await using WebTransportUascBaselineConnection serverCarrier = await accepting.ConfigureAwait(false);

            byte[] transcriptBytes = Enumerable.Repeat((byte)0xA5, 32).ToArray();
            ByteString transcriptDigest = new(transcriptBytes);
            ByteString clientExporter = await clientCarrier.ExportKeyingMaterialAsync(
                transcriptDigest, deadline.Token).ConfigureAwait(false);
            ByteString serverExporter = await serverCarrier.ExportKeyingMaterialAsync(
                transcriptDigest, deadline.Token).ConfigureAwait(false);
            ByteString changedContextExporter = await clientCarrier.ExportKeyingMaterialAsync(
                new ByteString(Enumerable.Repeat((byte)0x5A, 32).ToArray()), deadline.Token).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(clientExporter.Length, Is.EqualTo(32));
                Assert.That(clientExporter.ToArray(), Is.EqualTo(serverExporter.ToArray()));
                Assert.That(clientExporter.ToArray(), Is.Not.EqualTo(changedContextExporter.ToArray()));
            });

            var clientBuffers = new BufferManager("WebTransportUascClient", 65536, m_environment.Telemetry);
            var serverBuffers = new BufferManager("WebTransportUascServer", 65536, m_environment.Telemetry);
            IUaSCByteTransport clientTransport = clientCarrier.Create(
                clientBuffers, 65536, m_environment.Telemetry);
            IUaSCByteTransport serverTransport = serverCarrier.Create(
                serverBuffers, 65536, m_environment.Telemetry);
            IDataChannelDirectBindingProvider clientBinding =
                clientTransport as IDataChannelDirectBindingProvider ??
                throw new InvalidOperationException("The client UASC stream did not receive its direct binding provider.");
            IDataChannelDirectBindingProvider serverBinding =
                serverTransport as IDataChannelDirectBindingProvider ??
                throw new InvalidOperationException("The server UASC stream did not receive its direct binding provider.");
            ByteString forwardedExporter = await clientBinding.ExportKeyingMaterialAsync(
                transcriptDigest, deadline.Token).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(clientBinding.IsAvailable && serverBinding.IsAvailable, Is.True);
                Assert.That(clientBinding.TransportProfileUri, Is.EqualTo(DataChannelProfiles.UascWebTransport));
                Assert.That(clientBinding.Capability, Is.EqualTo(DataChannelProfiles.WebTransportDirectCapability));
                Assert.That(clientBinding.Authority, Is.EqualTo(listener.EndpointUrl.Authority));
                Assert.That(clientBinding.Path, Is.EqualTo(listener.EndpointUrl.PathAndQuery));
                Assert.That(forwardedExporter.ToArray(), Is.EqualTo(clientExporter.ToArray()));
            });
            try
            {
                await clientTransport.ConnectAsync(listener.EndpointUrl, deadline.Token).ConfigureAwait(false);
                byte[] hello = [0x48, 0x45, 0x4c, 0x46, 0x0c, 0, 0, 0, 0x01, 0x02, 0x03, 0x04];
                await clientTransport.SendChunkAsync(hello, deadline.Token).ConfigureAwait(false);
                ArraySegment<byte> received = await serverTransport.ReceiveChunkAsync(deadline.Token)
                    .ConfigureAwait(false);
                try
                {
                    Assert.That(received.AsSpan().ToArray(), Is.EqualTo(hello));
                }
                finally
                {
                    serverBuffers.ReturnBuffer(received.Array, nameof(IUaSCByteTransport.ReceiveChunkAsync));
                }

                byte[] acknowledgement = [0x41, 0x43, 0x4b, 0x46, 0x0c, 0, 0, 0, 0x05, 0x06, 0x07, 0x08];
                await serverTransport.SendChunkAsync(acknowledgement, deadline.Token).ConfigureAwait(false);
                ArraySegment<byte> response = await clientTransport.ReceiveChunkAsync(deadline.Token)
                    .ConfigureAwait(false);
                try
                {
                    Assert.That(response.AsSpan().ToArray(), Is.EqualTo(acknowledgement));
                }
                finally
                {
                    clientBuffers.ReturnBuffer(response.Array, nameof(IUaSCByteTransport.ReceiveChunkAsync));
                }
                Assert.That(clientCarrier.TransportProfileUri, Is.EqualTo(DataChannelProfiles.UascWebTransport));
            }
            finally
            {
                clientTransport.Close();
                serverTransport.Close();
            }
        }

        /// <summary>
        /// Routes actual stream objects by token rather than by arrival order or numeric IDs.
        /// </summary>
        [TestCase(true)]
        [TestCase(false)]
        public async Task NativeDirectStreamProviderMatchesTokensAndPreservesBaseline(bool clientSends)
        {
            await WithNativeBaselineAsync(async (client, server, cancellationToken) =>
            {
                UascStreamByteTransport sender = clientSends ? client : server;
                UascStreamByteTransport receiver = clientSends ? server : client;
                Guid firstToken = Guid.NewGuid();
                Guid secondToken = Guid.NewGuid();
                Task<Stream> firstAdmission = receiver.AcceptDirectStreamAsync(firstToken, cancellationToken).AsTask();
                Task<Stream> secondAdmission = receiver.AcceptDirectStreamAsync(secondToken, cancellationToken)
                    .AsTask();
                await using Stream secondSend = await sender.OpenDirectStreamAsync(secondToken, cancellationToken)
                    .ConfigureAwait(false);
                await using Stream secondReceive = await secondAdmission.ConfigureAwait(false);
                Assert.That(firstAdmission.IsCompleted, Is.False);
                await using Stream firstSend = await sender.OpenDirectStreamAsync(firstToken, cancellationToken)
                    .ConfigureAwait(false);
                await using Stream firstReceive = await firstAdmission.ConfigureAwait(false);
                Assert.Multiple(() =>
                {
                    Assert.That(sender.AreDirectStreamsAvailable && receiver.AreDirectStreamsAvailable, Is.True);
                    Assert.That(ReferenceEquals(firstReceive, secondReceive), Is.False);
                    Assert.That(firstSend.CanWrite && !firstSend.CanRead, Is.True);
                    Assert.That(firstReceive.CanRead && !firstReceive.CanWrite, Is.True);
                });
                byte[] first = [0x05, 0, 0, 0, 0, 0x91, 0x82, 0x0C, 0x42];
                byte[] second = [0x03, 0, 0, 0, 0, 0xAA, 0xBB];
                await firstSend.WriteAsync(first.AsMemory(0, 2), cancellationToken).ConfigureAwait(false);
                await firstSend.WriteAsync(first.AsMemory(2), cancellationToken).ConfigureAwait(false);
                await secondSend.WriteAsync(second, cancellationToken).ConfigureAwait(false);
                byte[] firstReceived = new byte[first.Length];
                byte[] secondReceived = new byte[second.Length];
                await firstReceive.ReadExactlyAsync(firstReceived, cancellationToken).ConfigureAwait(false);
                await secondReceive.ReadExactlyAsync(secondReceived, cancellationToken).ConfigureAwait(false);
                Assert.Multiple(() =>
                {
                    Assert.That(firstReceived, Is.EqualTo(first));
                    Assert.That(secondReceived, Is.EqualTo(second));
                });
                DataChannelException reusedSend = await Assert.ThrowsAsync<DataChannelException>(async () =>
                    await sender.OpenDirectStreamAsync(firstToken, cancellationToken).ConfigureAwait(false));
                DataChannelException reusedReceive = await Assert.ThrowsAsync<DataChannelException>(async () =>
                    await receiver.AcceptDirectStreamAsync(firstToken, cancellationToken).ConfigureAwait(false));
                Assert.Multiple(() =>
                {
                    Assert.That(reusedSend.Result, Is.EqualTo(DataChannelResult.InvalidState));
                    Assert.That(reusedReceive.Result, Is.EqualTo(DataChannelResult.InvalidState));
                });
            }).ConfigureAwait(false);
        }

        /// <summary>
        /// Baseline sessions route unreliable input without waiting for the legacy standalone context.
        /// </summary>
        [TestCase(true)]
        [TestCase(false)]
        public async Task NativeDirectDatagramProviderPreservesBytesAndBaseline(bool clientSends)
        {
            await WithNativeBaselineAsync(async (client, server, cancellationToken) =>
            {
                UascStreamByteTransport sender = clientSends ? client : server;
                UascStreamByteTransport receiver = clientSends ? server : client;
                Assert.That(sender.MaxDirectDatagramSize, Is.GreaterThan(24));
                ByteString payload = new(new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05 });
                Task<ByteString> pending = receiver.ReceiveDirectDatagramAsync(cancellationToken).AsTask();
                var write = new DataChannelWrite(DataChannelPath.Datagram, 1, 0, 0, payload, true, 0,
                    static () => DataChannelResult.Good);
                Assert.That(await sender.SendDirectDatagramAsync(write, cancellationToken).ConfigureAwait(false),
                    Is.EqualTo(DataChannelResult.Good));
                ByteString received = await pending.WaitAsync(TimeSpan.FromSeconds(3), cancellationToken)
                    .ConfigureAwait(false);
                Assert.Multiple(() =>
                {
                    Assert.That(write.IsCommitted, Is.True);
                    Assert.That(received, Is.EqualTo(payload));
                });
            }).ConfigureAwait(false);
        }

        /// <summary>
        /// Cancelled receive and pre-commit refusal cannot retain the receive owner or transmit rejected bytes.
        /// </summary>
        [Test]
        public async Task NativeDirectDatagramProviderCancellationAndRefusalPreserveBaseline()
        {
            await WithNativeBaselineAsync(async (client, server, cancellationToken) =>
            {
                using var cancelled = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                Task<ByteString> abandoned = server.ReceiveDirectDatagramAsync(cancelled.Token).AsTask();
                DataChannelException concurrent = await Assert.ThrowsAsync<DataChannelException>(async () =>
                    await server.ReceiveDirectDatagramAsync(cancellationToken).ConfigureAwait(false));
                Assert.That(concurrent.Result, Is.EqualTo(DataChannelResult.InvalidState));
                await cancelled.CancelAsync().ConfigureAwait(false);
                await Assert.CatchAsync<OperationCanceledException>(async () => await abandoned.ConfigureAwait(false));
                Task<ByteString> pending = server.ReceiveDirectDatagramAsync(cancellationToken).AsTask();
                var rejected = new DataChannelWrite(DataChannelPath.Datagram, 1, 0, 0,
                    new ByteString(new byte[] { 1 }), true, 0, static () => DataChannelResult.WouldBlock);
                Assert.That(await client.SendDirectDatagramAsync(rejected, cancellationToken).ConfigureAwait(false),
                    Is.EqualTo(DataChannelResult.WouldBlock));
                var oversized = new DataChannelWrite(DataChannelPath.Datagram, 1, 0, 0,
                    new ByteString(new byte[client.MaxDirectDatagramSize + 1]), true, 0,
                    static () => DataChannelResult.Good);
                Assert.That(await client.SendDirectDatagramAsync(oversized, cancellationToken).ConfigureAwait(false),
                    Is.EqualTo(DataChannelResult.TooLarge));
                ByteString expected = new(new byte[] { 0x91, 0x82, 0, 42 });
                var accepted = new DataChannelWrite(DataChannelPath.Datagram, 1, 0, 0, expected, true, 0,
                    static () => DataChannelResult.Good);
                Assert.That(await client.SendDirectDatagramAsync(accepted, cancellationToken).ConfigureAwait(false),
                    Is.EqualTo(DataChannelResult.Good));
                Assert.That(await pending.WaitAsync(TimeSpan.FromSeconds(3), cancellationToken).ConfigureAwait(false),
                    Is.EqualTo(expected));
                Assert.Multiple(() =>
                {
                    Assert.That(rejected.IsCommitted, Is.False);
                    Assert.That(oversized.IsCommitted, Is.False);
                    Assert.That(accepted.IsCommitted, Is.True);
                });
            }).ConfigureAwait(false);
        }

        /// <summary>
        /// Completing a direct sender retains its entire payload through reset-based disposal.
        /// </summary>
        [TestCase(false, 1)]
        [TestCase(true, 1)]
        [TestCase(false, 8192)]
        [TestCase(true, 8192)]
        public async Task NativeDirectStreamCompletionRetainsPayloadAcrossDisposal(bool serverOpens, int payloadSize)
        {
            await WithNativeBaselineAsync(async (client, server, cancellationToken) =>
            {
                Guid token = Guid.NewGuid();
                UascStreamByteTransport opener = serverOpens ? server : client;
                UascStreamByteTransport acceptor = serverOpens ? client : server;
                Task<Stream> admission = acceptor.AcceptDirectStreamAsync(token, cancellationToken).AsTask();
                await using Stream send = await opener.OpenDirectStreamAsync(token, cancellationToken)
                    .ConfigureAwait(false);
                await using Stream receive = await admission.ConfigureAwait(false);
                byte[] expected = Enumerable.Range(0, payloadSize).Select(static value => (byte)value).ToArray();
                Assert.That(send, Is.InstanceOf<IDataChannelDirectStreamCompletion>());
                var completion = (IDataChannelDirectStreamCompletion)send;
                await Assert.ThrowsAsync<ArgumentException>(async () =>
                    await completion.CompleteWritesAsync(default, cancellationToken).ConfigureAwait(false));
                using var cancelled = new CancellationTokenSource();
                await cancelled.CancelAsync().ConfigureAwait(false);
                await Assert.CatchAsync<OperationCanceledException>(async () =>
                    await completion.CompleteWritesAsync(ByteString.FromHexString("ff"), cancelled.Token)
                        .ConfigureAwait(false));
                await Assert.ThrowsAsync<NotSupportedException>(async () =>
                    await ((IDataChannelDirectStreamCompletion)receive).CompleteWritesAsync(
                        ByteString.FromHexString("ff"), cancellationToken).ConfigureAwait(false));
                await send.WriteAsync(expected.AsMemory(0, expected.Length - 1), cancellationToken)
                    .ConfigureAwait(false);
                await completion.CompleteWritesAsync(
                    new ByteString(expected.AsSpan(expected.Length - 1).ToArray()), cancellationToken)
                    .ConfigureAwait(false);
                await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                    await completion.CompleteWritesAsync(ByteString.FromHexString("ff"), cancellationToken)
                        .ConfigureAwait(false));
                await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                    await send.WriteAsync(new byte[] { 0xff }, cancellationToken).ConfigureAwait(false));
                Assert.That(send.CanWrite, Is.False);
                await send.DisposeAsync().ConfigureAwait(false);
                byte[] actual = new byte[expected.Length];
                await receive.ReadExactlyAsync(actual, cancellationToken).ConfigureAwait(false);
                Assert.That(actual, Is.EqualTo(expected));
            }).ConfigureAwait(false);
        }

        /// <summary>
        /// Cancelled admission consumes its token but does not retire the session or unrelated admission.
        /// </summary>
        [Test]
        public async Task NativeDirectStreamProviderCancellationDoesNotPermitTokenReuse()
        {
            await WithNativeBaselineAsync(async (client, server, cancellationToken) =>
            {
                Guid token = Guid.NewGuid();
                using var cancelled = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                Task<Stream> pending = server.AcceptDirectStreamAsync(token, cancelled.Token).AsTask();
                await cancelled.CancelAsync().ConfigureAwait(false);
                await Assert.CatchAsync<OperationCanceledException>(async () => await pending.ConfigureAwait(false));
                DataChannelException reused = await Assert.ThrowsAsync<DataChannelException>(async () =>
                    await server.AcceptDirectStreamAsync(token, cancellationToken).ConfigureAwait(false));
                Assert.That(reused.Result, Is.EqualTo(DataChannelResult.InvalidState));
                Guid freshToken = Guid.NewGuid();
                Task<Stream> freshAdmission = server.AcceptDirectStreamAsync(freshToken, cancellationToken).AsTask();
                await using Stream send = await client.OpenDirectStreamAsync(freshToken, cancellationToken)
                    .ConfigureAwait(false);
                await using Stream receive = await freshAdmission.ConfigureAwait(false);
                await send.WriteAsync(new byte[] { 0x5A }, cancellationToken).ConfigureAwait(false);
                byte[] received = new byte[1];
                await receive.ReadExactlyAsync(received, cancellationToken).ConfigureAwait(false);
                Assert.That(received, Is.EqualTo(new byte[] { 0x5A }));
            }).ConfigureAwait(false);
        }

        /// <summary>
        /// An unknown stream cannot claim a waiting token or retire its session's reliable baseline.
        /// </summary>
        [Test]
        public async Task NativeDirectStreamProviderRefusesUnknownTokenWithoutStealingAdmission()
        {
            await WithNativeBaselineAsync(async (client, server, cancellationToken) =>
            {
                Guid expected = Guid.NewGuid();
                Task<Stream> admission = server.AcceptDirectStreamAsync(expected, cancellationToken).AsTask();
                await using (Stream unknown = await client.OpenDirectStreamAsync(
                    Guid.NewGuid(), cancellationToken).ConfigureAwait(false))
                {
                    Assert.That(admission.IsCompleted, Is.False);
                }
                await using Stream sender = await client.OpenDirectStreamAsync(expected, cancellationToken)
                    .ConfigureAwait(false);
                await using Stream receiver = await admission.ConfigureAwait(false);
                await sender.WriteAsync(new byte[] { 0xA1, 0xB2 }, cancellationToken).ConfigureAwait(false);
                byte[] received = new byte[2];
                await receiver.ReadExactlyAsync(received, cancellationToken).ConfigureAwait(false);
                Assert.That(received, Is.EqualTo(new byte[] { 0xA1, 0xB2 }));
            }).ConfigureAwait(false);
        }

        /// <summary>
        /// Invalid and pre-cancelled requests do not consume a token or a pending-admission slot.
        /// </summary>
        [Test]
        public async Task NativeDirectStreamProviderValidatesBeforeAdmission()
        {
            await WithNativeBaselineAsync(async (client, server, cancellationToken) =>
            {
                DataChannelException empty = await Assert.ThrowsAsync<DataChannelException>(async () =>
                    await server.AcceptDirectStreamAsync(Guid.Empty, cancellationToken).ConfigureAwait(false));
                DataChannelException emptySend = await Assert.ThrowsAsync<DataChannelException>(async () =>
                    await client.OpenDirectStreamAsync(Guid.Empty, cancellationToken).ConfigureAwait(false));
                Assert.Multiple(() =>
                {
                    Assert.That(empty.Result, Is.EqualTo(DataChannelResult.InvalidArgument));
                    Assert.That(emptySend.Result, Is.EqualTo(DataChannelResult.InvalidArgument));
                });
                Guid token = Guid.NewGuid();
                using var cancelled = new CancellationTokenSource();
                await cancelled.CancelAsync().ConfigureAwait(false);
                await Assert.CatchAsync<OperationCanceledException>(async () =>
                    await server.AcceptDirectStreamAsync(token, cancelled.Token).ConfigureAwait(false));
                await Assert.CatchAsync<OperationCanceledException>(async () =>
                    await client.OpenDirectStreamAsync(token, cancelled.Token).ConfigureAwait(false));
                Task<Stream> admission = server.AcceptDirectStreamAsync(token, cancellationToken).AsTask();
                await using Stream sender = await client.OpenDirectStreamAsync(token, cancellationToken)
                    .ConfigureAwait(false);
                await using Stream receiver = await admission.ConfigureAwait(false);
                await sender.WriteAsync(new byte[] { 0xC3 }, cancellationToken).ConfigureAwait(false);
                byte[] received = new byte[1];
                await receiver.ReadExactlyAsync(received, cancellationToken).ConfigureAwait(false);
                Assert.That(received[0], Is.EqualTo(0xC3));
            }).ConfigureAwait(false);
        }

        /// <summary>
        /// Pending tokens have an independent finite limit and cancelled history cannot be recycled.
        /// </summary>
        [Test]
        public async Task NativeDirectStreamProviderBoundsPendingAdmissionAndTokenHistory()
        {
            await WithNativeBaselineAsync(async (_, server, cancellationToken) =>
            {
                using var cancelling = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                Task<Stream>[] pending = Enumerable.Range(0, 8)
                    .Select(_ => server.AcceptDirectStreamAsync(Guid.NewGuid(), cancelling.Token).AsTask())
                    .ToArray();
                Guid lastToken = Guid.NewGuid();
                DataChannelException saturated = await Assert.ThrowsAsync<DataChannelException>(async () =>
                    await server.AcceptDirectStreamAsync(lastToken, cancellationToken).ConfigureAwait(false));
                Assert.That(saturated.Result, Is.EqualTo(DataChannelResult.WouldBlock));
                await cancelling.CancelAsync().ConfigureAwait(false);
                foreach (Task<Stream> admission in pending)
                {
                    await Assert.CatchAsync<OperationCanceledException>(async () =>
                        await admission.ConfigureAwait(false));
                }
                using var lastCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                Task<Stream> last = server.AcceptDirectStreamAsync(lastToken, lastCancellation.Token).AsTask();
                Assert.That(last.IsCompleted, Is.False);
                await lastCancellation.CancelAsync().ConfigureAwait(false);
                await Assert.CatchAsync<OperationCanceledException>(async () => await last.ConfigureAwait(false));
                DataChannelException exhausted = await Assert.ThrowsAsync<DataChannelException>(async () =>
                    await server.AcceptDirectStreamAsync(Guid.NewGuid(), cancellationToken).ConfigureAwait(false));
                Assert.That(exhausted.Result, Is.EqualTo(DataChannelResult.WouldBlock));
            }, tokenHistory: 9).ConfigureAwait(false);
        }

        /// <summary>
        /// Asynchronous stream writes preserve a body spanning several bounded transport copies.
        /// </summary>
        [Test]
        public async Task NativeDirectStreamProviderSegmentsLargeWritesWithoutChangingBytes()
        {
            await WithNativeBaselineAsync(async (client, server, cancellationToken) =>
            {
                Guid token = Guid.NewGuid();
                Task<Stream> admission = server.AcceptDirectStreamAsync(token, cancellationToken).AsTask();
                await using Stream sender = await client.OpenDirectStreamAsync(token, cancellationToken)
                    .ConfigureAwait(false);
                await using Stream receiver = await admission.ConfigureAwait(false);
                byte[] expected = Enumerable.Range(0, 131089).Select(i => (byte)(i * 31 + 7)).ToArray();
                byte[] received = new byte[expected.Length];
                Task reading = receiver.ReadExactlyAsync(received, cancellationToken).AsTask();
                await sender.WriteAsync(expected, cancellationToken).ConfigureAwait(false);
                await reading.ConfigureAwait(false);
                Assert.That(received, Is.EqualTo(expected));
            }, readSize: 65536).ConfigureAwait(false);
        }

        /// <summary>
        /// Expired admission retains its token history while a fresh admission and the baseline remain usable.
        /// </summary>
        [Test]
        public async Task NativeDirectStreamProviderTimeoutDoesNotPermitTokenReuse()
        {
            await WithNativeBaselineAsync(async (client, server, cancellationToken) =>
            {
                Guid expired = Guid.NewGuid();
                DataChannelException timeout = await Assert.ThrowsAsync<DataChannelException>(async () =>
                    await server.AcceptDirectStreamAsync(expired, cancellationToken).ConfigureAwait(false));
                DataChannelException reused = await Assert.ThrowsAsync<DataChannelException>(async () =>
                    await server.AcceptDirectStreamAsync(expired, cancellationToken).ConfigureAwait(false));
                Assert.Multiple(() =>
                {
                    Assert.That(timeout.Result, Is.EqualTo(DataChannelResult.Timeout));
                    Assert.That(reused.Result, Is.EqualTo(DataChannelResult.InvalidState));
                });
                Guid fresh = Guid.NewGuid();
                Task<Stream> admission = server.AcceptDirectStreamAsync(fresh, cancellationToken).AsTask();
                await using Stream sender = await client.OpenDirectStreamAsync(fresh, cancellationToken)
                    .ConfigureAwait(false);
                await using Stream receiver = await admission.ConfigureAwait(false);
                await sender.WriteAsync(new byte[] { 0xD4 }, cancellationToken).ConfigureAwait(false);
                byte[] received = new byte[1];
                await receiver.ReadExactlyAsync(received, cancellationToken).ConfigureAwait(false);
                Assert.That(received[0], Is.EqualTo(0xD4));
            }).ConfigureAwait(false);
        }

        /// <summary>
        /// Closing one actual direction interrupts its reader without retiring a sibling or the baseline.
        /// </summary>
        [Test]
        public async Task NativeDirectStreamProviderDisposalIsScopedAndIdempotent()
        {
            await WithNativeBaselineAsync(async (client, server, cancellationToken) =>
            {
                Guid first = Guid.NewGuid();
                Guid second = Guid.NewGuid();
                Task<Stream> firstAdmission = server.AcceptDirectStreamAsync(first, cancellationToken).AsTask();
                Task<Stream> secondAdmission = server.AcceptDirectStreamAsync(second, cancellationToken).AsTask();
                await using Stream firstSender = await client.OpenDirectStreamAsync(first, cancellationToken)
                    .ConfigureAwait(false);
                await using Stream firstReceiver = await firstAdmission.ConfigureAwait(false);
                await using Stream secondSender = await client.OpenDirectStreamAsync(second, cancellationToken)
                    .ConfigureAwait(false);
                await using Stream secondReceiver = await secondAdmission.ConfigureAwait(false);
                Task<int> reading = firstReceiver.ReadAsync(new byte[1], cancellationToken).AsTask();
                await firstSender.DisposeAsync().ConfigureAwait(false);
                await firstSender.DisposeAsync().ConfigureAwait(false);
                DataChannelException reset = await Assert.CatchAsync<DataChannelException>(async () =>
                    await reading.ConfigureAwait(false));
                Assert.Multiple(() =>
                {
                    Assert.That(reset.Result, Is.EqualTo(DataChannelResult.Closed));
                    Assert.That(firstSender.CanRead || firstSender.CanWrite, Is.False);
                });
                await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
                    await firstSender.WriteAsync(new byte[1], cancellationToken).ConfigureAwait(false));
                await secondSender.WriteAsync(new byte[] { 0xE5 }, cancellationToken).ConfigureAwait(false);
                byte[] received = new byte[1];
                await secondReceiver.ReadExactlyAsync(received, cancellationToken).ConfigureAwait(false);
                Assert.That(received[0], Is.EqualTo(0xE5));
            }).ConfigureAwait(false);
        }

        /// <summary>
        /// Runs the ordinary UACP/UASC HEL, OPN, Service and close paths over the actual WebTransport baseline.
        /// </summary>
        [TestCase(DataChannelDeliveryMode.Ordered)]
        [TestCase(DataChannelDeliveryMode.Datagram)]
        [TestCase(DataChannelDeliveryMode.Ordered, DataChannelProfiles.UadpOpaqueApplicationPayload,
            DataChannelDirection.Duplex, false)]
        [TestCase(DataChannelDeliveryMode.Ordered, DataChannelProfiles.UadpOpaqueApplicationPayload,
            DataChannelDirection.Duplex, true)]
        [TestCase(DataChannelDeliveryMode.Ordered, DataChannelProfiles.UadpOpaqueApplicationPayload,
            DataChannelDirection.OpenerToAcceptor, false)]
        [TestCase(DataChannelDeliveryMode.Ordered, DataChannelProfiles.UadpOpaqueApplicationPayload,
            DataChannelDirection.OpenerToAcceptor, true)]
        [TestCase(DataChannelDeliveryMode.Ordered, DataChannelProfiles.UadpOpaqueApplicationPayload,
            DataChannelDirection.AcceptorToOpener, false)]
        [TestCase(DataChannelDeliveryMode.Ordered, DataChannelProfiles.UadpOpaqueApplicationPayload,
            DataChannelDirection.AcceptorToOpener, true)]
        [TestCase(DataChannelDeliveryMode.Datagram, DataChannelProfiles.UadpOpaqueApplicationPayload,
            DataChannelDirection.Duplex, false)]
        [TestCase(DataChannelDeliveryMode.Datagram, DataChannelProfiles.UadpOpaqueApplicationPayload,
            DataChannelDirection.Duplex, true)]
        [TestCase(DataChannelDeliveryMode.Datagram, DataChannelProfiles.UadpOpaqueApplicationPayload,
            DataChannelDirection.OpenerToAcceptor, false)]
        [TestCase(DataChannelDeliveryMode.Datagram, DataChannelProfiles.UadpOpaqueApplicationPayload,
            DataChannelDirection.OpenerToAcceptor, true)]
        [TestCase(DataChannelDeliveryMode.Datagram, DataChannelProfiles.UadpOpaqueApplicationPayload,
            DataChannelDirection.AcceptorToOpener, false)]
        [TestCase(DataChannelDeliveryMode.Datagram, DataChannelProfiles.UadpOpaqueApplicationPayload,
            DataChannelDirection.AcceptorToOpener, true)]
        [TestCase(DataChannelDeliveryMode.Ordered, DataChannelProfiles.UadpAvApplicationPayload,
            DataChannelDirection.Duplex, false)]
        [TestCase(DataChannelDeliveryMode.Datagram, DataChannelProfiles.UadpAvApplicationPayload,
            DataChannelDirection.Duplex, true)]
        public async Task NativeBaselineCarriesSecureChannelServicesAndConfiguredFlowWithoutTlsIdentity(
            DataChannelDeliveryMode delivery,
            string protocol = DataChannelProfiles.UadpPayload,
            DataChannelDirection direction = DataChannelDirection.Duplex,
            bool serverOpens = false)
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            const string securityPolicy = SecurityPolicies.Basic256Sha256;
            var clientTransport = new WebTransportUascTransport(
                m_environment!.ClientBackend, m_library!, m_environment.ClientBudget, m_environment.Options);
            var serverTransport = new WebTransportUascTransport(
                m_environment.ServerBackend, m_library!, m_environment.ServerBudget, m_environment.Options);
            await using WebTransportUascListener webTransportListener = await serverTransport.ListenAsync(
                new Uri("https://127.0.0.1:0/ua/wt?resource=service"),
                new IPEndPoint(IPAddress.Loopback, 0),
                m_environment.TlsServerCredentials,
                static (_, cancellationToken) =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    return ValueTask.FromResult(true);
                },
                cancellationToken: deadline.Token).ConfigureAwait(false);
            using CertificateEntry serverCertificate =
                m_environment.ServerManager.AcquireApplicationCertificateBySecurityPolicy(securityPolicy)!;
            using CertificateEntry clientCertificate =
                m_environment.ClientManager.AcquireApplicationCertificateBySecurityPolicy(securityPolicy)!;
            using var clientCertificateChain = new CertificateCollection { clientCertificate.Certificate };
            foreach (Certificate issuer in clientCertificate.IssuerChain)
            {
                clientCertificateChain.Add(issuer);
            }
            Uri endpointUrl = webTransportListener.EndpointUrl;
            EndpointDescription endpoint = new()
            {
                EndpointUrl = endpointUrl.ToString(),
                SecurityMode = MessageSecurityMode.SignAndEncrypt,
                SecurityPolicyUri = securityPolicy,
                ServerCertificate = serverCertificate.Certificate.RawData.ToByteString(),
                TransportProfileUri = DataChannelProfiles.UascWebTransport,
                Server = new ApplicationDescription
                {
                    ApplicationUri = WebTransportTestEnvironment.ServerUri
                }
            };
            EndpointConfiguration configuration = EndpointConfiguration.Create();
            configuration.OperationTimeout = 10000;
            configuration.MaxMessageSize = 64 * 1024;
            configuration.MaxBufferSize = 64 * 1024;
            configuration.ChannelLifetime = 60000;
            configuration.SecurityTokenLifetime = 60000;
            var callback = new WebTransportEchoCallback();
            await using var uascListener = new TcpTransportListener(m_environment.Telemetry);
            uascListener.OpenExternal(
                endpointUrl,
                new TransportListenerSettings
                {
                    Descriptions = new List<EndpointDescription> { endpoint },
                    Configuration = configuration,
                    ServerCertificates = m_environment.ServerManager,
                    CertificateValidator = m_environment.ServerManager,
                    DataChannels = m_environment.ServerFactory,
                    NamespaceUris = new NamespaceTable(),
                    Factory = EncodeableFactory.Create(),
                    MaxChannelCount = 4
                },
                callback);

            Task<WebTransportUascBaselineConnection> accepting =
                webTransportListener.AcceptAsync(deadline.Token).AsTask();
            await using WebTransportUascBaselineConnection clientCarrier = await clientTransport.ConnectAsync(
                endpointUrl, m_environment.TlsClientCredentials,
                cancellationToken: deadline.Token).ConfigureAwait(false);
            await using WebTransportUascBaselineConnection serverCarrier = await accepting.ConfigureAwait(false);
            TcpListenerChannel acceptedChannel =
                uascListener.AcceptTransport(serverCarrier, m_environment.ServerManager, configuration.MaxBufferSize);

            using ITransportChannel channel = clientCarrier.Create(m_environment.Telemetry);
            try
            {
                await ((ISecureChannel)channel).OpenAsync(
                    endpointUrl,
                    new TransportChannelSettings
                    {
                        Description = endpoint,
                        Configuration = configuration,
                        ClientCertificate = clientCertificate.Certificate,
                        ClientCertificateChain = clientCertificateChain,
                        ServerCertificate = serverCertificate.Certificate,
                        CertificateValidator = m_environment.ClientManager,
                        DataChannels = m_environment.ClientFactory,
                        NamespaceUris = new NamespaceTable(),
                        Factory = EncodeableFactory.Create()
                    },
                    deadline.Token).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                Assert.Fail(callback.OpenSecureChannelError?.ToString() ?? exception.ToString());
            }
            IServiceResponse response = await channel.SendRequestAsync(
                new ReadRequest
                {
                    RequestHeader = new RequestHeader { TimeoutHint = 10000 },
                    NodesToRead = new ArrayOf<ReadValueId>()
                },
                deadline.Token).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(response, Is.InstanceOf<ReadResponse>());
                Assert.That(callback.RequestCount, Is.EqualTo(1));
                Assert.That(channel.EndpointDescription.TransportProfileUri,
                    Is.EqualTo(DataChannelProfiles.UascWebTransport));
            });
            DataChannelContext clientContext = ((IDataChannelTransportChannel)channel).DataChannels ??
                throw new InvalidOperationException("The configured WebTransport data context was not attached.");
            DataChannelContext serverContext = acceptedChannel.DataChannels ??
                throw new InvalidOperationException("The accepted WebTransport data context was not attached.");
            Assert.That(clientContext.CarrierKind, Is.EqualTo(DataChannelCarrierKind.WebTransportUasc));
            Assert.That(serverContext.CarrierKind, Is.EqualTo(DataChannelCarrierKind.WebTransportUasc));
            Assert.That(clientContext.IsDirectBindingProviderAvailable, Is.True);
            Assert.That(serverContext.IsDirectBindingProviderAvailable, Is.True);
            Assert.That(
                await clientContext.WaitForDirectBindingAsync(deadline.Token).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            Assert.That(
                await serverContext.WaitForDirectBindingAsync(deadline.Token).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            using IDisposable publisherRoute = serverContext.RegisterUascPublisherRoute(
                WebTransportTestEnvironment.ResourceUri, new Variant(42u));
            using IDisposable reversePublisherRoute = clientContext.RegisterUascPublisherRoute(
                WebTransportTestEnvironment.ResourceUri, new Variant(42u));
            DataChannelContext opener = serverOpens ? serverContext : clientContext;
            DataChannelContext acceptor = serverOpens ? clientContext : serverContext;
            Task<DataChannel?> acceptingFlow = acceptor.AcceptAsync(deadline.Token).AsTask();
            DataChannelParametersDataType flowParameters = WebTransportTestEnvironment.Parameters();
            flowParameters.Protocol = protocol;
            flowParameters.ContentType = protocol switch
            {
                DataChannelProfiles.UadpAvApplicationPayload => "video/h264",
                DataChannelProfiles.UadpOpaqueApplicationPayload => "application/octet-stream",
                _ => "application/opcua+uadp"
            };
            flowParameters.Direction = direction;
            flowParameters.Delivery = delivery;
            flowParameters.MaxRate = delivery == DataChannelDeliveryMode.Datagram ? 100000 : 0;
            await using DataChannel openedFlow = await opener.OpenAsync(
                flowParameters, cancellationToken: deadline.Token).ConfigureAwait(false);
            await using DataChannel acceptedFlow = await acceptingFlow.ConfigureAwait(false) ??
                throw new InvalidOperationException("The server data context closed before READY.");
            DataChannel sender = direction == DataChannelDirection.AcceptorToOpener ? acceptedFlow : openedFlow;
            DataChannel receiver = direction == DataChannelDirection.AcceptorToOpener ? openedFlow : acceptedFlow;
            byte[] payload = protocol == DataChannelProfiles.UadpPayload
                ? [0x91, 0x82, 0, 42, 0, 0, 0, 0x10, 0x20, 0, 0x40, 0xff]
                : [0x91, 0x82, 0x0c, 42, 0, 0, 0, 0, 0xff, 0x01, 0x7f];
            Assert.That(
                await sender.SendAsync(new ByteString(payload), cancellationToken: deadline.Token)
                    .ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            using DataChannelMessage? received = await receiver.ReadAsync(deadline.Token).ConfigureAwait(false);
            Assert.That(received!.Payload.Span.ToArray(), Is.EqualTo(payload));
            if (direction == DataChannelDirection.Duplex)
            {
                Assert.That(
                    await receiver.SendAsync(new ByteString(payload), cancellationToken: deadline.Token)
                        .ConfigureAwait(false),
                    Is.EqualTo(DataChannelResult.Good));
            }
            using DataChannelMessage? reverseReceived = direction == DataChannelDirection.Duplex
                ? await sender.ReadAsync(deadline.Token).ConfigureAwait(false) : null;
            if (direction == DataChannelDirection.Duplex)
            {
                Assert.That(reverseReceived!.Payload.Span.ToArray(), Is.EqualTo(payload));
            }
            DataChannelParametersDataType revised = openedFlow.Parameters;
            revised.MaxMessageSize = 128;
            var peerUpdated = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            void OnPeerUpdated(DataChannelStateChangedEventArgs notification)
            {
                if (notification.Status.Revision == 2 && notification.Status.State == DataChannelState.Open)
                {
                    peerUpdated.TrySetResult();
                }
            }
            acceptedFlow.StateChanged += OnPeerUpdated;
            try
            {
                Assert.That(await openedFlow.UpdateAsync(revised, cancellationToken: deadline.Token)
                    .ConfigureAwait(false), Is.EqualTo(DataChannelResult.Good));
                await peerUpdated.Task.WaitAsync(deadline.Token).ConfigureAwait(false);
            }
            finally
            {
                acceptedFlow.StateChanged -= OnPeerUpdated;
            }
            Assert.Multiple(() =>
            {
                Assert.That(openedFlow.Revision, Is.EqualTo(2u));
                Assert.That(acceptedFlow.Revision, Is.EqualTo(2u));
                Assert.That(received.Revision, Is.EqualTo(1u));
                if (direction == DataChannelDirection.Duplex)
                {
                    Assert.That(reverseReceived!.Revision, Is.EqualTo(1u));
                }
            });
            Assert.That(await sender.SendAsync(new ByteString(payload), cancellationToken: deadline.Token)
                .ConfigureAwait(false), Is.EqualTo(DataChannelResult.Good));
            using (DataChannelMessage? current = await receiver.ReadAsync(deadline.Token).ConfigureAwait(false))
            {
                Assert.That(current!.Revision, Is.EqualTo(2u));
                Assert.That(current.Payload.Span.ToArray(), Is.EqualTo(payload));
            }
            Assert.That(await sender.EndAsync(deadline.Token).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            Assert.That(await receiver.ReadAsync(deadline.Token).ConfigureAwait(false), Is.Null);
            Assert.That(await receiver.EndAsync(deadline.Token).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            Assert.That(
                await channel.SendRequestAsync(
                    new ReadRequest
                    {
                        RequestHeader = new RequestHeader { TimeoutHint = 10000 },
                        NodesToRead = new ArrayOf<ReadValueId>()
                    },
                    deadline.Token).ConfigureAwait(false),
                Is.InstanceOf<ReadResponse>());
            await channel.CloseAsync(deadline.Token).ConfigureAwait(false);
        }

        /// <summary>
        /// Denied CONNECT never reaches UACP and releases both TLS-only baseline reservations.
        /// </summary>
        [Test]
        public async Task NativeBaselineRefusesUnauthorizedConnectBeforeUasc()
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var budget = new DataChannelApplicationBudget();
            var transport = new WebTransportUascTransport(m_backend!, m_library!, budget, m_environment!.Options);
            await using WebTransportUascListener listener = await transport.ListenAsync(
                new Uri("https://127.0.0.1:0/ua/wt?resource=denied"),
                new IPEndPoint(IPAddress.Loopback, 0),
                m_environment.TlsServerCredentials,
                static (_, cancellationToken) =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    return ValueTask.FromResult(false);
                },
                cancellationToken: deadline.Token).ConfigureAwait(false);
            Task<WebTransportUascBaselineConnection> accepting = listener.AcceptAsync(deadline.Token).AsTask();

            Exception? clientFailure = null;
            try
            {
                await using WebTransportUascBaselineConnection unexpected = await transport.ConnectAsync(
                    listener.EndpointUrl,
                    m_environment.TlsClientCredentials,
                    cancellationToken: deadline.Token).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                clientFailure = exception;
            }
            Assert.That(clientFailure, Is.Not.Null, "A denied CONNECT must not establish a UACP baseline.");
            try
            {
                await accepting.ConfigureAwait(false);
                Assert.Fail("The listener must not return an unauthorized baseline.");
            }
            catch (Exception)
            {
                Assert.That(accepting.IsFaulted, Is.True);
            }
            DataChannelResourceAssertions.AreReleased(budget.Usage);
        }

        /// <summary>
        /// Supplies real TLS-only H3 peers and verifies the baseline still carries UACP after direct activity.
        /// These transport-primitive tests do not substitute for authenticated FLC association tests.
        /// </summary>
        private async Task WithNativeBaselineAsync(
            Func<UascStreamByteTransport, UascStreamByteTransport, CancellationToken, Task> scenario,
            int tokenHistory = 256,
            int readSize = 13)
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var budget = new DataChannelApplicationBudget();
            WebTransportDataChannelOptions options = m_environment!.Options with
            {
                MaxStreamHistory = tokenHistory,
                MaxHostBufferedBytes = Math.Max(m_environment.Options.MaxHostBufferedBytes,
                    m_environment.Options.Http3OutputBytes +
                        m_environment.Options.Native.MaxStreams * readSize + 65536),
                Native = m_environment.Options.Native with { ReadSize = readSize }
            };
            var transport = new WebTransportUascTransport(m_backend!, m_library!, budget, options);
            await using (WebTransportUascListener listener = await transport.ListenAsync(
                new Uri("https://127.0.0.1:0/ua/wt?resource=direct"),
                new IPEndPoint(IPAddress.Loopback, 0),
                m_environment.TlsServerCredentials,
                static (_, token) =>
                {
                    token.ThrowIfCancellationRequested();
                    return ValueTask.FromResult(true);
                },
                cancellationToken: deadline.Token).ConfigureAwait(false))
            {
                Task<WebTransportUascBaselineConnection> accepting = listener.AcceptAsync(deadline.Token).AsTask();
                await using WebTransportUascBaselineConnection client = await transport.ConnectAsync(
                    listener.EndpointUrl, m_environment.TlsClientCredentials,
                    cancellationToken: deadline.Token).ConfigureAwait(false);
                await using WebTransportUascBaselineConnection server = await accepting.ConfigureAwait(false);
                var clientBuffers = new BufferManager("DirectBaselineClient", 65536, m_environment.Telemetry);
                var serverBuffers = new BufferManager("DirectBaselineServer", 65536, m_environment.Telemetry);
                using var clientBytes = (UascStreamByteTransport)client.Create(
                    clientBuffers, 65536, m_environment.Telemetry);
                using var serverBytes = (UascStreamByteTransport)server.Create(
                    serverBuffers, 65536, m_environment.Telemetry);
                await clientBytes.ConnectAsync(listener.EndpointUrl, deadline.Token).ConfigureAwait(false);
                Assert.Multiple(() =>
                {
                    Assert.That(clientBytes, Is.InstanceOf<IDataChannelDirectStreamProvider>());
                    Assert.That(serverBytes, Is.InstanceOf<IDataChannelDirectStreamProvider>());
                    Assert.That(((IDataChannelDirectStreamProvider)clientBytes).AreDirectStreamsAvailable, Is.True);
                    Assert.That(((IDataChannelDirectStreamProvider)serverBytes).AreDirectStreamsAvailable, Is.True);
                });
                await scenario(clientBytes, serverBytes, deadline.Token).ConfigureAwait(false);
                byte[] hello = [0x48, 0x45, 0x4C, 0x46, 0x0C, 0, 0, 0, 1, 2, 3, 4];
                await clientBytes.SendChunkAsync(hello, deadline.Token).ConfigureAwait(false);
                ArraySegment<byte> received = await serverBytes.ReceiveChunkAsync(deadline.Token).ConfigureAwait(false);
                try
                {
                    Assert.That(received.ToArray(), Is.EqualTo(hello));
                }
                finally
                {
                    serverBuffers.ReturnBuffer(received.Array, nameof(WithNativeBaselineAsync));
                }
            }
            DataChannelResourceAssertions.AreReleased(budget.Usage);
        }

        private static async ValueTask<NativeQuicStream[]> OpenCriticalStreamsAsync(
            NativeQuicConnection connection,
            CancellationToken cancellationToken)
        {
            NativeQuicStream[] streams = new NativeQuicStream[3];
            int opened = 0;
            try
            {
                for (; opened < streams.Length; opened++)
                {
                    streams[opened] = await connection.OpenStreamAsync(true, cancellationToken).ConfigureAwait(false);
                }
                return streams;
            }
            catch
            {
                for (int index = 0; index < opened; index++)
                {
                    await streams[index].DisposeAsync().ConfigureAwait(false);
                }
                throw;
            }
        }

        private sealed class WebTransportEchoCallback : ITransportListenerCallback
        {
            public int RequestCount => Volatile.Read(ref m_requestCount);

            public Exception? OpenSecureChannelError { get; private set; }

            public ValueTask<IServiceResponse> ProcessRequestAsync(
                SecureChannelContext secureChannelContext,
                IServiceRequest request,
                CancellationToken cancellationToken = default)
            {
                Interlocked.Increment(ref m_requestCount);
                Assert.That(secureChannelContext, Is.Not.Null);
                Assert.That(request, Is.InstanceOf<ReadRequest>());
                return ValueTask.FromResult<IServiceResponse>(new ReadResponse
                {
                    ResponseHeader = new ResponseHeader { ServiceResult = StatusCodes.Good }
                });
            }

            public bool TryGetSecureChannelIdForAuthenticationToken(NodeId authenticationToken, out uint channelId)
            {
                channelId = 0;
                return false;
            }

            public void ReportAuditOpenSecureChannelEvent(
                string globalChannelId,
                EndpointDescription endpointDescription,
                OpenSecureChannelRequest request,
                Certificate clientCertificate,
                Exception exception)
            {
                OpenSecureChannelError = exception;
            }

            public void ReportAuditCloseSecureChannelEvent(string globalChannelId, Exception exception)
            {
            }

            public void ReportAuditCertificateEvent(Certificate clientCertificate, Exception exception)
            {
            }

            private int m_requestCount;
        }

        /// <summary>
        /// Both transport roles open actual distinct bidi streams and exchange intact application records.
        /// </summary>
        [TestCase(true)]
        [TestCase(false)]
        public async Task BothRolesExchangeWholeRecordsOnActualStreams(bool clientOpens)
        {
            await using WebTransportPair pair = await m_environment!.ConnectPairAsync().ConfigureAwait(false);
            (DataChannel opener, DataChannel acceptor) = await pair.OpenAsync(clientOpens).ConfigureAwait(false);
            byte[] payload = [0, 1, 255, 17, 0, 128, 3];
            Assert.That(await opener.SendAsync(new ByteString(payload)).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using DataChannelMessage? first = await acceptor.ReadAsync(deadline.Token).ConfigureAwait(false);
            Assert.That(first, Is.Not.Null);
            Assert.That(first!.Payload.Span.ToArray(), Is.EqualTo(payload));
            byte[] reverse = [9, 0, 8, 255];
            Assert.That(await acceptor.SendAsync(new ByteString(reverse)).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            using DataChannelMessage? response = await opener.ReadAsync(deadline.Token).ConfigureAwait(false);
            Assert.That(response!.Payload.Span.ToArray(), Is.EqualTo(reverse));
            Assert.Multiple(() =>
            {
                Assert.That(opener.IsLocalOpener, Is.True);
                Assert.That(acceptor.IsLocalOpener, Is.False);
                Assert.That(opener.ChannelId % 2, Is.EqualTo(clientOpens ? 1 : 0));
                Assert.That(opener.GetDiagnostics().MessagesSent, Is.EqualTo(1));
                Assert.That(acceptor.GetDiagnostics().MessagesReceived, Is.EqualTo(1));
            });
        }

        /// <summary>
        /// Uses one real HTTP-framed datagram, preserving the Core ChannelId+record and final capacity.
        /// </summary>
        [Test]
        public async Task DatagramCarriesExactCoreBytesOnce()
        {
            await using WebTransportPair pair = await m_environment!.ConnectPairAsync().ConfigureAwait(false);
            DataChannelParametersDataType parameters =
                WebTransportTestEnvironment.Parameters(DataChannelDeliveryMode.Datagram);
            parameters.MaxMessageSize = 4096;
            (DataChannel source, DataChannel sink) = await pair.OpenAsync(parameters: parameters).ConfigureAwait(false);
            byte[] payload = [.. Enumerable.Range(0, checked((int)source.Parameters.MaxMessageSize)).Select(value => (byte)(value % 251))];
            Assert.That(await source.SendAsync(new ByteString(payload)).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using DataChannelMessage? received = await sink.ReadAsync(deadline.Token).ConfigureAwait(false);
            Assert.That(received!.Payload.Span.ToArray(), Is.EqualTo(payload));
            Assert.That(m_environment.ClientBackend.Datagrams.TryDequeue(out ByteString native), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(native.Span[0], Is.Zero);
                Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(native.Span[1..]), Is.EqualTo(source.ChannelId));
                Assert.That(native.Span[^payload.Length..].ToArray(), Is.EqualTo(payload));
                Assert.That(native.Length, Is.EqualTo(1 + 4 + 13 + payload.Length));
                Assert.That(pair.Client.MaxDatagramSize, Is.EqualTo(pair.Client.NativeConnection.MaxDatagramSize - 1));
            });
            Assert.That(await source.SendAsync(new ByteString(new byte[payload.Length + 1])).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.TooLarge));
            Assert.That(m_environment.ClientBackend.Datagrams, Is.Empty);
        }

        /// <summary>
        /// Binding never creates substitute streams or accepts management, wrong-opener or reused resources.
        /// </summary>
        [Test]
        public async Task BindingRejectsManagementForeignAndReuse()
        {
            await using WebTransportPair pair = await m_environment!.ConnectPairAsync().ConfigureAwait(false);
            Assert.That(await pair.Client.BindStreamAsync(pair.Client.ManagementStreamId, 101, true)
                .ConfigureAwait(false), Is.False);
            Assert.That(await pair.Server.BindStreamAsync(400, 103, false).ConfigureAwait(false), Is.False);
            ulong stream = await pair.Client.OpenStreamAsync().ConfigureAwait(false);
            Assert.That(await pair.Client.BindStreamAsync(stream, 101, false).ConfigureAwait(false), Is.False);
            Assert.That(await pair.Client.BindStreamAsync(stream, 101, true).ConfigureAwait(false), Is.True);
            Assert.That(await pair.Client.BindStreamAsync(stream, 103, true).ConfigureAwait(false), Is.False);
            await pair.Client.CloseFlowAsync(stream, DataChannelResult.Closed).ConfigureAwait(false);
            Assert.That(await pair.Client.BindStreamAsync(stream, 105, true).ConfigureAwait(false), Is.False);
            Assert.That(pair.Client.Context.IsReady, Is.True);
        }

        /// <summary>
        /// Waits for bounded asynchronous UDP/socket cleanup and checks every actual native counter.
        /// </summary>
        private async Task AssertNativeReleasedAsync()
        {
            NativeQuicCounts counts = default;
            for (int attempt = 0; attempt < 100; attempt++)
            {
                counts = await m_backend!.GetCountsAsync().ConfigureAwait(false);
                if (counts == default)
                {
                    break;
                }
                await Task.Delay(20).ConfigureAwait(false);
            }
            NativeQuicResourceAssertions.AreReleased(counts);
        }

        private INativeQuicBackend? m_backend;
        private NativeHttp3Library? m_library;
        private WebTransportTestEnvironment? m_environment;
    }
}
