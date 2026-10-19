// Copyright (c) OPC Foundation. Licensed under the OPC Foundation MIT License 1.00.

using System;
using System.Buffers.Binary;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.DataChannels;
using Opc.Ua.Native.QuicGo;

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
