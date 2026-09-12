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
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Time.Testing;
using Moq;
using NUnit.Framework;
using Opc.Ua.Redundancy;
using Opc.Ua.Redundancy.Server;

namespace Opc.Ua.Server.Tests.Redundancy
{
    [TestFixture]
    [Category("Distributed")]
    [Parallelizable(ParallelScope.All)]
    public sealed class SharedStoreLeaseServiceLevelRegressionTests
    {
        [TestCase(RedundancySupport.Cold, ServiceLevels.NoData)]
        [TestCase(RedundancySupport.Warm, ServiceLevels.DegradedMaximum)]
        public async Task BlockedRenewalDemotesServiceLevelAtConfirmedExpiryAsync(
            RedundancySupport mode,
            byte standbyLevel)
        {
            var time = new FakeTimeProvider();
            using var backend = new InMemorySharedKeyValueStore();
            var store = new Mock<ISharedKeyValueStore>(MockBehavior.Strict);
            store
                .Setup(s => s.TryGetAsync(kLeaseKey, It.IsAny<CancellationToken>()))
                .Returns((string key, CancellationToken ct) => backend.TryGetAsync(key, ct));
            store
                .Setup(s => s.CompareAndSwapAsync(
                    kLeaseKey, It.IsAny<ByteString>(), It.IsAny<ByteString>(), It.IsAny<CancellationToken>()))
                .Returns((string key, ByteString expected, ByteString value, CancellationToken ct) =>
                    backend.CompareAndSwapAsync(key, expected, value, ct));
            store
                .Setup(s => s.DeleteAsync(kLeaseKey, It.IsAny<CancellationToken>()))
                .Returns((string key, CancellationToken ct) => backend.DeleteAsync(key, ct));

            await using var election = new SharedStoreLeaseElection(
                store.Object, kLeaseKey, "A", s_leaseDuration, s_renewInterval, time);
            await using var standby = new SharedStoreLeaseElection(
                backend, kLeaseKey, "B", s_leaseDuration, s_renewInterval, time);
            using var serviceLevel = new LeaderServiceLevelProvider(election, mode);
            using var standbyServiceLevel = new LeaderServiceLevelProvider(standby, mode);
            var changes = new ConcurrentQueue<byte>();
            serviceLevel.ServiceLevelChanged += changes.Enqueue;
            Assert.That(await election.TryAcquireOrRenewAsync().ConfigureAwait(false), Is.True);
            Assert.That(serviceLevel.GetServiceLevel(), Is.EqualTo(ServiceLevels.Maximum));
            time.Advance(s_renewInterval);

            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var complete = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            store
                .Setup(s => s.TryGetAsync(kLeaseKey, It.IsAny<CancellationToken>()))
                .Returns(async (string key, CancellationToken ct) =>
                {
                    (bool found, ByteString value) = await backend.TryGetAsync(key, ct).ConfigureAwait(false);
                    entered.TrySetResult(true);
                    await complete.Task.ConfigureAwait(false);
                    return (found, value);
                });
            election.Start();
            try
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                time.Advance(s_leaseDuration - s_renewInterval - TimeSpan.FromTicks(1));
                Assert.That(serviceLevel.GetServiceLevel(), Is.EqualTo(ServiceLevels.Maximum));
                Assert.That(standbyServiceLevel.GetServiceLevel(), Is.EqualTo(standbyLevel));
                byte[] beforeExpiry = [ServiceLevels.Maximum];
                Assert.That(changes, Is.EqualTo(beforeExpiry));

                time.Advance(TimeSpan.FromTicks(1));
                byte[] atExpiry = [ServiceLevels.Maximum, standbyLevel];
                Assert.That(changes, Is.EqualTo(atExpiry), "Published service level must be demoted without polling.");
                Assert.That(serviceLevel.GetServiceLevel(), Is.EqualTo(standbyLevel));
                Assert.That(await standby.TryAcquireOrRenewAsync().ConfigureAwait(false), Is.True);
                Assert.That(standbyServiceLevel.GetServiceLevel(), Is.EqualTo(ServiceLevels.Maximum));
                Assert.That(election.IsLeader, Is.False);

                time.Advance(TimeSpan.FromTicks(1));
                Assert.That(serviceLevel.GetServiceLevel(), Is.EqualTo(standbyLevel));
                Assert.That(changes, Is.EqualTo(atExpiry));
            }
            finally
            {
                store
                    .Setup(s => s.TryGetAsync(kLeaseKey, It.IsAny<CancellationToken>()))
                    .Returns((string key, CancellationToken ct) => backend.TryGetAsync(key, ct));
                complete.TrySetResult(true);
            }
        }

        private const string kLeaseKey = "lease/service-level-expiry";
        private static readonly TimeSpan s_leaseDuration = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan s_renewInterval = TimeSpan.FromSeconds(10);
    }
}
