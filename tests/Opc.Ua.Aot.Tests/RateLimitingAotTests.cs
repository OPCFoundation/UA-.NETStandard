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

using System.Net;
using Opc.Ua.Client;
using Opc.Ua.Server;

namespace Opc.Ua.Aot.Tests
{
    /// <summary>
    /// AOT tests that exercise the rate-limiting code paths so the
    /// <c>System.Threading.RateLimiting</c> dependency is verified under
    /// Native AOT (token bucket, concurrency limiter, and the client gate).
    /// </summary>
    public class RateLimitingAotTests
    {
        [Test]
        public async Task RuntimeIsolationPreservesBootstrapFloorAtSharedCapacityAsync()
        {
            var configuration = new ApplicationConfiguration
            {
                ServerConfiguration = new ServerConfiguration { MaxSessionCount = 75, MaxChannelCount = 1000 },
                TransportQuotas = new TransportQuotas { MaxMessageSize = 4 * 1024 * 1024, MaxBufferSize = 65535 }
            };
            var options = new ServerResourceIsolationOptions();
            ServerResourceIsolationPlan plan = options.CreateRuntimePlan(configuration, new ServerRateLimitOptions());
            using var provider = new DefaultServerResourceIsolationProvider(
                plan, DefaultTelemetry.Create(_ => { }), classifier: new BootstrapClassifier());
            ResourceIsolationStagePlan bytes = plan.GetStage(ResourceIsolationStage.ReassemblyBytes);
            ResourceIsolationOwner ordinary = provider.ClassifyConnection(
                new IPEndPoint(IPAddress.Parse("192.0.2.1"), 4840));
            ResourceIsolationOwner bootstrap = provider.ClassifyConnection(
                new IPEndPoint(IPAddress.Loopback, 4840));
            bool admitted = provider.TryAcquire(
                ResourceIsolationStage.ReassemblyBytes, ordinary, bytes.SharedCapacity,
                out IDisposable shared, out _);
            using (shared)
            {
                await Assert.That(admitted).IsTrue();
                await Assert.That(provider.TryAcquire(
                    ResourceIsolationStage.ReassemblyBytes, ordinary, 1, out IDisposable denied, out _)).IsFalse();
                denied?.Dispose();
                bool protectedAdmission = provider.TryAcquire(
                    ResourceIsolationStage.ReassemblyBytes, bootstrap, bytes.BootstrapReserved,
                    out IDisposable reserved, out _);
                using (reserved)
                {
                    await Assert.That(protectedAdmission).IsTrue();
                    await Assert.That(provider.GetUsage(ResourceIsolationStage.ReassemblyBytes))
                        .IsEqualTo(bytes.SharedCapacity + bytes.BootstrapReserved);
                }
            }
            await Assert.That(provider.GetUsage(ResourceIsolationStage.ReassemblyBytes)).IsEqualTo(0L);
            await Assert.That(provider.TrackedOwnerCount).IsEqualTo(0);
        }

        [Test]
        public async Task ServerConnectionLimiterAdmitsThenRejectsAsync()
        {
            using var limiter = new TokenBucketConnectionRateLimiter(
                connectionsPerSecond: 1,
                burst: 3);

            var remote = new IPEndPoint(IPAddress.Loopback, 4840);

            await Assert.That(limiter.TryAdmitConnection(remote, out _)).IsTrue();
            await Assert.That(limiter.TryAdmitConnection(remote, out _)).IsTrue();
            await Assert.That(limiter.TryAdmitConnection(remote, out _)).IsTrue();
            // Burst exhausted -> reject.
            await Assert.That(limiter.TryAdmitConnection(remote, out _)).IsFalse();
        }

        [Test]
        public async Task ServerSessionLimiterAcquiresAndRejectsAsync()
        {
            var options = new ServerRateLimitOptions
            {
                MaxConcurrentSessionEstablishment = 1,
                SessionEstablishmentQueueLimit = 0
            };
            using var provider = new DefaultServerRateLimiterProvider(options);

            await Assert.That(provider.ConnectionRateLimiter).IsNotNull();

            bool first = provider.TryAcquireSessionEstablishment(
                out IDisposable lease1,
                out _);
            await Assert.That(first).IsTrue();

            bool second = provider.TryAcquireSessionEstablishment(
                out IDisposable lease2,
                out _);
            await Assert.That(second).IsFalse();
            await Assert.That(lease2).IsNull();

            lease1?.Dispose();

            bool third = provider.TryAcquireSessionEstablishment(
                out IDisposable lease3,
                out _);
            await Assert.That(third).IsTrue();
            lease3?.Dispose();
        }

        [Test]
        public async Task ClientConnectGateAcquiresAndReleasesAsync()
        {
            using var gate = new RateLimiterClientConnectGate(maxConcurrency: 1);

            IDisposable lease = await gate.AcquireAsync(CancellationToken.None)
                .ConfigureAwait(false);
            await Assert.That(lease).IsNotNull();
            lease.Dispose();

            // A subsequent acquire succeeds once the first permit is released.
            IDisposable second = await gate.AcquireAsync(CancellationToken.None)
                .ConfigureAwait(false);
            await Assert.That(second).IsNotNull();
            second.Dispose();
        }

        [Test]
        public async Task ClientAdaptivePolicyBacksOffOnBusyAsync()
        {
            var policy = new ReconnectPolicy(new ReconnectPolicyOptions
            {
                Strategy = BackoffStrategy.Constant,
                InitialDelay = TimeSpan.FromSeconds(1),
                MaxDelay = TimeSpan.FromSeconds(30),
                JitterFactor = 0.0
            });

            await Assert.That(
                ReconnectPolicy.IsServerBusySignal(StatusCodes.BadServerTooBusy)).IsTrue();

            bool goodAdaptive = policy.TryGetNextDelay(
                0, StatusCodes.Good, serverRetryAfter: null, out TimeSpan? good);
            bool busyAdaptive = policy.TryGetNextDelay(
                0,
                StatusCodes.BadServerTooBusy,
                serverRetryAfter: null,
                out TimeSpan? busy);

            await Assert.That(goodAdaptive).IsTrue();
            await Assert.That(busyAdaptive).IsTrue();
            await Assert.That(good.HasValue).IsTrue();
            await Assert.That(busy.HasValue).IsTrue();
            await Assert.That(busy!.Value > good!.Value).IsTrue();
        }

        private sealed class BootstrapClassifier : IResourceIsolationClassifier
        {
            public bool TryClassifyIngress(IPEndPoint remoteEndpoint, out ResourceIsolationIdentity identity)
            {
                identity = new ResourceIsolationIdentity("protected-loopback", ResourceIsolationClass.Bootstrap);
                return remoteEndpoint?.Address.Equals(IPAddress.Loopback) == true;
            }

            public bool TryClassify(
                SecureChannelContext channelContext, SessionBindingContext sessionBinding,
                out ResourceIsolationIdentity identity)
            {
                identity = default;
                return false;
            }
        }
    }
}
