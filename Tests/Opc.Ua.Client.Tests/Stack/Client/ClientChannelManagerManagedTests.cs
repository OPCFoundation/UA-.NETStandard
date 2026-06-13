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

#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Diagnostics.Tracing;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Time.Testing;
using Moq;
using NUnit.Framework;
using Opc.Ua.Bindings;
using Opc.Ua.Client;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Tests;

// CA2000: test code; many disposables are ownership-transferred to test fixtures or short-lived.
#pragma warning disable CA2000

namespace Opc.Ua.Client.Tests.Stack.Client
{
    /// <summary>
    /// Unit tests for the managed (sharing, refcount, coalesced
    /// reconnect, participant notification) behavior of
    /// <see cref="ClientChannelManager"/> implementing
    /// <see cref="IClientChannelManager"/>.
    /// </summary>
    [TestFixture]
    [Category("Client")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public sealed class ClientChannelManagerManagedTests
    {
        private static readonly ICertificateFactory s_factory = DefaultCertificateFactory.Instance;

        [Test]
        public void ChannelKeyEqualityIsValueBased()
        {
            using Certificate serverCert = s_factory.CreateCertificate("CN=server").CreateForRSA();
            ConfiguredEndpoint endpoint1 = GetTestEndpoint(serverCert);
            ConfiguredEndpoint endpoint2 = GetTestEndpoint(serverCert);

            ManagedChannelKey k1 = ManagedChannelKey.FromEndpoint(endpoint1);
            ManagedChannelKey k2 = ManagedChannelKey.FromEndpoint(endpoint2);

            Assert.That(k1, Is.EqualTo(k2));
            Assert.That(k1.GetHashCode(), Is.EqualTo(k2.GetHashCode()));
        }

        [Test]
        public void ChannelKeyDistinguishesReverseFromForward()
        {
            using Certificate serverCert = s_factory.CreateCertificate("CN=server").CreateForRSA();
            ConfiguredEndpoint endpoint = GetTestEndpoint(serverCert);

            ManagedChannelKey forward = ManagedChannelKey.FromEndpoint(endpoint);
            ManagedChannelKey reverse = ManagedChannelKey.FromEndpoint(
                endpoint,
                reverseConnectionIdentity: new object());

            Assert.That(forward, Is.Not.EqualTo(reverse));
        }

        [Test]
        public void ChannelKeyDistinguishesDifferentReverseHandles()
        {
            using Certificate serverCert = s_factory.CreateCertificate("CN=server").CreateForRSA();
            ConfiguredEndpoint endpoint = GetTestEndpoint(serverCert);

            ManagedChannelKey r1 = ManagedChannelKey.FromEndpoint(endpoint, reverseConnectionIdentity: new object());
            ManagedChannelKey r2 = ManagedChannelKey.FromEndpoint(endpoint, reverseConnectionIdentity: new object());

            Assert.That(r1, Is.Not.EqualTo(r2));
        }

        [Test]
        public void ExponentialBackoffPolicyDoublesWithCap()
        {
            var policy = new ExponentialBackoffChannelReconnectPolicy
            {
                MinDelay = TimeSpan.FromMilliseconds(100),
                MaxDelay = TimeSpan.FromSeconds(1)
            };

            Assert.That(policy.GetDelay(0).TotalMilliseconds, Is.EqualTo(100));
            Assert.That(policy.GetDelay(1).TotalMilliseconds, Is.EqualTo(200));
            Assert.That(policy.GetDelay(2).TotalMilliseconds, Is.EqualTo(400));
            Assert.That(policy.GetDelay(3).TotalMilliseconds, Is.EqualTo(800));
            Assert.That(policy.GetDelay(4).TotalMilliseconds, Is.EqualTo(1000)); // capped
            Assert.That(policy.GetDelay(10).TotalMilliseconds, Is.EqualTo(1000));
        }

        [Test]
        public void ExponentialBackoffPolicyReturnsInfiniteWhenExhausted()
        {
            var policy = new ExponentialBackoffChannelReconnectPolicy
            {
                MaxAttempts = 3
            };

            Assert.That(policy.GetDelay(2), Is.Not.EqualTo(Timeout.InfiniteTimeSpan));
            Assert.That(policy.GetDelay(3), Is.EqualTo(Timeout.InfiniteTimeSpan));
            Assert.That(policy.GetDelay(100), Is.EqualTo(Timeout.InfiniteTimeSpan));
        }

        [Test]
        public async Task GetAsyncReturnsManagedChannelWithMatchingKeyAsync()
        {
            (ClientChannelManager sut, Certificate serverCert, _) = CreateMockedSut();
            try
            {
                ConfiguredEndpoint endpoint = GetTestEndpoint(serverCert);
                var participant = new TestParticipant("p1", endpoint);

                IManagedTransportChannel channel = await sut.GetAsync(participant, default)
                    .ConfigureAwait(false);

                Assert.That(channel, Is.Not.Null);
                Assert.That(channel.Key, Is.EqualTo(ManagedChannelKey.FromEndpoint(endpoint)));
                Assert.That(channel.State, Is.EqualTo(ChannelState.Ready));
                Assert.That(channel.Manager, Is.SameAs(sut));

                channel.Dispose();
            }
            finally
            {
                serverCert.Dispose();
            }
        }

        [Test]
        public async Task GetAsyncWithFactoryConstructsParticipantInsideLockAsync()
        {
            (ClientChannelManager sut, Certificate serverCert, _) = CreateMockedSut();
            try
            {
                ConfiguredEndpoint endpoint = GetTestEndpoint(serverCert);
                IManagedTransportChannel? observedLease = null;
                TestParticipant? participant = null;
                int factoryCalls = 0;

                IManagedTransportChannel channel = await sut.GetAsync(
                    endpoint,
                    lease =>
                    {
                        factoryCalls++;
                        observedLease = lease;
                        Assert.That(lease.Key, Is.EqualTo(ManagedChannelKey.FromEndpoint(endpoint)));
                        Assert.That(lease.Manager, Is.SameAs(sut));
                        var createdParticipant = new TestParticipant("p1", endpoint);
                        participant = createdParticipant;
                        return createdParticipant;
                    },
                    reverseConnection: null,
                    default)
                    .ConfigureAwait(false);

                Assert.That(factoryCalls, Is.EqualTo(1));
                Assert.That(channel, Is.SameAs(observedLease));
                Assert.That(participant, Is.Not.Null);
                Assert.That(channel.State, Is.EqualTo(ChannelState.Ready));

                channel.Dispose();
            }
            finally
            {
                serverCert.Dispose();
            }
        }

        [Test]
        public async Task GetAsyncWithFactoryExceptionPropagates()
        {
            (ClientChannelManager sut, Certificate serverCert, Mock<IChannel> chMock) = CreateMockedSut();
            try
            {
                ConfiguredEndpoint endpoint = GetTestEndpoint(serverCert);
                var expected = new InvalidOperationException("factory failed");
                Exception? actual = null;
                int factoryCalls = 0;

                try
                {
                    _ = await sut.GetAsync(
                        endpoint,
                        _ =>
                        {
                            factoryCalls++;
                            throw expected;
                        },
                        reverseConnection: null,
                        default)
                        .ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    actual = ex;
                }

                Assert.That(actual, Is.SameAs(expected));
                Assert.That(factoryCalls, Is.EqualTo(1));

                var participant = new TestParticipant("p1", endpoint);
                IManagedTransportChannel channel = await sut.GetAsync(participant, default)
                    .ConfigureAwait(false);

                chMock.Verify(c => c.OpenAsync(
                        It.IsAny<Uri>(),
                        It.IsAny<TransportChannelSettings>(),
                        It.IsAny<CancellationToken>()),
                    Times.Exactly(2));

                channel.Dispose();
            }
            finally
            {
                serverCert.Dispose();
            }
        }

        [Test]
        public async Task TwoParticipantsSameEndpointShareUnderlyingChannelAsync()
        {
            (ClientChannelManager sut, Certificate serverCert, Mock<IChannel> chMock) = CreateMockedSut();
            try
            {
                ConfiguredEndpoint endpoint = GetTestEndpoint(serverCert);
                var p1 = new TestParticipant("p1", endpoint);
                var p2 = new TestParticipant("p2", endpoint);

                IManagedTransportChannel ch1 = await sut.GetAsync(p1, default).ConfigureAwait(false);
                IManagedTransportChannel ch2 = await sut.GetAsync(p2, default).ConfigureAwait(false);

                Assert.That(ch1.Key, Is.EqualTo(ch2.Key));
                Assert.That(ch1, Is.Not.SameAs(ch2)); // distinct lease wrappers
                // underlying transport opened exactly once
                chMock.Verify(c => c.OpenAsync(
                        It.IsAny<Uri>(),
                        It.IsAny<TransportChannelSettings>(),
                        It.IsAny<CancellationToken>()),
                    Times.Once);

                ch1.Dispose();
                ch2.Dispose();
            }
            finally
            {
                serverCert.Dispose();
            }
        }

        [Test]
        public async Task ReleasingOneLeaseKeepsChannelAliveForOtherAsync()
        {
            (ClientChannelManager sut, Certificate serverCert, Mock<IChannel> chMock) = CreateMockedSut();
            try
            {
                ConfiguredEndpoint endpoint = GetTestEndpoint(serverCert);
                var p1 = new TestParticipant("p1", endpoint);
                var p2 = new TestParticipant("p2", endpoint);

                IManagedTransportChannel ch1 = await sut.GetAsync(p1, default).ConfigureAwait(false);
                IManagedTransportChannel ch2 = await sut.GetAsync(p2, default).ConfigureAwait(false);

                ch1.Dispose();
                // underlying channel should still be alive
                Assert.That(ch2.State, Is.EqualTo(ChannelState.Ready));
                chMock.Verify(c => c.CloseAsync(It.IsAny<CancellationToken>()), Times.Never);

                ch2.Dispose();
                // give the close fiber a moment to run
                await Task.Delay(100).ConfigureAwait(false);
            }
            finally
            {
                serverCert.Dispose();
            }
        }

        [Test]
        public async Task DiscoveryClientCreateAsyncSharesSessionChannelAndReleasesLeaseAsync()
        {
            (ClientChannelManager sut, Certificate serverCert, Mock<IChannel> chMock) = CreateMockedSut();
            try
            {
                var endpointConfiguration = new EndpointConfiguration
                {
                    OperationTimeout = 6000
                };
                ConfiguredEndpoint endpoint = GetNoneSecurityEndpoint(endpointConfiguration);
                var sessionParticipant = new TestParticipant("session", endpoint);
                IManagedTransportChannel sessionChannel = await sut.GetAsync(sessionParticipant, default)
                    .ConfigureAwait(false);
                DiscoveryClient? discoveryClient = null;

                try
                {
                    discoveryClient = await DiscoveryClient.CreateAsync(
                        sut,
                        new Uri(endpoint.Description.EndpointUrl!),
                        endpointConfiguration,
                        NUnitTelemetryContext.Create(),
                        ct: default).ConfigureAwait(false);

                    Assert.That(discoveryClient.TransportChannel, Is.InstanceOf<IManagedTransportChannel>());
                    var discoveryChannel = (IManagedTransportChannel)discoveryClient.TransportChannel;
                    Assert.That(discoveryChannel.Key, Is.EqualTo(sessionChannel.Key));
                    Assert.That(discoveryChannel, Is.Not.SameAs(sessionChannel));
                    chMock.Verify(c => c.OpenAsync(
                            It.IsAny<Uri>(),
                            It.IsAny<TransportChannelSettings>(),
                            It.IsAny<CancellationToken>()),
                        Times.Once);

                    discoveryClient.Dispose();
                    discoveryClient = null;

                    chMock.Verify(c => c.CloseAsync(It.IsAny<CancellationToken>()), Times.Never);
                    Assert.That(sessionChannel.State, Is.EqualTo(ChannelState.Ready));
                }
                finally
                {
                    discoveryClient?.Dispose();
                    sessionChannel.Dispose();
                }

                // lease.Dispose() is non-blocking; poll for the
                // CloseAsync invocation before the strict verify.
                await WaitForMockInvocationAsync(
                    () => chMock.Verify(c => c.CloseAsync(It.IsAny<CancellationToken>()), Times.Once))
                    .ConfigureAwait(false);
            }
            finally
            {
                serverCert.Dispose();
            }
        }

        [Test]
        public async Task RegistrationClientCreateAsyncUsesManagedLeaseAndReleasesItAsync()
        {
            (ClientChannelManager sut, Certificate serverCert, Mock<IChannel> chMock) = CreateMockedSut();
            try
            {
                ConfiguredEndpoint endpoint = GetNoneSecurityEndpoint(new EndpointConfiguration
                {
                    OperationTimeout = 6000
                });
                RegistrationClient registrationClient = await RegistrationClient.CreateAsync(
                    sut,
                    endpoint.Description,
                    endpoint.Configuration,
                    NUnitTelemetryContext.Create(),
                    ct: default).ConfigureAwait(false);

                Assert.That(registrationClient.TransportChannel, Is.InstanceOf<IManagedTransportChannel>());
                chMock.Verify(c => c.OpenAsync(
                        It.IsAny<Uri>(),
                        It.IsAny<TransportChannelSettings>(),
                        It.IsAny<CancellationToken>()),
                    Times.Once);

                registrationClient.Dispose();

                chMock.Verify(c => c.CloseAsync(It.IsAny<CancellationToken>()), Times.Once);
            }
            finally
            {
                serverCert.Dispose();
            }
        }

        [Test]
        public async Task ReconnectAsyncNotifiesAttachedParticipantsAsync()
        {
            (ClientChannelManager sut, Certificate serverCert, Mock<IChannel> chMock) = CreateMockedSut();
            try
            {
                ConfiguredEndpoint endpoint = GetTestEndpoint(serverCert);
                var participant = new TestParticipant("p1", endpoint);
                IManagedTransportChannel ch = await sut.GetAsync(participant, default).ConfigureAwait(false);

                chMock.Setup(c => c.SupportedFeatures).Returns(TransportChannelFeatures.Reconnect);
                chMock.Setup(c => c.ReconnectAsync(
                        It.IsAny<ITransportWaitingConnection?>(),
                        It.IsAny<CancellationToken>()))
                    .Returns(new ValueTask());

                await sut.ReconnectAsync(ch, default).ConfigureAwait(false);

                Assert.That(participant.NotificationCount, Is.GreaterThanOrEqualTo(1));
                Assert.That(ch.State, Is.EqualTo(ChannelState.Ready));
                ch.Dispose();
            }
            finally
            {
                serverCert.Dispose();
            }
        }

        [Test]
        public async Task ConcurrentReconnectCallsCoalesceIntoOneCycleAsync()
        {
            (ClientChannelManager sut, Certificate serverCert, Mock<IChannel> chMock) = CreateMockedSut();
            try
            {
                ConfiguredEndpoint endpoint = GetTestEndpoint(serverCert);
                var participant = new TestParticipant("p1", endpoint);
                IManagedTransportChannel ch = await sut.GetAsync(participant, default).ConfigureAwait(false);

                chMock.Setup(c => c.SupportedFeatures).Returns(TransportChannelFeatures.Reconnect);
                int reconnectCalls = 0;
                chMock.Setup(c => c.ReconnectAsync(
                        It.IsAny<ITransportWaitingConnection?>(),
                        It.IsAny<CancellationToken>()))
                    .Returns(async () =>
                    {
                        Interlocked.Increment(ref reconnectCalls);
                        await Task.Delay(100).ConfigureAwait(false);
                    });

                Task t1 = sut.ReconnectAsync(ch, default).AsTask();
                Task t2 = sut.ReconnectAsync(ch, default).AsTask();
                Task t3 = sut.ReconnectAsync(ch, default).AsTask();

                await Task.WhenAll(t1, t2, t3).ConfigureAwait(false);

                Assert.That(reconnectCalls, Is.EqualTo(1),
                    "Concurrent ReconnectAsync calls should coalesce into a single cycle.");
                Assert.That(participant.NotificationCount, Is.EqualTo(1));
                ch.Dispose();
            }
            finally
            {
                serverCert.Dispose();
            }
        }

        [Test]
        public async Task FatalForChannelTransitionsToFaultedStateAsync()
        {
            (ClientChannelManager sut, Certificate serverCert, Mock<IChannel> chMock) = CreateMockedSut();
            try
            {
                ConfiguredEndpoint endpoint = GetTestEndpoint(serverCert);
                var participant = new TestParticipant(
                    "p1", endpoint,
                    onReconnect: (_, _, _) => ParticipantReconnectResult.FatalForChannel);
                IManagedTransportChannel ch = await sut.GetAsync(participant, default).ConfigureAwait(false);

                chMock.Setup(c => c.SupportedFeatures).Returns(TransportChannelFeatures.Reconnect);
                chMock.Setup(c => c.ReconnectAsync(
                        It.IsAny<ITransportWaitingConnection?>(),
                        It.IsAny<CancellationToken>()))
                    .Returns(new ValueTask());

                await sut.ReconnectAsync(ch, default).ConfigureAwait(false);

                Assert.That(ch.State, Is.EqualTo(ChannelState.Faulted));
                ch.Dispose();
            }
            finally
            {
                serverCert.Dispose();
            }
        }

        [Test]
        public async Task FatalForParticipantDetachesOnlyThatParticipantAsync()
        {
            (ClientChannelManager sut, Certificate serverCert, Mock<IChannel> chMock) = CreateMockedSut();
            try
            {
                ConfiguredEndpoint endpoint = GetTestEndpoint(serverCert);
                var p1 = new TestParticipant(
                    "p1", endpoint,
                    onReconnect: (_, _, _) => ParticipantReconnectResult.FatalForParticipant);
                var p2 = new TestParticipant("p2", endpoint);

                IManagedTransportChannel ch1 = await sut.GetAsync(p1, default).ConfigureAwait(false);
                IManagedTransportChannel ch2 = await sut.GetAsync(p2, default).ConfigureAwait(false);

                chMock.Setup(c => c.SupportedFeatures).Returns(TransportChannelFeatures.Reconnect);
                chMock.Setup(c => c.ReconnectAsync(
                        It.IsAny<ITransportWaitingConnection?>(),
                        It.IsAny<CancellationToken>()))
                    .Returns(new ValueTask());

                await sut.ReconnectAsync(ch1, default).ConfigureAwait(false);

                Assert.That(ch1.State, Is.EqualTo(ChannelState.Ready));
                Assert.That(ch2.State, Is.EqualTo(ChannelState.Ready));
                // p2 still got the notification
                Assert.That(p2.NotificationCount, Is.GreaterThanOrEqualTo(1));

                ch1.Dispose();
                ch2.Dispose();
            }
            finally
            {
                serverCert.Dispose();
            }
        }

        [Test]
        public async Task ReconnectAllAsyncTriggersAllEntriesAsync()
        {
            (ClientChannelManager sut, Certificate serverCert, Mock<IChannel> chMock) = CreateMockedSut();
            try
            {
                ConfiguredEndpoint endpoint = GetTestEndpoint(serverCert);
                var p1 = new TestParticipant("p1", endpoint);
                IManagedTransportChannel ch = await sut.GetAsync(p1, default).ConfigureAwait(false);

                chMock.Setup(c => c.SupportedFeatures).Returns(TransportChannelFeatures.Reconnect);
                chMock.Setup(c => c.ReconnectAsync(
                        It.IsAny<ITransportWaitingConnection?>(),
                        It.IsAny<CancellationToken>()))
                    .Returns(new ValueTask());

                await sut.ReconnectAllAsync(default).ConfigureAwait(false);

                Assert.That(p1.NotificationCount, Is.GreaterThanOrEqualTo(1));
                Assert.That(ch.State, Is.EqualTo(ChannelState.Ready));
                ch.Dispose();
            }
            finally
            {
                serverCert.Dispose();
            }
        }

        [Test]
        public async Task RebindParticipantSwapsParticipantOnLeaseAsync()
        {
            (ClientChannelManager sut, Certificate serverCert, _) = CreateMockedSut();
            try
            {
                ConfiguredEndpoint endpoint = GetTestEndpoint(serverCert);
                var bootstrap = new TestParticipant("boot", endpoint);
                IManagedTransportChannel ch = await sut.GetAsync(bootstrap, default)
                    .ConfigureAwait(false);

                var real = new TestParticipant("real", endpoint);
#pragma warning disable CS0618 // Test verifies the obsolete compatibility shim remains functional.
                sut.RebindParticipant(ch, real);
#pragma warning restore CS0618
                // No exception means swap succeeded; participant list is internal,
                // but downstream OnReconnect tests verify behavior.

                ch.Dispose();
            }
            finally
            {
                serverCert.Dispose();
            }
        }

        [Test]
        public async Task FailoverWithDifferentEndpointSwapsLeaseAsync()
        {
            var harness = new SessionChannelHarness();
            ConfiguredEndpoint endpointA = GetSessionEndpoint("opc.tcp://localhost:4840");
            ConfiguredEndpoint endpointB = GetSessionEndpoint("opc.tcp://localhost:4841");
            Session? session = null;
            try
            {
                session = await harness.CreateSessionAsync(endpointA).ConfigureAwait(false);
                IManagedTransportChannel oldLease = session.ManagedChannel!;
                ScriptedChannel oldTransport = harness.CreatedChannels[0];

                await session.RecreateInPlaceAsync(endpointB, ct: default).ConfigureAwait(false);

                IManagedTransportChannel newLease = session.ManagedChannel!;
                Assert.That(newLease, Is.Not.SameAs(oldLease));
                Assert.That(newLease.Key, Is.EqualTo(ManagedChannelKey.FromEndpoint(endpointB)));
                Assert.That(newLease.State, Is.EqualTo(ChannelState.Ready));
                // RecreateInPlaceAsync swaps the lease and fires the
                // old-lease teardown asynchronously (the underlying
                // CloseAsync runs on the thread pool). Poll for both
                // the lease-state transition AND the underlying close
                // before the hard assertions.
                await WaitForConditionAsync(
                    () => oldLease.State == ChannelState.Closed &&
                          oldTransport.CloseCount == 1,
                    "oldLease.State == Closed && oldTransport.CloseCount == 1").ConfigureAwait(false);
                Assert.That(oldLease.State, Is.EqualTo(ChannelState.Closed));
                Assert.That(oldTransport.CloseCount, Is.EqualTo(1));
                Assert.That(harness.CreatedChannels, Has.Count.EqualTo(2));
                Assert.That(
                    harness.CreatedChannels[1].OpenedEndpointUrl,
                    Is.EqualTo(endpointB.Description.EndpointUrl));
            }
            finally
            {
                session?.Dispose();
                await harness.DisposeAsync().ConfigureAwait(false);
            }
        }

        [Test]
        public async Task FailoverWithSameEndpointKeepsLeaseAsync()
        {
            var harness = new SessionChannelHarness();
            ConfiguredEndpoint endpoint = GetSessionEndpoint("opc.tcp://localhost:4840");
            Session? session = null;
            try
            {
                session = await harness.CreateSessionAsync(endpoint).ConfigureAwait(false);
                IManagedTransportChannel oldLease = session.ManagedChannel!;
                ScriptedChannel transport = harness.CreatedChannels[0];
                transport.SupportedFeatures = TransportChannelFeatures.Reconnect;

                await session.RecreateInPlaceAsync(ct: default).ConfigureAwait(false);

                Assert.That(session.ManagedChannel, Is.SameAs(oldLease));
                Assert.That(transport.ReconnectCount, Is.EqualTo(1));
                Assert.That(transport.CloseCount, Is.Zero);
                Assert.That(harness.CreatedChannels, Has.Count.EqualTo(1));
            }
            finally
            {
                session?.Dispose();
                await harness.DisposeAsync().ConfigureAwait(false);
            }
        }

        [Test]
        public async Task FailoverWithExplicitChannelTakesLegacyPathAsync()
        {
            var harness = new SessionChannelHarness();
            ConfiguredEndpoint endpointA = GetSessionEndpoint("opc.tcp://localhost:4840");
            ConfiguredEndpoint endpointB = GetSessionEndpoint("opc.tcp://localhost:4841");
            Session? session = null;
            try
            {
                session = await harness.CreateSessionAsync(endpointA).ConfigureAwait(false);
                int managerCreatedChannels = harness.CreatedChannels.Count;
                var explicitChannel = harness.CreateOpenedStandaloneChannel(endpointB);

                await session
                    .RecreateInPlaceAsync(endpointB, channel: explicitChannel.Channel, ct: default)
                    .ConfigureAwait(false);

                Assert.That(harness.CreatedChannels, Has.Count.EqualTo(managerCreatedChannels));
                Assert.That(session.TransportChannel, Is.SameAs(explicitChannel.Channel));
                Assert.That(explicitChannel.SendRequestCount, Is.GreaterThan(0));
                Assert.That(explicitChannel.CloseCount, Is.Zero);
            }
            finally
            {
                session?.Dispose();
                await harness.DisposeAsync().ConfigureAwait(false);
            }
        }

        [Test]
        [NonParallelizable]
        public async Task MetricsAreEmittedForChannelLifetimeAsync()
        {
            using var metrics = new ChannelMetricListener();
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            (ClientChannelManager sut, Certificate serverCert, _) = CreateMockedSut(telemetry);
            try
            {
                ConfiguredEndpoint endpoint = GetTestEndpoint(serverCert);
                string endpointUrl = endpoint.Description.EndpointUrl!;
                var p1 = new TestParticipant("p1", endpoint);
                var p2 = new TestParticipant("p2", endpoint);

                IManagedTransportChannel ch1 = await sut.GetAsync(p1, default).ConfigureAwait(false);
                IManagedTransportChannel ch2 = await sut.GetAsync(p2, default).ConfigureAwait(false);
                metrics.RecordObservableInstruments();

                ch1.Dispose();
                ch2.Dispose();

                // ch.Dispose() is non-blocking (lease teardown runs on
                // the threadpool); poll for the close metric before the
                // hard assertion so the test does not race with the
                // asynchronous teardown.
                await WaitForMeasurementAsync(
                    metrics,
                    "opcua.channel.close",
                    Tag("endpoint", endpointUrl),
                    Tag("reverse", false),
                    Tag("reason", "lease-released")).ConfigureAwait(false);

                Assert.That(metrics.HasMeasurement(
                    "opcua.channel.open",
                    Tag("endpoint", endpointUrl),
                    Tag("reverse", false)), Is.True, metrics.FormatMeasurements());
                Assert.That(metrics.HasMeasurement(
                    "opcua.channel.active",
                    1,
                    Tag("endpoint", endpointUrl)), Is.True, metrics.FormatMeasurements());
                Assert.That(metrics.HasMeasurement(
                    "opcua.channel.active",
                    -1,
                    Tag("endpoint", endpointUrl)), Is.True, metrics.FormatMeasurements());
                Assert.That(metrics.HasMeasurement(
                    "opcua.channel.refcount",
                    2,
                    Tag("endpoint", endpointUrl)), Is.True, metrics.FormatMeasurements());
                Assert.That(metrics.HasMeasurement(
                    "opcua.channel.participants",
                    2,
                    Tag("endpoint", endpointUrl)), Is.True, metrics.FormatMeasurements());
                Assert.That(metrics.HasMeasurement(
                    "opcua.channel.close",
                    Tag("endpoint", endpointUrl),
                    Tag("reverse", false),
                    Tag("reason", "lease-released")), Is.True, metrics.FormatMeasurements());
            }
            finally
            {
                await sut.DisposeAsync().ConfigureAwait(false);
                serverCert.Dispose();
            }
        }

        [Test]
        [NonParallelizable]
        public async Task MetricsAreEmittedForReconnectAndGateWaitAsync()
        {
            using var metrics = new ChannelMetricListener();
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            (ClientChannelManager sut, Certificate serverCert, Mock<IChannel> chMock) = CreateMockedSut(telemetry);
            try
            {
                ConfiguredEndpoint endpoint = GetTestEndpoint(serverCert);
                string endpointUrl = endpoint.Description.EndpointUrl!;
                var participant = new TestParticipant("p1", endpoint);
                IManagedTransportChannel ch = await sut.GetAsync(participant, default).ConfigureAwait(false);

                var reconnectEntered = new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                var allowReconnect = new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                chMock.Setup(c => c.SupportedFeatures).Returns(TransportChannelFeatures.Reconnect);
                chMock.Setup(c => c.ReconnectAsync(
                        It.IsAny<ITransportWaitingConnection?>(),
                        It.IsAny<CancellationToken>()))
                    .Returns(() => new ValueTask(WaitForReconnectReleaseAsync(
                        reconnectEntered,
                        allowReconnect)));
                chMock.Setup(c => c.SendRequestAsync(
                        It.IsAny<IServiceRequest>(),
                        It.IsAny<CancellationToken>()))
                    .Returns(new ValueTask<IServiceResponse>(new ReadResponse
                    {
                        ResponseHeader = new ResponseHeader { ServiceResult = StatusCodes.Good }
                    }));

                Task reconnectTask = sut.ReconnectAsync(ch, default).AsTask();
                await reconnectEntered.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                Task<IServiceResponse> sendTask = ch.SendRequestAsync(
                    new ReadRequest { RequestHeader = new RequestHeader() },
                    default).AsTask();
                allowReconnect.SetResult(true);

                await Task.WhenAll(reconnectTask, sendTask).ConfigureAwait(false);

                // RunReconnectCycleAsync.RecordReconnectDuration is emitted from a
                // finally block AFTER tcs.TrySetResult(true) returns, so on a fast
                // runner the test thread can race past the assertion before the
                // measurement lands. Poll briefly for the histogram measurement.
                await WaitForMeasurementAsync(
                    metrics,
                    "opcua.channel.reconnect.duration",
                    Tag("endpoint", endpointUrl),
                    Tag("outcome", "success"))
                    .ConfigureAwait(false);

                Assert.That(metrics.HasMeasurement(
                    "opcua.channel.reconnect.attempts",
                    Tag("endpoint", endpointUrl),
                    Tag("outcome", "success")), Is.True, metrics.FormatMeasurements());
                Assert.That(metrics.HasMeasurement(
                    "opcua.channel.reconnect.duration",
                    Tag("endpoint", endpointUrl),
                    Tag("outcome", "success")), Is.True, metrics.FormatMeasurements());
                Assert.That(metrics.HasMeasurement(
                    "opcua.channel.gate.wait",
                    Tag("endpoint", endpointUrl)), Is.True, metrics.FormatMeasurements());
                ch.Dispose();
            }
            finally
            {
                await sut.DisposeAsync().ConfigureAwait(false);
                serverCert.Dispose();
            }
        }

        private static async Task WaitForMeasurementAsync(
            ChannelMetricListener metrics,
            string instrumentName,
            params KeyValuePair<string, object?>[] tags)
        {
            const int kMaxPollMs = 2000;
            const int kPollIntervalMs = 25;
            int elapsed = 0;
            while (!metrics.HasMeasurement(instrumentName, tags) && elapsed < kMaxPollMs)
            {
                await Task.Delay(kPollIntervalMs).ConfigureAwait(false);
                elapsed += kPollIntervalMs;
            }
        }

        private static async Task WaitForMockInvocationAsync(Action verify)
        {
            // Used to bridge Moq.Verify against state mutated by fire-
            // and-forget tasks (e.g. ManagedTransportChannelLease.Dispose
            // which posts the actual underlying CloseAsync onto the
            // thread pool). Polls the verify until it stops throwing or
            // the budget is exhausted; the final invocation is allowed
            // to throw and surface the failure to NUnit.
            const int kMaxPollMs = 2000;
            const int kPollIntervalMs = 25;
            int elapsed = 0;
            while (elapsed < kMaxPollMs)
            {
                try
                {
                    verify();
                    return;
                }
                catch (Moq.MockException)
                {
                    await Task.Delay(kPollIntervalMs).ConfigureAwait(false);
                    elapsed += kPollIntervalMs;
                }
            }
            verify();
        }

        private static async Task WaitForConditionAsync(
            Func<bool> condition,
            string description)
        {
            // Generic test-side poll for state mutated by fire-and-forget
            // tasks. Returns once the condition holds; if the budget is
            // exhausted the caller's subsequent assertion is allowed to
            // run and surface the failure to NUnit.
            const int kMaxPollMs = 2000;
            const int kPollIntervalMs = 25;
            int elapsed = 0;
            while (!condition() && elapsed < kMaxPollMs)
            {
                await Task.Delay(kPollIntervalMs).ConfigureAwait(false);
                elapsed += kPollIntervalMs;
            }
            _ = description;
        }

        [Test]
        [NonParallelizable]
        public async Task ActivitySpanIsRecordedForReconnectAsync()
        {
            using var listener = new ChannelActivityListener();
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            (ClientChannelManager sut, Certificate serverCert, Mock<IChannel> chMock) = CreateMockedSut(telemetry);
            try
            {
                ConfiguredEndpoint endpoint = GetTestEndpoint(serverCert);
                string endpointUrl = endpoint.Description.EndpointUrl!;
                var participant = new TestParticipant("p1", endpoint);
                IManagedTransportChannel ch = await sut.GetAsync(participant, default).ConfigureAwait(false);

                chMock.Setup(c => c.SupportedFeatures).Returns(TransportChannelFeatures.Reconnect);
                chMock.Setup(c => c.ReconnectAsync(
                        It.IsAny<ITransportWaitingConnection?>(),
                        It.IsAny<CancellationToken>()))
                    .Returns(new ValueTask());

                await sut.ReconnectAsync(ch, default).ConfigureAwait(false);

                Activity activity = await listener
                    .WaitForStoppedActivityAsync("OpcUaChannelReconnect")
                    .ConfigureAwait(false);
                Dictionary<string, object?> tags = activity.TagObjects.ToDictionary(t => t.Key, t => t.Value);
                Assert.That(tags, Does.ContainKey("endpoint"));
                Assert.That(tags["endpoint"], Is.EqualTo(endpointUrl));
                Assert.That(tags, Does.ContainKey("attempt.count"));
                Assert.That(tags["attempt.count"], Is.EqualTo(1));
                Assert.That(tags, Does.ContainKey("outcome"));
                Assert.That(tags["outcome"], Is.EqualTo("success"));

                ch.Dispose();
            }
            finally
            {
                await sut.DisposeAsync().ConfigureAwait(false);
                serverCert.Dispose();
            }
        }

        [Test]
        [NonParallelizable]
        public async Task EventSourceFiresStateTransitionsAsync()
        {
            using var listener = new ChannelEventListener();
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            (ClientChannelManager sut, Certificate serverCert, Mock<IChannel> chMock) = CreateMockedSut(telemetry);
            try
            {
                ConfiguredEndpoint endpoint = GetTestEndpoint(serverCert);
                var participant = new TestParticipant("p1", endpoint);
                IManagedTransportChannel ch = await sut.GetAsync(participant, default).ConfigureAwait(false);

                chMock.Setup(c => c.SupportedFeatures).Returns(TransportChannelFeatures.Reconnect);
                chMock.Setup(c => c.ReconnectAsync(
                        It.IsAny<ITransportWaitingConnection?>(),
                        It.IsAny<CancellationToken>()))
                    .Returns(new ValueTask());

                await sut.ReconnectAsync(ch, default).ConfigureAwait(false);
                ch.Dispose();

                // ch.Dispose() is non-blocking; the ParticipantDetached
                // and ChannelClosed EventSource events fire from the
                // background teardown task, so poll for them before the
                // hard assertions.
                await WaitForConditionAsync(
                    () => listener.EventNames.Contains("ParticipantDetached") &&
                          listener.EventNames.Contains("ChannelClosed"),
                    "ParticipantDetached + ChannelClosed events").ConfigureAwait(false);

                Assert.That(listener.EventNames, Does.Contain("StateChanged"), listener.FormatEvents());
                Assert.That(listener.EventNames, Does.Contain("ReconnectStarted"), listener.FormatEvents());
                Assert.That(listener.EventNames, Does.Contain("ReconnectCompleted"), listener.FormatEvents());
                Assert.That(listener.EventNames, Does.Contain("ParticipantAttached"), listener.FormatEvents());
                Assert.That(listener.EventNames, Does.Contain("ParticipantDetached"), listener.FormatEvents());
                Assert.That(listener.EventNames, Does.Contain("ChannelOpened"), listener.FormatEvents());
                Assert.That(listener.EventNames, Does.Contain("ChannelClosed"), listener.FormatEvents());
            }
            finally
            {
                await sut.DisposeAsync().ConfigureAwait(false);
                serverCert.Dispose();
            }
        }

        [Test]
        public async Task GetChannelDiagnosticsReturnsSnapshot()
        {
            (ClientChannelManager sut, Certificate serverCert, _) = CreateMockedSut();
            try
            {
                ConfiguredEndpoint endpoint1 = GetTestEndpoint(serverCert);
                ConfiguredEndpoint endpoint2 = GetTestEndpoint(serverCert);
                endpoint2.Description.EndpointUrl = "opc.tcp://localhost:4841";
                var p1 = new TestParticipant("p1", endpoint1);
                var p2 = new TestParticipant("p2", endpoint2);

                IManagedTransportChannel ch1 = await sut.GetAsync(p1, default).ConfigureAwait(false);
                IManagedTransportChannel ch2 = await sut.GetAsync(p2, default).ConfigureAwait(false);

                IReadOnlyList<ManagedChannelDiagnostic> snapshot = sut.GetChannelDiagnostics();

                Assert.That(snapshot, Has.Count.EqualTo(2));
                ManagedChannelDiagnostic d1 = snapshot.Single(d => d.Key == ch1.Key);
                ManagedChannelDiagnostic d2 = snapshot.Single(d => d.Key == ch2.Key);
                AssertChannelDiagnostic(d1, ch1.Key);
                AssertChannelDiagnostic(d2, ch2.Key);

                ch1.Dispose();
                ch2.Dispose();
            }
            finally
            {
                await sut.DisposeAsync().ConfigureAwait(false);
                serverCert.Dispose();
            }
        }

        [Test]
        public async Task ReconnectAsyncWithBudgetStopsWhenExhaustedAsync()
        {
            var timeProvider = new FakeTimeProvider();
            var reconnectPolicy = new ExponentialBackoffChannelReconnectPolicy
            {
                MinDelay = TimeSpan.Zero,
                MaxDelay = TimeSpan.Zero
            };
            (ClientChannelManager sut, Certificate serverCert, Mock<IChannel> chMock) =
                CreateMockedSut(reconnectPolicy: reconnectPolicy, timeProvider: timeProvider);
            try
            {
                ConfiguredEndpoint endpoint = GetTestEndpoint(serverCert);
                var participant = new TestParticipant("p1", endpoint);
                IManagedTransportChannel ch = await sut.GetAsync(participant, default).ConfigureAwait(false);
                var budget = new RetryBudget(TimeSpan.Zero, timeProvider);

                ServiceResultException? ex = Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await sut.ReconnectAsync(ch, budget, default).AsTask().ConfigureAwait(false));

                Assert.That(ex, Is.Not.Null);
                Assert.That(ex!.StatusCode, Is.EqualTo(StatusCodes.BadSecureChannelClosed));
                Assert.That(ch.State, Is.EqualTo(ChannelState.Faulted));
                chMock.Verify(c => c.ReconnectAsync(
                        It.IsAny<ITransportWaitingConnection?>(),
                        It.IsAny<CancellationToken>()),
                    Times.Never);
                ch.Dispose();
            }
            finally
            {
                await sut.DisposeAsync().ConfigureAwait(false);
                serverCert.Dispose();
            }
        }

        [Test]
        public async Task ReconnectAsyncSwapsFaultedLeaseEntryAsync()
        {
            var timeProvider = new FakeTimeProvider();
            var reconnectPolicy = new ExponentialBackoffChannelReconnectPolicy
            {
                MinDelay = TimeSpan.FromMilliseconds(100),
                MaxDelay = TimeSpan.FromMilliseconds(100),
                MaxAttempts = 3
            };
            (ClientChannelManager sut, Certificate serverCert, _) =
                CreateMockedSut(reconnectPolicy: reconnectPolicy, timeProvider: timeProvider);
            try
            {
                ConfiguredEndpoint endpoint = GetTestEndpoint(serverCert);
                var participant = new TestParticipant("p1", endpoint);
                IManagedTransportChannel ch = await sut.GetAsync(participant, default).ConfigureAwait(false);
                object originalEntry = GetLeaseEntry(ch);
                var exhaustedBudget = new RetryBudget(TimeSpan.Zero, timeProvider);

                _ = Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await sut.ReconnectAsync(ch, exhaustedBudget, default).AsTask().ConfigureAwait(false));

                Assert.That(ch.State, Is.EqualTo(ChannelState.Faulted));

                Task reconnectTask = sut.ReconnectAsync(ch, default).AsTask();
                await Task.Delay(10).ConfigureAwait(false);

                Assert.That(reconnectTask.IsCompleted, Is.False, "Swap back-off should delay the reset.");

                for (int i = 0; i < 4 && !reconnectTask.IsCompleted; i++)
                {
                    timeProvider.Advance(TimeSpan.FromMilliseconds(100));
                    await Task.Delay(10).ConfigureAwait(false);
                }

                await reconnectTask.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

                object freshEntry = GetLeaseEntry(ch);
                ManagedChannelDiagnostic diagnostic = sut.GetChannelDiagnostics()
                    .Single(d => d.Key.Equals(ch.Key));
                Assert.That(freshEntry, Is.Not.SameAs(originalEntry));
                Assert.That(GetEntryState(freshEntry), Is.EqualTo(ChannelState.Ready));
                Assert.That(GetInternalIntProperty(freshEntry, "RefCount"), Is.EqualTo(1));
                Assert.That(GetInternalIntProperty(freshEntry, "ParticipantCount"), Is.EqualTo(1));
                Assert.That(GetInternalIntProperty(originalEntry, "RefCount"), Is.Zero);
                Assert.That(GetInternalIntProperty(ch, "SwapCount"), Is.EqualTo(1));
                Assert.That(diagnostic.Refcount, Is.EqualTo(1));
                Assert.That(diagnostic.ParticipantCount, Is.EqualTo(1));
                Assert.That(participant.NotificationCount, Is.GreaterThanOrEqualTo(2));

                ch.Dispose();
            }
            finally
            {
                await sut.DisposeAsync().ConfigureAwait(false);
                serverCert.Dispose();
            }
        }

