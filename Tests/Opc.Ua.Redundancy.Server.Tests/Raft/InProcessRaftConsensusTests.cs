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

// IDE0230: byte-array literals below are opaque binary test vectors, not text; a
// UTF-8 "..."u8 literal would misrepresent their intent, so keep the explicit byte arrays.
#pragma warning disable IDE0230 // Use UTF-8 string literal

// CA2007: tests run without a SynchronizationContext; ConfigureAwait(false)
// adds noise without a behavioural benefit. Disabled file-level for the suite.
#pragma warning disable CA2007

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Redundancy;

namespace Opc.Ua.Server.Tests.Redundancy
{
    /// <summary>
    /// Unit tests for <see cref="InProcessRaftConsensus"/> and <see cref="InProcessRaftCluster"/>.
    /// </summary>
    [TestFixture]
    [Category("Distributed")]
    [Parallelizable(ParallelScope.All)]
    public class InProcessRaftConsensusTests
    {
        [Test]
        public async Task SingleNodeBecomesLeaderOnStartAsync()
        {
            await using var node = new InProcessRaftConsensus();
            var transitions = new List<bool>();
            node.LeadershipChanged += transitions.Add;

            Assert.That(node.IsLeader, Is.False, "a node is not the leader before it starts");

            await node.StartAsync().ConfigureAwait(false);

            Assert.That(node.IsLeader, Is.True);
            Assert.That(transitions, Is.EqualTo([true]));
        }

        [Test]
        public async Task ProposeDeliversToCommittedInLogOrderAsync()
        {
            await using var node = new InProcessRaftConsensus();
            await node.StartAsync().ConfigureAwait(false);

            await node.ProposeAsync(new byte[] { 1 }).ConfigureAwait(false);
            await node.ProposeAsync(new byte[] { 2 }).ConfigureAwait(false);
            await node.ProposeAsync(new byte[] { 3 }).ConfigureAwait(false);

            IReadOnlyList<byte[]> committed = await ReadCommittedAsync(node, 3).ConfigureAwait(false);

            Assert.That(committed[0], Is.EqualTo(new byte[] { 1 }));
            Assert.That(committed[1], Is.EqualTo(new byte[] { 2 }));
            Assert.That(committed[2], Is.EqualTo(new byte[] { 3 }));
        }

        [Test]
        public async Task ClusterLowestIdIsLeaderAsync()
        {
            var cluster = new InProcessRaftCluster();
            await using InProcessRaftConsensus node1 = cluster.CreateNode(1);
            await using InProcessRaftConsensus node2 = cluster.CreateNode(2);

            await node1.StartAsync().ConfigureAwait(false);
            await node2.StartAsync().ConfigureAwait(false);

            Assert.That(node1.IsLeader, Is.True, "the lowest live id is the leader");
            Assert.That(node2.IsLeader, Is.False);
        }

        [Test]
        public async Task ClusterProposeBroadcastsToAllMembersInSameOrderAsync()
        {
            var cluster = new InProcessRaftCluster();
            await using InProcessRaftConsensus node1 = cluster.CreateNode(1);
            await using InProcessRaftConsensus node2 = cluster.CreateNode(2);
            await node1.StartAsync().ConfigureAwait(false);
            await node2.StartAsync().ConfigureAwait(false);

            // Proposing on either member replicates the command to every member
            // in one identical global order.
            await node1.ProposeAsync(new byte[] { 0xA }).ConfigureAwait(false);
            await node2.ProposeAsync(new byte[] { 0xB }).ConfigureAwait(false);

            IReadOnlyList<byte[]> seenBy1 = await ReadCommittedAsync(node1, 2).ConfigureAwait(false);
            IReadOnlyList<byte[]> seenBy2 = await ReadCommittedAsync(node2, 2).ConfigureAwait(false);

            Assert.That(seenBy1[0], Is.EqualTo(new byte[] { 0xA }));
            Assert.That(seenBy1[1], Is.EqualTo(new byte[] { 0xB }));
            Assert.That(seenBy2[0], Is.EqualTo(new byte[] { 0xA }));
            Assert.That(seenBy2[1], Is.EqualTo(new byte[] { 0xB }));
        }

        [Test]
        public async Task DisposingLeaderReelectsNextLowestAsync()
        {
            var cluster = new InProcessRaftCluster();
            InProcessRaftConsensus node1 = cluster.CreateNode(1);
            await using InProcessRaftConsensus node2 = cluster.CreateNode(2);

            var node2Transitions = new List<bool>();
            node2.LeadershipChanged += node2Transitions.Add;

            await node1.StartAsync().ConfigureAwait(false);
            await node2.StartAsync().ConfigureAwait(false);
            Assert.That(node2.IsLeader, Is.False);

            await node1.DisposeAsync().ConfigureAwait(false);

            Assert.That(node2.IsLeader, Is.True, "the next-lowest live id takes over when the leader leaves");
            Assert.That(node2Transitions, Is.EqualTo([true]));
        }

        [Test]
        public async Task ProposeAfterDisposeThrowsAsync()
        {
            var node = new InProcessRaftConsensus();
            await node.StartAsync().ConfigureAwait(false);
            await node.DisposeAsync().ConfigureAwait(false);

            Assert.That(
                async () => await node.ProposeAsync(new byte[] { 1 }).ConfigureAwait(false),
                Throws.TypeOf<ObjectDisposedException>());
        }

        private static async Task<IReadOnlyList<byte[]>> ReadCommittedAsync(
            InProcessRaftConsensus node,
            int count)
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var result = new List<byte[]>(count);
            for (int ii = 0; ii < count; ii++)
            {
                ReadOnlyMemory<byte> command = await node.Committed.ReadAsync(cts.Token).ConfigureAwait(false);
                result.Add(command.ToArray());
            }
            return result;
        }
    }
}
