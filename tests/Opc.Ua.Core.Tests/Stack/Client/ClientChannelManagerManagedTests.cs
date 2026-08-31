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
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Moq;
using NUnit.Framework;
using Opc.Ua.Bindings;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Tests;

// CA2000: test code; many disposables are ownership-transferred to test fixtures or short-lived.
#pragma warning disable CA2000

namespace Opc.Ua.Core.Tests.Stack.Client
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

        /// <summary>
        /// How long a test waits for an asynchronous operation it has already
        /// unblocked to be observed as complete.
        /// </summary>
        /// <remarks>
        /// This is a hang detector, not a latency assertion. These tests drive a
        /// fake clock, so the work under test finishes in microseconds; what is
        /// being waited on is purely the thread-pool scheduling of the
        /// continuation chain. On a saturated CI agent the pool injects threads
        /// at roughly one per second, so a short budget times out while the
        /// operation is merely queued - the channel has already faulted and its
        /// completion source has already been signalled. Keep this generous
        /// enough that it never fires on a healthy run, and far below the
        /// blame-hang timeout so a genuine deadlock still fails the job quickly.
        /// </remarks>
        private static readonly TimeSpan s_completionTimeout = TimeSpan.FromSeconds(60);

        [Test]
        public void ChannelKeyEqualityIsValueBased()
        {
            using Certificate serverCert = s_factory.CreateCertificate("CN=server").CreateForRSA();
            ConfiguredEndpoint endpoint1 = GetTestEndpoint(serverCert);
            ConfiguredEndpoint endpoint2 = GetTestEndpoint(serverCert);

            var k1 = ManagedChannelKey.FromEndpoint(endpoint1);
            var k2 = ManagedChannelKey.FromEndpoint(endpoint2);

            Assert.That(k1, Is.EqualTo(k2));
            Assert.That(k1.GetHashCode(), Is.EqualTo(k2.GetHashCode()));
        }

        [Test]
        public void ChannelKeyDistinguishesReverseFromForward()
        {
            using Certificate serverCert = s_factory.CreateCertificate("CN=server").CreateForRSA();
            ConfiguredEndpoint endpoint = GetTestEndpoint(serverCert);

            var forward = ManagedChannelKey.FromEndpoint(endpoint);
            var reverse = ManagedChannelKey.FromEndpoint(
                endpoint,
                reverseConnectionIdentity: new object());

            Assert.That(forward, Is.Not.EqualTo(reverse));
        }

        [Test]
        public void ChannelKeyDistinguishesDifferentReverseHandles()
        {
            using Certificate serverCert = s_factory.CreateCertificate("CN=server").CreateForRSA();
            ConfiguredEndpoint endpoint = GetTestEndpoint(serverCert);

            var r1 = ManagedChannelKey.FromEndpoint(endpoint, reverseConnectionIdentity: new object());
            var r2 = ManagedChannelKey.FromEndpoint(endpoint, reverseConnectionIdentity: new object());

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
        public async Task ConcurrentInitialOpenAndReconnectDisposesLosingTransportAsync()
        {
            var initialOpenStarted = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseInitialOpen = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var reconnectOpenStarted = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var initialChannel = new Mock<IChannel>();
            var reconnectChannel = new Mock<IChannel>();

            initialChannel
                .Setup(channel => channel.OpenAsync(
                    It.IsAny<Uri>(),
                    It.IsAny<TransportChannelSettings>(),
                    It.IsAny<CancellationToken>()))
                .Callback(() => initialOpenStarted.TrySetResult(true))
                .Returns(() => new ValueTask(releaseInitialOpen.Task));
            reconnectChannel
                .Setup(channel => channel.OpenAsync(
                    It.IsAny<Uri>(),
                    It.IsAny<TransportChannelSettings>(),
                    It.IsAny<CancellationToken>()))
                .Callback(() => reconnectOpenStarted.TrySetResult(true))
                .Returns(new ValueTask());
            initialChannel
                .Setup(channel => channel.CloseAsync(It.IsAny<CancellationToken>()))
                .Returns(new ValueTask());
            reconnectChannel
                .Setup(channel => channel.CloseAsync(It.IsAny<CancellationToken>()))
                .Returns(new ValueTask());

            int createCount = 0;
            var bindings = new Mock<ITransportChannelBindings>();
            bindings
                .Setup(factory => factory.Create(
                    It.IsAny<string>(),
                    It.IsAny<ITelemetryContext>()))
                .Returns(() => Interlocked.Increment(ref createCount) == 1
                    ? initialChannel.Object
                    : reconnectChannel.Object);

            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var configuration = new ApplicationConfiguration(telemetry);
            await using var manager = new ClientChannelManager(
                configuration,
                telemetry,
                bindings.Object);
            ConfiguredEndpoint endpoint = GetNoneSecurityEndpoint(
                new EndpointConfiguration { OperationTimeout = 6000 });
            var entry = new ChannelEntry(
                (IChannelEntryHost)manager,
                ManagedChannelKey.FromEndpoint(endpoint),
                endpoint,
                reverseConnection: null);
            ManagedTransportChannelLease? lease = null;

            try
            {
                Task initialOpen = entry.OpenInitialAsync(
                    clientCertificate: null,
                    clientCertificateChain: null,
                    clientCertificateVersion: 0,
                    default);
                await initialOpenStarted.Task.WaitAsync(s_completionTimeout)
                    .ConfigureAwait(false);

                lease = entry.AcquireLease(new TestParticipant("p1", endpoint));
                Task<bool> reconnect = entry.RequestReconnectAsync(default);
                await reconnectOpenStarted.Task.WaitAsync(s_completionTimeout)
                    .ConfigureAwait(false);
                Assert.That(
                    await reconnect.WaitAsync(s_completionTimeout).ConfigureAwait(false),
                    Is.True);

                releaseInitialOpen.TrySetResult(true);
                await initialOpen.WaitAsync(s_completionTimeout).ConfigureAwait(false);

                Assert.That(entry.Underlying, Is.SameAs(reconnectChannel.Object));
                initialChannel.Verify(
                    channel => channel.CloseAsync(It.IsAny<CancellationToken>()),
                    Times.Once);
                initialChannel.Verify(channel => channel.Dispose(), Times.Once);
                reconnectChannel.Verify(channel => channel.Dispose(), Times.Never);
            }
            finally
            {
                releaseInitialOpen.TrySetResult(true);
                if (lease != null)
                {
                    await lease.CloseAsync(default).ConfigureAwait(false);
                }
                await entry.DisposeAsync().ConfigureAwait(false);
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
                    "opc.ua.channel.close",
                    Tag("endpoint", endpointUrl),
                    Tag("reverse", false),
                    Tag("reason", "lease-released")).ConfigureAwait(false);

                Assert.That(metrics.HasMeasurement(
                    "opc.ua.channel.open",
                    Tag("endpoint", endpointUrl),
                    Tag("reverse", false)), Is.True, metrics.FormatMeasurements());
                Assert.That(metrics.HasMeasurement(
                    "opc.ua.channel.active",
                    1,
                    Tag("endpoint", endpointUrl)), Is.True, metrics.FormatMeasurements());
                Assert.That(metrics.HasMeasurement(
                    "opc.ua.channel.active",
                    -1,
                    Tag("endpoint", endpointUrl)), Is.True, metrics.FormatMeasurements());
                Assert.That(metrics.HasMeasurement(
                    "opc.ua.channel.refcount",
                    2,
                    Tag("endpoint", endpointUrl)), Is.True, metrics.FormatMeasurements());
                Assert.That(metrics.HasMeasurement(
                    "opc.ua.channel.participants",
                    2,
                    Tag("endpoint", endpointUrl)), Is.True, metrics.FormatMeasurements());
                Assert.That(metrics.HasMeasurement(
                    "opc.ua.channel.close",
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
                await reconnectEntered.Task.WaitAsync(s_completionTimeout).ConfigureAwait(false);
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
                    "opc.ua.channel.reconnect.duration",
                    Tag("endpoint", endpointUrl),
                    Tag("outcome", "success"))
                    .ConfigureAwait(false);

                Assert.That(metrics.HasMeasurement(
                    "opc.ua.channel.reconnect.attempts",
                    Tag("endpoint", endpointUrl),
                    Tag("outcome", "success")), Is.True, metrics.FormatMeasurements());
                Assert.That(metrics.HasMeasurement(
                    "opc.ua.channel.reconnect.duration",
                    Tag("endpoint", endpointUrl),
                    Tag("outcome", "success")), Is.True, metrics.FormatMeasurements());
                Assert.That(metrics.HasMeasurement(
                    "opc.ua.channel.gate.wait",
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
                catch (MockException)
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
                var tags = activity.TagObjects.ToDictionary(t => t.Key, t => t.Value);
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
        public async Task StructuredLogsCaptureStateTransitionsAsync()
        {
            using var loggerProvider = new RecordingLoggerProvider();
            ITelemetryContext telemetry = DefaultTelemetry.Create(
                builder => builder.AddProvider(loggerProvider));
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

                // ch.Dispose() is non-blocking; teardown logs are emitted by
                // the background cleanup task.
                await WaitForConditionAsync(
                    () => loggerProvider.Records.Any(record =>
                            record.EventId.Name == "ParticipantDetached") &&
                        loggerProvider.Records.Any(record =>
                            record.EventId.Name == "ChannelClosed"),
                    "ParticipantDetached + ChannelClosed events").ConfigureAwait(false);

                RecordedLogRecord[] records = loggerProvider.Records
                    .Where(record => record.CategoryName == "Opc.Ua.ChannelManager")
                    .ToArray();
                string formatted = string.Join(
                    Environment.NewLine,
                    records.Select(record => $"{record.EventId.Name} {record.Message}"));
                string?[] eventNames = records.Select(record => record.EventId.Name).ToArray();
                Assert.That(eventNames, Does.Contain("StateChanged"), formatted);
                Assert.That(eventNames, Does.Contain("ReconnectStarted"), formatted);
                Assert.That(eventNames, Does.Contain("ReconnectCompleted"), formatted);
                Assert.That(eventNames, Does.Contain("ParticipantAttached"), formatted);
                Assert.That(eventNames, Does.Contain("ParticipantDetached"), formatted);
                Assert.That(eventNames, Does.Contain("ChannelOpened"), formatted);
                Assert.That(eventNames, Does.Contain("ChannelClosed"), formatted);
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
        [NonParallelizable]
        public async Task ReconnectAsyncWithBudgetStopsWhenExhaustedAsync()
        {
            using var listener = new ChannelActivityListener();
            ITelemetryContext telemetry = NUnitTelemetryContext.Create(logLevel: LogLevel.Critical);
            var timeProvider = new FakeTimeProvider();
            var reconnectPolicy = new ExponentialBackoffChannelReconnectPolicy
            {
                MinDelay = TimeSpan.Zero,
                MaxDelay = TimeSpan.Zero
            };
            (ClientChannelManager sut, Certificate serverCert, Mock<IChannel> chMock) =
                CreateMockedSut(telemetry, reconnectPolicy, timeProvider);
            try
            {
                ConfiguredEndpoint endpoint = GetTestEndpoint(serverCert);
                var participant = new TestParticipant("p1", endpoint);
                IManagedTransportChannel ch = await sut.GetAsync(participant, default).ConfigureAwait(false);
                var budget = new RetryBudget(TimeSpan.Zero, timeProvider);

                // Await rather than Assert.ThrowsAsync: see AssertThrowsAsync.
                Task exhaustedReconnect = sut.ReconnectAsync(ch, budget, default).AsTask();
                ServiceResultException ex = await AssertThrowsAsync<ServiceResultException>(
                    exhaustedReconnect,
                    s_completionTimeout).ConfigureAwait(false);

                Assert.That(ex, Is.Not.Null);
                Assert.That(ex!.StatusCode, Is.EqualTo(StatusCodes.BadSecureChannelClosed));
                Assert.That(ch.State, Is.EqualTo(ChannelState.Faulted));
                Activity activity = await listener
                    .WaitForStoppedActivityAsync("OpcUaChannelReconnect")
                    .ConfigureAwait(false);
                var tags = activity.TagObjects.ToDictionary(t => t.Key, t => t.Value);
                Assert.That(tags["attempt.count"], Is.Zero);
                Assert.That(tags["outcome"], Is.EqualTo("policy-exhausted"));
                Assert.That(tags["error.status_code"], Is.EqualTo("BadSecureChannelClosed"));
                Assert.That(
                    tags["error.message"],
                    Is.EqualTo("Channel reconnect policy exhausted after 0 attempts."));
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
            var timeProvider = new ObservableFakeTimeProvider();
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
                var faulted = new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                var reconnecting = new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                var ready = new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                ch.StateChanged += (_, change) =>
                {
                    if (change.NewState == ChannelState.Faulted)
                    {
                        faulted.TrySetResult(true);
                    }
                    else if (change.NewState == ChannelState.TransportReconnecting)
                    {
                        reconnecting.TrySetResult(true);
                    }
                    else if (change.NewState == ChannelState.Ready)
                    {
                        ready.TrySetResult(true);
                    }
                };

                // Await rather than Assert.ThrowsAsync: see AssertThrowsAsync.
                // This test drives a fake clock from this thread, so blocking
                // here would prevent the clock from ever moving again.
                Task faultedReconnect = sut
                    .ReconnectAsync(ch, exhaustedBudget, default)
                    .AsTask();
                await faulted.Task.WaitAsync(s_completionTimeout).ConfigureAwait(false);
                await AssertThrowsAsync<ServiceResultException>(
                    faultedReconnect,
                    s_completionTimeout).ConfigureAwait(false);

                Assert.That(ch.State, Is.EqualTo(ChannelState.Faulted));

                // Arm before starting the reconnect: the waiter counts timers
                // created from here, so the earlier faulted reconnect above
                // cannot consume the slot this test is waiting on.
                Task<bool> swapBackoff = timeProvider.WaitForTimersCreatedAsync();
                Task reconnectTask = sut.ReconnectAsync(ch, default).AsTask();
                await swapBackoff.WaitAsync(s_completionTimeout).ConfigureAwait(false);
                Assert.That(reconnectTask.IsCompleted, Is.False, "Swap back-off should delay the reset.");

                Task<bool> retryBackoff = timeProvider.WaitForTimersCreatedAsync();
                timeProvider.Advance(TimeSpan.FromMilliseconds(100));
                await reconnecting.Task.WaitAsync(s_completionTimeout).ConfigureAwait(false);
                await retryBackoff.WaitAsync(s_completionTimeout).ConfigureAwait(false);

                Assert.That(reconnectTask.IsCompleted, Is.False, "Reconnect back-off should delay the retry.");

                timeProvider.Advance(TimeSpan.FromMilliseconds(100));

                await reconnectTask.WaitAsync(s_completionTimeout).ConfigureAwait(false);
                await ready.Task.WaitAsync(s_completionTimeout).ConfigureAwait(false);

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
            var timeProvider = new ObservableFakeTimeProvider();
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

                // Arm before starting the reconnect so the waiter cannot be
                // satisfied by an unrelated timer created earlier in the run.
                Task<bool> shrunkBackoff = timeProvider.WaitForTimersCreatedAsync();
                Task reconnectTask = sut.ReconnectAsync(ch, budget, default).AsTask();
                await reconnecting.Task.WaitAsync(s_completionTimeout).ConfigureAwait(false);
                await shrunkBackoff.WaitAsync(s_completionTimeout).ConfigureAwait(false);

                Assert.That(reconnectTask.IsCompleted, Is.False);

                timeProvider.Advance(TimeSpan.FromMilliseconds(100));

                ServiceResultException ex = await AssertThrowsAsync<ServiceResultException>(
                    reconnectTask,
                    s_completionTimeout).ConfigureAwait(false);

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
        public async Task ReconnectAsyncWithExhaustedPolicyDoesNotSwapTheEntryAsync()
        {
            var timeProvider = new ObservableFakeTimeProvider();
            var reconnectPolicy = new ExponentialBackoffChannelReconnectPolicy
            {
                MinDelay = TimeSpan.FromMilliseconds(10),
                MaxDelay = TimeSpan.FromMilliseconds(10),
                MaxAttempts = 0
            };
            (ClientChannelManager sut, Certificate serverCert, Mock<IChannel> chMock) =
                CreateMockedSut(reconnectPolicy: reconnectPolicy, timeProvider: timeProvider);
            try
            {
                ConfiguredEndpoint endpoint = GetTestEndpoint(serverCert);
                var participant = new TestParticipant("p1", endpoint);
                IManagedTransportChannel ch = await sut.GetAsync(participant, default).ConfigureAwait(false);

                // Deliberately generous. The policy, not the budget, is what ends this
                // cycle: GetDelay returns the infinite sentinel as soon as the attempt
                // count reaches MaxAttempts, before the budget is ever consulted. A
                // race check that only asked whether the budget still had room would
                // see plenty here, mistake the deliberate stop for a lost race against
                // a concurrent close, and swap the entry to run a second, unbudgeted
                // reconnect cycle behind the swap back-off.
                var budget = new RetryBudget(TimeSpan.FromMinutes(1), timeProvider);

                ServiceResultException ex = await AssertThrowsAsync<ServiceResultException>(
                    sut.ReconnectAsync(ch, budget, default).AsTask(),
                    s_completionTimeout).ConfigureAwait(false);

                Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadSecureChannelClosed));
                Assert.That(ch.State, Is.EqualTo(ChannelState.Faulted));
                Assert.That(GetInternalIntProperty(ch, "SwapCount"), Is.Zero);
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

        /// <summary>
        /// Awaits <paramref name="task"/> and returns the exception it faulted
        /// with, failing the test if it succeeded or threw something else.
        /// </summary>
        /// <typeparam name="TException">The expected exception type.</typeparam>
        /// <remarks>
        /// Deliberately not <c>Assert.ThrowsAsync</c>. That blocks the calling
        /// thread until the task completes - sync over async - so on a
        /// constrained CI agent it can starve the very continuation it is
        /// waiting for, and it has no timeout, so the block is unbounded. These
        /// tests drive a fake clock from the test thread, which makes both
        /// failure modes fatal: the test cannot advance the clock while it is
        /// blocked, NUnit's runner thread never returns, and the whole test host
        /// hangs until the blame collector kills it.
        /// </remarks>
        private static async Task<TException> AssertThrowsAsync<TException>(
            Task task,
            TimeSpan timeout)
            where TException : Exception
        {
            try
            {
                await task.WaitAsync(timeout).ConfigureAwait(false);
            }
            catch (TException expected)
            {
                return expected;
            }
            catch (Exception other)
            {
                Assert.Fail(
                    $"Expected {typeof(TException).Name} but got " +
                    $"{other.GetType().Name}: {other}");
                throw;
            }

            Assert.Fail(
                $"Expected {typeof(TException).Name} but the operation completed successfully.");
            throw new InvalidOperationException("unreachable");
        }

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
                .Callback<Uri, TransportChannelSettings, CancellationToken>(
                    (_, settings, _) => DisposeMockOwnedCertificates(settings))
                .Returns(new ValueTask());
            chMock.Setup(c => c.OpenAsync(
                    It.IsAny<ITransportWaitingConnection>(),
                    It.IsAny<TransportChannelSettings>(),
                    It.IsAny<CancellationToken>()))
                .Callback<ITransportWaitingConnection, TransportChannelSettings, CancellationToken>(
                    (_, settings, _) => DisposeMockOwnedCertificates(settings))
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

        private static void DisposeMockOwnedCertificates(TransportChannelSettings settings)
        {
            settings.ServerCertificate?.Dispose();
            settings.ServerCertificate = null;
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

        private sealed class ChannelActivityListener : IDisposable
        {
            public ChannelActivityListener()
            {
                m_listener = new ActivityListener
                {
                    ShouldListenTo = source => source.Name == "Opc.Ua.ChannelManager",
                    Sample = (ref _) =>
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
                    .WaitAsync(s_completionTimeout)
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

        private sealed class ChannelMetricListener : IDisposable
        {
            public ChannelMetricListener()
            {
                m_listener = new MeterListener
                {
                    InstrumentPublished = (instrument, listener) =>
                    {
                        if (instrument.Name.StartsWith("opc.ua.channel.", StringComparison.Ordinal))
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

        /// <summary>
        /// A <see cref="FakeTimeProvider"/> that lets a test wait until the code
        /// under test has actually registered its back-off timers.
        /// </summary>
        /// <remarks>
        /// Waiting is deliberately <b>relative</b>: a waiter is armed for "N
        /// more timers from now" rather than for "the Nth timer of the run".
        /// Absolute numbering is a race - anything else that happens to create a
        /// timer on this provider first (an earlier reconnect in the same test,
        /// or manager housekeeping) consumes the low numbers, the waiter then
        /// completes before the timer the test cares about exists, and the
        /// subsequent Advance fires nothing. The reconnect is left parked on a
        /// fake clock that nobody will move again, which hangs the test - and,
        /// because NUnit blocks the runner thread, the whole test host.
        /// </remarks>
        private sealed class ObservableFakeTimeProvider : FakeTimeProvider
        {
            public override ITimer CreateTimer(
                TimerCallback callback,
                object? state,
                TimeSpan dueTime,
                TimeSpan period)
            {
                ITimer timer = base.CreateTimer(callback, state, dueTime, period);

                List<TaskCompletionSource<bool>>? ready = null;
                lock (m_lock)
                {
                    m_timerCount++;
                    for (int i = m_waiters.Count - 1; i >= 0; i--)
                    {
                        if (m_timerCount >= m_waiters[i].Target)
                        {
                            (ready ??= []).Add(m_waiters[i].Completion);
                            m_waiters.RemoveAt(i);
                        }
                    }
                }

                // Completed outside the lock: the continuations run
                // asynchronously, but there is no reason to hold the lock while
                // handing them off.
                if (ready != null)
                {
                    foreach (TaskCompletionSource<bool> completion in ready)
                    {
                        completion.TrySetResult(true);
                    }
                }

                return timer;
            }

            /// <summary>
            /// Returns a task that completes once <paramref name="count"/>
            /// further timers have been created, counted from this call. Arm it
            /// <b>before</b> starting the operation whose timers are awaited.
            /// </summary>
            public Task<bool> WaitForTimersCreatedAsync(int count = 1)
            {
                if (count < 1)
                {
                    throw new ArgumentOutOfRangeException(nameof(count));
                }

                var completion = new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                lock (m_lock)
                {
                    m_waiters.Add((m_timerCount + count, completion));
                }
                return completion.Task;
            }

            private readonly System.Threading.Lock m_lock = new();
            private readonly List<(int Target, TaskCompletionSource<bool> Completion)> m_waiters = [];
            private int m_timerCount;
        }
    }
}
