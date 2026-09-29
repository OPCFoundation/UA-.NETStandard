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
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Time.Testing;
using Moq;
using NUnit.Framework;
using Opc.Ua.Bindings;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests
{
    [TestFixture]
    [Parallelizable(ParallelScope.All)]
    public sealed class ResourceIsolationConnectionRateLimiterTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public void SharedConnectionChurnCannotConsumeProtectedRateTokens(bool distributed)
        {
            var clock = new FakeTimeProvider();
            using DefaultServerResourceIsolationProvider provider = CreateProvider();
            using var limiter = new ResourceIsolationConnectionRateLimiter(3, 4, provider, clock);
            var admission = new UaScConnectionAdmission(100, limiter, provider, timeProvider: clock);
            UaScConnectionAdmission secondListener = admission.CreateIndependentScope();
            try
            {
                for (int ii = 0; ii < 2; ii++)
                {
                    AdmitAndClose(ii == 0 ? admission : secondListener, Peer(distributed ? 10 + ii : 10));
                }
                Assert.That(admission.TryAcquire(Peer(20), out _), Is.False);
                Assert.That(secondListener.TryAcquire(Peer(20), out _), Is.False);
                Assert.That(provider.GetUsage(ResourceIsolationStage.Connection), Is.Zero);
                Assert.That(provider.GetUsage(ResourceIsolationStage.Handshake), Is.Zero);
                AdmitAndClose(secondListener, Peer(1));
                AdmitAndClose(admission, Peer(2));
                Assert.That(admission.TryAcquire(Peer(1), out _), Is.False);
                Assert.That(admission.TryAcquire(Peer(2), out _), Is.False);
                Assert.That(admission.TryAcquire(Peer(30), out _), Is.False);
                Assert.That(provider.TrackedOwnerCount, Is.Zero);
            }
            finally
            {
                try
                {
                    admission.Stop();
                }
                finally
                {
                    secondListener.Stop();
                }
            }
        }

        [Test]
        public void ProvisionedTrustedOwnersHaveSeparateTokensWithinOriginalBurst()
        {
            var clock = new FakeTimeProvider();
            using DefaultServerResourceIsolationProvider provider =
                CreateProvider(ServerResourceIsolationMode.TrustedReservations);
            using var limiter = new ResourceIsolationConnectionRateLimiter(5, 7, provider, clock);
            ResourceIsolationOwner shared = provider.ClassifyConnection(Peer(10));
            for (int ii = 0; ii < 3; ii++)
            {
                Assert.That(limiter.TryAdmitConnection(Peer(10), shared, out _), Is.True);
            }
            Assert.That(limiter.TryAdmitConnection(Peer(10), shared, out _), Is.False);
            for (int peer = 1; peer <= 4; peer++)
            {
                ResourceIsolationOwner owner = provider.ClassifyConnection(Peer(peer));
                Assert.That(limiter.TryAdmitConnection(Peer(peer), owner, out _), Is.True);
                Assert.That(limiter.TryAdmitConnection(Peer(peer), owner, out _), Is.False);
            }
            Assert.That(limiter.TryAdmitConnection(null, out _), Is.False);
            clock.Advance(TimeSpan.FromSeconds(1));
            Assert.That(ConsumeAvailable(limiter, shared), Is.EqualTo(1));
            for (int peer = 1; peer <= 4; peer++)
            {
                Assert.That(ConsumeAvailable(limiter, provider.ClassifyConnection(Peer(peer))), Is.EqualTo(1));
            }
        }

        [Test]
        public void ProtectedOwnersCanBorrowSharedTokensButNotAnotherOwnersReserve()
        {
            var clock = new FakeTimeProvider();
            using DefaultServerResourceIsolationProvider provider =
                CreateProvider(ServerResourceIsolationMode.TrustedReservations);
            using var limiter = new ResourceIsolationConnectionRateLimiter(5, 7, provider, clock);
            ResourceIsolationOwner tenantA = provider.ClassifyConnection(Peer(3));
            for (int ii = 0; ii < 4; ii++)
            {
                Assert.That(limiter.TryAdmitConnection(Peer(3), tenantA, out _), Is.True);
            }
            Assert.That(limiter.TryAdmitConnection(Peer(3), tenantA, out _), Is.False);
            Assert.That(limiter.TryAdmitConnection(Peer(10), provider.ClassifyConnection(Peer(10)), out _), Is.False);
            Assert.That(limiter.TryAdmitConnection(Peer(4), provider.ClassifyConnection(Peer(4)), out _), Is.True);
            Assert.That(limiter.TryAdmitConnection(Peer(1), provider.ClassifyConnection(Peer(1)), out _), Is.True);
            Assert.That(limiter.TryAdmitConnection(Peer(2), provider.ClassifyConnection(Peer(2)), out _), Is.True);
        }

        [Test]
        public void LegacyEndpointOnlyCallsCannotClaimProtectedTokens()
        {
            using DefaultServerResourceIsolationProvider provider = CreateProvider();
            using var limiter = new ResourceIsolationConnectionRateLimiter(3, 3, provider, new FakeTimeProvider());
            Assert.That(limiter.TryAdmitConnection(Peer(1), out _), Is.True);
            Assert.That(limiter.TryAdmitConnection(Peer(1), out _), Is.False);
            Assert.That(limiter.TryAdmitConnection(Peer(2), out _), Is.False);
            Assert.That(limiter.TryAdmitConnection(Peer(1), provider.ClassifyConnection(Peer(1)), out _), Is.True);
            Assert.That(limiter.TryAdmitConnection(Peer(2), provider.ClassifyConnection(Peer(2)), out _), Is.True);
        }

        [Test]
        public void ForgedAndForeignOwnersAreRejectedWithoutConsumingTokens()
        {
            using DefaultServerResourceIsolationProvider provider = CreateProvider();
            using DefaultServerResourceIsolationProvider foreign = CreateProvider();
            using var limiter = new ResourceIsolationConnectionRateLimiter(3, 3, provider, new FakeTimeProvider());
            var forged = new ResourceIsolationOwner(
                "bootstrap", ResourceIsolationClass.Bootstrap, 1, new long[] { 1, 1, 1, 1, 1, 1, 1, 1 });
            Assert.That(() => limiter.TryAdmitConnection(Peer(1), forged, out _), Throws.ArgumentException);
            Assert.That(() => limiter.TryAdmitConnection(
                Peer(1), foreign.ClassifyConnection(Peer(1)), out _), Throws.ArgumentException);
            Assert.That(limiter.TryAdmitConnection(Peer(10), provider.ClassifyConnection(Peer(10)), out _), Is.True);
            Assert.That(limiter.TryAdmitConnection(Peer(1), provider.ClassifyConnection(Peer(1)), out _), Is.True);
            Assert.That(limiter.TryAdmitConnection(Peer(2), provider.ClassifyConnection(Peer(2)), out _), Is.True);
            Assert.That(limiter.TryAdmitConnection(null, out _), Is.False);
        }

        [Test]
        public void ReplenishmentUsesOneBoundaryAndDoesNotExceedConfiguredRate()
        {
            var clock = new FakeTimeProvider();
            using DefaultServerResourceIsolationProvider provider = CreateProvider();
            using var limiter = new ResourceIsolationConnectionRateLimiter(5, 10, provider, clock);
            ResourceIsolationOwner shared = provider.ClassifyConnection(Peer(10));
            ResourceIsolationOwner bootstrap = provider.ClassifyConnection(Peer(1));
            ResourceIsolationOwner reconnect = provider.ClassifyConnection(Peer(2));
            Assert.That(ConsumeAvailable(limiter, shared), Is.EqualTo(8));
            Assert.That(ConsumeAvailable(limiter, bootstrap), Is.EqualTo(1));
            Assert.That(ConsumeAvailable(limiter, reconnect), Is.EqualTo(1));
            clock.Advance(TimeSpan.FromMilliseconds(999));
            Assert.That(limiter.TryAdmitConnection(null, shared, out TimeSpan? retryAfter), Is.False);
            Assert.That(retryAfter, Is.EqualTo(TimeSpan.FromMilliseconds(1)));
            Assert.That(limiter.TryAdmitConnection(null, bootstrap, out _), Is.False);
            clock.Advance(TimeSpan.FromMilliseconds(1));
            Assert.That(ConsumeAvailable(limiter, shared), Is.EqualTo(3));
            Assert.That(ConsumeAvailable(limiter, bootstrap), Is.EqualTo(1));
            Assert.That(ConsumeAvailable(limiter, reconnect), Is.EqualTo(1));
            Assert.That(limiter.TryAdmitConnection(null, shared, out retryAfter), Is.False);
            Assert.That(retryAfter, Is.EqualTo(TimeSpan.FromSeconds(1)));
        }

        [Test]
        public void IdleReplenishmentSaturatesBurstWithoutArithmeticOverflow()
        {
            var clock = new FakeTimeProvider();
            using DefaultServerResourceIsolationProvider provider = CreateProvider();
            using var limiter = new ResourceIsolationConnectionRateLimiter(int.MaxValue, 3, provider, clock);
            ResourceIsolationOwner shared = provider.ClassifyConnection(Peer(10));
            ResourceIsolationOwner bootstrap = provider.ClassifyConnection(Peer(1));
            ResourceIsolationOwner reconnect = provider.ClassifyConnection(Peer(2));
            Assert.That(ConsumeAvailable(limiter, shared), Is.EqualTo(1));
            Assert.That(ConsumeAvailable(limiter, bootstrap), Is.EqualTo(1));
            Assert.That(ConsumeAvailable(limiter, reconnect), Is.EqualTo(1));
            clock.Advance(TimeSpan.FromDays(365000));
            Assert.That(ConsumeAvailable(limiter, shared), Is.EqualTo(1));
            Assert.That(ConsumeAvailable(limiter, bootstrap), Is.EqualTo(1));
            Assert.That(ConsumeAvailable(limiter, reconnect), Is.EqualTo(1));
        }

        [Test]
        public async Task ConcurrentClaimsNeverExceedSharedGlobalBurstOrRateAsync()
        {
            var clock = new FakeTimeProvider();
            using DefaultServerResourceIsolationProvider provider = CreateProvider();
            using var limiter = new ResourceIsolationConnectionRateLimiter(7, 19, provider, clock);
            ResourceIsolationOwner[] owners =
            [
                provider.ClassifyConnection(Peer(10)),
                provider.ClassifyConnection(Peer(1)),
                provider.ClassifyConnection(Peer(2))
            ];
            int[] first = await RaceAdmissionsAsync(limiter, owners).ConfigureAwait(false);
            Assert.That(first[0] + first[1] + first[2], Is.EqualTo(19));
            Assert.That(first[1], Is.GreaterThanOrEqualTo(1));
            Assert.That(first[2], Is.GreaterThanOrEqualTo(1));
            clock.Advance(TimeSpan.FromSeconds(1));
            int[] second = await RaceAdmissionsAsync(limiter, owners).ConfigureAwait(false);
            Assert.That(second[0] + second[1] + second[2], Is.EqualTo(7));
            Assert.That(second[1], Is.GreaterThanOrEqualTo(1));
            Assert.That(second[2], Is.GreaterThanOrEqualTo(1));
        }

        [TestCase(0, 3)]
        [TestCase(-1, 3)]
        [TestCase(3, 0)]
        [TestCase(3, -1)]
        public void InvalidTotalsAreRejected(int rate, int burst)
        {
            using DefaultServerResourceIsolationProvider provider = CreateProvider();
            Assert.That(() => new ResourceIsolationConnectionRateLimiter(rate, burst, provider),
                Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        [Test]
        public void NullProviderOrClassifiedOwnerCannotFallBackToUnclassifiedAdmission()
        {
            var clock = new FakeTimeProvider();
            Assert.That(() => new ResourceIsolationConnectionRateLimiter(3, 3, null!, clock),
                Throws.TypeOf<ArgumentNullException>());
            using DefaultServerResourceIsolationProvider provider = CreateProvider();
            using var limiter = new ResourceIsolationConnectionRateLimiter(3, 3, provider, clock);
            Assert.That(() => limiter.TryAdmitConnection(null, null!, out _),
                Throws.TypeOf<ArgumentNullException>());
            Assert.That(limiter.TryAdmitConnection(null, out _), Is.True);
        }

        [TestCase(ServerResourceIsolationMode.Balanced, 2, 3)]
        [TestCase(ServerResourceIsolationMode.Balanced, 3, 2)]
        [TestCase(ServerResourceIsolationMode.Balanced, 1, 1)]
        [TestCase(ServerResourceIsolationMode.TrustedReservations, 4, 5)]
        [TestCase(ServerResourceIsolationMode.TrustedReservations, 5, 4)]
        public void ImpossibleReservedTotalsFailWithoutGrowingConfiguration(
            ServerResourceIsolationMode mode,
            int rate,
            int burst)
        {
            using DefaultServerResourceIsolationProvider provider = CreateProvider(mode);
            var options = new ServerRateLimitOptions { ConnectionsPerSecond = rate, ConnectionBurst = burst };
            Assert.That(() => new DefaultServerRateLimiterProvider(options, provider, new FakeTimeProvider()),
                Throws.ArgumentException.With.Message.Contains("at least one shared token"));
            Assert.That(options.ConnectionsPerSecond, Is.EqualTo(rate));
            Assert.That(options.ConnectionBurst, Is.EqualTo(burst));
        }

        [TestCase(ServerResourceIsolationMode.Balanced, 3)]
        [TestCase(ServerResourceIsolationMode.TrustedReservations, 5)]
        public void MinimumUsableTotalsContainExactlyOneSharedToken(ServerResourceIsolationMode mode, int total)
        {
            using DefaultServerResourceIsolationProvider provider = CreateProvider(mode);
            using var limiter = new ResourceIsolationConnectionRateLimiter(
                total, total, provider, new FakeTimeProvider());
            Assert.That(ConsumeAvailable(limiter, provider.ClassifyConnection(Peer(10))), Is.EqualTo(1));
            for (int peer = 1; peer < total; peer++)
            {
                Assert.That(ConsumeAvailable(limiter, provider.ClassifyConnection(Peer(peer))), Is.EqualTo(1));
            }
        }

        [TestCase(ServerResourceIsolationMode.SharedOnly)]
        [TestCase(ServerResourceIsolationMode.FairShare)]
        public void CompatibilityProfilesKeepPlainTokenBucket(ServerResourceIsolationMode mode)
        {
            using DefaultServerResourceIsolationProvider isolation = CreateProvider(mode);
            using var provider = new DefaultServerRateLimiterProvider(
                new ServerRateLimitOptions { ConnectionsPerSecond = 1, ConnectionBurst = 1 },
                isolation, new FakeTimeProvider());
            Assert.That(provider.ConnectionRateLimiter, Is.TypeOf<TokenBucketConnectionRateLimiter>());
            Assert.That(provider.ConnectionRateLimiter!.TryAdmitConnection(null, out _), Is.True);
            Assert.That(provider.ConnectionRateLimiter.TryAdmitConnection(null, out _), Is.False);
            Assert.That(() => new ResourceIsolationConnectionRateLimiter(5, 5, isolation), Throws.ArgumentException);
        }

        [TestCase(false, true)]
        [TestCase(true, false)]
        public void DisabledRateLimitsDoNotRequireProtectedTokenCapacity(bool enabled, bool connectionsEnabled)
        {
            using DefaultServerResourceIsolationProvider isolation =
                CreateProvider(ServerResourceIsolationMode.TrustedReservations);
            using var provider = new DefaultServerRateLimiterProvider(new ServerRateLimitOptions
            {
                Enabled = enabled,
                ConnectionRateLimitEnabled = connectionsEnabled,
                ConnectionsPerSecond = 1,
                ConnectionBurst = 1
            }, isolation, new FakeTimeProvider());
            Assert.That(provider.ConnectionRateLimiter, Is.Null);
        }

        [Test]
        public void DisposingLimiterDoesNotDisposeBorrowedIsolationProvider()
        {
            using DefaultServerResourceIsolationProvider isolation = CreateProvider();
            var limiter = new ResourceIsolationConnectionRateLimiter(3, 3, isolation, new FakeTimeProvider());
            limiter.Dispose();
            limiter.Dispose();
            Assert.That(() => limiter.TryAdmitConnection(null, out _), Throws.TypeOf<ObjectDisposedException>());
            ResourceIsolationOwner owner = isolation.ClassifyConnection(Peer(1));
            Assert.That(isolation.TryAcquire(ResourceIsolationStage.Connection, owner, 1,
                out IDisposable? lease, out _), Is.True);
            lease!.Dispose();
            Assert.That(isolation.TrackedOwnerCount, Is.Zero);
        }

        [Test]
        public async Task StandardServerSelectsAndRebindsOwnedProtectedRateLimiterAsync()
        {
            var clock = new FakeTimeProvider();
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            await using var server = new StandardServer(telemetry, clock)
            {
                ResourceIsolationOptions = Options(ServerResourceIsolationMode.Balanced),
                ResourceIsolationClassifier = new IngressClassifier(),
                RateLimitOptions = new ServerRateLimitOptions { ConnectionsPerSecond = 3, ConnectionBurst = 3 }
            };
            server.InitializeResourceIsolation(Configuration(), telemetry);
            server.InitializeRateLimiting();
            IConnectionRateLimiter previous = server.RateLimiterProvider!.ConnectionRateLimiter!;
            Assert.That(previous, Is.TypeOf<ResourceIsolationConnectionRateLimiter>());
            Assert.That(previous.TryAdmitConnection(null, out _), Is.True);
            Assert.That(previous.TryAdmitConnection(null, out _), Is.False);
            await server.StopAsync(CancellationToken.None).ConfigureAwait(false);
            server.InitializeResourceIsolation(Configuration(), telemetry);
            server.InitializeRateLimiting();
            var current = (IResourceIsolationConnectionRateLimiter)server.RateLimiterProvider!.ConnectionRateLimiter!;
            Assert.That(current, Is.Not.SameAs(previous));
            Assert.That(() => previous.TryAdmitConnection(null, out _), Throws.TypeOf<ObjectDisposedException>());
            ResourceIsolationOwner bootstrap = server.ResourceIsolationProvider!.ClassifyConnection(Peer(1));
            Assert.That(current.TryAdmitConnection(Peer(1), bootstrap, out _), Is.True);
            Assert.That(current.TryAdmitConnection(null, out _), Is.True);
            Assert.That(current.TryAdmitConnection(null, out _), Is.False);
        }

        [Test]
        public async Task ExplicitCustomLimiterRemainsAnAdditionalCeilingForProtectedIngressAsync()
        {
            var clock = new FakeTimeProvider();
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var custom = new Mock<IServerRateLimiterProvider>(MockBehavior.Strict);
            var limiter = new Mock<IConnectionRateLimiter>(MockBehavior.Strict);
            TimeSpan? retryAfter = TimeSpan.FromSeconds(4);
            limiter.Setup(l => l.TryAdmitConnection(It.IsAny<EndPoint>(), out retryAfter)).Returns(false);
            custom.SetupGet(p => p.ConnectionRateLimiter).Returns(limiter.Object);
            await using (var server = new StandardServer(telemetry, clock)
            {
                ResourceIsolationOptions = Options(ServerResourceIsolationMode.Balanced),
                ResourceIsolationClassifier = new IngressClassifier(),
                RateLimiterProvider = custom.Object
            })
            {
                server.InitializeResourceIsolation(Configuration(), telemetry);
                server.InitializeRateLimiting();
                Assert.That(server.RateLimiterProvider, Is.SameAs(custom.Object));
                var admission = new UaScConnectionAdmission(
                    100, server.RateLimiterProvider.ConnectionRateLimiter,
                    server.ResourceIsolationProvider, timeProvider: clock);
                try
                {
                    Assert.That(admission.TryAcquire(Peer(1), out _), Is.False);
                    var isolation = (DefaultServerResourceIsolationProvider)server.ResourceIsolationProvider!;
                    Assert.That(isolation.GetUsage(ResourceIsolationStage.Connection), Is.Zero);
                    Assert.That(isolation.GetUsage(ResourceIsolationStage.Handshake), Is.Zero);
                }
                finally
                {
                    admission.Stop();
                }
            }
            limiter.Verify(l => l.TryAdmitConnection(It.IsAny<EndPoint>(), out retryAfter), Times.Once);
            limiter.Verify(l => l.Dispose(), Times.Never);
            custom.Verify(p => p.Dispose(), Times.Never);
        }

        [Test]
        public void AdmissionWithoutClassificationUsesExistingLimiterCapability()
        {
            var limiter = new Mock<IResourceIsolationConnectionRateLimiter>(MockBehavior.Strict);
            TimeSpan? retryAfter = null;
            limiter.Setup(l => l.TryAdmitConnection(It.IsAny<EndPoint>(), out retryAfter)).Returns(true);
            var admission = new UaScConnectionAdmission(2, limiter.Object, timeProvider: new FakeTimeProvider());
            try
            {
                AdmitAndClose(admission, Peer(1));
            }
            finally
            {
                admission.Stop();
            }
            limiter.Verify(l => l.TryAdmitConnection(It.IsAny<EndPoint>(), out retryAfter), Times.Once);
            limiter.VerifyNoOtherCalls();
        }

        private static void AdmitAndClose(UaScConnectionAdmission admission, IPEndPoint peer)
        {
            Assert.That(admission.TryAcquire(peer, out UaScConnectionAdmission.Lease? lease), Is.True);
            Assert.That(lease, Is.Not.Null);
            lease!.Dispose();
            lease.Dispose();
        }

        private static int ConsumeAvailable(
            ResourceIsolationConnectionRateLimiter limiter,
            ResourceIsolationOwner owner)
        {
            int admitted = 0;
            while (admitted < 100 && limiter.TryAdmitConnection(null, owner, out _))
            {
                admitted++;
            }
            Assert.That(admitted, Is.LessThan(100), "The configured test burst must be finite.");
            return admitted;
        }

        private static async Task<int[]> RaceAdmissionsAsync(
            ResourceIsolationConnectionRateLimiter limiter,
            ResourceIsolationOwner[] owners)
        {
            int[] admitted = new int[owners.Length];
            var attempts = new Task[150];
            for (int ii = 0; ii < attempts.Length; ii++)
            {
                int index = ii % owners.Length;
                attempts[ii] = Task.Run(() =>
                {
                    if (limiter.TryAdmitConnection(null, owners[index], out _))
                    {
                        Interlocked.Increment(ref admitted[index]);
                    }
                });
            }
            await Task.WhenAll(attempts).ConfigureAwait(false);
            return admitted;
        }

        private static DefaultServerResourceIsolationProvider CreateProvider(
            ServerResourceIsolationMode mode = ServerResourceIsolationMode.Balanced)
        {
            return new DefaultServerResourceIsolationProvider(
                Options(mode).CreateRuntimePlan(Configuration(), new ServerRateLimitOptions()),
                NUnitTelemetryContext.Create(), classifier: new IngressClassifier());
        }

        private static ServerResourceIsolationOptions Options(ServerResourceIsolationMode mode)
        {
            var options = new ServerResourceIsolationOptions
            {
                Mode = mode,
                MaxRetainedMessageBytes = 10
            };
            if (mode == ServerResourceIsolationMode.TrustedReservations)
            {
                options.TrustedOwners =
                [
                    new TrustedResourceOwnerOptions { Key = "tenant-a" },
                    new TrustedResourceOwnerOptions { Key = "tenant-b" }
                ];
            }
            return options;
        }

        private static ApplicationConfiguration Configuration()
        {
            return new ApplicationConfiguration
            {
                ServerConfiguration = new ServerConfiguration { MaxSessionCount = 2, MaxChannelCount = 100 },
                TransportQuotas = new TransportQuotas { MaxMessageSize = 10, MaxBufferSize = 8192 }
            };
        }

        private static IPEndPoint Peer(int suffix)
        {
            return new IPEndPoint(IPAddress.Parse("192.0.2." + suffix), 4840);
        }

        private sealed class IngressClassifier : IResourceIsolationClassifier
        {
            public bool TryClassifyIngress(IPEndPoint? remoteEndpoint, out ResourceIsolationIdentity identity)
            {
                identity = remoteEndpoint?.Address.ToString() switch
                {
                    "192.0.2.1" => new("bootstrap", ResourceIsolationClass.Bootstrap),
                    "192.0.2.2" => new("reconnect", ResourceIsolationClass.Reconnect),
                    "192.0.2.3" => new("tenant-a", ResourceIsolationClass.Trusted),
                    "192.0.2.4" => new("tenant-b", ResourceIsolationClass.Trusted),
                    _ => default
                };
                return identity.Key != null;
            }

            public bool TryClassify(SecureChannelContext channelContext, SessionBindingContext? sessionBinding,
                out ResourceIsolationIdentity identity)
            {
                identity = default;
                return false;
            }
        }
    }
}