        [Test]
        public async Task ReconnectAsyncWithBudgetShrinksDelayToFitRemainingAsync()
        {
            var timeProvider = new FakeTimeProvider();
            var reconnectPolicy = new ExponentialBackoffChannelReconnectPolicy
            {
                MinDelay = TimeSpan.FromSeconds(10),
                MaxDelay = TimeSpan.FromSeconds(10)
            };
            (ClientChannelManager sut, Certificate serverCert, Mock<IChannel> chMock) =
                CreateMockedSut(reconnectPolicy: reconnectPolicy, timeProvider: timeProvider);
            try
            {
                ConfiguredEndpoint endpoint = GetTestEndpoint(serverCert);
                var participant = new TestParticipant("p1", endpoint);
                IManagedTransportChannel ch = await sut.GetAsync(participant, default).ConfigureAwait(false);
                var budget = new RetryBudget(TimeSpan.FromMilliseconds(100), timeProvider);
                var reconnecting = new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                ch.StateChanged += (_, change) =>
                {
                    if (change.NewState == ChannelState.TransportReconnecting)
                    {
                        reconnecting.TrySetResult(true);
                    }
                };

                Task reconnectTask = sut.ReconnectAsync(ch, budget, default).AsTask();
                await reconnecting.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

                Assert.That(reconnectTask.IsCompleted, Is.False);

                timeProvider.Advance(TimeSpan.FromMilliseconds(100));

                ServiceResultException? ex = Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await reconnectTask.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false));

