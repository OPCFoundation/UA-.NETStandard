/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to deal
 * in the Software without restriction, including without limitation the rights
 * to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in
 * all copies or substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS
 * IN THE SOFTWARE.
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Rejects an override switch unless every potentially affected browser owner participates.
    /// </summary>
    internal static class OverrideBrowsePublicationValidator
    {
        /// <summary>
        /// Checks both views before the durable decision or any live address-space publication.
        /// </summary>
        internal static void Validate(
            NodeManagerRoutingTable.RoutingSnapshot current,
            NodeManagerRoutingTable.RoutingSnapshot next)
        {
            var changed = new HashSet<NodeId>();
            foreach (KeyValuePair<NodeId, IAsyncNodeManager> entry in current.NodeOverrides)
            {
                if (!next.NodeOverrides.TryGetValue(entry.Key, out IAsyncNodeManager? owner) ||
                    !ReferenceEquals(current.GetGeneration(entry.Value), next.GetGeneration(owner)))
                {
                    changed.Add(entry.Key);
                }
            }
            foreach (NodeId nodeId in next.NodeOverrides.Keys)
            {
                if (!current.NodeOverrides.ContainsKey(nodeId))
                {
                    changed.Add(nodeId);
                }
            }
            var checkedOwners = new HashSet<IAsyncNodeManager>(NodeManagerReferenceComparer.Instance);
            if (changed.Count != 0)
            {
                ArrayOf<NodeId> targets = [.. changed];
                ValidateOwners(current.VisibleNodeManagers, targets);
                ValidateOwners(next.VisibleNodeManagers, targets);
                return;
            }
            if (next.NodeOverrides.Count == 0)
            {
                return;
            }

            ArrayOf<NodeId> existingTargets = [.. next.NodeOverrides.Keys];
            var currentOwners = new HashSet<IAsyncNodeManager>(NodeManagerReferenceComparer.Instance);
            foreach (IAsyncNodeManager manager in current.VisibleNodeManagers)
            {
                currentOwners.Add(NodeManagerGenerationRegistry.GetIdentity(manager));
            }
            foreach (IAsyncNodeManager manager in next.VisibleNodeManagers)
            {
                IAsyncNodeManager identity = NodeManagerGenerationRegistry.GetIdentity(manager);
                if (!currentOwners.Contains(identity) ||
                    !current.TryGetGeneration(manager, out NodeManagerGeneration? generation) ||
                    !ReferenceEquals(generation, next.GetGeneration(manager)))
                {
                    ValidateOwners([manager], existingTargets);
                }
            }

            void ValidateOwners(IEnumerable<IAsyncNodeManager> managers, ArrayOf<NodeId> targets)
            {
                foreach (IAsyncNodeManager manager in managers)
                {
                    IAsyncNodeManager identity = NodeManagerGenerationRegistry.GetIdentity(manager);
                    if (!checkedOwners.Add(identity))
                    {
                        continue;
                    }
                    INodeManagerOverrideBrowseCapability? capability =
                        identity as INodeManagerOverrideBrowseCapability ??
                        (identity is AsyncNodeManagerAdapter adapter
                            ? adapter.SyncNodeManager as INodeManagerOverrideBrowseCapability
                            : null);
                    if (capability is null || !capability.SupportsOverrideBrowse(targets))
                    {
                        throw new NotSupportedException(
                            $"Node manager '{identity.GetType().FullName}' does not guarantee " +
                            "snapshot-safe override browsing.");
                    }

                }
            }
        }

        /// <summary>
        /// Compares node manager identities by reference, matching routing-generation ownership.
        /// </summary>
        private sealed class NodeManagerReferenceComparer : IEqualityComparer<IAsyncNodeManager>
        {
            /// <summary>
            /// Gets the singleton comparer.
            /// </summary>
            public static NodeManagerReferenceComparer Instance { get; } = new();

            /// <summary>
            /// Determines whether two managers are the same instance.
            /// </summary>
            public bool Equals(IAsyncNodeManager? left, IAsyncNodeManager? right)
            {
                return ReferenceEquals(left, right);
            }

            /// <summary>
            /// Gets the reference-based hash code for a manager.
            /// </summary>
            public int GetHashCode(IAsyncNodeManager manager)
            {
                return RuntimeHelpers.GetHashCode(manager);
            }
        }
    }
}
