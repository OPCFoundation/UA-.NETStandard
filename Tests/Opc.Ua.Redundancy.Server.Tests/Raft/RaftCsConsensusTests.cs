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

using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Redundancy;
using Opc.Ua.Redundancy.Server;

namespace Opc.Ua.Server.Tests.Redundancy
{
    /// <summary>
    /// Tests that exercise the in-repo Raft seams over a real <see cref="DefaultRaftConsensus"/> (a single-node RaftCs
    /// replica with real election, log replication, and commit), proving the adapter binding to the external engine.
    /// </summary>
    [TestFixture]
    [Category("Distributed")]
    [Parallelizable(ParallelScope.All)]
    public sealed class RaftCsConsensusTests
    {
        [Test]
        public async Task SingleNodeElectsItselfLeaderAsync()
        {
            await using DefaultRaftConsensus consensus = DefaultRaftConsensus.CreateSingleNode();
            await using var election = new RaftLeaderElection(consensus);

            await consensus.StartAsync();

            Assert.That(consensus.IsLeader, Is.True, "a single-voter RaftCs replica elects itself leader");
            Assert.That(election.IsLeader, Is.True);
        }

        [Test]
        public async Task StoreCompareAndSwapIsLinearizableAsync()
        {
            await using var store = new RaftSharedKeyValueStore(
                DefaultRaftConsensus.CreateSingleNode(), ownsConsensus: true);

            bool created = await store.CompareAndSwapAsync("k", default, ByteString.From(new byte[] { 1 }));
            bool createdAgain = await store.CompareAndSwapAsync("k", default, ByteString.From(new byte[] { 2 }));
            (bool found, ByteString value) = await store.TryGetAsync("k");

            Assert.That(created, Is.True);
            Assert.That(createdAgain, Is.False, "the second create-if-absent loses once the key is committed");
            Assert.That(found, Is.True);
            Assert.That(value.ToArray(), Is.EqualTo(new byte[] { 1 }));
        }

        [Test]
        public async Task SetThenGetRoundTripsThroughCommittedLogAsync()
        {
            await using var store = new RaftSharedKeyValueStore(
                DefaultRaftConsensus.CreateSingleNode(), ownsConsensus: true);
            ByteString payload = ByteString.From(new byte[] { 4, 5, 6 });

            await store.SetAsync("session/a", payload);
            (bool found, ByteString value) = await store.TryGetAsync("session/a");

            Assert.That(found, Is.True);
            Assert.That(value.ToArray(), Is.EqualTo(payload.ToArray()));
        }

        [Test]
        public async Task NonceRegistryEnforcesExactlyOnceAsync()
        {
            await using var store = new RaftSharedKeyValueStore(
                DefaultRaftConsensus.CreateSingleNode(), ownsConsensus: true);
            var registry = new SharedSingleUseNonceRegistry(store);
            ByteString nonce = ByteString.From(new byte[] { 1, 2, 3, 4 });

            bool first = await registry.TryConsumeAsync(nonce);
            bool second = await registry.TryConsumeAsync(nonce);

            Assert.That(first, Is.True);
            Assert.That(second, Is.False, "a replayed nonce is rejected by the RaftCs-backed registry");
        }
    }
}
