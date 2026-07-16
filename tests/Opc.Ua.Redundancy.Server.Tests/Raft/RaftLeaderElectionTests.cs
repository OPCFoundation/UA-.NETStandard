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

using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Redundancy;

namespace Opc.Ua.Server.Tests.Redundancy
{
    /// <summary>
    /// Unit tests for <see cref="RaftLeaderElection"/>: leadership backed by native Raft consensus.
    /// </summary>
    [TestFixture]
    [Category("Distributed")]
    [Parallelizable(ParallelScope.All)]
    public class RaftLeaderElectionTests
    {
        [Test]
        public async Task SingleNodeElectionBecomesLeaderAsync()
        {
            await using var consensus = new InProcessRaftConsensus();
            await using var election = new RaftLeaderElection(consensus);
            var transitions = new List<bool>();
            election.LeadershipChanged += transitions.Add;

            Assert.That(election.IsLeader, Is.False);

            election.Start();
            bool acquired = await election.TryAcquireOrRenewAsync().ConfigureAwait(false);

            Assert.That(acquired, Is.True);
            Assert.That(election.IsLeader, Is.True);
            Assert.That(transitions, Is.EqualTo([true]));
        }

        [Test]
        public async Task FollowerIsNotLeaderAsync()
        {
            var cluster = new InProcessRaftCluster();
            await using InProcessRaftConsensus consensus1 = cluster.CreateNode(1);
            await using InProcessRaftConsensus consensus2 = cluster.CreateNode(2);
            await using var election1 = new RaftLeaderElection(consensus1);
            await using var election2 = new RaftLeaderElection(consensus2);

            await consensus1.StartAsync().ConfigureAwait(false);
            await consensus2.StartAsync().ConfigureAwait(false);

            Assert.That(election1.IsLeader, Is.True);
            Assert.That(election2.IsLeader, Is.False);
        }

        [Test]
        public async Task ElectionFollowsConsensusFailoverAsync()
        {
            var cluster = new InProcessRaftCluster();
            InProcessRaftConsensus consensus1 = cluster.CreateNode(1);
            await using InProcessRaftConsensus consensus2 = cluster.CreateNode(2);
            await using var election2 = new RaftLeaderElection(consensus2);

            var node2Transitions = new List<bool>();
            election2.LeadershipChanged += node2Transitions.Add;

            await consensus1.StartAsync().ConfigureAwait(false);
            await consensus2.StartAsync().ConfigureAwait(false);
            Assert.That(election2.IsLeader, Is.False);

            // The leader leaves; election2 must observe the takeover.
            await consensus1.DisposeAsync().ConfigureAwait(false);

            Assert.That(election2.IsLeader, Is.True, "the surviving replica is elected leader");
            Assert.That(node2Transitions, Is.EqualTo([true]));
        }
    }
}
