/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

#if NET8_0_OR_GREATER
using System.Net;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http;
using NUnit.Framework;

namespace Opc.Ua.Bindings.Https.WebApi.Tests
{
    /// <summary>
    /// Tests the default limiter of <see cref="HttpsRateLimiterStartupContributor"/>.
    /// </summary>
    [TestFixture]
    [Category("WebApiStartupContributors")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public sealed class HttpsRateLimiterStartupContributorTests
    {
        /// <summary>
        /// One peer exhausting its window must not lock out other peers: the
        /// default limiter partitions by remote address.
        /// </summary>
        [Test]
        public void DefaultLimiterDoesNotLetOnePeerExhaustAnotherPeersBudget()
        {
            using PartitionedRateLimiter<HttpContext> limiter =
                HttpsRateLimiterStartupContributor.CreateDefaultGlobalLimiter();
            HttpContext flooder = CreateContext(IPAddress.Parse("192.0.2.1"));
            HttpContext victim = CreateContext(IPAddress.Parse("192.0.2.2"));

            int acquired = 0;
            for (int i = 0; i < 1000; i++)
            {
                using RateLimitLease lease = limiter.AttemptAcquire(flooder);
                if (lease.IsAcquired)
                {
                    acquired++;
                }
            }

            using RateLimitLease flooderLease = limiter.AttemptAcquire(flooder);
            using RateLimitLease victimLease = limiter.AttemptAcquire(victim);
            Assert.That(acquired, Is.LessThan(1000), "The flooding peer must be limited.");
            Assert.That(flooderLease.IsAcquired, Is.False);
            Assert.That(victimLease.IsAcquired, Is.True, "Another peer must keep its own budget.");
        }

        /// <summary>
        /// An IPv4 peer seen as an IPv4-mapped IPv6 address shares its partition.
        /// </summary>
        [Test]
        public void PartitionKeyNormalizesIpv4MappedAddresses()
        {
            IPAddress ipv4 = IPAddress.Parse("192.0.2.7");

            Assert.That(
                HttpsRateLimiterStartupContributor.GetPartitionKey(CreateContext(ipv4.MapToIPv6())),
                Is.EqualTo(HttpsRateLimiterStartupContributor.GetPartitionKey(CreateContext(ipv4))));
        }

        private static DefaultHttpContext CreateContext(IPAddress address)
        {
            var context = new DefaultHttpContext();
            context.Connection.RemoteIpAddress = address;
            return context;
        }
    }
}
#endif
