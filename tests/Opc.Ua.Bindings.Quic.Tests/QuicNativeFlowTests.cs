// Copyright (c) OPC Foundation. Licensed under the OPC Foundation MIT License 1.00.

using System;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.DataChannels;
using Opc.Ua.Native.QuicGo;

namespace Opc.Ua.Bindings.Quic.Tests
{
    /// <summary>
    /// Real production-native flow integration, not a loopback carrier or TLS/stream simulation.
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    [Category("Quic")]
    [Category("DataChannels")]
    public sealed partial class QuicNativeFlowTests
    {
        /// <summary>
        /// Loads the reviewed native asset once per process.
        /// </summary>
        [OneTimeSetUp]
        public async Task LoadBackendAsync()
        {
            m_backend = await NativeFlowTestEnvironment.LoadBackendAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// Starts each test from zero actual native ownership with fresh manager identities.
        /// </summary>
        [SetUp]
        public async Task SetUpAsync()
        {
            await NativeFlowTestEnvironment.AssertNoNativeOwnershipAsync(m_backend!).ConfigureAwait(false);
            m_environment = await NativeFlowTestEnvironment.CreateAsync(m_backend!).ConfigureAwait(false);
        }

        /// <summary>
        /// Requires all native jobs/handles/sockets and common application reservations to be released.
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
            await NativeFlowTestEnvironment.AssertNoNativeOwnershipAsync(m_backend!).ConfigureAwait(false);
        }

