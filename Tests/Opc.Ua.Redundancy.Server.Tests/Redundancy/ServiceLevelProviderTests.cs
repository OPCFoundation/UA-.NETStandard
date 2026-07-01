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

// CA2007: tests run without a SynchronizationContext; ConfigureAwait(false)
// adds noise without a behavioural benefit. Disabled file-level for the suite.
#pragma warning disable CA2007

#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Redundancy;
using Opc.Ua.Redundancy.Server;

namespace Opc.Ua.Server.Tests.Redundancy
{
    /// <summary>
    /// Unit tests for the service-level providers.
    /// </summary>
    [TestFixture]
    [Category("Distributed")]
    [Parallelizable(ParallelScope.All)]
    public class ServiceLevelProviderTests
    {
        [Test]
        public void ConstantProviderReturnsConfiguredLevel()
        {
            Assert.That(new ConstantServiceLevelProvider().GetServiceLevel(), Is.EqualTo(ServiceLevels.Maximum));
            Assert.That(new ConstantServiceLevelProvider(ServiceLevels.HealthyMinimum).GetServiceLevel(),
                Is.EqualTo(ServiceLevels.HealthyMinimum));
        }

        [Test]
        public void ConstantProviderAcceptsEventSubscriptions()
        {
            var provider = new ConstantServiceLevelProvider(250);
            static void Handler(byte _)
            {
            }

            provider.ServiceLevelChanged += Handler;
            provider.ServiceLevelChanged -= Handler;

            Assert.That(provider.GetServiceLevel(), Is.EqualTo(250));
        }

        [Test]
        public async Task LeaderProviderReportsLeaderLevelWhenLeaderAsync()
        {
            await using var leaderElection = new StaticLeaderElection(true);
            await using var standbyElection = new StaticLeaderElection(false);
            using var leader = new LeaderServiceLevelProvider(leaderElection);
            using var standby = new LeaderServiceLevelProvider(standbyElection);

            Assert.That(leader.GetServiceLevel(), Is.EqualTo(ServiceLevels.Maximum));
            Assert.That(standby.GetServiceLevel(), Is.EqualTo(ServiceLevels.DegradedMaximum));
        }

        [Test]
        public async Task LeaderProviderMapsStandbySubrangeFromFailoverModeAsync()
        {
            await using var coldElection = new StaticLeaderElection(false);
            await using var warmElection = new StaticLeaderElection(false);
            await using var hotElection = new StaticLeaderElection(false);
            using var cold = new LeaderServiceLevelProvider(coldElection, RedundancySupport.Cold);
            using var warm = new LeaderServiceLevelProvider(warmElection, RedundancySupport.Warm);
            using var hot = new LeaderServiceLevelProvider(hotElection, RedundancySupport.Hot);

            Assert.That(cold.GetServiceLevel(), Is.EqualTo(ServiceLevels.NoData));
            Assert.That(warm.GetServiceLevel(), Is.EqualTo(ServiceLevels.DegradedMaximum));
            Assert.That(hot.GetServiceLevel(), Is.EqualTo(ServiceLevels.Maximum));
        }

        [Test]
        public async Task LeaderProviderDecrementsHealthyLevelForLoadBalancingAsync()
        {
            await using var election = new StaticLeaderElection(true);
            using var provider = new LeaderServiceLevelProvider(
                election,
                RedundancySupport.Hot,
                getConnectedClientCount: () => 3);

            Assert.That(provider.GetServiceLevel(), Is.EqualTo((byte)(ServiceLevels.Maximum - 3)));
        }

        [Test]
        public async Task LeaderProviderAppliesHealthInputAsMaximumLevelAsync()
        {
            await using var election = new StaticLeaderElection(true);
            using var provider = new LeaderServiceLevelProvider(
                election,
                RedundancySupport.Hot,
                getHealthServiceLevel: () => ServiceLevels.DegradedMaximum);

            Assert.That(provider.GetServiceLevel(), Is.EqualTo(ServiceLevels.DegradedMaximum));
        }

        [Test]
        public async Task LeaderProviderRaisesServiceLevelChangedOnTransitionAsync()
        {
            await using var election = new MutableLeaderElection();
            using var provider = new LeaderServiceLevelProvider(election, leaderLevel: 255, standbyLevel: 10);
            byte? observed = null;
            provider.ServiceLevelChanged += level => observed = level;

            election.Set(true);
            Assert.That(observed, Is.EqualTo(ServiceLevels.Maximum));
            Assert.That(provider.GetServiceLevel(), Is.EqualTo(ServiceLevels.Maximum));

            election.Set(false);
            Assert.That(observed, Is.EqualTo((byte)10));
            Assert.That(provider.GetServiceLevel(), Is.EqualTo((byte)10));
        }

