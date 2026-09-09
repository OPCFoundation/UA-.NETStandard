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
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Server;

namespace Opc.Ua.Redundancy.Server
{
    /// <summary>
    /// Routes one replica's shared graph through its disjoint node-manager ownership partitions.
    /// </summary>
    internal sealed class ReplicaLocalAddressSpace : ILocalAddressSpace, IDisposable
    {
        internal ReplicaLocalAddressSpace(ISystemContext context, List<AddressSpaceRegistration> registrations)
        {
            Context = context;
            m_registrations = registrations;
            foreach (AddressSpaceRegistration registration in m_registrations)
            {
                foreach (NodeState node in registration.AddressSpace.Nodes)
                {
                    _ = FindOwner(node.NodeId);
                }
            }
            foreach (AddressSpaceRegistration registration in m_registrations)
            {
                registration.AddressSpace.NodeAdded += OnNodeAdded;
                registration.AddressSpace.NodeRemoved += OnNodeRemoved;
            }
        }

        public ISystemContext Context { get; }

        public IEnumerable<NodeState> Nodes
        {
            get
            {
                foreach (AddressSpaceRegistration registration in m_registrations)
                {
                    foreach (NodeState node in registration.AddressSpace.Nodes)
                    {
                        if (registration.OwnsNode(node.NodeId))
                        {
                            yield return node;
                        }
                    }
                }
            }
        }

        public event Action<NodeState>? NodeAdded;

        public event Action<NodeId>? NodeRemoved;

        public bool TryGetNode(NodeId nodeId, [NotNullWhen(true)] out NodeState? node)
        {
            ILocalAddressSpace? owner = FindOwner(nodeId);
            if (owner != null)
            {
                return owner.TryGetNode(nodeId, out node);
            }
            node = null;
            return false;
        }

        public ValueTask AddOrUpdateNodeAsync(NodeState node, CancellationToken cancellationToken = default)
        {
            return GetOwner(node.NodeId).AddOrUpdateNodeAsync(node, cancellationToken);
        }

        public async ValueTask AddOrUpdateRangeAsync(
            IEnumerable<NodeState> nodes,
            CancellationToken cancellationToken = default)
        {
            var groups = new Dictionary<ILocalAddressSpace, List<NodeState>>();
            foreach (NodeState node in nodes)
            {
                ILocalAddressSpace owner = GetOwner(node.NodeId);
                if (!groups.TryGetValue(owner, out List<NodeState>? group))
                {
                    groups.Add(owner, group = []);
                }
                group.Add(node);
            }
            foreach (KeyValuePair<ILocalAddressSpace, List<NodeState>> group in groups)
            {
                await group.Key.AddOrUpdateRangeAsync(group.Value, cancellationToken).ConfigureAwait(false);
            }
        }

        public ValueTask<bool> RemoveNodeAsync(NodeId nodeId, CancellationToken cancellationToken = default)
        {
            return GetOwner(nodeId).RemoveNodeAsync(nodeId, cancellationToken);
        }

        public void Dispose()
        {
            foreach (AddressSpaceRegistration registration in m_registrations)
            {
                registration.AddressSpace.NodeAdded -= OnNodeAdded;
                registration.AddressSpace.NodeRemoved -= OnNodeRemoved;
            }
        }

        internal bool OwnsNode(NodeId nodeId)
        {
            return FindOwner(nodeId) != null;
        }

        private ILocalAddressSpace GetOwner(NodeId nodeId)
        {
            return FindOwner(nodeId) ??
                throw new ServiceResultException(StatusCodes.BadNodeIdUnknown,
                    $"No local shared partition owns replicated node '{nodeId}'.");
        }

        private ILocalAddressSpace? FindOwner(NodeId nodeId)
        {
            ILocalAddressSpace? owner = null;
            foreach (AddressSpaceRegistration registration in m_registrations)
            {
                if (registration.OwnsNode(nodeId))
                {
                    if (owner != null)
                    {
                        throw new ServiceResultException(StatusCodes.BadConfigurationError,
                            $"Multiple shared partitions claim replicated node '{nodeId}'.");
                    }
                    owner = registration.AddressSpace;
                }
            }
            return owner;
        }

        private void OnNodeAdded(NodeState node)
        {
            if (OwnsNode(node.NodeId))
            {
                NodeAdded?.Invoke(node);
            }
        }

        private void OnNodeRemoved(NodeId nodeId)
        {
            if (OwnsNode(nodeId))
            {
                NodeRemoved?.Invoke(nodeId);
            }
        }

        private readonly List<AddressSpaceRegistration> m_registrations;
    }
}
