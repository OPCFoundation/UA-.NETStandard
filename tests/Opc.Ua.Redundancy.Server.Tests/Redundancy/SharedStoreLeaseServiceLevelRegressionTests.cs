// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Time.Testing;
using Moq;
using NUnit.Framework;
using Opc.Ua.Redundancy;
using Opc.Ua.Redundancy.Server;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests.Redundancy
{
    /// <summary>
    /// Verifies published service levels follow confirmed lease expiry even when renewal storage is blocked.
    /// </summary>
    [TestFixture]
    [Category("Distributed")]
    [Parallelizable(ParallelScope.All)]
    public sealed class SharedStoreLeaseServiceLevelRegressionTests
    {
        /// <summary>
        /// Verifies demotion at the exact deadline and standby takeover without waiting for a blocked renewal.
        /// </summary>
        [TestCase(RedundancySupport.Cold, ServiceLevels.NoData)]
        [TestCase(RedundancySupport.Warm, ServiceLevels.DegradedMaximum)]
        public async Task BlockedRenewalDemotesServiceLevelAtConfirmedExpiryAsync(
            RedundancySupport mode,
            byte standbyLevel)
        {
            var time = new FakeTimeProvider();
            using var payload = new InMemorySharedKeyValueStore();
            using var backend = new SimulatedSharedKeyValueStore();
            using var random = RandomNumberGenerator.Create();
            var keyBytes = new byte[32];
            random.GetBytes(keyBytes);
            using var protector = new AesCbcHmacRecordProtector(keyBytes);
            var store = new Mock<ISharedKeyValueStore>(MockBehavior.Strict);
            store.As<ISharedKeyValueStoreConsistency>()
                .Setup(s => s.IsLinearizable(It.IsAny<string>())).Returns(true);
            store.As<ISharedKeyValueStoreConsistency>()
                .Setup(s => s.IsProcessLocal(It.IsAny<string>())).Returns(false);
            store
                .Setup(s => s.TryGetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns((string key, CancellationToken ct) => backend.TryGetAsync(key, ct));
            store
                .Setup(s => s.CompareAndSwapAsync(
                    It.IsAny<string>(), It.IsAny<ByteString>(), It.IsAny<ByteString>(), It.IsAny<CancellationToken>()))
                .Returns((string key, ByteString expected, ByteString value, CancellationToken ct) =>
                    backend.CompareAndSwapAsync(key, expected, value, ct));
            Guid deployment = Guid.NewGuid();
            Guid epoch = Guid.NewGuid();
            var primaryStore = new FreshnessProtectedKeyValueStore(payload, store.Object, protector, deployment, epoch);
            var standbyStore = new FreshnessProtectedKeyValueStore(payload, backend, protector, deployment, epoch);
            await primaryStore.EnrollAsync().ConfigureAwait(false);

            await using var election = new SharedStoreLeaseElection(
                primaryStore, kLeaseKey, "A", ["A", "B"], s_leaseDuration, s_renewInterval, time);
            await using var standby = new SharedStoreLeaseElection(
                standbyStore, kLeaseKey, "B", ["A", "B"], s_leaseDuration, s_renewInterval, time);
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
                .Setup(s => s.TryGetAsync(It.Is<string>(key => key.EndsWith(kLeaseKey, StringComparison.Ordinal)),
                    It.IsAny<CancellationToken>()))
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
                    .Setup(s => s.TryGetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                    .Returns((string key, CancellationToken ct) => backend.TryGetAsync(key, ct));
                complete.TrySetResult(true);
            }
        }

        /// <summary>
        /// Identifies the lease record shared by the active and standby participants.
        /// </summary>
        private const string kLeaseKey = "lease/service-level-expiry";

        /// <summary>
        /// Defines the confirmed leadership lifetime used by deadline assertions.
        /// </summary>
        private static readonly TimeSpan s_leaseDuration = TimeSpan.FromSeconds(30);

        /// <summary>
        /// Starts renewal before expiry so its storage read can be held across the deadline.
        /// </summary>
        private static readonly TimeSpan s_renewInterval = TimeSpan.FromSeconds(10);
    }
}
