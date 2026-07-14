/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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

#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// Deterministic unit tests for
    /// <see cref="PushConfigurationTrustListEffectHandler"/>, the default
    /// implementation of the OPC 10000-12 §7.10.9 post-<c>ApplyChanges</c>
    /// TrustList effects: force the SecureChannels whose peer certificate is
    /// no longer trusted to renegotiate, and close the Sessions (plus their
    /// Subscriptions) whose certificate user identity is no longer valid,
    /// while leaving unaffected channels and Sessions untouched.
    /// </summary>
    [TestFixture]
    [Category("ConfigurationNodeManager")]
    [Parallelizable]
    public class PushConfigurationTrustListEffectHandlerTests
    {
        private static readonly ITelemetryContext s_telemetry = NUnitTelemetryContext.Create();

        private PushConfigurationTrustListEffectHandler m_handler;
        private List<Certificate> m_certificates;

        [SetUp]
        public void SetUp()
        {
            m_handler = new PushConfigurationTrustListEffectHandler(s_telemetry);
            m_certificates = [];
        }

        [TearDown]
        public void TearDown()
        {
            foreach (Certificate certificate in m_certificates)
            {
                certificate.Dispose();
            }

            m_certificates.Clear();
        }

        [Test]
        public void ConstructorRejectsNullTelemetry()
        {
            Assert.That(
                () => new PushConfigurationTrustListEffectHandler(null!),
                Throws.InstanceOf<ArgumentNullException>());
        }

        [Test]
        public void ApplyAsyncRejectsNullContext()
        {
            Assert.That(
                async () => await m_handler.ApplyAsync(null!).ConfigureAwait(false),
                Throws.InstanceOf<ArgumentNullException>());
        }

        [Test]
        public async Task SecureChannelTrustEffectClosesOnlyUntrustedPeerChannelsAsync()
        {
            Certificate trustedPeer = CreateCertificate("CN=Trusted Peer");
            Certificate untrustedPeer = CreateCertificate("CN=Untrusted Peer");

            var listener = new FakePeerRotationListener(
                "listener-1",
                new FakeChannel("chan-trusted", trustedPeer),
                new FakeChannel("chan-untrusted", untrustedPeer),
                new FakeChannel("chan-nocert", peerCertificate: null));

            Mock<ICertificateValidatorEx> validator = CreateValidator(untrusted: [untrustedPeer]);

            var closedSessions = new List<NodeId>();
            PushConfigurationTrustListEffectContext context = CreateContext(
                effects: [SecureChannelEffect(TrustListIdentifier.Peers)],
                listeners: [listener],
                sessionManager: null,
                validator: validator.Object,
                closedSessions: closedSessions);

            await m_handler.ApplyAsync(context).ConfigureAwait(false);

            Assert.That(listener.RevalidationInvocationCount, Is.EqualTo(1));
            Assert.That(listener.ClosedChannelIds, Has.Count.EqualTo(1));
            Assert.That(listener.ClosedChannelIds[0], Is.EqualTo("chan-untrusted"));
            Assert.That(closedSessions, Is.Empty, "no user effect must not close Sessions");
        }

        [Test]
        public async Task SecureChannelTrustEffectLeavesEveryChannelOpenWhenAllPeersStillTrustedAsync()
        {
            // The "trusted addition" path: nothing was removed from trust, so
            // every currently-connected peer still validates and no channel is
            // cut.
            Certificate peerA = CreateCertificate("CN=Peer A");
            Certificate peerB = CreateCertificate("CN=Peer B");

            var listener = new FakePeerRotationListener(
                "listener-1",
                new FakeChannel("chan-a", peerA),
                new FakeChannel("chan-b", peerB));

            Mock<ICertificateValidatorEx> validator = CreateValidator(untrusted: []);

            PushConfigurationTrustListEffectContext context = CreateContext(
                effects: [SecureChannelEffect(TrustListIdentifier.Peers)],
                listeners: [listener],
                sessionManager: null,
                validator: validator.Object,
                closedSessions: []);

            await m_handler.ApplyAsync(context).ConfigureAwait(false);

            Assert.That(listener.RevalidationInvocationCount, Is.EqualTo(1));
            Assert.That(listener.ClosedChannelIds, Is.Empty);
        }

        [Test]
        public async Task HttpsScopeEffectDrivesChannelRenegotiationOnHttpsScopedListenerAsync()
        {
            Certificate untrustedPeer = CreateCertificate("CN=Untrusted HTTPS Peer");

            // A listener that validates its peer certificates against the
            // HTTPS store (declared via PeerCertificateTrustListScope) must
            // receive an HTTPS-group effect.
            var listener = new FakePeerRotationListener(
                "listener-1",
                new FakeChannel("chan-https", untrustedPeer))
            {
                PeerCertificateTrustListScope = TrustListIdentifier.Https
            };

            // The HTTPS scope must be the one consulted for an HTTPS-group
            // effect; validating against Peers would (incorrectly) accept it.
            Mock<ICertificateValidatorEx> validator = CreateValidator(
                untrusted: [untrustedPeer],
                scope: TrustListIdentifier.Https);

            PushConfigurationTrustListEffectContext context = CreateContext(
                effects: [SecureChannelEffect(TrustListIdentifier.Https)],
                listeners: [listener],
                sessionManager: null,
                validator: validator.Object,
                closedSessions: []);

            await m_handler.ApplyAsync(context).ConfigureAwait(false);

            Assert.That(listener.RevalidationInvocationCount, Is.EqualTo(1));
            Assert.That(listener.ClosedChannelIds, Has.Count.EqualTo(1));
            Assert.That(listener.ClosedChannelIds[0], Is.EqualTo("chan-https"));
        }

        [Test]
        public async Task HttpsScopeEffectLeavesOpcTcpPeersChannelsUntouchedAsync()
        {
            // The scope-routing contract: an HTTPS-group TrustList change must
            // never re-validate or close an opc.tcp (Peers-scoped) listener's
            // channels against the HTTPS store.
            Certificate peer = CreateCertificate("CN=opc.tcp Peer");

            var opcTcpListener = new FakePeerRotationListener(
                "opc-tcp",
                new FakeChannel("chan-peers", peer))
            {
                PeerCertificateTrustListScope = TrustListIdentifier.Peers
            };

            // Even though the peer would be reported untrusted, the listener
            // must be skipped entirely because its scope did not change.
            Mock<ICertificateValidatorEx> validator = CreateValidator(untrusted: [peer]);

            PushConfigurationTrustListEffectContext context = CreateContext(
                effects: [SecureChannelEffect(TrustListIdentifier.Https)],
                listeners: [opcTcpListener],
                sessionManager: null,
                validator: validator.Object,
                closedSessions: []);

            await m_handler.ApplyAsync(context).ConfigureAwait(false);

            Assert.That(
                opcTcpListener.RevalidationInvocationCount,
                Is.Zero,
                "an opc.tcp listener must not be consulted for an HTTPS-group change");
            Assert.That(opcTcpListener.ClosedChannelIds, Is.Empty);
            validator.Verify(
                v => v.ValidateAsync(
                    It.IsAny<Certificate>(),
                    It.IsAny<TrustListIdentifier>(),
                    It.IsAny<CancellationToken>()),
                Times.Never,
                "the opc.tcp peer certificate must never be validated against the HTTPS store");
        }

        [Test]
        public async Task PeersScopeEffectLeavesHttpsChannelsUntouchedAsync()
        {
            // The mirror case: a Peers-group change must never touch an
            // HTTPS-scoped listener's channels.
            Certificate peer = CreateCertificate("CN=HTTPS Peer");

            var httpsListener = new FakePeerRotationListener(
                "https",
                new FakeChannel("chan-https", peer))
            {
                PeerCertificateTrustListScope = TrustListIdentifier.Https
            };

            Mock<ICertificateValidatorEx> validator = CreateValidator(untrusted: [peer]);

            PushConfigurationTrustListEffectContext context = CreateContext(
                effects: [SecureChannelEffect(TrustListIdentifier.Peers)],
                listeners: [httpsListener],
                sessionManager: null,
                validator: validator.Object,
                closedSessions: []);

            await m_handler.ApplyAsync(context).ConfigureAwait(false);

            Assert.That(httpsListener.RevalidationInvocationCount, Is.Zero);
            Assert.That(httpsListener.ClosedChannelIds, Is.Empty);
        }

        [Test]
        public async Task SecureChannelEffectRoutesEachScopeToItsOwnListenerAsync()
        {
            // With both a Peers-scoped and an HTTPS-scoped listener bound, a
            // Peers-only effect must close only the Peers listener's untrusted
            // channel and leave the HTTPS listener entirely untouched.
            Certificate opcTcpPeer = CreateCertificate("CN=opc.tcp Peer");
            Certificate httpsPeer = CreateCertificate("CN=HTTPS Peer");

            var opcTcpListener = new FakePeerRotationListener(
                "opc-tcp",
                new FakeChannel("chan-peers", opcTcpPeer))
            {
                PeerCertificateTrustListScope = TrustListIdentifier.Peers
            };
            var httpsListener = new FakePeerRotationListener(
                "https",
                new FakeChannel("chan-https", httpsPeer))
            {
                PeerCertificateTrustListScope = TrustListIdentifier.Https
            };

            // Both peers would be reported untrusted if consulted; only the
            // Peers listener must actually be driven.
            Mock<ICertificateValidatorEx> validator = CreateValidator(
                untrusted: [opcTcpPeer, httpsPeer]);

            PushConfigurationTrustListEffectContext context = CreateContext(
                effects: [SecureChannelEffect(TrustListIdentifier.Peers)],
                listeners: [opcTcpListener, httpsListener],
                sessionManager: null,
                validator: validator.Object,
                closedSessions: []);

            await m_handler.ApplyAsync(context).ConfigureAwait(false);

            Assert.That(opcTcpListener.RevalidationInvocationCount, Is.EqualTo(1));
            Assert.That(opcTcpListener.ClosedChannelIds, Has.Count.EqualTo(1));
            Assert.That(opcTcpListener.ClosedChannelIds[0], Is.EqualTo("chan-peers"));
            Assert.That(httpsListener.RevalidationInvocationCount, Is.Zero);
            Assert.That(httpsListener.ClosedChannelIds, Is.Empty);
        }

        [Test]
        public async Task UserIdentityTrustEffectClosesSessionsWithUntrustedUserCertificatesAsync()
        {
            Certificate trustedUser = CreateCertificate("CN=Trusted User");
            Certificate untrustedUser = CreateCertificate("CN=Untrusted User");

            ISession certSessionTrusted = CreateCertificateSession(new NodeId(1), trustedUser);
            ISession certSessionUntrusted = CreateCertificateSession(new NodeId(2), untrustedUser);
            ISession usernameSession = CreateUserNameSession(new NodeId(3));

            Mock<ISessionManager> sessionManager = CreateSessionManager(
                certSessionTrusted, certSessionUntrusted, usernameSession);
            Mock<ICertificateValidatorEx> validator = CreateValidator(untrusted: [untrustedUser]);

            var closedSessions = new List<(NodeId SessionId, bool DeleteSubscriptions)>();
            PushConfigurationTrustListEffectContext context = CreateContext(
                effects: [UserIdentityEffect()],
                listeners: [],
                sessionManager: sessionManager.Object,
                validator: validator.Object,
                closedSessionsWithFlag: closedSessions);

            await m_handler.ApplyAsync(context).ConfigureAwait(false);

            Assert.That(closedSessions, Has.Count.EqualTo(1));
            Assert.That(closedSessions[0].SessionId, Is.EqualTo(new NodeId(2)));
            Assert.That(
                closedSessions[0].DeleteSubscriptions,
                Is.True,
                "§7.10.9 requires the invalid Session's Subscriptions are also deleted");
        }

        [Test]
        public async Task UserIdentityTrustEffectLeavesSessionsOpenWhenAllUserCertsStillTrustedAsync()
        {
            Certificate userA = CreateCertificate("CN=User A");
            Certificate userB = CreateCertificate("CN=User B");

            Mock<ISessionManager> sessionManager = CreateSessionManager(
                CreateCertificateSession(new NodeId(1), userA),
                CreateCertificateSession(new NodeId(2), userB));
            Mock<ICertificateValidatorEx> validator = CreateValidator(untrusted: []);

            var closedSessions = new List<NodeId>();
            PushConfigurationTrustListEffectContext context = CreateContext(
                effects: [UserIdentityEffect()],
                listeners: [],
                sessionManager: sessionManager.Object,
                validator: validator.Object,
                closedSessions: closedSessions);

            await m_handler.ApplyAsync(context).ConfigureAwait(false);

            Assert.That(closedSessions, Is.Empty);
        }

        [Test]
        public async Task UserIdentityTrustEffectIgnoresNonCertificateIdentitiesAsync()
        {
            // A user TrustList change must never disturb anonymous / username
            // Sessions — they are not certificate-based identities.
            Mock<ISessionManager> sessionManager = CreateSessionManager(
                CreateUserNameSession(new NodeId(1)),
                CreateAnonymousSession(new NodeId(2)));
            Mock<ICertificateValidatorEx> validator = CreateValidator(untrusted: []);

            var closedSessions = new List<NodeId>();
            PushConfigurationTrustListEffectContext context = CreateContext(
                effects: [UserIdentityEffect()],
                listeners: [],
                sessionManager: sessionManager.Object,
                validator: validator.Object,
                closedSessions: closedSessions);

            await m_handler.ApplyAsync(context).ConfigureAwait(false);

            Assert.That(closedSessions, Is.Empty);
            // The non-certificate identities must not even be re-validated.
            validator.Verify(
                v => v.ValidateAsync(
                    It.IsAny<Certificate>(),
                    It.IsAny<TrustListIdentifier>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Test]
        public async Task NullValidatorSkipsEveryEffectAsync()
        {
            Certificate untrustedPeer = CreateCertificate("CN=Peer");
            var listener = new FakePeerRotationListener("listener-1", new FakeChannel("chan", untrustedPeer));
            Mock<ISessionManager> sessionManager = CreateSessionManager(
                CreateCertificateSession(new NodeId(1), CreateCertificate("CN=User")));

            var closedSessions = new List<NodeId>();
            PushConfigurationTrustListEffectContext context = CreateContext(
                effects: [SecureChannelEffect(TrustListIdentifier.Peers), UserIdentityEffect()],
                listeners: [listener],
                sessionManager: sessionManager.Object,
                validator: null,
                closedSessions: closedSessions);

            await m_handler.ApplyAsync(context).ConfigureAwait(false);

            Assert.That(listener.RevalidationInvocationCount, Is.Zero);
            Assert.That(closedSessions, Is.Empty);
        }

        [Test]
        public async Task EmptyEffectsIsNoOpAsync()
        {
            var listener = new FakePeerRotationListener(
                "listener-1",
                new FakeChannel("chan", CreateCertificate("CN=Peer")));
            Mock<ICertificateValidatorEx> validator = CreateValidator(untrusted: []);

            PushConfigurationTrustListEffectContext context = CreateContext(
                effects: [],
                listeners: [listener],
                sessionManager: null,
                validator: validator.Object,
                closedSessions: []);

            await m_handler.ApplyAsync(context).ConfigureAwait(false);

            Assert.That(listener.RevalidationInvocationCount, Is.Zero);
        }

        [Test]
        public async Task SecureChannelEffectSkipsListenersWithoutTheCapabilityAsync()
        {
            // A listener that does not implement the peer-rotation capability
            // must simply be skipped rather than causing a failure.
            var plainListener = new Mock<ITransportListener>();
            Certificate untrustedPeer = CreateCertificate("CN=Peer");
            var capableListener = new FakePeerRotationListener(
                "listener-2",
                new FakeChannel("chan", untrustedPeer));
            Mock<ICertificateValidatorEx> validator = CreateValidator(untrusted: [untrustedPeer]);

            PushConfigurationTrustListEffectContext context = CreateContext(
                effects: [SecureChannelEffect(TrustListIdentifier.Peers)],
                listeners: [plainListener.Object, capableListener],
                sessionManager: null,
                validator: validator.Object,
                closedSessions: []);

            await m_handler.ApplyAsync(context).ConfigureAwait(false);

            Assert.That(capableListener.ClosedChannelIds, Has.Count.EqualTo(1));
            Assert.That(capableListener.ClosedChannelIds[0], Is.EqualTo("chan"));
        }

        private Certificate CreateCertificate(string subjectName)
        {
            Certificate certificate = CertificateBuilder.Create(subjectName).CreateForRSA();
            m_certificates.Add(certificate);
            return certificate;
        }

        private static TrustListChangeEffect SecureChannelEffect(TrustListIdentifier scope)
        {
            return new TrustListChangeEffect
            {
                TrustListId = new NodeId(Guid.NewGuid(), 1),
                CertificateGroupId = new NodeId(Guid.NewGuid(), 1),
                Kind = TrustListEffectKind.SecureChannelTrust,
                ValidationScope = scope
            };
        }

        private static TrustListChangeEffect UserIdentityEffect()
        {
            return new TrustListChangeEffect
            {
                TrustListId = new NodeId(Guid.NewGuid(), 1),
                CertificateGroupId = new NodeId(Guid.NewGuid(), 1),
                Kind = TrustListEffectKind.UserIdentityTrust,
                ValidationScope = TrustListIdentifier.Users
            };
        }

        private static Mock<ICertificateValidatorEx> CreateValidator(
            IReadOnlyList<Certificate> untrusted,
            TrustListIdentifier? scope = null)
        {
            var untrustedThumbprints = new HashSet<string>(
                untrusted.Select(c => c.Thumbprint),
                StringComparer.OrdinalIgnoreCase);

            var validator = new Mock<ICertificateValidatorEx>();
            validator
                .Setup(v => v.ValidateAsync(
                    It.IsAny<Certificate>(),
                    It.IsAny<TrustListIdentifier>(),
                    It.IsAny<CancellationToken>()))
                .Returns((Certificate certificate, TrustListIdentifier? trustList, CancellationToken ct) =>
                {
                    bool scopeApplies = scope == null || scope.Equals(trustList);
                    bool untrustedInScope = scopeApplies && untrustedThumbprints.Contains(certificate.Thumbprint);
                    CertificateValidationResult result = untrustedInScope
                        ? new CertificateValidationResult(
                            false, StatusCodes.BadCertificateUntrusted, [], false)
                        : CertificateValidationResult.Success;
                    return Task.FromResult(result);
                });
            return validator;
        }

        private static Mock<ISessionManager> CreateSessionManager(params ISession[] sessions)
        {
            var manager = new Mock<ISessionManager>();
            manager.Setup(m => m.GetSessions()).Returns([.. sessions]);
            return manager;
        }

        private static ISession CreateCertificateSession(NodeId sessionId, Certificate userCertificate)
        {
            var handler = new Mock<IUserIdentityTokenHandler>();
            handler.Setup(h => h.TokenType).Returns(UserTokenType.Certificate);
            handler.Setup(h => h.Token).Returns(new X509IdentityToken
            {
                CertificateData = userCertificate.RawData.ToByteString()
            });

            return CreateSession(sessionId, handler.Object);
        }

        private static ISession CreateUserNameSession(NodeId sessionId)
        {
            var handler = new Mock<IUserIdentityTokenHandler>();
            handler.Setup(h => h.TokenType).Returns(UserTokenType.UserName);
            handler.Setup(h => h.Token).Returns(new UserNameIdentityToken { UserName = "user" });

            return CreateSession(sessionId, handler.Object);
        }

        private static ISession CreateAnonymousSession(NodeId sessionId)
        {
            var handler = new Mock<IUserIdentityTokenHandler>();
            handler.Setup(h => h.TokenType).Returns(UserTokenType.Anonymous);
            handler.Setup(h => h.Token).Returns(new AnonymousIdentityToken());

            return CreateSession(sessionId, handler.Object);
        }

        private static ISession CreateSession(NodeId sessionId, IUserIdentityTokenHandler identityToken)
        {
            var session = new Mock<ISession>();
            session.Setup(s => s.Id).Returns(sessionId);
            session.Setup(s => s.IdentityToken).Returns(identityToken);
            return session.Object;
        }

        private static PushConfigurationTrustListEffectContext CreateContext(
            IReadOnlyList<TrustListChangeEffect> effects,
            IReadOnlyList<ITransportListener> listeners,
            ISessionManager? sessionManager,
            ICertificateValidatorEx? validator,
            List<NodeId>? closedSessions = null,
            List<(NodeId SessionId, bool DeleteSubscriptions)>? closedSessionsWithFlag = null)
        {
            return new PushConfigurationTrustListEffectContext
            {
                Effects = effects,
                TransportListeners = listeners,
                SessionManager = sessionManager,
                CertificateValidator = validator,
                CloseSessionAsync = (sessionId, deleteSubscriptions, ct) =>
                {
                    closedSessions?.Add(sessionId);
                    closedSessionsWithFlag?.Add((sessionId, deleteSubscriptions));
                    return default;
                }
            };
        }

#pragma warning disable CS0067 // events are required by ITransportListener but unused in the fake
        private sealed class FakePeerRotationListener
            : ITransportListener, ITransportListenerPeerCertificateRotation
        {
            private readonly List<FakeChannel> m_channels;

            public FakePeerRotationListener(string listenerId, params FakeChannel[] channels)
            {
                ListenerId = listenerId;
                m_channels = [.. channels];
            }

            public int RevalidationInvocationCount { get; private set; }

            public List<string> ClosedChannelIds { get; } = [];

            public TrustListIdentifier PeerCertificateTrustListScope { get; init; }
                = TrustListIdentifier.Peers;

            public async ValueTask<IReadOnlyList<string>> CloseChannelsForUntrustedPeersAsync(
                Func<Certificate, CancellationToken, ValueTask<bool>> isPeerTrustedAsync,
                CancellationToken ct = default)
            {
                RevalidationInvocationCount++;
                var closed = new List<string>();
                foreach (FakeChannel channel in m_channels)
                {
                    if (channel.Closed || channel.PeerCertificate == null)
                    {
                        continue;
                    }

                    bool trusted = await isPeerTrustedAsync(channel.PeerCertificate, ct).ConfigureAwait(false);
                    if (trusted)
                    {
                        continue;
                    }

                    channel.Closed = true;
                    closed.Add(channel.GlobalChannelId);
                    ClosedChannelIds.Add(channel.GlobalChannelId);
                }

                return closed;
            }

            public string ListenerId { get; }

            public string UriScheme => Utils.UriSchemeOpcTcp;

            public event ConnectionWaitingHandlerAsync? ConnectionWaiting;

            public event EventHandler<ConnectionStatusEventArgs>? ConnectionStatusChanged;

            public ValueTask OpenAsync(
                Uri baseAddress,
                TransportListenerSettings settings,
                ITransportListenerCallback callback,
                CancellationToken ct = default)
            {
                return default;
            }

            public ValueTask CloseAsync(CancellationToken ct = default)
            {
                return default;
            }

            public void CertificateUpdate(
                ICertificateValidatorEx validator,
                ICertificateRegistry serverCertificates)
            {
            }

            public void CreateReverseConnection(Uri url, int timeout)
            {
            }

            public void UpdateChannelLastActiveTime(string globalChannelId)
            {
            }

            public ValueTask DisposeAsync()
            {
                return default;
            }
        }
#pragma warning restore CS0067

        private sealed class FakeChannel
        {
            public FakeChannel(string globalChannelId, Certificate? peerCertificate)
            {
                GlobalChannelId = globalChannelId;
                PeerCertificate = peerCertificate;
            }

            public string GlobalChannelId { get; }

            public Certificate? PeerCertificate { get; }

            public bool Closed { get; set; }
        }
    }
}
