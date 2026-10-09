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