        /// <summary>
        /// Proves actual mTLS, fresh HELLO, native-only readiness and payload without Session/UASC setup.
        /// </summary>
        [Test]
        public async Task NativeOnlyContextsAuthenticateAndExchangeOrderedPayload()
        {
            await using NativeFlowPair pair = await m_environment!.ConnectPairAsync().ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(pair.Client.Context.IsReady, Is.True);
                Assert.That(pair.Server.Context.IsReady, Is.True);
                Assert.That(pair.Client.Context.Channels, Is.Empty);
                Assert.That(pair.Server.Context.Channels, Is.Empty);
                Assert.That(pair.Client.NativeConnection.Peer.IsClient, Is.True);
                Assert.That(pair.Server.NativeConnection.Peer.IsClient, Is.False);
                Assert.That(pair.Client.NativeConnection.Peer.TlsVersion, Is.EqualTo(0x304u));
                Assert.That(pair.Server.NativeConnection.Peer.TlsVersion, Is.EqualTo(0x304u));
                Assert.That(pair.Client.NativeConnection.Peer.HandshakeConfirmed, Is.True);
                Assert.That(pair.Server.NativeConnection.Peer.HandshakeConfirmed, Is.True);
                Assert.That(pair.Client.NativeConnection.Peer.MutualTlsAuthenticated, Is.True);
                Assert.That(pair.Server.NativeConnection.Peer.MutualTlsAuthenticated, Is.True);
                Assert.That(pair.Client.NativeConnection.Peer.LocalCertificateUsed, Is.True);
                Assert.That(pair.Server.NativeConnection.Peer.LocalCertificateUsed, Is.True);
                Assert.That(pair.Client.NativeConnection.Peer.Used0Rtt, Is.False);
                Assert.That(pair.Client.NativeConnection.Peer.Resumed, Is.False);
                Assert.That(pair.Client.NativeConnection.VerifiedPeerCertificateChain.Count,
                    Is.EqualTo(pair.Client.NativeConnection.Peer.VerifiedCertificateCount));
                Assert.That(
                    SHA256.HashData(pair.Client.NativeConnection.VerifiedPeerCertificateChain[0].Span),
                    Is.EqualTo(pair.Client.NativeConnection.Peer.CertificateSha256.Span.ToArray()));
                Assert.That(pair.Client.NativeConnection.Peer.ApplicationProtocol, Is.EqualTo(DataChannelProfiles.QuicAlpn));
                Assert.That(pair.Client.Identity.LocalApplicationUri, Is.EqualTo(NativeFlowTestEnvironment.ClientUri));
                Assert.That(pair.Client.Identity.PeerApplicationUri, Is.EqualTo(NativeFlowTestEnvironment.ServerUri));
                Assert.That(pair.Server.Identity.PeerKeyThumbprint, Is.EqualTo(pair.Client.Identity.LocalKeyThumbprint));
                Assert.That(pair.Client.Identity.PeerKeyThumbprint, Is.EqualTo(pair.Server.Identity.LocalKeyThumbprint));
                Assert.That(pair.Client.ManagementStreamId, Is.Zero);
                Assert.That(pair.Server.ManagementStreamId, Is.Zero);
                Assert.That(pair.Client.Context.Identity.ContextId, Is.EqualTo(pair.Server.Context.Identity.ContextId));
                Assert.That(pair.Client.Context.Identity.LocalChallenge, Is.EqualTo(pair.Server.Context.Identity.PeerChallenge));
                Assert.That(pair.Client.Context.Identity.LocalChallenge, Is.Not.EqualTo(pair.Client.Context.Identity.PeerChallenge));
            });
            (DataChannel opener, DataChannel acceptor) = await pair.OpenAsync().ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(opener.State, Is.EqualTo(DataChannelState.Open));
                Assert.That(acceptor.State, Is.EqualTo(DataChannelState.Open));
                Assert.That(opener.GetDiagnostics().MessagesSent, Is.Zero);
                Assert.That(acceptor.GetDiagnostics().MessagesReceived, Is.Zero);
            });
            byte[] payload = [0, 1, 2, 255, 127, 0];
            Assert.That(await opener.SendAsync(new ByteString(payload)).ConfigureAwait(false), Is.EqualTo(DataChannelResult.Good));
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using DataChannelMessage? message = await acceptor.ReadAsync(deadline.Token).ConfigureAwait(false);
            Assert.That(message, Is.Not.Null);
            Assert.That(message!.Payload.Span.ToArray(), Is.EqualTo(payload));
        }

        /// <summary>
        /// Proves both native transport roles can open independent real bidirectional flow streams.
        /// </summary>
        [Test]
        public async Task BothRolesOpenIndependentBidirectionalFlows()
        {
            await using NativeFlowPair pair = await m_environment!.ConnectPairAsync().ConfigureAwait(false);
            (DataChannel clientFlow, DataChannel serverSink) = await pair.OpenAsync().ConfigureAwait(false);
            (DataChannel serverFlow, DataChannel clientSink) = await pair.OpenAsync(false).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(clientFlow.ChannelId % 2, Is.EqualTo(1));
                Assert.That(serverFlow.ChannelId % 2, Is.Zero);
                Assert.That(clientFlow.IsLocalOpener, Is.True);
                Assert.That(clientSink.IsLocalOpener, Is.False);
                Assert.That(serverFlow.IsLocalOpener, Is.True);
                Assert.That(serverSink.IsLocalOpener, Is.False);
                Assert.That(pair.Client.NativeConnection.TryGetStream(4, out _), Is.True);
                Assert.That(pair.Server.NativeConnection.TryGetStream(1, out _), Is.True);
            });
            Assert.That(await clientFlow.SendAsync(Bytes(11)).ConfigureAwait(false), Is.EqualTo(DataChannelResult.Good));
            Assert.That(await serverFlow.SendAsync(Bytes(22)).ConfigureAwait(false), Is.EqualTo(DataChannelResult.Good));
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using DataChannelMessage? first = await serverSink.ReadAsync(deadline.Token).ConfigureAwait(false);
            using DataChannelMessage? second = await clientSink.ReadAsync(deadline.Token).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(first!.Payload.Span.ToArray(), Is.EqualTo(new byte[] { 11 }));
                Assert.That(second!.Payload.Span.ToArray(), Is.EqualTo(new byte[] { 22 }));
            });
        }

        /// <summary>
        /// Refuses management, nonexistent/unannounced, wrong-role and reused stream bindings.
        /// </summary>
        [Test]
        public async Task BindingRejectsManagementUnannouncedWrongOwnerAndReuse()
        {
            await using NativeFlowPair pair = await m_environment!.ConnectPairAsync().ConfigureAwait(false);
            Assert.That(await pair.Client.BindStreamAsync(0, 101, true).ConfigureAwait(false), Is.False);
            Assert.That(await pair.Server.BindStreamAsync(400, 103, false).ConfigureAwait(false), Is.False);
            ulong id = await pair.Client.OpenStreamAsync().ConfigureAwait(false);
            Assert.That(await pair.Client.BindStreamAsync(id, 101, false).ConfigureAwait(false), Is.False);
            Assert.That(await pair.Server.BindStreamAsync(id, 101, false).ConfigureAwait(false), Is.False);
            Assert.That(await pair.Client.BindStreamAsync(id, 101, true).ConfigureAwait(false), Is.True);
            Assert.That(await pair.Client.BindStreamAsync(id, 103, true).ConfigureAwait(false), Is.False);
            await pair.Client.CloseFlowAsync(id, DataChannelResult.Closed).ConfigureAwait(false);
            Assert.That(await pair.Client.BindStreamAsync(id, 105, true).ConfigureAwait(false), Is.False);
            (DataChannel surviving, DataChannel sink) = await pair.OpenAsync().ConfigureAwait(false);
            Assert.That(await surviving.SendAsync(Bytes(7)).ConfigureAwait(false), Is.EqualTo(DataChannelResult.Good));
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using DataChannelMessage? received = await sink.ReadAsync(deadline.Token).ConfigureAwait(false);
            Assert.That(received!.Payload.Span.ToArray(), Is.EqualTo(new byte[] { 7 }));
        }

        /// <summary>
        /// Uses real RFC9221 datagrams with negotiated final capacity and no ordered-stream emulation.
        /// </summary>
        [Test]
        public async Task DatagramFlowUsesRealNativeDatagramsAndCapacity()
        {
            await using NativeFlowPair pair = await m_environment!.ConnectPairAsync().ConfigureAwait(false);
            DataChannelParametersDataType parameters = NativeFlowTestEnvironment.Parameters(DataChannelDeliveryMode.Datagram);
            parameters.MaxMessageSize = 4096;
            (DataChannel source, DataChannel sink) = await pair.OpenAsync(parameters: parameters).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(pair.Client.MaxDatagramSize, Is.GreaterThan(17));
                Assert.That(source.Parameters.Delivery, Is.EqualTo(DataChannelDeliveryMode.Datagram));
                Assert.That(source.Parameters.MaxMessageSize, Is.LessThanOrEqualTo(pair.Client.MaxDatagramSize - 17));
            });
            byte[] payload = new byte[source.Parameters.MaxMessageSize];
            payload.AsSpan().Fill(0x5a);
            Assert.That(await source.SendAsync(new ByteString(payload)).ConfigureAwait(false), Is.EqualTo(DataChannelResult.Good));
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using DataChannelMessage? received = await sink.ReadAsync(deadline.Token).ConfigureAwait(false);
            Assert.That(received!.Payload.Span.ToArray(), Is.EqualTo(payload));
            Assert.That(
                await source.SendAsync(new ByteString(new byte[payload.Length + 1])).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.TooLarge));
        }

        /// <summary>
        /// Completes a revision transaction on the actual bound stream and delivers the next-revision payload.
        /// </summary>
        [Test]
        public async Task OrderedRevisionBarrierUsesBoundStream()
        {
            await using NativeFlowPair pair = await m_environment!.ConnectPairAsync().ConfigureAwait(false);
            (DataChannel source, DataChannel sink) = await pair.OpenAsync().ConfigureAwait(false);
            DataChannelParametersDataType revised = source.Parameters;
            revised.MaxMessageSize = 512;
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            Assert.That(await source.UpdateAsync(revised, 21, timeout.Token).ConfigureAwait(false), Is.EqualTo(DataChannelResult.Good));
            Assert.That(await source.SendAsync(Bytes(3, 2, 1), cancellationToken: timeout.Token).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            using DataChannelMessage? message = await sink.ReadAsync(timeout.Token).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(source.Revision, Is.EqualTo(2));
                Assert.That(sink.Revision, Is.EqualTo(2));
                Assert.That(message!.Revision, Is.EqualTo(2));
                Assert.That(message.Payload.Span.ToArray(), Is.EqualTo(new byte[] { 3, 2, 1 }));
                Assert.That(sink.Parameters.MaxMessageSize, Is.EqualTo(512));
            });
        }

        /// <summary>
        /// A reset closes only its flow; END produces directional EOF while reverse traffic remains usable.
        /// </summary>
        [Test]
        public async Task ResetIsolatesFlowAndHalfCloseKeepsReverseDirection()
        {
            await using NativeFlowPair pair = await m_environment!.ConnectPairAsync().ConfigureAwait(false);
            (DataChannel reset, DataChannel resetPeer) = await pair.OpenAsync().ConfigureAwait(false);
            (DataChannel source, DataChannel sink) = await pair.OpenAsync().ConfigureAwait(false);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            Task<DataChannelMessage?> pending = resetPeer.ReadAsync(timeout.Token).AsTask();
            await reset.ResetAsync(DataChannelResult.InvalidState, timeout.Token).ConfigureAwait(false);
            DataChannelException? error = await Assert.ThrowsAsync<DataChannelException>(
                async () => await pending.ConfigureAwait(false)).ConfigureAwait(false);
            Assert.That(error!.Result, Is.EqualTo(DataChannelResult.InvalidState));
            Assert.That(await source.EndAsync(timeout.Token).ConfigureAwait(false), Is.EqualTo(DataChannelResult.Good));
            Assert.That(await sink.ReadAsync(timeout.Token).ConfigureAwait(false), Is.Null);
            Assert.That(await sink.SendAsync(Bytes(9), cancellationToken: timeout.Token).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            using DataChannelMessage? reverse = await source.ReadAsync(timeout.Token).ConfigureAwait(false);
            Assert.That(reverse!.Payload.Span.ToArray(), Is.EqualTo("\t"u8.ToArray()));
            Assert.That(pair.Client.Context.IsReady, Is.True);
            Assert.That(pair.Server.Context.IsReady, Is.True);
        }

        /// <summary>
        /// Closing a listener releases its port but does not revoke independently accepted native peers.
        /// </summary>
        [Test]
        public async Task ListenerDisposalDoesNotCloseAcceptedConnections()
        {
            await using NativeFlowPair pair = await m_environment!.ConnectPairAsync().ConfigureAwait(false);
            await pair.Listener.DisposeAsync().ConfigureAwait(false);
            Assert.That(pair.Listener.IsListening, Is.False);
            (DataChannel source, DataChannel sink) = await pair.OpenAsync().ConfigureAwait(false);
            Assert.That(await source.SendAsync(Bytes(42)).ConfigureAwait(false), Is.EqualTo(DataChannelResult.Good));
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using DataChannelMessage? message = await sink.ReadAsync(timeout.Token).ConfigureAwait(false);
            Assert.That(message!.Payload.Span.ToArray(), Is.EqualTo("*"u8.ToArray()));
        }

        /// <summary>
        /// Cancelled caller input never reaches native transmission or prevents the next valid message.
        /// </summary>
        [Test]
        public async Task CancelledWriteDoesNotReachPeer()
        {
            await using NativeFlowPair pair = await m_environment!.ConnectPairAsync().ConfigureAwait(false);
            (DataChannel source, DataChannel sink) = await pair.OpenAsync().ConfigureAwait(false);
            using var cancelled = new CancellationTokenSource();
            await cancelled.CancelAsync().ConfigureAwait(false);
            await Assert.ThrowsAsync<OperationCanceledException>(async () =>
                await source.SendAsync(Bytes(99), cancellationToken: cancelled.Token).ConfigureAwait(false))
                .ConfigureAwait(false);
            Assert.That(await source.SendAsync(Bytes(1)).ConfigureAwait(false), Is.EqualTo(DataChannelResult.Good));
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using DataChannelMessage? message = await sink.ReadAsync(timeout.Token).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(message!.Payload.Span.ToArray(), Is.EqualTo(new byte[] { 1 }));
                Assert.That(source.GetDiagnostics().MessagesSent, Is.EqualTo(1));
                Assert.That(sink.GetDiagnostics().MessagesReceived, Is.EqualTo(1));
            });
        }

        /// <summary>
        /// Actual connection loss completes pending application reads and clears native/context ownership.
        /// </summary>
        [Test]
        public async Task ConnectionLossUnblocksPendingRead()
        {
            await using NativeFlowPair pair = await m_environment!.ConnectPairAsync().ConfigureAwait(false);
            (DataChannel source, DataChannel sink) = await pair.OpenAsync().ConfigureAwait(false);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            Task<DataChannelMessage?> pending = sink.ReadAsync(timeout.Token).AsTask();
            await pair.Client.NativeConnection.CloseAsync((ulong)DataChannelResult.Closed).ConfigureAwait(false);
            await Assert.ThrowsAsync<DataChannelException>(async () => await pending.ConfigureAwait(false))
                .ConfigureAwait(false);
            Assert.That(await source.SendAsync(Bytes(1)).ConfigureAwait(false), Is.EqualTo(DataChannelResult.Closed));
        }

        /// <summary>
        /// Native and common ownership returns to zero after active flows and idempotent disposal.
        /// </summary>
        [Test]
        public async Task DisposalDrainsAllNativeHandles()
        {
            NativeFlowPair pair = await m_environment!.ConnectPairAsync().ConfigureAwait(false);
            await pair.OpenAsync().ConfigureAwait(false);
            await pair.OpenAsync(false).ConfigureAwait(false);
            await pair.DisposeAsync().ConfigureAwait(false);
            await pair.DisposeAsync().ConfigureAwait(false);
            await NativeFlowTestEnvironment.AssertNoNativeOwnershipAsync(m_backend!).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                DataChannelResourceAssertions.AreReleased(m_environment.ClientBudget.Usage);
                DataChannelResourceAssertions.AreReleased(m_environment.ServerBudget.Usage);
            });
            await Assert.ThrowsAsync<ObjectDisposedException>(
                async () => await pair.Client.OpenStreamAsync().ConfigureAwait(false)).ConfigureAwait(false);
        }

        /// <summary>
        /// Creates an owned payload for concise byte-exact network assertions.
        /// </summary>
        private static ByteString Bytes(params ReadOnlySpan<byte> values)
        {
            return new ByteString(values.ToArray());
        }

        private INativeQuicBackend? m_backend;
        private NativeFlowTestEnvironment? m_environment;
    }
}
