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

namespace Opc.Ua.Server.Historian
{
    /// <summary>
    /// Default <see cref="IHistorianProviderRegistry"/> implementation —
    /// a thread-safe lookup keyed by NodeId, namespace URI and a single
    /// default fallback. Used by <c>ServerInternalData</c> via the additive
    /// <see cref="IHistorianRegistryProvider"/> interface.
    /// </summary>
    public sealed class HistorianProviderRegistry : IHistorianProviderRegistry, IDisposable
    {
        /// <summary>
        /// Creates an empty registry.
        /// </summary>
        public HistorianProviderRegistry(NamespaceTable namespaceTable)
        {
            m_namespaceTable = namespaceTable ?? throw new ArgumentNullException(nameof(namespaceTable));
        }

        /// <inheritdoc/>
        public void RegisterForNode(NodeId nodeId, IHistorianProvider provider)
        {
            if (nodeId.IsNull)
            {
                throw new ArgumentException("NodeId must not be null.", nameof(nodeId));
            }
            if (provider == null)
            {
                throw new ArgumentNullException(nameof(provider));
            }

            lock (m_lock)
            {
                m_nodes[nodeId] = provider;
                m_providers.Add(provider);
            }
        }

        /// <inheritdoc/>
        public void RegisterForNamespace(string namespaceUri, IHistorianProvider provider)
        {
            if (string.IsNullOrEmpty(namespaceUri))
            {
                throw new ArgumentException("Namespace URI must not be null or empty.", nameof(namespaceUri));
            }
            if (provider == null)
            {
                throw new ArgumentNullException(nameof(provider));
            }

            lock (m_lock)
            {
                m_namespaces[namespaceUri] = provider;
                m_providers.Add(provider);
            }
        }

        /// <inheritdoc/>
        public void RegisterDefault(IHistorianProvider provider)
        {
            if (provider == null)
            {
                throw new ArgumentNullException(nameof(provider));
            }

            lock (m_lock)
            {
                m_default = provider;
                m_providers.Add(provider);
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
                if (m_nodes.TryGetValue(nodeId, out IHistorianProvider? provider))
                {
                    m_nodes.Remove(nodeId);
                    RebuildProviderSet(provider);
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
                if (m_namespaces.TryGetValue(namespaceUri, out IHistorianProvider? provider))
                {
                    m_namespaces.Remove(namespaceUri);
                    RebuildProviderSet(provider);
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
                IHistorianProvider? prev = m_default;
                m_default = null;
                if (prev != null)
                {
                    RebuildProviderSet(prev);
                }
            }
        }

        /// <inheritdoc/>
        public IHistorianProvider? Resolve(NodeId nodeId)
        {
            if (nodeId.IsNull)
            {
                return null;
            }

            lock (m_lock)
            {
                if (m_nodes.TryGetValue(nodeId, out IHistorianProvider? byNode))
                {
                    return byNode;
                }

                if (m_namespaces.Count > 0)
                {
                    string? uri = m_namespaceTable.GetString(nodeId.NamespaceIndex);
                    if (uri != null && m_namespaces.TryGetValue(uri, out IHistorianProvider? byNamespace))
                    {
                        return byNamespace;
                    }
                }

                return m_default;
            }
        }

        /// <inheritdoc/>
        public IReadOnlyCollection<IHistorianProvider> Providers
        {
            get
            {
                lock (m_lock)
                {
                    return [.. m_providers];
                }
            }
        }

        /// <summary>
        /// Disposes any registered provider that implements
        /// <see cref="IDisposable"/>.
        /// </summary>
        public void Dispose()
        {
            HashSet<IHistorianProvider> providers;
            lock (m_lock)
            {
                providers = [.. m_providers];
                m_nodes.Clear();
                m_namespaces.Clear();
                m_default = null;
                m_providers.Clear();
            }

            foreach (IHistorianProvider provider in providers)
            {
                if (provider is IDisposable disposable)
                {
                    try
                    {
                        disposable.Dispose();
                    }
#pragma warning disable RCS1075 // intentional best-effort swallow during teardown (see comment)
                    catch (Exception)
                    {
                        // swallow during teardown — TODO(historian): log via shared telemetry once wired.
                    }
#pragma warning restore RCS1075
                }
            }
        }

        private void RebuildProviderSet(IHistorianProvider candidate)
        {
            if (!ContainsProvider(candidate))
            {
                m_providers.Remove(candidate);
            }
        }

        private bool ContainsProvider(IHistorianProvider candidate)
        {
            if (ReferenceEquals(candidate, m_default))
            {
                return true;
            }
            foreach (IHistorianProvider value in m_nodes.Values)
            {
                if (ReferenceEquals(value, candidate))
                {
                    return true;
                }
            }
            foreach (IHistorianProvider value in m_namespaces.Values)
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
        private readonly NodeIdDictionary<IHistorianProvider> m_nodes = [];
        private readonly Dictionary<string, IHistorianProvider> m_namespaces = new(StringComparer.Ordinal);
        private readonly HashSet<IHistorianProvider> m_providers = [];
        private IHistorianProvider? m_default;
    }
}