        [Test]
        public async Task LeaderProviderManualServiceLevelOverridesCurrentRoleAsync()
        {
            await using var election = new MutableLeaderElection();
            using var provider = new LeaderServiceLevelProvider(election);
            byte? observed = null;
            provider.ServiceLevelChanged += level => observed = level;

            provider.SetServiceLevel(ServiceLevels.Maintenance);

            Assert.That(observed, Is.EqualTo(ServiceLevels.Maintenance));
            Assert.That(provider.GetServiceLevel(), Is.EqualTo(ServiceLevels.Maintenance));
        }

        [Test]
        public async Task LeaderProviderHotAndMirroredStandbyReportsMaximumAsync()
        {
            await using var election = new StaticLeaderElection(false);
            using var provider = new LeaderServiceLevelProvider(
                election,
                RedundancySupport.HotAndMirrored);

            Assert.That(provider.GetServiceLevel(), Is.EqualTo(ServiceLevels.Maximum));
        }

        [Test]
        public async Task LeaderProviderHealthCapsLeaderLevelAsync()
        {
            await using var election = new StaticLeaderElection(true);
            using var provider = new LeaderServiceLevelProvider(
                election,
                RedundancySupport.Hot,
                getHealthServiceLevel: () => 200);

            Assert.That(provider.GetServiceLevel(), Is.EqualTo(200));
        }

        [Test]
        public async Task LeaderProviderLoadDecrementClampsToHealthyMinimumAsync()
        {
            await using var election = new StaticLeaderElection(true);
            using var provider = new LeaderServiceLevelProvider(
                election,
                RedundancySupport.Hot,
                getConnectedClientCount: () => 300);

            byte result = provider.GetServiceLevel();
            Assert.That(result, Is.EqualTo(ServiceLevels.HealthyMinimum));
        }

        [Test]
        public async Task LeaderProviderDoesNotApplyLoadBalancingToDegradedLevelAsync()
        {
            await using var election = new StaticLeaderElection(true);
            using var provider = new LeaderServiceLevelProvider(
                election,
                RedundancySupport.Hot,
                getConnectedClientCount: () => 10,
                getHealthServiceLevel: () => ServiceLevels.DegradedMaximum);

            Assert.That(provider.GetServiceLevel(), Is.EqualTo(ServiceLevels.DegradedMaximum));
        }

        [Test]
        public async Task LeaderProviderManualOverrideClearsOnTransitionAsync()
        {
            await using var election = new MutableLeaderElection();
            using var provider = new LeaderServiceLevelProvider(election, leaderLevel: 255, standbyLevel: 200);
            provider.SetServiceLevel(ServiceLevels.DegradedMaximum);
            Assert.That(provider.GetServiceLevel(), Is.EqualTo(ServiceLevels.DegradedMaximum));

            election.Set(true);

            Assert.That(provider.GetServiceLevel(), Is.EqualTo(255));
        }

        [Test]
        public async Task LeaderProviderConcurrentManualOverrideAndClearDoesNotProduceTornZero()
        {
            await using var election = new MutableLeaderElection();
            election.Set(true);
            using var provider = new LeaderServiceLevelProvider(election, leaderLevel: 255, standbyLevel: 200);
            using var cts = new CancellationTokenSource();
            int invalidLevel = -1;

            var writer = Task.Run(() =>
            {
                while (!cts.IsCancellationRequested)
                {
                    provider.SetServiceLevel(ServiceLevels.DegradedMaximum);
                    election.Set(true);
                }
            });
            var reader = Task.Run(() =>
            {
                for (int ii = 0; ii < 100_000; ii++)
                {
                    byte level = provider.GetServiceLevel();
                    if (level is not ServiceLevels.DegradedMaximum and not ServiceLevels.Maximum)
                    {
                        Volatile.Write(ref invalidLevel, level);
                        cts.Cancel();
                        break;
                    }
                }

                cts.Cancel();
            });

            await Task.WhenAll(writer, reader).ConfigureAwait(false);

            Assert.That(
                Volatile.Read(ref invalidLevel),
                Is.EqualTo(-1),
                "Concurrent SetServiceLevel/leadership clear must not expose torn ServiceLevel values.");
        }

        [Test]
        public void ConstantProviderConstructorThrowsOnNullElection()
        {
            Assert.That(
                () => new LeaderServiceLevelProvider(null!),
                Throws.ArgumentNullException);
        }

        private sealed class MutableLeaderElection : ILeaderElection
        {
            public bool IsLeader => Volatile.Read(ref m_isLeader) != 0;

            public event Action<bool>? LeadershipChanged;

            public void Set(bool isLeader)
            {
                Volatile.Write(ref m_isLeader, isLeader ? 1 : 0);
                LeadershipChanged?.Invoke(isLeader);
            }

            public ValueTask<bool> TryAcquireOrRenewAsync(CancellationToken ct = default)
            {
                return new ValueTask<bool>(IsLeader);
            }

            public void Start()
            {
            }

            public ValueTask DisposeAsync()
            {
                return default;
            }

            private int m_isLeader;
        }
    }
}
