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

using System;
using System.Collections.Generic;
using System.Threading;

namespace Opc.Ua.Redundancy
{
    /// <summary>
    /// Extension beyond OPC 10000-4 §6.6: an in-process coordinator that links one or more
    /// <see cref="InProcessRaftConsensus"/> replicas onto a single shared, totally-ordered committed log. Proposing on
    /// any member broadcasts the command to every member in one global order, so a state machine built on top
    /// (for example <c>RaftSharedKeyValueStore</c>) converges deterministically — the same guarantee a real
    /// Raft cluster provides, without the network.
    /// </summary>
    /// <remarks>
    /// Leadership is deterministic: the live member with the lowest node id is the leader. Disposing the leader
    /// re-elects the next-lowest member and raises <see cref="InProcessRaftConsensus.LeadershipChanged"/>, which makes
    /// failover paths exercisable in a single process and in CI.
    /// </remarks>
    public sealed class InProcessRaftCluster
    {
        /// <summary>
        /// Creates a new consensus replica attached to this cluster.
        /// </summary>
        /// <param name="nodeId">
        /// This replica's unique, non-zero identity; the lowest live id is the leader.
        /// </param>
        /// <returns>The created in-process consensus replica.</returns>
        public InProcessRaftConsensus CreateNode(ulong nodeId)
        {
            return new InProcessRaftConsensus(this, nodeId);
        }

        /// <summary>
        /// Adds a consensus replica to the live membership used for deterministic leadership and broadcasts.
        /// </summary>
        /// <param name="node">The consensus replica to add to the cluster.</param>
        internal void Register(InProcessRaftConsensus node)
        {
            lock (m_lock)
            {
                if (!m_members.Contains(node))
                {
                    m_members.Add(node);
                }
            }
            Reelect();
        }

        /// <summary>
        /// Removes a consensus replica from the live membership and triggers deterministic re-election.
        /// </summary>
        /// <param name="node">The consensus replica to remove from the cluster.</param>
        internal void Unregister(InProcessRaftConsensus node)
        {
            lock (m_lock)
            {
                m_members.Remove(node);
            }
            Reelect();
        }

        /// <summary>
        /// Broadcasts a committed command to every live member in one shared global order.
        /// </summary>
        /// <param name="command">The encoded command to deliver to each consensus replica.</param>
        internal void Propose(ReadOnlyMemory<byte> command)
        {
            // Hold the lock across the whole broadcast so every member observes
            // commands in one identical global order, even under concurrent
            // proposers. Channel writes are non-blocking (unbounded).
            lock (m_lock)
            {
                for (int ii = 0; ii < m_members.Count; ii++)
                {
                    m_members[ii].Deliver(command);
                }
            }
        }

        private void Reelect()
        {
            var transitions = new List<(InProcessRaftConsensus Node, bool IsLeader)>();
            lock (m_lock)
            {
                InProcessRaftConsensus? newLeader = null;
                for (int ii = 0; ii < m_members.Count; ii++)
                {
                    InProcessRaftConsensus candidate = m_members[ii];
                    if (newLeader == null || candidate.NodeId < newLeader.NodeId)
                    {
                        newLeader = candidate;
                    }
                }

                if (!ReferenceEquals(newLeader, m_leader))
                {
                    if (m_leader != null)
                    {
                        transitions.Add((m_leader, false));
                    }
                    m_leader = newLeader;
                    if (newLeader != null)
                    {
                        transitions.Add((newLeader, true));
                    }
                }
            }

            // Raise leadership callbacks outside the lock: never invoke consumer
            // delegates while holding an internal lock.
            foreach ((InProcessRaftConsensus node, bool isLeader) in transitions)
            {
                node.SetLeadership(isLeader);
            }
        }

        private readonly Lock m_lock = new();
        private readonly List<InProcessRaftConsensus> m_members = [];
        private InProcessRaftConsensus? m_leader;
    }
}
