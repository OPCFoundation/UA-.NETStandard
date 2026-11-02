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
    public sealed class HistorianProviderRegistry :
        IHistorianProviderRegistry,
        IHistorianNodeBindingRegistry,
        IHistorianNodeReplacementRegistry,
        IDisposable
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
                RejectPendingReplacement(nodeId);
                m_nodes.TryGetValue(nodeId, out IHistorianProvider? previous);
                m_nodes[nodeId] = provider;
                m_nodeBindings.Remove(nodeId);
                AddProvider(provider, ownsProvider: true);
                if (previous != null)
                {
                    RebuildProviderSet(previous);
                }
            }
        }

        /// <inheritdoc/>
        public bool TryRegisterForNode(NodeId nodeId, IHistorianProvider provider, out IDisposable? binding)
        {
            return TryRegisterForNode(nodeId, provider, null, out binding);
        }

        /// <inheritdoc/>
        public bool TryRegisterForNode(NodeState node, IHistorianProvider provider, out IDisposable? binding)
        {
            if (node == null)
            {
                throw new ArgumentNullException(nameof(node));
            }
            return TryRegisterForNode(node.NodeId, provider, node, out binding);
        }

        /// <inheritdoc/>
        IHistorianNodeReplacement IHistorianNodeReplacementRegistry.BeginNodeReplacement(
            BaseVariableState node,
            IHistorianProvider provider)
        {
            return BeginNodeReplacement(node, provider);
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
                m_namespaces.TryGetValue(namespaceUri, out IHistorianProvider? previous);
                m_namespaces[namespaceUri] = provider;
                AddProvider(provider, ownsProvider: true);
                if (previous != null)
                {
                    RebuildProviderSet(previous);
                }
            }
        }

        /// <inheritdoc/>
        public void RegisterDefault(IHistorianProvider provider)
        {
            RegisterDefault(provider, ownsProvider: true);
        }

        /// <summary>
        /// Registers the fallback historian provider with an explicit lifetime ownership setting.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="provider"/> is <c>null</c>.</exception>
        internal void RegisterDefault(
            IHistorianProvider provider,
            bool ownsProvider)
        {
            if (provider == null)
            {
                throw new ArgumentNullException(nameof(provider));
            }

            lock (m_lock)
            {
                IHistorianProvider? previous = m_default;
                m_default = provider;
                AddProvider(provider, ownsProvider);
                if (previous != null)
                {
                    RebuildProviderSet(previous);
                }
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
                RejectPendingReplacement(nodeId);
                if (m_nodes.TryGetValue(nodeId, out IHistorianProvider? provider))
                {
                    m_nodes.Remove(nodeId);
                    m_nodeBindings.Remove(nodeId);
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
        public ArrayOf<IHistorianProvider> Providers
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
                providers = [.. m_ownedProviders];
                m_nodes.Clear();
                m_nodeBindings.Clear();
                m_pendingReplacements.Clear();
                m_namespaces.Clear();
                m_default = null;
                m_providers.Clear();
                m_ownedProviders.Clear();
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

        /// <summary>
        /// Reserves a replacement before fluent setup changes attributes, restoring the prior owner if setup fails.
        /// </summary>
        private NodeReplacement BeginNodeReplacement(BaseVariableState node, IHistorianProvider provider)
        {
            ArgumentNullException.ThrowIfNull(node);
            ArgumentNullException.ThrowIfNull(provider);
            if (node.NodeId.IsNull)
            {
                throw new ArgumentException("NodeId must not be null.", nameof(node));
            }
            lock (m_lock)
            {
                RejectPendingReplacement(node.NodeId);
                m_nodes.TryGetValue(node.NodeId, out IHistorianProvider? previousProvider);
                m_nodeBindings.TryGetValue(node.NodeId, out NodeBinding? previousBinding);
                var current = new NodeBinding(this, node.NodeId, node);
                m_pendingReplacements.Add(node.NodeId, current);
                return new NodeReplacement(this, node.NodeId, current, provider, previousProvider, previousBinding);
            }
        }

        /// <summary>
        /// Acquires a registration identity and, when supplied, its node attributes in one operation.
        /// </summary>
        private bool TryRegisterForNode(
            NodeId nodeId,
            IHistorianProvider provider,
            NodeState? node,
            out IDisposable? binding)
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
                if (m_nodes.ContainsKey(nodeId) || m_pendingReplacements.ContainsKey(nodeId))
                {
                    binding = null;
                    return false;
                }
                var registration = new NodeBinding(this, nodeId, node);
                m_nodes.Add(nodeId, provider);
                m_nodeBindings.Add(nodeId, registration);
                AddProvider(provider, ownsProvider: true);
                binding = registration;
                return true;
            }
        }

        /// <summary>
        /// Releases a scoped registration only while it is still the exact current registration.
        /// </summary>
        private void ReleaseNodeBinding(NodeId nodeId, NodeBinding binding)
        {
            lock (m_lock)
            {
                binding.IsDisposed = true;
                if (m_pendingReplacements.ContainsKey(nodeId))
                {
                    return;
                }
                if (m_nodeBindings.TryGetValue(nodeId, out NodeBinding? current) &&
                    ReferenceEquals(current, binding))
                {
                    binding.RestoreAttributes();
                    m_nodeBindings.Remove(nodeId);
                    if (m_nodes.TryGetValue(nodeId, out IHistorianProvider? provider))
                    {
                        m_nodes.Remove(nodeId);
                        RebuildProviderSet(provider);
                    }
                }
            }
        }

        /// <summary>
        /// Commits a prepared replacement or restores the previous owner unless a later registration superseded it.
        /// </summary>
        private void CompleteNodeReplacement(
            NodeId nodeId,
            NodeBinding current,
            IHistorianProvider provider,
            IHistorianProvider? previousProvider,
            NodeBinding? previousBinding,
            bool commit)
        {
            lock (m_lock)
            {
                if (!m_pendingReplacements.TryGetValue(nodeId, out NodeBinding? active) ||
                    !ReferenceEquals(active, current))
                {
                    return;
                }
                m_pendingReplacements.Remove(nodeId);
                if (commit)
                {
                    m_nodes[nodeId] = provider;
                    m_nodeBindings.Remove(nodeId);
                    AddProvider(provider, ownsProvider: true);
                    if (previousProvider != null)
                    {
                        RebuildProviderSet(previousProvider);
                    }
                    return;
                }
                if (previousBinding?.IsDisposed == true)
                {
                    previousBinding.RestoreAttributes();
                    m_nodes.Remove(nodeId);
                    m_nodeBindings.Remove(nodeId);
                    if (previousProvider != null)
                    {
                        RebuildProviderSet(previousProvider);
                    }
                }
                else
                {
                    current.RestoreAttributes();
                }
            }
        }

        /// <summary>
        /// Rejects competing mutations before they can alter an in-progress replacement.
        /// </summary>
        private void RejectPendingReplacement(NodeId nodeId)
        {
            if (m_pendingReplacements.ContainsKey(nodeId))
            {
                throw new InvalidOperationException("A historian replacement is already being configured for this node.");
            }
        }

        private void RebuildProviderSet(IHistorianProvider candidate)
        {
            if (!ContainsProvider(candidate))
            {
                m_providers.Remove(candidate);
            }
        }

        private void AddProvider(
            IHistorianProvider provider,
            bool ownsProvider)
        {
            m_providers.Add(provider);
            if (ownsProvider)
            {
                m_ownedProviders.Add(provider);
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

        /// <summary>
        /// The identity of one detachable exact-node registration.
        /// </summary>
        private sealed class NodeBinding : IDisposable
        {
            /// <summary>
            /// Creates an identity owned by the registry.
            /// </summary>
            public NodeBinding(HistorianProviderRegistry registry, NodeId nodeId, NodeState? node)
            {
                m_registry = registry;
                m_nodeId = nodeId;
                m_node = node;
                if (node is BaseVariableState variable)
                {
                    m_historizing = variable.Historizing;
                    m_accessLevel = variable.AccessLevel;
                    m_userAccessLevel = variable.UserAccessLevel;
                }
                if (node is BaseObjectState notifier)
                {
                    m_eventNotifier = notifier.EventNotifier;
                }
            }

            /// <summary>
            /// Whether the original owner released this registration, including while a replacement was being prepared.
            /// </summary>
            internal bool IsDisposed { get; set; }

            /// <inheritdoc/>
            public void Dispose()
            {
                m_registry.ReleaseNodeBinding(m_nodeId, this);
            }

            /// <summary>
            /// Restores the node attributes while the registry still owns this registration identity.
            /// </summary>
            public void RestoreAttributes()
            {
                if (m_node is BaseVariableState variable)
                {
                    variable.Historizing = m_historizing;
                    variable.AccessLevel = m_accessLevel;
                    variable.UserAccessLevel = m_userAccessLevel;
                }
                if (m_node is BaseObjectState notifier)
                {
                    notifier.EventNotifier = m_eventNotifier;
                }
            }

            /// <summary>The registry responsible for the atomic release.</summary>
            private readonly HistorianProviderRegistry m_registry;

            /// <summary>The exact node registration.</summary>
            private readonly NodeId m_nodeId;

            /// <summary>The optional node whose historian attributes belong to this registration.</summary>
            private readonly NodeState? m_node;

            /// <summary>The original variable historizing flag.</summary>
            private readonly bool m_historizing;

            /// <summary>The original variable access level.</summary>
            private readonly byte m_accessLevel;

            /// <summary>The original per-user variable access level.</summary>
            private readonly byte m_userAccessLevel;

            /// <summary>The original notifier event access.</summary>
            private readonly byte m_eventNotifier;
        }

        /// <summary>
        /// Restores an uncommitted fluent replacement without reviving an owner that disposed during setup.
        /// </summary>
        private sealed class NodeReplacement : IHistorianNodeReplacement
        {
            /// <summary>
            /// Records the reservation and the exact prior owner for rollback.
            /// </summary>
            public NodeReplacement(
                HistorianProviderRegistry registry,
                NodeId nodeId,
                NodeBinding current,
                IHistorianProvider provider,
                IHistorianProvider? previousProvider,
                NodeBinding? previousBinding)
            {
                m_registry = registry;
                m_nodeId = nodeId;
                m_current = current;
                m_provider = provider;
                m_previousProvider = previousProvider;
                m_previousBinding = previousBinding;
            }

            /// <inheritdoc/>
            public void Dispose()
            {
                Complete(commit: false);
            }

            /// <summary>
            /// Retains the mapping after fluent setup succeeds.
            /// </summary>
            public void Commit()
            {
                Complete(commit: true);
            }

            /// <summary>
            /// Completes this reservation once.
            /// </summary>
            private void Complete(bool commit)
            {
                if (Interlocked.Exchange(ref m_completed, 1) == 0)
                {
                    m_registry.CompleteNodeReplacement(
                        m_nodeId, m_current, m_provider, m_previousProvider, m_previousBinding, commit);
                }
            }

            /// <summary>The registry coordinating the reservation.</summary>
            private readonly HistorianProviderRegistry m_registry;

            /// <summary>The node whose provider is being replaced.</summary>
            private readonly NodeId m_nodeId;

            /// <summary>The reservation identity.</summary>
            private readonly NodeBinding m_current;

            /// <summary>The provider published only after setup commits.</summary>
            private readonly IHistorianProvider m_provider;

            /// <summary>The prior exact-node provider, if any.</summary>
            private readonly IHistorianProvider? m_previousProvider;

            /// <summary>The prior detachable owner, if any.</summary>
            private readonly NodeBinding? m_previousBinding;

            /// <summary>Whether this reservation has committed or rolled back.</summary>
            private int m_completed;
        }

        private readonly Lock m_lock = new();
        private readonly NamespaceTable m_namespaceTable;
        private readonly NodeIdDictionary<IHistorianProvider> m_nodes = [];

        /// <summary>Identities of detachable bindings, distinct from provider identity.</summary>
        private readonly NodeIdDictionary<NodeBinding> m_nodeBindings = [];

        /// <summary>Unpublished replacements that exclude competing mutations for the same node.</summary>
        private readonly NodeIdDictionary<NodeBinding> m_pendingReplacements = [];

        private readonly Dictionary<string, IHistorianProvider> m_namespaces = new(StringComparer.Ordinal);
        private readonly HashSet<IHistorianProvider> m_providers = [];
        private readonly HashSet<IHistorianProvider> m_ownedProviders = [];
        private IHistorianProvider? m_default;
    }
}
