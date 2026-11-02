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
    /// Identifies one registration contributed by a prepared NodeManager generation.
    /// </summary>
    internal readonly record struct HistorianProviderRegistration(
        HistorianProviderRegistrationKind Kind,
        NodeId NodeId,
        string? NamespaceUri,
        IHistorianProvider Provider);

    /// <summary>
    /// Identifies the scope of a staged historian provider registration.
    /// </summary>
    internal enum HistorianProviderRegistrationKind
    {
        /// <summary>A registration for one exact NodeId.</summary>
        Node,

        /// <summary>A registration for every NodeId in one namespace.</summary>
        Namespace,

        /// <summary>The fallback provider registration.</summary>
        Default
    }

    /// <summary>
    /// Supports atomic replacement and removal of one generation's historian registrations.
    /// </summary>
    internal interface IHistorianOwnedProviderRegistry
    {
        /// <summary>
        /// Resolves an exact NodeId binding without falling through to broader scopes.
        /// </summary>
        IHistorianProvider? ResolveExactNode(NodeId nodeId);

        /// <summary>
        /// Resolves a namespace binding without falling through to the default scope.
        /// </summary>
        IHistorianProvider? ResolveNamespace(string namespaceUri);

        /// <summary>
        /// Resolves the default binding.
        /// </summary>
        IHistorianProvider? ResolveDefault();

        /// <summary>
        /// Replaces all registrations currently contributed by <paramref name="owner"/>.
        /// </summary>
        void ReplaceOwnedRegistrations(object owner, HistorianProviderRegistration[] registrations);

        /// <summary>
        /// Removes registrations contributed by <paramref name="owner"/>.
        /// </summary>
        void RemoveOwnedRegistrations(object owner);
    }

    /// <summary>
    /// Default <see cref="IHistorianProviderRegistry"/> implementation —
    /// a thread-safe lookup keyed by NodeId, namespace URI and a single
    /// default fallback. Used by <c>ServerInternalData</c> via the additive
    /// <see cref="IHistorianRegistryProvider"/> interface.
    /// </summary>
    public sealed class HistorianProviderRegistry :
        IHistorianProviderRegistry,
        IHistorianOwnedProviderRegistry,
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
                m_nodes.TryGetValue(nodeId, out IHistorianProvider? previous);
                m_nodes[nodeId] = provider;
                m_nodeRegistrationOrder[nodeId] = ++m_registrationOrder;
                AddProvider(provider, ownsProvider: true);
                if (previous != null)
                {
                    RebuildProviderSet(previous);
                }
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
                m_namespaces.TryGetValue(namespaceUri, out IHistorianProvider? previous);
                m_namespaces[namespaceUri] = provider;
                m_namespaceRegistrationOrder[namespaceUri] = ++m_registrationOrder;
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
                m_defaultRegistrationOrder = ++m_registrationOrder;
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
                if (m_nodes.TryGetValue(nodeId, out IHistorianProvider? provider))
                {
                    m_nodes.Remove(nodeId);
                    m_nodeRegistrationOrder.Remove(nodeId);
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
                    m_namespaceRegistrationOrder.Remove(namespaceUri);
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
                m_defaultRegistrationOrder = 0;
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
                IHistorianProvider? nodeProvider = ResolveExactNodeCore(nodeId);
                if (nodeProvider != null)
                {
                    return nodeProvider;
                }

                string? uri = m_namespaceTable.GetString(nodeId.NamespaceIndex);
                if (uri != null)
                {
                    IHistorianProvider? namespaceProvider = ResolveNamespaceCore(uri);
                    if (namespaceProvider != null)
                    {
                        return namespaceProvider;
                    }
                }

                return ResolveDefaultCore();
            }
        }

        /// <inheritdoc/>
        IHistorianProvider? IHistorianOwnedProviderRegistry.ResolveExactNode(NodeId nodeId)
        {
            if (nodeId.IsNull)
            {
                return null;
            }
            lock (m_lock)
            {
                return ResolveExactNodeCore(nodeId);
            }
        }

        /// <inheritdoc/>
        IHistorianProvider? IHistorianOwnedProviderRegistry.ResolveNamespace(string namespaceUri)
        {
            if (string.IsNullOrEmpty(namespaceUri))
            {
                return null;
            }
            lock (m_lock)
            {
                return ResolveNamespaceCore(namespaceUri);
            }
        }

        /// <inheritdoc/>
        IHistorianProvider? IHistorianOwnedProviderRegistry.ResolveDefault()
        {
            lock (m_lock)
            {
                return ResolveDefaultCore();
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
        /// Replaces all active registrations contributed by one owner.
        /// </summary>
        /// <param name="owner">The stable candidate owner.</param>
        /// <param name="registrations">The complete registration snapshot for that owner.</param>
        void IHistorianOwnedProviderRegistry.ReplaceOwnedRegistrations(
            object owner,
            HistorianProviderRegistration[] registrations)
        {
            if (owner == null)
            {
                throw new ArgumentNullException(nameof(owner));
            }
            if (registrations == null)
            {
                throw new ArgumentNullException(nameof(registrations));
            }

            lock (m_lock)
            {
                RemoveOwnedRegistrationsCore(owner);
                if (registrations.Length == 0)
                {
                    return;
                }

                long order = ++m_registrationOrder;
                var owned = new OwnedRegistrationSet(registrations);
                m_ownedRegistrations.Add(owner, owned);
                foreach (HistorianProviderRegistration registration in registrations)
                {
                    switch (registration.Kind)
                    {
                        case HistorianProviderRegistrationKind.Node:
                            AddOwnedBinding(
                                m_ownedNodes,
                                registration.NodeId,
                                new OwnedProviderBinding(owner, registration.Provider, order));
                            break;
                        case HistorianProviderRegistrationKind.Namespace:
                            AddOwnedBinding(
                                m_ownedNamespaces,
                                registration.NamespaceUri!,
                                new OwnedProviderBinding(owner, registration.Provider, order));
                            break;
                        case HistorianProviderRegistrationKind.Default:
                            m_ownedDefaults.Add(new OwnedProviderBinding(owner, registration.Provider, order));
                            break;
                        default:
                            throw new ArgumentOutOfRangeException(nameof(registrations));
                    }
                    AddProvider(registration.Provider, ownsProvider: true);
                }
            }
        }

        /// <summary>
        /// Removes all active registrations contributed by one owner.
        /// </summary>
        /// <param name="owner">The stable candidate owner.</param>
        void IHistorianOwnedProviderRegistry.RemoveOwnedRegistrations(object owner)
        {
            if (owner == null)
            {
                throw new ArgumentNullException(nameof(owner));
            }
            lock (m_lock)
            {
                RemoveOwnedRegistrationsCore(owner);
            }
        }

        /// <summary>
        /// Captures the local registrations so a private candidate can publish them as one owned set.
        /// </summary>
        /// <returns>A snapshot of node, namespace, and default provider registrations.</returns>
        internal HistorianProviderRegistration[] CaptureRegistrations()
        {
            lock (m_lock)
            {
                var registrations = new List<HistorianProviderRegistration>(
                    m_nodes.Count + m_namespaces.Count + (m_default is null ? 0 : 1));
                foreach (KeyValuePair<NodeId, IHistorianProvider> entry in m_nodes)
                {
                    registrations.Add(new HistorianProviderRegistration(
                        HistorianProviderRegistrationKind.Node,
                        entry.Key,
                        null,
                        entry.Value));
                }
                foreach (KeyValuePair<string, IHistorianProvider> entry in m_namespaces)
                {
                    registrations.Add(new HistorianProviderRegistration(
                        HistorianProviderRegistrationKind.Namespace,
                        NodeId.Null,
                        entry.Key,
                        entry.Value));
                }
                if (m_default is not null)
                {
                    registrations.Add(new HistorianProviderRegistration(
                        HistorianProviderRegistrationKind.Default,
                        NodeId.Null,
                        null,
                        m_default));
                }
                return [.. registrations];
            }
        }

        /// <summary>
        /// Resolves an exact NodeId binding without falling through to broader scopes.
        /// </summary>
        internal IHistorianProvider? ResolveExactNode(NodeId nodeId)
        {
            if (nodeId.IsNull)
            {
                return null;
            }
            lock (m_lock)
            {
                return ResolveExactNodeCore(nodeId);
            }
        }

        /// <summary>
        /// Resolves a namespace binding without falling through to the default scope.
        /// </summary>
        internal IHistorianProvider? ResolveNamespace(string namespaceUri)
        {
            if (string.IsNullOrEmpty(namespaceUri))
            {
                return null;
            }
            lock (m_lock)
            {
                return ResolveNamespaceCore(namespaceUri);
            }
        }

        /// <summary>
        /// Resolves the local default binding.
        /// </summary>
        internal IHistorianProvider? ResolveDefault()
        {
            lock (m_lock)
            {
                return m_default;
            }
        }

        /// <summary>
        /// Transfers ownership of locally registered providers after publication to the serving registry.
        /// </summary>
        internal void TransferProviderOwnership()
        {
            lock (m_lock)
            {
                m_ownedProviders.Clear();
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
                m_namespaces.Clear();
                m_default = null;
                m_nodeRegistrationOrder.Clear();
                m_namespaceRegistrationOrder.Clear();
                m_defaultRegistrationOrder = 0;
                m_ownedNodes.Clear();
                m_ownedNamespaces.Clear();
                m_ownedDefaults.Clear();
                m_ownedRegistrations.Clear();
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
            foreach (OwnedRegistrationSet registrations in m_ownedRegistrations.Values)
            {
                foreach (HistorianProviderRegistration registration in registrations.Registrations)
                {
                    if (ReferenceEquals(registration.Provider, candidate))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        private bool TryGetOwnedNode(
            NodeId nodeId,
            out IHistorianProvider? provider,
            out long order)
        {
            return TryGetLatestOwnedBinding(m_ownedNodes, nodeId, out provider, out order);
        }

        private IHistorianProvider? ResolveExactNodeCore(NodeId nodeId)
        {
            if (TryGetOwnedNode(nodeId, out IHistorianProvider? ownedNode, out long ownedNodeOrder) &&
                (!m_nodes.TryGetValue(nodeId, out IHistorianProvider? byNode) ||
                    !m_nodeRegistrationOrder.TryGetValue(nodeId, out long nodeOrder) ||
                    ownedNodeOrder > nodeOrder))
            {
                return ownedNode;
            }
            return m_nodes.TryGetValue(nodeId, out IHistorianProvider? registeredNode)
                ? registeredNode
                : null;
        }

        private IHistorianProvider? ResolveNamespaceCore(string namespaceUri)
        {
            if (TryGetOwnedNamespace(
                namespaceUri,
                out IHistorianProvider? ownedNamespace,
                out long ownedNamespaceOrder) &&
                (!m_namespaces.TryGetValue(namespaceUri, out IHistorianProvider? byNamespace) ||
                    !m_namespaceRegistrationOrder.TryGetValue(namespaceUri, out long namespaceOrder) ||
                    ownedNamespaceOrder > namespaceOrder))
            {
                return ownedNamespace;
            }
            return m_namespaces.TryGetValue(namespaceUri, out IHistorianProvider? registeredNamespace)
                ? registeredNamespace
                : null;
        }

        private IHistorianProvider? ResolveDefaultCore()
        {
            return TryGetOwnedDefault(out IHistorianProvider? ownedDefault, out long ownedDefaultOrder) &&
                (m_default == null || ownedDefaultOrder > m_defaultRegistrationOrder)
                ? ownedDefault
                : m_default;
        }

        private bool TryGetOwnedNamespace(
            string namespaceUri,
            out IHistorianProvider? provider,
            out long order)
        {
            return TryGetLatestOwnedBinding(m_ownedNamespaces, namespaceUri, out provider, out order);
        }

        private bool TryGetOwnedDefault(out IHistorianProvider? provider, out long order)
        {
            provider = null;
            order = 0;
            foreach (OwnedProviderBinding binding in m_ownedDefaults)
            {
                if (binding.Order > order)
                {
                    provider = binding.Provider;
                    order = binding.Order;
                }
            }
            return provider != null;
        }

        private static bool TryGetLatestOwnedBinding<TKey>(
            IDictionary<TKey, List<OwnedProviderBinding>> bindings,
            TKey key,
            out IHistorianProvider? provider,
            out long order)
            where TKey : notnull
        {
            provider = null;
            order = 0;
            if (!bindings.TryGetValue(key, out List<OwnedProviderBinding>? candidates))
            {
                return false;
            }
            foreach (OwnedProviderBinding binding in candidates)
            {
                if (binding.Order > order)
                {
                    provider = binding.Provider;
                    order = binding.Order;
                }
            }
            return provider != null;
        }

        private static void AddOwnedBinding<TKey>(
            IDictionary<TKey, List<OwnedProviderBinding>> bindings,
            TKey key,
            OwnedProviderBinding binding)
            where TKey : notnull
        {
            if (!bindings.TryGetValue(key, out List<OwnedProviderBinding>? candidates))
            {
                bindings.Add(key, candidates = []);
            }
            candidates.Add(binding);
        }

        private void RemoveOwnedRegistrationsCore(object owner)
        {
            if (!m_ownedRegistrations.TryGetValue(owner, out OwnedRegistrationSet? registrations))
            {
                return;
            }
            m_ownedRegistrations.Remove(owner);

            foreach (HistorianProviderRegistration registration in registrations.Registrations)
            {
                switch (registration.Kind)
                {
                    case HistorianProviderRegistrationKind.Node:
                        RemoveOwnedBinding(m_ownedNodes, registration.NodeId, owner);
                        break;
                    case HistorianProviderRegistrationKind.Namespace:
                        RemoveOwnedBinding(m_ownedNamespaces, registration.NamespaceUri!, owner);
                        break;
                    case HistorianProviderRegistrationKind.Default:
                        m_ownedDefaults.RemoveAll(binding => ReferenceEquals(binding.Owner, owner));
                        break;
                }
                RebuildProviderSet(registration.Provider);
            }
        }

        private static void RemoveOwnedBinding<TKey>(
            IDictionary<TKey, List<OwnedProviderBinding>> bindings,
            TKey key,
            object owner)
            where TKey : notnull
        {
            if (!bindings.TryGetValue(key, out List<OwnedProviderBinding>? candidates))
            {
                return;
            }
            candidates.RemoveAll(binding => ReferenceEquals(binding.Owner, owner));
            if (candidates.Count == 0)
            {
                bindings.Remove(key);
            }
        }

        private readonly Lock m_lock = new();
        private readonly NamespaceTable m_namespaceTable;
        private readonly NodeIdDictionary<IHistorianProvider> m_nodes = [];
        private readonly Dictionary<string, IHistorianProvider> m_namespaces = new(StringComparer.Ordinal);
        private readonly NodeIdDictionary<long> m_nodeRegistrationOrder = [];
        private readonly Dictionary<string, long> m_namespaceRegistrationOrder = new(StringComparer.Ordinal);
        private readonly NodeIdDictionary<List<OwnedProviderBinding>> m_ownedNodes = [];
        private readonly Dictionary<string, List<OwnedProviderBinding>> m_ownedNamespaces =
            new(StringComparer.Ordinal);
        private readonly List<OwnedProviderBinding> m_ownedDefaults = [];
        private readonly Dictionary<object, OwnedRegistrationSet> m_ownedRegistrations =
            new(RefEqualityComparer.Default);
        private readonly HashSet<IHistorianProvider> m_providers = [];
        private readonly HashSet<IHistorianProvider> m_ownedProviders = [];
        private IHistorianProvider? m_default;
        private long m_defaultRegistrationOrder;
        private long m_registrationOrder;

        private sealed record OwnedRegistrationSet(HistorianProviderRegistration[] Registrations);

        private sealed record OwnedProviderBinding(
            object Owner,
            IHistorianProvider Provider,
            long Order);
    }
}
