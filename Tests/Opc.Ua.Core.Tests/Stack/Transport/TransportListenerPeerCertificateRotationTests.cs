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

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Bindings;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Tests;

namespace Opc.Ua.Core.Tests.Stack.Transport
{
    /// <summary>
    /// Unit tests for the
    /// <see cref="ITransportListenerPeerCertificateRotation"/> capability
    /// added to <see cref="TcpTransportListener"/> per OPC UA Part 12
    /// §7.10.9 (ApplyChanges → force renegotiate the SecureChannels whose
    /// peer certificate is no longer trusted after a committed TrustList
    /// change).
    /// </summary>
    [TestFixture]
    [Category("TransportListenerCertificateRotation")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class TransportListenerPeerCertificateRotationTests
    {
        private ITelemetryContext m_telemetry;
        private BufferManager m_buffers;
        private ChannelQuotas m_quotas;

        [OneTimeSetUp]
        protected void OneTimeSetUp()
        {
            m_telemetry = NUnitTelemetryContext.Create();
            m_buffers = new BufferManager("peer-rotation-test", 8192, m_telemetry);
            m_quotas = new ChannelQuotas(ServiceMessageContext.Create(m_telemetry));
        }

        /// <summary>
        /// The TCP listener implements the optional peer-trust rotation
        /// capability so <c>ConfigurationNodeManager.ApplyChanges</c> can fan
        /// out post-response channel cuts for untrusted peers, and advertises
        /// the <see cref="TrustListIdentifier.Peers"/> scope so an
        /// HTTPS-group change is never routed to it.
        /// </summary>
        [Test]
        public async Task TcpTransportListenerImplementsPeerCertificateRotationCapabilityAsync()
        {
            await using var listener = new TcpTransportListener(m_telemetry);
            Assert.That(listener, Is.InstanceOf<ITransportListenerPeerCertificateRotation>());
            Assert.That(
                ((ITransportListenerPeerCertificateRotation)listener).PeerCertificateTrustListScope,
                Is.EqualTo(TrustListIdentifier.Peers));
        }

        /// <summary>
        /// Calling
        /// <see cref="ITransportListenerPeerCertificateRotation.CloseChannelsForUntrustedPeersAsync"/>
        /// on a fresh, never-opened TCP listener with no channels must return
        /// an empty list rather than throwing or invoking the predicate.
        /// </summary>
        [Test]
        public async Task TcpListenerCloseChannelsForUntrustedPeersOnEmptyListenerReturnsEmptyAsync()
        {
            await using var listener = new TcpTransportListener(m_telemetry);
            bool predicateInvoked = false;

            System.Collections.Generic.IReadOnlyList<string> closed = await CallCloseChannelsForUntrustedPeersAsync(
                listener,
                (_, _) =>
                {
                    predicateInvoked = true;
                    return new ValueTask<bool>(true);
                }).ConfigureAwait(false);

            Assert.That(closed, Is.Not.Null);
            Assert.That(closed, Is.Empty);
            Assert.That(predicateInvoked, Is.False, "predicate must not be invoked when there are no channels");
        }

        /// <summary>
        /// Passing a <c>null</c> trust callback is a contract violation and
        /// must surface an <see cref="ArgumentNullException"/> synchronously
        /// rather than corrupting the channel map.
        /// </summary>
        [Test]
        public async Task TcpListenerCloseChannelsForUntrustedPeersRejectsNullPredicateAsync()
        {
            await using var listener = new TcpTransportListener(m_telemetry);

            Assert.That(
                () => CallCloseChannelsForUntrustedPeersAsync(listener, null!),
                Throws.InstanceOf<ArgumentNullException>());
        }

        /// <summary>
        /// With a mix of tracked channels — an untrusted peer, a still-trusted
        /// peer, and a no-client-certificate (e.g. SecurityPolicy.None)
        /// channel — the classic TCP listener force-closes only the untrusted
        /// peer's channel, returns its global channel id, and leaves the
        /// others open.
        /// </summary>
        [Test]
        public async Task TcpListenerClosesOnlyUntrustedPeerChannelsAsync()
        {
            await using var listener = new TcpTransportListener(m_telemetry);

            // The channels take ownership of these certificates and dispose
            // them via DisposeChannels; do not wrap them in `using` (that would
            // double-dispose the shared handle).
            Certificate trustedCert = CreateSelfSigned("CN=Trusted Peer");
            Certificate untrustedCert = CreateSelfSigned("CN=Untrusted Peer");
            string untrustedThumbprint = untrustedCert.Thumbprint;

            RevalidationTestChannel trusted = CreateOpenChannel("trusted", 1, trustedCert);
            RevalidationTestChannel untrusted = CreateOpenChannel("untrusted", 2, untrustedCert);
            RevalidationTestChannel noCert = CreateOpenChannel("nocert", 3, peerCertificate: null);

            var channels = new List<RevalidationTestChannel> { trusted, untrusted, noCert };
            try
            {
                InjectClassicChannels(listener, (1, trusted), (2, untrusted), (3, noCert));

                IReadOnlyList<string> closed = await CallCloseChannelsForUntrustedPeersAsync(
                    listener,
                    (certificate, _) => new ValueTask<bool>(
                        !string.Equals(
                            certificate.Thumbprint,
                            untrustedThumbprint,
                            StringComparison.OrdinalIgnoreCase))).ConfigureAwait(false);

                Assert.That(closed, Has.Count.EqualTo(1));
                Assert.That(closed[0], Is.EqualTo(untrusted.GlobalChannelId));
                Assert.That(untrusted.CurrentState, Is.EqualTo(TcpChannelState.Faulted));
                Assert.That(trusted.CurrentState, Is.EqualTo(TcpChannelState.Open));
                Assert.That(noCert.CurrentState, Is.EqualTo(TcpChannelState.Open));
            }
            finally
            {
                DisposeChannels(channels);
            }
        }

#if HAS_KESTREL_TCP_LISTENER
        /// <summary>
        /// The Kestrel-hosted opc.tcp listener also implements the peer-trust
        /// rotation capability with the <see cref="TrustListIdentifier.Peers"/>
        /// scope.
        /// </summary>
        [Test]
        public async Task KestrelTransportListenerImplementsPeerCertificateRotationCapabilityAsync()
        {
            await using var listener = new KestrelTcpTransportListener(m_telemetry);
            Assert.That(listener, Is.InstanceOf<ITransportListenerPeerCertificateRotation>());
            Assert.That(
                ((ITransportListenerPeerCertificateRotation)listener).PeerCertificateTrustListScope,
                Is.EqualTo(TrustListIdentifier.Peers));
        }

        /// <summary>
        /// A never-opened Kestrel listener has no channel map, so the call
        /// must return an empty list without invoking the predicate.
        /// </summary>
        [Test]
        public async Task KestrelListenerCloseChannelsForUntrustedPeersOnEmptyListenerReturnsEmptyAsync()
        {
            await using var listener = new KestrelTcpTransportListener(m_telemetry);
            bool predicateInvoked = false;

            IReadOnlyList<string> closed = await CallCloseChannelsForUntrustedPeersAsync(
                listener,
                (_, _) =>
                {
                    predicateInvoked = true;
                    return new ValueTask<bool>(true);
                }).ConfigureAwait(false);

            Assert.That(closed, Is.Not.Null);
            Assert.That(closed, Is.Empty);
            Assert.That(predicateInvoked, Is.False, "predicate must not be invoked when there are no channels");
        }

        /// <summary>
        /// The Kestrel listener rejects a <c>null</c> trust callback with an
        /// <see cref="ArgumentNullException"/>.
        /// </summary>
        [Test]
        public async Task KestrelListenerCloseChannelsForUntrustedPeersRejectsNullPredicateAsync()
        {
            await using var listener = new KestrelTcpTransportListener(m_telemetry);

            Assert.That(
                () => CallCloseChannelsForUntrustedPeersAsync(listener, null!),
                Throws.InstanceOf<ArgumentNullException>());
        }

        /// <summary>
        /// The Kestrel listener force-closes only the untrusted peer's tracked
        /// channel, reusing the same shared snapshot/close helpers as the
        /// classic listener.
        /// </summary>
        [Test]
        public async Task KestrelListenerClosesOnlyUntrustedPeerChannelsAsync()
        {
            await using var listener = new KestrelTcpTransportListener(m_telemetry);

            // The channels take ownership of these certificates and dispose
            // them via DisposeChannels; do not wrap them in `using` (that would
            // double-dispose the shared handle).
            Certificate trustedCert = CreateSelfSigned("CN=Trusted Peer");
            Certificate untrustedCert = CreateSelfSigned("CN=Untrusted Peer");
            string untrustedThumbprint = untrustedCert.Thumbprint;

            RevalidationTestChannel trusted = CreateOpenChannel("trusted", 1, trustedCert);
            RevalidationTestChannel untrusted = CreateOpenChannel("untrusted", 2, untrustedCert);
            RevalidationTestChannel noCert = CreateOpenChannel("nocert", 3, peerCertificate: null);

            var channels = new List<RevalidationTestChannel> { trusted, untrusted, noCert };
            try
            {
                InjectKestrelChannels(listener, (1, trusted), (2, untrusted), (3, noCert));

                IReadOnlyList<string> closed = await CallCloseChannelsForUntrustedPeersAsync(
                    listener,
                    (certificate, _) => new ValueTask<bool>(
                        !string.Equals(
                            certificate.Thumbprint,
                            untrustedThumbprint,
                            StringComparison.OrdinalIgnoreCase))).ConfigureAwait(false);

                Assert.That(closed, Has.Count.EqualTo(1));
                Assert.That(closed[0], Is.EqualTo(untrusted.GlobalChannelId));
                Assert.That(untrusted.CurrentState, Is.EqualTo(TcpChannelState.Faulted));
                Assert.That(trusted.CurrentState, Is.EqualTo(TcpChannelState.Open));
                Assert.That(noCert.CurrentState, Is.EqualTo(TcpChannelState.Open));
            }
            finally
            {
                DisposeChannels(channels);
            }
        }
#endif

        private RevalidationTestChannel CreateOpenChannel(
            string contextId,
            uint channelId,
            Certificate peerCertificate)
        {
            var listenerMock = new Mock<ITcpChannelListener>();
            listenerMock.Setup(l => l.EndpointUrl).Returns(new Uri("opc.tcp://localhost:4840"));
            listenerMock.Setup(l => l.ChannelClosed(It.IsAny<uint>()));

            var channel = new RevalidationTestChannel(
                contextId,
                listenerMock.Object,
                m_buffers,
                m_quotas,
                m_telemetry);
            channel.AssignChannelId(channelId);
            channel.SetPeerCertificate(peerCertificate);
            channel.MarkOpen();
            return channel;
        }

        private static void DisposeChannels(IEnumerable<RevalidationTestChannel> channels)
        {
            foreach (RevalidationTestChannel channel in channels)
            {
                channel.Dispose();
            }
        }

        private static void InjectClassicChannels(
            TcpTransportListener listener,
            params (uint Id, TcpListenerChannel Channel)[] channels)
        {
            var map = new ConcurrentDictionary<uint, TcpListenerChannel>();
            foreach ((uint id, TcpListenerChannel channel) in channels)
            {
                map[id] = channel;
            }

            SetChannelMap(typeof(TcpTransportListener), listener, map);
        }

#if HAS_KESTREL_TCP_LISTENER
        private static void InjectKestrelChannels(
            KestrelTcpTransportListener listener,
            params (uint Id, TcpListenerChannel Channel)[] channels)
        {
            var map = new ConcurrentDictionary<uint, (TcpListenerChannel, TaskCompletionSource<bool>)>();
            foreach ((uint id, TcpListenerChannel channel) in channels)
            {
                map[id] = (channel, new TaskCompletionSource<bool>());
            }

            SetChannelMap(typeof(KestrelTcpTransportListener), listener, map);
        }
#endif

        private static void SetChannelMap(Type listenerType, object listener, object map)
        {
            FieldInfo field = listenerType.GetField(
                "m_channels", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException($"Field m_channels not found on {listenerType.Name}.");
            field.SetValue(listener, map);
        }

        private static Certificate CreateSelfSigned(string subjectName)
        {
            return CertificateBuilder.Create(subjectName).CreateForRSA();
        }

        /// <summary>
        /// A concrete <see cref="TcpListenerChannel"/> that exposes the
        /// protected state/id/client-certificate setters so a channel can be
        /// deterministically staged into an <see cref="Open"/> state with a
        /// negotiated peer certificate for revalidation tests, without opening
        /// a real socket.
        /// </summary>
        private sealed class RevalidationTestChannel : TcpListenerChannel
        {
            public RevalidationTestChannel(
                string contextId,
                ITcpChannelListener listener,
                BufferManager bufferManager,
                ChannelQuotas quotas,
                ITelemetryContext telemetry)
                : base(
                    contextId,
                    listener,
                    bufferManager,
                    quotas,
                    null!,
                    new List<EndpointDescription>(),
                    telemetry)
            {
            }

            public TcpChannelState CurrentState => State;

            public void MarkOpen()
            {
                State = TcpChannelState.Open;
            }

            public void AssignChannelId(uint channelId)
            {
                ChannelId = channelId;
            }

            public void SetPeerCertificate(Certificate peerCertificate)
            {
                ClientCertificate = peerCertificate;
            }
        }

        private static async Task<System.Collections.Generic.IReadOnlyList<string>>
            CallCloseChannelsForUntrustedPeersAsync(
                ITransportListener listener,
                Func<Certificate, CancellationToken, ValueTask<bool>> isPeerTrustedAsync)
        {
            var rotation = (ITransportListenerPeerCertificateRotation)listener;
            return await rotation.CloseChannelsForUntrustedPeersAsync(isPeerTrustedAsync).ConfigureAwait(false);
        }
    }
}
