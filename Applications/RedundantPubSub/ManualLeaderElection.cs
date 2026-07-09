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
using Opc.Ua.Redundancy;

namespace RedundantPubSub
{
    internal sealed class ManualLeaderElection
    {
        public ILeaderElection ForOwner(string ownerId)
        {
            return new Node(this, ownerId);
        }

        public void SetLeader(string ownerId)
        {
            List<Node> nodes;
            lock (m_lock)
            {
                m_leader = ownerId;
                nodes = [.. m_nodes];
            }

            foreach (Node node in nodes)
            {
                node.Notify();
            }
        }

        private bool IsLeader(string ownerId)
        {
            lock (m_lock)
            {
                return string.Equals(m_leader, ownerId, StringComparison.Ordinal);
            }
        }

        private void Add(Node node)
        {
            lock (m_lock)
            {
                m_nodes.Add(node);
            }
        }

        private void Remove(Node node)
        {
            lock (m_lock)
            {
                m_nodes.Remove(node);
            }
        }

        private readonly Lock m_lock = new();
        private readonly List<Node> m_nodes = [];
        private string? m_leader;

        private sealed class Node : ILeaderElection
        {
            public Node(ManualLeaderElection owner, string ownerId)
            {
                m_owner = owner;
                m_ownerId = ownerId;
                m_owner.Add(this);
            }

            public bool IsLeader => m_owner.IsLeader(m_ownerId);

            public event Action<bool>? LeadershipChanged;

            public ValueTask<bool> TryAcquireOrRenewAsync(CancellationToken ct = default)
            {
                return new ValueTask<bool>(IsLeader);
            }

            public void Start()
            {
                Notify();
            }

            public ValueTask DisposeAsync()
            {
                m_owner.Remove(this);
                return default;
            }

            public void Notify()
            {
                LeadershipChanged?.Invoke(IsLeader);
            }

            private readonly ManualLeaderElection m_owner;
            private readonly string m_ownerId;
        }
    }
}