                Assert.That(ex, Is.Not.Null);
                Assert.That(ex!.StatusCode, Is.EqualTo(StatusCodes.BadSecureChannelClosed));
                Assert.That(ch.State, Is.EqualTo(ChannelState.Faulted));
                chMock.Verify(c => c.ReconnectAsync(
                        It.IsAny<ITransportWaitingConnection?>(),
                        It.IsAny<CancellationToken>()),
                    Times.Never);
                ch.Dispose();
            }
            finally
            {
                await sut.DisposeAsync().ConfigureAwait(false);
                serverCert.Dispose();
            }
        }

        // ---- helpers ----

        private static (ClientChannelManager sut, Certificate serverCert, Mock<IChannel> chMock) CreateMockedSut(
            ITelemetryContext? telemetry = null,
            IChannelReconnectPolicy? reconnectPolicy = null,
            TimeProvider? timeProvider = null)
        {
            telemetry ??= NUnitTelemetryContext.Create();
            Certificate serverCert = s_factory.CreateCertificate("CN=server").CreateForRSA();

            var chMock = new Mock<IChannel>();
            chMock.Setup(c => c.OpenAsync(
                    It.IsAny<Uri>(),
                    It.IsAny<TransportChannelSettings>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask());
            chMock.Setup(c => c.OpenAsync(
                    It.IsAny<ITransportWaitingConnection>(),
                    It.IsAny<TransportChannelSettings>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask());
            chMock.Setup(c => c.CloseAsync(It.IsAny<CancellationToken>()))
                .Returns(new ValueTask());
            chMock.Setup(c => c.SupportedFeatures).Returns(TransportChannelFeatures.None);

            var bindings = new Mock<ITransportChannelBindings>();
            bindings.Setup(b => b.Create(It.IsAny<string>(), It.IsAny<ITelemetryContext>()))
                .Returns(chMock.Object);

            var configuration = new ApplicationConfiguration(telemetry);
            var sut = new ClientChannelManager(
                configuration,
                telemetry,
                bindings.Object,
                reconnectPolicy,
                timeProvider);
            return (sut, serverCert, chMock);
        }

        private static ConfiguredEndpoint GetTestEndpoint(Certificate serverCert)
        {
            var endpoint = new ConfiguredEndpoint
            {
                Configuration = new EndpointConfiguration
                {
                    OperationTimeout = 6000
                }
            };
            endpoint.Description.EndpointUrl = "opc.tcp://localhost:4840";
            endpoint.Description.SecurityMode = MessageSecurityMode.SignAndEncrypt;
            endpoint.Description.SecurityPolicyUri = SecurityPolicies.Basic256Sha256;
            endpoint.Description.ServerCertificate = serverCert.RawData.ToByteString();
            return endpoint;
        }

        private static ConfiguredEndpoint GetNoneSecurityEndpoint(EndpointConfiguration endpointConfiguration)
        {
            var description = new EndpointDescription
            {
                EndpointUrl = "opc.tcp://localhost:4840",
                SecurityMode = MessageSecurityMode.None,
                SecurityPolicyUri = SecurityPolicies.None
            };
            description.Server.ApplicationUri = description.EndpointUrl;
            description.Server.ApplicationType = ApplicationType.DiscoveryServer;

            return new ConfiguredEndpoint(null, description, endpointConfiguration)
            {
                UpdateBeforeConnect = false
            };
        }

        private static ConfiguredEndpoint GetSessionEndpoint(string endpointUrl)
        {
            var endpointConfiguration = new EndpointConfiguration
            {
                OperationTimeout = 6000
            };
            var description = new EndpointDescription
            {
                EndpointUrl = endpointUrl,
                SecurityMode = MessageSecurityMode.None,
                SecurityPolicyUri = SecurityPolicies.None,
                TransportProfileUri = Profiles.UaTcpTransport,
                UserIdentityTokens =
                [
                    new UserTokenPolicy
                    {
                        PolicyId = "anonymous",
                        TokenType = UserTokenType.Anonymous,
                        SecurityPolicyUri = SecurityPolicies.None
                    }
                ]
            };
            description.Server.ApplicationUri = endpointUrl;
            description.Server.ApplicationType = ApplicationType.Server;

            return new ConfiguredEndpoint(null, description, endpointConfiguration)
            {
                UpdateBeforeConnect = false
            };
        }

        private static KeyValuePair<string, object?> Tag(string key, object? value)
        {
            return new KeyValuePair<string, object?>(key, value);
        }

        private static async Task WaitForReconnectReleaseAsync(
            TaskCompletionSource<bool> reconnectEntered,
            TaskCompletionSource<bool> allowReconnect)
        {
            reconnectEntered.TrySetResult(true);
            await allowReconnect.Task.ConfigureAwait(false);
        }

        private static object GetLeaseEntry(IManagedTransportChannel channel)
        {
            return GetInternalPropertyValue(channel, "Entry");
        }

        private static ChannelState GetEntryState(object entry)
        {
            return (ChannelState)GetInternalPropertyValue(entry, "State");
        }

        private static int GetInternalIntProperty(object target, string propertyName)
        {
            return (int)GetInternalPropertyValue(target, propertyName);
        }

        private static object GetInternalPropertyValue(object target, string propertyName)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            PropertyInfo? property = target.GetType().GetProperty(propertyName, flags);
            Assert.That(property, Is.Not.Null, $"Expected property {propertyName} on {target.GetType()}.");
            object? value = property!.GetValue(target);
            Assert.That(value, Is.Not.Null, $"Expected property {propertyName} to return a value.");
            return value!;
        }

        private static void AssertChannelDiagnostic(
            ManagedChannelDiagnostic diagnostic,
            ManagedChannelKey key)
        {
            Assert.That(diagnostic.Key, Is.EqualTo(key));
            Assert.That(diagnostic.State, Is.EqualTo(ChannelState.Ready));
            Assert.That(diagnostic.Refcount, Is.EqualTo(1));
            Assert.That(diagnostic.ParticipantCount, Is.EqualTo(1));
            Assert.That(diagnostic.OpenedAt, Is.Not.Default);
            Assert.That(diagnostic.LastStateChange, Is.Not.Default);
            Assert.That(diagnostic.LastReconnectAttempt, Is.Zero);
            Assert.That(diagnostic.LastError, Is.Null);
        }

        public interface IChannel : ITransportChannel, ISecureChannel;

        private sealed class SessionChannelHarness : IAsyncDisposable
        {
            public SessionChannelHarness()
            {
                ITelemetryContext telemetry = NUnitTelemetryContext.Create();
                Configuration = CreateConfiguration(telemetry);
                m_bindings = new Mock<ITransportChannelBindings>();
                m_bindings.Setup(b => b.Create(It.IsAny<string>(), It.IsAny<ITelemetryContext>()))
                    .Returns((string _, ITelemetryContext context) => CreateManagedChannel(context));
                Manager = new ClientChannelManager(
                    Configuration,
                    telemetry,
                    m_bindings.Object,
                    new ExponentialBackoffChannelReconnectPolicy
                    {
                        MinDelay = TimeSpan.Zero,
                        MaxDelay = TimeSpan.Zero,
                        MaxAttempts = 1
                    });
            }

            public ApplicationConfiguration Configuration { get; }

            public ClientChannelManager Manager { get; }

            public List<ScriptedChannel> CreatedChannels { get; } = [];

            public async ValueTask DisposeAsync()
            {
                await Manager.DisposeAsync().ConfigureAwait(false);
            }

            public Task<Session> CreateSessionAsync(ConfiguredEndpoint endpoint)
            {
                return Session.CreateAsync(
                    Manager,
                    Configuration,
                    endpoint,
                    updateBeforeConnect: false,
                    checkDomain: false,
                    sessionName: "ClientChannelManagerManagedTests",
                    sessionTimeout: 60000,
                    identity: new UserIdentity(),
                    ct: default);
            }

            public ScriptedChannel CreateOpenedStandaloneChannel(ConfiguredEndpoint endpoint)
            {
                var channel = new ScriptedChannel(Configuration.CreateMessageContext());
                channel.OpenForEndpoint(endpoint);
                return channel;
            }

            private static ApplicationConfiguration CreateConfiguration(ITelemetryContext telemetry)
            {
                return new ApplicationConfiguration(telemetry)
                {
                    ApplicationName = "ClientChannelManagerManagedTests",
                    ApplicationType = ApplicationType.Client,
                    ApplicationUri = "urn:localhost:ClientChannelManagerManagedTests",
                    ProductUri = "urn:localhost:ClientChannelManagerManagedTests",
                    ClientConfiguration = new ClientConfiguration
                    {
                        DefaultSessionTimeout = 60000,
                        MinSubscriptionLifetime = 10000
                    },
                    TransportQuotas = new TransportQuotas
                    {
                        OperationTimeout = 6000,
                        MaxMessageSize = 1_048_576,
                        MaxStringLength = 1_048_576,
                        MaxByteStringLength = 1_048_576,
                        MaxArrayLength = 65_535
                    }
                };
            }

            private ITransportChannel CreateManagedChannel(ITelemetryContext telemetry)
            {
                var channel = new ScriptedChannel(Configuration.CreateMessageContext(), telemetry);
                CreatedChannels.Add(channel);
                return channel.Channel;
            }

            private readonly Mock<ITransportChannelBindings> m_bindings;
        }

        private sealed class ScriptedChannel
        {
            public ScriptedChannel(IServiceMessageContext messageContext, ITelemetryContext? telemetry = null)
            {
                m_messageContext = messageContext;
                Mock = new Mock<IChannel>();
                Mock.Setup(c => c.SupportedFeatures).Returns(() => SupportedFeatures);
                Mock.Setup(c => c.EndpointDescription).Returns(() => m_description);
                Mock.Setup(c => c.EndpointConfiguration).Returns(() => m_endpointConfiguration);
                Mock.Setup(c => c.MessageContext).Returns(m_messageContext);
                Mock.Setup(c => c.ChannelThumbprint).Returns([]);
                Mock.Setup(c => c.ClientChannelCertificate).Returns([]);
                Mock.Setup(c => c.ServerChannelCertificate).Returns([]);
                Mock.Setup(c => c.OperationTimeout).Returns(() => m_operationTimeout);
                Mock.SetupSet(c => c.OperationTimeout = It.IsAny<int>())
                    .Callback<int>(value => m_operationTimeout = value);
                Mock.Setup(c => c.CurrentToken).Returns((ChannelToken?)null);
                Mock.Setup(c => c.OpenAsync(
                        It.IsAny<Uri>(),
                        It.IsAny<TransportChannelSettings>(),
                        It.IsAny<CancellationToken>()))
                    .Returns<Uri, TransportChannelSettings, CancellationToken>(OpenAsync);
                Mock.Setup(c => c.OpenAsync(
                        It.IsAny<ITransportWaitingConnection>(),
                        It.IsAny<TransportChannelSettings>(),
                        It.IsAny<CancellationToken>()))
                    .Returns<ITransportWaitingConnection, TransportChannelSettings, CancellationToken>(
                        OpenReverseAsync);
                Mock.Setup(c => c.ReconnectAsync(
                        It.IsAny<ITransportWaitingConnection?>(),
                        It.IsAny<CancellationToken>()))
                    .Returns<ITransportWaitingConnection?, CancellationToken>(ReconnectAsync);
                Mock.Setup(c => c.SendRequestAsync(
                        It.IsAny<IServiceRequest>(),
                        It.IsAny<CancellationToken>()))
                    .Returns<IServiceRequest, CancellationToken>(SendRequestAsync);
                Mock.Setup(c => c.CloseAsync(It.IsAny<CancellationToken>()))
                    .Returns<CancellationToken>(CloseAsync);
                Mock.Setup(c => c.Dispose()).Callback(() => DisposeCount++);
                _ = telemetry;
            }

            public Mock<IChannel> Mock { get; }

            public ITransportChannel Channel => Mock.Object;

            public TransportChannelFeatures SupportedFeatures { get; set; }

            public int CloseCount { get; private set; }

            public int DisposeCount { get; private set; }

            public int ReconnectCount { get; private set; }

            public int SendRequestCount { get; private set; }

            public string? OpenedEndpointUrl { get; private set; }

            public void OpenForEndpoint(ConfiguredEndpoint endpoint)
            {
                m_description = endpoint.Description;
                m_endpointConfiguration = endpoint.Configuration!;
                OpenedEndpointUrl = endpoint.Description.EndpointUrl;
            }

            private ValueTask OpenAsync(
                Uri uri,
                TransportChannelSettings settings,
                CancellationToken ct)
            {
                _ = uri;
                _ = ct;
                OpenWithSettings(settings);
                return new ValueTask();
            }

            private ValueTask OpenReverseAsync(
                ITransportWaitingConnection connection,
                TransportChannelSettings settings,
                CancellationToken ct)
            {
                _ = connection;
                _ = ct;
                OpenWithSettings(settings);
                return new ValueTask();
            }

            private ValueTask ReconnectAsync(
                ITransportWaitingConnection? connection,
                CancellationToken ct)
            {
                _ = connection;
                _ = ct;
                ReconnectCount++;
                return new ValueTask();
            }

            private ValueTask<IServiceResponse> SendRequestAsync(
                IServiceRequest request,
                CancellationToken ct)
            {
                _ = ct;
                SendRequestCount++;
                return new ValueTask<IServiceResponse>(CreateResponse(request));
            }

            private ValueTask CloseAsync(CancellationToken ct)
            {
                _ = ct;
                CloseCount++;
                return new ValueTask();
            }

            private void OpenWithSettings(TransportChannelSettings settings)
            {
                EndpointDescription description = settings.Description
                    ?? throw new InvalidOperationException("Transport settings do not include an endpoint.");
                EndpointConfiguration endpointConfiguration = settings.Configuration
                    ?? throw new InvalidOperationException("Transport settings do not include endpoint configuration.");

                m_description = description;
                m_endpointConfiguration = endpointConfiguration;
                OpenedEndpointUrl = description.EndpointUrl;
            }

            private IServiceResponse CreateResponse(IServiceRequest request)
            {
                return request switch
                {
                    CreateSessionRequest => CreateSessionResponse(),
                    ActivateSessionRequest => CreateActivateSessionResponse(),
                    ReadRequest readRequest => CreateReadResponse(readRequest),
                    CloseSessionRequest => new CloseSessionResponse { ResponseHeader = CreateGoodHeader() },
                    _ => throw ServiceResultException.Create(
                        StatusCodes.BadServiceUnsupported,
                        "Unexpected request type {0}.",
                        request.GetType().Name)
                };
            }

            private CreateSessionResponse CreateSessionResponse()
            {
                m_sessionCounter++;
                string suffix = m_sessionCounter.ToString(CultureInfo.InvariantCulture);
                return new CreateSessionResponse
                {
                    ResponseHeader = CreateGoodHeader(),
                    SessionId = new NodeId($"session-{suffix}", 1),
                    AuthenticationToken = new NodeId($"token-{suffix}", 1),
                    RevisedSessionTimeout = 60000,
                    ServerNonce = ByteString.Empty,
                    ServerCertificate = ByteString.Empty,
                    ServerSignature = new SignatureData(),
                    ServerEndpoints = [m_description],
                    MaxRequestMessageSize = 1_048_576
                };
            }

            private static ActivateSessionResponse CreateActivateSessionResponse()
            {
                return new ActivateSessionResponse
                {
                    ResponseHeader = CreateGoodHeader(),
                    ServerNonce = ByteString.Empty,
                    Results = [],
                    DiagnosticInfos = []
                };
            }

            private static ReadResponse CreateReadResponse(ReadRequest request)
            {
                return new ReadResponse
                {
                    ResponseHeader = CreateGoodHeader(),
                    Results = CreateReadResults(request),
                    DiagnosticInfos = []
                };
            }

            private static ResponseHeader CreateGoodHeader()
            {
                return new ResponseHeader
                {
                    ServiceResult = StatusCodes.Good,
                    Timestamp = DateTime.UtcNow
                };
            }

            private static ArrayOf<DataValue> CreateReadResults(ReadRequest request)
            {
                int count = request.NodesToRead.Count;
                if (count == 1 &&
                    Equals(request.NodesToRead[0].NodeId, VariableIds.Server_ServerStatus_State))
                {
                    return [CreateDataValue(new Variant((int)ServerState.Running))];
                }

                if (count == 2)
                {
                    return
                    [
                        CreateDataValue(new Variant(ArrayOf.Wrapped(Namespaces.OpcUa))),
                        CreateDataValue(new Variant(ArrayOf.Wrapped("urn:localhost:server")))
                    ];
                }

                var values = new DataValue[count];
                for (int index = 0; index < count; index++)
                {
                    if (count > 1 && index is 12 or 13 or 14)
                    {
                        values[index] = CreateDataValue(new Variant((ushort)0));
                    }
                    else if (count > 1 && index == 18)
                    {
                        values[index] = CreateDataValue(new Variant(0d));
                    }
                    else
                    {
                        values[index] = CreateDataValue(new Variant(0u));
                    }
                }
                return new ArrayOf<DataValue>(values);
            }

            private static DataValue CreateDataValue(Variant value)
            {
                return new DataValue(value, StatusCodes.Good);
            }

            private readonly IServiceMessageContext m_messageContext;
            private EndpointDescription m_description = new();
            private EndpointConfiguration m_endpointConfiguration = new();
            private int m_operationTimeout;
            private int m_sessionCounter;
        }

        private sealed class ChannelActivityListener : IDisposable
        {
            public ChannelActivityListener()
            {
                m_listener = new ActivityListener
                {
                    ShouldListenTo = source => source.Name == "Opc.Ua.ChannelManager",
                    Sample = (ref ActivityCreationOptions<ActivityContext> _) =>
                        ActivitySamplingResult.AllDataAndRecorded,
                    ActivityStopped = activity =>
                    {
                        StoppedActivities.Add(activity);
                        _ = m_stoppedActivity.TrySetResult(activity);
                    }
                };
                ActivitySource.AddActivityListener(m_listener);
            }

            public List<Activity> StoppedActivities { get; } = [];

            public async Task<Activity> WaitForStoppedActivityAsync(string operationName)
            {
                Activity activity = await m_stoppedActivity.Task
                    .WaitAsync(TimeSpan.FromSeconds(5))
                    .ConfigureAwait(false);

                Assert.That(activity.OperationName, Is.EqualTo(operationName));
                return activity;
            }

            public void Dispose()
            {
                m_listener.Dispose();
            }

            private readonly ActivityListener m_listener;
            private readonly TaskCompletionSource<Activity> m_stoppedActivity = new(
                TaskCreationOptions.RunContinuationsAsynchronously);
        }

        private sealed class ChannelEventListener : EventListener
        {
            public ConcurrentQueue<ChannelEventRecord> Events { get; } = new();

            public IEnumerable<string> EventNames => Events.Select(e => e.Name);

            public string FormatEvents()
            {
                var builder = new StringBuilder();
                // ConcurrentQueue enumeration is snapshot-stable so it
                // races safely with concurrent EventWritten callbacks
                // arriving on the EventSource's worker thread.
                foreach (ChannelEventRecord record in Events)
                {
                    builder.Append(record.Name);
                    if (record.Payload.Count > 0)
                    {
                        builder.Append(' ');
                        builder.Append(string.Join(
                            ", ",
                            record.Payload.Select(p => $"{p.Key}={p.Value}")));
                    }
                    builder.AppendLine();
                }
                return builder.ToString();
            }

            protected override void OnEventSourceCreated(EventSource eventSource)
            {
                if (eventSource.Name == "Opc.Ua.ChannelManager")
                {
                    EnableEvents(eventSource, EventLevel.LogAlways);
                }
            }

            protected override void OnEventWritten(EventWrittenEventArgs eventData)
            {
                string name = eventData.EventName ?? eventData.EventId.ToString(CultureInfo.InvariantCulture);
                var payload = new Dictionary<string, object?>();
                IList<object?>? payloadValues = eventData.Payload;
                IList<string>? payloadNames = eventData.PayloadNames;
                if (payloadValues != null)
                {
                    for (int i = 0; i < payloadValues.Count; i++)
                    {
                        string key = payloadNames?[i] ?? i.ToString(CultureInfo.InvariantCulture);
                        payload[key] = payloadValues[i];
                    }
                }
                Events.Enqueue(new ChannelEventRecord(name, payload));
            }
        }

        private sealed class ChannelEventRecord
        {
            public ChannelEventRecord(string name, Dictionary<string, object?> payload)
            {
                Name = name;
                Payload = payload;
            }

            public string Name { get; }

            public Dictionary<string, object?> Payload { get; }
        }

        private sealed class ChannelMetricListener : IDisposable
        {
            public ChannelMetricListener()
            {
                m_listener = new MeterListener
                {
                    InstrumentPublished = (instrument, listener) =>
                    {
                        if (instrument.Name.StartsWith("opcua.channel.", StringComparison.Ordinal))
                        {
                            listener.EnableMeasurementEvents(instrument);
                        }
                    }
                };
                m_listener.SetMeasurementEventCallback<long>(OnLongMeasurementRecorded);
                m_listener.SetMeasurementEventCallback<double>(OnDoubleMeasurementRecorded);
                m_listener.Start();
            }

            public ConcurrentQueue<MeasurementRecord> Measurements { get; } = new();

            public void RecordObservableInstruments()
            {
                m_listener.RecordObservableInstruments();
            }

            public bool HasMeasurement(
                string instrumentName,
                params KeyValuePair<string, object?>[] tags)
            {
                return HasMeasurement(instrumentName, null, tags);
            }

            public bool HasMeasurement(
                string instrumentName,
                double? value,
                params KeyValuePair<string, object?>[] tags)
            {
                // Snapshot under enumeration to avoid races with the
                // metric callbacks that fire concurrently from the
                // channel manager's threadpool teardown work.
                foreach (MeasurementRecord m in Measurements)
                {
                    if (m.InstrumentName == instrumentName &&
                        (value == null || m.Value == value.Value) &&
                        tags.All(tag => m.Tags.TryGetValue(tag.Key, out object? actual) &&
                            Equals(actual, tag.Value)))
                    {
                        return true;
                    }
                }
                return false;
            }

            public string FormatMeasurements()
            {
                var builder = new StringBuilder();
                // ConcurrentQueue enumeration is snapshot-stable.
                foreach (MeasurementRecord measurement in Measurements)
                {
                    string tags = string.Join(
                        ", ",
                        measurement.Tags.Select(t => $"{t.Key}={t.Value}"));
                    builder
                        .Append(measurement.InstrumentName)
                        .Append('=')
                        .Append(measurement.Value.ToString(CultureInfo.InvariantCulture))
                        .Append(" {")
                        .Append(tags)
                        .AppendLine("}");
                }
                return builder.ToString();
            }

            public void Dispose()
            {
                m_listener.Dispose();
            }

            private void OnLongMeasurementRecorded(
                Instrument instrument,
                long measurement,
                ReadOnlySpan<KeyValuePair<string, object?>> tags,
                object? state)
            {
                Measurements.Enqueue(new MeasurementRecord(instrument.Name, measurement, tags.ToArray()));
            }

            private void OnDoubleMeasurementRecorded(
                Instrument instrument,
                double measurement,
                ReadOnlySpan<KeyValuePair<string, object?>> tags,
                object? state)
            {
                Measurements.Enqueue(new MeasurementRecord(instrument.Name, measurement, tags.ToArray()));
            }

            private readonly MeterListener m_listener;
        }

        private sealed class MeasurementRecord
        {
            public MeasurementRecord(
                string instrumentName,
                double value,
                KeyValuePair<string, object?>[] tags)
            {
                InstrumentName = instrumentName;
                Value = value;
                Tags = tags.ToDictionary(t => t.Key, t => t.Value);
            }

            public string InstrumentName { get; }

            public double Value { get; }

            public Dictionary<string, object?> Tags { get; }
        }

        private sealed class TestParticipant : IReconnectParticipant
        {
            private readonly Func<IManagedTransportChannel, int, CancellationToken,
                ParticipantReconnectResult>? m_onReconnect;
            private int m_notificationCount;

            public TestParticipant(
                string id,
                ConfiguredEndpoint endpoint,
                Func<IManagedTransportChannel, int, CancellationToken,
                    ParticipantReconnectResult>? onReconnect = null)
            {
                Id = id;
                Endpoint = endpoint;
                m_onReconnect = onReconnect;
            }

            public string Id { get; }
            public ConfiguredEndpoint Endpoint { get; }
            public int NotificationCount => Volatile.Read(ref m_notificationCount);

            public ValueTask<ParticipantReconnectResult> OnReconnectAsync(
                IManagedTransportChannel channel,
                int reconnectAttempt,
                CancellationToken ct)
            {
                Interlocked.Increment(ref m_notificationCount);
                ParticipantReconnectResult result = m_onReconnect?.Invoke(channel, reconnectAttempt, ct)
                    ?? ParticipantReconnectResult.Reactivated;
                return new ValueTask<ParticipantReconnectResult>(result);
            }
        }
    }
}
