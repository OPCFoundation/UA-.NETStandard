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
 *
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
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Time.Testing;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client.Tests.Stack.Client.Fakes;
using Opc.Ua.Security.Certificates;
using ManagedClient = Opc.Ua.Client.ManagedSession;

namespace Opc.Ua.Client.Tests
{
    /// <summary>
    /// Exercises communication-recovery admission through real managed/raw sessions.
    /// </summary>
    [TestFixture]
    [Parallelizable]
    public sealed class ManagedSessionRecoveryAdmissionTests
    {
        /// <summary>
        /// A healthy keep-alive resets the consecutive failure count before recovery.
        /// </summary>
        [TestCase(1u, false)]
        [TestCase(3u, false)]
        [TestCase(3u, true)]
        public async Task KeepAliveRecoveryWaitsForConsecutiveFailuresAsync(uint threshold, bool interruptFailures)
        {
            await using var channels = new SessionChannelHarness(timeProvider: new FakeTimeProvider());
            ReportingSession? raw = null;
            await using ManagedClient session = await ConnectAsync(channels,
                new ManagedSessionOptions { KeepAliveFailureThreshold = threshold },
                (created, _) => raw = created).ConfigureAwait(false);
            Assert.That(raw, Is.Not.Null);
            raw!.KeepAliveInterval = 3_600_000;
            int recoveries = 0;
            int notifications = 0;
            var recovered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            session.ConnectionStateChanged += (_, change) =>
            {
                if (change.NewState == ConnectionState.Reconnecting)
                {
                    Interlocked.Increment(ref recoveries);
                }
                if (change.NewState == ConnectionState.Connected)
                {
                    recovered.TrySetResult(true);
                }
            };
            session.KeepAlive += (_, args) =>
            {
                if (args.Status != null && ServiceResult.IsBad(args.Status))
                {
                    Interlocked.Increment(ref notifications);
                }
            };

            for (uint i = 1; i < threshold; i++)
            {
                raw.ReportKeepAlive(false);
            }
            Assert.That(Volatile.Read(ref recoveries), Is.Zero);
            if (interruptFailures)
            {
                raw.ReportKeepAlive(true);
                for (uint i = 1; i < threshold; i++)
                {
                    raw.ReportKeepAlive(false);
                }
                Assert.That(Volatile.Read(ref recoveries), Is.Zero);
            }
            raw.ReportKeepAlive(false);
            await recovered.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);

            Assert.That(Volatile.Read(ref recoveries), Is.EqualTo(1));
            Assert.That(Volatile.Read(ref notifications), Is.EqualTo(interruptFailures ? (2 * threshold) - 1 : threshold));
        }

        /// <summary>
        /// Disabling automatic recovery does not suppress events or explicitly requested reconnects.
        /// </summary>
        [Test]
        public async Task DisabledAutomaticRecoveryStillAllowsManualReconnectAsync()
        {
            await using var channels = new SessionChannelHarness(timeProvider: new FakeTimeProvider());
            ReportingSession? raw = null;
            ScriptedChannel? transport = null;
            await using ManagedClient session = await ConnectAsync(channels,
                new ManagedSessionOptions { ReconnectOnCommunicationFailure = false },
                (created, channel) => { raw = created; transport = channel; }).ConfigureAwait(false);
            Assert.That(raw, Is.Not.Null);
            Assert.That(transport, Is.Not.Null);
            raw!.KeepAliveInterval = 3_600_000;
            int notifications = 0;
            session.KeepAlive += (_, args) =>
            {
                if (args.Status != null && ServiceResult.IsBad(args.Status))
                {
                    Interlocked.Increment(ref notifications);
                }
            };

            for (int i = 0; i < 5; i++)
            {
                raw.ReportKeepAlive(false);
            }
            Assert.That(transport!.ReconnectCount, Is.Zero);
            Assert.That(session.Reconnecting, Is.False);
            Assert.That(Volatile.Read(ref notifications), Is.EqualTo(5));

            await session.ReconnectAsync(ct: CancellationToken.None)
                .WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);

