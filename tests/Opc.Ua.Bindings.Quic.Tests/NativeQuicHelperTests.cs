// Copyright (c) OPC Foundation. Licensed under the OPC Foundation MIT License 1.00.

using System;
using System.Collections.Generic;
using System.Linq;
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
    public sealed partial class QuicNativeFlowTests
    {
        /// <summary>
        /// The helper applies a late reset code to a blocked native write, drains its real partial result,
        /// and retains the independent receive half and actual local reset evidence.
        /// </summary>
        [Test]
        public async Task LateResetInterruptsCommittedWriteAndPreservesReceiveHalf()
        {
            await using NativePeerPair pair = await NativePeerPair.CreateAsync(m_environment!).ConfigureAwait(false);
            await using NativeQuicStream source = await pair.Client.OpenStreamAsync().ConfigureAwait(false);
            Assert.That((await source.SendAsync(Bytes(1, 2, 3, 4), flags: 2).ConfigureAwait(false))!.Status,
                Is.EqualTo(NativeQuicStatus.Success));
            await using NativeQuicStream remote = await pair.Server.AcceptStreamAsync().ConfigureAwait(false);
            Assert.That((await source.SendAsync(new ByteString(new byte[60000])).ConfigureAwait(false))!.Status,
                Is.EqualTo(NativeQuicStatus.Success));
            Task<NativeQuicCompletion?> sending = source.SendAsync(
                new ByteString(new byte[65536]), tryCommit: () => true).AsTask();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            NativeQuicCompletion offset;
            do
            {
                offset = await source.GetSentOffsetAsync(timeout.Token).ConfigureAwait(false);
                Assert.That(offset.Status, Is.EqualTo(NativeQuicStatus.Success));
                if (offset.FinalSize < 65536)
                {
                    await Task.Delay(1, timeout.Token).ConfigureAwait(false);
                }
            }
            while (offset.FinalSize < 65536);
            Assert.That(sending.IsCompleted, Is.False, "The test must reach a genuinely backpressured native write.");
            NativeQuicCompletion reset = await source.ResetSendAsync(91).AsTask().WaitAsync(timeout.Token)
                .ConfigureAwait(false);
            NativeQuicCompletion? partial = await sending.ConfigureAwait(false);
            NativeQuicCompletion local = await source.WaitResetSentAsync(timeout.Token).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(reset.Status, Is.EqualTo(NativeQuicStatus.Success));
                Assert.That(partial!.Status, Is.EqualTo(NativeQuicStatus.Cancelled));
                Assert.That(partial.NativeStatus, Is.EqualTo(NativeQuicStatus.Closed));
                Assert.That(partial.BytesTransferred, Is.EqualTo(5532));
                Assert.That(partial.ErrorCode, Is.EqualTo(91));
                Assert.That(local.Status, Is.EqualTo(NativeQuicStatus.Success));
                Assert.That(local.FinalSize, Is.EqualTo(65536));
                Assert.That(local.ReliableSize, Is.EqualTo(4));
                Assert.That(local.ResetFrameType, Is.EqualTo(0x24));
                Assert.That(local.ErrorCode, Is.EqualTo(91));
                Assert.That(pair.Client.IsClosed, Is.False);
            });
            var received = new List<byte>();
            NativeQuicCompletion read;
            do
            {
                read = await remote.ReadAsync(timeout.Token).ConfigureAwait(false);
                received.AddRange(read.Payload.Span.ToArray());
            }
            while (read.ReadOutcome != 2);
            Assert.Multiple(() =>
            {
                Assert.That(received.Take(4), Is.EqualTo(new byte[] { 1, 2, 3, 4 }));
                Assert.That(read.FinalSize, Is.EqualTo(local.FinalSize));
                Assert.That(read.ReliableSize, Is.EqualTo(local.ReliableSize));
                Assert.That(read.ErrorCode, Is.EqualTo(91));
            });
            Assert.That((await remote.SendAsync(Bytes(8, 7), flags: 1).ConfigureAwait(false))!.Status,
                Is.EqualTo(NativeQuicStatus.Success));
            NativeQuicCompletion opposite = await source.ReadAsync(timeout.Token).ConfigureAwait(false);
            Assert.That(opposite.Payload.Span.ToArray(), Is.EqualTo(new byte[] { 8, 7 }));
        }

        /// <summary>
        /// A typed STOP_SENDING preserves the opposite native direction and is observable without a pending write.
        /// </summary>
        [Test]
        public async Task StopReceivePreservesOppositeDirectionAndActualPeerError()
        {
            await using NativePeerPair pair = await NativePeerPair.CreateAsync(m_environment!).ConfigureAwait(false);
            await using NativeQuicStream source = await pair.Client.OpenStreamAsync().ConfigureAwait(false);
            Assert.That((await source.SendAsync(Bytes(7)).ConfigureAwait(false))!.Status,
                Is.EqualTo(NativeQuicStatus.Success));
            await using NativeQuicStream receiver = await pair.Server.AcceptStreamAsync().ConfigureAwait(false);
            Assert.That((await receiver.StopReceiveAsync(37).ConfigureAwait(false)).Status,
                Is.EqualTo(NativeQuicStatus.Success));
            NativeQuicCompletion stopped = await source.WaitStoppedAsync().ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(stopped.Status, Is.EqualTo(NativeQuicStatus.Success));
                Assert.That(stopped.ErrorCode, Is.EqualTo(37));
                Assert.That(stopped.Remote, Is.True);
                Assert.That(pair.Client.IsClosed, Is.False);
                Assert.That(pair.Server.IsClosed, Is.False);
            });
            Assert.That((await receiver.SendAsync(Bytes(11, 12), flags: 1).ConfigureAwait(false))!.Status,
                Is.EqualTo(NativeQuicStatus.Success));
            NativeQuicCompletion reply = await source.ReadAsync().ConfigureAwait(false);
            Assert.That(reply.Payload.Span.ToArray(), Is.EqualTo(new byte[] { 11, 12 }));
        }

        /// <summary>
        /// Local reset and peer STOP preserve the established reliable prefix.
        /// </summary>
        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public async Task LocalResetAndPeerStopReportActualReliableBoundary(bool serverSends, bool peerStops)
        {
            const ulong errorCode = 0x52E4A40FA8E3;
            ByteString prefix = Bytes(0x40, 0x41, 0);
            ByteString body = new(Enumerable.Repeat((byte)0xA5, 2138).ToArray());
            await using NativePeerPair pair = await NativePeerPair.CreateAsync(m_environment!).ConfigureAwait(false);
            NativeQuicConnection sender = serverSends ? pair.Server : pair.Client;
            NativeQuicConnection recipient = serverSends ? pair.Client : pair.Server;
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await using NativeQuicStream source = await sender.OpenStreamAsync(cancellationToken: timeout.Token)
                .ConfigureAwait(false);
            NativeQuicCompletion? prefixSent = await source.SendAsync(
                prefix, flags: 2, cancellationToken: timeout.Token).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(prefixSent!.Status, Is.EqualTo(NativeQuicStatus.Success));
                Assert.That(prefixSent.BytesTransferred, Is.EqualTo(prefix.Length));
            });
            await using NativeQuicStream receiver = await recipient.AcceptStreamAsync(cancellationToken: timeout.Token)
                .ConfigureAwait(false);
            Assert.That((await receiver.ReadAsync(timeout.Token).ConfigureAwait(false)).Payload, Is.EqualTo(prefix));
            NativeQuicCompletion? bodySent = await source.SendAsync(body, cancellationToken: timeout.Token)
                .ConfigureAwait(false);
            Assert.That(bodySent!.Status, Is.EqualTo(NativeQuicStatus.Success));
            var received = new List<byte>();
            while (received.Count < body.Length)
            {
                NativeQuicCompletion part = await receiver.ReadAsync(timeout.Token).ConfigureAwait(false);
                Assert.Multiple(() =>
                {
                    Assert.That(part.Status, Is.EqualTo(NativeQuicStatus.Success));
                    Assert.That(part.Payload.Length, Is.GreaterThan(0));
                });
                received.AddRange(part.Payload.Span.ToArray());
            }
            Assert.That(received, Is.EqualTo(body.Span.ToArray()));
            Assert.That(
                (await receiver.SendAsync(Bytes(23), cancellationToken: timeout.Token).ConfigureAwait(false))!.Status,
                Is.EqualTo(NativeQuicStatus.Success));
            Assert.That((await source.ReadAsync(timeout.Token).ConfigureAwait(false)).Payload, Is.EqualTo(Bytes(23)));
            await Task.Delay(TimeSpan.FromMilliseconds(250), timeout.Token).ConfigureAwait(false);

            Task<NativeQuicCompletion> observing = source.WaitResetSentAsync(timeout.Token).AsTask();
            if (peerStops)
            {
                Task<NativeQuicCompletion> stopping = source.WaitStoppedAsync(timeout.Token).AsTask();
                Assert.That((await receiver.StopReceiveAsync(errorCode, timeout.Token).ConfigureAwait(false)).Status,
                    Is.EqualTo(NativeQuicStatus.Success));
                NativeQuicCompletion stopped = await stopping.ConfigureAwait(false);
                Assert.Multiple(() =>
                {
                    Assert.That(stopped.Status, Is.EqualTo(NativeQuicStatus.Success));
                    Assert.That(stopped.Remote, Is.True);
                    Assert.That(stopped.ErrorCode, Is.EqualTo(errorCode));
                });
            }
            else
            {
                Assert.That((await source.ResetSendAsync(errorCode).ConfigureAwait(false)).Status,
                    Is.EqualTo(NativeQuicStatus.Success));
            }
            NativeQuicCompletion local = await observing.ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(local.Status, Is.EqualTo(NativeQuicStatus.Success));
                Assert.That(local.NativeStatus, Is.EqualTo(NativeQuicStatus.Success));
                Assert.That(local.Remote, Is.False);
                Assert.That(local.ErrorCode, Is.EqualTo(errorCode));
                Assert.That(local.FinalSize, Is.EqualTo(prefix.Length + body.Length));
                Assert.That(local.ReliableSize, Is.EqualTo(prefix.Length));
                Assert.That(local.ResetFrameType, Is.EqualTo(0x24));
                Assert.That(sender.IsClosed, Is.False);
                Assert.That(recipient.IsClosed, Is.False);
            });
            Assert.That(
                (await receiver.SendAsync(Bytes(17, 18), cancellationToken: timeout.Token).ConfigureAwait(false))!.Status,
                Is.EqualTo(NativeQuicStatus.Success));
            Assert.That((await source.ReadAsync(timeout.Token).ConfigureAwait(false)).Payload,
                Is.EqualTo(Bytes(17, 18)));
        }

        /// <summary>
        /// A later authenticated peer STOP remains observable when a different local reset won first-code ownership.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task PeerStopRemainsObservableAfterLocalReset(bool serverSends)
        {
            const ulong localError = 0x52E4A40FA8E2;
            const ulong peerError = 0x52E4A40FA8E3;
            ByteString prefix = Bytes(0x40, 0x41, 0);
            await using NativePeerPair pair = await NativePeerPair.CreateAsync(m_environment!, relayPackets: true)
                .ConfigureAwait(false);
            NativeQuicConnection sender = serverSends ? pair.Server : pair.Client;
            NativeQuicConnection recipient = serverSends ? pair.Client : pair.Server;
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await using NativeQuicStream source = await sender.OpenStreamAsync(cancellationToken: timeout.Token)
                .ConfigureAwait(false);
            Assert.That(
                (await source.SendAsync(prefix, flags: 2, cancellationToken: timeout.Token).ConfigureAwait(false))!.Status,
                Is.EqualTo(NativeQuicStatus.Success));
            await using NativeQuicStream receiver = await recipient.AcceptStreamAsync(cancellationToken: timeout.Token)
                .ConfigureAwait(false);
            Assert.That((await receiver.ReadAsync(timeout.Token).ConfigureAwait(false)).Payload, Is.EqualTo(prefix));
            pair.Relay!.Hold(fromClient: !serverSends);
            Assert.That((await source.ResetSendAsync(localError).ConfigureAwait(false)).Status,
                Is.EqualTo(NativeQuicStatus.Success));
            NativeQuicCompletion local = await source.WaitResetSentAsync(timeout.Token).ConfigureAwait(false);
            await pair.Relay.WaitForHeldPacketAsync(timeout.Token).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(local.Status, Is.EqualTo(NativeQuicStatus.Success));
                Assert.That(local.Remote, Is.False);
                Assert.That(local.ErrorCode, Is.EqualTo(localError));
                Assert.That(local.FinalSize, Is.EqualTo(prefix.Length));
                Assert.That(local.ReliableSize, Is.EqualTo(prefix.Length));
                Assert.That(local.ResetFrameType, Is.EqualTo(0x24));
            });

            Task<NativeQuicCompletion> observing = source.WaitStoppedAsync(timeout.Token).AsTask();
            Assert.That((await receiver.StopReceiveAsync(peerError, timeout.Token).ConfigureAwait(false)).Status,
                Is.EqualTo(NativeQuicStatus.Success));
            NativeQuicCompletion stopped = await observing.ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(stopped.Status, Is.EqualTo(NativeQuicStatus.Success));
                Assert.That(stopped.NativeStatus, Is.EqualTo(NativeQuicStatus.Success));
                Assert.That(stopped.Remote, Is.True);
                Assert.That(stopped.ErrorCode, Is.EqualTo(peerError));
                Assert.That(sender.IsClosed, Is.False);
                Assert.That(recipient.IsClosed, Is.False);
            });
            NativeQuicCompletion legacy = await sender.Backend.StreamAsync(
                source.Handle, NativeQuicStreamOperation.WaitStopped,
                new NativeQuicRequest(TimeSpan.FromSeconds(1)), timeout.Token).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(legacy.Status, Is.EqualTo(NativeQuicStatus.Success));
                Assert.That(legacy.Remote, Is.False);
                Assert.That(legacy.ErrorCode, Is.EqualTo(localError));
            });
            await pair.Relay.ReleaseAsync(timeout.Token).ConfigureAwait(false);
            Assert.That(
                (await receiver.SendAsync(Bytes(31), cancellationToken: timeout.Token).ConfigureAwait(false))!.Status,
                Is.EqualTo(NativeQuicStatus.Success));
            Assert.That((await source.ReadAsync(timeout.Token).ConfigureAwait(false)).Payload, Is.EqualTo(Bytes(31)));
        }

        /// <summary>
        /// Per-connection stream admission refuses the adjacent over-limit allocation without leaking a handle.
        /// </summary>
        [Test]
        public async Task NativeStreamLimitRefusesWithoutAllocatingAnotherHandle()
        {
            await using NativePeerPair pair = await NativePeerPair.CreateAsync(
                m_environment!, m_environment!.BindingOptions.Native with { MaxStreams = 2 }).ConfigureAwait(false);
            await using NativeQuicStream first = await pair.Client.OpenStreamAsync().ConfigureAwait(false);
            await using NativeQuicStream second = await pair.Client.OpenStreamAsync().ConfigureAwait(false);
            NativeQuicCounts before = await m_backend!.GetCountsAsync().ConfigureAwait(false);
            await Assert.ThrowsAsync<NativeQuicTransportException>(async () =>
                await pair.Client.OpenStreamAsync().ConfigureAwait(false)).ConfigureAwait(false);
            NativeQuicCounts after = await m_backend.GetCountsAsync().ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(before.Streams, Is.EqualTo(2));
                Assert.That(after.Streams, Is.EqualTo(before.Streams));
                Assert.That(first.StreamId, Is.Not.EqualTo(second.StreamId));
                Assert.That(pair.Client.IsClosed, Is.False);
            });
        }

        /// <summary>
        /// Pending read owners count toward local native-operation admission and cancellation drains them.
        /// </summary>
        [Test]
        public async Task NativeOperationLimitCountsPendingReadsAndDrainsCancellation()
        {
            await using NativePeerPair pair = await NativePeerPair.CreateAsync(
                m_environment!, m_environment!.BindingOptions.Native with { MaxPendingOperations = 4 })
                .ConfigureAwait(false);
            await using NativeQuicStream first = await pair.Client.OpenStreamAsync().ConfigureAwait(false);
            await using NativeQuicStream second = await pair.Client.OpenStreamAsync().ConfigureAwait(false);
            await using NativeQuicStream third = await pair.Client.OpenStreamAsync().ConfigureAwait(false);
            using var cancellation = new CancellationTokenSource();
            Task<NativeQuicCompletion>[] reads =
            [
                first.ReadAsync(cancellation.Token).AsTask(),
                second.ReadAsync(cancellation.Token).AsTask(),
                third.ReadAsync(cancellation.Token).AsTask()
            ];
            NativeQuicCounts before = await m_backend!.GetCountsAsync().ConfigureAwait(false);
            await Assert.ThrowsAsync<NativeQuicTransportException>(async () =>
                await pair.Client.ReceiveDatagramAsync().ConfigureAwait(false)).ConfigureAwait(false);
            Assert.That((await m_backend.GetCountsAsync().ConfigureAwait(false)).Operations, Is.EqualTo(before.Operations));
            await cancellation.CancelAsync().ConfigureAwait(false);
            NativeQuicCompletion[] cancelled = await Task.WhenAll(reads).ConfigureAwait(false);
            Assert.That(cancelled.Select(result => result.Status), Is.All.EqualTo(NativeQuicStatus.Cancelled));
            Assert.That(pair.Client.IsClosed, Is.False);
        }

        /// <summary>
        /// A non-exportable registry key is refused explicitly without falling back to another identity or store.
        /// </summary>
        [Test]
        public async Task NonExportableSigningProviderIsRefusedBeforeNativeAdmission()
        {
            using CertificateEntry current = m_environment!.ClientManager.AcquireApplicationCertificateByType(
                ObjectTypeIds.RsaSha256ApplicationCertificateType)!;
            using RSA publicKey = current.Certificate.GetRSAPublicKey()!;
            var hardware = new Mock<RSA>();
            hardware.SetupGet(key => key.KeySize).Returns(2048);
            hardware.Setup(key => key.ExportParameters(false)).Returns(publicKey.ExportParameters(false));
            hardware.Setup(key => key.ExportParameters(true)).Throws(new CryptographicException("Non-exportable test key."));
            using Certificate nonExportable = current.Certificate.CopyWithDetachedPrivateKey(
                hardware.Object, ownsPrivateKey: false);
            await m_environment.ClientManager.UpdateApplicationCertificateAsync(
                ObjectTypeIds.RsaSha256ApplicationCertificateType, nonExportable, current.IssuerChain).ConfigureAwait(false);
            NotSupportedException? exception = await Assert.ThrowsAsync<NotSupportedException>(async () =>
                await m_environment.ClientCredentials.AcquireAsync().ConfigureAwait(false)).ConfigureAwait(false);
            Assert.That(exception!.InnerException, Is.TypeOf<CryptographicException>());
            await NativeFlowTestEnvironment.AssertNoNativeOwnershipAsync(m_backend!).ConfigureAwait(false);
        }

        /// <summary>
        /// Direct and fluent DI creation use the supplied optional factory before HELLO, not a legacy UASC adapter.
        /// </summary>
        [Test]
        public async Task DirectAndInjectedFactoriesExposeOnlyNativeFlowApis()
        {
            var counted = new CountingContextFactory(m_environment!.ClientFactory);
            var services = new ServiceCollection();
            services.AddSingleton(m_backend!);
            services.AddSingleton<INativeQuicCredentialProvider>(m_environment.ClientCredentials);
            services.AddSingleton<IDataChannelContextFactory>(counted);
            services.AddOpcUa().AddQuicDataChannels(m_environment.BindingOptions);
            using ServiceProvider provider = services.BuildServiceProvider();
            QuicDataChannelTransport transport = provider.GetRequiredService<QuicDataChannelTransport>();
            await using QuicDataChannelListener listener = await m_environment.CreateServer()
                .ListenAsync(new Uri("opc.quic://127.0.0.1:0")).ConfigureAwait(false);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            Task<QuicDataChannelConnection> accepted = listener.AcceptAsync(timeout.Token).AsTask();
            await using QuicDataChannelConnection client = await transport.ConnectAsync(listener.EndpointUrl, timeout.Token)
                .ConfigureAwait(false);
            await using QuicDataChannelConnection server = await accepted.ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(counted.Count, Is.EqualTo(1));
                Assert.That(client.Context.IsReady, Is.True);
                Assert.That(client.Context.Channels, Is.Empty);
                Assert.That(server.Context.Channels, Is.Empty);
                Assert.That(client.Kind, Is.EqualTo(DataChannelCarrierKind.Quic));
            });
        }

        /// <summary>
        /// Common application context ceilings apply to real independently authenticated native connections.
        /// </summary>
        [Test]
        public async Task ApplicationContextLimitRefusesSecondNativeContextWithoutDisturbingFirst()
        {
            var budget = new DataChannelApplicationBudget(new DataChannelApplicationLimits { MaxContexts = 1 });
            var factory = new DataChannelContextFactory(
                new PassthroughConfiguredAuthorizer(), m_environment!.Options, budget,
                TimeProvider.System, m_environment.Telemetry);
            await using NativeFlowPair first = await m_environment.ConnectPairAsync(clientFactory: factory).ConfigureAwait(false);
            DataChannelException? rejected = await Assert.ThrowsAsync<DataChannelException>(async () =>
                await m_environment.ConnectPairAsync(clientFactory: factory).ConfigureAwait(false)).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(rejected!.Result, Is.EqualTo(DataChannelResult.WouldBlock));
                Assert.That(budget.Usage.Contexts, Is.EqualTo(1));
                Assert.That(first.Client.Context.IsReady, Is.True);
            });
            await first.OpenAsync().ConfigureAwait(false);
            await first.DisposeAsync().ConfigureAwait(false);
            DataChannelResourceAssertions.AreReleased(budget.Usage);
        }

        /// <summary>
        /// A send direction with no local prefix reports an actual zero reliable boundary and preserves peer input.
        /// </summary>
        [Test]
        public async Task ResetWithoutLocalPrefixReportsActualZeroReliableBoundary()
        {
            await using NativePeerPair pair = await NativePeerPair.CreateAsync(m_environment!).ConfigureAwait(false);
            await using NativeQuicStream opener = await pair.Client.OpenStreamAsync().ConfigureAwait(false);
            Assert.That((await opener.SendAsync(Bytes(1)).ConfigureAwait(false))!.Status,
                Is.EqualTo(NativeQuicStatus.Success));
            await using NativeQuicStream acceptor = await pair.Server.AcceptStreamAsync().ConfigureAwait(false);
            Assert.That((await acceptor.ReadAsync().ConfigureAwait(false)).Payload, Is.EqualTo(Bytes(1)));
            Assert.That((await acceptor.ResetSendAsync(101).ConfigureAwait(false)).Status,
                Is.EqualTo(NativeQuicStatus.Success));
            NativeQuicCompletion local = await acceptor.WaitResetSentAsync().ConfigureAwait(false);
            NativeQuicCompletion remote = await opener.ReadAsync().ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(local.Status, Is.EqualTo(NativeQuicStatus.Success));
                Assert.That(local.FinalSize, Is.Zero);
                Assert.That(local.ReliableSize, Is.Zero);
                Assert.That(local.ResetFrameType, Is.EqualTo(0x04).Or.EqualTo(0x24));
                Assert.That(local.ErrorCode, Is.EqualTo(101));
                Assert.That(remote.ReadOutcome, Is.EqualTo(2));
                Assert.That(remote.FinalSize, Is.Zero);
                Assert.That(remote.ReliableSize, Is.Zero);
                Assert.That(remote.ErrorCode, Is.EqualTo(local.ErrorCode));
                Assert.That(pair.Server.Peer.PeerResetStreamAt09, Is.True);
            });
            Assert.That((await opener.SendAsync(Bytes(9)).ConfigureAwait(false))!.Status,
                Is.EqualTo(NativeQuicStatus.Success));
            Assert.That((await acceptor.ReadAsync().ConfigureAwait(false)).Payload, Is.EqualTo(Bytes(9)));
        }

        /// <summary>
        /// Application-wide native capacity refusal happens before creating a native connection.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">The requested limit dimension is unknown.</exception>
        [TestCase("bytes")]
        [TestCase("operations")]
        [TestCase("streams")]
        public async Task NativeCapacityAdmissionIsAtomicBeforeNetworkCreation(string dimension)
        {
            var defaults = new DataChannelApplicationLimits();
            DataChannelApplicationLimits limits = dimension switch
            {
                "bytes" => defaults with { MaxBufferedBytes = 1 },
                "operations" => defaults with { MaxOperations = 1 },
                "streams" => defaults with { MaxStreams = 1 },
                _ => throw new ArgumentOutOfRangeException(nameof(dimension))
            };
            var budget = new DataChannelApplicationBudget(limits);
            var transport = new QuicDataChannelTransport(
                m_backend!, m_environment!.ClientCredentials, m_environment.ClientFactory,
                m_environment.BindingOptions, applicationBudget: budget);
            DataChannelException? failure = await Assert.ThrowsAsync<DataChannelException>(async () =>
                await transport.ConnectAsync(new Uri("opc.quic://127.0.0.1:4840")).ConfigureAwait(false))
                .ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(failure!.Result, Is.EqualTo(DataChannelResult.WouldBlock));
                DataChannelResourceAssertions.AreReleased(budget.Usage);
            });
            await NativeFlowTestEnvironment.AssertNoNativeOwnershipAsync(m_backend!).ConfigureAwait(false);
        }

        /// <summary>
        /// Oversized caller storage is refused before it can wait behind a native writer or consume an operation slot.
        /// </summary>
        [Test]
        public async Task OversizedNativeInputIsRefusedBeforeWriterAdmission()
        {
            await using NativePeerPair pair = await NativePeerPair.CreateAsync(m_environment!).ConfigureAwait(false);
            await using NativeQuicStream stream = await pair.Client.OpenStreamAsync().ConfigureAwait(false);
            ByteString payload = new(new byte[65537]);
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
                await stream.SendAsync(payload).ConfigureAwait(false)).ConfigureAwait(false);
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
                await pair.Client.SendDatagramAsync(payload).ConfigureAwait(false)).ConfigureAwait(false);
            Assert.That((await stream.SendAsync(Bytes(17)).ConfigureAwait(false))!.Status,
                Is.EqualTo(NativeQuicStatus.Success));
            await using NativeQuicStream peer = await pair.Server.AcceptStreamAsync().ConfigureAwait(false);
            Assert.That((await peer.ReadAsync().ConfigureAwait(false)).Payload, Is.EqualTo(Bytes(17)));
        }

        /// <summary>
        /// The AOT-shared scenario exercises the same real public flow facade without a test-framework dependency.
        /// </summary>
        [Test]
        public async Task FrameworkIndependentNativeFlowScenario()
        {
            ArrayOf<string> checks = await NativeFlowLiveScenario.RunAsync(m_backend!).ConfigureAwait(false);
            string[] completed = checks.Span.ToArray();
            Assert.Multiple(() =>
            {
                Assert.That(checks.Count, Is.EqualTo(17));
                Assert.That(completed, Does.Contain("ConfirmedMutualTlsBothRoles"));
                Assert.That(completed, Does.Contain("OpenAcceptReadyBeforeApplicationData"));
                Assert.That(completed, Does.Contain("ByteExactDatagramDelivery"));
                Assert.That(completed, Does.Contain("ZeroCommonAndCarrierReservations"));
            });
        }

        /// <summary>
        /// Invalid QUIC error codes are refused without poisoning owners or preventing later valid operations.
        /// </summary>
        [TestCase(true)]
        [TestCase(false)]
        public async Task InvalidCloseCodeDoesNotPoisonNativeOwner(bool closeStream)
        {
            await using NativePeerPair pair = await NativePeerPair.CreateAsync(m_environment!).ConfigureAwait(false);
            await using NativeQuicStream source = await pair.Client.OpenStreamAsync().ConfigureAwait(false);
            if (closeStream)
            {
                await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
                    await source.CloseAsync(1UL << 62).ConfigureAwait(false)).ConfigureAwait(false);
            }
            else
            {
                await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
                    await pair.Client.CloseAsync(1UL << 62).ConfigureAwait(false)).ConfigureAwait(false);
            }
            Assert.Multiple(() =>
            {
                Assert.That(source.IsClosed, Is.False);
                Assert.That(pair.Client.IsClosed, Is.False);
            });
            Assert.That((await source.SendAsync(Bytes(25)).ConfigureAwait(false))!.Status,
                Is.EqualTo(NativeQuicStatus.Success));
            await using NativeQuicStream peer = await pair.Server.AcceptStreamAsync().ConfigureAwait(false);
            Assert.That((await peer.ReadAsync().ConfigureAwait(false)).Payload, Is.EqualTo(Bytes(25)));
        }

        /// <summary>
        /// A completely occupied native job table cannot strand connection or stream cleanup ownership.
        /// </summary>
        [Test]
        public async Task GlobalNativeOperationSaturationStillDrainsConnectionClosure()
        {
            NativeQuicConnectionSettings settings = m_environment!.BindingOptions.Native with
            {
                MaxStreams = 32,
                MaxPendingOperations = 32,
                ReadSize = 13,
                OperationTimeout = TimeSpan.FromSeconds(10)
            };
            await using NativePeerPair first = await NativePeerPair.CreateAsync(m_environment, settings).ConfigureAwait(false);
            await using NativePeerPair second = await NativePeerPair.CreateAsync(m_environment, settings).ConfigureAwait(false);
            NativeQuicConnection[] connections = [first.Client, first.Server, second.Client, second.Server];
            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var reads = new List<Task<NativeQuicCompletion>>();
            try
            {
                foreach (NativeQuicConnection connection in connections)
                {
                    for (int index = 0; index < 31; index++)
                    {
                        NativeQuicStream stream = await connection.OpenStreamAsync(cancellationToken: cancellation.Token)
                            .ConfigureAwait(false);
                        reads.Add(stream.ReadAsync(cancellation.Token).AsTask());
                    }
                }
                NativeQuicCounts saturated = await m_backend!.GetCountsAsync(cancellation.Token).ConfigureAwait(false);
                Assert.That(saturated.Operations, Is.EqualTo(128));
                await Task.WhenAll(connections.Select(connection => connection.CloseAsync().AsTask()))
                    .WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                NativeQuicCompletion[] outcomes = await Task.WhenAll(reads).ConfigureAwait(false);
                Assert.Multiple(() =>
                {
                    Assert.That(outcomes, Has.Length.EqualTo(124));
                    Assert.That(outcomes.All(result =>
                        result.Status is NativeQuicStatus.Cancelled or NativeQuicStatus.Closed), Is.True);
                });
            }
            finally
            {
                await cancellation.CancelAsync().ConfigureAwait(false);
                await Task.WhenAll(reads).ConfigureAwait(false);
            }
            await first.DisposeAsync().ConfigureAwait(false);
            await second.DisposeAsync().ConfigureAwait(false);
            await NativeFlowTestEnvironment.AssertNoNativeOwnershipAsync(m_backend!).ConfigureAwait(false);
        }

        /// <summary>
        /// Stream release joins native STOP/reset observers as well as ordinary read/write operations.
        /// </summary>
        [Test]
        public async Task StreamDisposalDrainsAuxiliaryObserversWithoutClosingConnection()
        {
            await using NativePeerPair pair = await NativePeerPair.CreateAsync(m_environment!).ConfigureAwait(false);
            for (int iteration = 0; iteration < 10; iteration++)
            {
                await using NativeQuicStream source = await pair.Client.OpenStreamAsync().ConfigureAwait(false);
                Assert.That((await source.SendAsync(Bytes(1)).ConfigureAwait(false))!.Status,
                    Is.EqualTo(NativeQuicStatus.Success));
                await using NativeQuicStream peer = await pair.Server.AcceptStreamAsync().ConfigureAwait(false);
                Assert.That((await peer.ReadAsync().ConfigureAwait(false)).Payload, Is.EqualTo(Bytes(1)));
                Task<NativeQuicCompletion> stopped = source.WaitStoppedAsync().AsTask();
                Task<NativeQuicCompletion> reset = source.WaitResetSentAsync().AsTask();
                Task<NativeQuicCompletion> receivedReset = source.WaitResetReceivedAsync().AsTask();
                Assert.Multiple(() =>
                {
                    Assert.That(stopped.IsCompleted, Is.False);
                    Assert.That(reset.IsCompleted, Is.False);
                    Assert.That(receivedReset.IsCompleted, Is.False);
                });
                await source.CloseAsync(73).ConfigureAwait(false);
                NativeQuicCompletion[] observed = await Task.WhenAll(stopped, reset, receivedReset).ConfigureAwait(false);
                Assert.Multiple(() =>
                {
                    Assert.That(observed.Take(2).Select(result => result.Status), Is.All.EqualTo(NativeQuicStatus.Cancelled));
                    Assert.That(observed[2].Status, Is.EqualTo(NativeQuicStatus.Cancelled).Or.EqualTo(NativeQuicStatus.Success));
                    Assert.That(pair.Client.IsClosed, Is.False);
                    Assert.That(pair.Server.IsClosed, Is.False);
                });
                if (observed[2].Status == NativeQuicStatus.Success)
                {
                    Assert.Multiple(() =>
                    {
                        Assert.That(observed[2].Remote, Is.True);
                        Assert.That(observed[2].ReadOutcome, Is.EqualTo(2));
                        Assert.That(observed[2].ErrorCode, Is.EqualTo(73));
                    });
                }
            }
            Assert.That((await pair.Client.SendDatagramAsync(Bytes(13)).ConfigureAwait(false))!.Status,
                Is.EqualTo(NativeQuicStatus.Success));
            Assert.That((await pair.Server.ReceiveDatagramAsync().ConfigureAwait(false)).Payload, Is.EqualTo(Bytes(13)));
        }

        /// <summary>
        /// Core ingress cancellation releases carrier storage even while a detached application policy unwinds.
        /// </summary>
        [Test]
        public async Task IngressCancellationDoesNotPinCarrierCapacityOnDetachedPolicy()
        {
            var authorizer = new PausingOpenAuthorizer();
            var serverFactory = new DataChannelContextFactory(
                authorizer, m_environment!.Options, m_environment.ServerBudget,
                TimeProvider.System, m_environment.Telemetry);
            await using NativeFlowPair pair = await m_environment.ConnectPairAsync(serverFactory: serverFactory)
                .ConfigureAwait(false);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            Task<DataChannel> opening = pair.Client.Context.OpenAsync(
                NativeFlowTestEnvironment.Parameters(), cancellationToken: timeout.Token).AsTask();
            await authorizer.Entered.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
            Task disposing = pair.Server.DisposeAsync().AsTask();
            try
            {
                await pair.Server.NativeConnection.Completion.WaitAsync(timeout.Token).ConfigureAwait(false);
                await pair.Server.Completion.WaitAsync(timeout.Token).ConfigureAwait(false);
                Assert.Multiple(() =>
                {
                    Assert.That(authorizer.Release.Task.IsCompleted, Is.False);
                    Assert.That(m_environment.ServerBudget.Usage.BufferedBytes, Is.Zero);
                });
            }
            finally
            {
                authorizer.Release.TrySetResult();
                await disposing.WaitAsync(timeout.Token).ConfigureAwait(false);
                await Assert.ThrowsAsync<DataChannelException>(async () => await opening.ConfigureAwait(false))
                    .ConfigureAwait(false);
            }
            DataChannelResourceAssertions.AreReleased(m_environment.ServerBudget.Usage);
        }

        /// <summary>
        /// A deterministic asynchronous application callback, not a mocked transport or protocol implementation.
        /// </summary>
        private sealed class PausingOpenAuthorizer : IDataChannelAuthorizer
        {
            /// <summary>
            /// Gets notification that real native OPEN ingress reached application authorization.
            /// </summary>
            internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

            /// <summary>
            /// Gets the test-controlled completion of the admitted application callback.
            /// </summary>
            internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

            /// <inheritdoc/>
            public async ValueTask<DataChannelAuthorization> AuthorizeAsync(
                DataChannelAuthorizationRequest request,
                CancellationToken cancellationToken = default)
            {
                Entered.TrySetResult();
                await Release.Task.ConfigureAwait(false);
                return new DataChannelAuthorization(DataChannelResult.Good, request.Parameters);
            }
        }

        /// <summary>
        /// Explicit test resource authorization, used only when supplying a separate application budget.
        /// </summary>
        private sealed class PassthroughConfiguredAuthorizer : IDataChannelAuthorizer
        {
            /// <inheritdoc/>
            public ValueTask<DataChannelAuthorization> AuthorizeAsync(
                DataChannelAuthorizationRequest request,
                CancellationToken cancellationToken = default)
            {
                return ValueTask.FromResult(
                    request.Mode == DataChannelAuthorizationMode.Configured &&
                    request.Parameters.ResourceUri == NativeFlowTestEnvironment.ResourceUri
                        ? new DataChannelAuthorization(DataChannelResult.Good, request.Parameters)
                        : new DataChannelAuthorization(DataChannelResult.NotAuthorized));
            }
        }
    }
}
