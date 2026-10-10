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
using Opc.Ua.Server;

namespace Opc.Ua.Redundancy.Server
{
    /// <summary>
    /// Validates the same shared ownership contract for both replication modes.
    /// </summary>
    internal sealed record AddressSpaceRegistration(
        ILocalAddressSpace AddressSpace,
        Func<NodeId, bool> OwnsNode,
        string PartitionId)
    {
        internal static List<AddressSpaceRegistration> Create(
            IServerContext server,
            ReplicaNodeIdFactory? identity,
            IEnumerable<ILocalAddressSpaceSource>? sources = null)
        {
            var registrations = new List<AddressSpaceRegistration>();
            var namespaceClaims = new Dictionary<ushort, bool>();
            var partitionIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (ILocalAddressSpaceSource source in sources ?? server.FindNodeManagers<ILocalAddressSpaceSource>())
            {
                if (source is ICoreNodeManager or IDiagnosticsNodeManager)
                {
                    continue;
                }
                ILocalAddressSpace addressSpace = source.CreateLocalAddressSpace();
                if (identity != null)
                {
                    foreach (NodeState root in addressSpace.Nodes)
                    {
                        if (root.NodeId.NamespaceIndex > 1)
                        {
                            identity.ValidateRegistration(addressSpace.Context, root);
                        }
                    }
                }
                IEnumerable<string>? namespaceUris = source switch
                {
                    IAsyncNodeManager asyncNodeManager => asyncNodeManager.NamespaceUris,
                    INodeManager nodeManager => nodeManager.NamespaceUris,
                    _ => null
                };
                List<string>? sortedNamespaceUris = namespaceUris == null ? null : [.. namespaceUris];
                sortedNamespaceUris?.Sort(StringComparer.Ordinal);
                var namespaceIndexes = new HashSet<ushort>();
                if (sortedNamespaceUris != null)
                {
                    foreach (string namespaceUri in sortedNamespaceUris)
                    {
                        int index = server.MessageContext.NamespaceUris.GetIndex(namespaceUri);
                        if (index is <= 0 or > ushort.MaxValue || (identity != null && index == 1))
                        {
                            continue;
                        }
                        if (identity != null && !identity.IsShared(new NodeId(1, (ushort)index)))
                        {
                            throw new ServiceResultException(StatusCodes.BadConfigurationError,
                                $"Shared namespace '{namespaceUri}' is not in the replica identity layout.");
                        }
                        namespaceIndexes.Add((ushort)index);
                    }
                }

                bool hasExplicitOwnership = source is ILocalAddressSpaceOwnership;
                foreach (ushort index in namespaceIndexes)
                {
                    if (namespaceClaims.TryGetValue(index, out bool existingExplicit) &&
                        (!hasExplicitOwnership || !existingExplicit))
                    {
                        throw new InvalidOperationException(
                            $"Namespace index {index} is claimed by multiple distributed node managers. " +
                            $"Every manager sharing a namespace must implement {nameof(ILocalAddressSpaceOwnership)}.");
                    }
                    namespaceClaims[index] = hasExplicitOwnership;
                }

                Func<NodeId, bool> ownsNode;
                string partitionId;
                if (source is ILocalAddressSpaceOwnership ownership)
                {
                    if (string.IsNullOrWhiteSpace(ownership.PartitionId))
                    {
                        throw new InvalidOperationException(
                            $"{source.GetType().FullName} returned an empty distributed address-space partition id.");
                    }
                    foreach (NodeState root in addressSpace.Nodes)
                    {
                        ValidateExplicitOwnership(addressSpace.Context, root, ownership);
                    }
                    partitionId = ownership.PartitionId;
                    ownsNode = nodeId => nodeId.NamespaceIndex != 0 &&
                        (identity == null || identity.IsShared(nodeId)) &&
                        ownership.OwnsNode(nodeId);
                }
                else
                {
                    if (sortedNamespaceUris == null)
                    {
                        throw new InvalidOperationException(
                            $"{source.GetType().FullName} must expose owned NamespaceUris or implement " +
                            $"{nameof(ILocalAddressSpaceOwnership)}.");
                    }
                    if (namespaceIndexes.Count == 0)
                    {
                        continue;
                    }
                    partitionId = string.Join("|", sortedNamespaceUris);
                    ownsNode = nodeId => namespaceIndexes.Contains(nodeId.NamespaceIndex);
                }
                if (!partitionIds.Add(partitionId))
                {
                    throw new InvalidOperationException(
                        $"Distributed address-space partition '{partitionId}' is registered more than once.");
                }
                registrations.Add(new AddressSpaceRegistration(addressSpace, ownsNode, partitionId));
            }
            return registrations;
        }

        internal static List<AddressSpaceRegistration> CreatePrepared(
            IServerContext server,
            ReplicaNodeIdFactory identity,
            IAsyncNodeManager next,
            IAsyncNodeManager? previous)
        {
            if (next is not ILocalAddressSpaceSource source)
            {
                return [];
            }
            var future = new List<ILocalAddressSpaceSource>();
            foreach (ILocalAddressSpaceSource current in server.FindNodeManagers<ILocalAddressSpaceSource>())
            {
                if (!ReferenceEquals(current, previous))
                {
                    future.Add(current);
                }
            }
            future.Add(source);
            _ = Create(server, identity, future);
            return Create(server, identity, [source]);
        }

        private static void ValidateExplicitOwnership(
            ISystemContext context,
            NodeState node,
            ILocalAddressSpaceOwnership ownership)
        {
            if (node.NodeId.NamespaceIndex == 0 && ownership.OwnsNode(node.NodeId))
            {
                throw new InvalidOperationException(
                    $"Distributed address-space partition '{ownership.PartitionId}' claims namespace-zero node " +
                    $"{node.NodeId}.");
            }
            var children = new List<BaseInstanceState>();
            node.GetChildren(context, children);
            foreach (BaseInstanceState child in children)
            {
                ValidateExplicitOwnership(context, child, ownership);
            }
        }
    }
}
