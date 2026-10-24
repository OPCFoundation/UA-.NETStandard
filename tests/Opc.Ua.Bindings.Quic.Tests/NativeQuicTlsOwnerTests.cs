// Copyright (c) OPC Foundation. Licensed under the OPC Foundation MIT License 1.00.

using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Security;
using System.Runtime.Versioning;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NUnit.Framework;
using Opc.Ua.Bindings.Native;
using Opc.Ua.DataChannels;
using Opc.Ua.Native.QuicGo;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.Bindings.Quic.Tests
{
    /// <summary>
    /// Transport-only TLS ownership tests, not HTTP/3, WebTransport or application authentication.
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    [Category("Quic")]
    public sealed class NativeQuicTlsOwnerTests
    {
        [OneTimeSetUp]
        public async Task LoadBackendAsync()
        {
            m_backend = await NativeFlowTestEnvironment.LoadBackendAsync().ConfigureAwait(false);
        }

        [SetUp]
        public async Task SetUpAsync()
        {
            await NativeFlowTestEnvironment.AssertNoNativeOwnershipAsync(m_backend!).ConfigureAwait(false);
            m_environment = await NativeFlowTestEnvironment.CreateAsync(m_backend!).ConfigureAwait(false);
            m_certificate = m_environment.CreateApplicationCertificate("urn:test:tls-host");
            using RSA rsa = m_certificate.GetRSAPrivateKey()!;
            byte[] exported = rsa.ExportPkcs8PrivateKey();
            try
            {
                m_key = new NativeQuicPrivateKey(exported);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(exported);
            }
            m_serverCredentials = NativeQuicCredentials.ForTlsServer(
                new ByteString(m_certificate.RawData), default, m_key);
            m_clientCredentials = NativeQuicCredentials.ForTlsClient(new ByteString(m_environment.Root.RawData));
        }

        [TearDown]
        public async Task TearDownAsync()
        {
            m_key?.Dispose();
            m_certificate?.Dispose();
            if (m_environment != null)
            {
                await m_environment.DisposeAsync().ConfigureAwait(false);
            }
            await NativeFlowTestEnvironment.AssertNoNativeOwnershipAsync(m_backend!).ConfigureAwait(false);
        }

        [Test]
        public async Task TlsEvidenceHasActualRoleAndNoApplicationIdentity()
        {
            await using NativePeerPair pair = await ConnectPairAsync().ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(m_clientCredentials!.IsTransportOnly, Is.True);
                Assert.That(m_clientCredentials.CertificateDer.IsEmpty, Is.True);
                Assert.That(m_clientCredentials.ApplicationUri, Is.Empty);
                Assert.That(m_serverCredentials!.IsTransportOnly, Is.True);
                Assert.That(m_serverCredentials.ApplicationUri, Is.Empty);
                Assert.That(pair.Client.IsClient, Is.True);
                Assert.That(pair.Server.IsClient, Is.False);
                Assert.That(pair.Client.TlsPeer.IsClient, Is.True);
                Assert.That(pair.Server.TlsPeer.IsClient, Is.False);
                Assert.That(pair.Client.TlsPeer.TlsVersion, Is.EqualTo(0x304u));
                Assert.That(pair.Server.TlsPeer.TlsVersion, Is.EqualTo(0x304u));
                Assert.That(pair.Client.TlsPeer.HandshakeConfirmed, Is.True);
                Assert.That(pair.Server.TlsPeer.HandshakeConfirmed, Is.True);
                Assert.That(pair.Client.TlsPeer.LocalCertificateUsed, Is.False);
                Assert.That(pair.Server.TlsPeer.LocalCertificateUsed, Is.True);
                Assert.That(pair.Client.TlsPeer.ApplicationProtocol, Is.EqualTo(kAlpn));
                Assert.That(pair.Server.TlsPeer.ApplicationProtocol, Is.EqualTo(kAlpn));
                Assert.That(pair.Client.VerifiedPeerCertificateChain, Is.Not.Empty);
                Assert.That(pair.Server.VerifiedPeerCertificateChain, Is.Empty);
                Assert.That(pair.Client.VerifiedPeerCertificateChain[0],
                    Is.EqualTo(new ByteString(m_certificate!.RawData)));
                Assert.That(pair.Client.TlsPeer.VerifiedCertificateCount,
                    Is.EqualTo(pair.Client.VerifiedPeerCertificateChain.Count));
                Assert.That(pair.Server.TlsPeer.VerifiedCertificateCount, Is.Zero);
                Assert.That(pair.Client.TlsPeer.CertificateSha256.Span.ToArray(),
                    Is.EqualTo(SHA256.HashData(m_certificate.RawData)));
                Assert.That(pair.Server.TlsPeer.CertificateSha256.Span.ToArray(), Is.EqualTo(new byte[32]));
                Assert.That(pair.Server.TlsPeer.SpkiSha256.Span.ToArray(), Is.EqualTo(new byte[32]));
                Assert.That(pair.Client.TlsPeer.SpkiSha256,
                    Is.EqualTo(NativeQuicCertificateProvider.GetPublicKeySha256(m_certificate)));
                Assert.Throws<InvalidOperationException>(() => _ = pair.Client.Peer);
                Assert.Throws<InvalidOperationException>(() => _ = pair.Server.Peer);
                Assert.Throws<InvalidOperationException>(() => _ = pair.Client.Credentials);
                Assert.Throws<InvalidOperationException>(() => _ = pair.Server.Credentials);
            });
        }

        [Test]
        public async Task ListenerCopiesCallerKeyAndAcceptedConnectionsOutliveListener()
        {
            await using NativeQuicListener listener = await ListenAsync().ConfigureAwait(false);
            // Successful reuse proves native creation did not dispose the caller's key.
            await using (NativeQuicListener second = await ListenAsync().ConfigureAwait(false))
            {
                Assert.That(second.IsListening, Is.True);
                Assert.That(second.Address, Is.Not.EqualTo(listener.Address));
            }
            m_key!.Dispose();
            await using NativePeerPair first = await ConnectPairAsync(listener: listener).ConfigureAwait(false);
            await using NativePeerPair next = await ConnectPairAsync(listener: listener).ConfigureAwait(false);
            await listener.DisposeAsync().ConfigureAwait(false);
            await listener.Completion.ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(listener.IsListening, Is.False);
                Assert.That(first.Client.IsClosed, Is.False);
                Assert.That(first.Server.IsClosed, Is.False);
                Assert.That(next.Client.IsClosed, Is.False);
                Assert.That(next.Server.IsClosed, Is.False);
            });
            await ExchangeDatagramAsync(first.Client, first.Server, Bytes(0, 1, 255)).ConfigureAwait(false);
            await ExchangeDatagramAsync(next.Server, next.Client, Bytes(8, 0, 9)).ConfigureAwait(false);
        }

        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public async Task TlsStreamsPreserveRoleOwnershipAndExactBytes(bool serverOpens, bool unidirectional)
        {
            await using NativePeerPair pair = await ConnectPairAsync().ConfigureAwait(false);
            NativeQuicConnection opener = serverOpens ? pair.Server : pair.Client;
            NativeQuicConnection acceptor = serverOpens ? pair.Client : pair.Server;
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await using NativeQuicStream local = await opener.OpenStreamAsync(unidirectional, timeout.Token)
                .ConfigureAwait(false);
            ByteString payload = Bytes(0, 19, 255, 42, 0);
            NativeQuicCompletion? sent = await local.SendAsync(payload, flags: 1, cancellationToken: timeout.Token)
                .ConfigureAwait(false);
            await using NativeQuicStream remote = await acceptor.AcceptStreamAsync(unidirectional, timeout.Token)
                .ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(sent!.Status, Is.EqualTo(NativeQuicStatus.Success));
                Assert.That(sent.BytesTransferred, Is.EqualTo(payload.Length));
                Assert.That(local.Connection, Is.SameAs(opener));
                Assert.That(remote.Connection, Is.SameAs(acceptor));
                Assert.That(local.IsLocal, Is.True);
                Assert.That(remote.IsLocal, Is.False);
                Assert.That(local.IsBidirectional, Is.EqualTo(!unidirectional));
                Assert.That(remote.IsBidirectional, Is.EqualTo(!unidirectional));
                Assert.That(local.StreamId, Is.EqualTo((serverOpens ? 1UL : 0UL) + (unidirectional ? 2UL : 0UL)));
                Assert.That(remote.StreamId, Is.EqualTo(local.StreamId));
                Assert.That(opener.TryGetStream(local.StreamId, out NativeQuicStream? owned), Is.True);
                Assert.That(owned, Is.SameAs(local));
                Assert.That(acceptor.TryGetStream(remote.StreamId, out NativeQuicStream? received), Is.True);
                Assert.That(received, Is.SameAs(remote));
            });
            Assert.That(await ReadPayloadAsync(remote, payload.Length, timeout.Token).ConfigureAwait(false),
                Is.EqualTo(payload.Span.ToArray()));
            if (!unidirectional)
            {
                ByteString reply = Bytes(71, 0, 72);
                NativeQuicCompletion? replied = await remote.SendAsync(reply, flags: 1, cancellationToken: timeout.Token)
                    .ConfigureAwait(false);
                Assert.That(replied!.Status, Is.EqualTo(NativeQuicStatus.Success));
                Assert.That(replied.BytesTransferred, Is.EqualTo(reply.Length));
                Assert.That(await ReadPayloadAsync(local, reply.Length, timeout.Token).ConfigureAwait(false),
                    Is.EqualTo(reply.Span.ToArray()));
            }
            await local.DisposeAsync().ConfigureAwait(false);
            await remote.DisposeAsync().ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(local.IsClosed, Is.True);
                Assert.That(remote.IsClosed, Is.True);
                Assert.That(opener.TryGetStream(local.StreamId, out _), Is.False);
                Assert.That(acceptor.TryGetStream(remote.StreamId, out _), Is.False);
                Assert.That(opener.IsClosed, Is.False);
                Assert.That(acceptor.IsClosed, Is.False);
            });
        }

        /// <summary>
        /// Byte adapters preserve actual directions, fragmented payload and FIN without adding framing.
        /// </summary>
        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public async Task NativeByteStreamPreservesDirectionsBytesAndFin(bool serverOpens, bool unidirectional)
        {
            await using NativePeerPair pair = await ConnectPairAsync().ConfigureAwait(false);
            NativeQuicConnection sender = serverOpens ? pair.Server : pair.Client;
            NativeQuicConnection receiver = serverOpens ? pair.Client : pair.Server;
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await using NativeQuicStream local = await sender.OpenStreamAsync(unidirectional, timeout.Token)
                .ConfigureAwait(false);
            Assert.That((await local.SendAsync(Bytes(7, 0, 255, 4, 3), flags: 1,
                cancellationToken: timeout.Token).ConfigureAwait(false))!.Status,
                Is.EqualTo(NativeQuicStatus.Success));
            await using NativeQuicStream remote = await receiver.AcceptStreamAsync(unidirectional, timeout.Token)
                .ConfigureAwait(false);
            await using var stream = new NativeQuicByteStream(remote);
            byte[] actual = new byte[5];
            int offset = 0;
            while (offset < actual.Length)
            {
                int read = await stream.ReadAsync(
                    actual.AsMemory(offset, Math.Min(2, actual.Length - offset)), timeout.Token).ConfigureAwait(false);
                Assert.That(read, Is.InRange(1, 2));
                offset += read;
            }
            Assert.That(await stream.ReadAsync(new byte[1], timeout.Token).ConfigureAwait(false), Is.Zero);
            Assert.That(await stream.ReadAsync(new byte[1], timeout.Token).ConfigureAwait(false), Is.Zero);
            Assert.Multiple(() =>
            {
                Assert.That(actual, Is.EqualTo(new byte[] { 7, 0, 255, 4, 3 }));
                Assert.That(stream.CanRead, Is.True);
                Assert.That(stream.CanWrite, Is.EqualTo(!unidirectional));
                Assert.That(stream.CanSeek, Is.False);
                Assert.Throws<NotSupportedException>(() => stream.ReadByte());
                Assert.Throws<NotSupportedException>(() => stream.Write(actual, 0, 1));
                Assert.Throws<NotSupportedException>(() => stream.Seek(0, SeekOrigin.Begin));
            });
            if (unidirectional)
            {
                await using var openerBytes = new NativeQuicByteStream(local);
                Assert.That(openerBytes.CanRead, Is.False);
                Assert.That(openerBytes.CanWrite, Is.True);
                await Assert.ThrowsAsync<NotSupportedException>(async () =>
                    await openerBytes.ReadAsync(new byte[1], timeout.Token).ConfigureAwait(false))
                    .ConfigureAwait(false);
                await Assert.ThrowsAsync<NotSupportedException>(async () =>
                    await stream.WriteAsync(new byte[] { 1 }, timeout.Token).ConfigureAwait(false))
                    .ConfigureAwait(false);
            }
            else
            {
                await stream.WriteAsync(new byte[] { 8, 0, 9 }, timeout.Token).ConfigureAwait(false);
                Assert.That(await ReadPayloadAsync(local, 3, timeout.Token).ConfigureAwait(false),
                    Is.EqualTo(new byte[] { 8, 0, 9 }));
            }
            await stream.DisposeAsync().ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(stream.CanRead, Is.False);
                Assert.That(stream.CanWrite, Is.False);
                Assert.That(receiver.TryGetStream(remote.StreamId, out _), Is.False);
                Assert.That(sender.IsClosed, Is.False);
                Assert.That(receiver.IsClosed, Is.False);
            });
        }

        /// <summary>
        /// Orderly byte-stream completion releases the handle without resetting outstanding FIN/data.
        /// </summary>
        [TestCase(false, false, 1)]
        [TestCase(true, false, 65537)]
        [TestCase(false, true, 65537)]
        [TestCase(true, true, 1)]
        public async Task NativeByteStreamCompletionRetainsBytesAfterHandleRelease(
            bool serverOpens, bool unidirectional, int length)
        {
            await using NativePeerPair pair = await ConnectPairAsync().ConfigureAwait(false);
            NativeQuicConnection opener = serverOpens ? pair.Server : pair.Client;
            NativeQuicConnection acceptor = serverOpens ? pair.Client : pair.Server;
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await using NativeQuicStream native = await opener.OpenStreamAsync(unidirectional, deadline.Token)
                .ConfigureAwait(false);
            await using var sending = new NativeQuicByteStream(native);
            byte[] expected = new byte[length];
            for (int index = 0; index < length; index++)
            {
                expected[index] = unchecked((byte)(index * 17));
            }
            await sending.CompleteWritesAsync(new ByteString(expected), deadline.Token).ConfigureAwait(false);
            await using NativeQuicStream accepted = await acceptor.AcceptStreamAsync(unidirectional, deadline.Token)
                .ConfigureAwait(false);
            await sending.DisposeAsync().ConfigureAwait(false);
            Assert.That(opener.TryGetStream(native.StreamId, out _), Is.False);
            await using var receiving = new NativeQuicByteStream(accepted);
            byte[] actual = new byte[length];
            await receiving.ReadExactlyAsync(actual, deadline.Token).ConfigureAwait(false);
            Assert.That(actual, Is.EqualTo(expected));
            Assert.That(await receiving.ReadAsync(new byte[1], deadline.Token).ConfigureAwait(false), Is.Zero);
            Assert.That(opener.IsClosed, Is.False);
            Assert.That(acceptor.IsClosed, Is.False);
            await ExchangeDatagramAsync(opener, acceptor, Bytes(0, 255, 42)).ConfigureAwait(false);
        }

        /// <summary>
        /// FIN and handle release retain native bytes even when the managed peer has no RESET_STREAM_AT.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        [SupportedOSPlatform("windows")]
        [SupportedOSPlatform("linux")]
        [SupportedOSPlatform("macos")]
        public async Task NativeByteStreamCompletionDoesNotRequirePartialResetExtension(bool nativeOpens)
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await using NativeQuicListener listener = await ListenAsync().ConfigureAwait(false);
            Task<NativeQuicConnection> accepting = listener.AcceptAsync(deadline.Token).AsTask();
            var options = new System.Net.Quic.QuicClientConnectionOptions
            {
                RemoteEndPoint = System.Net.IPEndPoint.Parse(listener.Address),
                DefaultStreamErrorCode = 0,
                DefaultCloseErrorCode = 0,
                MaxInboundBidirectionalStreams = 8,
                ClientAuthenticationOptions = new SslClientAuthenticationOptions
                {
                    TargetHost = "localhost",
                    ApplicationProtocols = [new SslApplicationProtocol(kAlpn)],
                    RemoteCertificateValidationCallback = (_, certificate, _, _) =>
                        certificate != null && certificate.GetRawCertData().AsSpan().SequenceEqual(m_certificate!.RawData)
                }
            };
            await using System.Net.Quic.QuicConnection managed =
                await System.Net.Quic.QuicConnection.ConnectAsync(options, deadline.Token).ConfigureAwait(false);
            await using NativeQuicConnection native = await accepting.ConfigureAwait(false);
            Assert.That(native.TlsPeer.PeerResetStreamAt09, Is.False);
            System.Net.Quic.QuicStream peer;
            NativeQuicStream local;
            if (nativeOpens)
            {
                local = await native.OpenStreamAsync(cancellationToken: deadline.Token).ConfigureAwait(false);
                await local.SendAsync(Bytes(42), cancellationToken: deadline.Token).ConfigureAwait(false);
                peer = await managed.AcceptInboundStreamAsync(deadline.Token).ConfigureAwait(false);
                byte[] preface = new byte[1];
                await peer.ReadExactlyAsync(preface, deadline.Token).ConfigureAwait(false);
                Assert.That(preface, Is.EqualTo(new byte[] { 42 }));
            }
            else
            {
                peer = await managed.OpenOutboundStreamAsync(
                    System.Net.Quic.QuicStreamType.Bidirectional, deadline.Token).ConfigureAwait(false);
                await peer.WriteAsync(new byte[] { 42 }, deadline.Token).ConfigureAwait(false);
                local = await native.AcceptStreamAsync(cancellationToken: deadline.Token).ConfigureAwait(false);
                Assert.That(await ReadPayloadAsync(local, 1, deadline.Token).ConfigureAwait(false),
                    Is.EqualTo(new byte[] { 42 }));
            }
            await using (peer.ConfigureAwait(false))
            await using (local.ConfigureAwait(false))
            {
                await using var sending = new NativeQuicByteStream(local);
                ByteString expected = ByteString.FromHexString("0001ff030004ff");
                await sending.CompleteWritesAsync(expected, deadline.Token).ConfigureAwait(false);
                await sending.DisposeAsync().ConfigureAwait(false);
                byte[] actual = new byte[expected.Length];
                await peer.ReadExactlyAsync(actual, deadline.Token).ConfigureAwait(false);
                Assert.That(actual, Is.EqualTo(expected.Span.ToArray()));
                Assert.That(await peer.ReadAsync(new byte[1], deadline.Token).ConfigureAwait(false), Is.Zero);
                Assert.That(native.TryGetStream(local.StreamId, out _), Is.False);
                Assert.That(native.IsClosed, Is.False);
            }
        }

        /// <summary>
        /// Native operation limits do not truncate a larger byte write or interleave its segments.
        /// </summary>
        [TestCase(65536)]
        [TestCase(65537)]
        public async Task NativeByteStreamSegmentsWritesWithoutChangingBytes(int length)
        {
            await using NativePeerPair pair = await ConnectPairAsync().ConfigureAwait(false);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await using NativeQuicStream local = await pair.Client.OpenStreamAsync(
                cancellationToken: timeout.Token).ConfigureAwait(false);
            await using var sender = new NativeQuicByteStream(local);
            byte[] expected = new byte[length];
            for (int i = 0; i < expected.Length; i++)
            {
                expected[i] = unchecked((byte)(i * 17));
            }
            Task sending = sender.WriteAsync(expected, timeout.Token).AsTask();
            await using NativeQuicStream remote = await pair.Server.AcceptStreamAsync(
                cancellationToken: timeout.Token).ConfigureAwait(false);
            await using var receiver = new NativeQuicByteStream(remote);
            byte[] actual = new byte[length];
            await receiver.ReadExactlyAsync(actual, timeout.Token).ConfigureAwait(false);
            await sending.ConfigureAwait(false);
            Assert.That(actual, Is.EqualTo(expected));
            Assert.That(await receiver.ReadAsync(Memory<byte>.Empty, timeout.Token).ConfigureAwait(false), Is.Zero);
            await sender.WriteAsync(ReadOnlyMemory<byte>.Empty, timeout.Token).ConfigureAwait(false);
            await sender.FlushAsync(timeout.Token).ConfigureAwait(false);
            Assert.That(pair.Client.IsClosed, Is.False);
        }

        /// <summary>
        /// Received bytes precede the exact peer reset; reset is never presented as graceful EOF.
        /// </summary>
        [Test]
        public async Task NativeByteStreamDistinguishesPeerResetFromFin()
        {
            await using NativePeerPair pair = await ConnectPairAsync().ConfigureAwait(false);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await using NativeQuicStream local = await pair.Client.OpenStreamAsync(
                cancellationToken: timeout.Token).ConfigureAwait(false);
            Assert.That((await local.SendAsync(Bytes(1, 2, 3), cancellationToken: timeout.Token)
                .ConfigureAwait(false))!.Status, Is.EqualTo(NativeQuicStatus.Success));
            await using NativeQuicStream remote = await pair.Server.AcceptStreamAsync(
                cancellationToken: timeout.Token).ConfigureAwait(false);
            await using var receiver = new NativeQuicByteStream(remote);
            byte[] first = new byte[1];
            Assert.That(await receiver.ReadAsync(first, timeout.Token).ConfigureAwait(false), Is.EqualTo(1));
            Assert.That(first[0], Is.EqualTo(1));
            byte[] tail = new byte[2];
            await receiver.ReadExactlyAsync(tail, timeout.Token).ConfigureAwait(false);
            Assert.That(tail, Is.EqualTo(new byte[] { 2, 3 }));
            Assert.That((await local.ResetSendAsync(77).ConfigureAwait(false)).Status,
                Is.EqualTo(NativeQuicStatus.Success));
            NativeQuicTransportException? reset = await Assert.ThrowsAsync<NativeQuicTransportException>(async () =>
                await receiver.ReadAsync(first, timeout.Token).ConfigureAwait(false)).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(reset!.Completion!.ReadOutcome, Is.EqualTo(2u));
                Assert.That(reset.Completion.ErrorCode, Is.EqualTo(77u));
                Assert.That(reset.Completion.Remote, Is.True);
                Assert.That(pair.Client.IsClosed, Is.False);
                Assert.That(pair.Server.IsClosed, Is.False);
            });
        }

        /// <summary>
        /// A cancelled read is recoverable, while disposal drains only its native stream and pending I/O.
        /// </summary>
        [Test]
        public async Task NativeByteStreamCancellationAndDisposalPreserveConnection()
        {
            await using NativePeerPair pair = await ConnectPairAsync().ConfigureAwait(false);
            await using NativeQuicStream local = await pair.Client.OpenStreamAsync().ConfigureAwait(false);
            await using var stream = new NativeQuicByteStream(local);
            using var cancelled = new CancellationTokenSource();
            Task<int> reading = stream.ReadAsync(new byte[1], cancelled.Token).AsTask();
            Assert.That(reading.IsCompleted, Is.False);
            await cancelled.CancelAsync().ConfigureAwait(false);
            await Assert.CatchAsync<OperationCanceledException>(async () =>
                await reading.ConfigureAwait(false)).ConfigureAwait(false);
            await stream.WriteAsync(new byte[] { 4 }).ConfigureAwait(false);
            await using NativeQuicStream remote = await pair.Server.AcceptStreamAsync().ConfigureAwait(false);
            Assert.That(await ReadPayloadAsync(remote, 1, CancellationToken.None).ConfigureAwait(false),
                Is.EqualTo(new byte[] { 4 }));
            Assert.That((await remote.SendAsync(Bytes(9)).ConfigureAwait(false))!.Status,
                Is.EqualTo(NativeQuicStatus.Success));
            Assert.That(await stream.ReadAsync(new byte[1]).ConfigureAwait(false), Is.EqualTo(1));
            Task<int> pending = stream.ReadAsync(new byte[1]).AsTask();
            await stream.DisposeAsync().ConfigureAwait(false);
            await Assert.CatchAsync<OperationCanceledException>(async () =>
                await pending.ConfigureAwait(false)).ConfigureAwait(false);
            await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
                await stream.ReadAsync(new byte[1]).ConfigureAwait(false)).ConfigureAwait(false);
            Assert.That(pair.Client.TryGetStream(local.StreamId, out _), Is.False);
            await ExchangeDatagramAsync(pair.Server, pair.Client, Bytes(8, 0, 6)).ConfigureAwait(false);
        }

        /// <summary>
        /// Explicit native selection carries the first UACP bytes unchanged on a uniquely claimed baseline.
        /// </summary>
        [Test]
        public async Task NativeUascBaselinePreservesFirstBytesEndpointOwnershipAndListenerIndependence()
        {
            var serverTransport = new NativeQuicUascTransport(
                m_backend!, m_environment!.ServerBudget, m_environment.BindingOptions);
            var clientTransport = new NativeQuicUascTransport(
                m_backend!, m_environment.ClientBudget, m_environment.BindingOptions);
            await using NativeQuicUascListener listener = await serverTransport.ListenAsync(
                new Uri("opc.quic://127.0.0.1:0/uasc?selection=exact"), m_serverCredentials!).ConfigureAwait(false);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            Task<NativeQuicUascConnection> accepting = listener.AcceptAsync(timeout.Token).AsTask();
            await using NativeQuicUascConnection client = await clientTransport.ConnectAsync(
                listener.EndpointUrl, m_clientCredentials!, cancellationToken: timeout.Token).ConfigureAwait(false);
            var wrong = new UriBuilder(listener.EndpointUrl) { Query = "selection=changed" };
            ServiceResultException? mismatch = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await client.ClaimStreamAsync(wrong.Uri, timeout.Token).ConfigureAwait(false)).ConfigureAwait(false);
            Assert.That(mismatch!.StatusCode, Is.EqualTo(StatusCodes.BadTcpEndpointUrlInvalid));
            await using Stream sender = await client.ClaimStreamAsync(listener.EndpointUrl, timeout.Token)
                .ConfigureAwait(false);
            byte[] firstBytes = [0x48, 0x45, 0x4c, 0x46, 8, 0, 0, 0];
            await sender.WriteAsync(firstBytes, timeout.Token).ConfigureAwait(false);
            await using NativeQuicUascConnection server = await accepting.ConfigureAwait(false);
            await using Stream receiver = await server.ClaimStreamAsync(listener.EndpointUrl, timeout.Token)
                .ConfigureAwait(false);
            await listener.DisposeAsync().ConfigureAwait(false);
            byte[] actual = new byte[firstBytes.Length];
            await receiver.ReadExactlyAsync(actual, timeout.Token).ConfigureAwait(false);
            Assert.That(actual, Is.EqualTo(firstBytes), "No native management envelope precedes UACP.");
            await receiver.WriteAsync(new byte[] { 0x41, 0x43, 0x4b, 0x46 }, timeout.Token).ConfigureAwait(false);
            byte[] response = new byte[4];
            await sender.ReadExactlyAsync(response, timeout.Token).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(response, Is.EqualTo(new byte[] { 0x41, 0x43, 0x4b, 0x46 }));
                Assert.That(client.TransportProfileUri, Is.EqualTo(DataChannelProfiles.UascQuic));
                Assert.That(server.TransportProfileUri, Is.EqualTo(DataChannelProfiles.UascQuic));
                Assert.That(server.EndpointUrl, Is.EqualTo(client.EndpointUrl));
                Assert.That(client.EndpointUrl.PathAndQuery, Is.EqualTo("/uasc?selection=exact"));
                Assert.That(client.EndpointUrl.Port, Is.GreaterThan(0));
                Assert.That(m_environment.ClientBudget.Usage.BufferedBytes, Is.GreaterThan(0));
                Assert.That(m_environment.ServerBudget.Usage.BufferedBytes, Is.GreaterThan(0));
                Assert.Throws<InvalidOperationException>(() => server.Create(m_environment.Telemetry));
            });
            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await client.ClaimStreamAsync(listener.EndpointUrl, timeout.Token).ConfigureAwait(false))
                .ConfigureAwait(false);
            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await server.ClaimStreamAsync(listener.EndpointUrl, timeout.Token).ConfigureAwait(false))
                .ConfigureAwait(false);
            Task firstClose = client.DisposeAsync().AsTask();
            Task secondClose = client.DisposeAsync().AsTask();
            await Task.WhenAll(firstClose, secondClose).ConfigureAwait(false);
            await server.DisposeAsync().ConfigureAwait(false);
            DataChannelResourceAssertions.AreReleased(m_environment.ClientBudget.Usage);
            DataChannelResourceAssertions.AreReleased(m_environment.ServerBudget.Usage);
            await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
                await client.ClaimStreamAsync(listener.EndpointUrl, timeout.Token).ConfigureAwait(false))
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Unannounced stream zero times out without retaining the accepted connection's reservation.
        /// </summary>
        [Test]
        public async Task NativeUascBaselineFirstStreamDeadlineReleasesCapacity()
        {
            QuicDataChannelOptions options = m_environment!.BindingOptions with
            {
                EstablishmentTimeout = TimeSpan.FromMilliseconds(100)
            };
            var transport = new NativeQuicUascTransport(m_backend!, m_environment.ServerBudget, options);
            await using NativeQuicUascListener listener = await transport.ListenAsync(
                new Uri("opc.quic://127.0.0.1:0/uasc"), m_serverCredentials!).ConfigureAwait(false);
            Task<NativeQuicUascConnection> accepting = listener.AcceptAsync().AsTask();
            await using NativeQuicConnection idle = await NativeQuicConnection.ConnectTlsAsync(
                m_backend!, m_clientCredentials!, $"127.0.0.1:{listener.EndpointUrl.Port}",
                DataChannelProfiles.UascQuicAlpn, "localhost", options.Native).ConfigureAwait(false);
            NativeQuicTransportException? rejected = await Assert.ThrowsAsync<NativeQuicTransportException>(
                async () => await accepting.ConfigureAwait(false)).ConfigureAwait(false);
            Assert.That(rejected!.Completion!.Status,
                Is.AnyOf(NativeQuicStatus.Cancelled, NativeQuicStatus.Timeout));
            DataChannelResourceAssertions.AreReleased(m_environment.ServerBudget.Usage);
        }

        /// <summary>
        /// Scheme, embedded credentials, fragments and absent remote ports cannot allocate a native baseline.
        /// </summary>
        [TestCase("opc.tcp://127.0.0.1:4840/uasc")]
        [TestCase("opc.quic://user:password@127.0.0.1:4840/uasc")]
        [TestCase("opc.quic://127.0.0.1:4840/uasc#fragment")]
        [TestCase("opc.quic://127.0.0.1:0/uasc")]
        public async Task NativeUascBaselineRejectsEndpointSubstitutionBeforeAllocation(string value)
        {
            var backend = new Mock<INativeQuicBackend>(MockBehavior.Strict);
            var transport = new NativeQuicUascTransport(
                backend.Object, m_environment!.ClientBudget, m_environment.BindingOptions);
            await Assert.ThrowsAsync<ArgumentException>(async () =>
                await transport.ConnectAsync(new Uri(value), m_clientCredentials!).ConfigureAwait(false))
                .ConfigureAwait(false);
            backend.VerifyNoOtherCalls();
            DataChannelResourceAssertions.AreReleased(m_environment.ClientBudget.Usage);
        }

        /// <summary>
        /// Actual HEL, OPN and Read Services compose with authenticated direct delivery and independent UASC keys.
        /// </summary>
        [TestCase(false, DataChannelDeliveryMode.Ordered, DataChannelDirection.Duplex, false)]
        [TestCase(true, DataChannelDeliveryMode.Ordered, DataChannelDirection.Duplex, false)]
        [TestCase(true, DataChannelDeliveryMode.Ordered, DataChannelDirection.Duplex, true)]
        [TestCase(true, DataChannelDeliveryMode.Ordered, DataChannelDirection.OpenerToAcceptor, false)]
        [TestCase(true, DataChannelDeliveryMode.Ordered, DataChannelDirection.OpenerToAcceptor, true)]
        [TestCase(true, DataChannelDeliveryMode.Ordered, DataChannelDirection.AcceptorToOpener, false)]
        [TestCase(true, DataChannelDeliveryMode.Ordered, DataChannelDirection.AcceptorToOpener, true)]
        [TestCase(true, DataChannelDeliveryMode.Datagram, DataChannelDirection.Duplex, false)]
        [TestCase(true, DataChannelDeliveryMode.Datagram, DataChannelDirection.Duplex, true)]
        [TestCase(true, DataChannelDeliveryMode.Datagram, DataChannelDirection.OpenerToAcceptor, false)]
        [TestCase(true, DataChannelDeliveryMode.Datagram, DataChannelDirection.OpenerToAcceptor, true)]
        [TestCase(true, DataChannelDeliveryMode.Datagram, DataChannelDirection.AcceptorToOpener, false)]
        [TestCase(true, DataChannelDeliveryMode.Datagram, DataChannelDirection.AcceptorToOpener, true)]
        [TestCase(false, DataChannelDeliveryMode.Ordered, DataChannelDirection.Duplex, false, true)]
        [TestCase(true, DataChannelDeliveryMode.Ordered, DataChannelDirection.Duplex, false, true)]
        [TestCase(true, DataChannelDeliveryMode.Ordered, DataChannelDirection.Duplex, true, true)]
        [TestCase(true, DataChannelDeliveryMode.Ordered, DataChannelDirection.Duplex, false, false,
            DataChannelProfiles.UadpOpaqueApplicationPayload, TestName = "NativeUascStreamingOrderedDuplexClient")]
        [TestCase(true, DataChannelDeliveryMode.Ordered, DataChannelDirection.Duplex, true, false,
            DataChannelProfiles.UadpOpaqueApplicationPayload, TestName = "NativeUascStreamingOrderedDuplexServer")]
        [TestCase(true, DataChannelDeliveryMode.Ordered, DataChannelDirection.OpenerToAcceptor, false, false,
            DataChannelProfiles.UadpOpaqueApplicationPayload, TestName = "NativeUascStreamingOrderedForwardClient")]
        [TestCase(true, DataChannelDeliveryMode.Ordered, DataChannelDirection.OpenerToAcceptor, true, false,
            DataChannelProfiles.UadpOpaqueApplicationPayload, TestName = "NativeUascStreamingOrderedForwardServer")]
        [TestCase(true, DataChannelDeliveryMode.Ordered, DataChannelDirection.AcceptorToOpener, false, false,
            DataChannelProfiles.UadpOpaqueApplicationPayload, TestName = "NativeUascStreamingOrderedReverseClient")]
        [TestCase(true, DataChannelDeliveryMode.Ordered, DataChannelDirection.AcceptorToOpener, true, false,
            DataChannelProfiles.UadpOpaqueApplicationPayload, TestName = "NativeUascStreamingOrderedReverseServer")]
        [TestCase(true, DataChannelDeliveryMode.Datagram, DataChannelDirection.Duplex, false, false,
            DataChannelProfiles.UadpOpaqueApplicationPayload, TestName = "NativeUascStreamingDatagramDuplexClient")]
        [TestCase(true, DataChannelDeliveryMode.Datagram, DataChannelDirection.Duplex, true, false,
            DataChannelProfiles.UadpOpaqueApplicationPayload, TestName = "NativeUascStreamingDatagramDuplexServer")]
        [TestCase(true, DataChannelDeliveryMode.Datagram, DataChannelDirection.OpenerToAcceptor, false, false,
            DataChannelProfiles.UadpOpaqueApplicationPayload, TestName = "NativeUascStreamingDatagramForwardClient")]
        [TestCase(true, DataChannelDeliveryMode.Datagram, DataChannelDirection.OpenerToAcceptor, true, false,
            DataChannelProfiles.UadpOpaqueApplicationPayload, TestName = "NativeUascStreamingDatagramForwardServer")]
        [TestCase(true, DataChannelDeliveryMode.Datagram, DataChannelDirection.AcceptorToOpener, false, false,
            DataChannelProfiles.UadpOpaqueApplicationPayload, TestName = "NativeUascStreamingDatagramReverseClient")]
        [TestCase(true, DataChannelDeliveryMode.Datagram, DataChannelDirection.AcceptorToOpener, true, false,
            DataChannelProfiles.UadpOpaqueApplicationPayload, TestName = "NativeUascStreamingDatagramReverseServer")]
        [TestCase(true, DataChannelDeliveryMode.Ordered, DataChannelDirection.Duplex, false, false,
            DataChannelProfiles.UadpAvApplicationPayload, TestName = "NativeUascStreamingAvOrdered")]
        [TestCase(true, DataChannelDeliveryMode.Datagram, DataChannelDirection.Duplex, true, false,
            DataChannelProfiles.UadpAvApplicationPayload, TestName = "NativeUascStreamingAvDatagram")]
        [TestCase(true, DataChannelDeliveryMode.Ordered, DataChannelDirection.Duplex, false, true,
            DataChannelProfiles.UadpOpaqueApplicationPayload, TestName = "NativeUascStreamingManagedOpaqueBaseline")]
        [TestCase(true, DataChannelDeliveryMode.Ordered, DataChannelDirection.Duplex, false, true,
            DataChannelProfiles.UadpAvApplicationPayload, TestName = "NativeUascStreamingManagedAvBaseline")]
        [SupportedOSPlatform("windows")]
        [SupportedOSPlatform("linux")]
        [SupportedOSPlatform("osx")]
        public async Task NativeUascBaselineCarriesServicesAndOptionalConfiguredFlow(
            bool enableFlows, DataChannelDeliveryMode delivery, DataChannelDirection direction, bool serverOpens,
            bool useManagedClient = false, string protocol = DataChannelProfiles.UadpPayload)
        {
            if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
            {
                throw new PlatformNotSupportedException("QUIC interoperability requires a supported QUIC platform.");
            }
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            var serverTransport = new NativeQuicUascTransport(
                m_backend!, m_environment!.ServerBudget, m_environment.BindingOptions);
            var clientTransport = new NativeQuicUascTransport(
                m_backend!, m_environment.ClientBudget, m_environment.BindingOptions);
            await using NativeQuicUascListener listener = await serverTransport.ListenAsync(
                new Uri("opc.quic://127.0.0.1:0/uasc?resource=services"), m_serverCredentials!)
                .ConfigureAwait(false);
            const string policy = SecurityPolicies.Basic256Sha256;
            using CertificateEntry serverCertificate =
                m_environment.ServerManager.AcquireApplicationCertificateBySecurityPolicy(policy)!;
            using CertificateEntry clientCertificate =
                m_environment.ClientManager.AcquireApplicationCertificateBySecurityPolicy(policy)!;
            using var clientChain = new CertificateCollection { clientCertificate.Certificate };
            foreach (Certificate issuer in clientCertificate.IssuerChain)
            {
                clientChain.Add(issuer);
            }
            var authorizer = new Mock<IDataChannelAuthorizer>();
            authorizer.Setup(value => value.AuthorizeAsync(
                    It.IsAny<DataChannelAuthorizationRequest>(), It.IsAny<CancellationToken>()))
                .Returns((DataChannelAuthorizationRequest request, CancellationToken token) =>
                    ValueTask.FromResult(request.Mode == DataChannelAuthorizationMode.Configured &&
                        request.Parameters.ResourceUri == NativeFlowTestEnvironment.ResourceUri &&
                        request.Parameters.Protocol == protocol
                            ? new DataChannelAuthorization(DataChannelResult.Good, request.Parameters)
                            : new DataChannelAuthorization(DataChannelResult.NotAuthorized)));
            DataChannelContextFactory? clientFactory = enableFlows
                ? new DataChannelContextFactory(authorizer.Object, m_environment.Options,
                    m_environment.ClientBudget, TimeProvider.System, m_environment.Telemetry)
                : null;
            DataChannelContextFactory? serverFactory = enableFlows
                ? new DataChannelContextFactory(authorizer.Object, m_environment.Options,
                    m_environment.ServerBudget, TimeProvider.System, m_environment.Telemetry)
                : null;
            var endpoint = new EndpointDescription
            {
                EndpointUrl = listener.EndpointUrl.ToString(),
                SecurityMode = MessageSecurityMode.SignAndEncrypt,
                SecurityPolicyUri = policy,
                ServerCertificate = serverCertificate.Certificate.RawData.ToByteString(),
                TransportProfileUri = DataChannelProfiles.UascQuic,
                Server = new ApplicationDescription { ApplicationUri = NativeFlowTestEnvironment.ServerUri }
            };
            EndpointConfiguration configuration = EndpointConfiguration.Create();
            configuration.OperationTimeout = 10000;
            configuration.MaxMessageSize = 65536;
            configuration.MaxBufferSize = 65536;
            var callback = new Mock<ITransportListenerCallback>();
            callback.Setup(value => value.ProcessRequestAsync(It.IsAny<SecureChannelContext>(),
                    It.IsAny<IServiceRequest>(), It.IsAny<CancellationToken>()))
                .Returns((SecureChannelContext context, IServiceRequest request, CancellationToken token) =>
                {
                    Assert.That(context, Is.Not.Null);
                    Assert.That(request, Is.InstanceOf<ReadRequest>());
                    return ValueTask.FromResult<IServiceResponse>(new ReadResponse
                    {
                        ResponseHeader = new ResponseHeader { ServiceResult = StatusCodes.Good }
                    });
                });
            await using var uascListener = new TcpTransportListener(m_environment.Telemetry);
            uascListener.OpenExternal(listener.EndpointUrl, new TransportListenerSettings
            {
                Descriptions = new List<EndpointDescription> { endpoint },
                Configuration = configuration,
                ServerCertificates = m_environment.ServerManager,
                CertificateValidator = m_environment.ServerManager,
                DataChannels = serverFactory,
                NamespaceUris = new NamespaceTable(),
                Factory = EncodeableFactory.Create(),
                MaxChannelCount = 4
            }, callback.Object);
            Task<NativeQuicUascConnection> accepting = listener.AcceptAsync(timeout.Token).AsTask();
            await using NativeQuicUascConnection? client = useManagedClient ? null :
                await clientTransport.ConnectAsync(listener.EndpointUrl, m_clientCredentials!,
                    cancellationToken: timeout.Token).ConfigureAwait(false);
            await using QuicUascConnection? managedClient = useManagedClient
                ? await new QuicUascTransport().ConnectAsync(listener.EndpointUrl,
                    new SslClientAuthenticationOptions
                    {
                        TargetHost = listener.EndpointUrl.DnsSafeHost,
                        ApplicationProtocols = [new SslApplicationProtocol(DataChannelProfiles.UascQuicAlpn)],
                        RemoteCertificateValidationCallback = (_, certificate, _, errors) =>
                            certificate != null &&
                            errors is SslPolicyErrors.None or SslPolicyErrors.RemoteCertificateChainErrors &&
                            certificate.GetRawCertData().AsSpan().SequenceEqual(m_certificate!.RawData)
                    }, timeout.Token).ConfigureAwait(false)
                : null;
            using ITransportChannel channel = client != null
                ? client.Create(m_environment.Telemetry) : managedClient!.Create(m_environment.Telemetry);
            async Task OpenClientAsync()
            {
                await ((ISecureChannel)channel).OpenAsync(listener.EndpointUrl, new TransportChannelSettings
                {
                    Description = endpoint,
                    Configuration = configuration,
                    ClientCertificate = clientCertificate.Certificate,
                    ClientCertificateChain = clientChain,
                    ServerCertificate = serverCertificate.Certificate,
                    CertificateValidator = m_environment.ClientManager,
                    DataChannels = clientFactory,
                    NamespaceUris = new NamespaceTable(),
                    Factory = EncodeableFactory.Create()
                }, timeout.Token).ConfigureAwait(false);
            }
            Task opening = OpenClientAsync();
            await using NativeQuicUascConnection server = await accepting.ConfigureAwait(false);
            TcpListenerChannel acceptedChannel =
                uascListener.AcceptTransport(server, m_environment.ServerManager, configuration.MaxBufferSize);
            await opening.ConfigureAwait(false);
            if (useManagedClient)
            {
                Assert.That(server.MaxDirectDatagramSize, Is.Zero,
                    "A reliable baseline peer need not negotiate RFC 9221 datagrams.");
            }
            await listener.DisposeAsync().ConfigureAwait(false);
            Assert.That(await channel.SendRequestAsync(new ReadRequest
            {
                RequestHeader = new RequestHeader { TimeoutHint = 10000 },
                NodesToRead = new ArrayOf<ReadValueId>()
            }, timeout.Token).ConfigureAwait(false), Is.InstanceOf<ReadResponse>());
            Assert.Multiple(() =>
            {
                Assert.That(clientCertificate.Certificate.RawData, Is.Not.EqualTo(m_certificate!.RawData));
                Assert.That(serverCertificate.Certificate.RawData, Is.Not.EqualTo(m_certificate.RawData));
                Assert.That(channel.EndpointDescription.TransportProfileUri, Is.EqualTo(DataChannelProfiles.UascQuic));
            });
            DataChannelContext? clientContext = ((IDataChannelTransportChannel)channel).DataChannels;
            DataChannelContext? serverContext = acceptedChannel.DataChannels;
            if (enableFlows)
            {
                Assert.That(clientContext, Is.Not.Null);
                Assert.That(serverContext, Is.Not.Null);
                Assert.That(clientContext!.CarrierKind, Is.EqualTo(DataChannelCarrierKind.QuicUasc));
                Assert.That(serverContext!.CarrierKind, Is.EqualTo(DataChannelCarrierKind.QuicUasc));
                Assert.That(await clientContext.WaitForDirectBindingAsync(timeout.Token).ConfigureAwait(false),
                    Is.EqualTo(useManagedClient ? DataChannelResult.Unsupported : DataChannelResult.Good));
                if (!useManagedClient)
                {
                    Assert.That(await serverContext.WaitForDirectBindingAsync(timeout.Token).ConfigureAwait(false),
                        Is.EqualTo(DataChannelResult.Good));
                }
                DataChannelContext opener = serverOpens ? serverContext : clientContext;
                DataChannelContext acceptor = serverOpens ? clientContext : serverContext;
                DataChannelParametersDataType parameters = NativeFlowTestEnvironment.Parameters(delivery, direction);
                parameters.Protocol = protocol;
                parameters.ContentType = protocol switch
                {
                    DataChannelProfiles.UadpAvApplicationPayload => "video/h264",
                    DataChannelProfiles.UadpOpaqueApplicationPayload => "application/octet-stream",
                    _ => "application/opcua+uadp"
                };
                bool streaming = protocol != DataChannelProfiles.UadpPayload;
                if (streaming && useManagedClient)
                {
                    DataChannelException? missingRoute = await Assert.ThrowsAsync<DataChannelException>(async () =>
                        await opener.OpenAsync(parameters, cancellationToken: timeout.Token).ConfigureAwait(false))
                        .ConfigureAwait(false);
                    Assert.That(missingRoute!.Result, Is.EqualTo(DataChannelResult.InvalidArgument));
                }
                using IDisposable route = serverContext.RegisterUascPublisherRoute(
                    NativeFlowTestEnvironment.ResourceUri, new Variant(42u));
                using IDisposable reverseRoute = clientContext.RegisterUascPublisherRoute(
                    NativeFlowTestEnvironment.ResourceUri, new Variant(42u));
                Task<DataChannel?> acceptingFlow = acceptor.AcceptAsync(timeout.Token).AsTask();
                await using DataChannel source = await opener.OpenAsync(
                    parameters, cancellationToken: timeout.Token).ConfigureAwait(false);
                await using DataChannel sink = await acceptingFlow.ConfigureAwait(false) ??
                    throw new InvalidOperationException("The native baseline closed before configured READY.");
                if (streaming && useManagedClient)
                {
                    DataChannelException? duplicate = await Assert.ThrowsAsync<DataChannelException>(async () =>
                        await opener.OpenAsync(parameters, cancellationToken: timeout.Token).ConfigureAwait(false))
                        .ConfigureAwait(false);
                    Assert.That(duplicate!.Result, Is.EqualTo(DataChannelResult.InvalidArgument));
                    Assert.That(source.State, Is.EqualTo(DataChannelState.Open));
                    Assert.That(sink.State, Is.EqualTo(DataChannelState.Open));
                }
                DataChannel sender = direction == DataChannelDirection.AcceptorToOpener ? sink : source;
                DataChannel receiver = direction == DataChannelDirection.AcceptorToOpener ? source : sink;
                byte[] payload = streaming
                    ? [0x91, 0x82, 0x0c, 42, 0, 0, 0, 0, 0xff, 0x01, 0x7f]
                    : [0x91, 0x82, 0, 42, 0, 0, 0, 0x10, 0x20, 0, 0x40, 0xff];
                Assert.That(await sender.SendAsync(new ByteString(payload), cancellationToken: timeout.Token)
                    .ConfigureAwait(false), Is.EqualTo(DataChannelResult.Good));
                using DataChannelMessage? received = await receiver.ReadAsync(timeout.Token).ConfigureAwait(false);
                Assert.That(received!.Payload.Span.ToArray(), Is.EqualTo(payload));
                if (direction == DataChannelDirection.Duplex)
                {
                    Assert.That(await receiver.SendAsync(new ByteString(payload), cancellationToken: timeout.Token)
                        .ConfigureAwait(false), Is.EqualTo(DataChannelResult.Good));
                }
                using DataChannelMessage? reverse = direction == DataChannelDirection.Duplex
                    ? await sender.ReadAsync(timeout.Token).ConfigureAwait(false) : null;
                if (direction == DataChannelDirection.Duplex)
                {
                    Assert.That(reverse, Is.Not.Null);
                    Assert.That(reverse!.Payload.Span.ToArray(), Is.EqualTo(payload));
                }
                DataChannelParametersDataType revised = source.Parameters;
                revised.MaxMessageSize = 128;
                var sinkUpdated = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                void OnSinkUpdated(DataChannelStateChangedEventArgs notification)
                {
                    if (notification.Status.Revision == 2 && notification.Status.State == DataChannelState.Open)
                    {
                        sinkUpdated.TrySetResult();
                    }
                }
                sink.StateChanged += OnSinkUpdated;
                try
                {
                    Assert.That(await source.UpdateAsync(revised, cancellationToken: timeout.Token)
                        .ConfigureAwait(false), Is.EqualTo(DataChannelResult.Good));
                    await sinkUpdated.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                }
                finally
                {
                    sink.StateChanged -= OnSinkUpdated;
                }
                Assert.Multiple(() =>
                {
                    Assert.That(source.Revision, Is.EqualTo(2u));
                    Assert.That(sink.Revision, Is.EqualTo(2u));
                    Assert.That(sink.State, Is.EqualTo(DataChannelState.Open));
                    Assert.That(received.Revision, Is.EqualTo(1u));
                    if (direction == DataChannelDirection.Duplex)
                    {
                        Assert.That(reverse!.Revision, Is.EqualTo(1u));
                    }
                });
                Assert.That(await sender.SendAsync(new ByteString(payload), cancellationToken: timeout.Token)
                    .ConfigureAwait(false), Is.EqualTo(DataChannelResult.Good));
                using (DataChannelMessage? current = await receiver.ReadAsync(timeout.Token).ConfigureAwait(false))
                {
                    Assert.That(current!.Revision, Is.EqualTo(2u));
                    Assert.That(current.Payload.Span.ToArray(), Is.EqualTo(payload));
                }
                Assert.That(await channel.SendRequestAsync(new ReadRequest
                {
                    RequestHeader = new RequestHeader { TimeoutHint = 10000 },
                    NodesToRead = new ArrayOf<ReadValueId>()
                }, timeout.Token).ConfigureAwait(false), Is.InstanceOf<ReadResponse>());
                ulong chargedBytes = (ulong)(payload.Length +
                    (useManagedClient ? 0 : delivery == DataChannelDeliveryMode.Datagram ? 24 : 5));
                ulong reverseMessages = direction == DataChannelDirection.Duplex ? 1ul : 0ul;
                DataChannelDiagnosticsDataType senderDiagnostics = sender.GetDiagnostics();
                DataChannelDiagnosticsDataType receiverDiagnostics = receiver.GetDiagnostics();
                Assert.Multiple(() =>
                {
                    Assert.That(senderDiagnostics.MessagesSent, Is.EqualTo(2ul));
                    Assert.That(senderDiagnostics.BytesSent, Is.EqualTo(2 * chargedBytes));
                    Assert.That(senderDiagnostics.MessagesReceived, Is.EqualTo(reverseMessages));
                    Assert.That(senderDiagnostics.BytesReceived, Is.EqualTo(reverseMessages * chargedBytes));
                    Assert.That(receiverDiagnostics.MessagesSent, Is.EqualTo(reverseMessages));
                    Assert.That(receiverDiagnostics.BytesSent, Is.EqualTo(reverseMessages * chargedBytes));
                    Assert.That(receiverDiagnostics.MessagesReceived, Is.EqualTo(2ul));
                    Assert.That(receiverDiagnostics.BytesReceived, Is.EqualTo(2 * chargedBytes));
                });
                Assert.That(await sender.EndAsync(timeout.Token).ConfigureAwait(false),
                    Is.EqualTo(DataChannelResult.Good));
                Assert.That(await receiver.ReadAsync(timeout.Token).ConfigureAwait(false), Is.Null);
                Assert.That(await receiver.EndAsync(timeout.Token).ConfigureAwait(false),
                    Is.EqualTo(DataChannelResult.Good));
            }
            else
            {
                Assert.That(clientContext, Is.Null);
                Assert.That(serverContext, Is.Null);
            }
            Assert.That(await channel.SendRequestAsync(new ReadRequest
            {
                RequestHeader = new RequestHeader { TimeoutHint = 10000 },
                NodesToRead = new ArrayOf<ReadValueId>()
            }, timeout.Token).ConfigureAwait(false), Is.InstanceOf<ReadResponse>());
            callback.Verify(value => value.ProcessRequestAsync(It.IsAny<SecureChannelContext>(),
                It.IsAny<IServiceRequest>(), It.IsAny<CancellationToken>()), Times.Exactly(enableFlows ? 3 : 2));
            Task<Stream>? pendingClient = client?.AcceptDirectStreamAsync(Guid.NewGuid(), timeout.Token).AsTask();
            Task<Stream> pendingServer = server.AcceptDirectStreamAsync(Guid.NewGuid(), timeout.Token).AsTask();
            await channel.CloseAsync(timeout.Token).ConfigureAwait(false);
            if (pendingClient != null)
            {
                await Assert.CatchAsync<OperationCanceledException>(async () =>
                    await pendingClient.ConfigureAwait(false)).ConfigureAwait(false);
            }
            await Assert.CatchAsync<OperationCanceledException>(async () =>
                await pendingServer.ConfigureAwait(false)).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                if (client != null)
                {
                    Assert.That(client.IsAvailable, Is.False);
                }
                Assert.That(server.IsAvailable, Is.False);
            });
        }

        /// <summary>
        /// Runs the same authenticated native UASC/direct scenario used by the NativeAOT executable.
        /// </summary>
        [Test]
        public async Task NativeUascAotScenarioUsesAuthenticatedServicesAndDirectDelivery()
        {
            ArrayOf<string> checks = await NativeUascLiveScenario.RunAsync(m_backend!).ConfigureAwait(false);
            string[] completed = checks.Span.ToArray();
            Assert.Multiple(() =>
            {
                Assert.That(checks.Count, Is.EqualTo(98));
                Assert.That(completed, Does.Contain("AuthenticatedExporterBindingBothRoles"));
                foreach (string protocol in new[]
                {
                    DataChannelProfiles.UadpPayload,
                    DataChannelProfiles.UadpOpaqueApplicationPayload,
                    DataChannelProfiles.UadpAvApplicationPayload
                })
                {
                    Assert.That(completed, Does.Contain(protocol + " OrderedExactForwardBytesAndRevision"));
                    Assert.That(completed, Does.Contain(protocol + " DatagramExactReverseBytesAndRevision"));
                    Assert.That(completed, Does.Contain(protocol + " OrderedPeerRevisionCompleted"));
                    Assert.That(completed, Does.Contain(protocol + " DatagramExactRevisionTwoBytes"));
                    Assert.That(completed, Does.Contain(protocol + " OrderedCarrierChargedByteCounters"));
                    Assert.That(completed, Does.Contain(protocol + " DatagramCarrierChargedByteCounters"));
                    Assert.That(completed, Does.Contain(protocol + " OrderedForwardEndOfStream"));
                    Assert.That(completed, Does.Contain(protocol + " DatagramReverseEndOfStream"));
                }
                Assert.That(completed, Does.Contain("ApplicationCertificateAndServiceAudit"));
                Assert.That(completed, Does.Contain("ZeroUascAndDirectReservations"));
            });
        }

        /// <summary>
        /// Real exporter context and actual token-prefaced stream ownership work in both transport roles.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task NativeUascProvidersBindExporterAndActualTokenStreams(bool serverOpens)
        {
            await WithNativeUascPairAsync(async (client, server, token) =>
            {
                ByteString digest = new(new byte[32]);
                ByteString left = await client.ExportKeyingMaterialAsync(digest, token).ConfigureAwait(false);
                ByteString right = await server.ExportKeyingMaterialAsync(digest, token).ConfigureAwait(false);
                byte[] changed = new byte[32];
                changed[31] = 1;
                ByteString other = await client.ExportKeyingMaterialAsync(new ByteString(changed), token)
                    .ConfigureAwait(false);
                Assert.Multiple(() =>
                {
                    Assert.That(left.Length, Is.EqualTo(32));
                    Assert.That(left, Is.EqualTo(right));
                    Assert.That(left, Is.Not.EqualTo(other));
                    Assert.That(client.Capability, Is.EqualTo("quic-direct-draft"));
                    Assert.That(client.Authority, Is.EqualTo(server.Authority));
                    Assert.That(client.Path, Is.EqualTo("/uasc?selection=providers"));
                });
                await Assert.ThrowsAsync<ArgumentException>(async () =>
                    await client.ExportKeyingMaterialAsync(new ByteString(new byte[31]), token).ConfigureAwait(false))
                    .ConfigureAwait(false);
                NativeQuicUascConnection opener = serverOpens ? server : client;
                NativeQuicUascConnection acceptor = serverOpens ? client : server;
                Guid association = Guid.NewGuid();
                Task<Stream> admitting = acceptor.AcceptDirectStreamAsync(association, token).AsTask();
                await using Stream sending = await opener.OpenDirectStreamAsync(association, token).ConfigureAwait(false);
                await using Stream receiving = await admitting.ConfigureAwait(false);
                byte[] payload = [0, 0xff, 7, 0, 9];
                await sending.WriteAsync(payload, token).ConfigureAwait(false);
                byte[] actual = new byte[payload.Length];
                await receiving.ReadExactlyAsync(actual, token).ConfigureAwait(false);
                Assert.That(actual, Is.EqualTo(payload), "The exact token preface is consumed before payload delivery.");
                await receiving.WriteAsync(new byte[] { 0xfe, 0, 4 }, token).ConfigureAwait(false);
                byte[] reverse = new byte[3];
                await sending.ReadExactlyAsync(reverse, token).ConfigureAwait(false);
                Assert.That(reverse, Is.EqualTo(new byte[] { 0xfe, 0, 4 }));
                await sending.DisposeAsync().ConfigureAwait(false);
                await receiving.DisposeAsync().ConfigureAwait(false);
                DataChannelException? reused = await Assert.ThrowsAsync<DataChannelException>(async () =>
                    await opener.OpenDirectStreamAsync(association, token).ConfigureAwait(false)).ConfigureAwait(false);
                DataChannelException? rebound = await Assert.ThrowsAsync<DataChannelException>(async () =>
                    await acceptor.AcceptDirectStreamAsync(association, token).ConfigureAwait(false)).ConfigureAwait(false);
                Assert.Multiple(() =>
                {
                    Assert.That(reused!.Result, Is.EqualTo(DataChannelResult.InvalidState));
                    Assert.That(rebound!.Result, Is.EqualTo(DataChannelResult.InvalidState));
                    Assert.That(client.IsAvailable, Is.True);
                    Assert.That(server.IsAvailable, Is.True);
                });
            }).ConfigureAwait(false);
        }

        /// <summary>
        /// Actual datagrams retain commitment, cancellation and bounded single-reader semantics.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task NativeUascDirectDatagramsRefuseBeforeCommitAndRecoverFromCancellation(bool serverSends)
        {
            await WithNativeUascPairAsync(async (client, server, token) =>
            {
                NativeQuicUascConnection sender = serverSends ? server : client;
                NativeQuicUascConnection receiver = serverSends ? client : server;
                using var cancelled = CancellationTokenSource.CreateLinkedTokenSource(token);
                Task<ByteString> pending = receiver.ReceiveDirectDatagramAsync(cancelled.Token).AsTask();
                Assert.That(pending.IsCompleted, Is.False);
                DataChannelException? concurrent = await Assert.ThrowsAsync<DataChannelException>(async () =>
                    await receiver.ReceiveDirectDatagramAsync(token).ConfigureAwait(false)).ConfigureAwait(false);
                Assert.That(concurrent!.Result, Is.EqualTo(DataChannelResult.InvalidState));
                await cancelled.CancelAsync().ConfigureAwait(false);
                await Assert.CatchAsync<OperationCanceledException>(async () =>
                    await pending.ConfigureAwait(false)).ConfigureAwait(false);
                int commits = 0;
                var oversized = new DataChannelWrite(DataChannelPath.Datagram, 1, 0, 0,
                    new ByteString(new byte[sender.MaxDirectDatagramSize + 1]), true, 0,
                    () =>
                    {
                        commits++;
                        return DataChannelResult.Good;
                    });
                Assert.That(await sender.SendDirectDatagramAsync(oversized, token).ConfigureAwait(false),
                    Is.EqualTo(DataChannelResult.TooLarge));
                Assert.That(commits, Is.Zero);
                ByteString payload = Bytes(1, 0, 0xff, 8);
                var wrongPath = new DataChannelWrite(DataChannelPath.Flow, 1, 0, 0, payload, true, 0,
                    static () => DataChannelResult.Good);
                var control = new DataChannelWrite(DataChannelPath.Datagram, 1, 0, 0, payload, false, 0,
                    static () => DataChannelResult.Good);
                Assert.That(await sender.SendDirectDatagramAsync(wrongPath, token).ConfigureAwait(false),
                    Is.EqualTo(DataChannelResult.InvalidArgument));
                Assert.That(await sender.SendDirectDatagramAsync(control, token).ConfigureAwait(false),
                    Is.EqualTo(DataChannelResult.InvalidArgument));
                var refused = new DataChannelWrite(DataChannelPath.Datagram, 1, 0, 0, payload, true, 0,
                    static () => DataChannelResult.WouldBlock);
                Assert.That(await sender.SendDirectDatagramAsync(refused, token).ConfigureAwait(false),
                    Is.EqualTo(DataChannelResult.WouldBlock));
                Task<ByteString> reading = receiver.ReceiveDirectDatagramAsync(token).AsTask();
                var write = new DataChannelWrite(DataChannelPath.Datagram, 1, 0, 0, payload, true, 0,
                    static () => DataChannelResult.Good);
                Assert.That(await sender.SendDirectDatagramAsync(write, token).ConfigureAwait(false),
                    Is.EqualTo(DataChannelResult.Good));
                Assert.That(await reading.ConfigureAwait(false), Is.EqualTo(payload));
                Assert.Multiple(() =>
                {
                    Assert.That(oversized.IsCommitted, Is.False);
                    Assert.That(wrongPath.IsCommitted, Is.False);
                    Assert.That(control.IsCommitted, Is.False);
                    Assert.That(refused.IsCommitted, Is.False);
                    Assert.That(write.IsCommitted, Is.True);
                    Assert.That(client.IsAvailable, Is.True);
                    Assert.That(server.IsAvailable, Is.True);
                });
            }).ConfigureAwait(false);
        }

        /// <summary>
        /// Actual connection ownership survives a committed native admission refusal and delivers the next datagram.
        /// </summary>
        [TestCase(NativeQuicStatus.Limit)]
        [TestCase(NativeQuicStatus.Busy)]
        public async Task NativeUascCommittedDatagramCapacityRefusalPreservesBaseline(NativeQuicStatus refusal)
        {
            INativeQuicBackend backend = m_backend!;
            var proxy = new Mock<INativeQuicBackend>(MockBehavior.Strict);
            proxy.Setup(value => value.ConnectAsync(It.IsAny<NativeQuicOptions>(), It.IsAny<CancellationToken>()))
                .Returns((NativeQuicOptions options, CancellationToken token) => backend.ConnectAsync(options, token));
            int sends = 0;
            proxy.Setup(value => value.ConnectionAsync(It.IsAny<NativeQuicConnectionHandle>(),
                    It.IsAny<NativeQuicConnectionOperation>(), It.IsAny<NativeQuicRequest>(),
                    It.IsAny<CancellationToken>()))
                .Returns((NativeQuicConnectionHandle handle, NativeQuicConnectionOperation operation,
                    NativeQuicRequest request, CancellationToken token) =>
                    operation == NativeQuicConnectionOperation.SendDatagram && Interlocked.Increment(ref sends) == 1
                        ? new ValueTask<NativeQuicCompletion>(new NativeQuicCompletion
                        {
                            Status = refusal,
                            NativeStatus = refusal
                        })
                        : backend.ConnectionAsync(handle, operation, request, token));
            proxy.Setup(value => value.StreamAsync(It.IsAny<NativeQuicStreamHandle>(),
                    It.IsAny<NativeQuicStreamOperation>(), It.IsAny<NativeQuicRequest>(),
                    It.IsAny<CancellationToken>()))
                .Returns((NativeQuicStreamHandle handle, NativeQuicStreamOperation operation,
                    NativeQuicRequest request, CancellationToken token) =>
                    backend.StreamAsync(handle, operation, request, token));
            await WithNativeUascPairAsync(async (client, server, token) =>
            {
                ByteString payload = Bytes(1, 0, 7);
                var first = new DataChannelWrite(DataChannelPath.Datagram, 1, 0, 0, payload, true, 0,
                    static () => DataChannelResult.Good);
                Assert.That(await client.SendDirectDatagramAsync(first, token).ConfigureAwait(false),
                    Is.EqualTo(DataChannelResult.WouldBlock));
                Assert.That(first.IsCommitted, Is.True);
                Task<ByteString> receiving = server.ReceiveDirectDatagramAsync(token).AsTask();
                var next = new DataChannelWrite(DataChannelPath.Datagram, 1, 0, 0, payload, true, 0,
                    static () => DataChannelResult.Good);
                Assert.That(await client.SendDirectDatagramAsync(next, token).ConfigureAwait(false),
                    Is.EqualTo(DataChannelResult.Good));
                Assert.That(await receiving.ConfigureAwait(false), Is.EqualTo(payload));
                Assert.That(client.IsAvailable, Is.True);
            }, clientBackend: proxy.Object).ConfigureAwait(false);
        }

        /// <summary>
        /// Cancelled admissions retain exact nonreuse history and stop at its configured finite boundary.
        /// </summary>
        [Test]
        public async Task NativeUascDirectTokenHistoryIsFiniteAndRetainedAfterCancellation()
        {
            await WithNativeUascPairAsync(async (client, server, token) =>
            {
                DataChannelException? empty = await Assert.ThrowsAsync<DataChannelException>(async () =>
                    await server.AcceptDirectStreamAsync(Guid.Empty, token).ConfigureAwait(false)).ConfigureAwait(false);
                Assert.That(empty!.Result, Is.EqualTo(DataChannelResult.InvalidArgument));
                Guid previous = Guid.Empty;
                for (int i = 0; i < 8; i++)
                {
                    previous = Guid.NewGuid();
                    using var cancelled = CancellationTokenSource.CreateLinkedTokenSource(token);
                    Task<Stream> pending = server.AcceptDirectStreamAsync(previous, cancelled.Token).AsTask();
                    await cancelled.CancelAsync().ConfigureAwait(false);
                    await Assert.CatchAsync<OperationCanceledException>(async () =>
                        await pending.ConfigureAwait(false)).ConfigureAwait(false);
                }
                DataChannelException? duplicate = await Assert.ThrowsAsync<DataChannelException>(async () =>
                    await server.AcceptDirectStreamAsync(previous, token).ConfigureAwait(false)).ConfigureAwait(false);
                DataChannelException? exhausted = await Assert.ThrowsAsync<DataChannelException>(async () =>
                    await server.AcceptDirectStreamAsync(Guid.NewGuid(), token).ConfigureAwait(false))
                    .ConfigureAwait(false);
                Assert.Multiple(() =>
                {
                    Assert.That(duplicate!.Result, Is.EqualTo(DataChannelResult.InvalidState));
                    Assert.That(exhausted!.Result, Is.EqualTo(DataChannelResult.WouldBlock));
                    Assert.That(client.IsAvailable, Is.True);
                    Assert.That(server.IsAvailable, Is.True);
                });
            }, m_environment!.BindingOptions with { MaxStreamHistory = 8 }).ConfigureAwait(false);
        }

        /// <summary>
        /// Exact native stream exhaustion pauses accept instead of closing Services and recovers after release.
        /// </summary>
        [Test]
        public async Task NativeUascDirectStreamCapacityRecoversWithoutReplacingBaseline()
        {
            var streams = new List<Stream>();
            await WithNativeUascPairAsync(async (client, server, token) =>
            {
                try
                {
                    for (int i = 0; i < 3; i++)
                    {
                        Guid association = Guid.NewGuid();
                        Task<Stream> pending = server.AcceptDirectStreamAsync(association, token).AsTask();
                        streams.Add(await client.OpenDirectStreamAsync(association, token).ConfigureAwait(false));
                        streams.Add(await pending.ConfigureAwait(false));
                    }
                    DataChannelException? full = await Assert.ThrowsAsync<DataChannelException>(async () =>
                        await client.OpenDirectStreamAsync(Guid.NewGuid(), token).ConfigureAwait(false))
                        .ConfigureAwait(false);
                    Assert.Multiple(() =>
                    {
                        Assert.That(full!.Result, Is.EqualTo(DataChannelResult.WouldBlock));
                        Assert.That(client.IsAvailable, Is.True);
                        Assert.That(server.IsAvailable, Is.True);
                    });
                    await streams[0].DisposeAsync().ConfigureAwait(false);
                    await streams[1].DisposeAsync().ConfigureAwait(false);
                    Guid fresh = Guid.NewGuid();
                    Task<Stream> accepting = server.AcceptDirectStreamAsync(fresh, token).AsTask();
                    Stream sender = await client.OpenDirectStreamAsync(fresh, token).ConfigureAwait(false);
                    streams.Add(sender);
                    Stream receiver = await accepting.ConfigureAwait(false);
                    streams.Add(receiver);
                    await sender.WriteAsync(new byte[] { 0x7e }, token).ConfigureAwait(false);
                    byte[] received = new byte[1];
                    await receiver.ReadExactlyAsync(received, token).ConfigureAwait(false);
                    Assert.That(received[0], Is.EqualTo(0x7e));
                }
                finally
                {
                    foreach (Stream stream in streams)
                    {
                        await stream.DisposeAsync().ConfigureAwait(false);
                    }
                }
            }, m_environment!.BindingOptions with
            {
                Native = m_environment.BindingOptions.Native with { MaxStreams = 6 }
            }).ConfigureAwait(false);
        }

        /// <summary>
        /// Native accept closure can precede the connection monitor without faulting ordinary owner cleanup.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task NativeUascPeerClosureDrainsPendingDirectAdmission(bool closeServer)
        {
            await WithNativeUascPairAsync(async (client, server, token) =>
            {
                NativeQuicUascConnection closing = closeServer ? server : client;
                NativeQuicUascConnection receiving = closeServer ? client : server;
                Task<Stream> pending = receiving.AcceptDirectStreamAsync(Guid.NewGuid(), token).AsTask();
                Assert.That(pending.IsCompleted, Is.False);
                await closing.DisposeAsync().ConfigureAwait(false);
                await Assert.CatchAsync<OperationCanceledException>(async () =>
                    await pending.ConfigureAwait(false)).ConfigureAwait(false);
                await receiving.DisposeAsync().ConfigureAwait(false);
                Assert.Multiple(() =>
                {
                    Assert.That(client.IsAvailable, Is.False);
                    Assert.That(server.IsAvailable, Is.False);
                    Assert.That(client.MaxDirectDatagramSize, Is.Zero);
                    Assert.That(server.AreDirectStreamsAvailable, Is.False);
                });
            }, expectBaselineUsable: false).ConfigureAwait(false);
        }

        /// <summary>
        /// Unknown actual tokens are reset without consuming an unrelated pending admission or the baseline.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task NativeUascUnknownDirectTokenIsIsolatedAndExpectedAdmissionStillSucceeds(bool serverOpens)
        {
            await WithNativeUascPairAsync(async (client, server, token) =>
            {
                NativeQuicUascConnection opener = serverOpens ? server : client;
                NativeQuicUascConnection receiver = serverOpens ? client : server;
                Guid expected = Guid.NewGuid();
                Task<Stream> pending = receiver.AcceptDirectStreamAsync(expected, token).AsTask();
                await using Stream unknown = await opener.OpenDirectStreamAsync(Guid.NewGuid(), token)
                    .ConfigureAwait(false);
                NativeQuicTransportException? rejected = await Assert.ThrowsAsync<NativeQuicTransportException>(
                    async () => await unknown.ReadAsync(new byte[1], token).ConfigureAwait(false))
                    .ConfigureAwait(false);
                Assert.Multiple(() =>
                {
                    Assert.That(rejected!.Completion!.ReadOutcome, Is.EqualTo(2u));
                    Assert.That(rejected.Completion.ErrorCode, Is.EqualTo((ulong)DataChannelResult.InvalidState));
                    Assert.That(pending.IsCompleted, Is.False);
                });
                await using Stream source = await opener.OpenDirectStreamAsync(expected, token).ConfigureAwait(false);
                await using Stream sink = await pending.ConfigureAwait(false);
                await source.WriteAsync(new byte[] { 9, 0, 7 }, token).ConfigureAwait(false);
                byte[] bytes = new byte[3];
                await sink.ReadExactlyAsync(bytes, token).ConfigureAwait(false);
                Assert.That(bytes, Is.EqualTo(new byte[] { 9, 0, 7 }));
            }).ConfigureAwait(false);
        }

        /// <summary>
        /// Cancellation racing FIN/reset retains pending bytes and the native terminal outcome for later reads.
        /// </summary>
        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public async Task NativeByteStreamCancellationRacingTerminalPreservesPendingBytesAndOutcome(
            bool reset, bool hasPayload)
        {
            INativeQuicBackend backend = m_backend!;
            using var cancelled = new CancellationTokenSource();
            var proxy = new Mock<INativeQuicBackend>(MockBehavior.Strict);
            proxy.Setup(value => value.ConnectAsync(It.IsAny<NativeQuicOptions>(), It.IsAny<CancellationToken>()))
                .Returns((NativeQuicOptions options, CancellationToken token) => backend.ConnectAsync(options, token));
            proxy.Setup(value => value.ConnectionAsync(It.IsAny<NativeQuicConnectionHandle>(),
                    It.IsAny<NativeQuicConnectionOperation>(), It.IsAny<NativeQuicRequest>(),
                    It.IsAny<CancellationToken>()))
                .Returns((NativeQuicConnectionHandle handle, NativeQuicConnectionOperation operation,
                    NativeQuicRequest request, CancellationToken token) =>
                    backend.ConnectionAsync(handle, operation, request, token));
            int inject = 0;
            proxy.Setup(value => value.StreamAsync(It.IsAny<NativeQuicStreamHandle>(),
                    It.IsAny<NativeQuicStreamOperation>(), It.IsAny<NativeQuicRequest>(),
                    It.IsAny<CancellationToken>()))
                .Returns(async (NativeQuicStreamHandle handle, NativeQuicStreamOperation operation,
                    NativeQuicRequest request, CancellationToken token) =>
                {
                    if (operation == NativeQuicStreamOperation.Read && Interlocked.Exchange(ref inject, 0) == 1)
                    {
                        await cancelled.CancelAsync().ConfigureAwait(false);
                        return new NativeQuicCompletion
                        {
                            Status = NativeQuicStatus.Cancelled,
                            NativeStatus = NativeQuicStatus.Success,
                            CancellationRequested = true,
                            Payload = hasPayload ? Bytes(0x2b) : ByteString.Empty,
                            BytesTransferred = hasPayload ? 1u : 0u,
                            ReadOutcome = reset ? 2u : 1u,
                            ErrorCode = reset ? 97u : 0u,
                            Remote = reset
                        };
                    }
                    return await backend.StreamAsync(handle, operation, request, token).ConfigureAwait(false);
                });
            await WithNativeUascPairAsync(async (client, server, token) =>
            {
                Guid association = Guid.NewGuid();
                Task<Stream> accepting = server.AcceptDirectStreamAsync(association, token).AsTask();
                await using Stream source = await client.OpenDirectStreamAsync(association, token)
                    .ConfigureAwait(false);
                await using Stream sink = await accepting.ConfigureAwait(false);
                Volatile.Write(ref inject, 1);
                byte[] bytes = new byte[1];
                await Assert.CatchAsync<OperationCanceledException>(async () =>
                    await source.ReadAsync(bytes, cancelled.Token).ConfigureAwait(false)).ConfigureAwait(false);
                if (hasPayload)
                {
                    Assert.That(await source.ReadAsync(bytes, token).ConfigureAwait(false), Is.EqualTo(1));
                    Assert.That(bytes[0], Is.EqualTo(0x2b));
                }
                for (int i = 0; i < 2; i++)
                {
                    if (reset)
                    {
                        NativeQuicTransportException? terminal = await Assert.ThrowsAsync<NativeQuicTransportException>(
                            async () => await source.ReadAsync(bytes, token).ConfigureAwait(false)).ConfigureAwait(false);
                        Assert.Multiple(() =>
                        {
                            Assert.That(terminal!.Completion!.ReadOutcome, Is.EqualTo(2u));
                            Assert.That(terminal.Completion.ErrorCode, Is.EqualTo(97u));
                            Assert.That(terminal.Completion.Remote, Is.True);
                        });
                    }
                    else
                    {
                        Assert.That(await source.ReadAsync(bytes, token).ConfigureAwait(false), Is.Zero);
                    }
                }
                Assert.That(client.IsAvailable, Is.True);
            }, clientBackend: proxy.Object).ConfigureAwait(false);
        }

        /// <summary>
        /// Replacement connections cannot reuse the old transport exporter or an unregistered old token.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task NativeUascConnectionReplacementSeparatesExporterAndRetiredAssociation(bool serverOpens)
        {
            Guid retired = Guid.NewGuid();
            ByteString digest = new(new byte[32]);
            ByteString previousExport = default;
            NativeQuicUascConnection? previousOwner = null;
            await WithNativeUascPairAsync(async (client, server, token) =>
            {
                NativeQuicUascConnection opener = serverOpens ? server : client;
                NativeQuicUascConnection receiver = serverOpens ? client : server;
                previousOwner = opener;
                previousExport = await opener.ExportKeyingMaterialAsync(digest, token).ConfigureAwait(false);
                Task<Stream> accepting = receiver.AcceptDirectStreamAsync(retired, token).AsTask();
                await using Stream source = await opener.OpenDirectStreamAsync(retired, token).ConfigureAwait(false);
                await using Stream sink = await accepting.ConfigureAwait(false);
                await source.WriteAsync(new byte[] { 0x35 }, token).ConfigureAwait(false);
                byte[] bytes = new byte[1];
                await sink.ReadExactlyAsync(bytes, token).ConfigureAwait(false);
                Assert.That(bytes[0], Is.EqualTo(0x35));
            }).ConfigureAwait(false);
            Assert.That(previousOwner, Is.Not.Null);
            await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
                await previousOwner!.OpenDirectStreamAsync(Guid.NewGuid()).ConfigureAwait(false)).ConfigureAwait(false);

            await WithNativeUascPairAsync(async (client, server, token) =>
            {
                NativeQuicUascConnection opener = serverOpens ? server : client;
                NativeQuicUascConnection receiver = serverOpens ? client : server;
                ByteString freshExport = await opener.ExportKeyingMaterialAsync(digest, token).ConfigureAwait(false);
                Assert.Multiple(() =>
                {
                    Assert.That(previousExport.Length, Is.EqualTo(32));
                    Assert.That(freshExport.Length, Is.EqualTo(32));
                    Assert.That(freshExport.Span.SequenceEqual(previousExport.Span), Is.False);
                });
                Guid fresh = Guid.NewGuid();
                Task<Stream> accepting = receiver.AcceptDirectStreamAsync(fresh, token).AsTask();
                await using Stream replay = await opener.OpenDirectStreamAsync(retired, token).ConfigureAwait(false);
                NativeQuicTransportException? refused = await Assert.ThrowsAsync<NativeQuicTransportException>(
                    async () => await replay.ReadAsync(new byte[1], token).ConfigureAwait(false)).ConfigureAwait(false);
                Assert.Multiple(() =>
                {
                    Assert.That(refused!.Completion!.ReadOutcome, Is.EqualTo(2u));
                    Assert.That(refused.Completion.ErrorCode, Is.EqualTo((ulong)DataChannelResult.InvalidState));
                    Assert.That(accepting.IsCompleted, Is.False);
                });
                await using Stream source = await opener.OpenDirectStreamAsync(fresh, token).ConfigureAwait(false);
                await using Stream sink = await accepting.ConfigureAwait(false);
                await source.WriteAsync(new byte[] { 0x53 }, token).ConfigureAwait(false);
                byte[] bytes = new byte[1];
                await sink.ReadExactlyAsync(bytes, token).ConfigureAwait(false);
                Assert.That(bytes[0], Is.EqualTo(0x53));
            }).ConfigureAwait(false);
        }

        /// <summary>
        /// Streams arriving before any authenticated receiver registration are refused without speculative buffering.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task NativeUascPrematureDirectStreamIsRefusedBeforeAnyAdmission(bool serverOpens)
        {
            await WithNativeUascPairAsync(async (client, server, token) =>
            {
                NativeQuicUascConnection opener = serverOpens ? server : client;
                NativeQuicUascConnection receiver = serverOpens ? client : server;
                await using Stream premature = await opener.OpenDirectStreamAsync(Guid.NewGuid(), token)
                    .ConfigureAwait(false);
                NativeQuicTransportException? refused = await Assert.ThrowsAsync<NativeQuicTransportException>(
                    async () => await premature.ReadAsync(new byte[1], token).ConfigureAwait(false))
                    .ConfigureAwait(false);
                Assert.Multiple(() =>
                {
                    Assert.That(refused!.Completion!.ReadOutcome, Is.EqualTo(2u));
                    Assert.That(refused.Completion.ErrorCode, Is.EqualTo((ulong)DataChannelResult.InvalidState));
                    Assert.That(receiver.IsAvailable, Is.True);
                });
                Guid expected = Guid.NewGuid();
                Task<Stream> accepting = receiver.AcceptDirectStreamAsync(expected, token).AsTask();
                await using Stream source = await opener.OpenDirectStreamAsync(expected, token).ConfigureAwait(false);
                await using Stream sink = await accepting.ConfigureAwait(false);
                await source.WriteAsync(new byte[] { 0x3b }, token).ConfigureAwait(false);
                byte[] bytes = new byte[1];
                await sink.ReadExactlyAsync(bytes, token).ConfigureAwait(false);
                Assert.That(bytes[0], Is.EqualTo(0x3b));
            }).ConfigureAwait(false);
        }

        /// <summary>
        /// Invalid native prefaces cannot consume another token; uni/bidirectional fragmented admission preserves bytes.
        /// </summary>
        [TestCase(false, 0)]
        [TestCase(false, 1)]
        [TestCase(false, 8)]
        [TestCase(false, 15)]
        [TestCase(false, 16)]
        [TestCase(true, 0)]
        [TestCase(true, 1)]
        [TestCase(true, 8)]
        [TestCase(true, 15)]
        [TestCase(true, 16)]
        public async Task NativeUascInvalidPrefacePreservesPendingAdmissionAndBaseline(
            bool unidirectional, int prefixLength)
        {
            bool expires = prefixLength == 8;
            bool premature = prefixLength == 16;
            QuicDataChannelOptions options = expires
                ? m_environment!.BindingOptions with { EstablishmentTimeout = TimeSpan.FromSeconds(1) }
                : m_environment!.BindingOptions;
            var transport = new NativeQuicUascTransport(
                m_backend!, m_environment.ServerBudget, options);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            await using (NativeQuicUascListener listener = await transport.ListenAsync(
                new Uri("opc.quic://127.0.0.1:0/uasc"), m_serverCredentials!,
                cancellationToken: timeout.Token).ConfigureAwait(false))
            {
                Task<NativeQuicUascConnection> accepting = listener.AcceptAsync(timeout.Token).AsTask();
                await using NativeQuicConnection peer = await NativeQuicConnection.ConnectTlsAsync(
                    m_backend!, m_clientCredentials!, listener.EndpointUrl.Authority,
                    DataChannelProfiles.UascQuicAlpn, "localhost", m_environment.BindingOptions.Native,
                    cancellationToken: timeout.Token).ConfigureAwait(false);
                await using NativeQuicStream peerBaseline = await peer.OpenStreamAsync(
                    cancellationToken: timeout.Token).ConfigureAwait(false);
                Assert.That((await peerBaseline.SendAsync(Bytes(0x48), cancellationToken: timeout.Token)
                    .ConfigureAwait(false))!.Status, Is.EqualTo(NativeQuicStatus.Success));
                await using NativeQuicUascConnection baseline = await accepting.ConfigureAwait(false);
                await using Stream baselineBytes = await baseline.ClaimStreamAsync(
                    listener.EndpointUrl, timeout.Token).ConfigureAwait(false);
                byte[] first = new byte[1];
                await baselineBytes.ReadExactlyAsync(first, timeout.Token).ConfigureAwait(false);
                Assert.That(first[0], Is.EqualTo(0x48));

                Guid expected = Guid.NewGuid();
                Task<Stream>? admitted = premature
                    ? null : baseline.AcceptDirectStreamAsync(expected, timeout.Token).AsTask();
                await using NativeQuicStream malformed = await peer.OpenStreamAsync(
                    unidirectional: unidirectional, cancellationToken: timeout.Token).ConfigureAwait(false);
                byte[] truncated = Guid.NewGuid().ToByteArray()[..prefixLength];
                NativeQuicCompletion? sent = await malformed.SendAsync(
                    new ByteString(truncated), flags: expires || premature ? 0u : 1u,
                    cancellationToken: timeout.Token).ConfigureAwait(false);
                Assert.That(sent!.Status, Is.EqualTo(NativeQuicStatus.Success));
                if (expires || premature)
                {
                    NativeQuicCompletion refused = await malformed.WaitStoppedAsync(timeout.Token)
                        .ConfigureAwait(false);
                    Assert.Multiple(() =>
                    {
                        Assert.That(refused.Status, Is.EqualTo(NativeQuicStatus.Success));
                        Assert.That(refused.Remote, Is.True);
                        Assert.That(refused.ErrorCode, Is.EqualTo((ulong)(expires
                            ? DataChannelResult.Timeout : DataChannelResult.InvalidState)));
                    });
                    if (expires)
                    {
                        DataChannelException? expired = await Assert.ThrowsAsync<DataChannelException>(async () =>
                            await admitted!.ConfigureAwait(false)).ConfigureAwait(false);
                        Assert.That(expired!.Result, Is.EqualTo(DataChannelResult.Timeout));
                    }
                    expected = Guid.NewGuid();
                    admitted = baseline.AcceptDirectStreamAsync(expected, timeout.Token).AsTask();
                }

                await using NativeQuicStream valid = await peer.OpenStreamAsync(
                    unidirectional: unidirectional, cancellationToken: timeout.Token).ConfigureAwait(false);
                byte[] preface = expected.ToByteArray();
                Assert.That((await valid.SendAsync(new ByteString(preface.AsMemory(0, 1)), cancellationToken: timeout.Token)
                    .ConfigureAwait(false))!.Status, Is.EqualTo(NativeQuicStatus.Success));
                Assert.That((await valid.SendAsync(new ByteString(preface.AsMemory(1)), cancellationToken: timeout.Token)
                    .ConfigureAwait(false))!.Status, Is.EqualTo(NativeQuicStatus.Success));
                Assert.That((await valid.SendAsync(Bytes(4, 0, 9), cancellationToken: timeout.Token)
                    .ConfigureAwait(false))!.Status, Is.EqualTo(NativeQuicStatus.Success));
                await using Stream sink = await admitted!.ConfigureAwait(false);
                byte[] received = new byte[3];
                await sink.ReadExactlyAsync(received, timeout.Token).ConfigureAwait(false);
                Assert.That(received, Is.EqualTo(new byte[] { 4, 0, 9 }));

                await baselineBytes.WriteAsync(new byte[] { 0x41 }, timeout.Token).ConfigureAwait(false);
                NativeQuicCompletion response = await peerBaseline.ReadAsync(timeout.Token).ConfigureAwait(false);
                Assert.Multiple(() =>
                {
                    Assert.That(response.Status, Is.EqualTo(NativeQuicStatus.Success));
                    Assert.That(response.Payload, Is.EqualTo(Bytes(0x41)));
                    Assert.That(baseline.IsAvailable, Is.True);
                });
            }
            DataChannelResourceAssertions.AreReleased(m_environment.ServerBudget.Usage);
        }

        /// <summary>
        /// Both native accept workers share one preface limit without consuming a refused stream's valid token.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task NativeUascConcurrentPrefaceLimitIsSharedAcrossStreamKinds(bool firstUnidirectional)
        {
            INativeQuicBackend backend = m_backend!;
            var observed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var proxy = new Mock<INativeQuicBackend>(MockBehavior.Strict);
            proxy.Setup(value => value.ListenAsync(It.IsAny<NativeQuicOptions>(), It.IsAny<CancellationToken>()))
                .Returns((NativeQuicOptions value, CancellationToken token) => backend.ListenAsync(value, token));
            proxy.Setup(value => value.ListenerAsync(It.IsAny<NativeQuicListenerHandle>(),
                    It.IsAny<NativeQuicListenerOperation>(), It.IsAny<NativeQuicRequest>(),
                    It.IsAny<CancellationToken>()))
                .Returns((NativeQuicListenerHandle handle, NativeQuicListenerOperation operation,
                    NativeQuicRequest request, CancellationToken token) =>
                    backend.ListenerAsync(handle, operation, request, token));
            proxy.Setup(value => value.ConnectionAsync(It.IsAny<NativeQuicConnectionHandle>(),
                    It.IsAny<NativeQuicConnectionOperation>(), It.IsAny<NativeQuicRequest>(),
                    It.IsAny<CancellationToken>()))
                .Returns((NativeQuicConnectionHandle handle, NativeQuicConnectionOperation operation,
                    NativeQuicRequest request, CancellationToken token) =>
                    backend.ConnectionAsync(handle, operation, request, token));
            proxy.Setup(value => value.StreamAsync(It.IsAny<NativeQuicStreamHandle>(),
                    It.IsAny<NativeQuicStreamOperation>(), It.IsAny<NativeQuicRequest>(),
                    It.IsAny<CancellationToken>()))
                .Returns((NativeQuicStreamHandle handle, NativeQuicStreamOperation operation,
                    NativeQuicRequest request, CancellationToken token) =>
                    ObserveReadAsync(handle, operation, request, token));
            var options = m_environment!.BindingOptions with { MaxPendingStreams = 1 };
            var transport = new NativeQuicUascTransport(proxy.Object, m_environment.ServerBudget, options);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            await using (NativeQuicUascListener listener = await transport.ListenAsync(
                new Uri("opc.quic://127.0.0.1:0/uasc?prefaces=shared"), m_serverCredentials!,
                cancellationToken: timeout.Token).ConfigureAwait(false))
            {
                Task<NativeQuicUascConnection> accepting = listener.AcceptAsync(timeout.Token).AsTask();
                await using NativeQuicConnection peer = await NativeQuicConnection.ConnectTlsAsync(
                    backend, m_clientCredentials!, listener.EndpointUrl.Authority,
                    DataChannelProfiles.UascQuicAlpn, "localhost", m_environment.BindingOptions.Native,
                    cancellationToken: timeout.Token).ConfigureAwait(false);
                await using NativeQuicStream peerBaseline = await peer.OpenStreamAsync(
                    cancellationToken: timeout.Token).ConfigureAwait(false);
                Assert.That((await peerBaseline.SendAsync(Bytes(0x48), cancellationToken: timeout.Token)
                    .ConfigureAwait(false))!.Status, Is.EqualTo(NativeQuicStatus.Success));
                await using NativeQuicUascConnection baseline = await accepting.ConfigureAwait(false);
                await using Stream baselineBytes = await baseline.ClaimStreamAsync(
                    listener.EndpointUrl, timeout.Token).ConfigureAwait(false);
                byte[] first = new byte[1];
                await baselineBytes.ReadExactlyAsync(first, timeout.Token).ConfigureAwait(false);
                Assert.That(first[0], Is.EqualTo(0x48));

                Guid expected = Guid.NewGuid();
                Task<Stream> admitted = baseline.AcceptDirectStreamAsync(expected, timeout.Token).AsTask();
                await using NativeQuicStream incomplete = await peer.OpenStreamAsync(
                    unidirectional: firstUnidirectional, cancellationToken: timeout.Token).ConfigureAwait(false);
                Assert.That((await incomplete.SendAsync(Bytes(0x7b), cancellationToken: timeout.Token)
                    .ConfigureAwait(false))!.Status, Is.EqualTo(NativeQuicStatus.Success));
                await observed.Task.WaitAsync(timeout.Token).ConfigureAwait(false);

                await using NativeQuicStream competing = await peer.OpenStreamAsync(
                    unidirectional: !firstUnidirectional, cancellationToken: timeout.Token).ConfigureAwait(false);
                Assert.That((await competing.SendAsync(new ByteString(expected.ToByteArray()),
                    cancellationToken: timeout.Token).ConfigureAwait(false))!.Status,
                    Is.EqualTo(NativeQuicStatus.Success));
                NativeQuicCompletion refused = await competing.WaitStoppedAsync(timeout.Token).ConfigureAwait(false);
                Assert.Multiple(() =>
                {
                    Assert.That(refused.Status, Is.EqualTo(NativeQuicStatus.Success));
                    Assert.That(refused.Remote, Is.True);
                    Assert.That(refused.ErrorCode, Is.EqualTo((ulong)DataChannelResult.WouldBlock));
                    Assert.That(admitted.IsCompleted, Is.False);
                    Assert.That(baseline.IsAvailable, Is.True);
                });

                Assert.That((await incomplete.SendAsync(new ByteString(new byte[15]),
                    cancellationToken: timeout.Token).ConfigureAwait(false))!.Status,
                    Is.EqualTo(NativeQuicStatus.Success));
                NativeQuicCompletion unknown = await incomplete.WaitStoppedAsync(timeout.Token).ConfigureAwait(false);
                Assert.That(unknown.ErrorCode, Is.EqualTo((ulong)DataChannelResult.InvalidState));
                Assert.That(admitted.IsCompleted, Is.False);
                await using NativeQuicStream fresh = await peer.OpenStreamAsync(
                    unidirectional: firstUnidirectional, cancellationToken: timeout.Token).ConfigureAwait(false);
                Assert.That((await fresh.SendAsync(new ByteString(expected.ToByteArray()),
                    cancellationToken: timeout.Token).ConfigureAwait(false))!.Status,
                    Is.EqualTo(NativeQuicStatus.Success));
                Assert.That((await fresh.SendAsync(Bytes(0, 7, 0xff), cancellationToken: timeout.Token)
                    .ConfigureAwait(false))!.Status, Is.EqualTo(NativeQuicStatus.Success));
                await using Stream sink = await admitted.ConfigureAwait(false);
                byte[] received = new byte[3];
                await sink.ReadExactlyAsync(received, timeout.Token).ConfigureAwait(false);
                Assert.That(received, Is.EqualTo(new byte[] { 0, 7, 0xff }));
                await baselineBytes.WriteAsync(new byte[] { 0x41 }, timeout.Token).ConfigureAwait(false);
                NativeQuicCompletion response = await peerBaseline.ReadAsync(timeout.Token).ConfigureAwait(false);
                Assert.Multiple(() =>
                {
                    Assert.That(response.Status, Is.EqualTo(NativeQuicStatus.Success));
                    Assert.That(response.Payload, Is.EqualTo(Bytes(0x41)));
                    Assert.That(baseline.IsAvailable, Is.True);
                });
            }
            DataChannelResourceAssertions.AreReleased(m_environment.ServerBudget.Usage);

            async ValueTask<NativeQuicCompletion> ObserveReadAsync(
                NativeQuicStreamHandle handle, NativeQuicStreamOperation operation,
                NativeQuicRequest request, CancellationToken token)
            {
                NativeQuicCompletion result = await backend.StreamAsync(handle, operation, request, token)
                    .ConfigureAwait(false);
                if (operation == NativeQuicStreamOperation.Read && result.Payload == Bytes(0x7b))
                {
                    observed.TrySetResult();
                }
                return result;
            }
        }

        /// <summary>
        /// Admission deadlines retain token nonreuse without preventing a fresh actual stream or baseline I/O.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task NativeUascDirectAdmissionTimeoutRetiresOnlyItsToken(bool serverOpens)
        {
            await WithNativeUascPairAsync(async (client, server, token) =>
            {
                NativeQuicUascConnection opener = serverOpens ? server : client;
                NativeQuicUascConnection receiver = serverOpens ? client : server;
                Guid expired = Guid.NewGuid();
                DataChannelException? timeout = await Assert.ThrowsAsync<DataChannelException>(async () =>
                    await receiver.AcceptDirectStreamAsync(expired, token).ConfigureAwait(false)).ConfigureAwait(false);
                Assert.That(timeout!.Result, Is.EqualTo(DataChannelResult.Timeout));
                DataChannelException? replay = await Assert.ThrowsAsync<DataChannelException>(async () =>
                    await receiver.AcceptDirectStreamAsync(expired, token).ConfigureAwait(false)).ConfigureAwait(false);
                Assert.That(replay!.Result, Is.EqualTo(DataChannelResult.InvalidState));

                Guid fresh = Guid.NewGuid();
                Task<Stream> accepting = receiver.AcceptDirectStreamAsync(fresh, token).AsTask();
                await using Stream source = await opener.OpenDirectStreamAsync(fresh, token).ConfigureAwait(false);
                await using Stream sink = await accepting.ConfigureAwait(false);
                await source.WriteAsync(new byte[] { 3, 0, 8 }, token).ConfigureAwait(false);
                byte[] bytes = new byte[3];
                await sink.ReadExactlyAsync(bytes, token).ConfigureAwait(false);
                Assert.That(bytes, Is.EqualTo(new byte[] { 3, 0, 8 }));
            }, m_environment!.BindingOptions with
            {
                EstablishmentTimeout = TimeSpan.FromMilliseconds(500)
            }).ConfigureAwait(false);
        }

        /// <summary>
        /// Pending admission refuses before consuming a token and returns its slot after caller cancellation.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task NativeUascPendingAdmissionLimitRecoversWithoutConsumingRefusedToken(bool serverOpens)
        {
            await WithNativeUascPairAsync(async (client, server, token) =>
            {
                NativeQuicUascConnection opener = serverOpens ? server : client;
                NativeQuicUascConnection receiver = serverOpens ? client : server;
                using var cancelled = CancellationTokenSource.CreateLinkedTokenSource(token);
                Guid first = Guid.NewGuid();
                Task<Stream> pending = receiver.AcceptDirectStreamAsync(first, cancelled.Token).AsTask();
                Assert.That(pending.IsCompleted, Is.False);
                Guid refused = Guid.NewGuid();
                DataChannelException? full = await Assert.ThrowsAsync<DataChannelException>(async () =>
                    await receiver.AcceptDirectStreamAsync(refused, token).ConfigureAwait(false)).ConfigureAwait(false);
                Assert.That(full!.Result, Is.EqualTo(DataChannelResult.WouldBlock));
                Assert.That(pending.IsCompleted, Is.False);
                await cancelled.CancelAsync().ConfigureAwait(false);
                await Assert.CatchAsync<OperationCanceledException>(async () =>
                    await pending.ConfigureAwait(false)).ConfigureAwait(false);

                Task<Stream> accepting = receiver.AcceptDirectStreamAsync(refused, token).AsTask();
                await using Stream source = await opener.OpenDirectStreamAsync(refused, token).ConfigureAwait(false);
                await using Stream sink = await accepting.ConfigureAwait(false);
                await source.WriteAsync(new byte[] { 6, 0, 2 }, token).ConfigureAwait(false);
                byte[] bytes = new byte[3];
                await sink.ReadExactlyAsync(bytes, token).ConfigureAwait(false);
                Assert.That(bytes, Is.EqualTo(new byte[] { 6, 0, 2 }));
            }, m_environment!.BindingOptions with { MaxPendingStreams = 1 }).ConfigureAwait(false);
        }

        /// <summary>
        /// Credential revocation closes active byte owners and pending direct admission/datagrams on either role.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task NativeUascCredentialRevocationDrainsActiveAndPendingDirectOwners(bool revokeServer)
        {
            using var invalidated = new CancellationTokenSource();
            await WithNativeUascPairAsync(async (client, server, token) =>
            {
                NativeQuicUascConnection revoked = revokeServer ? server : client;
                Guid association = Guid.NewGuid();
                Task<Stream> accepting = server.AcceptDirectStreamAsync(association, token).AsTask();
                await using Stream source = await client.OpenDirectStreamAsync(association, token)
                    .ConfigureAwait(false);
                await using Stream sink = await accepting.ConfigureAwait(false);
                await source.WriteAsync(new byte[] { 0x7d }, token).ConfigureAwait(false);
                byte[] bytes = new byte[1];
                await sink.ReadExactlyAsync(bytes, token).ConfigureAwait(false);
                Assert.That(bytes[0], Is.EqualTo(0x7d));

                Task<Stream> admission = revoked.AcceptDirectStreamAsync(Guid.NewGuid(), token).AsTask();
                Task<ByteString> datagram = revoked.ReceiveDirectDatagramAsync(token).AsTask();
                Task<int> reading = (revokeServer ? sink : source).ReadAsync(bytes, token).AsTask();
                Assert.Multiple(() =>
                {
                    Assert.That(admission.IsCompleted, Is.False);
                    Assert.That(datagram.IsCompleted, Is.False);
                    Assert.That(reading.IsCompleted, Is.False);
                });
                await invalidated.CancelAsync().ConfigureAwait(false);
                await Assert.CatchAsync<OperationCanceledException>(async () =>
                    await admission.ConfigureAwait(false)).ConfigureAwait(false);
                await Assert.CatchAsync<OperationCanceledException>(async () =>
                    await datagram.ConfigureAwait(false)).ConfigureAwait(false);
                await Assert.CatchAsync<OperationCanceledException>(async () =>
                    await reading.ConfigureAwait(false)).ConfigureAwait(false);
                Assert.Multiple(() =>
                {
                    Assert.That(revoked.IsAvailable, Is.False);
                    Assert.That(revoked.AreDirectStreamsAvailable, Is.False);
                    Assert.That(revoked.MaxDirectDatagramSize, Is.Zero);
                });
            }, expectBaselineUsable: false,
                clientInvalidated: revokeServer ? default : invalidated.Token,
                serverInvalidated: revokeServer ? invalidated.Token : default).ConfigureAwait(false);
        }

        /// <summary>
        /// Registration is inert until explicit connection establishment and reuses the injected application services.
        /// </summary>
        [Test]
        public async Task NativeUascBuilderRegistrationCreatesNoNativeOwners()
        {
            var services = new ServiceCollection();
            services.AddSingleton(m_backend!);
            services.AddSingleton(m_environment!.ClientBudget);
            services.AddOpcUa().AddNativeQuicUasc(m_environment.BindingOptions);
            using ServiceProvider provider = services.BuildServiceProvider();
            NativeQuicUascTransport transport = provider.GetRequiredService<NativeQuicUascTransport>();
            Assert.That(provider.GetRequiredService<NativeQuicUascTransport>(), Is.SameAs(transport));
            await NativeFlowTestEnvironment.AssertNoNativeOwnershipAsync(m_backend!).ConfigureAwait(false);
            DataChannelResourceAssertions.AreReleased(m_environment.ClientBudget.Usage);
        }

        [Test]
        public async Task TlsDatagramsExchangeExactBytes()
        {
            await using NativePeerPair pair = await ConnectPairAsync().ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(pair.Client.TlsPeer.LocalDatagramsEnabled, Is.True);
                Assert.That(pair.Client.TlsPeer.PeerDatagramsEnabled, Is.True);
                Assert.That(pair.Server.TlsPeer.LocalDatagramsEnabled, Is.True);
                Assert.That(pair.Server.TlsPeer.PeerDatagramsEnabled, Is.True);
                Assert.That(pair.Client.MaxDatagramSize, Is.GreaterThanOrEqualTo(3));
                Assert.That(pair.Server.MaxDatagramSize, Is.GreaterThanOrEqualTo(3));
            });
            await ExchangeDatagramAsync(pair.Client, pair.Server, Bytes(1, 0, 255)).ConfigureAwait(false);
            await ExchangeDatagramAsync(pair.Server, pair.Client, Bytes(254, 0, 2)).ConfigureAwait(false);
        }

        [Test]
        public async Task MutualTlsStillRequiresApplicationEvidence()
        {
            await using NativePeerPair pair = await NativePeerPair.CreateAsync(m_environment!).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(pair.Client.Peer.MutualTlsAuthenticated, Is.True);
                Assert.That(pair.Server.Peer.MutualTlsAuthenticated, Is.True);
                Assert.That(pair.Client.Credentials.Credentials.IsTransportOnly, Is.False);
                Assert.That(pair.Server.Credentials.Credentials.IsTransportOnly, Is.False);
                Assert.That(pair.Client.VerifiedPeerCertificateChain, Is.Not.Empty);
                Assert.That(pair.Server.VerifiedPeerCertificateChain, Is.Not.Empty);
                Assert.Throws<InvalidOperationException>(() => _ = pair.Client.TlsPeer);
                Assert.Throws<InvalidOperationException>(() => _ = pair.Server.TlsPeer);
            });
        }

        [Test]
        public async Task TlsStreamBudgetRecoversAfterRelease()
        {
            await using NativePeerPair pair = await ConnectPairAsync(s_settings with { MaxStreams = 1 })
                .ConfigureAwait(false);
            await using NativeQuicStream first = await pair.Client.OpenStreamAsync().ConfigureAwait(false);
            Assert.That((await first.SendAsync(Bytes(31), flags: 1).ConfigureAwait(false))!.Status,
                Is.EqualTo(NativeQuicStatus.Success));
            await using NativeQuicStream firstRemote = await pair.Server.AcceptStreamAsync().ConfigureAwait(false);
            Assert.That(await ReadPayloadAsync(firstRemote, 1, CancellationToken.None).ConfigureAwait(false),
                Is.EqualTo(new byte[] { 31 }));
            Assert.That((await firstRemote.SendAsync(Bytes(32), flags: 1).ConfigureAwait(false))!.Status,
                Is.EqualTo(NativeQuicStatus.Success));
            Assert.That(await ReadPayloadAsync(first, 1, CancellationToken.None).ConfigureAwait(false),
                Is.EqualTo(new byte[] { 32 }));
            NativeQuicTransportException? rejected = await Assert.ThrowsAsync<NativeQuicTransportException>(
                async () => await pair.Client.OpenStreamAsync().ConfigureAwait(false)).ConfigureAwait(false);
            Assert.That(rejected!.Completion, Is.Null, "The managed reservation rejects before native allocation.");
            Assert.That(pair.Client.TryGetStream(first.StreamId, out NativeQuicStream? retained), Is.True);
            Assert.That(retained, Is.SameAs(first));
            await first.DisposeAsync().ConfigureAwait(false);
            await firstRemote.DisposeAsync().ConfigureAwait(false);
            await using NativeQuicStream recovered = await pair.Client.OpenStreamAsync().ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(recovered.StreamId, Is.EqualTo(first.StreamId + 4));
                Assert.That(pair.Client.TryGetStream(first.StreamId, out _), Is.False);
                Assert.That(pair.Client.TryGetStream(recovered.StreamId, out NativeQuicStream? owned), Is.True);
                Assert.That(owned, Is.SameAs(recovered));
                Assert.That(pair.Client.IsClosed, Is.False);
            });
            Assert.That((await recovered.SendAsync(Bytes(5, 0, 6), flags: 1).ConfigureAwait(false))!.Status,
                Is.EqualTo(NativeQuicStatus.Success));
            await using NativeQuicStream remote = await pair.Server.AcceptStreamAsync().ConfigureAwait(false);
            Assert.That(await ReadPayloadAsync(remote, 3, CancellationToken.None).ConfigureAwait(false),
                Is.EqualTo(new byte[] { 5, 0, 6 }));
        }

        [Test]
        public async Task TlsOperationBudgetRecoversAfterCancelledReads()
        {
            await using NativePeerPair pair = await ConnectPairAsync(s_settings with { MaxPendingOperations = 4 })
                .ConfigureAwait(false);
            await using NativeQuicStream first = await pair.Client.OpenStreamAsync().ConfigureAwait(false);
            await using NativeQuicStream second = await pair.Client.OpenStreamAsync().ConfigureAwait(false);
            await using NativeQuicStream third = await pair.Client.OpenStreamAsync().ConfigureAwait(false);
            using var cancel = new CancellationTokenSource();
            // The fourth slot is owned by the connection's native WaitClosed monitor.
            Task<NativeQuicCompletion>[] reads =
            [
                first.ReadAsync(cancel.Token).AsTask(),
                second.ReadAsync(cancel.Token).AsTask(),
                third.ReadAsync(cancel.Token).AsTask()
            ];
            try
            {
                Assert.That(Array.TrueForAll(reads, task => !task.IsCompleted), Is.True);
                NativeQuicTransportException? rejected = await Assert.ThrowsAsync<NativeQuicTransportException>(
                    async () => await pair.Client.ReceiveDatagramAsync().ConfigureAwait(false)).ConfigureAwait(false);
                Assert.That(rejected!.Completion, Is.Null);
            }
            finally
            {
                await cancel.CancelAsync().ConfigureAwait(false);
                NativeQuicCompletion[] drained = await Task.WhenAll(reads).ConfigureAwait(false);
                Assert.That(Array.ConvertAll(drained, result => result.Status),
                    Is.All.EqualTo(NativeQuicStatus.Cancelled));
            }
            await ExchangeDatagramAsync(pair.Server, pair.Client, Bytes(7, 0, 8)).ConfigureAwait(false);
            Assert.That(pair.Client.Closed.IsCancellationRequested, Is.False);
        }

        [Test]
        public async Task TlsPendingStreamReservationRecoversAfterCancellation()
        {
            await using NativePeerPair pair = await ConnectPairAsync(s_settings with { MaxStreams = 1 })
                .ConfigureAwait(false);
            using var cancel = new CancellationTokenSource();
            Task<NativeQuicStream> accepting = pair.Server.AcceptStreamAsync(cancellationToken: cancel.Token).AsTask();
            try
            {
                Assert.That(accepting.IsCompleted, Is.False);
                NativeQuicTransportException? rejected = await Assert.ThrowsAsync<NativeQuicTransportException>(
                    async () => await pair.Server.OpenStreamAsync().ConfigureAwait(false)).ConfigureAwait(false);
                Assert.That(rejected!.Completion, Is.Null);
            }
            finally
            {
                await cancel.CancelAsync().ConfigureAwait(false);
                NativeQuicTransportException? cancelled = await Assert.ThrowsAsync<NativeQuicTransportException>(
                    async () => await accepting.ConfigureAwait(false)).ConfigureAwait(false);
                Assert.That(cancelled!.Completion!.Status, Is.EqualTo(NativeQuicStatus.Cancelled));
            }
            await using NativeQuicStream recovered = await pair.Server.OpenStreamAsync().ConfigureAwait(false);
            Assert.That(recovered.StreamId, Is.EqualTo(1UL));
            Assert.That((await recovered.SendAsync(Bytes(81, 0), flags: 1).ConfigureAwait(false))!.Status,
                Is.EqualTo(NativeQuicStatus.Success));
            await using NativeQuicStream remote = await pair.Client.AcceptStreamAsync().ConfigureAwait(false);
            Assert.That(await ReadPayloadAsync(remote, 2, CancellationToken.None).ConfigureAwait(false),
                Is.EqualTo(new byte[] { 81, 0 }));
            Assert.That(pair.Server.IsClosed, Is.False);
        }

        [Test]
        public async Task TlsAdmissionCancellationDoesNotBecomePolicyInvalidation()
        {
            using var operation = new CancellationTokenSource();
            await using NativePeerPair pair = await ConnectPairAsync(admissionCancellation: operation.Token)
                .ConfigureAwait(false);
            await operation.CancelAsync().ConfigureAwait(false);
            await ExchangeDatagramAsync(pair.Client, pair.Server, Bytes(61, 0, 62)).ConfigureAwait(false);
            await ExchangeDatagramAsync(pair.Server, pair.Client, Bytes(63, 0, 64)).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(pair.Listener.IsListening, Is.True);
                Assert.That(pair.Client.SecurityInvalidated, Is.False);
                Assert.That(pair.Server.SecurityInvalidated, Is.False);
                Assert.That(pair.Client.Closed.IsCancellationRequested, Is.False);
                Assert.That(pair.Server.Closed.IsCancellationRequested, Is.False);
            });
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task TlsExplicitCloseDrainsStreamsAndOperationsWithoutSecurityInvalidation(bool closeServer)
        {
            await using NativePeerPair pair = await ConnectPairAsync().ConfigureAwait(false);
            NativeQuicConnection target = closeServer ? pair.Server : pair.Client;
            await using NativeQuicStream stream = await target.OpenStreamAsync().ConfigureAwait(false);
            Task<NativeQuicCompletion> reading = stream.ReadAsync().AsTask();
            Task<NativeQuicCompletion> datagram = target.ReceiveDatagramAsync().AsTask();
            Assert.That(reading.IsCompleted, Is.False);
            Assert.That(datagram.IsCompleted, Is.False);
            await target.CloseAsync(73).ConfigureAwait(false);
            NativeQuicCompletion[] drained = await Task.WhenAll(reading, datagram).ConfigureAwait(false);
            await target.DisposeAsync().ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(target.Completion.IsCompletedSuccessfully, Is.True);
                Assert.That(target.SecurityInvalidated, Is.False);
                Assert.That(target.IsClosed, Is.True);
                Assert.That(stream.IsClosed, Is.True);
                Assert.That(stream.Closed.IsCancellationRequested, Is.True);
                Assert.That(target.TryGetStream(stream.StreamId, out _), Is.False);
                Assert.That(Array.ConvertAll(drained, result => result.Status),
                    Is.All.AnyOf(NativeQuicStatus.Cancelled, NativeQuicStatus.Closed));
                Assert.That(pair.Listener.IsListening, Is.True);
            });
            await Assert.ThrowsAsync<ObjectDisposedException>(
                async () => await target.OpenStreamAsync().ConfigureAwait(false)).ConfigureAwait(false);
            await Assert.ThrowsAsync<ObjectDisposedException>(
                async () => await stream.ReadAsync().ConfigureAwait(false)).ConfigureAwait(false);
        }

        [Test]
        public async Task CancelledAcceptAndStreamAdmissionLeaveOwnersUsable()
        {
            await using NativeQuicListener listener = await ListenAsync().ConfigureAwait(false);
            using (var cancel = new CancellationTokenSource())
            {
                Task<NativeQuicConnection> accepting = listener.AcceptAsync(cancel.Token).AsTask();
                Assert.That(accepting.IsCompleted, Is.False);
                await Assert.ThrowsAsync<InvalidOperationException>(
                    async () => await listener.AcceptAsync().ConfigureAwait(false)).ConfigureAwait(false);
                await cancel.CancelAsync().ConfigureAwait(false);
                NativeQuicTransportException? rejected = await Assert.ThrowsAsync<NativeQuicTransportException>(
                    async () => await accepting.ConfigureAwait(false)).ConfigureAwait(false);
                Assert.That(rejected!.Completion!.Status, Is.EqualTo(NativeQuicStatus.Cancelled));
                Assert.That(listener.IsListening, Is.True);
            }
            await using NativePeerPair pair = await ConnectPairAsync(listener: listener).ConfigureAwait(false);
            using (var cancel = new CancellationTokenSource())
            {
                Task<NativeQuicStream> accepting = pair.Server.AcceptStreamAsync(cancellationToken: cancel.Token)
                    .AsTask();
                Assert.That(accepting.IsCompleted, Is.False);
                await cancel.CancelAsync().ConfigureAwait(false);
                NativeQuicTransportException? rejected = await Assert.ThrowsAsync<NativeQuicTransportException>(
                    async () => await accepting.ConfigureAwait(false)).ConfigureAwait(false);
                Assert.That(rejected!.Completion!.Status, Is.EqualTo(NativeQuicStatus.Cancelled));
            }
            await using NativeQuicStream local = await pair.Client.OpenStreamAsync().ConfigureAwait(false);
            Assert.That((await local.SendAsync(Bytes(14, 0, 15)).ConfigureAwait(false))!.Status,
                Is.EqualTo(NativeQuicStatus.Success));
            await using NativeQuicStream remote = await pair.Server.AcceptStreamAsync().ConfigureAwait(false);
            using (var cancel = new CancellationTokenSource())
            {
                Task<NativeQuicCompletion> reading = local.ReadAsync(cancel.Token).AsTask();
                Assert.That(reading.IsCompleted, Is.False);
                await cancel.CancelAsync().ConfigureAwait(false);
                Assert.That((await reading.ConfigureAwait(false)).Status, Is.EqualTo(NativeQuicStatus.Cancelled));
            }
            Assert.That(await ReadPayloadAsync(remote, 3, CancellationToken.None).ConfigureAwait(false),
                Is.EqualTo(new byte[] { 14, 0, 15 }));
            Assert.That((await remote.SendAsync(Bytes(99, 0), flags: 1).ConfigureAwait(false))!.Status,
                Is.EqualTo(NativeQuicStatus.Success));
            Assert.That(await ReadPayloadAsync(local, 2, CancellationToken.None).ConfigureAwait(false),
                Is.EqualTo(new byte[] { 99, 0 }));
            Assert.Multiple(() =>
            {
                Assert.That(pair.Client.IsClosed, Is.False);
                Assert.That(pair.Server.IsClosed, Is.False);
                Assert.That(pair.Client.SecurityInvalidated, Is.False);
                Assert.That(pair.Server.SecurityInvalidated, Is.False);
                Assert.That(listener.IsListening, Is.True);
            });
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task TlsCredentialInvalidationClosesAssociatedOwners(bool invalidateServer)
        {
            using var invalidated = new CancellationTokenSource();
            await using NativePeerPair pair = await ConnectPairAsync(
                clientInvalidated: invalidateServer ? default : invalidated.Token,
                serverInvalidated: invalidateServer ? invalidated.Token : default).ConfigureAwait(false);
            NativeQuicConnection target = invalidateServer ? pair.Server : pair.Client;
            await using NativeQuicStream stream = await target.OpenStreamAsync().ConfigureAwait(false);
            Task<NativeQuicCompletion> pending = target.ReceiveDatagramAsync().AsTask();
            await invalidated.CancelAsync().ConfigureAwait(false);
            await Task.WhenAll(pair.Client.Completion, pair.Server.Completion)
                .WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            if (invalidateServer)
            {
                await pair.Listener.Completion.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            }
            NativeQuicCompletion drained = await pending.ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(target.SecurityInvalidated, Is.True);
                Assert.That(target.Closed.IsCancellationRequested, Is.True);
                Assert.That(pair.Client.IsClosed, Is.True);
                Assert.That(pair.Server.IsClosed, Is.True);
                Assert.That(stream.IsClosed, Is.True);
                Assert.That(target.TryGetStream(stream.StreamId, out _), Is.False);
                Assert.That(drained.Status, Is.AnyOf(NativeQuicStatus.Cancelled, NativeQuicStatus.Closed));
                Assert.That(pair.Listener.IsListening, Is.EqualTo(!invalidateServer));
            });
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task TlsCertificateExpiryClosesAssociation(bool expireServer)
        {
            var clock = new TlsPolicyClock();
            await using NativePeerPair pair = await ConnectPairAsync(
                clientClock: expireServer ? null : clock, serverClock: expireServer ? clock : null)
                .ConfigureAwait(false);
            NativeQuicConnection target = expireServer ? pair.Server : pair.Client;
            await ExchangeDatagramAsync(pair.Client, pair.Server, Bytes(3, 2, 1)).ConfigureAwait(false);
            clock.Advance(TimeSpan.FromHours(3));
            await Task.WhenAll(pair.Client.Completion, pair.Server.Completion)
                .WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            if (expireServer)
            {
                await pair.Listener.Completion.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            }
            Assert.Multiple(() =>
            {
                Assert.That(target.SecurityInvalidated, Is.True);
                Assert.That(target.Closed.IsCancellationRequested, Is.True);
                Assert.That(pair.Client.IsClosed, Is.True);
                Assert.That(pair.Server.IsClosed, Is.True);
                Assert.That(pair.Listener.IsListening, Is.EqualTo(!expireServer));
            });
        }

        [TestCase("hostname")]
        [TestCase("root")]
        [TestCase("alpn")]
        public async Task TlsHandshakeRejectsWrongHostnameRootAndAlpn(string mismatch)
        {
            await using NativeQuicListener listener = await ListenAsync().ConfigureAwait(false);
            using Certificate otherRoot = DefaultCertificateFactory.Instance.CreateCertificate("CN=Untrusted TLS root")
                .SetCAConstraint().SetRSAKeySize(2048).CreateForRSA();
            NativeQuicCredentials credentials = mismatch == "root"
                ? NativeQuicCredentials.ForTlsClient(new ByteString(otherRoot.RawData))
                : m_clientCredentials!;
            NativeQuicTransportException? rejected = await Assert.ThrowsAsync<NativeQuicTransportException>(
                async () => await NativeQuicConnection.ConnectTlsAsync(
                    m_backend!, credentials, listener.Address,
                    mismatch == "alpn" ? "different-alpn" : kAlpn,
                    mismatch == "hostname" ? "wrong.example.invalid" : "localhost", s_settings).ConfigureAwait(false))
                .ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(rejected!.Completion, Is.Not.Null);
                Assert.That(rejected.Completion!.Status, Is.Not.EqualTo(NativeQuicStatus.Success));
                Assert.That(listener.IsListening, Is.True);
            });
            // A rejected handshake must not poison the next valid admission.
            await using NativePeerPair recovered = await ConnectPairAsync(listener: listener).ConfigureAwait(false);
            await ExchangeDatagramAsync(recovered.Client, recovered.Server, Bytes(20, 0, 21)).ConfigureAwait(false);
        }

        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public async Task TlsAdmissionRejectsWrongRoleAndPolicyBeforeNativeAllocation(bool listener, bool mutualTls)
        {
            var backend = new Mock<INativeQuicBackend>(MockBehavior.Strict);
            NativeQuicCredentials credentials = mutualTls
                ? new NativeQuicCredentials(new ByteString(m_certificate!.RawData), default, m_key!,
                    new ByteString(m_environment!.Root.RawData), "urn:test:application")
                : listener ? m_clientCredentials! : m_serverCredentials!;
            ArgumentException? rejected = listener
                ? await Assert.ThrowsAsync<ArgumentException>(async () => await NativeQuicListener.ListenTlsAsync(
                    backend.Object, credentials, "127.0.0.1:0", kAlpn).ConfigureAwait(false)).ConfigureAwait(false)
                : await Assert.ThrowsAsync<ArgumentException>(async () => await NativeQuicConnection.ConnectTlsAsync(
                    backend.Object, credentials, "127.0.0.1:1", kAlpn, "localhost").ConfigureAwait(false))
                    .ConfigureAwait(false);
            Assert.That(rejected!.ParamName, Is.EqualTo("credentials"));
            backend.VerifyNoOtherCalls();
        }

        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public async Task TlsAdmissionRejectsPrecancelledCallsWithoutNativeAllocation(bool listener, bool invalidate)
        {
            var backend = new Mock<INativeQuicBackend>(MockBehavior.Strict);
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            CancellationToken operation = invalidate ? default : cancelled.Token;
            CancellationToken policy = invalidate ? cancelled.Token : default;
            if (listener)
            {
                await Assert.ThrowsAsync<OperationCanceledException>(
                    async () => await NativeQuicListener.ListenTlsAsync(
                        backend.Object, m_serverCredentials!, "127.0.0.1:0", kAlpn,
                        credentialsInvalidated: policy, cancellationToken: operation).ConfigureAwait(false))
                    .ConfigureAwait(false);
            }
            else
            {
                await Assert.ThrowsAsync<OperationCanceledException>(
                    async () => await NativeQuicConnection.ConnectTlsAsync(
                        backend.Object, m_clientCredentials!, "127.0.0.1:1", kAlpn, "localhost",
                        credentialsInvalidated: policy, cancellationToken: operation).ConfigureAwait(false))
                    .ConfigureAwait(false);
            }
            backend.VerifyNoOtherCalls();
        }

        [Test]
        public async Task TlsListenerRejectsExpiredCertificateBeforeNativeAllocation()
        {
            var backend = new Mock<INativeQuicBackend>(MockBehavior.Strict);
            var clock = new TlsPolicyClock();
            clock.Advance(TimeSpan.FromHours(3));
            await Assert.ThrowsAsync<AuthenticationException>(
                async () => await NativeQuicListener.ListenTlsAsync(
                    backend.Object, m_serverCredentials!, "127.0.0.1:0", kAlpn, timeProvider: clock)
                    .ConfigureAwait(false)).ConfigureAwait(false);
            backend.VerifyNoOtherCalls();
        }

        [TestCase(-1)]
        [TestCase(0)]
        [TestCase(1)]
        public async Task TlsListenerEnforcesExactCertificateExpiryBoundary(int secondsAfterExpiry)
        {
            DateTimeOffset expiry = new(m_certificate!.NotAfter.ToUniversalTime());
            var clock = new TlsPolicyClock(expiry.AddSeconds(secondsAfterExpiry));
            if (secondsAfterExpiry < 0)
            {
                await using NativeQuicListener listener = await ListenAsync(clock: clock).ConfigureAwait(false);
                Assert.That(listener.IsListening, Is.True);
                Assert.That(listener.Address, Does.StartWith("127.0.0.1:"));
                return;
            }
            var backend = new Mock<INativeQuicBackend>(MockBehavior.Strict);
            await Assert.ThrowsAsync<AuthenticationException>(
                async () => await NativeQuicListener.ListenTlsAsync(
                    backend.Object, m_serverCredentials!, "127.0.0.1:0", kAlpn, timeProvider: clock)
                    .ConfigureAwait(false)).ConfigureAwait(false);
            backend.VerifyNoOtherCalls();
        }

        [Test]
        public async Task MutualTlsListenerDoesNotDowngradeForCertificateFreeClient()
        {
            await using NativeQuicListener listener = await NativeQuicListener.ListenAsync(
                m_backend!, m_environment!.ServerCredentials, "127.0.0.1:0", kAlpn, s_settings)
                .ConfigureAwait(false);
            await Assert.ThrowsAsync<NativeQuicTransportException>(
                async () => await NativeQuicConnection.ConnectTlsAsync(
                    m_backend!, m_clientCredentials!, listener.Address, kAlpn, "localhost", s_settings)
                    .ConfigureAwait(false)).ConfigureAwait(false);
            Assert.That(listener.IsListening, Is.True);
        }

        /// <summary>
        /// Exercises provider I/O on actual native peers and checks all shared capacity after owner disposal.
        /// </summary>
        private async Task WithNativeUascPairAsync(
            Func<NativeQuicUascConnection, NativeQuicUascConnection, CancellationToken, Task> exercise,
            QuicDataChannelOptions? options = null,
            bool expectBaselineUsable = true,
            INativeQuicBackend? clientBackend = null,
            CancellationToken clientInvalidated = default,
            CancellationToken serverInvalidated = default)
        {
            options ??= m_environment!.BindingOptions;
            var serverTransport = new NativeQuicUascTransport(m_backend!, m_environment!.ServerBudget, options);
            var clientTransport = new NativeQuicUascTransport(
                clientBackend ?? m_backend!, m_environment.ClientBudget, options);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            await using NativeQuicUascListener listener = await serverTransport.ListenAsync(
                new Uri("opc.quic://127.0.0.1:0/uasc?selection=providers"), m_serverCredentials!,
                credentialsInvalidated: serverInvalidated,
                cancellationToken: timeout.Token).ConfigureAwait(false);
            await using (NativeQuicUascConnection client = await clientTransport.ConnectAsync(
                listener.EndpointUrl, m_clientCredentials!, credentialsInvalidated: clientInvalidated,
                cancellationToken: timeout.Token).ConfigureAwait(false))
            {
                Task<NativeQuicUascConnection> accepting = listener.AcceptAsync(timeout.Token).AsTask();
                await using Stream clientBaseline = await client.ClaimStreamAsync(listener.EndpointUrl, timeout.Token)
                    .ConfigureAwait(false);
                await clientBaseline.WriteAsync(new byte[] { 0x48 }, timeout.Token).ConfigureAwait(false);
                await using NativeQuicUascConnection server = await accepting.ConfigureAwait(false);
                await using Stream serverBaseline = await server.ClaimStreamAsync(listener.EndpointUrl, timeout.Token)
                    .ConfigureAwait(false);
                byte[] first = new byte[1];
                await serverBaseline.ReadExactlyAsync(first, timeout.Token).ConfigureAwait(false);
                await exercise(client, server, timeout.Token).ConfigureAwait(false);
                if (expectBaselineUsable)
                {
                    await serverBaseline.WriteAsync(new byte[] { 0x41 }, timeout.Token).ConfigureAwait(false);
                    await clientBaseline.ReadExactlyAsync(first, timeout.Token).ConfigureAwait(false);
                    Assert.That(first[0], Is.EqualTo(0x41), "Provider refusal must not replace or close the baseline.");
                }
            }
            DataChannelResourceAssertions.AreReleased(m_environment.ClientBudget.Usage);
            DataChannelResourceAssertions.AreReleased(m_environment.ServerBudget.Usage);
        }

        private ValueTask<NativeQuicListener> ListenAsync(
            NativeQuicConnectionSettings? settings = null,
            TimeProvider? clock = null,
            CancellationToken invalidated = default,
            CancellationToken admissionCancellation = default)
        {
            return NativeQuicListener.ListenTlsAsync(
                m_backend!, m_serverCredentials!, "127.0.0.1:0", kAlpn, settings ?? s_settings,
                clock, invalidated, admissionCancellation);
        }

        private async ValueTask<NativePeerPair> ConnectPairAsync(
            NativeQuicConnectionSettings? settings = null,
            NativeQuicListener? listener = null,
            TimeProvider? clientClock = null,
            TimeProvider? serverClock = null,
            CancellationToken clientInvalidated = default,
            CancellationToken serverInvalidated = default,
            CancellationToken admissionCancellation = default)
        {
            settings ??= s_settings;
            listener ??= await ListenAsync(settings, serverClock, serverInvalidated, admissionCancellation)
                .ConfigureAwait(false);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            Task<NativeQuicConnection> accepted = listener.AcceptAsync(timeout.Token).AsTask();
            NativeQuicConnection? client = null;
            try
            {
                client = await NativeQuicConnection.ConnectTlsAsync(
                    m_backend!, m_clientCredentials!, listener.Address, kAlpn, "localhost", settings,
                    clientClock, clientInvalidated, admissionCancellation.CanBeCanceled ? admissionCancellation : timeout.Token)
                    .ConfigureAwait(false);
                NativeQuicConnection server = await accepted.ConfigureAwait(false);
                return new NativePeerPair(listener, client, server);
            }
            catch
            {
                await timeout.CancelAsync().ConfigureAwait(false);
                await listener.DisposeAsync().ConfigureAwait(false);
                if (client != null)
                {
                    await client.DisposeAsync().ConfigureAwait(false);
                }
                await ((Task)accepted).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
                if (accepted.IsCompletedSuccessfully)
                {
                    await (await accepted.ConfigureAwait(false)).DisposeAsync().ConfigureAwait(false);
                }
                throw;
            }
        }

        private static async Task ExchangeDatagramAsync(
            NativeQuicConnection sender, NativeQuicConnection receiver, ByteString payload)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            Task<NativeQuicCompletion> receiving = receiver.ReceiveDatagramAsync(timeout.Token).AsTask();
            NativeQuicCompletion? sent = await sender.SendDatagramAsync(payload, cancellationToken: timeout.Token)
                .ConfigureAwait(false);
            NativeQuicCompletion received = await receiving.ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(sent!.Status, Is.EqualTo(NativeQuicStatus.Success));
                Assert.That(received.Status, Is.EqualTo(NativeQuicStatus.Success));
                Assert.That(received.Payload, Is.EqualTo(payload));
                Assert.That(sender.IsClosed, Is.False);
                Assert.That(receiver.IsClosed, Is.False);
            });
        }

        private static async Task<byte[]> ReadPayloadAsync(
            NativeQuicStream stream, int length, CancellationToken cancellationToken)
        {
            var bytes = new List<byte>();
            while (bytes.Count < length)
            {
                NativeQuicCompletion read = await stream.ReadAsync(cancellationToken).ConfigureAwait(false);
                Assert.That(read.Status, Is.EqualTo(NativeQuicStatus.Success));
                Assert.That(read.Payload.Length, Is.GreaterThan(0));
                bytes.AddRange(read.Payload.Span.ToArray());
            }
            return [.. bytes];
        }

        private static ByteString Bytes(params byte[] bytes) => new(bytes);

        /// <summary>
        /// Advances policy UTC observations only, matching the existing native security fixture.
        /// </summary>
        private sealed class TlsPolicyClock(DateTimeOffset? fixedUtcNow = null) : TimeProvider
        {
            public override DateTimeOffset GetUtcNow()
            {
                return (fixedUtcNow ?? TimeProvider.System.GetUtcNow()) +
                    TimeSpan.FromTicks(Interlocked.Read(ref m_offset));
            }

            internal void Advance(TimeSpan amount)
            {
                Interlocked.Add(ref m_offset, amount.Ticks);
            }

            private long m_offset;
        }

        private const string kAlpn = "opcua-transport-tls-owner-test";
        private static readonly NativeQuicConnectionSettings s_settings = new()
        {
            OperationTimeout = TimeSpan.FromSeconds(10),
            HandshakeTimeout = TimeSpan.FromSeconds(3),
            CertificateRecheckInterval = TimeSpan.FromMilliseconds(20)
        };

        private INativeQuicBackend? m_backend;
        private NativeFlowTestEnvironment? m_environment;
        private Certificate? m_certificate;
        private NativeQuicPrivateKey? m_key;
        private NativeQuicCredentials? m_clientCredentials;
        private NativeQuicCredentials? m_serverCredentials;
    }
}
