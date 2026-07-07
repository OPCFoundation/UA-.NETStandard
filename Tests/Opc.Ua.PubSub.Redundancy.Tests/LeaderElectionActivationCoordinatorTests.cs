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
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Redundancy;

[assembly: CLSCompliant(false)]

namespace Opc.Ua.PubSub.Redundancy.Tests
{
    /// <summary>
    /// Unit tests for <see cref="LeaderElectionActivationCoordinator"/>.
    /// </summary>
    [TestFixture]
    [Category("PubSub")]
    [Parallelizable(ParallelScope.All)]
    public sealed class LeaderElectionActivationCoordinatorTests
    {
        [Test]
        public async Task GetRoleReportsStandbyBeforeLeadershipAndActiveAfterLeadershipAsync()
        {
            var election = new ControllableLeaderElection(false);
            await using var coordinator = new LeaderElectionActivationCoordinator(election);

            await coordinator.StartAsync().ConfigureAwait(false);
            PubSubComponentRole standby = await coordinator
                .GetRoleAsync("pubsub:writergroup:WriterGroup1")
                .ConfigureAwait(false);

            election.SetLeader(true);
            PubSubComponentRole active = await coordinator
                .GetRoleAsync("pubsub:writergroup:WriterGroup1")
                .ConfigureAwait(false);

            Assert.That(standby, Is.EqualTo(PubSubComponentRole.Standby));
            Assert.That(active, Is.EqualTo(PubSubComponentRole.Active));
        }

        [Test]
        public async Task RoleChangedFiresForEveryKnownComponentWhenLeadershipChangesAsync()
        {
            var election = new ControllableLeaderElection(true);
            await using var coordinator = new LeaderElectionActivationCoordinator(election);
            var changes = new List<(string ComponentId, PubSubComponentRole Role)>();
            coordinator.RoleChanged += (_, e) => changes.Add((e.ComponentId, e.Role));

            await coordinator.StartAsync().ConfigureAwait(false);
            _ = await coordinator.GetRoleAsync("pubsub:writergroup:WriterGroup1").ConfigureAwait(false);
            _ = await coordinator.GetRoleAsync("pubsub:readergroup:ReaderGroup1").ConfigureAwait(false);
            changes.Clear();

            election.SetLeader(false);
            election.SetLeader(true);

            Assert.That(
                changes,
                Is.EqualTo(new[]
                {
                    ("pubsub:writergroup:WriterGroup1", PubSubComponentRole.Standby),
                    ("pubsub:readergroup:ReaderGroup1", PubSubComponentRole.Standby),
                    ("pubsub:writergroup:WriterGroup1", PubSubComponentRole.Active),
                    ("pubsub:readergroup:ReaderGroup1", PubSubComponentRole.Active)
                }));
        }

        [Test]
        public async Task StartAndStopAreIdempotentAsync()
        {
            var election = new ControllableLeaderElection(false);
            await using var coordinator = new LeaderElectionActivationCoordinator(election);
            var changes = new List<PubSubComponentRole>();
            coordinator.RoleChanged += (_, e) => changes.Add(e.Role);

            _ = await coordinator.GetRoleAsync("pubsub:writergroup:WriterGroup1").ConfigureAwait(false);
            await coordinator.StartAsync().ConfigureAwait(false);
            await coordinator.StartAsync().ConfigureAwait(false);
            changes.Clear();

            election.SetLeader(true);
            await coordinator.StopAsync().ConfigureAwait(false);
            await coordinator.StopAsync().ConfigureAwait(false);
            election.SetLeader(false);

            Assert.That(election.StartCount, Is.EqualTo(1));
            Assert.That(changes, Is.EqualTo(new[] { PubSubComponentRole.Active }));
        }

        [Test]
        public async Task DisposeAsyncDoesNotThrowAndStopsRaisingEventsAsync()
        {
            var election = new ControllableLeaderElection(false);
            var coordinator = new LeaderElectionActivationCoordinator(election);
            var changes = new List<PubSubComponentRole>();
            coordinator.RoleChanged += (_, e) => changes.Add(e.Role);

            _ = await coordinator.GetRoleAsync("pubsub:writergroup:WriterGroup1").ConfigureAwait(false);
            await coordinator.StartAsync().ConfigureAwait(false);
            changes.Clear();
            await coordinator.DisposeAsync().ConfigureAwait(false);

            Assert.That(async () => await coordinator.DisposeAsync().ConfigureAwait(false), Throws.Nothing);
            election.SetLeader(true);

            Assert.That(changes, Is.Empty);
        }

        [Test]
        public async Task StaticLeaderElectionReportsInitialLeadershipOnStartAsync()
        {
            await using var coordinator = new LeaderElectionActivationCoordinator(new StaticLeaderElection(true));
            var changes = new List<PubSubComponentRole>();
            coordinator.RoleChanged += (_, e) => changes.Add(e.Role);

            _ = await coordinator.GetRoleAsync("pubsub:writergroup:WriterGroup1").ConfigureAwait(false);
            await coordinator.StartAsync().ConfigureAwait(false);
            PubSubComponentRole role = await coordinator
                .GetRoleAsync("pubsub:writergroup:WriterGroup1")
                .ConfigureAwait(false);

            Assert.That(role, Is.EqualTo(PubSubComponentRole.Active));
            Assert.That(changes, Is.EqualTo(new[] { PubSubComponentRole.Active }));
        }

        private sealed class ControllableLeaderElection : ILeaderElection
        {
            public ControllableLeaderElection(bool isLeader)
            {
                IsLeader = isLeader;
            }

            public bool IsLeader { get; private set; }

            public int StartCount { get; private set; }

            public event Action<bool> LeadershipChanged;

            public ValueTask<bool> TryAcquireOrRenewAsync(CancellationToken ct = default)
            {
                return new ValueTask<bool>(IsLeader);
            }

            public void Start()
            {
                StartCount++;
                LeadershipChanged?.Invoke(IsLeader);
            }

            public ValueTask DisposeAsync()
            {
                return default;
            }

            public void SetLeader(bool isLeader)
            {
                IsLeader = isLeader;
                LeadershipChanged?.Invoke(isLeader);
            }
        }
    }
}
