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
                out IDisposable? lease1,
                out _);
            await Assert.That(first).IsTrue();

            bool second = provider.TryAcquireSessionEstablishment(
                out IDisposable? lease2,
                out _);
            await Assert.That(second).IsFalse();
            await Assert.That(lease2).IsNull();

            lease1?.Dispose();

            bool third = provider.TryAcquireSessionEstablishment(
                out IDisposable? lease3,
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

            TimeSpan? good = policy.GetNextDelay(0, StatusCodes.Good, serverRetryAfter: null);
            TimeSpan? busy = policy.GetNextDelay(
                0,
                StatusCodes.BadServerTooBusy,
                serverRetryAfter: null);

            await Assert.That(good.HasValue).IsTrue();
            await Assert.That(busy.HasValue).IsTrue();
            await Assert.That(busy!.Value > good!.Value).IsTrue();
        }
    }
}
