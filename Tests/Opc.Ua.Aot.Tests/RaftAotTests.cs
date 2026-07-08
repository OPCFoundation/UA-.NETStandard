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

using Opc.Ua.Redundancy;

// CA2007: AOT tests run without a SynchronizationContext.
#pragma warning disable CA2007

namespace Opc.Ua.Aot.Tests
{
    /// <summary>
    /// AOT integration tests that exercise the Raft (strong-consistency) building blocks over a real
    /// <see cref="DefaultRaftConsensus"/> replica, ensuring the external RaftCs engine is reachable and functional under
    /// NativeAOT.
    /// </summary>
    public class RaftAotTests
    {
        [Test]
        public async Task RaftCsStoreCompareAndSwapUnderAotAsync()
        {
            await using DefaultRaftConsensus consensus = DefaultRaftConsensus.CreateSingleNode();
            await using var store = new RaftSharedKeyValueStore(consensus, ownsConsensus: false);

            var value = new ByteString(new byte[] { 7, 8, 9 });
            bool created = await store.CompareAndSwapAsync("raft/aot", default, value).ConfigureAwait(false);
            (bool found, ByteString stored) = await store.TryGetAsync("raft/aot").ConfigureAwait(false);

            await Assert.That(created).IsTrue();
            await Assert.That(found).IsTrue();
            await Assert.That(stored.ToArray().Length).IsEqualTo(3);
        }

        [Test]
        public async Task RaftLeaderElectionUnderAotAsync()
        {
            await using DefaultRaftConsensus consensus = DefaultRaftConsensus.CreateSingleNode();
            await using var election = new RaftLeaderElection(consensus);

            await consensus.StartAsync().ConfigureAwait(false);

            await Assert.That(consensus.IsLeader).IsTrue();
            await Assert.That(election.IsLeader).IsTrue();
        }
    }
}
