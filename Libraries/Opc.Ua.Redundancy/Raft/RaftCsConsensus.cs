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

// ===========================================================================
// SCAFFOLD (Raft phase 7): adapter that binds the external multi-node Raft
// engine `RaftCs` (https://github.com/marcschier/raft-cs, bundled with the
// Crdt 1.0.5 GC libraries) to the in-repo IRaftConsensus seam.
//
// It is compiled out by default so the repository builds and tests fully
// offline against InProcessRaftConsensus. To ACTIVATE when the package is
// available on nuget.org:
//   1. In Directory.Packages.props add:
//        <PackageVersion Include="RaftCs" Version="..." />
//        <PackageVersion Include="RaftCs.Transport" Version="..." />
//        <PackageVersion Include="RaftCs.Transport.NanoMsg" Version="..." />  (production)
//        <PackageVersion Include="RaftCs.Storage.File" Version="..." />       (durable WAL, optional)
//   2. In Opc.Ua.Redundancy.csproj add the matching <PackageReference> items
//      and define the constant: <DefineConstants>$(DefineConstants);OPCUA_RAFTCS</DefineConstants>.
//   3. Register it from DI, e.g. via RedundancyConsistencyOptions.RaftConsensusFactory:
//        options.RaftConsensusFactory = sp => new RaftCsConsensus(
//            new RaftNode(config, storage, transport));
//   4. Wire NanoMsgBusTransport (RaftCs.Transport.NanoMsg) for the cross-pod
//      RPC so Raft shares the NanoMsg substrate with the CRDT gossip layer.
// ===========================================================================

#if OPCUA_RAFTCS
using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Raft;

namespace Opc.Ua.Redundancy
{
    /// <summary>
    /// Extension beyond OPC 10000-4 §6.6: binds an external <c>RaftCs</c> <see cref="RaftNode"/> replica to the
    /// <see cref="IRaftConsensus"/> seam. Proposals map to <see cref="RaftNode.ProposeAsync"/>, the committed apply
    /// stream is <see cref="RaftNode.Committed"/>, and leadership is observed from <see cref="RaftNode.IsLeader"/>.
    /// </summary>
    public sealed class RaftCsConsensus : IRaftConsensus
    {
        /// <summary>
        /// Creates an adapter over a <c>RaftCs</c> replica.
        /// </summary>
        /// <param name="node">The Raft replica (constructed with its storage and transport).</param>
        /// <param name="ownsNode">When <c>true</c>, the node is disposed with this adapter.</param>
        /// <param name="leadershipPollInterval">
        /// How often the adapter samples <see cref="RaftNode.IsLeader"/> to raise
        /// <see cref="LeadershipChanged"/> (the node exposes state, not an event).
        /// </param>
        public RaftCsConsensus(RaftNode node, bool ownsNode = true, TimeSpan leadershipPollInterval = default)
        {
            m_node = node ?? throw new ArgumentNullException(nameof(node));
            m_ownsNode = ownsNode;
            m_pollInterval = leadershipPollInterval <= TimeSpan.Zero
                ? TimeSpan.FromMilliseconds(50)
                : leadershipPollInterval;
        }

        /// <inheritdoc/>
        public bool IsLeader => m_node.IsLeader;

        /// <inheritdoc/>
        public event Action<bool>? LeadershipChanged;

        /// <inheritdoc/>
        public ChannelReader<ReadOnlyMemory<byte>> Committed => m_node.Committed;

        /// <inheritdoc/>
        public async ValueTask StartAsync(CancellationToken ct = default)
        {
            await m_node.StartAsync(ct).ConfigureAwait(false);
            if (Interlocked.Exchange(ref m_started, 1) == 0)
            {
                m_leadershipLoop = Task.Run(() => WatchLeadershipAsync(m_cts.Token));
            }
        }

        /// <inheritdoc/>
        public ValueTask ProposeAsync(ReadOnlyMemory<byte> command, CancellationToken ct = default)
        {
            // RaftNode.ProposeAsync is accepted only on the leader. A production
            // deployment forwards a follower's proposal to the current leader
            // (RaftNode.LeaderId) over the transport; the strong-consistency
            // store retries on the leader. TODO: add leader-forwarding here.
            return m_node.ProposeAsync(command, ct);
        }

        /// <inheritdoc/>
        public ValueTask CampaignAsync(CancellationToken ct = default)
        {
            return m_node.CampaignAsync(ct);
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref m_disposed, 1) != 0)
            {
                return;
            }

            m_cts.Cancel();
            if (m_leadershipLoop != null)
            {
                try
                {
                    await m_leadershipLoop.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // expected on shutdown
                }
            }

            if (m_ownsNode)
            {
                await m_node.DisposeAsync().ConfigureAwait(false);
            }

            m_cts.Dispose();
        }

        private async Task WatchLeadershipAsync(CancellationToken ct)
        {
            bool last = false;
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    bool now = m_node.IsLeader;
                    if (now != last)
                    {
                        last = now;
                        LeadershipChanged?.Invoke(now);
                    }
                    await Task.Delay(m_pollInterval, ct).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                // shutdown
            }
        }

        private readonly RaftNode m_node;
        private readonly bool m_ownsNode;
        private readonly TimeSpan m_pollInterval;
        private readonly CancellationTokenSource m_cts = new();
        private Task? m_leadershipLoop;
        private int m_started;
        private int m_disposed;
    }
}
#endif
