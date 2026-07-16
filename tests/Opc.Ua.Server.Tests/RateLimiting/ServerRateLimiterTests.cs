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
using System.Collections.Generic;
using System.Net;
using NUnit.Framework;

#nullable enable

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// Tests for the server admission-control rate limiters.
    /// </summary>
    [TestFixture]
    [Category("RateLimiting")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class ServerRateLimiterTests
    {
        /// <summary>
        /// Verifies that the token-bucket connection limiter admits up to the burst
        /// capacity and then rejects further connections.
        /// </summary>
        [Test]
        public void ConnectionRateLimiterAdmitsUpToBurstThenRejects()
        {
            using var limiter = new TokenBucketConnectionRateLimiter(
                connectionsPerSecond: 1,
                burst: 5);

            var remote = new IPEndPoint(IPAddress.Loopback, 4840);

            int admitted = 0;
            for (int i = 0; i < 5; i++)
            {
                if (limiter.TryAdmitConnection(remote, out _))
                {
                    admitted++;
                }
            }

            Assert.That(admitted, Is.EqualTo(5), "the full burst should be admitted");

            bool sixth = limiter.TryAdmitConnection(remote, out TimeSpan? retryAfter);
            Assert.That(sixth, Is.False, "the connection beyond the burst must be rejected");
            Assert.That(retryAfter, Is.Not.Null, "a rejected connection should carry a retry-after hint");
        }

        /// <summary>
        /// Verifies that a null remote endpoint is handled without throwing.
        /// </summary>
        [Test]
        public void ConnectionRateLimiterHandlesNullRemoteEndPoint()
        {
            using var limiter = new TokenBucketConnectionRateLimiter(1, 2);

            Assert.That(limiter.TryAdmitConnection(null, out _), Is.True);
        }

        /// <summary>
        /// Verifies that the connection limiter rejects invalid limits.
        /// </summary>
        [Test]
        public void ConnectionRateLimiterRejectsInvalidLimits()
        {
            Assert.That(
                () => new TokenBucketConnectionRateLimiter(0, 1),
                Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(
                () => new TokenBucketConnectionRateLimiter(1, 0),
                Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        /// <summary>
        /// Verifies that when rate limiting is disabled the provider applies no
        /// limits: no connection limiter and session establishment always proceeds.
        /// </summary>
        [Test]
        public void DisabledProviderAppliesNoLimits()
        {
            var options = new ServerRateLimitOptions { Enabled = false };
            using var provider = new DefaultServerRateLimiterProvider(options);

            Assert.That(provider.ConnectionRateLimiter, Is.Null);
            Assert.That(
                provider.TryAcquireSessionEstablishment(out IDisposable? lease, out _),
                Is.True);
            Assert.That(lease, Is.Null);
            // The backlog default is still applied even when limiting is disabled.
            Assert.That(provider.ListenBacklog, Is.EqualTo(ServerRateLimitOptions.DefaultListenBacklog));
        }

        /// <summary>
        /// Verifies that the default (enabled) provider exposes a connection limiter
        /// and the configured listener backlog.
        /// </summary>
        [Test]
        public void EnabledProviderExposesConnectionLimiterAndBacklog()
        {
            var options = new ServerRateLimitOptions { ListenBacklog = 777 };
            using var provider = new DefaultServerRateLimiterProvider(options);

            Assert.That(provider.ConnectionRateLimiter, Is.Not.Null);
            Assert.That(provider.ListenBacklog, Is.EqualTo(777));
        }

        /// <summary>
        /// Verifies that the session-establishment concurrency limiter admits up to
        /// the permit limit and rejects the next operation while permits are held,
        /// then admits again after a lease is released.
        /// </summary>
        [Test]
        public void SessionEstablishmentLimiterBoundsConcurrency()
        {
            var options = new ServerRateLimitOptions
            {
                MaxConcurrentSessionEstablishment = 2,
                SessionEstablishmentQueueLimit = 0
            };
            using var provider = new DefaultServerRateLimiterProvider(options);

            var leases = new List<IDisposable?>();

            Assert.That(
                provider.TryAcquireSessionEstablishment(out IDisposable? first, out _),
                Is.True);
            leases.Add(first);

            Assert.That(
                provider.TryAcquireSessionEstablishment(out IDisposable? second, out _),
                Is.True);
            leases.Add(second);

            // Both permits are held: the third establishment must be rejected.
            Assert.That(
                provider.TryAcquireSessionEstablishment(out IDisposable? third, out _),
                Is.False);
            Assert.That(third, Is.Null);

            // Release one permit; a new establishment can now proceed.
            first!.Dispose();
            Assert.That(
                provider.TryAcquireSessionEstablishment(out IDisposable? fourth, out _),
                Is.True);
            leases.Add(fourth);

            foreach (IDisposable? lease in leases)
            {
                lease?.Dispose();
            }
        }

        /// <summary>
        /// Verifies that disposing the provider is idempotent.
        /// </summary>
        [Test]
        public void ProviderDisposeIsIdempotent()
        {
            var provider = new DefaultServerRateLimiterProvider(new ServerRateLimitOptions());
            provider.Dispose();
            Assert.That(provider.Dispose, Throws.Nothing);
        }

        /// <summary>
        /// Verifies that a null options argument is rejected.
        /// </summary>
        [Test]
        public void ProviderRejectsNullOptions()
        {
            Assert.That(
                () => new DefaultServerRateLimiterProvider(null!),
                Throws.TypeOf<ArgumentNullException>());
        }
    }
}