            Assert.That(transport.ReconnectCount, Is.EqualTo(1));
            Assert.That(session.Connected, Is.True);
        }

        /// <summary>
        /// The largest supported threshold does not overflow or admit an early recovery.
        /// </summary>
        [Test]
        public async Task MaximumFailureThresholdDoesNotAdmitEarlyRecoveryAsync()
        {
            await using var channels = new SessionChannelHarness(timeProvider: new FakeTimeProvider());
            ReportingSession? raw = null;
            ScriptedChannel? transport = null;
            await using ManagedClient session = await ConnectAsync(channels,
                new ManagedSessionOptions { KeepAliveFailureThreshold = uint.MaxValue },
                (created, channel) => { raw = created; transport = channel; }).ConfigureAwait(false);
            raw!.KeepAliveInterval = 3_600_000;

            for (int i = 0; i < 10; i++)
            {
                raw.ReportKeepAlive(false);
            }

            Assert.That(session.Reconnecting, Is.False);
            Assert.That(transport!.ReconnectCount, Is.Zero);
        }

        /// <summary>
        /// Automatic communication recovery admission does not disable retries needed to create the initial session.
        /// </summary>
        [Test]
        public async Task InitialConnectionRetriesRemainAvailableWhenAutomaticRecoveryIsDisabledAsync()
        {
            await using var channels = new SessionChannelHarness(timeProvider: new FakeTimeProvider());
            int attempts = 0;
            await using ManagedClient session = await ConnectAsync(channels,
                new ManagedSessionOptions { ReconnectOnCommunicationFailure = false },
                (_, _) => { },
                () =>
                {
                    if (Interlocked.Increment(ref attempts) == 1)
                    {
                        throw new ServiceResultException(StatusCodes.BadConnectionClosed);
                    }
                }).ConfigureAwait(false);

            Assert.That(session.Connected, Is.True);
            Assert.That(Volatile.Read(ref attempts), Is.EqualTo(2));
        }

        /// <summary>
        /// Invalid thresholds are rejected before attempting a connection.
        /// </summary>
        [Test]
        public async Task ZeroThresholdIsRejectedBeforeConnectAsync()
        {
            await using var channels = new SessionChannelHarness();
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
                await ConnectAsync(channels, new ManagedSessionOptions { KeepAliveFailureThreshold = 0 },
                    (_, _) => Assert.Fail("Invalid options must not create a session.")).ConfigureAwait(false))
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Shared-channel recovery cannot silently bypass non-default session admission settings.
        /// </summary>
        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public async Task SharedChannelManagerRejectsCustomAdmissionBeforeConnectAsync(bool threshold, bool factoryWrapped)
        {
            await using var channels = new SessionChannelHarness();
            ISessionFactory factory = factoryWrapped
                ? new ChannelManagerSessionFactory(channels.Manager, channels.Telemetry)
                : new DefaultSessionFactory(channels.Telemetry);
            var options = new ManagedSessionOptions
            {
                Endpoint = SessionChannelHarness.CreateEndpoint(),
                ReconnectOnCommunicationFailure = threshold,
                KeepAliveFailureThreshold = threshold ? 3u : 1u
            };

            await Assert.ThrowsAsync<ArgumentException>(async () =>
                await ManagedClient.CreateAsync(options, channels.Configuration, factory,
                    channelManager: factoryWrapped ? null : channels.Manager).ConfigureAwait(false))
                .ConfigureAwait(false);

            Assert.That(channels.CreatedChannels, Is.Empty);
        }

        /// <summary>
        /// Explicitly selecting a session-owned transport leaves the supplied manager unused.
        /// </summary>
        [Test]
        public async Task SessionOwnedTransportDoesNotUseTheSuppliedChannelManagerAsync()
        {
            await using var channels = new SessionChannelHarness(timeProvider: new FakeTimeProvider());
            ReportingSession? raw = null;
            var factory = new StandaloneSessionFactory(channels, (created, _) => raw = created);
            var options = new ManagedSessionOptions
            {
                Endpoint = SessionChannelHarness.CreateEndpoint(),
                TimeProvider = channels.TimeProvider,
                ReconnectOnCommunicationFailure = false,
                UseSharedChannelManager = false
            };

            await using ManagedClient session = await ManagedClient.CreateAsync(options, channels.Configuration,
                factory, channelManager: channels.Manager, updateBeforeConnect: false)
                .WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);

            Assert.That(raw, Is.Not.Null);
            Assert.That(raw!.ChannelManager, Is.Null);
            Assert.That(channels.CreatedChannels, Is.Empty);
        }

        /// <summary>
        /// An explicit replacement cannot reintroduce independent channel recovery into a restricted session.
        /// </summary>
        [Test]
        public async Task ManualReconnectCannotBypassSessionOwnedTransportSelectionAsync()
        {
            await using var channels = new SessionChannelHarness(timeProvider: new FakeTimeProvider());
            ReportingSession? raw = null;
            await using ManagedClient session = await ConnectAsync(channels,
                new ManagedSessionOptions { ReconnectOnCommunicationFailure = false },
                (created, _) => raw = created).ConfigureAwait(false);
            var replacement = new Mock<ITransportChannel>(MockBehavior.Strict);
            replacement.As<IManagedTransportChannel>();
            ITransportChannel original = raw!.TransportChannel;

            await Assert.ThrowsAsync<ArgumentException>(async () =>
                await session.ReconnectAsync(null, replacement.Object).ConfigureAwait(false)).ConfigureAwait(false);

            Assert.That(raw.TransportChannel, Is.SameAs(original));
            replacement.VerifyNoOtherCalls();
        }

        /// <summary>
        /// An opaque custom factory returning a managed channel is rejected and cleaned up before exposure.
        /// </summary>
        [Test]
        public async Task OpaqueManagedChannelFactoryIsRejectedWithoutDisposingBorrowedManagerAsync()
        {
            await using var channels = new SessionChannelHarness();
            ConfiguredEndpoint endpoint = SessionChannelHarness.CreateEndpoint();
            await using Session peer = await channels.CreateSessionAsync(endpoint).ConfigureAwait(false);
            Session returned = await channels.CreateSessionAsync(endpoint).ConfigureAwait(false);
            var factory = new Mock<ISessionFactory>();
            factory.SetupGet(value => value.Telemetry).Returns(channels.Telemetry);
            factory.SetupGet(value => value.SubscriptionEngineFactory).Returns(DefaultSubscriptionEngineFactory.Instance);
            factory.Setup(value => value.CreateAsync(
                It.IsAny<ApplicationConfiguration>(), It.IsAny<ConfiguredEndpoint>(),
                It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<string>(), It.IsAny<uint>(),
                It.IsAny<IUserIdentity>(), It.IsAny<ArrayOf<string>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(returned);
            var options = new ManagedSessionOptions
            {
                Endpoint = endpoint,
                ReconnectOnCommunicationFailure = false
            };

            await Assert.ThrowsAsync<ArgumentException>(async () =>
                await ManagedClient.CreateAsync(options, channels.Configuration, factory.Object,
                    updateBeforeConnect: false).WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false))
                .ConfigureAwait(false);

            Assert.That(returned.Disposed, Is.True);
            Assert.That(peer.Connected, Is.True);
            Assert.That(channels.CreatedChannels[0].DisposeCount, Is.Zero);
        }

        /// <summary>
        /// Connects with the normal managed-session API using a scripted, session-owned transport.
        /// </summary>
        private static Task<ManagedClient> ConnectAsync(SessionChannelHarness channels, ManagedSessionOptions options,
            Action<ReportingSession, ScriptedChannel> onSession, Action? beforeCreateChannel = null)
        {
            return ManagedClient.CreateAsync(options with
            {
                Endpoint = SessionChannelHarness.CreateEndpoint(),
                TimeProvider = channels.TimeProvider,
                ReconnectPolicy = new ReconnectPolicyOptions
                {
                    InitialDelay = TimeSpan.Zero,
                    MaxDelay = TimeSpan.FromMilliseconds(1),
                    MaxRetries = 1,
                    JitterFactor = 0
                }
            }, channels.Configuration, new StandaloneSessionFactory(channels, onSession, beforeCreateChannel),
                updateBeforeConnect: false).WaitAsync(TimeSpan.FromSeconds(10));
        }

        /// <summary>
        /// Creates real sessions over the existing network-free transport fixture.
        /// </summary>
        private sealed class StandaloneSessionFactory(
            SessionChannelHarness channels,
            Action<ReportingSession, ScriptedChannel> onSession,
            Action? beforeCreateChannel = null) : DefaultSessionFactory(channels.Telemetry)
        {
            private ScriptedChannel? m_channel;

            /// <inheritdoc/>
            public override Task<ITransportChannel> CreateChannelAsync(ApplicationConfiguration configuration,
                ITransportWaitingConnection? connection, ConfiguredEndpoint endpoint,
                bool updateBeforeConnect, bool checkDomain, CancellationToken ct = default)
            {
                ct.ThrowIfCancellationRequested();
                beforeCreateChannel?.Invoke();
                m_channel = channels.CreateOpenedStandaloneChannel(endpoint);
                m_channel.SupportedFeatures = TransportChannelFeatures.Reconnect;
                return Task.FromResult(m_channel.Channel);
            }

            /// <inheritdoc/>
            public override ISession Create(ITransportChannel channel, ApplicationConfiguration configuration,
                ConfiguredEndpoint endpoint, Certificate? clientCertificate = null,
                CertificateCollection? clientCertificateChain = null,
                ArrayOf<EndpointDescription> availableEndpoints = default, ArrayOf<string> discoveryProfileUris = default)
            {
                var session = new ReportingSession(channel, configuration, endpoint, clientCertificate,
                    clientCertificateChain, availableEndpoints, discoveryProfileUris, SubscriptionEngineFactory, TimeProvider)
                {
                    SessionFactory = this,
                    ReturnDiagnostics = ReturnDiagnostics
                };
                onSession(session, m_channel!);
                return session;
            }
        }

        /// <summary>
        /// Exposes supported protected keep-alive hooks without reflection or state-machine interception.
        /// </summary>
        private sealed class ReportingSession(
            ITransportChannel channel, ApplicationConfiguration configuration, ConfiguredEndpoint endpoint,
            Certificate? certificate, CertificateCollection? chain, ArrayOf<EndpointDescription> endpoints,
            ArrayOf<string> profiles, ISubscriptionEngineFactory? engineFactory, TimeProvider? timeProvider)
            : Session(channel, configuration, endpoint, certificate, chain, endpoints, profiles, engineFactory, timeProvider)
        {
            /// <summary>
            /// Reports a healthy or failed keep-alive through the real session event path.
            /// </summary>
            public void ReportKeepAlive(bool healthy)
            {
                if (healthy)
                {
                    OnKeepAlive(ServerState.Running, DateTime.UtcNow);
                }
                else
                {
                    OnKeepAliveError(new ServiceResult(StatusCodes.BadNoCommunication));
                }
            }
        }
    }
}
