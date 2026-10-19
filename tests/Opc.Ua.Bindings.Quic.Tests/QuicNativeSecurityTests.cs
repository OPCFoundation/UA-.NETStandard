// Copyright (c) OPC Foundation. Licensed under the OPC Foundation MIT License 1.00.

using System;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
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
        /// URI assertions never substitute for the configured SPKI rule, and matching keys never waive URI checks.
        /// </summary>
        [TestCase(true)]
        [TestCase(false)]
        public async Task WrongPeerUriAndKeyAreRejected(bool wrongUri)
        {
            NativeQuicPeer expected = m_environment!.ServerPeer;
            var wrong = new NativeQuicPeer(
                wrongUri ? "urn:opcfoundation:flow-binding-tests:unlisted" : expected.ApplicationUri,
                wrongUri ? expected.SpkiSha256 : m_environment.ClientPeer.SpkiSha256);
            using NativeQuicCertificateProvider provider = m_environment.CreateCredentials(true, [wrong]);
            await using QuicDataChannelListener listener = await m_environment.CreateServer()
                .ListenAsync(new Uri("opc.quic://127.0.0.1:0")).ConfigureAwait(false);
            NativeQuicTransportException? failure = await Assert.ThrowsAsync<NativeQuicTransportException>(async () =>
                await m_environment.CreateClient(provider).ConnectAsync(listener.EndpointUrl).ConfigureAwait(false))
                .ConfigureAwait(false);
            Assert.That(failure!.Completion!.Status, Is.EqualTo(NativeQuicStatus.Authentication));
            Assert.That(m_environment.ClientBudget.Usage.Contexts, Is.Zero);
        }

        /// <summary>
        /// A trusted chain and correct key do not waive the connector's actual TLS hostname check.
        /// </summary>
        [Test]
        public async Task WrongHostnameIsRejectedOnRealHandshake()
        {
            await using NativeQuicListener listener = await NativeQuicListener.ListenAsync(
                m_backend!, m_environment!.ServerCredentials, "127.0.0.1:0",
                DataChannelProfiles.QuicAlpn, m_environment.BindingOptions.Native).ConfigureAwait(false);
            NativeQuicTransportException? failure = await Assert.ThrowsAsync<NativeQuicTransportException>(async () =>
                await NativeQuicConnection.ConnectAsync(
                    m_backend!, m_environment.ClientCredentials, listener.Address,
                    DataChannelProfiles.QuicAlpn, "not-the-certificate.example",
                    m_environment.BindingOptions.Native).ConfigureAwait(false)).ConfigureAwait(false);
            Assert.That(failure!.Completion!.Status, Is.EqualTo(NativeQuicStatus.Authentication));
        }

        /// <summary>
        /// Exact application/key allowlists do not bypass the explicitly selected native trust roots.
        /// </summary>
        [Test]
        public async Task WrongTrustIsRejectedOnRealHandshake()
        {
            var wrongList = new TrustListIdentifier("OtherNativeRoots");
            m_environment!.ClientManager.RegisterTrustList(wrongList, "InMemory:WrongNativeRoot:" + Guid.NewGuid());
            using Certificate otherRoot = DefaultCertificateFactory.Instance.CreateCertificate("CN=Other native test root")
                .SetCAConstraint()
                .SetNotBefore(DateTime.UtcNow.AddMinutes(-5))
                .SetNotAfter(DateTime.UtcNow.AddHours(1))
                .SetRSAKeySize(2048)
                .CreateForRSA();
            using (ICertificateStore store = m_environment.ClientManager.OpenTrustedStore(wrongList))
            {
                await store.AddAsync(otherRoot).ConfigureAwait(false);
            }
            using NativeQuicCertificateProvider provider = m_environment.CreateCredentials(
                true, [m_environment.ServerPeer], wrongList);
            await using QuicDataChannelListener listener = await m_environment.CreateServer()
                .ListenAsync(new Uri("opc.quic://127.0.0.1:0")).ConfigureAwait(false);
            NativeQuicTransportException? failure = await Assert.ThrowsAsync<NativeQuicTransportException>(async () =>
                await m_environment.CreateClient(provider).ConnectAsync(listener.EndpointUrl).ConfigureAwait(false))
                .ConfigureAwait(false);
            Assert.That(failure!.Completion!.Status, Is.EqualTo(NativeQuicStatus.Authentication));
        }

        /// <summary>
        /// No fallback ALPN is offered and no data context is created for a foreign protocol.
        /// </summary>
        [Test]
        public async Task ForeignAlpnNeverExposesContext()
        {
            await using NativeQuicListener listener = await NativeQuicListener.ListenAsync(
                m_backend!, m_environment!.ServerCredentials, "127.0.0.1:0",
                DataChannelProfiles.WebTransportAlpn, m_environment.BindingOptions.Native).ConfigureAwait(false);
            NativeQuicTransportException? failure = await Assert.ThrowsAsync<NativeQuicTransportException>(async () =>
                await m_environment.CreateClient().ConnectAsync(new Uri("opc.quic://" + listener.Address))
                    .ConfigureAwait(false)).ConfigureAwait(false);
            Assert.That(failure!.Completion!.Status, Is.EqualTo(NativeQuicStatus.Authentication));
            Assert.That(m_environment.ClientBudget.Usage.Contexts, Is.Zero);
        }

        /// <summary>
        /// The real native listener requires an explicitly authorized client application/key, not HELLO claims.
        /// </summary>
        [TestCase(true)]
        [TestCase(false)]
        public async Task UnlistedClientCannotEstablishNativeDataContext(bool wrongUri)
        {
            using NativeQuicCertificateProvider provider = m_environment!.CreateCredentials(false,
                [new NativeQuicPeer(
                    wrongUri ? "urn:opcfoundation:flow-binding-tests:other-client" : NativeFlowTestEnvironment.ClientUri,
                    wrongUri ? m_environment.ClientPeer.SpkiSha256 : m_environment.ServerPeer.SpkiSha256)]);
            await using QuicDataChannelListener listener = await m_environment.CreateServer(provider)
                .ListenAsync(new Uri("opc.quic://127.0.0.1:0")).ConfigureAwait(false);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            Task<QuicDataChannelConnection> accepted = listener.AcceptAsync(timeout.Token).AsTask();
            NativeQuicTransportException? failure = await Assert.ThrowsAsync<NativeQuicTransportException>(async () =>
                await m_environment.CreateClient().ConnectAsync(listener.EndpointUrl, timeout.Token).ConfigureAwait(false))
                .ConfigureAwait(false);
            Assert.That(failure!.Completion!.Status, Is.EqualTo(NativeQuicStatus.Authentication));
            timeout.Cancel();
            await RequireRejectedAcceptAsync(accepted).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(m_environment.ClientBudget.Usage.Contexts, Is.Zero);
                Assert.That(m_environment.ServerBudget.Usage.Contexts, Is.Zero);
            });
        }

        /// <summary>
        /// Stack validation is awaited before factory registration or HELLO on a real authenticated native peer.
        /// </summary>
        [Test]
        public async Task StackValidationRunsBeforeContextExposure()
        {
            var rejecting = new RejectingCredentialProvider(m_environment!.ClientCredentials);
            var factory = new CountingContextFactory(m_environment.ClientFactory);
            await using QuicDataChannelListener listener = await m_environment.CreateServer()
                .ListenAsync(new Uri("opc.quic://127.0.0.1:0")).ConfigureAwait(false);
            await Assert.ThrowsAsync<AuthenticationException>(async () =>
                await m_environment.CreateClient(rejecting, factory).ConnectAsync(listener.EndpointUrl).ConfigureAwait(false))
                .ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(rejecting.ValidatedPeer, Is.True);
                Assert.That(factory.Count, Is.Zero);
                Assert.That(m_environment.ClientBudget.Usage.Contexts, Is.Zero);
            });
        }

        /// <summary>
        /// Selected credential activation or selected trust invalidation closes the actual association.
        /// </summary>
        [TestCase(true)]
        [TestCase(false)]
        public async Task CertificateAndTrustChangesInvalidateAffectedConnections(bool rotateCertificate)
        {
            await using NativeFlowPair pair = await m_environment!.ConnectPairAsync().ConfigureAwait(false);
            await pair.OpenAsync().ConfigureAwait(false);
            var contextClosed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            pair.Server.Context.Closed += _ => contextClosed.TrySetResult();
            if (rotateCertificate)
            {
                using Certificate replacement = m_environment.CreateApplicationCertificate(NativeFlowTestEnvironment.ServerUri);
                using var issuers = new CertificateCollection { m_environment.Root };
                await m_environment.ServerManager.UpdateApplicationCertificateAsync(
                    ObjectTypeIds.RsaSha256ApplicationCertificateType, replacement, issuers).ConfigureAwait(false);
            }
            else
            {
                m_environment.ServerManager.NotifyTrustListChanged(TrustListIdentifier.Peers, true, true);
            }
            await Task.WhenAll(pair.Server.Completion, pair.Listener.Completion, contextClosed.Task)
                .WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(pair.Server.NativeConnection.SecurityInvalidated, Is.True);
                Assert.That(pair.Server.Context.IsReady, Is.False);
                Assert.That(pair.Listener.IsListening, Is.False);
            });
        }

        /// <summary>
        /// An unrelated certificate trust scope does not revoke a native connection's explicit policy.
        /// </summary>
        [Test]
        public async Task UnrelatedTrustChangeLeavesConnectionAlive()
        {
            var unrelated = new TrustListIdentifier("UnrelatedNativeTrust");
            m_environment!.ServerManager.RegisterTrustList(unrelated, "InMemory:UnrelatedNativeTrust:" + Guid.NewGuid());
            await using NativeFlowPair pair = await m_environment.ConnectPairAsync().ConfigureAwait(false);
            m_environment.ServerManager.NotifyTrustListChanged(unrelated, true, true);
            (DataChannel source, DataChannel sink) = await pair.OpenAsync().ConfigureAwait(false);
            Assert.That(await source.SendAsync(Bytes(81)).ConfigureAwait(false), Is.EqualTo(DataChannelResult.Good));
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using DataChannelMessage? message = await sink.ReadAsync(timeout.Token).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(message!.Payload.Span.ToArray(), Is.EqualTo(new byte[] { 81 }));
                Assert.That(pair.Server.NativeConnection.SecurityInvalidated, Is.False);
                Assert.That(pair.Listener.IsListening, Is.True);
            });
        }

        /// <summary>
        /// Missing software material is refused before native admission, never replaced with a generated identity.
        /// </summary>
        [Test]
        public async Task MissingCredentialsAreRefusedBeforeNativeAdmission()
        {
            using var provider = new NativeQuicCertificateProvider(
                m_environment!.ClientManager,
                new NativeQuicCertificateOptions(
                    NativeFlowTestEnvironment.ClientUri, new NodeId(999999u), [m_environment.ServerPeer]));
            await Assert.ThrowsAsync<AuthenticationException>(async () =>
                await provider.AcquireAsync().ConfigureAwait(false)).ConfigureAwait(false);
            await NativeFlowTestEnvironment.AssertNoNativeOwnershipAsync(m_backend!).ConfigureAwait(false);
        }

        /// <summary>
        /// Actual native evidence without confirmed mutual authentication cannot pass the reusable admission helper.
        /// Both outcomes use the real backend; there is no constructed or mocked peer-evidence object.
        /// </summary>
        [Test]
        public async Task NativeEvidenceMustConfirmMutualAuthenticationBeforeContextExposure()
        {
            await using NativeQuicListener listener = await NativeQuicListener.ListenAsync(
                m_backend!, m_environment!.ServerCredentials, "127.0.0.1:0",
                DataChannelProfiles.QuicAlpn, m_environment.BindingOptions.Native).ConfigureAwait(false);
            using NativeQuicCredentialLease credentials =
                await m_environment.ClientCredentials.AcquireAsync().ConfigureAwait(false);
            NativeQuicOptions options = NativeQuicConnection.CreateOptions(
                listener.Address, DataChannelProfiles.QuicAlpn, credentials, "localhost",
                m_environment.BindingOptions.Native);
            NativeQuicCompletion connected = await m_backend!.ConnectAsync(options).ConfigureAwait(false);
            Assert.That(connected.Status, Is.EqualTo(NativeQuicStatus.Success));
            NativeQuicConnectionHandle handle = connected.Connection;
            credentials.ReleasePrivateKey();
            try
            {
                NativeQuicPeerEvidence evidence = await m_backend.GetPeerEvidenceAsync(handle).ConfigureAwait(false);
                if (!evidence.HandshakeConfirmed || !evidence.MutualTlsAuthenticated)
                {
                    await Assert.ThrowsAsync<AuthenticationException>(async () =>
                        await NativeQuicConnection.AuthenticateAsync(
                            m_backend, m_environment.ClientCredentials, credentials, handle,
                            DataChannelProfiles.QuicAlpn, true, m_environment.BindingOptions.Native,
                            TimeProvider.System, CancellationToken.None).ConfigureAwait(false)).ConfigureAwait(false);
                    Assert.That(m_environment.ClientBudget.Usage.Contexts, Is.Zero);
                }
                else
                {
                    await using NativeQuicConnection authenticated = await NativeQuicConnection.AuthenticateAsync(
                        m_backend, m_environment.ClientCredentials, credentials, handle,
                        DataChannelProfiles.QuicAlpn, true, m_environment.BindingOptions.Native,
                        TimeProvider.System, CancellationToken.None).ConfigureAwait(false);
                    handle = default;
                    Assert.Multiple(() =>
                    {
                        Assert.That(authenticated.Peer.HandshakeConfirmed, Is.True);
                        Assert.That(authenticated.Peer.MutualTlsAuthenticated, Is.True);
                        Assert.That(authenticated.Peer.Used0Rtt, Is.False);
                        Assert.That(authenticated.Peer.Resumed, Is.False);
                    });
                }
            }
            finally
            {
                if (handle.Value != 0)
                {
                    await NativeQuicConnection.ReleaseUnestablishedAsync(m_backend, handle).ConfigureAwait(false);
                }
            }
        }

        /// <summary>
        /// RSA below ABI1's supported software capability is explicitly refused.
        /// </summary>
        [Test]
        public async Task UnsupportedSigningCapabilityIsRefusedBeforeNativeAdmission()
        {
            ICertificateBuilder builder = DefaultCertificateFactory.Instance.CreateApplicationCertificate(
                    NativeFlowTestEnvironment.ClientUri, "Unsupported native test", "CN=Unsupported native test",
                    ["localhost", "127.0.0.1"])
                .SetNotBefore(DateTime.UtcNow.AddMinutes(-5))
                .SetNotAfter(DateTime.UtcNow.AddMinutes(30));
            builder.SetIssuer(m_environment!.Root);
            using Certificate unsupported = builder.SetRSAKeySize(1024).CreateForRSA();
            using var issuers = new CertificateCollection { m_environment.Root };
            await m_environment.ClientManager.UpdateApplicationCertificateAsync(
                ObjectTypeIds.RsaSha256ApplicationCertificateType, unsupported, issuers).ConfigureAwait(false);
            await Assert.ThrowsAsync<NotSupportedException>(async () =>
                await m_environment.ClientCredentials.AcquireAsync().ConfigureAwait(false)).ConfigureAwait(false);
            await NativeFlowTestEnvironment.AssertNoNativeOwnershipAsync(m_backend!).ConfigureAwait(false);
        }

        /// <summary>
        /// Abandoning an unconfirmed native handshake drains both accepted and outbound ownership.
        /// </summary>
        [Test]
        public async Task CancellingHandshakeReleasesNativeOwnership()
        {
            await using NativeQuicListener listener = await NativeQuicListener.ListenAsync(
                m_backend!, m_environment!.ServerCredentials, "127.0.0.1:0",
                DataChannelProfiles.QuicAlpn, m_environment.BindingOptions.Native).ConfigureAwait(false);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var provider = new PausingCredentialProvider(m_environment.ClientCredentials);
            Task<QuicDataChannelConnection> connecting = m_environment.CreateClient(provider).ConnectAsync(
                new Uri("opc.quic://" + listener.Address), timeout.Token).AsTask();
            await provider.Validated.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
            NativeQuicCounts active = await m_backend!.GetCountsAsync(timeout.Token).ConfigureAwait(false);
            Assert.That(active.Connections, Is.GreaterThanOrEqualTo(1));
            timeout.Cancel();
            await Assert.CatchAsync<OperationCanceledException>(async () => await connecting.ConfigureAwait(false))
                .ConfigureAwait(false);
            await listener.DisposeAsync().ConfigureAwait(false);
            await NativeFlowTestEnvironment.AssertNoNativeOwnershipAsync(m_backend!).ConfigureAwait(false);
            Assert.That(m_environment.ClientBudget.Usage.Contexts, Is.Zero);
        }

        /// <summary>
        /// Every advertised ECDSA software-key family crosses the real certificate-manager and native TLS boundary.
        /// </summary>
        [TestCase(256)]
        [TestCase(384)]
        [TestCase(521)]
        public async Task SupportedEcdsaCredentialsEstablishRealNativeFlow(int bits)
        {
            ECCurve curve = bits switch
            {
                256 => ECCurve.NamedCurves.nistP256,
                384 => ECCurve.NamedCurves.nistP384,
                521 => ECCurve.NamedCurves.nistP521,
                _ => throw new ArgumentOutOfRangeException(nameof(bits))
            };
            NodeId type = bits switch
            {
                256 => ObjectTypeIds.EccNistP256ApplicationCertificateType,
                384 => ObjectTypeIds.EccNistP384ApplicationCertificateType,
                _ => new NodeId("NativeTestP521", 1)
            };
            DateTime now = DateTime.UtcNow;
            using Certificate root = DefaultCertificateFactory.Instance.CreateCertificate("CN=Native ECDSA test root")
                .SetCAConstraint()
                .SetNotBefore(now.AddMinutes(-5))
                .SetNotAfter(now.AddHours(2))
                .SetECCurve(curve)
                .CreateForECDsa();
            using Certificate client = CreateEcdsaApplication(NativeFlowTestEnvironment.ClientUri, root, curve, now);
            using Certificate server = CreateEcdsaApplication(NativeFlowTestEnvironment.ServerUri, root, curve, now);
            using var issuers = new CertificateCollection { root };
            using (ICertificateStore store = m_environment!.ClientManager.OpenTrustedStore(TrustListIdentifier.Peers))
            {
                await store.AddAsync(root).ConfigureAwait(false);
            }
            using (ICertificateStore store = m_environment.ServerManager.OpenTrustedStore(TrustListIdentifier.Peers))
            {
                await store.AddAsync(root).ConfigureAwait(false);
            }
            m_environment.ClientManager.NotifyTrustListChanged(TrustListIdentifier.Peers, true, false);
            m_environment.ServerManager.NotifyTrustListChanged(TrustListIdentifier.Peers, true, false);
            await m_environment.ClientManager.UpdateApplicationCertificateAsync(type, client, issuers).ConfigureAwait(false);
            await m_environment.ServerManager.UpdateApplicationCertificateAsync(type, server, issuers).ConfigureAwait(false);
            var clientPeer = new NativeQuicPeer(
                NativeFlowTestEnvironment.ClientUri, NativeQuicCertificateProvider.GetPublicKeySha256(client));
            var serverPeer = new NativeQuicPeer(
                NativeFlowTestEnvironment.ServerUri, NativeQuicCertificateProvider.GetPublicKeySha256(server));
            using var clientCredentials = new NativeQuicCertificateProvider(
                m_environment.ClientManager,
                new NativeQuicCertificateOptions(NativeFlowTestEnvironment.ClientUri, type, [serverPeer]));
            using var serverCredentials = new NativeQuicCertificateProvider(
                m_environment.ServerManager,
                new NativeQuicCertificateOptions(NativeFlowTestEnvironment.ServerUri, type, [clientPeer]));
            await using NativeFlowPair pair = await m_environment.ConnectPairAsync(
                clientCredentials: clientCredentials, serverCredentials: serverCredentials).ConfigureAwait(false);
            (DataChannel source, DataChannel sink) = await pair.OpenAsync().ConfigureAwait(false);
            Assert.That(await source.SendAsync(Bytes(1, 5, 9)).ConfigureAwait(false), Is.EqualTo(DataChannelResult.Good));
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using DataChannelMessage? message = await sink.ReadAsync(timeout.Token).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(message!.Payload, Is.EqualTo(Bytes(1, 5, 9)));
                Assert.That(pair.Client.Identity.PeerKeyThumbprint, Is.EqualTo(serverPeer.SpkiSha256));
                Assert.That(pair.Server.Identity.PeerKeyThumbprint, Is.EqualTo(clientPeer.SpkiSha256));
                Assert.That(pair.Client.NativeConnection.Peer.MutualTlsAuthenticated, Is.True);
            });
        }

        /// <summary>
        /// Continuing certificate validity is checked after TLS establishment, not only during HELLO.
        /// </summary>
        [Test]
        public async Task CertificateExpiryInvalidatesEstablishedAssociation()
        {
            var clock = new AdjustableClock();
            using var credentials = new NativeQuicCertificateProvider(
                m_environment!.ClientManager,
                new NativeQuicCertificateOptions(
                    NativeFlowTestEnvironment.ClientUri, ObjectTypeIds.RsaSha256ApplicationCertificateType,
                    [m_environment.ServerPeer]), clock);
            await using NativeFlowPair pair = await m_environment.ConnectPairAsync(clientCredentials: credentials)
                .ConfigureAwait(false);
            var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            pair.Client.Context.Closed += _ => closed.TrySetResult();
            clock.Advance(TimeSpan.FromHours(3));
            await Task.WhenAll(closed.Task, pair.Client.Completion).WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(pair.Client.NativeConnection.SecurityInvalidated, Is.True);
                Assert.That(pair.Client.Context.IsReady, Is.False);
            });
        }

        /// <summary>
        /// Normal multi-month application certificates do not exceed the listener's timer range.
        /// </summary>
        [Test]
        public async Task LongLivedCertificateDoesNotImmediatelyCloseListener()
        {
            await using NativeFlowTestEnvironment environment = await NativeFlowTestEnvironment.CreateAsync(
                m_backend!, certificateLifetime: TimeSpan.FromDays(365)).ConfigureAwait(false);
            await using NativeFlowPair pair = await environment.ConnectPairAsync().ConfigureAwait(false);
            await pair.OpenAsync().ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(pair.Listener.IsListening, Is.True);
                Assert.That(pair.Client.Context.IsReady, Is.True);
                Assert.That(pair.Server.Context.IsReady, Is.True);
            });
        }

        /// <summary>
        /// Creates a software ECDSA fixture using the existing stack factory.
        /// </summary>
        private static Certificate CreateEcdsaApplication(
            string uri,
            Certificate issuer,
            ECCurve curve,
            DateTime now)
        {
            ICertificateBuilder builder = DefaultCertificateFactory.Instance.CreateApplicationCertificate(
                uri, "Native ECDSA test", "CN=Native ECDSA test", ["localhost", "127.0.0.1"])
                .SetNotBefore(now.AddMinutes(-5))
                .SetNotAfter(now.AddHours(1));
            builder.SetIssuer(issuer);
            return builder.SetECCurve(curve).CreateForECDsa();
        }

        /// <summary>
        /// Waits for a refused/cancelled accept without hiding an unexpectedly exposed application context.
        /// </summary>
        private static async Task RequireRejectedAcceptAsync(Task<QuicDataChannelConnection> accepted)
        {
            bool exposed = false;
            try
            {
                await using QuicDataChannelConnection connection = await accepted.ConfigureAwait(false);
                exposed = true;
            }
            catch (Exception exception) when (exception is OperationCanceledException or
                NativeQuicTransportException or DataChannelException or ObjectDisposedException or AuthenticationException)
            {
                // No application context escaped the expected failed establishment.
            }
            Assert.That(exposed, Is.False);
        }

        /// <summary>
        /// Applies an explicit additional validation failure to genuine native certificate evidence.
        /// </summary>
        private sealed class RejectingCredentialProvider(INativeQuicCredentialProvider inner) : INativeQuicCredentialProvider
        {
            /// <summary>
            /// Gets whether actual native peer validation reached this additional policy.
            /// </summary>
            internal bool ValidatedPeer { get; private set; }

            /// <inheritdoc/>
            public ValueTask<NativeQuicCredentialLease> AcquireAsync(CancellationToken cancellationToken = default)
            {
                return inner.AcquireAsync(cancellationToken);
            }

            /// <inheritdoc/>
            public async ValueTask ValidatePeerAsync(
                NativeQuicCredentialLease credentials,
                NativeQuicPeerEvidence peer,
                ArrayOf<ByteString> verifiedChain,
                CancellationToken cancellationToken = default)
            {
                await inner.ValidatePeerAsync(credentials, peer, verifiedChain, cancellationToken).ConfigureAwait(false);
                ValidatedPeer = true;
                throw new AuthenticationException("The explicitly configured additional test policy refused this peer.");
            }
        }

        /// <summary>
        /// Counts actual application context registrations without replacing the native transport.
        /// </summary>
        private sealed class CountingContextFactory(IDataChannelContextFactory inner) : IDataChannelContextFactory
        {
            /// <summary>
            /// Gets the number of real context creations.
            /// </summary>
            internal int Count { get; private set; }

            /// <inheritdoc/>
            public DataChannelContext Create(IDataChannelCarrier carrier)
            {
                Count++;
                return inner.Create(carrier);
            }
        }

        /// <summary>
        /// Provides a deterministic cancellation point after genuine native and stack certificate validation.
        /// </summary>
        private sealed class PausingCredentialProvider(INativeQuicCredentialProvider inner) : INativeQuicCredentialProvider
        {
            /// <summary>
            /// Gets notification that the actual authenticated peer reached post-handshake policy.
            /// </summary>
            internal TaskCompletionSource Validated { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

            /// <inheritdoc/>
            public ValueTask<NativeQuicCredentialLease> AcquireAsync(CancellationToken cancellationToken = default)
            {
                return inner.AcquireAsync(cancellationToken);
            }

            /// <inheritdoc/>
            public async ValueTask ValidatePeerAsync(
                NativeQuicCredentialLease credentials,
                NativeQuicPeerEvidence peer,
                ArrayOf<ByteString> verifiedChain,
                CancellationToken cancellationToken = default)
            {
                await inner.ValidatePeerAsync(credentials, peer, verifiedChain, cancellationToken).ConfigureAwait(false);
                Validated.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Moves only certificate-policy UTC observations while native network timers keep using real time.
        /// </summary>
        private sealed class AdjustableClock : TimeProvider
        {
            /// <inheritdoc/>
            public override DateTimeOffset GetUtcNow()
            {
                return TimeProvider.System.GetUtcNow() + TimeSpan.FromTicks(Interlocked.Read(ref m_offset));
            }

            /// <summary>
            /// Advances the policy observation instant without sleeps or certificate artifacts.
            /// </summary>
            internal void Advance(TimeSpan amount)
            {
                Interlocked.Add(ref m_offset, amount.Ticks);
            }

            private long m_offset;
        }
    }
}
