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

namespace Opc.Ua.Server.Distributed
{
    /// <summary>
    /// Extension beyond OPC 10000-4 §6.6: default <see cref="INodeStateStoreRegistry"/> implementation — a
    /// thread-safe lookup keyed by NodeId, namespace URI and a single
    /// default fallback.
    /// </summary>
    public sealed class NodeStateStoreRegistry : INodeStateStoreRegistry, IDisposable
    {
        /// <summary>
        /// Creates an empty registry bound to a namespace table.
        /// </summary>
        /// <param name="namespaceTable">
        /// The namespace table used to resolve namespace-scoped bindings.
        /// </param>
        public NodeStateStoreRegistry(NamespaceTable namespaceTable)
        {
            m_namespaceTable = namespaceTable ?? throw new ArgumentNullException(nameof(namespaceTable));
        }

        /// <inheritdoc/>
        public IReadOnlyCollection<INodeStateStore> Stores
        {
            get
            {
                lock (m_lock)
                {
                    return [.. m_stores];
                }
            }
        }

        /// <inheritdoc/>
        public void RegisterForNode(NodeId nodeId, INodeStateStore store)
        {
            if (nodeId.IsNull)
            {
                throw new ArgumentException("NodeId must not be null.", nameof(nodeId));
            }
            if (store == null)
            {
                throw new ArgumentNullException(nameof(store));
            }

            lock (m_lock)
            {
                m_nodes[nodeId] = store;
                m_stores.Add(store);
            }
        }

        /// <inheritdoc/>
        public void RegisterForNamespace(string namespaceUri, INodeStateStore store)
        {
            if (string.IsNullOrEmpty(namespaceUri))
            {
                throw new ArgumentException("Namespace URI must not be null or empty.", nameof(namespaceUri));
            }
            if (store == null)
            {
                throw new ArgumentNullException(nameof(store));
            }

            lock (m_lock)
            {
                m_namespaces[namespaceUri] = store;
                m_stores.Add(store);
            }
        }

        /// <inheritdoc/>
        public void RegisterDefault(INodeStateStore store)
        {
            if (store == null)
            {
                throw new ArgumentNullException(nameof(store));
            }

            lock (m_lock)
            {
                m_default = store;
                m_stores.Add(store);
            }
        }

        /// <inheritdoc/>
        public bool UnregisterForNode(NodeId nodeId)
        {
            if (nodeId.IsNull)
            {
                return false;
            }

            lock (m_lock)
            {
                if (m_nodes.TryGetValue(nodeId, out INodeStateStore? store))
                {
                    m_nodes.Remove(nodeId);
                    RebuildStoreSet(store);
                    return true;
                }
                return false;
            }
        }

        /// <inheritdoc/>
        public bool UnregisterForNamespace(string namespaceUri)
        {
            if (string.IsNullOrEmpty(namespaceUri))
            {
                return false;
            }

            lock (m_lock)
            {
                if (m_namespaces.TryGetValue(namespaceUri, out INodeStateStore? store))
                {
                    m_namespaces.Remove(namespaceUri);
                    RebuildStoreSet(store);
                    return true;
                }
                return false;
            }
        }

        /// <inheritdoc/>
        public void ClearDefault()
        {
            lock (m_lock)
            {
                INodeStateStore? prev = m_default;
                m_default = null;
                if (prev != null)
                {
                    RebuildStoreSet(prev);
                }
            }
        }

        /// <inheritdoc/>
        public INodeStateStore? Resolve(NodeId nodeId)
        {
            if (nodeId.IsNull)
            {
                return null;
            }

            lock (m_lock)
            {
                if (m_nodes.TryGetValue(nodeId, out INodeStateStore? byNode))
                {
                    return byNode;
                }

                if (m_namespaces.Count > 0)
                {
                    string? uri = m_namespaceTable.GetString(nodeId.NamespaceIndex);
                    if (uri != null && m_namespaces.TryGetValue(uri, out INodeStateStore? byNamespace))
                    {
                        return byNamespace;
                    }
                }

                return m_default;
            }
        }

        /// <summary>
        /// Disposes any registered store that implements
        /// <see cref="IDisposable"/>.
        /// </summary>
        public void Dispose()
        {
            HashSet<INodeStateStore> stores;
            lock (m_lock)
            {
                stores = [.. m_stores];
                m_nodes.Clear();
                m_namespaces.Clear();
                m_default = null;
                m_stores.Clear();
            }

            foreach (INodeStateStore store in stores)
            {
                if (store is IDisposable disposable)
                {
                    disposable.Dispose();
                }
            }
        }

        private void RebuildStoreSet(INodeStateStore candidate)
        {
            if (!ContainsStore(candidate))
            {
                m_stores.Remove(candidate);
            }
        }

        private bool ContainsStore(INodeStateStore candidate)
        {
            if (ReferenceEquals(candidate, m_default))
            {
                return true;
            }
            foreach (INodeStateStore value in m_nodes.Values)
            {
                if (ReferenceEquals(value, candidate))
                {
                    return true;
                }
            }
            foreach (INodeStateStore value in m_namespaces.Values)
            {
                if (ReferenceEquals(value, candidate))
                {
                    return true;
                }
            }
            return false;
        }

        private readonly Lock m_lock = new();
        private readonly NamespaceTable m_namespaceTable;
        private readonly NodeIdDictionary<INodeStateStore> m_nodes = [];
        private readonly Dictionary<string, INodeStateStore> m_namespaces = new(StringComparer.Ordinal);
        private readonly HashSet<INodeStateStore> m_stores = [];
        private INodeStateStore? m_default;
    }
}
